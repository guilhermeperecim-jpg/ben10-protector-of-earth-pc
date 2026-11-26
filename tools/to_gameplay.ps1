# Pad script that goes from a fresh start (empty memory card) into the first level and fights: prints it for run.ps1.
# usage: tools\run.ps1 -PadScript (powershell -File tools\to_gameplay.ps1)
# Menus: memory-card prompts, START on the title, then CROSS every 4 s (New Game, difficulty, save slot, tips; in the
# level CROSS is a jump, while a late START would open the pause menu); then from 110 s up/right/left moves with square
# (attack) and cross (jump).
$menu = @('30:CROSS', '38:CROSS', '50:START') + (1..13 | ForEach-Object { "$(50 + 4 * $_):CROSS" })
$play = for ($k = 0; $k -lt 240; $k++) {
    $t = (110 + $k * 0.5).ToString([Globalization.CultureInfo]::InvariantCulture)
    "${t}:" + @('UP','UP','UP','UP','SQUARE','UP','UP','RIGHT','RIGHT','SQUARE','CROSS','LEFT')[$k % 12]
}
($menu + $play) -join ','
