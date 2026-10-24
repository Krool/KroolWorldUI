# Changelog

## 0.12.1

- Buff icons: default size is now 90% (was 70%). If you were on 70%, you move to 90%; any other size you chose is kept.

## 0.12.0

- New: buff icons. The game's buff icons at the top right show at 70% size (adjustable, 100% is the game's own), and wrap into a new row before they reach the compass instead of running under it. The wrap point follows your screen, the compass's width and size, and goes back to the game's 7 per row with the compass off.

## 0.11.1

- Compass: finished animations. Markers spring in and fade out instead of popping, keep their place as they leave, and the one you look at grows while its name and distance ease in. Pings ripple. Friends held at the edge get a chevron pointing the way to turn. Labels swell slightly as they pass the center. The strip fades with the map and glides under a boss bar or raid banner.
- Compass: distances round to steady steps (5 m past 50 m, 10 m past 200 m, km past 1000) and the readout shows how many markers are stacked (+2 more).
- Compass: new settings: Size, Heading Direction (212 SW), Distance (Focused, All Markers with a number under each icon, or Off), separate switches for Bed, Tombstone, Bosses and Pings, Max Markers (12), and Animations.

## 0.11.0

- New: compass. A strip at the top center with your heading for call-outs, friends in their own colors (held at the edge when behind you), map pins within 300 meters (yours, bench pins, bed, tombstone, bosses) and map pings. Look at a marker to see its name and distance. Height arrows for markers well above or below you; overlapping markers fold into a count; moves below a boss bar or raid banner. Window, width, range and opacity are adjustable.
- Bench pins: the Portal icon now makes a portal pin. It was making a tombstone-type pin, because the game lists Death and Bed before the fifth icon.

## 0.10.1

- Bench pins: taking a bench down with the hammer removes its pin, so a bench dropped to fix a boat leaves no trace. If another bench still stands within the skip distance, the pin moves to it instead. New setting Remove With Bench (on).
- Bench pins: the name bank grows to 100 prefixes and 100 suffixes, 10,000 names (Lokihollow, Sleipnirstrand, Frostcairn). If you edited the lists, yours are kept.

## 0.10.0

- New: auto repair, on by default. Opening a workbench, forge or other crafting station repairs every item it can, as if you clicked its repair button once per item: the same station and level rules and the same Crafting skill XP. One repair sound and a single Repaired N items message (can be turned off).

## 0.9.3

- Weight bar: easier to read. A dark backing behind the bar, a black outline on the numbers, a brighter max-weight line and a brighter default fill (a fill color you changed is kept).
- Weight bar: the fill now slides to your new weight right away instead of pausing first.

## 0.9.2

- Rested countdown: now sits just above the Resting buff icon, on one line (Rested in 14s), instead of in the slot beside it.

## 0.9.1

- Rested countdown: moved from the middle of the screen to the buff row, in the next slot left of your buff icons, and trimmed to just the timer (Rested 14s). The comfort and duration text, the progress line, the green Rested flash and the position settings are gone.

## 0.9.0

- New: rested countdown. While you rest by a fire, a box below the middle of the screen counts down the seconds until you are Rested, with a fill line, your comfort level and how long Rested will last at it. A soft chime when it lands (can be turned off). Timing comes from the game's own Resting effect.

## 0.8.0

- New: day/night bar above the minimap. Night at both ends with a moon, day in the middle with a sun, a marker for now, the day number, and the time until dusk or dawn (orange in the last minute before dark). Optional 24-hour clock. Uses the game's synced clock; hides with the minimap.

## 0.7.1

- Build materials: each common building material has its own setting (Always, When Known, When Carried or Never) in place of the single list. Defaults: Wood, Stone, Core Wood and Fine Wood once your character has found them; Resin, Bronze and Iron Nails, Black Marble, Yggdrasil Wood, Grausten and Ashwood only while carried. Other Materials takes any item by the name the game shows. With the hoe or cultivator out, only the selected piece's cost is listed.
- Bench pins: the default skip distance is now 50 meters (was 30), since a workbench alone covers 20. A value you saved earlier is kept.

## 0.7.0

- New: build materials. While the hammer, hoe or cultivator is out, a panel right of the build menu's piece box counts the materials you carry: what the selected piece costs (red when short), an always-shown list (Wood and Stone by default, editable in F7), and how many of the piece you can build. Counts match what the game checks when you place.
## 0.6.0

- Food decay: the stamina and eitr bars now get the same faint segment as the health bar, showing the max your food has lost as it wears off (the game shrinks all three alike). Gold on stamina, purple on eitr; each bar has its own switch and color. The stamina and eitr segments show while those bars do (when not full).
- The Food Max Health settings section is now Food Decay. A health segment color you had changed starts from the default again.
## 0.5.0

