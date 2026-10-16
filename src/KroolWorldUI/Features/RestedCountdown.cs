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
    /// next free slot left of the status effect icons, like one more buff. Read-only.
    /// </summary>
    internal static class RestedCountdown
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;

        private const string Section = "Rested Countdown";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "While you are resting (sitting or under a roof, near a fire), counts down the seconds until " +
                "you become Rested, beside your buff icons.");
            Sound = config.Bind(Section, "Sound", true, "A soft chime when you become Rested.");
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                new ConfigDescription("Chime volume (also follows the game's sound settings).",
                    new AcceptableValueRange<float>(0.05f, 1f)));
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
        private static bool _failed;
        private static AudioClip _chime;

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Hud, List<RectTransform>> _icons;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private AudioSource _audio;
        private bool _counting;
        private bool _previewing;
        private float _previewStart;

        internal static void Ensure()
        {
            if (_instance != null || _failed)
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
                // Same anchoring and size as a buff icon, so slot math matches Hud.UpdateStatusEffects.
                var template = hud.m_statusEffectTemplate;
                c._rect = (RectTransform)go.transform;
                c._rect.anchorMin = template.anchorMin;
                c._rect.anchorMax = template.anchorMax;
                c._rect.pivot = template.pivot;
                c._rect.sizeDelta = template.sizeDelta.x >= 10f ? template.sizeDelta : new Vector2(50f, 50f);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                c._text = HudKit.NewText(hud.m_healthText, go.transform, "text", TextAlignmentOptions.Center,
                    HudKit.Parchment, 15f);
                c._audio = HudKit.NewAudio(go);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Rested countdown disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Settings-window preview: loops a short countdown, chiming on the first pass only.</summary>
        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed || c._previewing == on)
                return;
            c._previewing = on;
            c._previewStart = Time.unscaledTime;
            c._counting = false;
        }

        private void Update()
        {
            if (_failed)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Rested countdown disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            var hud = Hud.instance;
            if (!RestedCountdown.Enabled.Value || player == null || hud == null)
            {
                _group.alpha = 0f;
                _counting = false;
                return;
            }

            float delay, elapsed;
            bool resting, sound = RestedCountdown.Sound.Value;
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
                var cozy = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
                resting = cozy != null;
                delay = cozy != null ? cozy.m_delay : 0f;
                elapsed = cozy != null ? cozy.GetDuration() : 0f;
            }

            float remaining = delay - elapsed;
            if (resting && remaining > 0f)
            {
                _counting = true;
                int secs = Mathf.CeilToInt(remaining);
                _text.text = $"<size=80%>Rested</size>\n<color=#{ColorUtility.ToHtmlStringRGB(HudKit.Gold)}>{secs}s</color>";
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
                PlaceInNextSlot(hud);
        }

        /// <summary>The slot just left of the last buff icon in the top row.</summary>
        private void PlaceInNextSlot(Hud hud)
        {
            var icons = _icons(hud);
            int slot = Mathf.Min(icons?.Count ?? 0, Mathf.Max(1, hud.m_effectsPerRow));
            _rect.anchoredPosition = new Vector2(-4f - slot * hud.m_statusEffectSpacing, 0f);
        }

        private void PlayChime()
        {
            if (_audio == null)
                return;
            if (_chime == null)
                _chime = HudKit.Tone("KroolWorldUI_RestedChime", 0.6f,
                    new HudKit.Note(523.25f, 0f, 0.5f, 7f), new HudKit.Note(659.25f, 0.12f, 0.45f, 7f));
            _audio.PlayOneShot(_chime, RestedCountdown.SoundVolume.Value);
        }
    }
}
