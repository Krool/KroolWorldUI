# Krool World UI

Client-side HUD additions for Valheim. Each feature has its own on/off switch, so you keep only the ones you like.

Client-side only: the server and other players do not need it. It reads game state for display and never changes your character or world.

## Features

### Skill XP bars

When you earn XP in a skill, a bar for that skill appears on the right side of the screen. It uses the same row art as the Skills tab.

- The bar shows progress toward the next level in two shades of one color. The darker part is where you were when the bar appeared. The lighter part is what you have earned since, so each axe swing visibly adds to it.
- Keep earning XP and the lighter part keeps growing, carrying over through a level-up. The text next to the skill name shows the running total as a percent of a level (for example `+12%`).
- After 3 seconds without XP for that skill, the bar fades out. Next time it appears, the lighter part starts from zero again.
- Bars for different skills stack, up to 5 at once. When one fades out, the bars below it slide up.
- The stack stays out of the way: it hides while the inventory, the big map or the menu is open, and moves down if the minimap or status effect icons would overlap it.

### Weight on the HUD

Your carry weight, always visible, without opening the inventory. It sits at the top left, just under the hotbar, with a weight icon, `current/max` and a thin fill bar.

- The numbers match the inventory screen exactly, including extra slots and backpacks from other mods and your strength belt.
- The current weight turns orange near your limit (80% by default) and flashes red when you are over, like the inventory does.
- Show mode: **Always** (default), **Near Limit** (only when you are close to full), or **When Changed** (a few seconds after you pick up or drop something).
- It hides while the inventory is open, since the inventory already shows weight.

## Settings

Press **F7** in game to open the Krool World UI settings window. Every option is there: on/off buttons for features, sliders for sizes and timings, click-to-cycle buttons for choices, and a Reset button next to anything changed from its default. **Preview XP bars** shows sample bars for your top skills so you can place and size them live. Esc or F7 closes it; while it is open your character ignores input.

No other mod is needed for this. The same settings are also in `BepInEx/config/krool.kroolworldui.cfg` (r2modman: Config editor).

| Setting | Default | What it does |
|---|---|---|
| SettingsKey | F7 | Key that opens the settings window |
| Enabled | true | Turn skill XP bars on or off |
| ShowGainText | true | Show `+N%` gained next to the skill name |
| HoldSeconds | 3 | Seconds a bar stays after the last XP gain |
| FadeSeconds | 0.6 | Length of the fade-out |
| MaxBars | 5 | Most bars shown at once |
| AnchorX / AnchorY | 0.985 / 0.6 | Where the top-right corner of the stack sits (0-1 of the screen) |
| BarWidth | 260 | Bar width in HUD pixels |
| Weight: Enabled | true | Show carry weight on the HUD |
| Weight: Show | Always | Always, Near Limit, or When Changed |
| Weight: NearLimitPercent | 80 | Percent of max where it turns orange (and appears, in Near Limit mode) |
| Weight: ChangedSeconds | 3 | When Changed mode: how long it stays |
| Weight: ShowBar | true | Thin fill bar under the numbers |
| Weight: AnchorX / AnchorY / Scale | 0.008 / 0.99 / 1 | Position of its top-left corner, and size. It always stays below the hotbar |

## Building from source

Requires Windows, the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`) and Valheim installed.

```powershell
.\scripts\build.ps1                 # builds and writes dist\Krool-Krool_World_UI-<version>.zip
.\scripts\new-dev-profile.ps1       # one time: copies the BCCBoys r2modman profile to KroolDev
.\scripts\deploy-dev.ps1            # builds and installs into the KroolDev profile
```

If Valheim is not in the default Steam folder, pass `-ValheimDir "<path>"` to `build.ps1`.
