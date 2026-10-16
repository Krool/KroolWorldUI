![Krool World UI](https://raw.githubusercontent.com/Krool/KroolWorldUI/main/docs/banner.png)

**Small HUD additions for Valheim that show what the game keeps hidden:** how close a skill is to its next level, how much you are carrying, how much wood and stone you have while building, how much health and stamina your food has lost, when to eat, how long until nightfall, and when the auto-save lag spike is coming. It can also pin your workbenches on the map with Norse-sounding names.

- **Client-side only.** The server and other players do not need it.
- **Read-only, with one exception.** It reads game state for display. The only thing it ever changes is your own map: **Bench Pins** adds a pin when you build a workbench, as you would by hand. It never touches your character or world.
- **Pick what you want.** Every feature has its own on/off switch in an in-game settings window (**F7**). No other config mod needed.

## At a glance

| Feature | What you get | On by default |
|---|---|---|
| **Skill XP bars** | A skill icon with an XP bar that fills as you earn XP, plus a floating `+0.8% Swords` | Yes |
| **Carry weight** | A vertical weight bar beside your food, styled like the health bar | Yes |
| **Build materials** | While the hammer is out: the wood, stone and other building materials you carry, what the selected piece costs, and how many you can build | Yes |
| **Food decay** | Faint segments past the end of the health, stamina and eitr bars: the max your food has lost as it wears off | Yes |
| **Food reminders** | A toast when food runs low, a message while you have none, and an optional "can eat again" | Yes ("can eat again" off) |
| **Day/night bar** | A bar above the minimap: night and day with moon and sun, where you are now, and the time until dusk or dawn | Yes |
| **Save countdown** | A clear 5-second countdown before the auto-save, so its lag spike never catches you mid-fight | Yes |
| **Bench pins** | A map pin with a random Norse name (Hrafnvik, Trollhaven, Odinheim) when you build a workbench | Yes |
| **Settings window** | Every option in game, with a live preview | Press **F7** |

---

## Skill XP bars

Swing an axe and a Woodcutting tile appears at the bottom right of the screen, just above the key hints: the skill's icon, with its XP bar underneath.

- **See every gain.** The darker part of the bar is where you were when the tile appeared. The lighter part is what you have earned since, so each swing visibly adds to it, carrying over through a level-up.
- **Lines up neatly.** Each skill gets its own tile, side by side (up to 4). After 3 seconds without XP a tile fades out and the others slide over.
- **Stays clear of the game's own UI.** It sits above the attack, block and build key hints, and only ever moves up to make room, so it does not hop when you equip something.
- **Floating text.** A blue `+0.8% Swords` floats up over your character like a damage number. Only you see it. Quick gains are added together. Set it to **Level Up Only** or **Off** if you prefer.
- **Your layout:**
  - Style: **Tiles** (icon over bar, default) or **Rows** (a wide bar with the skill name, level and `+12%` gained on it).
  - Tile size, an optional level number in the tile's corner, bar color.
  - Placement: **Bottom Right**, **Below Stamina**, **Right Side** (clear of the minimap), or **Custom**, plus nudge offsets.
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

## Build materials

Take out the hammer (or the hoe or cultivator) and a small panel appears right of the build menu's piece box, counting what you carry as you build.

- **What the selected piece costs,** each material with how many you have: `Wood 245 /2`. Red when you are short.
- **Your building materials, sensibly:**
  - **Wood, Stone, Core Wood, Fine Wood** once your character has found them, so a new character is not shown materials it has never seen.
  - **Resin, Bronze Nails, Iron Nails, Black Marble, Yggdrasil Wood, Grausten, Ashwood** only while you carry some, so a late-game panel stays short.
  - Each material has its own setting in **F7**: **Always**, **When Known**, **When Carried** or **Never**. Add anything else by the name the game shows (`Copper, Deer Hide`).
- With the **hoe** or **cultivator** out, it lists only what the selected piece costs.
- **Enough for 12:** how many of the selected piece your materials cover, or **Not enough**.
- Counts what the game itself counts when you place a piece: your inventory, not chests or backpacks.
- Updates five times a second while you build. Hides with the inventory, the big map and the menu.
- Sits left of the piece box instead on narrow screens, or anywhere with **Custom** placement.

## Food decay

Valheim quietly shrinks each food's bonuses as it wears off: health, stamina and eitr alike. A food at half its time gives only about 81% of each, and the drop speeds up near the end.

- Each bar continues past its end in a **faint segment**: red on health, gold on stamina, purple on eitr. That is the max you have lost this way.
- It is exactly what eating fresh food would give back, so you can tell when a top-up is worth it before a fight or a long run.
- The stamina and eitr bars only show while not full, and their segments come and go with them.
- Each bar has its own switch and color.

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

## Day/night bar

A slim bar along the top of the minimap shows the whole day, midnight to midnight: night at both ends with a moon, day in the middle with a sun, and a white marker for now.

- **Dusk in 6:40** by day, **Dawn in 2:10** by night. In the last minute before dark (adjustable) the dusk countdown turns orange, so you can head home before the night spawns.
- The day number, in your game's language, as the game shows it at sunrise.
- An optional 24-hour clock (06:00 sunrise, 18:00 sunset).
- Uses the game's own clock, synced from the server: a 20-minute day, about 14 minutes of daylight and 6 of night.
- Hides with the minimap. Bar height and both colors are adjustable.

## Save countdown

Valheim auto-saves every 30 minutes, and on a big world the save freezes the game for a moment. On hardcore that hitch can be the difference in a fight. A few seconds before it, a box near the top of the screen counts down: **World save in 5, 4, 3, 2, 1**, then **Saving...**

- **Exact in single player and as host:** it reads the game's own save timer.
- **On a dedicated server:** it times the world save from the server's 30-second warning, which every player receives. Your own client also saves your character on its own timer; that shows as **Character save in 5**.
- A soft tick each second, a brighter one on the last. It follows the game's sound volume and can be turned off.
- Start time (3 to 30 seconds) and position are adjustable.

## Bench pins

Build a workbench and a pin appears on your map, named like a Norse settlement: **Hrafnvik**, **Frostgard**, **Trollhaven**, **Eikthyrhold**, **Meadhall**.

- **One pin per base.** No new pin if one is already within 50 meters (adjustable), so a base with several benches gets one pin.
- **Pick the icon:** fire, house (default), hammer, dot or portal.
- **Pick the name:** random (default), the station's name, or none.
- **Make it yours:** the name bank is two editable lists, prefixes and suffixes, in **F7**. Add your clan's names.
- Workbench only by default; switch to **All Crafting Stations** to also pin forges, stonecutters and the rest.
- Pins are saved with your character like any pin you place. Removing the bench leaves the pin.

## Settings window

Press **F7** in game.

- **One section per feature,** each with its own On/Off on the header. Click a section's name to open or close it; the window remembers which are open.
- **Hover any setting** to see what it does, its default and its range.
- **Every option in one place:** sliders for sizes and timings, click-to-cycle buttons for choices, and a color swatch with an editable hex value.
- **Reset** sits next to anything you changed, and **Reset all** on a section's header.
- **Preview on HUD** shows sample skill tiles for your top skills, the food reminders, sample food decay segments and a looping save countdown (with sound on the first pass), so you can place and size things live.
- **Esc** or **F7** closes it. While it is open your character ignores input, and the mouse wheel scrolls the window instead of zooming the camera.

The same settings are in `BepInEx/config/krool.kroolworldui.cfg` (in r2modman: **Config editor**). Colors there are hex `RRGGBBAA`; the last two digits are opacity.

### Full settings list

Generated from the mod's code on every build, so it always matches the F7 window.

<!-- settings:begin (generated by tools/SettingsDoc; edit the config.Bind descriptions instead) -->

**General**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Settings Key | F7 | Any key | Key that opens this settings window. |

**Skill XP Bars**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Show a skill on the HUD while you gain XP in it. |
| Style | Tiles | Tiles, Rows | Tiles: the skill icon with its XP bar underneath, side by side. Rows: a wide bar with the icon on the left, stacked. |
| Placement | Bottom Right | Bottom Right, Below Stamina, Right Side, Custom | Bottom Right: above the key hints, growing left. Below Stamina: centered under the stamina bar. Right Side: right edge of the screen, below the minimap. Custom: placed by Anchor X and Anchor Y. |
| Tile Size | 64 | 40 to 128 | Tiles: width in HUD pixels. The tile is a third taller than it is wide. |
| Tile Level | Off |  | Tiles: show the skill level in the tile's corner. |
| Labels | Name And Level | Name And Level, Icon Only | Rows: Name And Level shows the skill name and level on the bar. Icon Only: just the icon and the bar. |
| Show Gain Text | On |  | Rows with Name And Level: show the XP gained since the bar appeared, as a percent of a level. |
| Bar Width | 220 | 100 to 500 | Rows: width of one bar in HUD pixels. |
| Bar Height | 22 | 12 to 48 | Rows: height of one bar in HUD pixels. |
| Bar Color | `DBA847FF` | Hex RRGGBBAA (AA = opacity) | Bar color. Progress you had is a darker shade, new XP a lighter one. |
| Hold Seconds | 3 | 0.5 to 30 | Seconds a bar stays fully visible after the last XP gain. |
| Fade Seconds | 0.6 | 0 to 5 | Seconds the fade-out takes. |
| Max Bars | 4 | 1 to 10 | Most skills shown at once (fewer if they would not fit on screen). The stalest one is reused past this. |
| Offset X | 0 | -400 to 400 | Nudge left (-) or right (+), in HUD pixels. |
| Offset Y | 0 | -400 to 400 | Nudge down (-) or up (+), in HUD pixels. |
| Anchor X | 0.5 | 0 to 1 | Custom placement: horizontal center (0 = left of screen, 1 = right). |
| Anchor Y | 0.3 | 0 to 1 | Custom placement: top edge (0 = bottom of screen, 1 = top). |
| Floating Text | Every Gain | Off, Every Gain, Level Up Only | Float each XP gain over your character like a damage number, seen only by you. Level Up Only: just new levels. |
| Floating Text Color | `73B8FFFF` | Hex RRGGBBAA (AA = opacity) | Color of the floating XP text. |

**Weight**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Show your carry weight on the HUD, left of the food icons. |
| Show | Always | Always, Near Limit, When Changed | Always. Near Limit: only at or above Near Limit Percent. When Changed: for a few seconds after your weight changes. |
| Near Limit Percent | 80 | 10 to 100 | Percent of max weight where the bar turns to the near-limit color (and where it appears, in Near Limit mode). |
| Changed Seconds | 3 | 1 to 15 | When Changed mode: seconds the bar stays after a change. |
| Colors | Thresholds | Thresholds, Gradient, Single | Thresholds: fill color, near-limit color past Near Limit Percent, over color when overweight. Gradient: blends from fill to near-limit to over as you fill up. Single: always the fill color. |
| Fill Color | `CC9E4CFF` | Hex RRGGBBAA (AA = opacity) | Bar color under the limit. |
| Near Limit Color | `FF8C1FFF` | Hex RRGGBBAA (AA = opacity) | Bar color near the limit. |
| Over Color | `DB291FFF` | Hex RRGGBBAA (AA = opacity) | Bar color when overweight. |
| Text | Current And Max | Current And Max, Current, Percent, None | Numbers above the bar: current over max, current only, percent, or none. |
| Text Size | 16 | 10 to 28 | Font size of the numbers. |
| Bar Length | 110 | 40 to 220 | Bar length in HUD pixels at the normal 300 max weight. |
| Bar Thickness | 16 | 6 to 32 | Bar thickness in HUD pixels (the health bar is 32). |
| Scale With Max Weight | On |  | Grow the bar with your max weight: a Megingjord (+150) makes it 50% longer. |
| Offset X | 0 | -200 to 200 | Nudge left (-) or right (+), in HUD pixels. |
| Offset Y | 0 | -200 to 300 | Nudge down (-) or up (+), in HUD pixels. |

**Food Decay**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Extend the health, stamina and eitr bars with faint segments showing the max your foods have lost as they wear off: what eating fresh food would give back. |
| Health | On |  | Segment on the health bar. |
| Health Color | `BF2E2473` | Hex RRGGBBAA (AA = opacity) | Color of the lost-health segment. Lower the last two digits to make it fainter. |
| Stamina | On |  | Segment on the stamina bar. Like the bar itself, it shows only while your stamina is not full. |
| Stamina Color | `EBB83373` | Hex RRGGBBAA (AA = opacity) | Color of the lost-stamina segment. |
| Eitr | On |  | Segment on the eitr bar (eitr foods only). Shows only while your eitr is not full. |
| Eitr Color | `9961E673` | Hex RRGGBBAA (AA = opacity) | Color of the lost-eitr segment. |

**Food Reminder**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Food reminders on the HUD. The switches below pick which ones. |
| Running Out | On |  | Remind when a food has less than Threshold Minutes left. |
| Threshold Minutes | 4 | 1 to 15 | Running-out reminder: minutes left that trigger it. |
| Can Eat Again | Off |  | Remind when a food can be eaten again (the game allows it once half its time has passed). |
| No Food Message | On |  | Keep a message up while you have no food, until you eat or press Dismiss Key. Shows on a new character after the arrival, and after respawning. |
| No Food When | No Food | No Food, Any Slot Empty | No Food: only when all your food is gone. Any Slot Empty: whenever a food slot is free. |
| Dismiss Key | Backspace | Any key | Hides the no-food message until you next eat. |
| Show Seconds | 6 | 2 to 20 | How long a reminder stays on screen. |
| Sound | On |  | Play a soft chime with each reminder. |
| Sound Volume | 0.35 | 0.05 to 1 | Chime volume (also follows the game's sound settings). |
| Anchor X | 0.5 | 0 to 1 | Horizontal position of the reminder's center (0 = left, 1 = right). |
| Anchor Y | 0.84 | 0 to 1 | Vertical position of the reminder's top edge (0 = bottom, 1 = top). It moves down on its own to clear the raid banner. |

**Build Materials**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | While a build tool is out, count the materials you carry, so you can watch your wood and stone as you build. |
| Selected Piece | On |  | List what the selected piece costs, each with how many you carry. Red when you are short. |
| Can Build Count | On |  | Show how many of the selected piece your materials cover. |
| Placement | Beside Build Info | Beside Build Info, Custom | Beside Build Info: right of the selected-piece box at the bottom center. Custom: placed by Anchor X and Anchor Y. |
| Anchor X | 0.8 | 0 to 1 | Custom placement: left edge (0 = left of screen, 1 = right). |
| Anchor Y | 0.1 | 0 to 1 | Custom placement: bottom edge (0 = bottom of screen, 1 = top). |
| Wood | When Known | Always, When Known, When Carried, Never | When to list Wood. When Known: once your character has picked one up. When Carried: only while you have some. |
| Stone | When Known | Always, When Known, When Carried, Never | When to list Stone. When Known: once your character has picked one up. When Carried: only while you have some. |
| Core Wood | When Known | Always, When Known, When Carried, Never | When to list Core Wood. When Known: once your character has picked one up. When Carried: only while you have some. |
| Fine Wood | When Known | Always, When Known, When Carried, Never | When to list Fine Wood. When Known: once your character has picked one up. When Carried: only while you have some. |
| Resin | When Carried | Always, When Known, When Carried, Never | When to list Resin. When Known: once your character has picked one up. When Carried: only while you have some. |
| Bronze Nails | When Carried | Always, When Known, When Carried, Never | When to list Bronze Nails. When Known: once your character has picked one up. When Carried: only while you have some. |
| Iron Nails | When Carried | Always, When Known, When Carried, Never | When to list Iron Nails. When Known: once your character has picked one up. When Carried: only while you have some. |
| Black Marble | When Carried | Always, When Known, When Carried, Never | When to list Black Marble. When Known: once your character has picked one up. When Carried: only while you have some. |
| Yggdrasil Wood | When Carried | Always, When Known, When Carried, Never | When to list Yggdrasil Wood. When Known: once your character has picked one up. When Carried: only while you have some. |
| Grausten | When Carried | Always, When Known, When Carried, Never | When to list Grausten. When Known: once your character has picked one up. When Carried: only while you have some. |
| Ashwood | When Carried | Always, When Known, When Carried, Never | When to list Ashwood. When Known: once your character has picked one up. When Carried: only while you have some. |
| Other Materials |  |  | More materials to always list, by the names the game shows, separated by commas (for example: Copper, Deer Hide). |

**Day Night Bar**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | A day and night bar above the minimap: where you are in the day, and how long until dusk or dawn. |
| Countdown | On |  | Show the time left until dusk (by day) or dawn (by night). |
| Day Number | On |  | Show the day number. |
| Clock | Off |  | Show the time of day as a 24-hour clock: 06:00 is sunrise, 18:00 sunset. |
| Dusk Warning Seconds | 60 | 0 to 300 | The dusk countdown turns orange this many seconds before night. 0 = never. |
| Bar Height | 10 | 6 to 18 | Bar height in HUD pixels. |
| Day Color | `DBAD52D9` | Hex RRGGBBAA (AA = opacity) | Color of the day part. |
| Night Color | `212B5CE6` | Hex RRGGBBAA (AA = opacity) | Color of the night part. |

**Save Countdown**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Counts down the last seconds before the auto-save, so its lag spike does not catch you mid-fight. Works in single player, as host, and on a dedicated server. |
| Seconds | 5 | 3 to 30 | How many seconds before the save the countdown starts. |
| Sound | On |  | A soft tick each second of the countdown. |
| Sound Volume | 0.35 | 0.05 to 1 | Tick volume (also follows the game's sound settings). |
| Anchor X | 0.5 | 0 to 1 | Horizontal position of the countdown's center (0 = left, 1 = right). |
| Anchor Y | 0.72 | 0 to 1 | Vertical position of the countdown's center (0 = bottom, 1 = top). |

**Bench Pins**

| Setting | Default | Range or choices | What it does |
|---|---|---|---|
| Enabled | On |  | Adds a map pin when you build a workbench, so you can find your camps again. This adds a pin to your map like you would by hand; removing the bench leaves the pin. |
| Pin Stations | Workbench Only | Workbench Only, All Crafting Stations | Workbench Only, or every crafting station (forge, stonecutter, artisan table, and so on). |
| Icon | House | Fire, House, Hammer, Dot, Portal | Which map pin icon to use. |
| Names | Random Name | Random Name, Station Name, No Name | Random Name: a Norse-sounding name from Name Prefixes + Name Suffixes. Station Name: "Workbench". No Name: just the icon. |
| Skip Near Pin Meters | 50 | 0 to 200 | No new pin if one is already on your map this close (meters), so a base gets one pin, not one per bench. 0 = always pin. |
| Announce | On |  | Show the new pin's name in the top-left messages. |
| Name Prefixes | Ulf, Hrafn, Jarn, Frost, Grim, Skjold, Bjorn, Eld, Svart, Thor, Odin, Rune, Troll, Mead, Ash, Ygg, Valk, Draugr, Fenris, Storm, Iron, Wolf, Raven, Boar, Elk, Ember, Hel, Njord, Sig, Hjalm, Gunn, Kald, Myrk, Vind, Stein, Gull, Sol, Eikthyr, Bonemass, Greyling |  | First halves of random names, separated by commas. |
| Name Suffixes | heim, gard, vik, stad, borg, holt, fjord, hall, by, hold, keep, stead, haven, mark, fell, dal, ness, wall, rest, ford, hof, skali, rock, watch, hearth |  | Second halves of random names, separated by commas. |

<!-- settings:end -->

Settings saved by an older version keep their values. Removed settings are ignored.

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
