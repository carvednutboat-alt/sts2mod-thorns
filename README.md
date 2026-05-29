# STS2 Thorns Mod

Standalone Thorns character mod prototype for Slay the Spire 2.

This project was split out from the combined Reed/Thorns prototype. It keeps only the Thorns character code, Thorns visual assets, and Thorns packaging workflow.

## Build

```powershell
dotnet build E:\Godot\sts2mod\thorns\thorns.csproj
```

Build the PCK after Godot has imported assets:

```powershell
& 'E:\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path 'E:\Godot\sts2mod\thorns' --script 'res://tools/pack_thorns_pck.gd'
```

Install only these runtime files into `...\Slay the Spire 2\mods\thorns\`:

```text
thorns.json
thorns.dll
thorns.pck
```
