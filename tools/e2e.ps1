<#
.SYNOPSIS
    Lance tous les tests de bout en bout d'Ivory State et affiche un verdict global.

.DESCRIPTION
    Prérequis : builds « Serveur Windows » et « Client Windows » (menu Ivory State > Build).
    Pour chaque scénario : un serveur headless neuf, un ou plusieurs clients headless qui jouent seuls,
    puis lecture des lignes RÉUSSI / ÉCHEC dans leurs logs.

      economie : prendre une livraison, livrer, être payé, acheter à manger   (-e2eEconomy)
      conduite : marcher jusqu'à un taxi, monter, rouler, descendre            (-e2eDrive)
      chat     : deux joueurs proches, messages + message piégé                 (-e2eChat / -e2eChatSay)

    Code de sortie 0 si tout réussit, 1 sinon (utilisable en intégration continue).
    Logs : loadtests/e2e-<date>/ (ignoré par git).

.EXAMPLE
    powershell -File tools/e2e.ps1
#>
param([int]$Port = 7790)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'IvoryState\Builds'
$serverExe = Join-Path $builds 'Server-Windows\IvoryStateServer.exe'
$clientExe = Join-Path $builds 'Client-Windows\IvoryState.exe'
foreach ($exe in @($serverExe, $clientExe)) {
    if (-not (Test-Path $exe)) { throw "Build introuvable : $exe" }
}

