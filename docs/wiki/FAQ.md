# FAQ and troubleshooting

Quick answers for Krool World UI. The full feature list is on the mod's main page; every setting is in the in-game window (**F7**).

## Settings

**Where are the settings?**
Press **F7** in game, or open the pause menu and pick **Krool World UI**. Every feature has its own section with an On/Off switch on the header; point at or select any setting to see what it does, its default and its range. Turn on **Preview on HUD** to see every feature's sample while you adjust it.

**Can I use it with a controller or on a Steam Deck?**
Yes. Open the settings from the pause menu. The D-pad moves and changes values, A selects, B closes, X turns the preview on, Y resets a setting, and the bumpers jump between sections.

**Is it in my language?**
It follows the language you picked in Valheim's own settings, in every language the game offers. If a line reads oddly, open an issue with a better wording.

**Can I hide everything the mod draws for a moment?**
Set **General > Hide Key** to a key. It hides and shows all of the mod's elements.

**F7 is taken by another mod. Can I change it?**
Yes: **General > Settings Key** in the F7 window, or `SettingsKey` in the config file.

**Where is the config file?**
`BepInEx/config/krool.kroolworldui.cfg` in your r2modman profile (r2modman: Settings > Browse profile folder). You never need to edit it; the F7 window writes it for you.

**How do I put everything back to default?**
Each section header in F7 has a **Reset all** button. To reset the whole mod, delete the config file while the game is closed.

**Can I move things that overlap another mod's HUD?**
Most panels can be moved in F7: the compass, the build materials panel, the food reminder, the save countdown, the skill XP tiles and the weight bar each have position settings. Anything you do not want can be switched off.

## Multiplayer and servers

**Does the server need it?**
No. It is client-side only. Install it on your own game; the server and other players do not need it.

**Can friends without the mod play with me?**
Yes. Players with and without it, or on different versions of it, play together normally.

**Will it cause an "incompatible version" or "incompatible mods" error?**
No. It takes no part in server version checks (it does not use Jotunn's or ServerSync's compatibility check), so a server never turns you away for having it or not having it. If you see that error, it comes from a mod that has to match on both sides.

**My dedicated server host's mod browser does not list it.**
That is expected: it is a client-side mod, and server hosts usually list server-side mods only. Add it to your own r2modman profile instead.

## Updating

**r2modman does not show the new version yet.**
Thunderstore rebuilds the mod list r2modman downloads on a schedule, so a new version can take up to an hour or so to appear there, even though the mod page already shows it. Use **Settings > Refresh online mod list** in r2modman, and try again a little later if it is not there yet.

## Features

**The compass does not show a friend.**
Friends nearby always show. Friends far away show only if they have **Visible on map** turned on, the same as on the game's map.

**The compass shows no pins, and I got no bench pin.**
On a world with the No Map modifier (or with your map turned off), the compass shows only your heading and friends in sight, and Bench Pins adds nothing: the world takes the map away on purpose. The day/night bar moves to the top right corner.

**Auto repair did not fix one of my items.**
It follows the same rules as the repair button: a workbench fixes what a workbench can, a forge fixes metal, and some items need a higher station level. Open the right station and it repairs on the spot.

**I built a bench and got no pin.**
No new pin is added within 50 meters of a pin you already have (adjustable in **Bench Pins > Skip Near Pin Meters**), so one base gets one pin. By default only workbenches get pins; set **Pin Stations** to **All Crafting Stations** to pin forges and the rest too.

**I took a bench down and the pin went with it.**
That is on purpose, so a bench dropped to fix a boat leaves no trace. If another bench stands nearby, the pin moves to it instead. Turn off **Bench Pins > Remove With Bench** to keep pins. A bench lost to monsters or decay keeps its pin.

**What is the red X on a mead buff?**
That is the game's own cooldown marker, not this mod: you cannot drink another mead of that kind until its timer runs out.

**The buff icons got smaller.**
Krool World UI shows them at 90% and wraps them into a new row before they reach the compass. Set **Buffs > Size** to 1 for the game's own size, or turn the Buffs section off.

## Reporting a problem

Open an issue at [github.com/Krool/KroolWorldUI/issues](https://github.com/Krool/KroolWorldUI/issues) with:

- what you expected and what happened,
- a screenshot if it is about the HUD,
- your log file: `BepInEx/LogOutput.log` in your r2modman profile. Krool World UI logs any error with its feature name, and turns off only the feature that failed.
