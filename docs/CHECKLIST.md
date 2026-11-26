# Checklist por fase

## Fase 0: build (concluída em 2026-11-25)

Critério: "a janela abre e o log mostra o primeiro erro". **Atingido.**

- [x] Projeto em `C:\dev\ben10-port` (o caminho da pasta temporária passava do limite de 260 caracteres do `cl.exe`)
- [x] Ferramentas compiladas: `ps2_recomp.exe`, `ps2_analyzer.exe`
- [x] `ps2_analyzer` no `SLUS_216.61`: 124 funções do SDK com handler + 190 sem (`config/analyzer.toml`)
- [x] Nomes do SDK no Ghidra (`tools/sdk_symbols.py`, 322 nomes); export: 4456 funções
- [x] Revisão dos stubs: 18 exclusões em `config/manual_stubs.txt` (libm/rand double por software; `libgraph` com estado dividido)
- [x] Recompilação sem erros, executável compilado, primeira execução até o primeiro erro

## Fase 1: boot até o menu (concluída em 2026-11-25)

Critério: logo e menu desenhados. **Atingido:** tela do memory card → título → menu → "Difficulty" (`docs/fase1-menu.png`).

- [x] **Base do runtime trocada** para o PS2Recomp `2c5fbb9` com o patch do Def Jam (`patches/README.md`): ele já corrigia a pilha do `SetupThread` e tirou as estruturas do runtime de cima da pilha principal
- [x] 345 funções alcançadas só por ponteiro adicionadas ao mapa (`tools/find_missing_starts.py`), com filtro para blocos de dados no `.text`
- [x] Syscall `0x80` (`GetGsDxDyOffset`): offsets zero
- [x] **CD:** com `PS2X_CD_IMAGE` (o override do jogo põe no `cdImage`), o `sceCdSearchFile` devolve o setor real do ISO9660, o mesmo que o CDARB lê pelo IOP. Antes, o CDARB repetia a leitura para sempre e o jogo rodava a 2% da velocidade
- [x] `sceCdLayerSearchFile` (`0x1C3620`) ligado ao `sceCdSearchFile` (o runtime do Def Jam não tem o nome antigo)
- [x] **Heap até a base da pilha principal** (`0x1FF0000`), como o `EndOfHeap` do kernel. Com o teto antigo (`0x1F00000`) o `malloc` do jogo falhava, uma tabela de tratadores ficava nula e o leitor RIFF chamava um método abstrato (`__terminate`, tela preta)
- [x] Velocidade real (60 vsync/s), memory card funcionando: o jogo criou o save em `game/mc0/BASLUS-21661/`
- [x] Diagnóstico: `PS2X_TICK_LOG` (onde a thread do jogo está), `tools/run.ps1` (print da janela; use `VENDETTA_WINDOW=window`), `VENDETTA_PADSCRIPT` (botões por script) e a armadilha no `__pure_virtual` em `overrides/ben10_overrides.cpp`

### Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1                      # ps2_recomp + ps2_analyzer
powershell -ExecutionPolicy Bypass -File tools\ghidra_analyze.ps1             # Ghidra + export
python -I tools\find_missing_starts.py game\SLUS_216.61 config\ben10_functions.ghidra.csv --write config\missing_starts.csv
python -I tools\make_config.py
build\ps2recomp-dj\ps2xRecomp\ps2_recomp.exe config\ben10.toml
python -I tools\vu1_recomp.py                                                  # VU1 compilado -> generated\vu1_compiled.cpp
python -I tools\sync_generated.py
powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Targets ps2EntryRunner
$env:VENDETTA_WINDOW='window'; powershell -ExecutionPolicy Bypass -File tools\run.ps1 -Seconds 60
```

## Fase 2: fase jogável (concluída em 2026-11-25)

Critério: Ben andando e lutando. **Atingido:** fase "Grand Canyon National Park", golpes ("Double Jab", "Light"), combo, inimigos (Vilgax Drone) (`docs/fase2-gameplay.png`, sequência em `docs/fase2-sequencia.png`).

- [x] Fluxo completo do jogo: memory card → Press Start → New Game → Difficulty → slot de save → dica → fase → pausa → "Return to Map" → save → mapa-múndi → fase de novo → tutorial do Omnitrix → combate
- [x] **Congelamento ao entrar na fase corrigido:** um caso do `switch` do interpretador de scripts (`FUN_00185ba0`, tabela de 157 casos em `0x23DAC0`) não tinha ponto de entrada (`0x185CDC`). `tools/find_jump_targets.py` acha todos os destinos de tabelas de salto sem entrada (5 no jogo) e o `make_config.py` os junta ao mapa
- [x] Save funcionando dentro do jogo (auto-save ao voltar para o mapa)
- [x] Nenhuma chamada abstrata, alvo faltando ou syscall sem handler em 4 minutos de jogo
- [x] `tools/run.ps1`: prints periódicos pela própria janela (`PrintWindow`, não captura outras janelas), escala de DPI correta, `-PadScript`; `tools/sheet.ps1` monta a grade de prints
- [x] `tools/play.ps1`: para jogar com teclado ou controle

### Controles (runtime do Def Jam)

| PS2 | Teclado | Gamepad (XInput) |
|---|---|---|
| Analógico esquerdo | W A S D | analógico esquerdo |
| Direcional | setas | direcional |
| ✕ / ○ / □ / △ | X / C / Z / V | A / B / X / Y |
| L1 / R1 / L2 / R2 | Q / E / 1 / 3 | LB / RB / LT / RT |
| START / SELECT | Enter / Shift direito | Start / Back |

## Fase 3: imagem (concluída em 2026-11-25)

Critério: 60 FPS na fase, sem defeitos. **Atingido** na primeira fase (Grand Canyon), GPU 8×: imagem correta e 60 quadros por segundo produzidos (`build\cmp\vu1c\shot_120.png`).

- [x] paraLLEl-GS (Vulkan) do Def Jam compilado no runtime (`third_party/parallel-gs`, `patches/defjam-parallel-gs-local.diff`); escolhe a GTX 1650 sozinho
- [x] Renderização na GPU em 8× (`VENDETTA_GS=gpu`, `VENDETTA_SSAA=8`; o `tools/play.ps1` já usa). Primeira execução numa escala nova: 10.902 shaders em ~4 min, depois em cache (0,5 s)
- [x] O Rustbucket, que saía deformado no renderizador por CPU, sai certo na GPU
- [x] **Formas pretas na fase corrigidas** (o que parecia "buraco" no chão e no céu). Não era o VU1 nem o recorte: era a malha do Ben (com skinning) com 62% dos vértices na origem e o resto esticado. A causa são **dois bugs do recompilador** (PS2Recomp), corrigidos em `ps2xRecomp`:
  - `VSQI/VSQD/VLQI/VLQD/VISWR/VILWR` (macro do VU0): acessavam a RAM principal em `vi*16` em vez da memória de dados do VU0 (`0x11004000`), e `VSQI/VSQD` trocavam os campos (`vi` pelo número do `vf`, sempre gravando `vf1`). O jogo grava as matrizes dos ossos com `VSQI` (`0x15F358`) e faz o skinning num microprograma do VU0: com a memória do VU0 zerada, posição e normal saíam zero
  - permutações MMI erradas: `PROT3W` girava as 4 palavras (o X de um vértice sim, outro não, recebia o W, que é zero), e `PEXEW`, `PEXCW`, `PEXEH`, `PEXCH`, `PREVH`, `PINTH`, `PINTEH` também não batiam com a ISA. Conferidas contra a definição escalar (`build\mmicheck\mmi_check.cpp`)
- [x] Ferramentas de diagnóstico novas (só ligam por variável): `PS2X_DRAWLOG=<de>,<até>` (cada primitiva com estado do GS, caminho GIF e microprograma do VU1, em `build\run\drawlog.txt`), `PS2X_VU1DUMP=<s>,<pc>,<n>` (memória e registradores do VU1 na entrada de um programa), `PS2X_VU0DUMP=<pc>`, `PS2X_FMTLOG` (o caminho número → texto do script), `PS2X_MC_ROOT` (outro cartão de memória; o `tools\run.ps1` com `-PadScript` usa um cartão de teste vazio)
- [x] Imagem da primeira fase conferida na GPU 8×: Ben iluminado e inteiro, trailer com antenas e prato, chão, cânion e céu sem formas pretas nem borrões (`build\cmp\vu1c\shot_120.png`)
- [x] **Desempenho: de ~10 para 60 FPS** (`VENDETTA_HOSTPROF=1`, adaptado do Def Jam no override). O gargalo era o VU1 no interpretador: o runtime desliga o caminho rápido e o código compilado sempre que o microcódigo lê a flag MAC, e o Ben 10 lê (`FMAND` no descarte de faces):
  - `tools\vu1_recomp.py` (do Def Jam) gera o VU1 compilado a partir do `.vutext` do jogo (8 programas, 6.879 de 6.881 pares)
  - só as instruções cuja flag MAC pode ser lida (≈145) calculam a flag (resultado rápido + Z/S); as demais seguem rápidas, no código compilado e no interpretador (`markMacProducers`, também para o skinning no VU0)
- [x] **60 quadros por segundo produzidos na fase** (`gs-presents/s=59–60`, com folga: a thread do jogo espera o relógio até 30% do tempo). A taxa que chega à janela não dá para medir nos testes automáticos (a janela fica atrás das outras e o Windows segura a apresentação): conferir jogando com `tools\play.ps1`
- [x] Volume nas opções de áudio ("96.501005" → inteiros: 41, 80, 100). O `floor` do jogo é `double`, mas o recompilador o trocava pelo stub `float` do runtime por nome; as 18 exclusões do `manual_stubs.txt` nunca tinham valido. Agora `-nome@endereço` vale (recompilador + `tools\make_config.py`): `floor`, `ceil`, `sqrt`, `fabs`, `log`, `tan` e `rand` rodam como código do jogo; a `libgraph` (`sceGs*`) continua no runtime, como sempre rodou
- [x] Save: um save criado num cartão de teste é lido de volta sem erro. O "corrompido" no `game\mc0` vem de um teste antigo que fechou o jogo no meio do auto-save (falta o `icon.sys`); basta aceitar sobrescrever. `tools\run.ps1 -PadScript` agora usa um cartão de teste (`build\run\mc-test`, `-KeepCard` para manter)

### O que fica para depois

- Renderizador por CPU (`VENDETTA_GS=cpu`): não reconferido depois das correções (o trailer deformado era o mesmo bug de skinning); a GPU é o padrão
- Conferir no PC, jogando, se a janela mostra os 60 quadros (e o vsync com monitores acima de 60 Hz)
- Teclado e controle de verdade: o mapeamento está no código, mas só testei com botões por script
- Paridade de campo do `sceGsSyncV` (não vi piscar até aqui)
- Som (fase 4): não verificado

## Fase 4: som e saves (em andamento)

Critério: música, efeitos e save funcionando.

- [x] Som sendo gerado (`VENDETTA_AUDIODUMP`): silêncio nas telas do cartão de memória, música a partir da tela de título (~-29 dB), som contínuo na fase com picos nos golpes (-4 a -6 dB). O SPU2 do runtime do Def Jam já tem reverb
- [x] Save completo por roteiro: nova partida → fase → pausa → "Return to Map" → "Saving data" → mapa; reaberto com o mesmo cartão, "Continue Game" mostra "0:00 Grand Canyon" e carrega o mapa
- [x] `OPMULA`/`OPMSUB` no caminho rápido do VU1 (o produto vetorial do descarte de faces, em todo laço de vértices)
- [x] Jogando, o som está bom (você confirmou). Os cortes vinham junto com pequenas travadas
- [x] Uma causa das travadas: o runtime imprimia `[MC] GetInfo`/`[MC] Sync` a cada consulta do jogo ao cartão de memória (146 KB numa sessão), e a thread do jogo ficava até ~90 ms presa na escrita (`ZwWriteFile` nas amostras do novo relatório `[stall]` do `VENDETTA_HOSTPROF`). Agora só com `VENDETTA_MCLOG`
- [ ] Conferir jogando (`tools\play.ps1 -Log`) se ainda há travadas; o `build\run\play.log` traz os `[stall]` com o que cada thread fazia
- Nos testes automáticos a janela fica atrás das outras e o Windows segura a apresentação (90% do tempo da GPU em vsync), o que também trava a emulação: as travadas medidas ali não valem para o jogo em primeiro plano
- [ ] A foto do local no mapa ("Grand Canyon") aparece como um quadro cinza: conferir se é assim no PS2 (talvez seja um vídeo, fase 5)
