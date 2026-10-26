# Translations

Krool World UI follows the language picked in Valheim's own settings. Each language has two
files here, named like Valheim's languages (`German`, `Portuguese_Brazilian`, `Chinese_Trad`):

- `<Language>.txt`: text on the HUD and the settings window's buttons. Source: `English.txt`.
- `<Language>.settings.txt`: every setting's name, description, sub-heading and choice in the
  settings window. Source: `English.settings.txt`, which the build generates from the code.

Format: `key = text`, one per line, UTF-8. `$1`, `$2` are filled in by the mod and must stay.
Keep it short: most lines sit in small boxes. A line left out shows in English.

To improve a translation, edit the line and open a pull request, or paste the better wording
into an issue. `scripts\check-translations.ps1` lists missing lines and dropped placeholders.
