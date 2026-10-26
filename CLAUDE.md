# KroolWorldUI

BepInEx 5 plugin for Valheim, shipped to Thunderstore as `Krool-Krool_World_UI` (team `Krool`).
Client-side, display-only HUD features, each behind its own `Enabled` config toggle.

## Rules

- **Never write game state.** Read skills/inventory/etc. for display only. `Skills.GetSkill()`
  creates a missing skill (which then saves to the character); use `SkillXpBars.FindExisting`
  (walks `GetSkillList()`) instead. One owner-approved exception (2026-10-15): Bench Pins adds a
  saved pin to the local player's own map via `Minimap.AddPin` when they build a workbench, and removes
  it (or moves it to a bench nearby) when they hammer-remove that bench (owner, 2026-10-20). A second (2026-10-19):
  Auto Repair repairs worn items when a crafting station opens, through InventoryGui's own CanRepair,
  matching RepairOneItem. Any other write needs the owner's explicit OK.
- **Patches must not throw into game code.** Wrap UI work called from Harmony postfixes in
  try/catch and log.
- **Test in the `KroolDev` r2modman profile**, never `BCCBoys` (the owner's play profile, which
  runs the Thunderstore version). `scripts\deploy-dev.ps1` builds the `Dev` configuration
  (DEV_BUILD: F7 title reads "vX dev") and installs it in KroolDev as a separate local mod,
  `Krool-Krool_World_UI_Dev`, removing the online copy from that profile so r2modman never
  updates over it. It refuses to edit the mod list while r2modman is running.
- **Every change to the mod gets a new version and a commit (owner, 2026-10-14).** Bump patch
  for fixes and tweaks, minor for a new feature; add the `## x.y.z` CHANGELOG entry and run
  `release.ps1 -Version x.y.z` (add `-Publish` once the token exists), which commits, tags and
  pushes. Never ship two different builds under one version number.
- **Releases go through `scripts\release.ps1 -Version x.y.z [-Publish]`.** It syncs the version
  in `thunderstore/manifest.json`, `thunderstore.toml` and `Plugin.Version` (the csproj gets it
  from build.ps1), requires a `## x.y.z` CHANGELOG section, builds, uploads with tcli, then
  commits, tags `vx.y.z` (annotated) and pushes. Write the changelog entry before running it.
  r2modman only offers updates for a strictly higher version of the same Thunderstore package.
- **Review the package description on every commit (owner, 2026-10-15).** r2modman's search
  matches only the package name and the description, and the name cannot change without breaking
  updates. Before committing, reread `description` in `thunderstore/manifest.json` and update it
  if a feature was added, renamed or removed: name every feature in the words players search
  for (HUD, UI, skill XP, carry weight, food timer...), max 250 characters, same text in
  `thunderstore.toml`. release.ps1 enforces the length and the match and prints it.
- **After a publish, r2modman needs a forced online-list refresh** (Settings > Refresh online
  mod list) before a new package or version shows up; its cached list lags Thunderstore. Link
  the mod as `thunderstore.io/c/valheim/p/Krool/Krool_World_UI/` - the community-less
  `/package/...` URL lands on Risk of Rain 2 and 404s.
- **release.ps1 commits only the version files, CHANGELOG and README.** Commit code changes
  yourself before running it, or the tag ships without them (0.13.0 did).
- **Every new or changed setting is designed for the F7 window, not just bound (owner, 2026-10-25).**
  Check it works with mouse, keyboard and controller; place it in a sensible order within its
  section, with grouping/gaps where that helps reading; pick the right control (slider for ranges,
  click-to-type for exact numbers, toggle for bools, cycle for enums); grey out settings whose
  parent setting makes them irrelevant; and keep its description current, brief and plain (no
  AI-sounding phrasing). Same brevity rule for the mod page text.
- **The Thunderstore token is `TCLI_AUTH_TOKEN` (user env var).** Never write it to a file or
  echo it; the owner sets it with `setx` in their own terminal.

## Layout

- `src/KroolWorldUI/Plugin.cs` - entry point; each feature exposes `BindConfig` and Harmony patches.
- `src/KroolWorldUI/SettingsWindow.cs` - built-in F7 settings window (IMGUI; also opened from the
  pause menu by `MenuButton.cs`). It renders every config entry generically in collapsible
  sections, in Bind order, so a new feature only needs `config.Bind` (with an
  `AcceptableValueRange` for a slider) plus a line in `Plugin.BindAll`. Use `ConfigText.Desc(...)`
  for the extras: `group` (sub-heading), `when`/`whenText` (greyed out unless; keep whenText in
  setting-name words, it is shown and translated), `step`. A section's `Enabled` bool becomes its
  header On/Off; the description shows in the window's description panel, so write it for players
  and use the window's spaced names ("Near Limit Percent"). Tag window-only state with
  `ConfigText.Hidden.Tag`. Sizes go through `P()` (no GUI.matrix scaling, so text stays sharp).
  Mouse, keyboard and controller (ZInput) all drive one focus model. No ConfigurationManager.
