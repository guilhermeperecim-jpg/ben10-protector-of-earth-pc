# Plano: port nativo de Ben 10: Protector of Earth (SLUS-21661 v1.01) para PC

Base: PS2Recomp (`ran-j/PS2Recomp`, commit `ccc1655`) + camada nativa por cima, no modelo do War of the Monsters.
Windows primeiro (MSVC, que é o compilador testado pelo PS2Recomp), Linux depois.

Revisão 2 (2026-11-24): corrigida a parte do SDK (o `ps2xAnalyzer` reconhece funções sem símbolos), os timers, o vídeo, o SIO2 e o SPU2, e a ordem das fases.

---

## 1. Como os dados foram levantados

| Fonte | O que deu |
|---|---|
| **REA 6.9.0** (CLI e MCP) | Inventário e hash do ELF e dos `.IRX`. **Não abre MIPS neste PC**: no Windows, o provedor Ghidra do REA só aceita PE/DOS/6502 bruto (`target_format_unsupported`), e Hopper, IDA e Kuna não aceitam MIPS. Testado pelo CLI e, depois que conectou, pelo MCP: o resultado é o mesmo. A ISO é recusada (`unsupported binary format`). |
| Ghidra 12.2 + `ghidra-emotionengine-reloaded` | Análise completa (`tools/ghidra_analyze.ps1`): 4438 funções, 1585 com nome de script, 42 syscalls nomeadas. |
| Leitura direta do ELF, dos `.IRX` e da ISO | Instruções `syscall` + `$v1`, chamadas `jal`, constantes `lui 0x8000` (RPC), acessos a hardware (base `lui` + o load/store que a usa), tabelas de import dos IRX (magic `0x41E00000`), ROMDIR do `IOPRP300.IMG` e cabeçalhos Bink no `GAME.WAD`. |
| Código do PS2Recomp, Def Jam e War of the Monsters | Despachante de syscalls, `ps2_call_list.h`, despachantes do `ps2xIOP`, política de módulos, `ps2xAnalyzer` e as configs do Def Jam (`analyzer.toml`, `manual_stubs.txt`). |

Para o REA funcionar neste alvo, seria preciso instalar o Rizin (`execute-rizin-command`, arch `mips`) ou rodar o REA dentro de um Linux (WSL), onde o provedor Ghidra aceita ELF.

---

## 2. Imports do lado do EE (o processador principal)

O ELF é estático e não tem tabela de import. O que ele "importa" são as syscalls do kernel, as bibliotecas do SDK que vieram ligadas a ele e o hardware que ele acessa direto.

### 2.1 Syscalls do kernel: 54 usadas, 53 cobertas

O executável traz a tabela inteira de wrappers da `libkernl` (142 números). O jogo chama 54 deles.

| Situação | Syscalls |
|---|---|
| **Prontas no runtime (52)** | Threads: `CreateThread`, `StartThread`, `ChangeThreadPriority`, `RotateThreadReadyQueue`, `GetThreadId`, `ReferThreadStatus`, `WakeupThread`, `iWakeupThread`, `SuspendThread`. Semáforos: `CreateSema` (19 chamadas), `DeleteSema` (26), `SignalSema` (46), `iSignalSema`, `WaitSema`, `PollSema`. Interrupções e DMA: `Add/RemoveIntcHandler`, `Add/RemoveDmacHandler`, `Enable/DisableIntc`, `Enable/DisableDmac`. SIF: `sceSifSetDma`, `isceSifSetDma`, `sceSifSetDChain`, `isceSifSetDChain`, `sceSifDmaStat`, `sceSifSetReg`, `sceSifGetReg`. Sistema: `FlushCache` (19), `iFlushCache`, `EndOfHeap`, `Get/SetOsdConfigParam`, `GetOsdConfigParam2`, `GsGetIMR`, `GsPutIMR`, `SetVSyncFlag`, `Deci2Call`, `FindAddress`. Patch de kernel do SDK: `Copy`, `GetEntryAddress`, `SetSyscall` (12 chamadas). No crt0: `SetupThread`, `SetupHeap`, `ExitThread`. |
| **Coberta por stub (1)** | `0x6B` (wrapper em `0x1C50B0`). O export do Ghidra já liga o endereço ao stub `sceSifStopDma`, que retorna 0. |
| **Falta (1)** | `0x80` (wrapper avulso em `0x1C0218`, 1 chamada a partir de `0x1C02A8`, na região da `libgraph`). Provavelmente é `GetGsDxDyOffset`, mas isso não foi confirmado. O `ps2xAnalyzer` pode dar o nome. |

