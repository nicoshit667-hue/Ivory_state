<#
.SYNOPSIS
    Test de bout en bout de la sauvegarde Nakama : argent et pseudo retrouvés après déconnexion ET
    redémarrage du serveur de jeu.

.DESCRIPTION
    Prérequis : Docker Desktop lancé, puis dans backend/ :
        powershell -File setup-keys.ps1     (une seule fois)
        powershell -File up.ps1             (réutilise un Nakama déjà lancé)
    et les builds « Serveur Windows » et « Client Windows ».

    1. Serveur n°1 + client (identifiant d'appareil aléatoire, pseudo « Aya ») qui joue le scénario
       économique (gagne et dépense de l'argent).
    2. Client et serveur arrêtés.
    3. Serveur n°2 (neuf) + même identifiant d'appareil, sans pseudo : le solde et le pseudo doivent
       revenir depuis Nakama.
#>
param([int]$Port = 7792)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'IvoryState\Builds'
$serverExe = Join-Path $builds 'Server-Windows\IvoryStateServer.exe'
$clientExe = Join-Path $builds 'Client-Windows\IvoryState.exe'

try {
    Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:7350/healthcheck' -TimeoutSec 5 | Out-Null
} catch {
    Write-Host "Nakama ne répond pas sur http://127.0.0.1:7350."
    Write-Host "Lance d'abord : cd backend ; powershell -File setup-keys.ps1 ; powershell -File up.ps1"
    exit 2
}

$outDir = Join-Path $root ("loadtests\persist-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $outDir | Out-Null
$deviceId = 'persist-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)

function Test-LogMatch([string]$path, [string]$pattern) {
    if (-not (Test-Path $path)) { return $false }
    return [bool](Select-String -Path $path -Pattern $pattern -Quiet)
}

function Start-Server([string]$name) {
    $log = Join-Path $outDir "$name.log"
    $p = Start-Process $serverExe -PassThru -ArgumentList @('-batchmode', '-nographics', '-server', '-port', $Port, '-logFile', "`"$log`"")
    $deadline = (Get-Date).AddSeconds(90)
    while (-not (Test-LogMatch $log 'taxis en station')) {
        if ((Get-Date) -gt $deadline) { throw "Serveur non démarré ($log)" }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-LogMatch $log 'Sauvegarde Nakama active')) { throw "Le serveur n'a pas activé la sauvegarde (clé introuvable ?) : $log" }
    return @{ Process = $p; Log = $log }
}

function Start-Client([string]$name, [string[]]$extra) {
    $log = Join-Path $outDir "$name.log"
    # Pas « $args » : c'est une variable automatique de PowerShell.
    $clientArgs = @('-batchmode', '-nographics', '-client', '-address', '127.0.0.1', '-port', $Port,
                    '-deviceId', $deviceId, '-e2ePersist', '-logFile', "`"$log`"") + $extra
    return @{ Process = (Start-Process $clientExe -PassThru -ArgumentList $clientArgs); Log = $log }
}

function Get-LastBalance([string]$log) {
    $line = Select-String -Path $log -Pattern '\[E2E-PERSIST\] (li. )?solde=(-?\d+)' | Select-Object -Last 1
    if ($line) { return [long]$line.Matches[0].Groups[2].Value } else { return $null }
}

# --- Partie 1 : jouer et gagner de l'argent ---
$server = Start-Server 'serveur-1'
$client = Start-Client 'client-1' @('-e2eEconomy', '-pseudo', 'Aya')
$deadline = (Get-Date).AddSeconds(400)
while ((Get-Date) -lt $deadline -and -not (Test-LogMatch $client.Log '\[E2E\] (R.USSI|.CHEC)')) { Start-Sleep 2 }
Start-Sleep -Seconds 3   # laisser partir les dernières opérations de portefeuille
$balance1 = Get-LastBalance $client.Log
Stop-Process -Id $client.Process.Id -Force; Start-Sleep -Seconds 3   # sauvegarde du profil à la déconnexion
Stop-Process -Id $server.Process.Id -Force; Start-Sleep -Seconds 2

# --- Partie 2 : serveur neuf, même appareil, sans pseudo ---
$server = Start-Server 'serveur-2'
$client = Start-Client 'client-2' @()
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline -and -not (Test-LogMatch $client.Log '\[E2E-PERSIST\] li. solde=')) { Start-Sleep 2 }
Start-Sleep -Seconds 2
$balance2 = Get-LastBalance $client.Log
$pseudoBack = Test-LogMatch $server.Log 'devient Aya'
Stop-Process -Id $client.Process.Id, $server.Process.Id -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== Test de sauvegarde (Nakama) ==="
Write-Host "Appareil           : $deviceId"
Write-Host "Solde fin partie 1 : $balance1 FCFA"
Write-Host "Solde reconnexion  : $balance2 FCFA (serveur de jeu redémarré entre-temps)"
Write-Host "Pseudo retrouvé    : $pseudoBack"
Write-Host "Logs               : $outDir"

$ok = ($balance1 -ne $null) -and ($balance1 -ne 5000) -and ($balance1 -eq $balance2) -and $pseudoBack
if ($ok) { Write-Host "RÉUSSI"; exit 0 } else { Write-Host "ÉCHEC"; exit 1 }
