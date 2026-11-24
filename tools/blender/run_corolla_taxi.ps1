$blender = "C:\Program Files\Blender Foundation\Blender 5.3\blender.exe"
$script = Join-Path $PSScriptRoot "vehicles\corolla_taxi.py"
& $blender --background --python $script
