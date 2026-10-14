# KroolWorldUI

BepInEx 5 plugin for Valheim, shipped to Thunderstore as `Krool-Krool_World_UI` (team `Krool`).
Client-side, display-only HUD features, each behind its own `Enabled` config toggle.

## Rules

- **Never write game state.** Read skills/inventory/etc. for display only. `Skills.GetSkill()`
  creates a missing skill (which then saves to the character); use `SkillXpBars.FindExisting`
  (walks `GetSkillList()`) instead.
- **Patches must not throw into game code.** Wrap UI work called from Harmony postfixes in
  try/catch and log.
- **Test in the `KroolDev` r2modman profile**, never `BCCBoys` (the owner's play profile).
  `scripts\deploy-dev.ps1` targets KroolDev by default.
- **Version lives in three places:** `thunderstore/manifest.json`, `Plugin.Version`, and the
  CHANGELOG. `build.ps1` refuses to package if the first two differ.

## Layout

- `src/KroolWorldUI/Plugin.cs` - entry point; each feature exposes `BindConfig` and Harmony patches.
- `src/KroolWorldUI/SettingsWindow.cs` - built-in F7 settings window (IMGUI). It renders every
  config entry generically, so a new feature only needs `config.Bind` with an `AcceptableValueRange`
  for numbers to get a slider. No dependency on ConfigurationManager (Azumatt's is deprecated).
- `src/KroolWorldUI/Features/` - one file per feature.
- `thunderstore/` - manifest.json and icon.png (256x256). Package = these + DLL + README + CHANGELOG.

## Gotchas

- Target `netstandard2.1`: Valheim's Unity DLLs reference netstandard 2.1; `net462` fails to build.
- Game DLLs are referenced from the Steam install (`-p:ValheimDir=...` to override); BepInEx and
  HarmonyX come from nuget.bepinex.dev.
- HUD placement: the owner's mods (ExtraSlots panels, EpicLoot ability bar) all live bottom-left.
  Keep new HUD elements off that corner. Taken so far: skill XP bars (right side, mid-height),
  weight bar (slim, directly above the top-most HotkeyBar, matched to its width). Below the
  hotbar is the raven tutorial text area: the owner saw overlap there, avoid it.
- The skill XP bar clones `InventoryGui.instance.m_skillsDialog.m_elementPrefab` and finds
  children by name (`icon`, `name`, `leveltext`, `bonustext`, GuiBars). The first spawn logs the
  prefab size/anchors to `BepInEx/LogOutput.log`; check it after a Valheim update.
