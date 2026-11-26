# Imports game\SLUS_216.61 into a Ghidra project (ghidra\, outside git), runs auto-analysis, names the strat-binding
# functions and exports the recompiler's inputs: config\ben10.ghidra.toml + config\ben10_functions.ghidra.csv.
# Scripts: tools\ghidra\ApplySymbolsCsv.java (from HayatoG/ps2_recomp), ExportPS2Functions.java (from ran-j/PS2Recomp).
# usage: powershell -ExecutionPolicy Bypass -File tools\ghidra_analyze.ps1   (GHIDRA_DIR / JAVA_HOME override the defaults)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ghidra = if ($env:GHIDRA_DIR) { $env:GHIDRA_DIR } else { "$env:LOCALAPPDATA\Programs\ghidra_12.2_PUBLIC" }
if (-not $env:JAVA_HOME) { $env:JAVA_HOME = (Get-ChildItem 'C:\Program Files\Eclipse Adoptium' -Directory -Filter 'jdk-21*' | Select-Object -First 1).FullName }
$elf = "$root\game\SLUS_216.61"
if (-not (Test-Path $elf)) { throw "missing $elf (extract it from your disc first)" }
New-Item -ItemType Directory -Force "$root\ghidra" | Out-Null
# Settings/cache inside the project: when run from a packaged (MSIX) app, writes to AppData are redirected and
# Ghidra's OSGi cache (Felix) then fails with "The data file must be inside the data dir".
$env:JDK_JAVA_OPTIONS = "-Dapplication.settingsdir=$root\ghidra\settings -Dapplication.cachedir=$root\ghidra\cache"
python -I "$root\tools\script_bindings.py" $elf "$root\config\symbols_script.csv"
if ($LASTEXITCODE) { throw 'script_bindings.py failed' }
# SDK names from ps2xAnalyzer's signature DB (config\analyzer.toml, made by build\...\ps2_analyzer.exe)
python -I "$root\tools\sdk_symbols.py" "$root\config\analyzer.toml" "$root\config\symbols_sdk.csv"
if ($LASTEXITCODE) { throw 'sdk_symbols.py failed (run ps2_analyzer first)' }
# A project copied without Ghidra's "~index" files cannot be opened: start it afresh.
Remove-Item -Recurse -Force "$root\ghidra\ben10.gpr", "$root\ghidra\ben10.rep" -ErrorAction SilentlyContinue
& "$ghidra\support\analyzeHeadless.bat" "$root\ghidra" ben10 -import $elf -overwrite `
    -processor 'r5900:LE:32:default' -scriptPath "$root\tools\ghidra" `
    -postScript ApplySymbolsCsv.java "$root\config\symbols_sdk.csv" `
    -postScript ApplySymbolsCsv.java "$root\config\symbols_script.csv" `
    -postScript ExportPS2Functions.java "$root\config\ben10.ghidra.toml" "$root\config\ben10_functions.ghidra.csv" `
    -log "$root\ghidra\headless.log" -scriptlog "$root\ghidra\scripts.log"
if ($LASTEXITCODE) { throw "analyzeHeadless failed ($LASTEXITCODE), see ghidra\headless.log" }
