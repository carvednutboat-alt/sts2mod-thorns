$ErrorActionPreference = "Stop"

$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ManifestPath = Join-Path $ProjectRoot "reed.json"
$ProjectFile = Join-Path $ProjectRoot "reed.csproj"
$DllPath = Join-Path $ProjectRoot "reed.dll"
$PckPath = Join-Path $ProjectRoot "reed.pck"
$GodotExe = "E:\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe"

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$version = $manifest.version
$distDir = Join-Path $ProjectRoot "dist"
$zipPath = Join-Path $distDir "reed-v$version.zip"

dotnet build $ProjectFile
& $GodotExe --headless --path $ProjectRoot --script "res://tools/pack_reed_pck.gd"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

if (Test-Path -LiteralPath $zipPath) {
	Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -LiteralPath @($ManifestPath, $DllPath, $PckPath) -DestinationPath $zipPath

Write-Host "Release package created: $zipPath"