$outDir = Join-Path $root ("loadtests\e2e-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $outDir | Out-Null

function Test-LogMatch([string]$path, [string]$pattern) {
    if (-not (Test-Path $path)) { return $false }
    return [bool](Select-String -Path $path -Pattern $pattern -Quiet)
}

function Start-Server([string]$name) {
    $log = Join-Path $outDir "$name-server.log"
    $process = Start-Process $serverExe -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-server', '-port', $Port, '-noLicense', '-logFile', "`"$log`"")
    $deadline = (Get-Date).AddSeconds(90)
    while (-not (Test-LogMatch $log 'taxis en station|Serveur d.marr')) {
        if ((Get-Date) -gt $deadline) { throw "Serveur non démarré ($log)" }
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Seconds 1
    return $process
}

function Start-Client([string]$name, [string[]]$extraArgs) {
    $log = Join-Path $outDir "$name.log"
    $arguments = @('-batchmode', '-nographics', '-client', '-address', '127.0.0.1', '-port', $Port,
                   '-logFile', "`"$log`"") + $extraArgs
    $process = Start-Process $clientExe -PassThru -ArgumentList $arguments
    return @{ Process = $process; Log = $log }
}

function Wait-Result([string]$log, [string]$tag, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-LogMatch $log "\[$tag\] R.USSI") { return $true }
        if (Test-LogMatch $log "\[$tag\] .CHEC") { return $false }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Stop-All($processes) {
    foreach ($p in $processes) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
    Start-Sleep -Seconds 1
}

$results = [ordered]@{}

# --- Économie ---
$server = Start-Server 'economie'
$client = Start-Client 'economie-client' @('-e2eEconomy')
$results['Économie (livraison, paie, achat)'] = Wait-Result $client.Log 'E2E' 400
Stop-All @($client.Process, $server)

# --- Conduite ---
$server = Start-Server 'conduite'
$client = Start-Client 'conduite-client' @('-e2eDrive')
$results['Conduite (taxi : monter, rouler, descendre)'] = Wait-Result $client.Log 'E2E-DRIVE' 180
Stop-All @($client.Process, $server)

# --- Chauffeur de taxi ---
$server = Start-Server 'course'
$client = Start-Client 'course-client' @('-e2eFare')
$results['Chauffeur de taxi (client pris, déposé, payé)'] = Wait-Result $client.Log 'E2E-FARE' 330
Stop-All @($client.Process, $server)

# --- Transfert d'argent entre joueurs ---
$server = Start-Server 'transfert'
$receiver = Start-Client 'transfert-recoit' @('-e2eTransfer', '-pseudo', 'Adjoua')
Start-Sleep -Seconds 4
$sender = Start-Client 'transfert-envoie' @('-e2eTransferSend', '-pseudo', 'Yao')
$deadline = (Get-Date).AddSeconds(90)
$transferOk = $false
while ((Get-Date) -lt $deadline) {
    # « .+ » plutôt que des accents : l'encodage des logs peut varier.
    $received = Test-LogMatch $receiver.Log '\[E2E-TRANSFER\] (Pingoin : )?Yao t.a envoy.+1 000 FCFA'
    $sentOk = Test-LogMatch $sender.Log '\[E2E-TRANSFER\] (Pingoin : )?[Tt]u as envoy.+1 000 FCFA.+Adjoua'
    if ($received -and $sentOk) { $transferOk = $true; break }
    Start-Sleep -Seconds 2
}
$results['Transfert entre joueurs (Yao -> Adjoua, 1 000 FCFA)'] = $transferOk
Stop-All @($receiver.Process, $sender.Process, $server)

# --- Téléphone : appel vocal entre deux joueurs ---
$server = Start-Server 'telephone'
$callee = Start-Client 'telephone-decroche' @('-e2ePhoneAnswer', '-pseudo', 'Awa')
Start-Sleep -Seconds 4
$caller = Start-Client 'telephone-appelle' @('-e2ePhoneCall', 'Awa', '-pseudo', 'Ibrahim')
# 150 s : demande d'ami (appels réservés aux amis) avant l'appel.
$results['Téléphone (appel, voix, raccrocher)'] = Wait-Result $callee.Log 'E2E-PHONE' 150
Stop-All @($callee.Process, $caller.Process, $server)

# --- Chat ---
$server = Start-Server 'chat'
# Les deux demandent le même pseudo : le serveur doit renommer le second « Kouassi2 ».
$listener = Start-Client 'chat-ecoute' @('-e2eChat', '-pseudo', 'Kouassi')
Start-Sleep -Seconds 4
$speaker = Start-Client 'chat-parole' @('-e2eChat', '-e2eChatSay', '-pseudo', 'Kouassi')
$deadline = (Get-Date).AddSeconds(90)
$chatOk = $false
$namesOk = $false
while ((Get-Date) -lt $deadline) {
    $received = @(Select-String -Path $listener.Log -Pattern '\[E2E-CHAT\] re.u' -ErrorAction SilentlyContinue)
    if ($received.Count -ge 3) {
        $text = ($received | ForEach-Object { $_.Line }) -join "`n"
        # Message piégé nettoyé, et message trop long coupé à 120 caractères.
        $chatOk = ($text -match ': balise et espaces') -and ($text -match '\(120 car\.\)') -and ($text -notmatch '<b>')
        $namesOk = $text -match 're.u de Kouassi2 '
        break
    }
    Start-Sleep -Seconds 2
}
$results['Chat (proximité, nettoyage, longueur)'] = $chatOk
$results['Pseudos (choix, unicité : Kouassi -> Kouassi2)'] = $namesOk
Stop-All @($listener.Process, $speaker.Process, $server)

# --- Profil, Commerces & Taxi ---
$server = Start-Server 'profil-commerces'
$hostClient = Start-Client 'profil-host' @('-e2eProfileHost', '-pseudo', 'KoffiProf')
Start-Sleep -Seconds 4
$viewerClient = Start-Client 'profil-viewer' @('-e2eProfileViewer', 'KoffiProf', '-pseudo', 'AwaView')
$results['Profil (statut, vues, masquage), Commerces & Taxi'] = Wait-Result $viewerClient.Log 'E2E-PROFILE' 120
Stop-All @($hostClient.Process, $viewerClient.Process, $server)

# --- Verdict ---
Write-Host ""
Write-Host "=== Tests de bout en bout Ivory State ==="
$allOk = $true
foreach ($entry in $results.GetEnumerator()) {
    $status = if ($entry.Value) { 'RÉUSSI' } else { 'ÉCHEC ' }
    if (-not $entry.Value) { $allOk = $false }
    Write-Host ("  [{0}] {1}" -f $status, $entry.Key)
}
Write-Host ""
Write-Host "Logs : $outDir"
if ($allOk) { Write-Host "Tout est vert."; exit 0 } else { Write-Host "Au moins un échec."; exit 1 }
