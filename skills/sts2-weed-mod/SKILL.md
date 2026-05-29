---
name: sts2-weed-mod
description: Use when working on this local Reed mod project for Slay the Spire 2, including the custom character, starter deck, cards, relics, BaseLib usage, manifest, build output, and install notes.
metadata:
  short-description: Reed mod project memory
---

# STS2 Reed Mod

Project root: `E:/Godot/sts2mod/weed`

Reference-only decompiled game project: `E:/Godot/sts2`

Do not edit `E:/Godot/sts2` for mod content. Do not edit external Codex skill directories for this project. Keep project memory under this `weed` folder.

## Current Files

- `src/WeedContent.cs`: all current content classes. Runtime IDs now use `reed`; the existing `Weed` namespace and pool class names are intentionally retained for compatibility with current content IDs.
- `reed.csproj`: uses `Microsoft.NET.Sdk`, references `sts2`, `BaseLib`, `0Harmony`, and `GodotSharp`, and copies built `reed.dll` to the project root. If Godot import/export rewrites it to `Godot.NET.Sdk/4.5.1`, restore `Microsoft.NET.Sdk`.
- `reed.json`: runtime manifest, depends on `BaseLib`. Current version: `0.1.22`, `has_pck: true`.
- `reed.dll`: runtime DLL copied to the game mod folder.
- `reed.pck`: runtime PCK for the Reed Spine visual.
- `tools/pack_reed_pck.gd`: the retained PCK packer. Other Spine inspection scripts were cleanup-only and have been removed.
- `CODEX_NOTES.md`: short project notes.

## Current Content

- Character: `Herbalist`, displayed as `Reed`.
- Base class: `BaseLib.Abstracts.PlaceholderCharacterModel`.
- Custom visual hooks: `Herbalist.CustomVisualPath`, `CustomMerchantAnimPath`, `CustomRestSiteAnimPath`, and `CustomCharacterSelectBg` check custom scenes/resources before returning Reed scenes; otherwise they fall back to Ironclad. With `reed.pck`, `scenes/creature_visuals/herbalist.tscn`, `scenes/merchant/characters/herbalist_merchant.tscn`, and `scenes/rest_site/characters/herbalist_rest_site.tscn` use `spine_runtime_loader.gd` to manually load the Spine 4.2 assets. `scenes/screens/char_select/char_select_bg_herbalist.tscn` uses `images/packed/character_select/reed_character_select_bg.png`, a 2560x1200 transparent canvas with the original 1920x1139 Reed JPG pasted unscaled at `(120, 30)`.
- Starter deck: 4x `Weed.Strike`, 4x `Weed.Defend`, 1x `Weed.HerbalGuard`, 1x `Weed.Scorch`. The `Weed.*` content IDs are retained intentionally even though the runtime mod ID is now `reed`.
- Starter card portraits:
  - `Strike`: `images/packed/card_portraits/weed/strike.png`
  - `Defend`: `images/packed/card_portraits/weed/defend.png`
  - `HerbalGuard`: `images/packed/card_portraits/weed/herbal_guard.png`
  - `Scorch`: `images/packed/card_portraits/weed/scorch.png`
- Character-select background: `scenes/screens/char_select/char_select_bg_herbalist.tscn`, using `images/packed/character_select/reed_character_select_bg.png` with transparent outer padding and no source-image upscaling.
- Character/avatar images derived from `original_resources/9903D60B5F1A8BFC64CDDF8FBF5015EB.jpg`:
  - Character-select button: `images/packed/character_select/char_select_reed.png`
  - Locked character-select button: `images/packed/character_select/char_select_reed_locked.png`
  - Top-panel/continue-run icon: `images/packed/ui/top_panel/character_icon_reed.png`
  - Icon outline/mask: `images/packed/ui/top_panel/character_icon_reed_outline.png`
