# Port de PS2 para PC: dump e o que reaproveitar

Repositórios clonados em `refs/` (clone raso, sem submódulos; nenhum script deles foi executado):

| Pasta | Jogo | Abordagem | Licença |
|---|---|---|---|
| `refs/PS2Recomp` | (motor) | Recompilador estático + runtime | GPL-3.0 |
| `refs/def-jam-vendetta-recomp` | Def Jam Vendetta (SLUS-20639) | PS2Recomp + overrides + paraLLEl-GS (Vulkan) | GPL-3.0 |
| `refs/War_of_the_Monsters_recomp` | War of the Monsters (SCUS-97197) | PS2Recomp + camada nativa (raylib) que substitui GS/VU1 | GPL-3.0 |
| `refs/ps2_recomp` | (base genérica do HayatoG) | Ferramentas + launcher para qualquer jogo | GPL-3.0 |
| `refs/ghost_pc` | Ghost in the Shell SAC (SLES-53020) | Usa a base acima. Ainda não desenha (tela magenta) | GPL-3.0 |
| `refs/Tenkaichi3Decomp` | DBZ Budokai Tenkaichi 3 (SLUS-21678) | **Decompilação** à mão em C + camada de port | AGPL-3.0 (código antigo MIT) |

---

## 1. Fazer o dump

O OPL não lê o disco direto: ele roda uma ISO que já existe em algum lugar. Então o "dump" quase sempre é só copiar essa ISO.

| Onde o OPL carrega o jogo | Como levar para o PC |
|---|---|
| **USB** (pendrive/HD, pasta `DVD/` ou `CD/`) | Ligue no PC e copie o `.iso` da pasta `DVD/`. Se o jogo estiver em partes `ul.*` (formato USBExtreme antigo), junte as partes com o **USBUtil**. |
| **HDD interno** (formato APA/PFS, partições `PP.`/`__.`) | O Windows não lê esse formato. Ligue o HD no PC (adaptador SATA/IDE-USB) e extraia com o **HDL Batch Installer** ou o `hdl_dump` (`hdl_dump extract`). |
| **SMB** (rede) | A ISO já está na pasta compartilhada do PC. |
| **MX4SIO / iLink** | Leia o cartão SD ou o HD direto no PC e copie a ISO. |

Se você tiver o **disco original**, prefira refazer o dump a partir dele, num drive de DVD do PC, com o ImgBurn (modo *Read*, gera `.iso`). Os discos de PS2 não têm proteção que atrapalhe isso.

**Confira se o dump está íntegro.** Compare o CRC32/SHA-1 da ISO com a entrada do jogo no **redump.org**:

```powershell
Get-FileHash .\jogo.iso -Algorithm SHA1
```

Se bater, o dump é idêntico ao disco de fábrica, que é o que interessa para preservação. Se não bater, ainda pode servir para o port, mas os hashes dos projetos acima (que conferem o ELF de boot) podem recusar a ISO.

Guarde a ISO **fora do repositório** do port (todos os projetos acima fazem isso: nada do jogo vai para o git).

---

## 2. As duas abordagens que aparecem nos projetos

**A. Recompilação estática com o PS2Recomp** (Def Jam, War of the Monsters, ghost_pc). A máquina traduz o executável inteiro para C++ e um runtime finge ser o hardware do PS2. Isso dá o primeiro boot em dias ou semanas, mas o código gerado é ilegível.

**B. Decompilação "matching"** (Tenkaichi 3, e a parte `src/` do War of the Monsters). Cada função é reescrita à mão em C até compilar para os mesmos bytes do original. Dá código legível, mas leva **anos** de trabalho.

**Recomendação: começar pela A.** É a base de 3 dos 5 projetos. A B só vale para as funções que você quiser modificar (é o que o War of the Monsters faz).

---

## 3. O que dá para reaproveitar

### Direto, servem para qualquer jogo

