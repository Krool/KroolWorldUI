# Changelog

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
