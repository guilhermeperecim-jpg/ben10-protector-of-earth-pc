# Ben 10: Protector of Earth (PS2) → PC

Port por recompilação estática com o [PS2Recomp](https://github.com/ran-j/PS2Recomp), usando como base o runtime do
[def-jam-vendetta-recomp](https://github.com/jcsix694/def-jam-vendetta-recomp).

**Este repositório não contém nenhum arquivo do jogo**: nem a ISO, nem o executável, nem o código gerado a partir dele,
nem imagens da tela. Para usar, você precisa do **seu próprio dump** do disco original.

## Dump suportado

| Campo | Valor |
|---|---|
| Serial | SLUS-21661 (NTSC-U), versão 1.01 (Original e Greatest Hits são o mesmo disco) |
| ISO | 3.400.499.200 bytes, CRC32 `872c6408`, SHA-1 `d212e3cbe2cf234b98c4d0cb35e0edc555b83d6e` ([redump 20349](http://redump.org/disc/20349/)) |
| ELF de boot | `SLUS_216.61`, sha256 `f9f1efe049d17729ce9c4a57dcdfd4a82d28c481f4193be207c9da84e3008f03` |

## Andamento

| Fase | Estado |
|---|---|
| 0. Build | concluída |
| 1. Boot até o menu | concluída |
| 2. Fase jogável | concluída |
| 3. Imagem (GPU, resolução alta, 60 FPS) | concluída |
| 4. Som e saves | em andamento |
| 5. Vídeos, 6. Recursos de PC, 7. Linux | a fazer |

Detalhes e comandos em [`docs/CHECKLIST.md`](docs/CHECKLIST.md); plano em [`PLANO.md`](PLANO.md).

## Como montar (Windows)

1. Visual Studio 2022 (C++), CMake, Ninja, Python 3, Git. Para refazer a análise: Ghidra 12.2 com `ghidra-emotionengine-reloaded`.
2. Recrie `third_party/` com os patches: [`patches/README.md`](patches/README.md).
3. Extraia o `SLUS_216.61` do seu dump para `game/`.
4. Siga os comandos de [`docs/CHECKLIST.md`](docs/CHECKLIST.md) (Fase 1): config, recompilação, `tools\vu1_recomp.py`, build.
5. Jogue: `powershell -ExecutionPolicy Bypass -File tools\play.ps1 -Iso "<caminho da sua ISO>"`.

## Estrutura

```
config/     símbolos, mapa de funções e decisões de stubs (endereços e nomes, nada do código do jogo)
tools/      scripts (build, execução, diagnóstico, gerador do VU1 compilado)
overrides/  código C++ específico do jogo aplicado pelo runtime
patches/    nossas mudanças no PS2Recomp e no paraLLEl-GS (o resto vem dos repositórios originais)
docs/       checklist das fases e análise
game/ generated/ build/ third_party/ refs/ ghidra/   ficam só no seu PC (fora do git)
```

## Para quem contribui

Ative uma vez a verificação que impede publicar arquivos do jogo (roda antes de cada commit):

```
git config core.hooksPath tools/githooks
python -I tools/check_publish.py --all
```

Ela barra extensões de disco/mídia, arquivos grandes, executáveis ELF, imagens ISO e qualquer trecho copiado do
executável do jogo.

## Licença

GPL-3.0 ([`LICENSE`](LICENSE)): o projeto inclui e adapta código GPL-3.0 do def-jam-vendetta-recomp. Ben 10 é marca da
Cartoon Network; este projeto não tem ligação com os donos do jogo e não distribui nada dele.
