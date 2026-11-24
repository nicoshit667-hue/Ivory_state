$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$blender = "C:\Program Files\Blender Foundation\Blender 5.3\blender.exe"
$script = Join-Path $PSScriptRoot "vehicles\corolla_taxi.py"
$unityExe = "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe"
$unityProj = Join-Path $repoRoot "IvoryState"

Write-Host "[1/2] Export Blender -> Unity FBX (LOD0 + LOD1, sans rendu Cycles)..." -ForegroundColor Cyan
& $blender --background --python $script -- --no-render
if ($LASTEXITCODE -ne 0) {
    throw "Blender export failed (exit code $LASTEXITCODE)"
}

$fbx0 = Join-Path $unityProj "Assets\_Project\Art\Models\Vehicles\VH_Taxi_Corolla.fbx"
$fbx1 = Join-Path $unityProj "Assets\_Project\Art\Models\Vehicles\VH_Taxi_Corolla_LOD1.fbx"
Write-Host "  OK : $fbx0" -ForegroundColor Green
Write-Host "  OK : $fbx1" -ForegroundColor Green

$unityRunning = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
if ($unityRunning) {
    Write-Host ""
    Write-Host "[2/2] Unity est deja ouvert !" -ForegroundColor Yellow
    Write-Host "  -> Dans Unity, clique sur : Ivory State > Setup > Mettre a jour le prefab Taxi" -ForegroundColor Yellow
    Write-Host "  -> Puis lance Play Mode > Hote pour voir le nouveau Taxi Corolla en jeu." -ForegroundColor Yellow
} elseif (Test-Path $unityExe) {
    Write-Host "[2/2] Reconstruction automatique de PF_Taxi.prefab dans Unity (batchmode)..." -ForegroundColor Cyan
    $logFile = Join-Path $repoRoot "tools\blender\output\corolla_taxi\unity_taxi_rebuild.log"
    & $unityExe -batchmode -quit -projectPath $unityProj -executeMethod IvoryState.Editor.IvoryProjectSetup.RebuildTaxiPrefab -logFile $logFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Erreur Unity (code $LASTEXITCODE). Consulte le log : $logFile" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "  OK : PF_Taxi.prefab mis a jour avec succes !" -ForegroundColor Green
    Write-Host "  Log : $logFile"
} else {
    Write-Host "[2/2] Unity.exe introuvable a l'emplacement par defaut." -ForegroundColor Yellow
    Write-Host "  -> Ouvre IvoryState dans Unity, puis clique : Ivory State > Setup > Mettre a jour le prefab Taxi" -ForegroundColor Yellow
}

$graphify = "C:\Users\david\AppData\Roaming\Python\Python313\Scripts\graphify.exe"
if (Test-Path $graphify) {
    Push-Location $repoRoot
    & $graphify update . | Out-Null
    Pop-Location
}
