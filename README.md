# STS2 Thorns Mod

A Slay the Spire 2 mod that adds **Thorns**, an Arknights-inspired character with three build archetypes: Alchemy Units, Neural Damage, and Pure Attack.

## Project Structure

```
├── src/
│   ├── ThornsCards.cs      # All 86 card implementations + custom powers
│   └── ThornsContent.cs    # Character model, starting deck, relic, patches
├── scenes/                 # Godot scenes (character visuals, UI, select screen)
├── images/packed/          # Game textures (icons, character select)
├── animations/             # Spine skeleton animation assets
├── tools/
│   ├── pack_thorns_pck.gd  # Godot script to build the .pck resource pack
│   ├── package_release.ps1 # PowerShell release packaging script
│   └── sync_thorns_cards.ps1
├── addons/spine/           # Spine runtime extension for Godot
├── thorns.csproj           # .NET 9.0 C# project
├── thorns.json             # Mod manifest
├── project.godot           # Godot project configuration
└── 棘刺卡片表_新版设计_v4_平衡校准.csv  # Card design spreadsheet (86 cards)
```

## Build

```powershell
# Build DLL
dotnet build thorns.csproj -c Debug

# Build PCK (requires Godot 4.5.1)
& "Godot_v4.5.1-stable_mono_win64_console.exe" --headless --path . --script "res://tools/pack_thorns_pck.gd"
```

## Install

Copy these three files into `Slay the Spire 2\mods\thorns\`:
- `thorns.dll`
- `thorns.json`
- `thorns.pck`

Requires [BaseLib](https://github.com/carvednutboat-alt/sts2mod-thorns) in `mods\BaseLib\`.

## Archetypes

| Archetype | Cards | Mechanic |
|---|---|---|
| **Alchemy Unit** | ~35 | Summon 1HP units that pulse (catalyst + poison) and release on death |
| **Neural Damage** | ~20 | Accumulate damage counter on enemies; at threshold, next attack deals 0 |
| **Pure Attack** | ~15 | Strength scaling, multi-hit attacks, Thorns retaliation |