### 2.2 Bibliotecas do SDK ligadas ao executável (sem símbolos)

**Correção da revisão 1:** o `ps2xAnalyzer` do PS2Recomp traz um banco de assinaturas do SDK da Sony (`sce-symbol-scanner`) e reconhece funções em executáveis sem símbolos. O export do Ghidra não usa esse banco. No Def Jam, o resultado ficou assim:

| Fonte | Funções ligadas a stubs do runtime |
|---|---|
| Export do Ghidra | 54 (só syscalls) |
| `ps2xAnalyzer` (`analyzer.toml`) | **203**: `sceCd*`, `scePad*`, `sceMc*`, `sceDma*`, `sceGs*`, `sceSif*` (incluindo `sceSifRebootIop`, `sceSifResetIop` e `sceSifSyncIop`), `sceOpen/sceRead/...`, `sceMpeg*`, `sceDeci2*` e a libc |
| À mão (`manual_stubs.txt`) | +3 (`sceCdDiskReady`, `sceCdSearchFile`, `sceMpegIsEnd`) e −9 que **não** podem ser ligadas: a matemática da libc (`sqrt`, `atan2`, `atan`, `fabs`, `__kernel_sinf/cosf`, `__ieee754_rem_pio2f`), porque as versões do runtime usam float e o jogo usa double por software; `rand`, que tem outra faixa de valores; e `sceGsResetGraph`, que perde o modo de vídeo do jogo |

**Para o Ben 10:** rodar o `ps2xAnalyzer` no `SLUS_216.61`, juntar a saída com o export do Ghidra (como o `make_config.py` do Def Jam faz) e já começar com essa lista de exclusões.

Bibliotecas que o jogo usa, identificadas pelo ID do servidor RPC:

| RPC | Biblioteca | Função no EE (Ghidra) |
|---|---|---|
| `0x80000001` | fileio (`sceOpen/sceRead/...`) | `entry_001c7718`, `entry_001c8e90` |
| `0x80000003` | iopheap (`sceSifAllocIopHeap`) | `entry_001c9a8c` |
| `0x80000006` | loadfile (`sceSifLoadModule`) | `entry_001c9c18` |
| `0x80000100`, `0x80000101` | libpad | `entry_001c111c`, `entry_001c1174` |
| `0x80000400` | libmc | `entry_001c218c` |
| `0x80000592/593/595/597/59A/59C` | libcdvd | `0x1C36E0`–`0x1C434C` |

Os valores `0x80008001`–`0x80009021` **não são RPC**: são endereços da memória do kernel usados pelo patch de kernel do SDK. Também não sei qual ID o EE usa para falar com o ALASD (`0x80000027`, em `entry_001987e8`, é um candidato que não foi confirmado).

### 2.3 Hardware acessado direto

Contei só os acessos em que o endereço montado pelo `lui` é usado de fato por um load/store.

| Bloco | Funções | Runtime |
|---|---|---|
| GIF + DMA ch2 | 5 + 7 | ok |
| VIF1 + DMA ch1 | 12 + 10 | ok (interpretador VIF1) |
| VIF0 + FIFO | 1 + 6 | ok |
| DMA ch8/9 (scratchpad) | 11 | ok (`tryProcessScratchpadDma`) |
| DMA ctrl, INTC/SIF | 16 + 7 | ok |
| GS privilegiado (CSR/IMR/display) | 4 + 9 | ok (backend GS por CPU) |
| Scratchpad | 60 | ok |
| Timers | **64** (108 leituras de `T0_COUNT`, 5 do timer 2). Corrigido: a revisão 1 dizia 210. | ok |
| IPU (vídeo MPEG) | 0 | não usado |

### 2.4 Unidades vetoriais

- VU0 macro: 2795 instruções, mais 2576 movimentações COP2 e 1235 `LQC2/SQC2`. O recompilador suporta.
- VU0 micro: 6 `VCALLMS`. O runtime tem `executeVU0Microprogram`.
- VU1: `.vutext` de 58 KB + 32 overlays (`.DVP.overlay.*`), que é o renderizador. Roda no interpretador de VU1 (lento). O Def Jam resolveu isso com `vu1_recomp.py`.
- MMI: 775 instruções, suportadas.

