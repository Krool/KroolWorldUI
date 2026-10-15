![Krool World UI](https://raw.githubusercontent.com/Krool/KroolWorldUI/main/docs/banner.png)

**Small HUD additions for Valheim that show what the game keeps hidden:** how close a skill is to its next level, how much you are carrying, how much health your food has lost, and when to eat.

- **Client-side only.** The server and other players do not need it.
- **Read-only.** It reads game state for display and never changes your character or world.
- **Pick what you want.** Every feature has its own on/off switch in an in-game settings window (**F7**). No other config mod needed.

## At a glance

| Feature | What you get | On by default |
|---|---|---|
| **Skill XP bars** | A bar under your stamina that fills as you earn XP, plus a floating `+0.8% Swords` | Yes |
| **Carry weight** | A vertical weight bar beside your food, styled like the health bar | Yes |
| **Food max health** | A faint red segment on the health bar: the max health your food has lost as it wears off | Yes |
| **Food reminders** | A toast when food runs low, a message while you have none, and an optional "can eat again" | Yes ("can eat again" off) |
| **Settings window** | Every option in game, with a live preview | Press **F7** |

---

## Skill XP bars

Swing an axe and a slim bar for Woodcutting appears under your stamina bar: the skill's icon, then a bar with the skill name and level on it.

- **See every gain.** The darker part of the bar is where you were when it appeared. The lighter part is what you have earned since, so each swing visibly adds to it.
- **Running total.** The `+12%` before the level counts everything earned while the bar is up, carrying over through a level-up.
- **Stacks neatly.** Each skill gets its own bar, stacking downward (up to 4, fewer if they would run off the screen). After 3 seconds without XP a bar fades out and the ones below slide up.
- **Floating text.** A blue `+0.8% Swords` floats up over your character like a damage number. Only you see it. Quick gains are added together. Set it to **Level Up Only** or **Off** if you prefer.
- **Your layout:**
  - Labels: **Name And Level** or **Icon Only**.
  - Width, height and color.
  - Position: **Below Stamina**, **Right Side** (clear of the minimap), or **Custom**, plus nudge offsets.
- Hides while the inventory, the big map or the menu is open.

## Carry weight

Your weight, always on screen, without opening the inventory. It sits left of the food icons and is built like the health bar beside it: a weight icon next to the fork, a bar rising above it, and your weight over your max on top.

- **Exact numbers.** Matches the inventory screen, including your strength belt and the extra slots and backpacks from other mods.
- **Grows with you.** The bar is 110 pixels tall at the normal 300 max and grows with your capacity: a Megingjord (+150) makes it half again as tall. You can turn the growth off.
- **Reads at a glance:**
  - **Thresholds** (default): your fill color, orange from 80% full, red when over.
  - **Gradient**: blends through the three as you fill up.
  - **Single**: one color.
  - Over capacity it flashes, like the inventory does. All three colors can be changed.
- **Text:** weight over max (default), weight only, percent, or none, at any size.
- **When to show it:** **Always**, **Near Limit** (only when you are close to full), or **When Changed** (a few seconds after you pick up or drop something).
- Bar length, thickness and position are adjustable.

## Food max health

Valheim quietly shrinks each food's health bonus as it wears off. A food at half its time gives only about 81% of its health, and the drop speeds up near the end.

- The health bar continues past its end in a **faint red segment**: the max health you have lost this way.
- That is exactly what eating fresh food would give back, so you can tell when a top-up is worth it.
- Color and opacity are adjustable.

## Food reminders

A small toast at the top of the screen with the food's icon, its name and the time left. Each kind has its own switch:

| Reminder | Default | When |
|---|---|---|
| **Running out** | On | A food drops under 4 minutes (adjustable) |
| **Can eat again** | Off | A food passes half its time, the point where the game lets you eat it again |
| **No food** | On | Stays up while you have no food, until you eat or press **Backspace** |

- The no-food message waits until a new character has arrived, and shows again after you respawn. Set it to **Any Slot Empty** to be told whenever a food slot is free.
- A soft two-note chime plays with each reminder. It follows the game's sound volume, and can be turned down or off.
- Timed reminders show once per food and fade after 6 seconds. They do not repeat until you have eaten that food again.
- The countdown uses fixed-width digits, so it holds still.
- Moves down to clear the raid banner, and hides while the inventory, the big map or the menu is open.

## Settings window

Press **F7** in game.

- **Every option in one place:** on/off buttons for features, sliders for sizes and timings, click-to-cycle buttons for choices, and a color swatch with an editable hex value.
- **Reset** sits next to anything you changed from its default.
- **Preview on HUD** shows sample XP bars for your top skills, the food reminders and a sample max-health segment (and plays the chime once), so you can place and size things live.
- **Esc** or **F7** closes it. While it is open your character ignores input.

The same settings are in `BepInEx/config/krool.kroolworldui.cfg` (in r2modman: **Config editor**). Colors there are hex `RRGGBBAA`; the last two digits are opacity.

### Full settings list

**General**

| Setting | Default | What it does |
|---|---|---|
| SettingsKey | F7 | Key that opens the settings window |

**Skill XP Bars**

| Setting | Default | What it does |
|---|---|---|
| Enabled | true | Turn skill XP bars on or off |
| Labels | Name And Level | Name And Level, or Icon Only |
| ShowGainText | true | Show `+N%` gained before the level |
| Position | Below Stamina | Below Stamina, Right Side, or Custom |
| BarWidth / BarHeight | 220 / 22 | Size of one bar in HUD pixels |
| BarColor | gold | Bar color; earlier progress is a darker shade, new XP lighter |
| HoldSeconds / FadeSeconds | 3 / 0.6 | How long a bar stays after the last XP, and its fade |
| MaxBars | 4 | Most bars shown at once |
| OffsetX / OffsetY | 0 / 0 | Nudge the stack, in HUD pixels |
| AnchorX / AnchorY | 0.5 / 0.3 | Custom position: center of the stack's top edge (0-1 of the screen) |
| FloatingText | Every Gain | Every Gain, Level Up Only, or Off |
| FloatingTextColor | blue | Color of the floating text |

**Weight**

| Setting | Default | What it does |
|---|---|---|
| Enabled | true | Show carry weight on the HUD |
| Show | Always | Always, Near Limit, or When Changed |
| NearLimitPercent | 80 | Percent of max where the near-limit color starts (and where it appears, in Near Limit mode) |
| ChangedSeconds | 3 | When Changed mode: how long it stays |
| Colors | Thresholds | Thresholds, Gradient, or Single |
| FillColor / NearLimitColor / OverColor | tan / orange / red | Bar colors |
| Text / TextSize | Current And Max / 16 | Numbers above the bar |
| BarLength / BarThickness | 110 / 16 | Bar size in HUD pixels (length at 300 max weight) |
| ScaleWithMaxWeight | true | Grow the bar with your max weight |
| OffsetX / OffsetY | 0 / 0 | Nudge it, in HUD pixels |

**Food Max Health**

| Setting | Default | What it does |
|---|---|---|
| Enabled | true | Show the lost-max-health segment on the health bar |
| Color | faint red | Segment color and opacity |

**Food Reminder**

| Setting | Default | What it does |
|---|---|---|
| Enabled | true | All food reminders on or off |
| RunningOut / ThresholdMinutes | true / 4 | Remind when a food has less than this many minutes left |
| CanEatAgain | false | Remind when a food can be eaten again |
| NoFoodMessage / NoFoodWhen | true / No Food | Message while you have no food (or any empty slot) |
| DismissKey | Backspace | Hides the no-food message until you next eat |
| ShowSeconds | 6 | How long a timed reminder stays |
| Sound / SoundVolume | true / 0.35 | Soft chime with each reminder, and its volume |
| AnchorX / AnchorY | 0.5 / 0.84 | Where the reminder sits (center of its top edge) |

Settings saved by an older version keep their values. Removed settings (the old hotbar placement for weight) are ignored.

## Install

- **r2modman / Thunderstore Mod Manager:** search for **Krool World UI** and install. BepInEx comes with it.
- **Manual:** install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), then put `KroolWorldUI.dll` in `BepInEx/plugins`.

