using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Building blocks for the stats window and the recap popups, all taken from the game's own
    /// Skills dialog (frame, row background, fonts, tooltip) so they look like the game. Icons are
    /// the game's: item icons, trophies for creatures, skill icons, status effect icons.
    /// Rows are laid out by hand (top-left anchors, y going down): no layout groups.
    /// </summary>
    internal static class StatsUi
    {
        // ------------------------------------------------------------------ colors

        internal static readonly Color Parchment = HudKit.Parchment;
        internal static readonly Color Gold = HudKit.Gold;
        internal static readonly Color Muted = new Color(0.66f, 0.6f, 0.5f, 1f);
        internal static readonly Color Faint = new Color(0.5f, 0.46f, 0.4f, 1f);
        internal static readonly Color White = new Color(0.96f, 0.94f, 0.9f, 1f);
        internal static Color Good => HudKit.Ready;
        internal static Color Bad => HudKit.Bad;

        private static readonly Color[] TypeColors =
        {
            new Color(0.80f, 0.66f, 0.46f), // blunt: bone
            new Color(0.82f, 0.84f, 0.86f), // slash: steel
            new Color(0.56f, 0.70f, 0.80f), // pierce: cold steel
            new Color(1.00f, 0.48f, 0.18f), // fire
            new Color(0.50f, 0.84f, 1.00f), // frost
            new Color(0.98f, 0.88f, 0.30f), // lightning
            new Color(0.55f, 0.83f, 0.35f), // poison
            new Color(0.80f, 0.64f, 1.00f), // spirit
            new Color(0.62f, 0.46f, 0.28f), // chop
            new Color(0.60f, 0.60f, 0.60f), // pickaxe
            new Color(0.70f, 0.62f, 0.52f), // other
        };

        internal static Color TypeColor(int type) => TypeColors[Mathf.Clamp(type, 0, TypeColors.Length - 1)];

        internal static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------------ numbers

        /// <summary>A whole number with the language's thousands separator, digits monospaced so columns line up.</summary>
        internal static string Int(float v) => Mono(Mathf.RoundToInt(v).ToString("#,0", L10n.Culture));

        internal static string Dec(float v, string format = "0.#") => Mono(v.ToString(format, L10n.Culture));

        /// <summary>"+12%" / "-8%" from a fraction (0.12). Zero shows as "0%".</summary>
        internal static string Pct(float fraction, bool sign = true)
        {
            int p = Mathf.RoundToInt(fraction * 100f);
            string s = Mathf.Abs(p).ToString(CultureInfo.InvariantCulture) + "%";
            if (sign && p > 0) s = "+" + s;
            else if (p < 0) s = "-" + s;
            return Mono(s);
        }

        internal static string Mono(string s) => $"<mspace=0.52em>{s}</mspace>";

        internal static string Time(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s >= 60 ? $"{s / 60}:{s % 60:00}" : L10n.T("seconds_short", s.ToString(CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ game art

        internal sealed class Art
        {
            public TMP_Text Body;      // the skill list's name text
            public TMP_Text Title;     // the Skills dialog's topic
            public Image RowBack;      // the skill row background
            public Image IconBack;     // the skill row's icon frame
            public Image Frame;        // the dialog's frame
            public Sprite Braid;       // the inventory's braided divider
            public GameObject Tooltip; // the game's tooltip prefab
        }

        private static Art _art;
        private static InventoryGui _artGui;

        /// <summary>The game's UI pieces to copy from, found once per inventory screen (one per login).</summary>
        internal static Art GetArt()
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_skillsDialog == null)
                return null;
            if (_art != null && ReferenceEquals(_artGui, gui) && _art.Body != null)
                return _art;
            var dialog = gui.m_skillsDialog;
            var element = dialog.m_elementPrefab != null ? dialog.m_elementPrefab.transform : null;
            var frame = dialog.transform.Find("SkillsFrame");
            var art = new Art
            {
                Body = element != null ? element.Find("name")?.GetComponent<TMP_Text>() : null,
                Title = frame != null ? frame.Find("topic")?.GetComponent<TMP_Text>() : null,
                RowBack = element != null ? element.Find("bkg")?.GetComponent<Image>() : null,
                IconBack = element != null ? element.Find("icon_bkg")?.GetComponent<Image>() : null,
                Frame = frame != null ? frame.Find("bkg")?.GetComponent<Image>() : null,
                Tooltip = element != null ? element.GetComponentInChildren<UITooltip>(true)?.m_tooltipPrefab : null,
            };
            var braid = gui.transform.Find("root/Info/TitlePanel/BraidLineHorisontalMedium (1)")?.GetComponent<Image>();
            art.Braid = braid != null ? braid.sprite : null;
            if (art.Body == null)
                art.Body = Hud.instance != null ? Hud.instance.m_healthText : null;
            if (art.Title == null)
                art.Title = art.Body;
            if (art.Body == null)
                return null;
            _art = art;
            _artGui = gui;
            return art;
        }

        // ------------------------------------------------------------------ building

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            TopLeft(rt);
            return rt;
        }

        internal static void TopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static TMP_Text Text(Art art, Transform parent, string text, float x, float y, float w, float h,
            float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool title = false)
        {
            var t = HudKit.NewText(title ? art.Title : art.Body, parent, "text", align, color, size);
            TopLeft(t.rectTransform);
            Place(t.rectTransform, x, y, w, h);
            t.text = text;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.fontStyle = title ? t.fontStyle : FontStyles.Normal;
            return t;
        }

        internal static Image Image(Transform parent, Sprite sprite, float x, float y, float w, float h, Color color)
        {
            var img = HudKit.NewImage("img", parent, color);
            img.sprite = sprite;
            img.preserveAspect = true;
            TopLeft(img.rectTransform);
            Place(img.rectTransform, x, y, w, h);
            return img;
        }

        internal static Image CopyOf(Image from, Transform parent, Color tint)
        {
            var img = HudKit.NewImage("bkg", parent, tint);
            if (from != null && from.sprite != null)
            {
                img.sprite = from.sprite;
                img.type = from.type;
                img.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
                img.color = new Color(from.color.r * tint.r, from.color.g * tint.g, from.color.b * tint.b, from.color.a * tint.a);
            }
            return img;
        }

        /// <summary>The game's skill-row background behind a row, faint, so long lists stay readable.</summary>
        internal static void RowBack(Art art, Transform parent, float y, float w, float h, float alpha = 0.55f)
        {
            var img = CopyOf(art.RowBack, parent, new Color(1f, 1f, 1f, alpha));
            if (art.RowBack == null)
                img.color = new Color(0f, 0f, 0f, 0.25f * alpha);
            TopLeft(img.rectTransform);
            Place(img.rectTransform, 0f, y, w, h);
        }

        /// <summary>A section heading in the game's title font with the braided line after it. Returns its height.</summary>
        internal static float Heading(Art art, Transform parent, string text, float y, float w)
        {
            var t = Text(art, parent, text, 2f, y, w, 30f, 21f, Gold, TextAlignmentOptions.BottomLeft, true);
            float tw = Mathf.Min(w * 0.6f, t.GetPreferredValues(text).x + 12f);
            if (art.Braid != null)
            {
                var line = Image(parent, art.Braid, tw, y + 10f, w - tw, 18f, new Color(1f, 1f, 1f, 0.55f));
                line.preserveAspect = false;
                line.type = UnityEngine.UI.Image.Type.Simple;
            }
            else
            {
                Image(parent, null, tw, y + 20f, w - tw, 1f, new Color(Gold.r, Gold.g, Gold.b, 0.35f));
            }
            return 36f;
        }

        /// <summary>Hover text for anything with a graphic; the game's own tooltip.</summary>
        internal static void Tip(Art art, GameObject go, string topic, string text)
        {
            if (art.Tooltip == null || string.IsNullOrEmpty(text) && string.IsNullOrEmpty(topic))
                return;
            var g = go.GetComponent<Graphic>();
            if (g != null)
                g.raycastTarget = true;
            var tip = go.GetComponent<UITooltip>() ?? go.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = art.Tooltip;
            tip.m_topic = topic ?? "";
            tip.m_text = text ?? "";
        }

        /// <summary>
        /// A bar split into the damage type colors, <paramref name="full"/> wide when it equals
        /// <paramref name="max"/>. Hover shows each type's share.
        /// </summary>
        internal static RectTransform TypeBar(Art art, Transform parent, float[] types, float max, float x, float y,
            float full, float h)
        {
            var bar = Rect("bar", parent);
            Place(bar, x, y, full, h);
            var back = HudKit.NewImage("back", bar, new Color(0f, 0f, 0f, 0.35f));
            TopLeft(back.rectTransform);
            Place(back.rectTransform, 0f, 0f, full, h);
            float total = Dmg.Sum(types);
            if (total <= 0f || max <= 0f)
                return bar;
            float width = Mathf.Max(2f, full * Mathf.Clamp01(total / max));
            float at = 0f;
            var tip = new System.Text.StringBuilder();
            foreach (int t in Order(types))
            {
                float w = width * types[t] / total;
                if (w < 0.5f)
                    continue;
                var seg = HudKit.NewImage("seg", bar, TypeColor(t));
                TopLeft(seg.rectTransform);
                Place(seg.rectTransform, at, 0f, w, h);
                at += w;
                tip.Append($"<color={Hex(TypeColor(t))}>{Dmg.Name(t)}</color>  {Int(types[t])}  ({Mathf.RoundToInt(types[t] / total * 100f)}%)\n");
            }
            Tip(art, back.gameObject, "", tip.ToString().TrimEnd());
            back.rectTransform.sizeDelta = new Vector2(full, h);
            return bar;
        }

        /// <summary>Damage types present, biggest first.</summary>
        internal static List<int> Order(float[] types)
        {
            var list = new List<int>();
            for (int i = 0; i < types.Length; i++)
                if (types[i] > 0.01f)
                    list.Add(i);
            list.Sort((a, b) => types[b].CompareTo(types[a]));
            return list;
        }

        /// <summary>A small square of each damage type's color with its name, for the types shown. Returns its height.</summary>
        internal static float Legend(Art art, Transform parent, float[] types, float y, float w, float size = 14f)
        {
            float x = 0f;
            float line = size + 8f;
            float h = line;
            foreach (int t in Order(types))
            {
                string name = Dmg.Name(t);
                var label = Text(art, parent, name, 0f, 0f, 200f, line, size, Muted);
                float tw = label.GetPreferredValues(name).x + 4f;
                if (x > 0f && x + 14f + tw > w)
                {
                    x = 0f;
                    y += line;
                    h += line;
                }
                Image(parent, null, x, y + (line - 10f) / 2f, 10f, 10f, TypeColor(t));
                Place(label.rectTransform, x + 14f, y, tw, line);
                x += 14f + tw + 14f;
            }
            return h;
        }

        // ------------------------------------------------------------------ icons

        private static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        internal static Sprite ItemIcon(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null)
                return null;
            if (_icons.TryGetValue("i:" + prefab, out var s))
                return s;
            var drop = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
            s = drop != null ? drop.m_itemData.GetIcon() : null;
            _icons["i:" + prefab] = s;
            return s;
        }

        internal static ItemDrop.ItemData ItemData(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null)
                return null;
            return ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData;
        }

        /// <summary>A creature's trophy, the picture the game uses for it everywhere else.</summary>
        internal static Sprite TrophyIcon(string creaturePrefab)
        {
            if (string.IsNullOrEmpty(creaturePrefab) || ZNetScene.instance == null)
                return null;
            if (_icons.TryGetValue("t:" + creaturePrefab, out var s))
                return s;
            var drops = ZNetScene.instance.GetPrefab(creaturePrefab)?.GetComponent<CharacterDrop>();
            if (drops != null)
            {
                foreach (var d in drops.m_drops)
                {
                    var item = d.m_prefab != null ? d.m_prefab.GetComponent<ItemDrop>() : null;
                    if (item != null && item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy)
                    {
                        s = item.m_itemData.GetIcon();
                        break;
                    }
                }
            }
            _icons["t:" + creaturePrefab] = s;
            return s;
        }

        internal static Sprite SkillIcon(Skills.SkillType skill)
        {
            var player = Player.m_localPlayer;
            if (player == null || skill == Skills.SkillType.None)
                return null;
            foreach (var def in player.GetSkills().m_skills)
                if (def.m_skill == skill)
                    return def.m_icon;
            return null;
        }

        internal static Sprite EffectIcon(string name)
        {
            if (ObjectDB.instance == null)
                return null;
            if (_icons.TryGetValue("s:" + name, out var s))
                return s;
            s = ObjectDB.instance.GetStatusEffect(name.GetStableHashCode())?.m_icon;
            _icons["s:" + name] = s;
            return s;
        }

        /// <summary>A picture for a damage type: the weapon skill that deals it, or its status effect.</summary>
        internal static Sprite TypeIcon(int type)
        {
            switch (type)
            {
                case Dmg.Blunt: return SkillIcon(Skills.SkillType.Clubs);
                case Dmg.Slash: return SkillIcon(Skills.SkillType.Swords);
                case Dmg.Pierce: return SkillIcon(Skills.SkillType.Spears);
                case Dmg.Fire: return EffectIcon("Burning");
                case Dmg.Frost: return EffectIcon("Frost");
                case Dmg.Lightning: return EffectIcon("Lightning");
                case Dmg.Poison: return EffectIcon("Poison");
                case Dmg.Spirit: return EffectIcon("Spirit");
                case Dmg.Chop: return SkillIcon(Skills.SkillType.Axes);
                case Dmg.Pickaxe: return SkillIcon(Skills.SkillType.Pickaxes);
                default: return null;
            }
        }

        /// <summary>The world hurting you: falling, drowning, burning...</summary>
        internal static Sprite CauseIcon(HitData.HitType cause)
        {
            switch (cause)
            {
                case HitData.HitType.Fall: return SkillIcon(Skills.SkillType.Jump);
                case HitData.HitType.Drowning:
                case HitData.HitType.Water: return SkillIcon(Skills.SkillType.Swim);
                case HitData.HitType.Freezing: return EffectIcon("Freezing");
                case HitData.HitType.Burning:
                case HitData.HitType.CinderFire:
                case HitData.HitType.AshlandsLava:
                case HitData.HitType.AshlandsOcean: return EffectIcon("Burning");
                case HitData.HitType.Poisoned: return EffectIcon("Poison");
                case HitData.HitType.Smoke: return EffectIcon("Smoked");
                case HitData.HitType.Tree: return ItemIcon("Wood");
                case HitData.HitType.Structural: return ItemIcon("Stone");
                case HitData.HitType.Self: return SkillIcon(Skills.SkillType.BloodMagic);
                default: return null;
            }
        }

        /// <summary>The name of the world cause, in the game's words where it has them.</summary>
        internal static string CauseName(HitData.HitType cause)
        {
            switch (cause)
            {
                case HitData.HitType.Fall: return L10n.Game("$item_falldamage", "Fall damage");
                case HitData.HitType.Freezing: return L10n.Game("$se_freezing_name", "Freezing");
                case HitData.HitType.Burning: return L10n.Game("$se_burning_name", "Burning");
                case HitData.HitType.Poisoned: return L10n.Game("$se_poison_name", "Poison");
                case HitData.HitType.Smoke: return L10n.Game("$se_smoked_name", "Smoked");
                case HitData.HitType.Drowning:
                case HitData.HitType.Water: return L10n.T("cause_drowning");
                case HitData.HitType.CinderFire:
                case HitData.HitType.AshlandsLava:
                case HitData.HitType.AshlandsOcean: return L10n.T("cause_lava");
                case HitData.HitType.Tree: return L10n.T("cause_tree");
                case HitData.HitType.Self: return L10n.T("cause_self");
                default: return L10n.T("cause_world");
            }
        }

        /// <summary>A name for whoever hit: a player's name, a creature's name, or the world cause.</summary>
        internal static string WhoName(Who who)
        {
            if (who.IsPlayer)
                return who.Name;
            if (string.IsNullOrEmpty(who.Name))
                return CauseName(who.Cause);
            return Localization.instance != null ? Localization.instance.Localize(who.Name) : who.Name;
        }

        /// <summary>The name of whoever hit, with the game's level stars after a creature's name.</summary>
        internal static TMP_Text WhoLabel(Art art, Transform parent, Who who, float x, float y, float w, float h,
            float size, Color color)
        {
            string name = WhoName(who);
            int stars = who.IsPlayer ? 0 : Mathf.Clamp(who.Level - 1, 0, 5);
            var star = stars > 0 ? StarSprite() : null;
            float starSize = Mathf.Round(size * 0.8f);
            float room = star != null ? stars * (starSize - 2f) + 4f : 0f;
            var t = Text(art, parent, name, x, y, w - room, h, size, color);
            if (star == null)
                return t;
            float sx = x + Mathf.Min(w - room, t.GetPreferredValues(name).x) + 3f;
            for (int i = 0; i < stars; i++)
                Image(parent, star, sx + i * (starSize - 2f), y + (h - starSize) / 2f, starSize, starSize, Color.white);
            return t;
        }

        private static Sprite _star;

        /// <summary>The star the game draws over a creature's health bar for each level.</summary>
        internal static Sprite StarSprite()
        {
            if (_star != null)
                return _star;
            var hud = EnemyHud.instance;
            var level = hud != null && hud.m_baseHud != null ? hud.m_baseHud.transform.Find("level_2") : null;
            var img = level != null ? level.GetComponentInChildren<Image>(true) : null;
            _star = img != null ? img.sprite : null;
            return _star;
        }

        internal static Sprite WhoIcon(Who who)
        {
            if (who.IsPlayer)
                return null;
            if (!string.IsNullOrEmpty(who.Prefab))
                return TrophyIcon(who.Prefab);
            return CauseIcon(who.Cause);
        }

        internal static void ClearCache() => _icons.Clear();
    }
}
