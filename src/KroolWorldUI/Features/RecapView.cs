using System;
using TMPro;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Draws a death recap or a boss recap into a container, top-left first, and returns the
    /// height used. The same layout serves the popups (compact) and the stats window (full).
    /// Rows are sorted by damage; numbers sit in right-aligned columns with monospaced digits.
    /// </summary>
    internal static class RecapView
    {
        private const float Icon = 26f;
        private const float Row = 30f;
        private const float SubRow = 26f;

        // ------------------------------------------------------------------ death

        internal static float Death(StatsUi.Art art, RectTransform root, DeathRecapData d, float w, bool compact,
            string when = null)
        {
            float y = 0f;
            y = Header(art, root, StatsUi.WhoIcon(d.Killer),
                L10n.T("death_killed_by", StatsUi.WhoName(d.Killer)),
                Join(L10n.T("death_blow", StatsUi.Int(d.KillingBlow)) +
                     (d.Overkill >= 1f ? $" <color={StatsUi.Hex(StatsUi.Faint)}>({L10n.T("death_over", StatsUi.Int(d.Overkill))})</color>" : ""),
                    L10n.T("death_total", StatsUi.Int(d.Taken), StatsUi.Time(d.Seconds)), when),
                w, y);

            // Columns from the right edge: bar, amount, count; the name takes the rest.
            float barW = compact ? 120f : 220f;
            float barX = w - barW;
            float amountX = barX - 6f - 64f;
            float countX = amountX - 6f - 40f;
            float max = 0f;
            foreach (var r in d.Rows)
                max = Mathf.Max(max, r.Taken);
            var all = new float[Dmg.Count];
            foreach (var r in d.Rows)
                for (int t = 0; t < Dmg.Count; t++)
                    all[t] += r.Types[t];
            int i = 0;
            foreach (var r in d.Rows)
            {
                if (compact && i == MaxCompactRows)
                {
                    More(art, root, d.Rows.Count - i, y, w);
                    y += 24f;
                    break;
                }
                if (i++ % 2 == 0)
                    StatsUi.RowBack(art, root, y, w, Row, 0.35f);
                IconCell(root, StatsUi.WhoIcon(r.Who), 2f, y, Row);
                var name = StatsUi.WhoLabel(art, root, r.Who, Icon + 10f, y, countX - Icon - 14f, Row, 17f,
                    r.Who.Key == d.Killer.Key ? StatsUi.Gold : StatsUi.Parchment);
                int count = r.Hits + r.Ticks;
                StatsUi.Text(art, root, StatsUi.Mono("×" + count), countX, y, 40f, Row, 15f, StatsUi.Muted,
                    TextAlignmentOptions.MidlineRight);
                StatsUi.Text(art, root, StatsUi.Int(r.Taken), amountX, y, 64f, Row, 18f, StatsUi.White,
                    TextAlignmentOptions.MidlineRight);
                StatsUi.TypeBar(art, root, r.Types, max, barX, y + 9f, barW, Row - 18f);
                string tip = RowTip(r);
                if (tip.Length > 0)
                    StatsUi.Tip(art, name.gameObject, StatsUi.WhoName(r.Who), tip);
                y += Row;
            }
            y += 8f;
            y += StatsUi.Legend(art, Sub(root, 0f, y, w), all, 0f, w);
            if (d.Stopped >= 1f)
            {
                StatsUi.Text(art, root, L10n.T("death_stopped", StatsUi.Int(d.Stopped)), 0f, y + 2f, w, 22f, 15f, StatsUi.Muted);
                y += 24f;
            }
            return y;
        }

        private static string RowTip(DeathRecapData.Row r)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.Hits > 0)
                parts.Add(L10n.T("death_hits", r.Hits.ToString()));
            if (r.Blocked > 0)
                parts.Add(L10n.T("death_blocked", r.Blocked.ToString()));
            if (r.Ticks > 0)
                parts.Add(L10n.T("death_ticks", r.Ticks.ToString()));
            return string.Join("\n", parts);
        }

        // ------------------------------------------------------------------ boss

        internal static float Boss(StatsUi.Art art, RectTransform root, BossRecapData b, float w, bool compact,
            string when = null)
        {
            float y = 0f;
            string name = Localization.instance != null ? Localization.instance.Localize(b.Name) : b.Name;
            y = Header(art, root, StatsUi.TrophyIcon(b.Prefab), name,
                Join(L10n.T("boss_defeated_in", StatsUi.Time(b.Seconds)),
                    L10n.T("boss_health", StatsUi.Int(b.MaxHealth)), when),
                w, y);

            // Columns from the right edge: (top weapons,) bar, share, amount, hits or damage per second.
            float iconsW = compact ? 3 * 22f + 4f : 0f;
            float barW = compact ? 64f : 170f;
            float barX = w - iconsW - barW;
            float pctX = barX - 6f - 46f;
            float amountX = pctX - 6f - 70f;
            float countX = amountX - 6f - 54f;
            float nameEnd = compact ? amountX - 6f : countX - 6f;
            float top = 0f;
            foreach (var f in b.Fighters)
                top = Mathf.Max(top, f.Total);
            var all = new float[Dmg.Count];
            foreach (var f in b.Fighters)
                for (int t = 0; t < Dmg.Count; t++)
                    all[t] += f.Types[t];

            int shown = 0;
            foreach (var f in b.Fighters)
            {
                if (compact && shown == MaxCompactRows)
                {
                    More(art, root, b.Fighters.Count - shown, y, w);
                    y += 24f;
                    break;
                }
                shown++;
                StatsUi.RowBack(art, root, y, w, Row + 2f, f.IsLocal ? 0.8f : 0.5f);
                var icon = f.Who.IsPlayer ? null : StatsUi.WhoIcon(f.Who);
                IconCell(root, icon, 2f, y + 1f, Row);
                float nameX = icon != null ? Icon + 10f : 8f;
                StatsUi.WhoLabel(art, root, f.Who, nameX, y + 1f, nameEnd - nameX, Row, 19f,
                    f.IsLocal ? StatsUi.Gold : StatsUi.Parchment);
                float dps = b.Seconds > 1f ? f.Total / b.Seconds : 0f;
                if (dps > 0f && !compact)
                    StatsUi.Text(art, root, L10n.T("dps", StatsUi.Int(dps)), countX, y + 1f, 54f, Row, 14f, StatsUi.Muted,
                        TextAlignmentOptions.MidlineRight);
                string total = (f.Estimated ? "~" : "") + StatsUi.Int(f.Total);
                var tt = StatsUi.Text(art, root, total, amountX, y + 1f, 70f, Row, 19f, StatsUi.White, TextAlignmentOptions.MidlineRight);
                if (f.Estimated)
                    StatsUi.Tip(art, tt.gameObject, "", L10n.T("boss_estimated"));
                StatsUi.Text(art, root, StatsUi.Pct(b.Total > 0f ? f.Total / b.Total : 0f, false), pctX, y + 1f, 46f, Row, 16f,
                    StatsUi.Muted, TextAlignmentOptions.MidlineRight);
                StatsUi.TypeBar(art, root, f.Types, top, barX, y + 10f, barW, Row - 18f);
                if (compact)
                {
                    // The fighter's three biggest sources, as pictures.
                    float ix = barX + barW + 4f;
                    int n = 0;
                    foreach (var s in f.Sources)
                    {
                        var sprite = SourceIcon(s, out _);
                        if (sprite == null)
                            continue;
                        var img = StatsUi.Image(root, sprite, ix + n * 22f, y + 5f, 22f, 22f, Color.white);
                        StatsUi.Tip(art, img.gameObject, SourceName(s), $"{StatsUi.Int(s.Total)}  ({Mathf.RoundToInt(s.Total / Mathf.Max(1f, f.Total) * 100f)}%)");
                        if (++n == 3)
                            break;
                    }
                }
                y += Row + 4f;

                if (compact)
                    continue;
                float stop = 0f;
                foreach (var s in f.Sources)
                    stop = Mathf.Max(stop, s.Total);
                foreach (var s in f.Sources)
                {
                    float x = 24f;
                    var main = SourceIcon(s, out var weapon);
                    if (weapon != null)
                    {
                        // A bow and its arrow: the bow small behind, the arrow in front.
                        StatsUi.Image(root, weapon, x - 6f, y + 3f, 18f, 18f, new Color(1f, 1f, 1f, 0.75f));
                    }
                    IconCell(root, main, x + 4f, y, SubRow, 22f);
                    StatsUi.Text(art, root, SourceName(s), x + 32f, y, countX - x - 38f, SubRow, 16f, StatsUi.Parchment);
                    if (s.Hits > 0)
                        StatsUi.Text(art, root, StatsUi.Mono("×" + s.Hits), countX, y, 54f, SubRow, 14f, StatsUi.Muted,
                            TextAlignmentOptions.MidlineRight);
                    StatsUi.Text(art, root, StatsUi.Int(s.Total), amountX, y, 70f, SubRow, 16f, StatsUi.Parchment,
                        TextAlignmentOptions.MidlineRight);
                    StatsUi.Text(art, root, StatsUi.Pct(f.Total > 0f ? s.Total / f.Total : 0f, false), pctX, y, 46f, SubRow, 14f,
                        StatsUi.Faint, TextAlignmentOptions.MidlineRight);
                    StatsUi.TypeBar(art, root, s.Types, stop, barX, y + 9f, barW, SubRow - 18f);
                    y += SubRow;
                }
                y += 8f;
            }

            y += 4f;
            y += StatsUi.Legend(art, Sub(root, 0f, y, w), all, 0f, w);
            if (b.MaxHealth > 0f && b.Total < b.MaxHealth * 0.9f)
            {
                StatsUi.Text(art, root, L10n.T("boss_partial"), 0f, y + 2f, w, 22f, 14f, StatsUi.Faint);
                y += 24f;
            }
            return y;
        }

        /// <summary>A recap line's picture: the arrow or bolt, else the weapon, else the skill or damage type. <paramref name="weapon"/> is the bow behind an arrow.</summary>
        internal static Sprite SourceIcon(RecapSource s, out Sprite weapon)
        {
            weapon = null;
            var ammo = StatsUi.ItemIcon(s.Ammo);
            var wpn = StatsUi.ItemIcon(s.Weapon);
            if (ammo != null)
            {
                weapon = wpn;
                return ammo;
            }
            if (wpn != null)
                return wpn;
            if (s.Skill != 0)
                return StatsUi.SkillIcon((Skills.SkillType)s.Skill);
            return StatsUi.TypeIcon(s.Type >= 0 ? s.Type : Dmg.Largest(s.Types));
        }

        internal static string SourceName(RecapSource s)
        {
            if (!string.IsNullOrEmpty(s.Effect))
                return EpicLootBridge.Name(s.Effect, L10n.T("src_" + s.Effect.ToLowerInvariant()));
            var item = StatsUi.ItemData(!string.IsNullOrEmpty(s.Ammo) ? s.Ammo : s.Weapon);
            if (item != null)
                return Localization.instance != null ? Localization.instance.Localize(item.m_shared.m_name) : item.m_shared.m_name;
            if (s.Skill != 0)
                return L10n.Game("$skill_" + ((Skills.SkillType)s.Skill).ToString().ToLowerInvariant(), ((Skills.SkillType)s.Skill).ToString());
            return Dmg.Name(s.Type >= 0 ? s.Type : Dmg.Largest(s.Types));
        }

        // ------------------------------------------------------------------ shared

        private static float Header(StatsUi.Art art, RectTransform root, Sprite icon, string title, string sub, float w, float y)
        {
            float x = 0f;
            if (icon != null)
            {
                StatsUi.Image(root, icon, 0f, y, 46f, 46f, Color.white);
                x = 56f;
            }
            StatsUi.Text(art, root, title, x, y - 2f, w - x, 30f, 25f, StatsUi.Gold, TextAlignmentOptions.MidlineLeft, true);
            StatsUi.Text(art, root, sub, x, y + 26f, w - x, 22f, 15f, StatsUi.Muted);
            return y + 58f;
        }

        private const int MaxCompactRows = 6;

        /// <summary>"+3 more" under a popup's list, which stops at six rows.</summary>
        private static void More(StatsUi.Art art, RectTransform root, int count, float y, float w)
        {
            StatsUi.Text(art, root, L10n.T("compass_more", count.ToString()), 8f, y, w - 8f, 22f, 15f, StatsUi.Faint);
        }

        private static void IconCell(RectTransform root, Sprite sprite, float x, float y, float rowH, float size = Icon)
        {
            if (sprite != null)
                StatsUi.Image(root, sprite, x, y + (rowH - size) / 2f, size, size, Color.white);
        }

        private static RectTransform Sub(RectTransform root, float x, float y, float w)
        {
            var r = StatsUi.Rect("legend", root);
            StatsUi.Place(r, x, y, w, 20f);
            return r;
        }

        private static string Join(params string[] parts)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var p in parts)
                if (!string.IsNullOrEmpty(p))
                    list.Add(p);
            return string.Join("  ·  ", list);
        }

        /// <summary>"Just now", "12 min ago", "2 h ago".</summary>
        internal static string Ago(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalMinutes < 1)
                return L10n.T("ago_now");
            if (span.TotalHours < 1)
                return L10n.T("ago_min", ((int)span.TotalMinutes).ToString());
            return L10n.T("ago_hour", ((int)span.TotalHours).ToString());
        }
    }
}