### 2.5 Vídeo

Os vídeos são **Bink 1, revisão `i`**: 69 cabeçalhos `BIKi` no `GAME.WAD`, um por `.bik` do `GAME.DIR`. O jogo os toca com `CBinkPlayer` e decodifica por software no EE. Funcionam recompilados, mas devem ser pesados. O FFmpeg decodifica Bink 1. Os `.pss`/`.psm` do `GAME.DIR` são pacotes de script e de mapa do motor, não vídeo.

---

## 3. Imports do lado do IOP (o processador de E/S)

### 3.1 Módulos

| Módulo | O que é | Carregado? |
|---|---|---|
| `IOPRP300.IMG` | Reinício do IOP com os módulos do SDK 3.0 (SYSMEM, LOADCORE, SIFCMD, SIFMAN, THREADMAN, IOMAN, MODLOAD, FILEIO, CDVDMAN, CDVDFSV, LOADFILE, TIMEMANI, ROMDRV, EESYNC, SYSCLIB, STDIO) | sim (`rom0:UDNL cdrom0:\IOPRP300.IMG;1`). O `ps2xIOP` não lê imagens IOPRP. O caminho é ligar `sceSifRebootIop/ResetIop/SyncIop` a stubs, como o Def Jam fez. |
| `SIO2MAN`, `PADMAN`, `MTAPMAN` | controle e multitap | sim |
| `MCMAN`, `MCSERV` | memory card | sim |
| `LIBSD` | biblioteca do chip de som (SPU2) | sim |
| `ALASD` | **driver de som do jogo** (vozes, streams, reverb; servidor RPC próprio) | sim |
| `CDARB` | **árbitro de CD da SN Systems** (divide o drive entre streaming e carregamento) | sim |
| `SCREAM`, `STREAM`, `CDVDSTM`, `USBD`, `USBKB`, `SN_ALASD`, `SN_CDARB` | estão no disco | **não**: o executável não cita esses nomes |

O `ps2xIOP` carrega o `.IRX` real e só usa a versão embutida se o arquivo faltar (`iop_subsystem.cpp`). As bibliotecas que os módulos do jogo exportam (`libsd`, `sio2man`, `mcman`, `CDArb`) rodam como código original, e as chamadas entre módulos seguem por `JumpToGuest`.

### 3.2 Funções do kernel do IOP importadas: 109, faltam 4

| Biblioteca | Ordinais usados | Situação |
|---|---|---|
| `thbase` | 4 5 6 8 10 14 20 22 23 33 35 39 | ok |
| `thsemap` | 4 5 6 7 8 9 11 | ok |
| `thevent` | 4–10 | ok |
| `intrman` | 4–9 17 18 23 | ok |
| `sifcmd` | 4 8 10 12 14 17 19 22 23 24 25 28 | ok |
| `sifman` | 5 7 8 29 32 | ok |
| `sysmem` | 4 5 7 14 | ok |
| `loadcore` | 5 6 7 27 | ok |
| `sysclib` | 8 12 14 17 20–23 27 29 30 32 36 | ok |
| `stdio` | 4 | ok |
| `timrman` | 4 6 7 9 11 16 20 22 23 24 | ok |
| `vblank` | 8 9 | ok |
| `dmacman` | 28 32–35 | ok |
| `cdvdman` | 4 6 8 11 13 24 28 75 | ok |
| `secrman` | 4 5 6 | ok |
| `modload` | 13 | ok |
| `ioman` | 20 21 (`AddDrv`/`DelDrv`, do MCMAN) | ok |
| **`ioman`** | **4 5 6 8** (`open`/`close`/`read`/`lseek`, do **CDARB**) | **faltam** |

Além dessas, os módulos do jogo importam entre si: `libsd` (15 funções), `sio2man` (16), `mcman` (23) e `CDArb` (2). Todas rodam como código dos próprios módulos.

### 3.3 Hardware do IOP

- **SIO2** (`core/iop_sio2.cpp`): atende o controle DualShock 2 (modo `0x01`) e o **memory card** (modo `0x81`). O multitap responde "nada conectado". Confirmado na revisão 2.
- **SPU2** (`core/iop_spu2.cpp`, 669 linhas): decodifica ADPCM, mixa as vozes e tem ADMA e IRQ. **Não tem reverb**: os efeitos que o ALASD liga com `SetReverb` vão sair sem eco. Isso não bloqueia nada, mas o som fica diferente.

