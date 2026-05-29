# STS2 Reed Mod

Custom Reed character mod prototype for Slay the Spire 2.

## Repository Contents

This private learning repository contains the code, project wiring, local workflow notes, and local test assets used during development:

- `src/WeedContent.cs`: custom character, cards, powers, relic, and Harmony patches.
- `scenes/`: Godot scenes and scripts used by the mod.
- `tools/pack_reed_pck.gd`: local PCK packing script.
- `skills/sts2-weed-mod/SKILL.md`: Codex project memory for continuing this local workflow.
- `animations/`, `images/packed/`, `original_resources/`: local test assets.
- `reed.csproj`, `reed.json`, `project.godot`: project and mod metadata.

The repository intentionally does not include generated build output or Godot cache files.

## Assets

Reed visual assets currently used during local testing are derived from Arknights/PRTS resources. Keep the repository private unless the assets are replaced with redistributable originals.

Local asset paths expected by the current scenes and packer include:

- `animations/characters/herbalist/`
- `images/packed/`
- `original_resources/`

Godot-generated imported texture files under `.godot/imported/` are not tracked. Open/import the project with Godot before rebuilding `reed.pck` so the `.ctex` files referenced by `tools/pack_reed_pck.gd` are regenerated.

## Build

This project expects the local STS2/BaseLib development layout used during development:

- STS2 decompiled project/reference assemblies under `E:/Godot/sts2`
- BaseLib at `E:/Godot/sts2mod/BaseLib/BaseLib.dll`
- Godot 4.5.1 mono under `E:/Godot/Godot_v4.5.1-stable_mono_win64/...`

Build the C# assembly:

```powershell
dotnet build E:\Godot\sts2mod\weed\reed.csproj
```

Build the local PCK after Godot has imported the local assets:

```powershell
& 'E:\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path 'E:\Godot\sts2mod\weed' --script 'res://tools/pack_reed_pck.gd'
```

Install these runtime files into the game mod folder:

```text
reed.json
reed.dll
reed.pck
```

## Release Package

Runtime release artifacts should be uploaded to GitHub Releases, not committed to source:

```text
reed.json
reed.dll
reed.pck
```

Create a release zip with:

```powershell
.\tools\package_release.ps1
```

The script rebuilds `reed.dll`, rebuilds `reed.pck`, and writes `dist/reed-v<version>.zip`.
