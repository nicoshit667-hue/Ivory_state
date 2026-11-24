<#
.SYNOPSIS
    Crée backend/.env avec des secrets aléatoires (une seule fois ; ne remplace pas un .env existant).

.DESCRIPTION
    - POSTGRES_PASSWORD, SESSION_*_KEY, CONSOLE_PASSWORD : secrets locaux, jamais versionnés (.env est ignoré par git).
    - NAKAMA_HTTP_KEY : clé SECRÈTE du serveur de jeu pour appeler les RPC serveur à serveur (portefeuille).
      Elle ne doit JAMAIS être embarquée dans l'application mobile.
    - NAKAMA_SERVER_KEY : clé « publique » d'authentification des clients, embarquée dans l'app
      (NetworkConfig > Nakama Server Key). Valeur de développement : ivory-dev-key.
#>
$envFile = Join-Path $PSScriptRoot '.env'
if (Test-Path $envFile) {
    Write-Host ".env existe déjà : rien à faire ($envFile)."
    exit 0
}

function New-Secret([int]$bytes = 24) {
    $buffer = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return ([Convert]::ToBase64String($buffer)).Replace('+', 'A').Replace('/', 'B').Replace('=', '')
}

$lines = @(
    "POSTGRES_PASSWORD=$(New-Secret)",
    "SESSION_ENCRYPTION_KEY=$(New-Secret 32)",
    "SESSION_REFRESH_KEY=$(New-Secret 32)",
    "CONSOLE_PASSWORD=$(New-Secret 12)",
    "NAKAMA_HTTP_KEY=$(New-Secret 32)",
    "NAKAMA_SERVER_KEY=ivory-dev-key"
)
[IO.File]::WriteAllLines($envFile, $lines)
Write-Host "Secrets générés dans $envFile"
Write-Host "Console d'administration : http://127.0.0.1:7351 (utilisateur admin, mot de passe dans .env)"
