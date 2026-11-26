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

## Fase 4: som e saves (concluída em 2026-11-25)

Critério: música, efeitos e save funcionando. **Atingido:** som bom e jogo liso jogando (confirmado por você), save gravado e carregado com "Continue".

- [x] Som sendo gerado (`VENDETTA_AUDIODUMP`): silêncio nas telas do cartão de memória, música a partir da tela de título (~-29 dB), som contínuo na fase com picos nos golpes (-4 a -6 dB). O SPU2 do runtime do Def Jam já tem reverb
- [x] Save completo por roteiro: nova partida → fase → pausa → "Return to Map" → "Saving data" → mapa; reaberto com o mesmo cartão, "Continue Game" mostra "0:00 Grand Canyon" e carrega o mapa
- [x] `OPMULA`/`OPMSUB` no caminho rápido do VU1 (o produto vetorial do descarte de faces, em todo laço de vértices)
- [x] Jogando, o som está bom (você confirmou). Os cortes vinham junto com pequenas travadas
- [x] Uma causa das travadas: o runtime imprimia `[MC] GetInfo`/`[MC] Sync` a cada consulta do jogo ao cartão de memória (146 KB numa sessão), e a thread do jogo ficava até ~90 ms presa na escrita (`ZwWriteFile` nas amostras do novo relatório `[stall]` do `VENDETTA_HOSTPROF`). Agora só com `VENDETTA_MCLOG`
- [x] Sessão jogando com `tools\play.ps1 -Log` (~2,5 min): nenhuma travada (`[stall]`), 285 quadros de áudio faltando no total (~6 ms), 60 quadros por segundo produzidos; liso jogando
- Nos testes automáticos a janela fica atrás das outras e o Windows segura a apresentação (90% do tempo da GPU em vsync), o que também trava a emulação: as travadas medidas ali não valem para o jogo em primeiro plano

### O que fica para depois

- A foto do local no mapa ("Grand Canyon") aparece como um quadro cinza: conferir se é assim no PS2 (não é um vídeo Bink: nenhum `Play` acontece no mapa até onde testamos)
- A janela às vezes descarta um quadro do jogo (`window-skips`, 3 a 14 por segundo): sincronia entre o ritmo do jogo e o do monitor (fase 6). Jogando, não se nota

## Fase 5: vídeos (concluída em 2026-11-26)

- [x] Os 69 vídeos do jogo são Bink 1 (`.bik` no GAME.WAD); o player do jogo (`CBinkPlayer`) usa a biblioteca RAD ligada no ELF, que lê o arquivo pelo módulo RAD no IOP
- [x] IOP: heap a partir de `0x80000` (era `0x120000`, pequeno demais: o `sceSifAllocIopHeap` da RAD falhava), `LoadModuleBuffer` lendo a RAM do IOP, e `argc/argv` de verdade no `start` dos módulos (o módulo RAD agora inicia)
- [x] Mesmo assim a RAD no IOP não andava (tela preta, o vídeo nunca terminava). Decisão: as funções da biblioteca Bink (`BinkOpen/Wait/DoFrame/CopyToBuffer/NextFrame/Goto/Pause/Close`) trocadas por `overrides/ben10_videos.cpp`, que decodifica com o FFmpeg (já embutido no runtime) lendo o `.bik` direto da ISO no setor que o jogo procurou; o resto continua sendo do jogo (desenho do quadro, fim, pular, volume)
- [x] Som do vídeo convertido para 48 kHz e mixado na saída do jogo; o `BinkWait` espera o VBlank pelo agendador (dormir na thread do jogo travava o IOP e o som saía na metade da velocidade)
- [x] O desligamento da RAD chamava `sceSifStopModule`, um RPC que o nosso IOP não atende: o jogo ficava preto depois da intro. Agora vai para o `SifStopModule` do runtime (e `sceSifUnloadModule` só retorna)
- [x] Logos de abertura (ESRB, Legal, CN, D3, HVSIntro) tocam inteiros com som e o jogo chega à tela de título
- [x] `11_intro` (início da primeira fase, 2245 quadros) toca inteiro (74,8 s de som) e o jogo entra na fase; apertar X durante o vídeo pula, como no PS2
- [x] Você assistiu com a janela na frente: imagem, som e sincronia bons (confirmado em 2026-11-26)

### Comandos

```powershell
powershell -ExecutionPolicy Bypass -File tools\play.ps1
```

Diagnóstico: cada vídeo escreve uma linha no fim (`[videos] ... closed at frame N of M (X s of sound mixed ...)`); `PS2X_VIDEOS=0` desliga (só para comparar: com a RAD original o vídeo fica preto).

### O que fica para depois

- Os outros vídeos (cenas entre fases) passam pelo mesmo caminho, mas só os de abertura e o `11_intro` foram vistos
- `BinkGoto` para trás (usado pelos comandos de script `avpSetFrame`/`avpHoldFrame`) reabre o arquivo e segue sem som; nenhum vídeo testado usou

## Fase 6: recursos de PC (concluída em 2026-11-26)

