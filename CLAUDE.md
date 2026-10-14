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
- **Every change to the mod gets a new version and a commit (owner, 2026-10-14).** Bump patch
  for fixes and tweaks, minor for a new feature; add the `## x.y.z` CHANGELOG entry and run
  `release.ps1 -Version x.y.z` (add `-Publish` once the token exists), which commits, tags and
  pushes. Never ship two different builds under one version number.
- **Releases go through `scripts\release.ps1 -Version x.y.z [-Publish]`.** It syncs the version
  in `thunderstore/manifest.json`, `thunderstore.toml` and `Plugin.Version` (the csproj gets it
  from build.ps1), requires a `## x.y.z` CHANGELOG section, builds, uploads with tcli, then
  commits, tags `vx.y.z` (annotated) and pushes. Write the changelog entry before running it.
  r2modman only offers updates for a strictly higher version of the same Thunderstore package.
- **The Thunderstore token is `TCLI_AUTH_TOKEN` (user env var).** Never write it to a file or
  echo it; the owner sets it with `setx` in their own terminal.

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