---

## 4. O que falta, em uma lista

| # | Item | Bloqueia? |
|---|---|---|
| 1 | Syscall `0x80` do EE | provavelmente sim, se a `libgraph` usar o retorno |
| 2 | `ioman` `open/close/read/lseek` no IOP, para o CDARB | sim, se o CDARB abrir arquivos ao iniciar |
| 3 | Rodar o `ps2xAnalyzer` e montar a lista de stubs com as exclusões do Def Jam | sim (sem ele, o reinício do IOP não passa) |
| 4 | Reverb no SPU2 | não (qualidade do som) |
| 5 | Desempenho: GS por CPU, VU1 interpretado e Bink por software | não, mas os três pesam |
| 6 | Funções chamadas por ponteiro que o Ghidra não achou | descobertas durante o boot (`PS2X_MISSING_LOG`) |

---

## 5. Fases

| Fase | Objetivo | Pronto quando | Base reaproveitada |
|---|---|---|---|
| **0. Build** | Compilar o PS2Recomp, rodar o `ps2xAnalyzer`, juntar os stubs, gerar o C++ e rodar | Janela abre e o log mostra o primeiro erro | `make_config.py` e `sync_generated.py` (Def Jam); pilha de 256 MB e build sem LTCG (ghost_pc) |
| **1. Boot até o menu** | Disco (`GAME.DIR`/`GAME.WAD`) + scripts carregando + menu na tela | Logo e menu desenhados (mesmo devagar) | `PS2X_MISSING_LOG` + `CreateFunctionsAt.java`, `find_missing_starts.py`, overrides por endereço. Itens 1–3 da lista acima |
| **2. Fase jogável** | Entrar numa fase com controle | Ben andando e lutando | Pad pelos stubs do runtime (se o analyzer achar `scePad*`) ou pelo PADMAN real |
| **3. Imagem** | GS na GPU com resolução alta e VU1 rápido | 60 FPS na fase, sem defeitos | paraLLEl-GS + `parallel-gs-local.diff` e `vu1_recomp.py` (Def Jam) |
| **4. Som e saves** | ALASD/LIBSD no SPU2, MCMAN | Música, efeitos e save funcionando | — |
| **5. Vídeo** | Bink pelo host | Cutscenes rodando | Interceptar `CBinkPlayer::Play(nome)` e tocar com o FFmpeg (decoder Bink 1) |
| **6. "Nativo"** | Widescreen, FPS alto, controles e opções | Menu de opções próprio | Tabela de scripts (1585 funções nomeadas) como pontos de gancho |
| **7. Linux** | Build no Linux | Mesmo jogo rodando no Linux | Scripts Linux do War of the Monsters (`setup_runtime.sh`, `build_runtime.sh`). Atenção a maiúsculas nos caminhos do `cdrom0:` |

Mudanças em relação à revisão 1: "Boot" e "Disco" viraram uma fase só, porque o menu já vem dos scripts do `GAME.WAD`. O controle subiu para a fase 2, porque sem ele não dá para testar nada depois do menu.

---

## 6. Decisões e riscos

- **Versão do PS2Recomp.** O Def Jam fixa o commit `2c5fbb9` e aplica 23 mil linhas de patch em cima. O paraLLEl-GS dele depende desse runtime. Proposta: começar do upstream atual (`ccc1655`, mais correções, inclusive a do `SQRT.S` que o War of the Monsters precisou) e portar o paraLLEl-GS na fase 3, sabendo que o diff vai precisar de ajuste.
- **Memória do build.** O Def Jam gera cerca de 25 mil arquivos para 3188 funções. O Ben 10 tem 4438 funções. Com 16 GB de RAM, a compilação precisa rodar com menos processos em paralelo.
- **Linux.** O PS2Recomp é testado principalmente com MSVC. O War of the Monsters compila no Linux com o fork dele, então devem aparecer ajustes para GCC/Clang.

## 7. Incertezas

- Nome da syscall `0x80` e o servidor `0x80000019`.
- Qual ID de RPC o EE usa para o ALASD.
- Quantas funções o banco do `ps2xAnalyzer` reconhece neste jogo (o banco foi montado principalmente com jogos japoneses).
- Se o SPU2 emulado aguenta os streams do ALASD.
