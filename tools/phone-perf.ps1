<#
.SYNOPSIS
    Mesure la fluidité d'Ivory State sur un téléphone Android branché en USB (débogage USB actif).

.DESCRIPTION
    Lance le jeu sur le téléphone avec des réglages de test (voir PerfProbe), le laisse jouer seul
    (-bot), puis lit dans le journal Android les moyennes d'images par seconde ([Perf]).
    Le serveur de jeu doit tourner sur le PC (adresse du PC sur le réseau local).

.EXAMPLE
    powershell -File tools/phone-perf.ps1 -Options "-renderScale 0.7"
    powershell -File tools/phone-perf.ps1 -Options "-noPost" -Seconds 60
#>
param(
    [string]$Options = "",
    [string]$Address = "192.168.1.16",
    [int]$Seconds = 45
)

$adb = "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$package = "com.ivorystate.game"
$activity = "$package/com.unity3d.player.UnityPlayerGameActivity"

& $adb shell am force-stop $package
& $adb logcat -c
$arguments = "-client -address $Address -pseudo PerfBot -bot -perfLog $Options".Trim()
& $adb shell am start -n $activity -e unity "'$arguments'" | Out-Null
Start-Sleep -Seconds $Seconds

$lines = & $adb logcat -d -s Unity:I | Select-String '\[Perf\]'
$fps = @($lines | ForEach-Object { if ($_ -match 'fps=([\d.]+)') { [double]$Matches[1] } })
# Les 10 premières secondes (chargement, apparition) faussent la moyenne : on les ignore.
$stable = if ($fps.Count -gt 2) { $fps[2..($fps.Count - 1)] } else { $fps }
$lines | Select-Object -Last 4 | ForEach-Object { $_.Line }
if ($stable.Count -gt 0) {
    $avg = ($stable | Measure-Object -Average).Average
    Write-Host ("Options [{0}] : {1:N1} img/s en moyenne ({2} mesures)" -f $Options, $avg, $stable.Count)
} else {
    Write-Host "Aucune mesure [Perf] : le jeu a-t-il démarré ?"
}