- `src/KroolWorldUI/L10n.cs` + `Translations/` - every player-visible string goes through
  `L10n.T(key, args)` (HUD and window chrome, keys in `English.txt`), `L10n.Game(token, fallback)`
  for words the game already translates, or `L10n.W` (settings window text, keys generated into
  `English.settings.txt` by SettingsDoc on every build; never hand-edit it). Other languages are
  `<ValheimLanguage>.txt` / `.settings.txt` with the same keys; a missing key falls back to English.
  **When you change English text, update every language's line for that key in the same change**
  (or delete it there so it falls back), and add new keys to all languages. Numbers on the HUD use
  `L10n.Num` (the language's decimal separator). HUD text boxes must size to their text.
- `General.cs` - the General section (settings key, window scale, hide key, volume, colorblind
  colors) and `Migrate()`: bump `CurrentConfigVersion` and add a step for any setting whose meaning
  changes, instead of breaking saved configs.
- `tools/SettingsDoc` - rewrites the README settings tables (between the `settings:begin/end`
  markers) from `Plugin.BindAll`; `build.ps1` runs it via `scripts/settings-doc.ps1`, and
  `release.ps1` commits README.md. Never hand-edit those tables.
- `src/KroolWorldUI/Features/` - one file per feature. `HudKit.cs` holds the shared toast look,
  game-font text cloning and synthesized sounds; new HUD toasts should use it. Static fields
  must not touch Harmony or game types in their initializers: SettingsDoc runs `BindConfig`
  outside the game.
- `thunderstore/` - manifest.json and icon.png (256x256). Package = these + DLL + README + CHANGELOG.
- `docs/wiki/FAQ.md` - the Thunderstore Wiki tab's page, pasted in by hand by the owner. FAQ and troubleshooting only,
  not a copy of the README; update it when a behavior it describes changes.

## Gotchas

- Commit from PowerShell 5.1 with `git commit -F <file>` (or no double quotes in the message):
  5.1 mangles embedded `"` in native arguments, the commit fails, and with stderr hidden the
  staged changes silently ride along into the next commit (happened in 0.2.0).
- After `git mv`, stage the NEW path only: `git add` with the old (deleted) path aborts the whole
  add, so the commit records just the rename (happened in 0.6.0).

- Target `netstandard2.1`: Valheim's Unity DLLs reference netstandard 2.1; `net462` fails to build.
- Game DLLs are referenced from the Steam install (`-p:ValheimDir=...` to override); BepInEx and
  HarmonyX come from nuget.bepinex.dev.
- HUD placement: the owner's mods (ExtraSlots panels, EpicLoot ability bar) all live bottom-left.
  Keep new HUD elements off that corner. Taken so far: skill XP tiles (default bottom right,
  growing left, just above the highest KeyHints graphic seen this session; key hints are one row,
  about 20-80 px up), weight bar (child of Hud.m_healthPanel, left of the
  food column: icon beside the fork, a clone of the health bar rising above it), the food
  decay segments (FoodDecay: children of Hud.m_healthBarRoot and of the stamina/eitr bars' inner "Stamina" rect, the parent of m_staminaBar2Slow / m_eitrBarSlow, past the far end; those panels hide when full and the segment hides with them). Below the hotbar is the
  raven tutorial text area: the owner saw overlap there, avoid it. Food reminder toast:
  top-center (0.5, 0.84), pushed below Hud.m_eventBar (raid banner) when that is up. Save
  countdown: centered at (0.5, 0.72), below the food toast. Rested countdown: a child of Hud.m_statusEffectListRoot placed above the Resting icon's name label (owner: not center-screen, timer only, above the buff not beside it).
  Compass: top center of Hud.m_rootObject (8 px down, about 74 px tall), pushed below Hud.m_eventBar and
  the boss bar (EnemyHud.m_baseHudBoss clones under m_hudRoot) when those are up.
  Buff icons (BuffIcons): re-placed after Hud.UpdateStatusEffects every frame at Size scale, wrapping before the compass.
  Day/night bar: child of Minimap.m_smallRoot (its own canvas), in the 40 px above the minimap.
- Day timing (EnvMan): cycle m_dayLengthSec (1200 s) from midnight on ZNet.GetTimeSeconds();
  daylight from 15% to 85% of it, which EnvMan rescales to the sun's 0.25-0.75 (RescaleDayFraction).
  Build materials: in place mode, right of the build HUD's `SelectedInfo` box (found under
  Hud.m_buildHud by name, placed from its world corners), bottom-aligned with it.
- Auto-save timing: Game.UpdateSaving adds unscaled time to `Game.m_saveTimer` and saves at the
  static `Game.m_saveInterval` (1800 s); the server broadcasts `$msg_worldsavewarning 30s` to
  everyone 30 s before. A client's own timer saves only its character, and the server's
  SavePlayerProfile RPC resets it. `Minimap.AddPin` needs a Splatform.dll reference (PlatformUserID).
- Vanilla bottom-left HUD (from the game's bundle): `healthpanel` holds a vertical health bar
  (`Health`, rotated 90 degrees, grows up, 32 px per 25 HP) with the heart under it, and the food
  column (`food0..2`, 45 px) with the fork (`foodicon (1)`, not referenced by Hud) under it. The
  stamina, adrenaline and eitr panels share one spot at bottom center; adrenaline draws 23 px
  below stamina. Decompile with ilspycmd 8.2 (`DOTNET_ROLL_FORWARD=Major`); GuiBar lives in
  assembly_guiutils.dll.
- Food health decays: Player.UpdateFood sets each food's health to max x (time/burn)^0.3, and
  Food.CanEatAgain() is time < burn/2. Vanilla, not a mod.
- Sounds are synthesized at runtime (AudioClip.Create) and routed to AudioMan.m_guiMixer so they
  follow the game's volume. Keep them short and quiet; the owner asked for non-annoying.
- Skill XP bars are built from plain Images plus TMP text cloned from Hud.m_healthText (no
  Skills-tab prefab since 0.3.0). The floating XP text calls DamageText's private
  AddInworldText by reflection; DamageText.ShowText would broadcast an RPC to every player.
