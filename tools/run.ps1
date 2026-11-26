# Runs the recompiled game for a while, keeps its logs and screenshots in build\run\ and prints what blocks it.
# usage: powershell -ExecutionPolicy Bypass -File tools\run.ps1 [-Seconds 90] [-ShotEvery 10] [-PadScript "30:CROSS,40:START"]
#   -PadScript: scripted presses (VENDETTA_PADSCRIPT, seconds from the game's first pad read); the PC's own keyboard and
#   controllers are then ignored so nothing typed elsewhere reaches the game.
param([int]$Seconds = 90, [int]$ShotEvery = 0, [string]$PadScript = '', [switch]$KeepCard,
      [string]$Iso = "$env:USERPROFILE\Downloads\Ben 10 - Protector of Earth (1)\Ben 10 - Protector of Earth.iso")
$root = Split-Path $PSScriptRoot -Parent
# The disc image for sector reads (EE sceCdRead and the IOP's cdvdman/CDARB read the same real sectors).
if (-not $env:PS2X_CD_IMAGE -and (Test-Path $Iso)) { $env:PS2X_CD_IMAGE = $Iso }
if (-not $env:VENDETTA_WINDOW) { $env:VENDETTA_WINDOW = 'window' }
if ($PadScript) {
    $env:VENDETTA_PADSCRIPT = $PadScript; $env:VENDETTA_NO_HOST_INPUT = '1'
    # Scripted runs start from an empty scratch memory card: the script expects a fresh card, and killing the game at
    # the end (maybe mid auto-save) must never touch the player's save in game\mc0. -KeepCard: the scratch card of
    # the previous scripted run stays (to test loading a save).
    if (-not $KeepCard) { Remove-Item -Recurse -Force "$root\build\run\mc-test" -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force "$root\build\run\mc-test\mc0", "$root\build\run\mc-test\mc1" | Out-Null
    $env:PS2X_MC_ROOT = "$root\build\run\mc-test\mc0"
}
$exe = "$root\build\ps2recomp-dj\ps2xRuntime\ps2EntryRunner.exe"
$out = "$root\build\run"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\shot_*.png" -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    public struct RECT { public int L, T, R, B; }'
[W.U]::SetProcessDPIAware() | Out-Null  # real pixels, so the capture covers the window exactly
# PrintWindow asks the game window for its own picture (PW_CLIENTONLY | PW_RENDERFULLCONTENT): it works while other
# windows cover it, and never captures anything but the game.
function Save-Shot($proc, $path) {
    $h = (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue).MainWindowHandle
    $r = New-Object W.U+RECT
    if ($h -and $h -ne [IntPtr]::Zero -and [W.U]::GetClientRect($h, [ref]$r) -and $r.R -gt 0) {
        $bmp = New-Object System.Drawing.Bitmap $r.R, $r.B
        $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
        [W.U]::PrintWindow($h, $dc, 3) | Out-Null
        $g.ReleaseHdc($dc); $g.Dispose(); $bmp.Save($path); $bmp.Dispose()
    }
}

$p = Start-Process -FilePath $exe -ArgumentList "$root\game\SLUS_216.61" -WorkingDirectory $out `
    -RedirectStandardOutput "$out\stdout.log" -RedirectStandardError "$out\stderr.log" -PassThru
for ($i = 1; $i -le $Seconds -and -not $p.HasExited; $i++) {
    Start-Sleep -Seconds 1
    if ($ShotEvery -gt 0 -and ($i % $ShotEvery) -eq 0) { Save-Shot $p ("$out\shot_{0:d3}.png" -f $i) }
}
$state = if ($p.HasExited) { "exited after ${i}s with code $($p.ExitCode)" } else { "still running after ${Seconds}s" }
if (-not $p.HasExited) {
    Save-Shot $p "$out\screen.png"; "screenshot: $out\screen.png"
    Stop-Process -Id $p.Id -Force
}
"run: $state"

$log = Get-Content "$out\stdout.log", "$out\stderr.log" -ErrorAction SilentlyContinue
foreach ($pat in 'pure virtual call', 'missing-target', 'Syscall TODO', 'unhandled import', 'TODO_NAMED', 'exception') {
    $hits = $log | Select-String -SimpleMatch $pat
    if ($hits) { "${pat}: $($hits.Count) line(s), first: $(($hits[0].Line).Substring(0, [Math]::Min(220, $hits[0].Line.Length)))" }
}
$ticks = $log | Select-String '^\[run:tick\]'
if ($ticks) { "last tick: $($ticks[-1].Line.Substring(0, [Math]::Min(200, $ticks[-1].Line.Length)))" }
