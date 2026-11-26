# Tiles build\run\shot_*.png (tools\run.ps1 -ShotEvery) into build\run\sheet.png, each labelled with its second.
param([int]$Columns = 4, [int]$Width = 384, [int]$Height = 216)
Add-Type -AssemblyName System.Drawing
$dir = Join-Path (Split-Path $PSScriptRoot -Parent) 'build\run'
$files = Get-ChildItem "$dir\shot_*.png" | Sort-Object Name
if (-not $files) { throw "no screenshots in $dir" }
$rows = [Math]::Ceiling($files.Count / $Columns)
$sheet = New-Object System.Drawing.Bitmap ($Width * $Columns), ($Height * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::Gray)
$font = New-Object System.Drawing.Font 'Arial', 14, ([System.Drawing.FontStyle]::Bold)
for ($k = 0; $k -lt $files.Count; $k++) {
    $img = [System.Drawing.Image]::FromFile($files[$k].FullName)
    $x = ($k % $Columns) * $Width; $y = [Math]::Floor($k / $Columns) * $Height
    $g.DrawImage($img, $x, $y, $Width - 2, $Height - 2); $img.Dispose()
    $g.DrawString(($files[$k].BaseName -replace 'shot_0*', '') + 's', $font, [System.Drawing.Brushes]::Yellow, $x + 4, $y + 4)
}
$sheet.Save("$dir\sheet.png"); "sheet: $dir\sheet.png"
