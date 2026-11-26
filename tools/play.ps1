# Starts the game to play (no time limit, your own keyboard / controller), in a window.
# usage: powershell -ExecutionPolicy Bypass -File tools\play.ps1 [-Fullscreen] [-Log]
#   Keyboard: WASD left stick, arrows d-pad, X/C/Z/V = cross/circle/square/triangle, Q/E = L1/R1, 1/3 = L2/R2,
#   Enter = START, right Shift = SELECT. An XInput controller (Xbox layout) works as well.
#   -Log: every 5 s the frame rate, where the time goes and the sound underruns, in build\run\play.log
param([switch]$Fullscreen, [switch]$Log,
      [string]$Iso = "$env:USERPROFILE\Downloads\Ben 10 - Protector of Earth (1)\Ben 10 - Protector of Earth.iso")
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path $Iso)) { throw "disc image not found: $Iso (pass -Iso <path to your Ben 10 ISO>)" }
$env:PS2X_CD_IMAGE = $Iso
$env:VENDETTA_WINDOW = if ($Fullscreen) { 'borderless' } else { 'window' }
# GS on the graphics card at 8x (16x, the runtime's default, is too heavy for a GTX 1650). The first start at a new
# scale prepares the shaders once (about 4 minutes here); build\run keeps that cache, shared with tools\run.ps1.
if (-not $env:VENDETTA_GS) { $env:VENDETTA_GS = 'gpu' }
if (-not $env:VENDETTA_SSAA) { $env:VENDETTA_SSAA = '8' }
# The keyboard plays as player 1 (the runtime's settings.toml default, keyboard = 0, gives it to nobody).
if (-not $env:VENDETTA_KEYBOARD) { $env:VENDETTA_KEYBOARD = '1' }
Remove-Item Env:VENDETTA_PADSCRIPT, Env:VENDETTA_NO_HOST_INPUT, Env:PS2X_MC_ROOT -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$root\build\run" | Out-Null
$run = @{ FilePath = "$root\build\ps2recomp-dj\ps2xRuntime\ps2EntryRunner.exe"; ArgumentList = "$root\game\SLUS_216.61"
          WorkingDirectory = "$root\build\run" }
if ($Log) {
    $env:VENDETTA_HOSTPROF = '1'; $env:VENDETTA_AUDIOLOG = '1'
    $run.RedirectStandardError = "$root\build\run\play.log"; $run.RedirectStandardOutput = "$root\build\run\play.out.log"
}
Start-Process @run