- Starter relic: `Weed.SeedCache`.
- `LeafCut`, `Gleam`, and `LifeSpark` remain in the reward pool.
- Harmony patches:
  - `TheArchitectDefineDialoguesPatch`: adds default repeating Architect ending dialogue for `Herbalist`.
  - `HerbalistTreasureSkipPatch`: prevents Herbalist treasure rooms from using the skip/proceed action while a chest relic is still available.
  - `ReedCharacterIconTexturePatch` / `ReedCharacterIconOutlineTexturePatch`: use Reed's custom 88x88 character icon in UI locations that call `CharacterModel.IconTexture` / `IconOutlineTexture`.
  - `ReedMerchantCharacterReadyPatch` / `ReedRestSiteCharacterReadyPatch`: use Reed's `Idle` Spine animation in merchant/rest-site scenes instead of vanilla `relaxed_loop` / act rest animations.

## Asset Notes

- PRTS page `https://prts.wiki/w/焰影苇草/spine` listed Spine 3.8 assets only; tested `spine42`, `spine41`, `spine40`, and `spine4` URLs returned 404.
- STS2's Godot Spine extension uses a Spine 4.2 runtime; the PRTS 3.8 skeleton cannot be used directly.
- Current working chain:
  - Keep original/reference Spine 3.8 files under `original_resources/spine/reed2/`.
  - Runtime atlas/texture: `animations/characters/herbalist/char_1020_reed2.atlas` and `.png`.
  - Required Godot texture import resources: `animations/characters/herbalist/char_1020_reed2.png.import` and `.godot/imported/char_1020_reed2.png-78bd075b54d42cac2899ecce08d59e0b.ctex`.
  - Converted Spine 4.2 skeleton: `animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2.skel`.
  - Retained 4.2 project source: `animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2_from38.spine`.
- Static `SpineSkeletonDataResource` `.tres` loading still fails with `Error reading attachment: Box`; runtime loading must use `SpineAtlasResource.load_from_atlas_file(...)` and `SpineSkeletonFileResource.load_from_file(...)` from `scenes/creature_visuals/spine_runtime_loader.gd`.
- Build `reed.pck` with `tools/pack_reed_pck.gd`, not the normal Godot export preset. Raw PNG alone made exported STS2 log `No loader found for resource: ...char_1020_reed2.png` and crash after combat scene creation.
- Card portrait PNGs also need their `.import` files and imported `.ctex` files in `reed.pck`. Runtime starter-card portraits live under `images/packed/card_portraits/weed/`.
- PRTS/Arknights assets are copyrighted by Hypergryph/Yostar; treat them as local test/reference assets, not redistributable mod content.

## Reuse Rules

- Add cards with `CustomCardModel` and `[Pool(typeof(WeedCardPool))]`.
- For Chinese-named content, keep the in-game `Localization` title in Chinese but use an English translated ASCII class/code name.
- Add powers with `CustomPowerModel`; do not add pool attributes to powers.
- For multiplayer-only cards, override `CardMultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly`.
- When changing a card effect, temporarily add that card to `Herbalist.StartingDeck` for testing and remove it after verification.
- Do not leave `WeedCardPool` with only `CardRarity.Basic` cards.
- Merchant character-card slots are fixed as `Attack, Attack, Skill, Skill, Power`; keep enough non-Basic card coverage.
- Keep the Architect dialogue and treasure skip patches unless replacing them with fuller fixes.
- Add relics with `CustomRelicModel` and `[Pool(typeof(WeedRelicPool))]`.
- Keep simple text in each model's `Localization` property until a real localization workflow is needed.
- Avoid `{DynamicVar}` placeholders in custom Power `description` entries used by hover tips.

## Build

```powershell
dotnet build E:\Godot\sts2mod\weed\reed.csproj
```

Build the Spine PCK:

```powershell
& 'E:\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path 'E:\Godot\sts2mod\weed' --script 'res://tools/pack_reed_pck.gd'
```

Install only these runtime files to `...\Slay the Spire 2\mods\reed\`:

```text
reed.json
reed.dll
reed.pck
```

Do not copy the whole source project into the game mods folder because STS2 scans `.json` recursively.