- [x] Widescreen (`overrides/ben10_widescreen.cpp`): a projeção da câmera (`FUN_00119408`) alargada pelo formato escolhido, a mesma mudança do patch de widescreen do PCSX2 para este ELF (CRC `8FCCB5D9`), aqui calculada para qualquer formato. Fases em 16:9 com mais cenário dos lados (o pause também); título, menus, dicas de carregamento, vídeos e o mapa em 4:3 (os menus são mapas `99_*`, reconhecidos pela mensagem "Opening" do próprio jogo). O HUD fica esticado, como no PCSX2
- [x] `settings.toml` do Ben 10 (o do Def Jam tinha lutadores, câmera de luta, velocidade da luta, atualizações): tela, formato, gráficos, som, vibração e teclado (ligado por padrão). Nitidez padrão 8x (16x pesa demais numa GTX 1650). `tools\play.ps1` não força mais nada: as opções vêm de `build\run\settings.toml` (apague para voltar ao padrão)
- [x] Sem acesso à rede: a checagem de atualização do runtime só roda com um repositório configurado, e o nosso não tem
- [x] Foto cinza no mapa: é o "cartão-postal" do local com os três espaços de cards Sumo Slammer, vazio numa fase recém-começada; o jogo não acusa arquivo faltando. Falta confirmar depois de completar uma fase
- [x] Você conferiu na janela da frente: widescreen bom na fase e no pause (confirmado em 2026-11-26)

### Fora desta fase

- 60 FPS de lógica: o patch do PCSX2 põe `1` em `0x268558` (o intervalo de VBlanks por quadro do jogo); o próprio PCSX2 deixa o patch de 60 FPS desligado porque quebra o pulo
- A janela às vezes descarta um quadro (`window-skips`); jogando você não notou

## Fase 7: launcher e pacote público (concluída em 2026-11-26)

Modelo: o pacote público não leva nada do jogo. O launcher confere a ISO do jogador e monta o jogo no PC dele (como o
Def Jam e o PL:A Recomp).

- [x] Launcher (`launcher/`, .NET 10 + Avalonia, adaptado do programa de configurações do Def Jam, GPL-3.0) com o visual
  do PL:A Recomp: tela inicial com arte nossa (vetorial, nada do jogo ou do desenho), JOGAR, OPÇÕES, CONTROLES, ARQUIVOS
  DO JOGO, SAIR; status, hardware (placa, CPU, RAM), versão e aviso de atualização
- [x] Opções (Tela, Gráficos, Som, Launcher), salvas no `settings.toml` a cada mudança; Controles com o mapa real do
  teclado (`ps2_pad.cpp`), teclado e vibração; Arquivos do jogo (disco, jogo montado, pastas, montar de novo, outro disco)
- [x] Português e inglês (`L.T`, `{l:T ...}`), automático pelo idioma do Windows ou escolhido em Opções › Launcher
  (`launcher.toml`)
- [x] Ícone nosso (`tools/make_icon.py`, sem bibliotecas), no launcher e na janela do jogo
- [x] `tools/prepare_disc.py`: confere a ISO (hash do `SLUS_216.61` v1.01), copia para `disc\SLUS-21661.iso` e extrai o
  executável; recusa outra versão ou arquivo errado
- [x] `tools/setup_game.py` (do Def Jam): disco, ferramentas de build da Microsoft (instaladas se faltarem), recompilador,
  código do jogo em C++, o jogo, shaders, fim; erros em palavras simples
- [x] O jogo acha a ISO sozinho (`disc\SLUS-21661.iso` ao lado de `game\`) quando aberto pelo atalho
- [x] `tools/make_release.py`: `dist\Ben10-Recomp-Windows.zip` com o launcher, Python portátil e o código-fonte (PS2Recomp
  com os patches aplicados, paraLLEl-GS, bibliotecas); recusa qualquer nome de arquivo do jogo e código gerado
- [x] Atualizações pelas releases do GitHub (`guilhermeperecim-jpg/ben10-protector-of-earth-pc`, quando for público)
- [x] Instalação de teste do zero (`C:\b10test`, `tools/setup_game.py`): ~25 min de build + ~20 min de shaders. Aberto sem nenhuma variável de ambiente, como pelo atalho: acha a ISO, o `settings.toml` e o cartão sozinho; logos, vídeos, título, menu e a fase em widescreen
- [x] Achado no teste: o runtime abre os `cdrom0:` (módulos IRX, `IOPRP300.IMG`, `SYSTEM.CNF`, `GAME.DIR`) pela pasta `game\`; o `prepare_disc.py` agora extrai todos os arquivos do disco menos o `GAME.WAD` (2,4 GB, lido por setor da ISO pelo CDARB)
- [x] Achado no zip: o `git archive` do paraLLEl-GS deixava de fora os 5 arquivos que o patch do Def Jam cria (o build do jogador parava em `sample_circuit_scaled.hpp`); o `make_release.py` agora parte do commit original `cc6184a` e aplica o patch, como o Def Jam
- [x] Zip conferido: nenhum trecho do executável do jogo, nenhum ELF/ISO do jogo, nenhum código gerado
- [x] Zip gerado (`dist\Ben10-Recomp-Windows.zip`, 86 MB) e instalado do zero numa pasta limpa (`C:\b10zip`) só com o que vem nele (Python e bibliotecas do pacote): 7 etapas OK; o jogo acha a ISO sozinho, os 5 vídeos de abertura tocam inteiros com som, e vai do título até a fase em widescreen
- [x] Você testou o launcher (confirmado em 2026-11-26)
- [x] Busca automática da ISO na primeira abertura (`launcher/Services/DiscFinder.cs`): pastas do usuário e discos,
  com limite de pastas e de tempo, nomes parecidos com o do jogo primeiro, cada candidata conferida pelo hash (0,8 s
  neste PC); "Procurar no PC" refaz a busca; `--find-disc` mostra o que ela acha
- [x] `THIRD-PARTY-NOTICES.txt` (as partes de terceiros e as licenças, conferidas nos próprios arquivos)
- [x] README para jogadores: link de download, 4 passos, telas do launcher (`docs/images/launcher-*.png`, desenhadas
  pelo `--shot` com pastas de exemplo e sem o hardware deste PC), requisitos, controles e créditos
- [x] Histórico do git conferido inteiro antes de publicar: nada do jogo
