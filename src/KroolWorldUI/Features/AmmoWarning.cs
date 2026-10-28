using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A short toast when a shot leaves you with few arrows, bolts or bait: once at the first
    /// count, once more at the second. Fires from Attack.UseAmmo, which spends exactly one per
    /// shot, so the count after a shot lands on each threshold instead of skipping it, and
    /// picking up or moving ammo never fires it. Counts the equipped stack, not spares. Read-only.
    /// </summary>
    internal static class AmmoWarning
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> FirstWarning;
        internal static ConfigEntry<int> SecondWarning;
        internal static ConfigEntry<float> ShowSeconds;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Low Ammo";

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Attack, Humanoid> _character;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Warn when a shot leaves you low on arrows, bolts or bait.");
            FirstWarning = config.Bind(Section, "FirstWarning", 25,
                ConfigText.Desc("Warn when this many are left. 0 = off.", new AcceptableValueRange<int>(0, 100)));
            SecondWarning = config.Bind(Section, "SecondWarning", 10,
                ConfigText.Desc("Warn again, in red, when this many are left. 0 = off.", new AcceptableValueRange<int>(0, 100)));
            ShowSeconds = config.Bind(Section, "ShowSeconds", 3f,
                ConfigText.Desc("Seconds the warning stays on screen.", new AcceptableValueRange<float>(1f, 10f), step: 0.5f));

            const string sound = "Sound";
            Sound = config.Bind(Section, "Sound", true, ConfigText.Desc("A short low tone with each warning.", group: sound));
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                ConfigText.Desc("Tone volume, on top of the game's own volume.", new AcceptableValueRange<float>(0.05f, 1f),
                    sound, () => Sound.Value, "Sound is on", 0.05f));

            const string look = "Look";
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look, step: 0.05f));
            Opacity = config.Bind(Section, "Opacity", 1f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.2f, 1f), look, step: 0.05f));

            const string placement = "Placement";
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                ConfigText.Desc("Center of the box. 0 is the left of the screen, 1 the right.",
                    new AcceptableValueRange<float>(0f, 1f), placement, step: 0.01f));
            AnchorY = config.Bind(Section, "AnchorY", 0.36f,
                ConfigText.Desc("Center of the box. 0 is the bottom of the screen, 1 the top.",
                    new AcceptableValueRange<float>(0f, 1f), placement, step: 0.01f));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    AmmoWarningHud.Ensure();
            }
        }

        // Runs after the shot's ammo left the inventory.
        [HarmonyPatch(typeof(Attack), "UseAmmo")]
        private static class AfterShot
        {
            private static void Postfix(Attack __instance, bool __result, ref ItemDrop.ItemData ammoItem)
            {
                if (!__result || ammoItem?.m_shared == null || !Enabled.Value)
                    return;
                try
                {
                    _character ??= AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
                    var player = Player.m_localPlayer;
                    if (player == null || !ReferenceEquals(_character(__instance), player))
                        return;
                    // The equipped stack only, not spares in the bag: the warning means "restock or swap".
                    int left = ammoItem.m_stack;
                    int first = FirstWarning.Value, second = SecondWarning.Value;
                    if (left <= 0 || (left != first && left != second))
                        return;
                    AmmoWarningHud.Show(ammoItem, left, last: left == second);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Low ammo: {e}");
                }
            }
        }
    }

    internal class AmmoWarningHud : MonoBehaviour
    {
        private const float Height = 42f;
        private const float IconSize = 32f;
        private const float Pad = 8f;
        private const float MinWidth = 150f;
        private const float FadeSeconds = 0.5f;

        private static AmmoWarningHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();
        private static AudioClip _tone, _lowTone;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _icon;
        private TMP_Text _text;
        private AudioSource _audio;
        private float _shownAt = -100f;
        private bool _previewing;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_LowAmmo", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var c = go.AddComponent<AmmoWarningHud>();
                _instance = c;
                c._rect = (RectTransform)go.transform;
                c._rect.pivot = new Vector2(0.5f, 0.5f);
                c._rect.sizeDelta = new Vector2(MinWidth, Height);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                HudKit.ToastBackground(go.transform);

                c._icon = HudKit.NewImage("icon", go.transform, Color.white);
                c._icon.preserveAspect = true;
                var irt = c._icon.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(IconSize, IconSize);
                irt.anchoredPosition = new Vector2(Pad, 0f);

                c._text = HudKit.NewText(hud.m_healthText, go.transform, "text", TextAlignmentOptions.MidlineLeft,
                    HudKit.Parchment, 20f);
                c._text.rectTransform.offsetMin = new Vector2(Pad + IconSize + 8f, 0f);
                c._text.rectTransform.offsetMax = new Vector2(-Pad, 0f);
                c._audio = HudKit.NewAudio(go);
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Low ammo disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        internal static void Show(ItemDrop.ItemData ammo, int left, bool last)
        {
            var c = _instance;
            if (c == null || _failed.IsSet)
                return;
            c.Fill(ammo, left, last);
            c._shownAt = Time.time;
            if (AmmoWarning.Sound.Value)
                c.PlayTone(last);
        }

        /// <summary>Settings-window preview: a sample warning for wood arrows, held while on.</summary>
        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed.IsSet || c._previewing == on)
                return;
            c._previewing = on;
            if (on)
            {
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("ArrowWood") : null;
                var item = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData : null;
                int count = AmmoWarning.FirstWarning.Value > 0 ? AmmoWarning.FirstWarning.Value : AmmoWarning.SecondWarning.Value;
                c.Fill(item, Mathf.Max(1, count), AmmoWarning.FirstWarning.Value == 0);
            }
            else
            {
                c._shownAt = -100f;
            }
        }

        private void Fill(ItemDrop.ItemData ammo, int left, bool last)
        {
            _icon.sprite = ammo != null ? ammo.GetIcon() : null;
            _icon.enabled = _icon.sprite != null;
            string raw = ammo?.m_shared?.m_name ?? "";
            string name = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
            var color = last ? HudKit.Bad : HudKit.Warn;
            string text = $"{name}  <color=#{HudKit.Hex(color)}>{L10n.T("ammo_left", left.ToString())}</color>";
            _text.text = text;
            _rect.sizeDelta = new Vector2(
                Mathf.Max(MinWidth, Pad + IconSize + 8f + Mathf.Ceil(_text.GetPreferredValues(text).x) + Pad + 4f), Height);
        }

        private void Update()
        {
            if (_failed.IsSet)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Low ammo disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            if (!AmmoWarning.Enabled.Value || !HudKit.Allowed || Player.m_localPlayer == null)
            {
                _group.alpha = 0f;
                return;
            }

            float alpha;
            if (_previewing)
                alpha = 1f;
            else
            {
                float age = Time.time - _shownAt, hold = AmmoWarning.ShowSeconds.Value;
                alpha = age <= hold ? 1f : Mathf.Clamp01(1f - (age - hold) / FadeSeconds);
            }
            if (!_previewing && (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible()))
                alpha = 0f;
            _group.alpha = alpha * AmmoWarning.Opacity.Value;
            if (alpha > 0f)
            {
                var anchor = new Vector2(AmmoWarning.AnchorX.Value, AmmoWarning.AnchorY.Value);
                _rect.anchorMin = anchor;
                _rect.anchorMax = anchor;
                _rect.anchoredPosition = Vector2.zero;
                float size = AmmoWarning.Size.Value;
                if (!Mathf.Approximately(_rect.localScale.x, size))
                    _rect.localScale = new Vector3(size, size, 1f);
            }
        }

        private void PlayTone(bool last)
        {
            if (_audio == null)
                return;
            // Explicit null checks: ??= skips Unity's destroyed-object check.
            if (_tone == null)
                _tone = HudKit.Tone("KroolWorldUI_AmmoLow", 0.3f, new HudKit.Note(392f, 0f, 0.28f, 12f));
            if (_lowTone == null)
                _lowTone = HudKit.Tone("KroolWorldUI_AmmoLast", 0.4f,
                    new HudKit.Note(392f, 0f, 0.18f, 14f), new HudKit.Note(311.13f, 0.14f, 0.25f, 12f));
            _audio.PlayOneShot(last ? _lowTone : _tone, General.Vol(AmmoWarning.SoundVolume.Value));
        }
    }
}
