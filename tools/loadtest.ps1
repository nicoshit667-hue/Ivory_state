<#
.SYNOPSIS
    Test de charge local d'Ivory State : serveur headless + bots serveur + clients bots headless.

.DESCRIPTION
    Prérequis : builds "Serveur Windows (test local)" et "Client Windows (test local)"
    (menu Ivory State > Build dans Unity).

    - Le serveur simule -ServerBots personnages (même code que les vrais joueurs).
    - -Clients vrais clients headless se connectent et mesurent ce qu'ils reçoivent.
    - Le script échantillonne le CPU et la mémoire du processus serveur.

    Résultats : loadtests/<date>-<label>/ (logs + summary.txt), ignoré par git.

.EXAMPLE
    .\tools\loadtest.ps1 -ServerBots 96 -Clients 4 -Spread 0 -Label foule
    .\tools\loadtest.ps1 -ServerBots 96 -Clients 4 -Spread 180 -Label disperse
#>
param(
    [int]$ServerBots = 96,
    [int]$Clients = 4,
    [double]$Spread = 0,
    [int]$Duration = 60,
    [int]$Port = 7770,
    [int]$MaxVisible = -1,   # -1 = valeur de la NetworkConfig, 0 = pas de plafond
    [switch]$GhostPlayers, # joueurs et véhicules se traversent (ancien mode, comparaison)
    [string]$Label = "test"
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

$root = Split-Path $PSScriptRoot -Parent
$builds = Join-Path $root 'IvoryState\Builds'
$serverExe = Join-Path $builds 'Server-Windows\IvoryStateServer.exe'
$clientExe = Join-Path $builds 'Client-Windows\IvoryState.exe'

foreach ($exe in @($serverExe, $clientExe)) {
    if (-not (Test-Path $exe)) { throw "Build introuvable : $exe. Lance d'abord Ivory State > Build dans Unity." }
}

$outDir = Join-Path $root ("loadtests\{0}-{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $Label)
New-Item -ItemType Directory -Force $outDir | Out-Null
$spreadArg = $Spread.ToString($inv)
$serverLog = Join-Path $outDir 'server.log'
$processes = @()

function Count-LogMatches([string]$path, [string]$pattern) {
    # Le log n'existe pas encore pendant les premières secondes du démarrage d'un processus.
    if (-not (Test-Path $path)) { return 0 }
    @(Select-String -Path $path -Pattern $pattern).Count
}

function Read-Stats([string]$path, [string]$tag) {
    # Renvoie une liste de tables clé=valeur pour chaque ligne "[tag] k=v k=v ...".
    # StartsWith et non -like : dans -like, [tag] serait lu comme une classe de caractères.
    if (-not (Test-Path $path)) { return @() }
    Get-Content $path | Where-Object { $_.StartsWith("[$tag] ") } | ForEach-Object {
        $row = @{}
        foreach ($m in [regex]::Matches($_, '(\w+)=([-\d.]+)')) {
            $row[$m.Groups[1].Value] = [double]::Parse($m.Groups[2].Value, $inv)
        }
        $row
    }
}

function Avg($rows, [string]$key) {
    $values = @($rows | ForEach-Object { $_[$key] } | Where-Object { $_ -ne $null })
    if ($values.Count -eq 0) { return [double]::NaN }
    ($values | Measure-Object -Average).Average
}

function Max($rows, [string]$key) {
    $values = @($rows | ForEach-Object { $_[$key] } | Where-Object { $_ -ne $null })
    if ($values.Count -eq 0) { return [double]::NaN }
    ($values | Measure-Object -Maximum).Maximum
}

try {
    Write-Host "Serveur : $ServerBots bots, rayon $Spread m..."
    $serverArgs = @('-batchmode', '-nographics', '-server', '-port', $Port,
        '-serverBots', $ServerBots, '-botSpread', $spreadArg, '-logFile', "`"$serverLog`"")
    # -maxVisible n'est transmis que s'il est précisé : sinon la NetworkConfig s'applique.
    if ($MaxVisible -ge 0) { $serverArgs += @('-maxVisible', $MaxVisible) }
    $extraClientArgs = @()
    if ($GhostPlayers) { $serverArgs += '-ghostPlayers'; $extraClientArgs += '-ghostPlayers' }
    $server = Start-Process $serverExe -PassThru -ArgumentList $serverArgs
    $processes += $server

    $deadline = (Get-Date).AddSeconds(90)
    while ((Count-LogMatches $serverLog 'Serveur démarré') -eq 0) {
        if ((Get-Date) -gt $deadline) { throw "Le serveur n'a pas démarré (voir $serverLog)." }
        Start-Sleep -Milliseconds 500
    }

    Write-Host "Clients : $Clients bots headless..."
    for ($i = 1; $i -le $Clients; $i++) {
        $log = Join-Path $outDir "client_$i.log"
        $clientArgs = @('-batchmode', '-nographics', '-client', '-bot', '-address', '127.0.0.1', '-port', $Port,
            '-botSpread', $spreadArg, '-logFile', "`"$log`"") + $extraClientArgs
        $processes += Start-Process $clientExe -PassThru -ArgumentList $clientArgs
        Start-Sleep -Seconds 1
    }

    # Attendre que tous les clients aient leur personnage, puis laisser la situation se stabiliser.
    $deadline = (Get-Date).AddSeconds(120)
    while ((Count-LogMatches $serverLog 'Joueur créé') -lt $Clients) {
        if ((Get-Date) -gt $deadline) { Write-Warning "Tous les clients ne se sont pas connectés."; break }
        Start-Sleep -Seconds 1
    }
    $statsLinesBefore = Count-LogMatches $serverLog '^\[Stats\]'
    Start-Sleep -Seconds 10

    Write-Host "Mesure pendant $Duration s..."
    $cpuSamples = @()
    $memSamples = @()
    $server.Refresh()
    $lastCpu = $server.TotalProcessorTime
    $lastTime = Get-Date
    $end = (Get-Date).AddSeconds($Duration)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds 5
        $server.Refresh()
        $now = Get-Date
        $cpuSamples += 100 * ($server.TotalProcessorTime - $lastCpu).TotalSeconds / ($now - $lastTime).TotalSeconds
        $memSamples += $server.WorkingSet64 / 1MB
        $lastCpu = $server.TotalProcessorTime
        $lastTime = $now
    }
}
finally {
    foreach ($p in $processes) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
}

Start-Sleep -Seconds 1

# Lignes de stats serveur produites pendant la mesure uniquement (après stabilisation).
$serverStats = @(Read-Stats $serverLog 'Stats' | Select-Object -Skip ($statsLinesBefore + 2))
$clientStats = @()
for ($i = 1; $i -le $Clients; $i++) {
    $clientStats += @(Read-Stats (Join-Path $outDir "client_$i.log") 'ClientStats' | Select-Object -Skip 2)
}

$players = $ServerBots + $Clients
$recvKb = Avg $clientStats 'recu_ko_s'
$recvPkt = Avg $clientStats 'recu_pkt_s'
# Trafic utile + en-têtes IP/UDP (28 octets par paquet), non comptés par le transport.
$recvKbWire = $recvKb + ($recvPkt * 28 / 1024)

$summary = @"
=== Test de charge Ivory State : $Label ===
Joueurs simulés     : $players ($ServerBots bots serveur + $Clients clients réels)
Rayon de dispersion : $Spread m (0 = foule serrée)
Plafond de visibles : $(if ($MaxVisible -lt 0) { 'NetworkConfig' } elseif ($MaxVisible -eq 0) { 'aucun' } else { $MaxVisible })
Durée de mesure     : $Duration s

--- Serveur ---
CPU moyen / max     : {0:0}% / {1:0}% d'un cœur
Mémoire (max)       : {2:0} Mo
FPS moyen           : {3:0.0} (cible 60 : 2 frames par tick)
Frame la plus longue: {4:0} ms
Ticks/s             : {5:0.0} (cible 30)
Trafic entrant      : {6:0.0} Ko/s

--- Par client (ce que reçoit un téléphone) ---
Joueurs visibles    : {7:0.0} sur $players
Reçu                : {8:0.0} Ko/s ({9:0} paquets/s), soit ~{10:0.0} Ko/s avec en-têtes IP/UDP
Coût par joueur vu  : {14:0.00} Ko/s (indicateur stable d'un test à l'autre)
Volume par heure    : ~{11:0} Mo

--- Fluidité (erreur de prédiction du personnage local) ---
Erreur moyenne      : {15:0.0} cm (0 = client et serveur parfaitement d'accord)
Erreur max          : {16:0.0} cm
Corrections > 5 cm  : {17:0.0} par client et par 5 s

--- Extrapolation ---
Sortant serveur si les $players joueurs étaient réels : ~{12:0.0} Mo/s (~{13:0} Mbit/s)
"@ -f (($cpuSamples | Measure-Object -Average).Average), (($cpuSamples | Measure-Object -Maximum).Maximum),
      (($memSamples | Measure-Object -Maximum).Maximum),
      (Avg $serverStats 'fps'), (Max $serverStats 'frame_max_ms'), (Avg $serverStats 'ticks'),
      (Avg $serverStats 'entrant_ko_s'),
      (Avg $clientStats 'visibles'), $recvKb, $recvPkt, $recvKbWire, ($recvKbWire * 3600 / 1024),
      ($recvKbWire * $players / 1024), ($recvKbWire * $players * 8 / 1024),
      ($recvKb / [Math]::Max(1, (Avg $clientStats 'visibles'))),
      (Avg $clientStats 'erreur_moy_cm'), (Max $clientStats 'erreur_max_cm'), (Avg $clientStats 'corrections_visibles')

$summary | Tee-Object -FilePath (Join-Path $outDir 'summary.txt')
Write-Host "`nLogs : $outDir"