- New: save countdown. A box near the top of the screen counts down the last 5 seconds (adjustable, 3 to 30) before the auto-save, then shows Saving..., with a soft tick each second. Exact in single player and as host; on a dedicated server it times the world save from the server's 30-second warning. A client's own character save shows as Character save.
- New: bench pins. Building a workbench adds a map pin with a random Norse-sounding name (Hrafnvik, Trollhaven, Odinheim). Choose the icon, the naming (random, station name or none) and whether all crafting stations get pins; no new pin within 30 meters of an existing one. The name bank is two editable lists in F7. This is the mod's first change to anything: it adds pins to your own map, as you would by hand.
- Settings window: the title no longer overlaps the Expand all / Collapse all row; long setting names and section titles stay on one line instead of wrapping over the next row; long text values (the name lists) fit the window instead of widening it.
- Preview on HUD also shows a looping save countdown.
## 0.4.0

- Skill XP: new default Tiles style, the skill icon with its XP bar underneath, a third taller than wide, side by side at the bottom right just above the key hints. It only moves up to make room for them, so it does not hop when you equip something. Rows (the bar with the name on it) is still a choice. New settings: Style, Tile Size, Tile Level (level number in the corner, off by default), and Bottom Right placement. The placement setting is now named Placement, so it starts from the new default.
- Settings window: one collapsible section per feature, with the feature's On/Off and a Reset all on the header; it remembers which are open. Hover a setting to see what it does, its default and its range. The mouse wheel scrolls the window instead of zooming the camera.
- Setting descriptions use the names shown in the window.
- Mod page rewritten: banner, an at-a-glance table, bulleted features, install steps and tips. The settings tables are generated from the code, so they always match the window.

## 0.3.1

- Floating XP text: the last gain of a burst now shows about a second later instead of waiting for that skill's next XP; tiny gains no longer print as +0%; leftover gains are dropped on a new character or respawn; an error disables it once instead of logging on every XP tick.
- Skill XP bars: a new bar is placed before its first frame is drawn; the skill name makes room for the level text after a settings change; the stack no longer follows the stamina bar's show/hide animation more than 40 pixels down.
- No-food message: Backspace only hides it while you can see it; the 3-second wait and the hidden state reset with each respawn.

## 0.3.0

- Weight moved beside the food icons and rebuilt like the health bar: a weight icon next to the fork, a vertical bar rising above it, and your weight over your max on top. New settings for colors (thresholds, gradient or single, with all three colors editable), bar length and thickness, text style and size, growth with max weight, and a nudge offset. The old hotbar placement is gone.
- New: the health bar continues in a faint red segment showing the max health your foods have lost as they wear off (a food at half time gives about 81% of its health).
- Skill XP bars rebuilt as slim bars centered under the stamina bar, stacking down. New settings: name-and-level or icon-only labels, width, height, color, and position (below stamina, right side or custom) with nudge offsets. Default max bars is now 4.
- New: blue floating XP text over your character, like a damage number, only on your screen. Every gain, level-ups only, or off.
- Food reminders: separate switches for running out (on) and can eat again (off, new). New no-food message that stays up until you eat or press Backspace; it waits for the arrival on a new character and shows again after a respawn.
- Settings window: colors show a swatch and an editable hex value. Preview also shows a sample max-health segment.

## 0.2.3

- Food reminder: removed the "eat again" text; the time left is right-aligned with fixed-width digits so the countdown no longer jiggles.

## 0.2.2

- Weight bar has a steady size and position: about two hotbar slots wide at 300 max weight, growing with your carry capacity (Megingjord makes it 50% longer), anchored to the first hotbar slot. It no longer stretches or shifts with the items and gear you have.

## 0.2.1

- Fixed the food reminder text showing rotated 90 degrees.

## 0.2.0

- Food reminder: when a food drops under 4 minutes, a small reminder with its icon, time left and "eat again" appears at the top of the screen, with a soft chime. Once per food, fades after 6 seconds.
- The settings window's Preview button now previews every feature on the HUD (XP bars and the food reminder, with the chime).

## 0.1.2

- The F7 settings window title shows the installed version, so you can see which one you are running.

## 0.1.1

- Mod page shows only player information; build and release notes moved to GitHub.

## 0.1.0

- Skill XP bars: a Skills-tab style bar appears while you earn XP in a skill, shows the XP gained since it appeared in a lighter shade, and fades out after 3 seconds without XP. Up to 5 stack on the right side of the screen.
- Built-in settings window (F7) with on/off buttons, sliders, per-setting reset and a live preview of the XP bars. No ConfigurationManager needed.
- Weight on the HUD: a slim bar above the hotbar with current / max inside, matching the inventory screen, with near-limit and over-limit colors, Always / Near Limit / When Changed modes and Above / Below Hotbar / Custom placement.
