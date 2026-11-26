# Patches do runtime (PS2Recomp)

`third_party/PS2Recomp` é recriado assim:

```powershell
git clone https://github.com/ran-j/PS2Recomp.git third_party\PS2Recomp
git -C third_party\PS2Recomp checkout 2c5fbb9
git -C third_party\PS2Recomp apply ..\..\patches\defjam-ps2recomp-local.diff
git -C third_party\PS2Recomp apply ..\..\patches\ben10-on-defjam.diff
```

| Arquivo | O que é |
|---|---|
| `defjam-ps2recomp-local.diff` | Runtime do def-jam-vendetta-recomp (GPL-3.0), cópia sem mudanças: layout de memória, pilhas, IOP, SPU2, GS, controle etc. |
| `ben10-on-defjam.diff` | Nossas mudanças por cima: busca ISO9660 no `sceCdSearchFile` com `cdImage` (o CDARB lê setores reais pelo IOP), syscall `0x80`, heap até a base da pilha principal, `setPresentUi` sem paraLLEl-GS, `PS2X_TICK_LOG`; no VU1, o código compilado e o modo rápido do interpretador valendo também para microcódigo que lê a flag MAC (`markMacProducers`, `execUpperFastMac`, `Vu1CompiledPiece::macFlagsExact`); no recompilador, `-nome@endereço` na lista de stubs (nunca vira stub, mesmo com nome de handler do runtime), as instruções de memória do VU0 em modo macro (`VLQI/VSQI/VLQD/VSQD/VILWR/VISWR`) e as permutações MMI (`PROT3W`, `PEXEW`, `PEXCW`, `PEXEH`, `PEXCH`, `PREVH`, `PINTH`, `PINTEH`); diagnósticos `PS2X_DRAWLOG`, `PS2X_VU1DUMP`, `PS2X_VU0DUMP` |
| `defjam-parallel-gs-local.diff` | paraLLEl-GS do Def Jam (GS na GPU, Vulkan): `git clone https://github.com/Arntzen-Software/parallel-gs third_party\parallel-gs`, `checkout cc6184af`, `submodule update --init --recursive`, depois este patch |
| `old-upstream-ccc1655.diff` | Histórico: as correções feitas sobre o PS2Recomp `ccc1655` antes da troca de base |