| O quê | De onde | Para quê |
|---|---|---|
| **PS2Recomp inteiro** (`ps2xRecomp`, `ps2xRuntime`, `ps2xIOP`, `ps2xAnalyzer`) | `refs/PS2Recomp` | É o motor. Já implementa ~677 funções da Sony (`sceSif*`, `scePad*`, `sceCd*`, `sceMc*`, `sceGs*`, `sceDma*`, `sceVu0*`, `sceMpeg*`, `sceSd*`...) em `ps2xRuntime/include/ps2_call_list.h`. Também roda os módulos `.IRX` originais do IOP. São exatamente as "funções que o PS2 usa para rodar o jogo". |
| Script do Ghidra `ExportPS2Functions.java` | `refs/PS2Recomp/ps2xRecomp/tools/ghidra/` | Exporta o mapa de funções (TOML + CSV) que o recompilador usa. É o fluxo recomendado para jogos sem símbolos. |
| Scripts Ghidra `CreateFunctionsAt`, `CreateFunctionsFromPointers`, `CreateMissingFunctions`, `ApplySymbolsCsv` | `refs/ps2_recomp/tools/ghidra/` | Acham funções que o Ghidra não viu (chamadas por ponteiro). É a causa nº 1 de travar no boot. |
| `find_missing_starts.py` | `refs/def-jam-vendetta-recomp/tools/` | Resolve o mesmo problema, varrendo tabelas de ponteiros no ELF. São 55 linhas, sem nada de Def Jam. |
| `extract_iso.py` | `refs/ps2_recomp/tools/` | Extrai o ELF, `SYSTEM.CNF` e `MODULES/*.IRX` da ISO. |
| `tools/disc/chd.py`, `chdreader.py` | `refs/def-jam-vendetta-recomp/` | Lê `.chd`, caso você guarde o dump comprimido. |
| `ps2launcher.py` + `game.toml` | `refs/ps2_recomp/launcher`, `refs/ghost_pc/game.toml` | Confere serial e hash do dump, extrai o que precisa e roda. Basta trocar o `game.toml` para o seu jogo. |
| `sync_generated.py` / `sync_recomp_output.py` | Def Jam / ps2_recomp | Copia só o C++ gerado que mudou, para o build não recompilar ~25 mil arquivos. |
| `irxdis.py` | Def Jam | Desmonta módulos IOP para depurar som e memory card. |
| Pad virtual por script, medidor de FPS, heartbeat, `PS2X_MISSING_LOG` | `refs/ghost_pc/patches/` | Ferramentas de depuração do runtime. O `PS2X_MISSING_LOG` lista cada função que faltou gerar e alimenta o `CreateFunctionsAt.java`. |
| Pilha de 256 MB no executável | `refs/ghost_pc/patches` (CMake) | Sem isso, o código gerado estoura a pilha (`0xC00000FD`). Vale para todo jogo. |
| `PS2X_VIDEO_HZ` (vblank 50/60 Hz) | ghost_pc | Necessário se o seu jogo for europeu (PAL). |

### Com adaptação

| O quê | De onde | Observação |
|---|---|---|
| `vu1_recomp.py` (VU1 → C++) | Def Jam | Genérico na ideia, mas lê o `.vutext` de um ELF específico e mexe em internals do runtime. Só vale se o VU1 virar gargalo. |
| Backend GS por GPU: paraLLEl-GS + `parallel-gs-local.diff` | Def Jam | Desenha o GS na GPU (Vulkan) com upscale. É o caminho mais rápido para ter imagem em HD sem reescrever o renderizador. |
| GS em thread/faixas, backend D3D11 | ghost_pc | Alternativa ao anterior, mas ainda incompleto. |
| `ps2recomp-local.diff` (23 mil linhas) | Def Jam | Melhorias no runtime e no IOP, misturadas com coisas do Def Jam. Precisa ser garimpado arquivo a arquivo. |
| `prepare_disc.py`, `setup_game.py` | Def Jam | Bom modelo de "instalador para o usuário final". Troque o serial e os caminhos. |
| Camada nativa (`native/`) | War of the Monsters | Mostra como **desligar** o GS/VU1 emulados (`PS2X_NO_GS`, `PS2X_NO_VU1`) e desenhar lendo a cena da RAM. É o caminho do port "de verdade", mas é 100% específico do motor do jogo. |
| Camada `port/` (Vulkan/OpenGL, `plat_mc.c`, `plat_file.c`, áudio ADX, filmes MPEG2) | Tenkaichi 3 | Só serve se você fizer decompilação. É **AGPL**: se copiar, o projeto inteiro vira AGPL. |

### Não reaproveitável

- `overrides/` do Def Jam (37 mil linhas), `ghost_overrides.cpp`, `native/wotm_*`, `src/` do Tenkaichi: são presos ao executável de cada jogo (endereços e estruturas próprios).
- Mapas de símbolos, CSVs do Ghidra, `config/*.toml` de cada projeto: os endereços valem só para aquele ELF.

---

## 4. Licença: o que muda para você

- Usar o PS2Recomp torna o port **GPL-3.0**. Copiar algo do `port/` do Tenkaichi 3 o torna **AGPL-3.0**.
- O C++ gerado é tradução do código do jogo: **nunca publique**. Todos os projetos geram esse código na máquina de cada usuário, a partir do disco dele.
- Para uso pessoal e preservação, nada disso impede nada.

---

## 5. Esqueleto proposto (quando soubermos o jogo)

```
meu-port/
  game.toml          serial, sha256 do ELF, arquivos a extrair   (modelo: ghost_pc)
  config/            TOML/CSV exportados do Ghidra + stubs manuais
  overrides/         correções por endereço deste jogo            (PS2_REGISTER_GAME_OVERRIDE)
  patches/           diffs sobre o PS2Recomp
  tools/             extract_iso, find_missing_starts, sync_generated, scripts Ghidra
  third_party/PS2Recomp   (submódulo)
  disc/ game/ generated/  (fora do git)
```

Ciclo de trabalho (o mesmo do README do PS2Recomp):
1. Extrair o ELF, abrir no Ghidra (extensão `ghidra-emotionengine-reloaded`) e exportar.
2. Recompilar, compilar o runtime e rodar.
3. Corrigir o que trava: função faltando (`PS2X_MISSING_LOG`), syscall sem implementação, I/O de disco.
4. Mover cada gambiarra para `overrides/`, nunca para o runtime global.
5. Depois que estiver bootando: GPU (paraLLEl-GS), resolução, controles.
