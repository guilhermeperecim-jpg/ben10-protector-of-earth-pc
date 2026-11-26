# Starts the game to play (no time limit, your own keyboard / controller), in a window.
# usage: powershell -ExecutionPolicy Bypass -File tools\play.ps1 [-Fullscreen] [-Log]
#   Keyboard (ps2_pad.cpp): WASD / arrows = d-pad, X/C/Z/V = cross/circle/square/triangle, Q/E = L1/R1,
#   left/right Shift = L2/R2, Enter = START, Tab = SELECT, I J K L = right stick. An XInput controller works as well.
#   -Log: every 5 s the frame rate, where the time goes and the sound underruns, in build\run\play.log
param([switch]$Fullscreen, [switch]$Log,
      [string]$Iso = "$env:USERPROFILE\Downloads\Ben 10 - Protector of Earth (1)\Ben 10 - Protector of Earth.iso")
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path $Iso)) { throw "disc image not found: $Iso (pass -Iso <path to your Ben 10 ISO>)" }
$env:PS2X_CD_IMAGE = $Iso
$env:VENDETTA_WINDOW = if ($Fullscreen) { 'borderless' } else { 'window' }
# The options (renderer, sharpness, widescreen, keyboard...) come from build\run\settings.toml (delete it for the
# defaults). The first start at a new sharpness prepares the shaders once (about 4 minutes here).
Remove-Item Env:VENDETTA_PADSCRIPT, Env:VENDETTA_NO_HOST_INPUT, Env:PS2X_MC_ROOT -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$root\build\run" | Out-Null
$run = @{ FilePath = "$root\build\ps2recomp-dj\ps2xRuntime\ps2EntryRunner.exe"; ArgumentList = "$root\game\SLUS_216.61"
          WorkingDirectory = "$root\build\run" }
if ($Log) {
    $env:VENDETTA_HOSTPROF = '1'; $env:VENDETTA_AUDIOLOG = '1'
    $run.RedirectStandardError = "$root\build\run\play.log"; $run.RedirectStandardOutput = "$root\build\run\play.out.log"
}
Start-Process @run