## Compatibility and tips

- Used in a modpack with EpicLoot, ExtraSlots, AdventureBackpacks, Quick Stack, MultiUserChest, PlantEverything and Warfare.
- **Something overlaps?** Other mods like the bottom-left corner too. Every element has nudge offsets in **F7**, and **Preview on HUD** shows where things land.
- **Floating text too busy** while running or sneaking? Set **FloatingText** to **Level Up Only**.
- **Updated and the defaults look off?** Settings you saved earlier are kept. Use **Reset** in **F7** to take the new defaults.

Source, issues and changelog: [github.com/Krool/KroolWorldUI](https://github.com/Krool/KroolWorldUI). MIT licensed.

## Building from source

Requires Windows, the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`) and Valheim installed.

```powershell
.\scripts\build.ps1                 # builds and writes dist\Krool-Krool_World_UI-<version>.zip
.\scripts\new-dev-profile.ps1       # one time: copies the BCCBoys r2modman profile to KroolDev
.\scripts\deploy-dev.ps1            # dev build into KroolDev as Krool_World_UI_Dev (close r2modman first time)
```

If Valheim is not in the default Steam folder, pass `-ValheimDir "<path>"` to `build.ps1`.

### Releasing

Add a `## x.y.z` section to `CHANGELOG.md`, then:

```powershell
.\scripts\release.ps1 -Version 0.3.2 -Publish
```

This bumps the version everywhere, builds the zip, uploads it to Thunderstore with [tcli](https://github.com/thunderstore-io/thunderstore-cli) (`dotnet tool install -g tcli`), then commits, tags and pushes. Uploading needs a Thunderstore service-account token for the Krool team in the `TCLI_AUTH_TOKEN` environment variable.

### Screenshots

The shot list for the mod page is in [docs/screenshots](docs/screenshots/README.md).
