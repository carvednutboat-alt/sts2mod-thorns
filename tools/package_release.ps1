$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = Join-Path $ProjectRoot "thorns.json"
$Manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$Version = $Manifest.version
$DistDir = Join-Path $ProjectRoot "dist"
$ZipPath = Join-Path $DistDir "thorns-v$Version.zip"
$Godot = "E:\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe"

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

dotnet build (Join-Path $ProjectRoot "thorns.csproj")
& $Godot --headless --path $ProjectRoot --script "res://tools/pack_thorns_pck.gd"

if (Test-Path $ZipPath) {
	Remove-Item -LiteralPath $ZipPath -Force
}

Compress-Archive -LiteralPath @(
	(Join-Path $ProjectRoot "thorns.json"),
	(Join-Path $ProjectRoot "thorns.dll"),
	(Join-Path $ProjectRoot "thorns.pck")
) -DestinationPath $ZipPath

Write-Host "Created $ZipPath"
