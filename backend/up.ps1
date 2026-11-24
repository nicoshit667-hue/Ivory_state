<#
.SYNOPSIS
    Démarre le backend (Nakama + PostgreSQL) de cette copie, sans jamais toucher à un Nakama déjà en route.

.DESCRIPTION
    Plusieurs copies du projet existent sur ce PC (OneDrive\Desktop\ivory, Downloads\ivory). Elles
    partagent les ports 7350/7351 : un seul Nakama peut tourner. Si un Nakama répond déjà sur
    http://127.0.0.1:7350 (quelle que soit la copie qui l'a lancé), ce script le réutilise et s'arrête
    là. Sinon il lance la pile de cette copie (projet Compose « ivory-onedrive-backend ») puis attend
    qu'elle soit en bonne santé.
#>
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Test-Nakama {
    try {
        Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:7350/healthcheck' -TimeoutSec 3 | Out-Null
        return $true
    } catch { return $false }
}

if (Test-Nakama) {
    $owner = docker ps --filter 'publish=7350' --format '{{json .}}' 2>$null | ForEach-Object {
        $c = $_ | ConvertFrom-Json
        $dir = ($c.Labels -split ',' | Where-Object { $_ -like 'com.docker.compose.project.working_dir=*' }) -replace '^[^=]*=', ''
        "$($c.Names) ($dir)"
    }
    Write-Host "Nakama tourne déjà sur le port 7350 : $owner"
    Write-Host "Rien à lancer : le serveur de jeu de cette copie l'utilisera (mêmes clés dans .env)."
    exit 0
}

# Port occupé mais Nakama muet (conteneur en cours de redémarrage) : ne pas s'en mêler.
$busy = docker ps --filter 'publish=7350' --format '{{.Names}}' 2>$null
if ($busy) {
    Write-Host "Le port 7350 est pris par $busy mais Nakama ne répond pas encore. Attends ou vérifie : docker logs $busy"
    exit 2
}

if (-not (Test-Path '.env')) {
    Write-Host "Fichier .env manquant : lance d'abord  powershell -File setup-keys.ps1"
    exit 2
}

docker compose up -d
$deadline = (Get-Date).AddSeconds(90)
while (-not (Test-Nakama)) {
    if ((Get-Date) -gt $deadline) { throw "Nakama n'a pas démarré (docker compose logs nakama)" }
    Start-Sleep -Seconds 2
}
Write-Host "Nakama prêt : http://127.0.0.1:7350 (console : http://127.0.0.1:7351)"
