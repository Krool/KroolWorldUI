using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Building support, before it happens. While placing, a piece that would break as soon as it is
    /// built flashes (and can show the support color it will have). While pointing the hammer at a
    /// built piece, what would fall if it were removed turns red and what would stand with clearly
    /// less support turns orange. Both use the game's own support rule (SupportModel). Display only:
    /// the tints are the same material highlight the hammer already uses on the piece you point at.
    /// </summary>
    internal static class BuildSupport
    {
        internal enum ShowWhen { KeyHeld, Always }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> BreakWarning;
        internal static ConfigEntry<bool> SupportColor;
        internal static ConfigEntry<bool> FallPreview;
        internal static ConfigEntry<ShowWhen> Show;
        internal static ConfigEntry<KeyboardShortcut> PreviewKey;
        internal static ConfigEntry<bool> ShowWeaker;
        internal static ConfigEntry<bool> Note;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;

        private const string Section = "Build Support";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "With the hammer out, warns when a piece you place would break, and shows what would fall if you removed one.");

            const string placing = "Placing";
            BreakWarning = config.Bind(Section, "BreakWarning", true,
                ConfigText.Desc("A piece that would break as soon as you build it flashes red, with a note above the build menu.",
                    group: placing));
            SupportColor = config.Bind(Section, "SupportColor", false,
                ConfigText.Desc("Color the piece you are placing by the support it will have, in the hammer's colors: " +
                                "blue on the ground, then green to red as support drops.", group: placing));

            const string removing = "Removing";
            FallPreview = config.Bind(Section, "FallPreview", true,
                ConfigText.Desc("Point the hammer at a built piece to see what would fall if you removed it (red) " +
                                "and what would stand with less support (orange).", group: removing));
            Show = config.Bind(Section, "ShowWhen", ShowWhen.KeyHeld,
                ConfigText.Desc("Key Held: while you hold Preview Key. Always: whenever you point at a piece. " +
                                "On a controller it always shows.", group: removing,
                    when: () => FallPreview.Value, whenText: "Fall Preview is on"));
            PreviewKey = config.Bind(Section, "PreviewKey", new KeyboardShortcut(KeyCode.LeftAlt),
                ConfigText.Desc("Hold to see what would fall.", group: removing,
                    when: () => FallPreview.Value && Show.Value == ShowWhen.KeyHeld,
                    whenText: "Show When is Key Held"));
            ShowWeaker = config.Bind(Section, "ShowWeaker", true,
                ConfigText.Desc("Also mark pieces that would stay up with clearly less support, in orange.", group: removing,
                    when: () => FallPreview.Value, whenText: "Fall Preview is on"));

            const string look = "Look";
            Note = config.Bind(Section, "Note", true,
                ConfigText.Desc("A short note above the build menu: Will break, or how many pieces would fall.", group: look));
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look,
                    () => Note.Value, "Note is on", 0.05f));
            Opacity = config.Bind(Section, "Opacity", 1f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.2f, 1f), look,
                    () => Note.Value, "Note is on", 0.05f));

            const string placement = "Placement";
            OffsetX = config.Bind(Section, "OffsetX", 0f,
                ConfigText.Desc("Move left (-) or right (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement, () => Note.Value, "Note is on"));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                ConfigText.Desc("Move down (-) or up (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement, () => Note.Value, "Note is on"));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    BuildSupportHud.Ensure();
            }
        }

        // The game sets or clears the ghost's red "can't place" tint at the end of this, every frame;
        // ours goes on after it.
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class AfterGhost
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    BuildSupportHud.AfterGhost(__instance);
            }
        }

        // The hammer's hover highlight clears a piece's tint 0.2 s after you look away, which would
        // also clear ours if the preview marked that piece since.
        [HarmonyPatch(typeof(WearNTear), "ResetHighlight")]
        private static class AfterResetHighlight
        {
            private static void Postfix(WearNTear __instance) => BuildSupportHud.Reapply(__instance);
        }
    }

    internal class BuildSupportHud : MonoBehaviour
    {
        private const float Height = 34f;
        private const float MinWidth = 120f;
        private const float Gap = 10f;
        private const float FadeSeconds = 0.15f;
        private const float RecheckSeconds = 1f;   // re-run the removal preview this often while pointing at one piece
        private const float GhostRecheckSeconds = 0.25f;
        private const float BudgetMs = 1.5f;       // removal work per frame
        private const int Cap = 500;               // affected pieces to look through at most
        private const float WeakerShare = 0.1f;    // "weaker": lost at least this share of the material's maximum

        private static BuildSupportHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();
        private static AccessTools.FieldRef<Player, GameObject> _ghostRef;
        private static int ColorId, EmissionId; // set in Ensure: no game calls in static initializers

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private RectTransform _buildInfo;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _alpha;

        // Removal preview: the running job, its result, and the result on screen.
        private IEnumerator _job;
        private SupportModel.Result _pending;
        private SupportModel.Result _shown;
        private float _jobStarted;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private class Tint
        {
            public float A;
            public bool Want;
            public bool Falls;
            public Color Base;
            public Color Applied;
        }

        private readonly Dictionary<WearNTear, Tint> _tints = new Dictionary<WearNTear, Tint>();
        private readonly List<WearNTear> _drop = new List<WearNTear>();
        private GameObject _hovered;

        // Placement ghost.
        private GameObject _ghost;
        private Color _ghostBase;
        private bool _ghostNeedsSync;
        private Vector3 _ghostPos;
        private Quaternion _ghostRot;
        private float _ghostCheckedAt;
        private bool _ghostKnown;
        private bool _ghostBreaks;
        private Color _ghostSupportColor;
        private float _ghostA;
        private Color _ghostColor;
        private bool _ghostPulse;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null
                || MaterialMan.instance == null)
                return;
            try
            {
                SupportModel.Init();
                if (_ghostRef == null)
                    _ghostRef = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
                ColorId = Shader.PropertyToID("_Color");
                EmissionId = Shader.PropertyToID("_EmissionColor");
                var go = new GameObject("KroolWorldUI_BuildSupport", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var b = go.AddComponent<BuildSupportHud>();
                _instance = b;
                b._rect = (RectTransform)go.transform;
                b._rect.pivot = Vector2.zero;
                b._rect.sizeDelta = new Vector2(MinWidth, Height);
                b._group = go.GetComponent<CanvasGroup>();
                b._group.alpha = 0f;
                b._group.blocksRaycasts = false;
                b._group.interactable = false;
                HudKit.ToastBackground(go.transform);
                b._text = HudKit.NewText(hud.m_healthText, go.transform, "text", TextAlignmentOptions.Center, HudKit.Parchment, 18f);
                b._buildInfo = hud.m_buildHud != null ? hud.m_buildHud.transform.Find("SelectedInfo") as RectTransform : null;
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Build support disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            foreach (var pair in _tints)
                if (pair.Key != null && pair.Key.gameObject != _hovered)
                    Clear(pair.Key.gameObject);
            _tints.Clear();
            if (_instance == this)
                _instance = null;
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
                Fail(e);
            }
        }

        private void Fail(System.Exception e)
        {
            _failed.Set();
            Plugin.Log.LogError($"Build support disabled for this session after an error: {e}");
            Destroy(gameObject);
        }

        private static bool BuildingFalls() =>
            ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildingFall);

        /// <summary>The hammer is out and the world is in view (not the build menu, inventory, map or pause menu).</summary>
        private static bool Building(Player player) =>
            player != null && player.InPlaceMode() && !player.IsDead() && !Hud.IsPieceSelectionVisible()
            && !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Menu.IsVisible();

        private static bool Wanted(Player player) =>
            BuildSupport.Enabled.Value && HudKit.Allowed && Building(player) && BuildingFalls();

        private void Tick()
        {
            var player = Player.m_localPlayer;
            bool wanted = Wanted(player);

            Piece target = null;
            if (wanted && BuildSupport.FallPreview.Value && Triggered()
                && player.GetBuildTool() != null && player.GetBuildTool().m_canRemovePieces)
            {
                var hover = player.GetHoveringPiece();
                if (hover != null && hover.m_canBeRemoved)
                    target = hover;
            }
            var hovered = player != null ? player.GetHoveringPiece() : null;
            _hovered = hovered != null ? hovered.gameObject : null;

            RunRemoval(target);
            UpdateTints();
            if (!wanted)
            {
                _ghostA = 0f; // the game resets the ghost's tint itself every frame
                _ghostBreaks = false;
            }
            UpdateNote(player, wanted, target);
        }

        private static bool Triggered()
        {
            if (BuildSupport.Show.Value == BuildSupport.ShowWhen.Always || ZInput.IsGamepadActive())
                return true;
            if (General.TextFocus() || SettingsWindow.IsCapturing || SettingsWindow.IsTyping)
                return false;
            var key = BuildSupport.PreviewKey.Value;
            return key.MainKey != KeyCode.None && key.IsPressed();
        }

        // ---- Removal preview ----

        private void RunRemoval(Piece target)
        {
            if (target == null)
            {
                _job = null;
                _pending = null;
                _shown = null;
                return;
            }
            if (_shown != null && _shown.Target != target)
                _shown = null;
            bool stale = _shown != null && _job == null && Time.unscaledTime - _jobStarted >= RecheckSeconds;
            if ((_job == null && _shown == null) || stale || (_pending != null && _pending.Target != target))
            {
                _pending = new SupportModel.Result { Target = target };
                _job = SupportModel.Removal(target, Cap, WeakerShare, _pending);
                _jobStarted = Time.unscaledTime;
            }
            if (_job == null)
                return;
            _clock.Restart();
            try
            {
                while (_clock.Elapsed.TotalMilliseconds < BudgetMs)
                {
                    if (!_job.MoveNext())
                    {
                        _job = null;
                        _shown = _pending;
                        _pending = null;
                        break;
                    }
                }
            }
            catch (MissingReferenceException)
            {
                // A piece was destroyed while this was being worked out: start over next time.
                _job = null;
                _pending = null;
            }
        }

        private void UpdateTints()
        {
            foreach (var t in _tints.Values)
                t.Want = false;
            if (_shown != null)
            {
                foreach (var w in _shown.Falls)
                    Want(w, true);
                if (BuildSupport.ShowWeaker.Value)
                    foreach (var w in _shown.Weaker)
                        Want(w, false);
            }

            float step = Time.deltaTime / FadeSeconds;
            _drop.Clear();
            foreach (var pair in _tints)
            {
                var w = pair.Key;
                var t = pair.Value;
                if (w == null)
                {
                    _drop.Add(w);
                    continue;
                }
                var go = w.gameObject;
                if (go == _hovered)
                {
                    // The hammer's own highlight owns the piece you point at.
                    if (!t.Want)
                        _drop.Add(w);
                    t.A = 0f;
                    continue;
                }
                float a = Mathf.MoveTowards(t.A, t.Want ? 1f : 0f, step);
                if (a <= 0f)
                {
                    Clear(go);
                    _drop.Add(w);
                    continue;
                }
                var color = t.Falls ? HudKit.Bad : HudKit.Warn;
                if (!Mathf.Approximately(a, t.A) || color != t.Applied)
                {
                    t.A = a;
                    t.Applied = color;
                    Apply(go, t);
                }
            }
            foreach (var w in _drop)
                _tints.Remove(w);
        }

        private void Want(WearNTear w, bool falls)
        {
            if (w == null)
                return;
            if (!_tints.TryGetValue(w, out var t))
            {
                t = new Tint { Base = BaseColor(w.gameObject) };
                _tints[w] = t;
            }
            t.Want = true;
            if (t.Falls != falls)
            {
                t.Falls = falls;
                t.Applied = default; // color changed: apply again
            }
        }

        private static void Apply(GameObject go, Tint t)
        {
            var color = t.Falls ? HudKit.Bad : HudKit.Warn;
            MaterialMan.instance.SetValue(go, ColorId, Color.Lerp(t.Base, color, t.A));
            MaterialMan.instance.SetValue(go, EmissionId, color * (0.4f * t.A));
        }

        private static void Clear(GameObject go)
        {
            if (MaterialMan.instance == null)
                return;
            MaterialMan.instance.ResetValue(go, ColorId);
            MaterialMan.instance.ResetValue(go, EmissionId);
        }

        internal static void Reapply(WearNTear w)
        {
            try
            {
                var me = _instance;
                if (me != null && w != null && me._tints.TryGetValue(w, out var t) && t.A > 0f && w.gameObject != me._hovered)
                    Apply(w.gameObject, t);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Build support: {e}");
            }
        }

        /// <summary>The piece's own material color, so a tint fades in from it instead of jumping.</summary>
        private static Color BaseColor(GameObject go)
        {
            var renderer = go.GetComponentInChildren<MeshRenderer>();
            var material = renderer != null ? renderer.sharedMaterial : null;
            return material != null && material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
        }

        // ---- Placement ghost ----

        internal static void AfterGhost(Player player)
        {
            var me = _instance;
            if (me == null || _failed.IsSet)
                return;
            try
            {
                me.Ghost(player);
            }
            catch (System.Exception e)
            {
                me.Fail(e);
            }
        }

        private void Ghost(Player player)
        {
            var ghost = _ghostRef(player);
            bool valid = ghost != null && ghost.activeSelf && player.GetPlacementStatus() == Player.PlacementStatus.Valid
                         && Wanted(player) && (BuildSupport.BreakWarning.Value || BuildSupport.SupportColor.Value);
            if (!valid)
            {
                // The game has just set (red) or cleared the ghost's tint; leave it be.
                _ghostA = 0f;
                _ghostBreaks = false;
                _ghostKnown = false;
                return;
            }
            if (ghost != _ghost)
            {
                _ghost = ghost;
                _ghostBase = BaseColor(ghost);
                _ghostNeedsSync = SupportModel.HasNonBoxCollider(ghost);
                _ghostCheckedAt = -1f;
            }

            var tr = ghost.transform;
            if (tr.position != _ghostPos || tr.rotation != _ghostRot || Time.unscaledTime - _ghostCheckedAt >= GhostRecheckSeconds)
            {
                _ghostPos = tr.position;
                _ghostRot = tr.rotation;
                _ghostCheckedAt = Time.unscaledTime;
                // Non-box colliders report bounds from the physics scene, which has not seen this frame's move yet.
                if (_ghostNeedsSync)
                    Physics.SyncTransforms();
                _ghostKnown = SupportModel.Ghost(ghost, out float support, out float min, out float max);
                _ghostBreaks = _ghostKnown && support < min;
                if (_ghostKnown)
                    _ghostSupportColor = SupportModel.HammerColor(support, min, max);
            }

            bool tint = false;
            if (_ghostBreaks && BuildSupport.BreakWarning.Value)
            {
                _ghostColor = HudKit.Bad;
                _ghostPulse = true;
                tint = true;
            }
            else if (_ghostKnown && BuildSupport.SupportColor.Value)
            {
                _ghostColor = _ghostSupportColor;
                _ghostPulse = false;
                tint = true;
            }
            _ghostA = Mathf.MoveTowards(_ghostA, tint ? 1f : 0f, Time.deltaTime / FadeSeconds);
            if (_ghostA <= 0f)
                return;
            // Breaking: a slow pulse, so it reads apart from the game's steady red "can't place".
            float glow = _ghostPulse ? 0.2f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : 0.4f;
            MaterialMan.instance.SetValue(ghost, ColorId, Color.Lerp(_ghostBase, _ghostColor, _ghostA));
            MaterialMan.instance.SetValue(ghost, EmissionId, _ghostColor * (glow * _ghostA));
        }

        // ---- Note above the build menu ----

        private void UpdateNote(Player player, bool wanted, Piece target)
        {
            string text = null;
            bool preview = SettingsWindow.IsOpen && SettingsWindow.Previewing;
            if (BuildSupport.Note.Value && BuildSupport.Enabled.Value && HudKit.Allowed)
            {
                if (preview && !wanted)
                    text = Count(12, 3, false);
                else if (wanted && target != null && _shown != null && _shown.Target == target)
                {
                    int weaker = BuildSupport.ShowWeaker.Value ? _shown.Weaker.Count : 0;
                    // Always-on: only speak up when something would happen. Key held: always answer.
                    bool quietWhenNothing = BuildSupport.Show.Value == BuildSupport.ShowWhen.Always || ZInput.IsGamepadActive();
                    if (_shown.Falls.Count > 0 || weaker > 0 || !quietWhenNothing)
                        text = Count(_shown.Falls.Count, weaker, _shown.Capped);
                }
                else if (wanted && target == null && _ghostBreaks && _ghostA > 0f && BuildSupport.BreakWarning.Value)
                    text = $"<color=#{HudKit.Hex(HudKit.Bad)}>{L10n.T("support_breaks")}</color>";
            }

            if (text != null && text != _text.text)
            {
                _text.text = text;
                float width = HudKit.FitWidth(_text, text, MinWidth, 28f);
                _rect.sizeDelta = new Vector2(width, Height);
            }
            _alpha = Mathf.MoveTowards(_alpha, text != null ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            _group.alpha = _alpha * BuildSupport.Opacity.Value;
            if (_alpha <= 0f)
                return;
            float size = BuildSupport.Size.Value;
            if (!Mathf.Approximately(_rect.localScale.x, size))
                _rect.localScale = new Vector3(size, size, 1f);
            Place();
        }

        private static string Count(int falls, int weaker, bool capped)
        {
            if (falls == 0 && weaker == 0)
                return $"<color=#{HudKit.Hex(HudKit.Muted)}>{L10n.T("support_none")}</color>";
            string more = capped ? "+" : "";
            var parts = new List<string>();
            if (falls > 0)
                parts.Add($"<color=#{HudKit.Hex(HudKit.Bad)}>{L10n.T("support_fall", L10n.Num(falls, "0") + more)}</color>");
            if (weaker > 0)
                parts.Add($"<color=#{HudKit.Hex(HudKit.Warn)}>{L10n.T("support_weaker", L10n.Num(weaker, "0") + more)}</color>");
            return string.Join("    ", parts);
        }

        /// <summary>Centered above the build menu's selected-piece box, or bottom center without it, plus the offsets.</summary>
        private void Place()
        {
            var offset = new Vector2(BuildSupport.OffsetX.Value, BuildSupport.OffsetY.Value);
            var root = _rect.parent as RectTransform;
            float width = _rect.rect.width * _rect.localScale.x;
            if (root != null && _buildInfo != null && _buildInfo.gameObject.activeInHierarchy)
            {
                _buildInfo.GetWorldCorners(_corners);
                Vector3 boxMin = root.InverseTransformPoint(_corners[0]);
                Vector3 boxMax = root.InverseTransformPoint(_corners[2]);
                float x = (boxMin.x + boxMax.x) * 0.5f - width * 0.5f;
                _rect.localPosition = new Vector3(x + offset.x, boxMax.y + Gap + offset.y, 0f);
                return;
            }
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0f);
            _rect.anchoredPosition = new Vector2(-width * 0.5f, 160f) + offset;
        }
    }
}
