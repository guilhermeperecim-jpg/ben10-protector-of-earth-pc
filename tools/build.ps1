# Configures and builds PS2Recomp (third_party\PS2Recomp, outside git) in build\ps2recomp with MSVC + Ninja.
# usage: powershell -ExecutionPolicy Bypass -File tools\build.ps1 [-Targets ps2_recomp,ps2_analyzer] [-Jobs 6]
#   targets: ps2_recomp, ps2_analyzer (the tools) or ps2EntryRunner (the game, after tools\sync_generated.py)
param([string[]]$Targets = @('ps2_recomp', 'ps2_analyzer'), [int]$Jobs = 6, [switch]$KeepGoing)
$ErrorActionPreference = 'Stop'
$Targets = $Targets -split ','  # "-File ... -Targets a,b" arrives as one string
$root = Split-Path $PSScriptRoot -Parent
$src = "$root\third_party\PS2Recomp"
$bld = "$root\build\ps2recomp-dj"
if (-not (Test-Path "$src\CMakeLists.txt")) { throw "missing $src (clone ran-j/PS2Recomp there)" }

# Load the MSVC x64 environment into this process.
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath
cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item "env:$($Matches[1])" $Matches[2] }
}

# Same configuration as def-jam-vendetta-recomp's build-fast.bat (the runtime is theirs, see patches\README.md):
# RelWithDebInfo with explicit flags, so no /GL + /LTCG over thousands of generated functions; aggressive logs off.
# 256 MB stack: every guest call is a nested C++ call; the default 1 MB overflowed in ghost_pc (0xC00000FD).
cmake -S $src -B $bld -G Ninja -DCMAKE_BUILD_TYPE=RelWithDebInfo `
    '-DCMAKE_CXX_FLAGS_RELWITHDEBINFO=/O2 /Ob1 /DNDEBUG /arch:AVX2' '-DCMAKE_C_FLAGS_RELWITHDEBINFO=/O2 /DNDEBUG' `
    '-DCMAKE_EXE_LINKER_FLAGS_RELWITHDEBINFO=/INCREMENTAL:NO /DEBUG /OPT:REF /OPT:ICF /STACK:268435456' `
    -DPS2X_BUILD_TEST=OFF -DPS2X_BUILD_STUDIO=OFF -DPS2X_ENABLE_AGRESSIVE_LOGS=OFF -DPS2X_ENABLE_SCCACHE=OFF `
    "-DPS2X_PARALLEL_GS_DIR=$root/third_party/parallel-gs"  # GS on the GPU (Vulkan); skipped if the folder is missing
if ($LASTEXITCODE) { throw "cmake configure failed ($LASTEXITCODE)" }
$extra = if ($KeepGoing) { @('--', '-k', '0') } else { @() }  # -KeepGoing: report every failing file at once
cmake --build $bld --target @Targets -j $Jobs @extra
if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }
