# Offline Companions Reborn

Source for the Valheim mod **Offline Companions Reborn** (BepInEx), a Valheim 1.0 update of
[ProfMags' Offline Companions](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/).
The player-facing description is in [`package/README.md`](package/README.md).

## Layout

| Path | What it is |
|---|---|
| `src/Companions/` | The mod: `Companions.csproj` and the C# source, one file per class (folders below) |
| `src/Companions/Resources/Textures/UI/` | UI textures embedded in the DLL |
| `package/` | Thunderstore/Hexium package files: `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, plus the player docs `GUIDE.md` and `CONFIGURATION.md` |
| `tools/gen_config_doc.py` | Regenerates `package/CONFIGURATION.md` from the settings in `ModConfig.cs` |
| `tools/readme_to_nexus.py` | Converts `package/README.md` into Nexus Mods BBCode for the mod page description (`[spoiler]` sections) |
| `docs/DEV_NOTES.md` | Development history, design notes and backlog |
| `Environment.props.example` | Template for your local paths (Valheim install, deploy folder) |

### Source folders (`src/Companions/`)

| Folder | What's in it |
|---|---|
| `Core/` | Plugin entry point, config, localization, speech config, shared helpers (reflection, textures, input) |
| `Companion/` | The companion itself: setup, spawning/prefabs, appearance, tiers, skills, stamina, food, rest, talk, death/respawn |
| `AI/` | Movement and combat AI: `CompanionAI`, `CompanionCombatAI`, weapon/shield choice, torch opener, water safety, doors, `MHR_Mover` |
| `Jobs/` | Work modes: harvest (wood/stone/ore/forage), farm, fish, hunt, cook, smelt, repair (gear), repair and restock (buildings, fires, chest sorting), plus gather/seed rules and the tool keeper |
| `UI/` | Panels, radial menu, HUD, config panel, zone placers, font and inventory-grid fixes |
| `Patches/` | Harmony patches on vanilla game classes |
| `Diagnostics/` | Debug logging helpers (`[AIGate]`, `[EQUIPDIAG]`, `[GRIDDIAG]`, `[UiDiag]`, `[Diag]`) |

All files share the `Companions` namespace; the folders are only for finding things.

## Building

Requirements: the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer), or Visual Studio 2022 / Rider,
and Valheim with BepInEx installed. The project builds against the DLLs in your own Valheim install;
no game files are kept in this repository.

1. Copy `Environment.props.example` to `Environment.props` and set `ValheimDir` to your Valheim folder.
   (If Valheim is in the default Steam location you can skip this.)
   Optionally set `DeployDir` to have each build copied straight into a `BepInEx\plugins` folder.
2. Build:
   ```
   dotnet build -c Release
   ```
   or open `Companions.sln` in Visual Studio and build.
3. The mod is at `src/Companions/bin/Release/net472/Companions.dll`.

The game references used are `valheim_Data/Managed` (`assembly_valheim`, `assembly_utils`,
`assembly_guiutils`, `Splatform`, `Unity.TextMeshPro`, `UnityEngine.*`) and `BepInEx/core`
(`BepInEx`, `0Harmony`).

## Releasing

1. Bump `PluginVersion` in `src/Companions/Core/CompanionsPlugin.cs` and `version_number` in `package/manifest.json`.
2. Add the changelog entry to `package/CHANGELOG.md` and update "Recent Highlights" in `package/README.md`.
   If settings changed, regenerate the settings reference: `python3 tools/gen_config_doc.py src/Companions/Core/ModConfig.cs package/CONFIGURATION.md`. Update `package/GUIDE.md` for behaviour changes.
3. Build in Release and zip `Companions.dll` with the files in `package/`.

## License

MIT, see [LICENSE](LICENSE). Original mod and concept by ProfMags.
