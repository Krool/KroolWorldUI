using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Counts down to Rested while you rest. The game's "Resting" effect (SE_Cozy) adds Rested once
    /// it has run m_delay seconds, so the countdown is m_delay minus its elapsed time. Shown in the
    /// buff row, just above the Resting icon. Read-only.
    /// </summary>
    internal static class RestedCountdown
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> TextSize;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;

        private const string Section = "Rested Countdown";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "While you rest by a fire, count down the seconds until you are Rested, above the Resting icon.");
            TextSize = config.Bind(Section, "TextSize", 15f,
                ConfigText.Desc("Font size of the countdown.", new AcceptableValueRange<float>(11f, 26f)));
            Sound = config.Bind(Section, "Sound", true, "A soft chime when you become Rested.");
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                ConfigText.Desc("Chime volume, on top of the game's own volume.", new AcceptableValueRange<float>(0.05f, 1f),
                    when: () => Sound.Value, whenText: "Sound is on", step: 0.05f));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    RestedCountdownHud.Ensure();
            }
        }
    }

    internal class RestedCountdownHud : MonoBehaviour
    {
        private const float PreviewDelay = 10f;
        private const float PreviewGap = 2f;

        private static RestedCountdownHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();

        // For BuffIcons: the buff icon the countdown stands above (-1 when hidden) and its height in
        // the buff list's units, so a wrapped row under it can make room.
        internal static int RoomIcon = -1;
        internal static float RoomHeight;
        private static AudioClip _chime;

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Hud, List<RectTransform>> _icons;
        private static readonly List<StatusEffect> _effects = new List<StatusEffect>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private AudioSource _audio;
        private bool _counting;
        private bool _previewing;
        private float _previewStart;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_statusEffectListRoot == null || hud.m_statusEffectTemplate == null ||
                hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                _icons ??= AccessTools.FieldRefAccess<Hud, List<RectTransform>>("m_statusEffects");
                var go = new GameObject("KroolWorldUI_RestedCountdown", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_statusEffectListRoot, false);
                var c = go.AddComponent<RestedCountdownHud>();
                _instance = c;
                // Placed in world space above the Resting icon's name; bottom-center pivot so it sits on it.
                var template = hud.m_statusEffectTemplate;
                c._rect = (RectTransform)go.transform;
                c._rect.anchorMin = template.anchorMin;
                c._rect.anchorMax = template.anchorMax;
                c._rect.pivot = new Vector2(0.5f, 0f);
                c._rect.sizeDelta = new Vector2(template.sizeDelta.x >= 10f ? template.sizeDelta.x : 50f, 20f);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                c._text = HudKit.NewText(hud.m_healthText, go.transform, "text", TextAlignmentOptions.Bottom,
                    HudKit.Parchment, 15f);
                c._audio = HudKit.NewAudio(go);
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Rested countdown disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                RoomIcon = -1;
            }
        }

        /// <summary>Settings-window preview: loops a short countdown, chiming on the first pass only.</summary>
        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed.IsSet || c._previewing == on)
                return;
            c._previewing = on;
            c._previewStart = Time.unscaledTime;
            c._counting = false;
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
                Plugin.Log.LogError($"Rested countdown disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            var hud = Hud.instance;
            if (!RestedCountdown.Enabled.Value || !HudKit.Allowed || player == null || hud == null)
            {
                _group.alpha = 0f;
                _counting = false;
                RoomIcon = -1;
                return;
            }

            float delay, elapsed;
            bool resting, sound = RestedCountdown.Sound.Value;
            var cozy = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
            if (_previewing)
            {
                float t = Time.unscaledTime - _previewStart;
                delay = PreviewDelay;
                elapsed = t % (PreviewDelay + PreviewGap);
                resting = true;
                sound &= t < PreviewDelay + PreviewGap;
            }
            else
            {
                resting = cozy != null;
                delay = cozy != null ? cozy.m_delay : 0f;
                elapsed = cozy != null ? cozy.GetDuration() : 0f;
            }

            float remaining = delay - elapsed;
            if (resting && remaining > 0f)
            {
                _counting = true;
                int secs = Mathf.CeilToInt(remaining);
                string text = $"<size=85%>{L10n.T("rested_in", $"<color=#{HudKit.Hex(HudKit.Gold)}>{L10n.T("seconds_short", secs.ToString())}</color>")}</size>";
                if (_text.text != text)
                {
                    _text.text = text;
                    _rect.sizeDelta = new Vector2(HudKit.FitWidth(_text, text, 40f, 6f), _rect.sizeDelta.y);
                }
                if (!Mathf.Approximately(_text.fontSize, RestedCountdown.TextSize.Value))
                {
                    _text.fontSize = RestedCountdown.TextSize.Value;
                    _rect.sizeDelta = new Vector2(HudKit.FitWidth(_text, text, 40f, 6f), _text.fontSize + 6f);
                }
            }
            else if (_counting)
            {
                // Became Rested, or stopped resting before the end.
                _counting = false;
                if (resting && sound)
                    PlayChime();
            }

            _group.alpha = _counting ? 1f : 0f;
            if (_counting)
                PlaceAbove(hud, player, cozy);
            else
                RoomIcon = -1;
        }

        /// <summary>
        /// Just above the Resting icon's name label. Hud.UpdateStatusEffects lays its icons out in
        /// SEMan.GetHUDStatusEffects order, so the effect's index there is its icon's index.
        /// Without a Resting icon (preview), above the first icon.
        /// </summary>
        private void PlaceAbove(Hud hud, Player player, StatusEffect target)
        {
            var icons = _icons(hud);
            if (icons == null || icons.Count == 0)
            {
                _rect.anchoredPosition = new Vector2(-4f, 40f);
                RoomIcon = -1;
                return;
            }
            _effects.Clear();
            player.GetSEMan().GetHUDStatusEffects(_effects);
            int index = target != null ? _effects.IndexOf(target) : -1;
            if (index < 0 || index >= icons.Count)
                index = 0;
            RoomIcon = index;
            RoomHeight = _rect.sizeDelta.y + 2f;
            var icon = icons[index];
            if (icon == null)
                return;

            icon.GetWorldCorners(Corners);
            float x = (Corners[0].x + Corners[2].x) * 0.5f;
            float top = Corners[1].y;
            // The name label ("Resting") sits above the icon; stand on its drawn text, not its rect.
            var label = icon.GetComponentInChildren<TMP_Text>();
            if (label != null && label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text))
                top = Mathf.Max(top, label.transform.TransformPoint(label.bounds.max).y);
            float gap = 2f * _rect.lossyScale.y;
            // Keep the whole text on screen when the Resting icon is the rightmost one.
            var screen = hud.m_rootObject.transform as RectTransform;
            if (screen != null)
            {
                screen.GetWorldCorners(Corners);
                float half = _rect.rect.width * 0.5f * _rect.lossyScale.x;
                x = Mathf.Min(x, Corners[2].x - half - 4f * _rect.lossyScale.x);
            }
            _rect.position = new Vector3(x, top + gap, _rect.position.z);
        }

        private void PlayChime()
        {
            if (_audio == null)
                return;
            if (_chime == null)
                _chime = HudKit.Tone("KroolWorldUI_RestedChime", 0.6f,
                    new HudKit.Note(523.25f, 0f, 0.5f, 7f), new HudKit.Note(659.25f, 0.12f, 0.45f, 7f));
            _audio.PlayOneShot(_chime, General.Vol(RestedCountdown.SoundVolume.Value));
        }
    }
}
