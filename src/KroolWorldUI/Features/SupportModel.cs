using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// The game's building support rule (WearNTear.UpdateSupport), worked out on the side so the mod
    /// can ask "what if": the support a piece would get where the build ghost stands, and what would
    /// fall if a piece were removed. Same padded collider boxes, material losses and two-sided rule
    /// as the game; other pieces' values are the game's own (synced from whoever owns them).
    /// Read-only: nothing here writes to a piece.
    /// </summary>
    internal static class SupportModel
    {
        internal sealed class Node
        {
            public WearNTear W;
            public float Current;          // the game's support value right now
            public float Max, Min;
            public bool CanFall;           // loses health without support, and is not a chest with items in it
            public bool Gathered;
            public bool Ground;            // touches terrain or something that is not a building piece
            public bool GroundByRemoved;   // ...where that something is the piece being removed
            public readonly List<Contact> Contacts = new List<Contact>();
            public readonly List<int> Pairs = new List<int>(); // two-sided holds: index pairs into Contacts, flattened
            public readonly List<Box> Boxes = new List<Box>();
            public bool Marked;            // its support may depend on the removed piece
            public bool Gone;              // removed, or fell
            public float Before;           // our value with everything still standing
            public float Value;            // our value after the removal
        }

        internal struct Contact
        {
            public Node N;
            public float K;       // support passed on: the neighbor's value times K
            public float KPoint;  // the same, as one side of a two-sided hold
            public bool Point;    // rests on the neighbor (support point below its center of mass)
        }

        internal struct Box
        {
            public Vector3 Pos, Half;
            public Quaternion Rot;
        }

        internal enum Mode
        {
            Before,         // everything standing, at the game's values
            After,          // removed and fallen pieces gone, affected pieces at their new values
            WithoutMarked,  // as if every affected piece were gone too (the worst case)
        }

        internal sealed class Table
        {
            internal readonly Dictionary<WearNTear, Node> Nodes = new Dictionary<WearNTear, Node>();
            internal HashSet<Collider> Removed; // colliders of the piece being removed

            internal Node For(WearNTear w, bool ghost)
            {
                if (Nodes.TryGetValue(w, out var n))
                    return n;
                n = new Node { W = w };
                _material(w, out n.Max, out n.Min, out _, out _);
                if (ghost)
                {
                    // Not built yet: no value of its own, and always empty.
                    n.Current = n.Max;
                    n.CanFall = w.m_noSupportWear;
                }
                else
                {
                    n.Current = _support(w);
                    var piece = w.GetComponent<Piece>();
                    n.CanFall = w.m_noSupportWear && (piece == null || piece.CanBeRemoved());
                }
                Nodes[w] = n;
                return n;
            }
        }

        internal sealed class Result
        {
            internal Piece Target;
            internal readonly List<WearNTear> Falls = new List<WearNTear>();
            internal readonly List<WearNTear> Weaker = new List<WearNTear>();
            internal bool Capped;
        }

        private delegate void MaterialFn(WearNTear w, out float max, out float min, out float horizontal, out float vertical);

        private static Func<WearNTear, float> _support;
        private static MaterialFn _material;
        private static int _mask;
        private static int _terrain;
        private static Collider[] _hits;
        private static Collider[] _near;
        private static readonly List<Collider> _own = new List<Collider>();
        private static readonly HashSet<Collider> _ownSet = new HashSet<Collider>();
        private static readonly List<Vector3> _sides = new List<Vector3>();
        private static readonly List<WearNTear> _found = new List<WearNTear>();
        private static readonly Table _ghostTable = new Table();

        // How far past a piece's own padded boxes to look for pieces that might rest on it. The game
        // checks non-box colliders by their world bounding box, which can reach well past the
        // collider itself (a turned mesh), so this is wider than the game's 0.15 padding.
        private const float NearMargin = 0.5f;

        /// <summary>Looks up the game's private support and material methods. Throws if a game update renamed them.</summary>
        internal static void Init()
        {
            if (_material != null)
                return;
            var support = AccessTools.MethodDelegate<Func<WearNTear, float>>(
                AccessTools.Method(typeof(WearNTear), "GetSupport") ?? throw new MissingMethodException("WearNTear.GetSupport"));
            var material = AccessTools.MethodDelegate<MaterialFn>(
                AccessTools.Method(typeof(WearNTear), "GetMaterialProperties") ?? throw new MissingMethodException("WearNTear.GetMaterialProperties"));
            // The game's own mask and buffer size: it reads no more than 128 overlaps per box.
            _mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
            _terrain = LayerMask.NameToLayer("terrain");
            _hits = new Collider[128];
            _near = new Collider[256];
            _support = support;
            _material = material;
        }

        /// <summary>The support color the hammer shows on a built piece (WearNTear.Highlight), for a value.</summary>
        internal static Color HammerColor(float support, float min, float max)
        {
            if (support >= max)
                return new Color(0.6f, 0.8f, 1f);
            float v = Mathf.Clamp01((support - min) / (max * 0.5f - min));
            var c = Color.Lerp(new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), v);
            Color.RGBToHSV(c, out float h, out _, out _);
            return Color.HSVToRGB(h, Mathf.Lerp(1f, 0.5f, v), Mathf.Lerp(1.2f, 0.9f, v));
        }

        /// <summary>
        /// The support the build ghost's piece would get if placed where it stands. False when the
        /// piece never takes damage from a lack of support.
        /// </summary>
        internal static bool Ghost(GameObject ghost, out float support, out float min, out float max)
        {
            support = min = max = 0f;
            var w = ghost.GetComponent<WearNTear>();
            if (w == null || !w.m_noSupportWear)
                return false;
            _ghostTable.Nodes.Clear();
            var n = _ghostTable.For(w, true);
            Gather(_ghostTable, n, true);
            support = Eval(n, Mode.Before);
            min = n.Min;
            max = n.Max;
            _ghostTable.Nodes.Clear();
            return true;
        }

        /// <summary>True if any collider of the ghost is not a box (its world bounds need the physics scene in step).</summary>
        internal static bool HasNonBoxCollider(GameObject go)
        {
            go.GetComponentsInChildren(true, _own);
            foreach (var c in _own)
                if (!(c is BoxCollider) && !c.isTrigger)
                    return true;
            return false;
        }

        /// <summary>
        /// Works out what falls and what weakens if <paramref name="x"/> were removed, a little at a
        /// time: the caller steps it until it ends, within a time budget per frame. Pieces whose support
        /// could depend on x are found outward from it; their support is then settled again from
        /// everything that does not depend on x, and those left under their minimum fall (which can
        /// take more with them). Stops looking after <paramref name="cap"/> affected pieces.
        /// </summary>
        internal static IEnumerator Removal(Piece x, int cap, float weakerShare, Result r)
        {
            var t = new Table();
            x.GetComponentsInChildren(true, _own);
            t.Removed = new HashSet<Collider>(_own);
            var xBoxes = new List<Box>();
            foreach (var c in _own)
                if (!c.isTrigger && c.attachedRigidbody == null && TryBox(c, false, out var box))
                    xBoxes.Add(box);
            var wx = x.GetComponent<WearNTear>();
            if (wx != null)
            {
                var xn = t.For(wx, false);
                xn.Marked = true;
                xn.Gone = true;
            }

            var marked = new List<Node>();
            var queue = new Queue<List<Box>>();
            queue.Enqueue(xBoxes);
            while (queue.Count > 0 && !r.Capped)
            {
                foreach (var w in Near(queue.Dequeue()))
                {
                    if (w == null)
                        continue;
                    var n = t.For(w, false);
                    if (n.Marked || !w.m_noSupportWear)
                        continue; // pieces that never lose support keep their value
                    if (!n.Gathered)
                    {
                        Gather(t, n, false);
                        yield return null;
                    }
                    if (n.Ground)
                        continue;
                    float before = Eval(n, Mode.Before);
                    if (Eval(n, Mode.WithoutMarked) < before - Mathf.Max(0.01f, before * 0.001f))
                    {
                        n.Marked = true;
                        n.Before = before;
                        marked.Add(n);
                        queue.Enqueue(n.Boxes);
                        if (marked.Count >= cap)
                        {
                            r.Capped = true;
                            break;
                        }
                    }
                }
            }

            // Settle from nothing upward (the rule only ever passes on less than it gets, so this
            // finds the one stable state), drop what ends under its minimum, and settle again.
            while (true)
            {
                foreach (var m in marked)
                    m.Value = 0f;
                for (int round = 0; round < 400; round++)
                {
                    bool changed = false;
                    foreach (var m in marked)
                    {
                        if (m.Gone)
                            continue;
                        float v = Eval(m, Mode.After);
                        if (v > m.Value + m.Max * 1e-5f)
                        {
                            m.Value = v;
                            changed = true;
                        }
                    }
                    if (!changed)
                        break;
                    if ((round & 7) == 7)
                        yield return null;
                }
                bool fell = false;
                foreach (var m in marked)
                {
                    if (!m.Gone && m.CanFall && m.Value < m.Min)
                    {
                        m.Gone = true;
                        fell = true;
                    }
                }
                if (!fell)
                    break;
            }

            foreach (var m in marked)
            {
                if (m.W == null)
                    continue;
                if (m.Gone)
                    r.Falls.Add(m.W);
                else if (m.Before - m.Value >= weakerShare * m.Max)
                    r.Weaker.Add(m.W);
            }
        }

        /// <summary>Building pieces whose colliders come near the boxes (a superset of those that touch them).</summary>
        private static WearNTear[] Near(List<Box> boxes)
        {
            _found.Clear();
            var margin = new Vector3(NearMargin, NearMargin, NearMargin);
            foreach (var b in boxes)
            {
                int count = Physics.OverlapBoxNonAlloc(b.Pos, b.Half + margin, _near, b.Rot, _mask);
                for (int i = 0; i < count; i++)
                {
                    var c = _near[i];
                    if (c.isTrigger || c.attachedRigidbody != null)
                        continue;
                    var w = c.GetComponentInParent<WearNTear>();
                    if (w != null && !_found.Contains(w))
                        _found.Add(w);
                }
            }
            return _found.ToArray();
        }

        /// <summary>What a piece touches and how much support each neighbor would pass on (WearNTear.UpdateSupport).</summary>
        private static void Gather(Table t, Node n, bool ghost)
        {
            n.Gathered = true;
            var w = n.W;
            _material(w, out _, out _, out float horizontal, out float vertical);
            Vector3 com = Com(w);
            w.GetComponentsInChildren(true, _own);
            _ownSet.Clear();
            n.Boxes.Clear();
            foreach (var c in _own)
            {
                _ownSet.Add(c);
                if (!c.isTrigger && c.attachedRigidbody == null && TryBox(c, ghost, out var box))
                    n.Boxes.Add(box);
            }

            _sides.Clear(); // flat direction to each contact's support point, parallel to n.Contacts
            foreach (var box in n.Boxes)
            {
                int count = Physics.OverlapBoxNonAlloc(box.Pos, box.Half, _hits, box.Rot, _mask);
                for (int i = 0; i < count; i++)
                {
                    var c = _hits[i];
                    if (c.attachedRigidbody != null || c.isTrigger || _ownSet.Contains(c))
                        continue;
                    if (c.gameObject.layer == _terrain)
                    {
                        n.Ground = true;
                        return; // full support; the rest does not matter
                    }
                    var other = c.GetComponentInParent<WearNTear>();
                    if (other == null)
                    {
                        if (t.Removed != null && t.Removed.Contains(c))
                        {
                            n.GroundByRemoved = true;
                            continue;
                        }
                        n.Ground = true;
                        return;
                    }
                    if (!other.m_supports)
                        continue;

                    Vector3 otherCom = Com(other);
                    float d = Vector3.Distance(com, otherCom) + 0.1f;
                    float d2 = Vector3.Distance(com, other.transform.position) + 0.1f;
                    if (d2 < d && !w.m_forceCorrectCOMCalculation)
                        d = d2;
                    var contact = new Contact { N = t.For(other, false) };
                    float k = 1f - horizontal * d;
                    var side = Vector3.zero;
                    Vector3 p = SupportPoint(com, otherCom, c);
                    if (p.y < com.y + 0.05f)
                    {
                        Vector3 dir = (p - com).normalized;
                        if (dir.y < 0f)
                        {
                            float tilt = Mathf.Acos(1f - Mathf.Abs(dir.y)) / (Mathf.PI / 2f);
                            k = Mathf.Max(k, 1f - Mathf.Lerp(horizontal, vertical, tilt) * d);
                        }
                        contact.Point = true;
                        contact.KPoint = 1f - vertical * d;
                        side = p - com;
                        side.y = 0f;
                    }
                    contact.K = k;
                    n.Contacts.Add(contact);
                    _sides.Add(side);
                }
            }

            // Held from two sides at least 100 degrees apart: the average of the two counts.
            for (int i = 0; i < n.Contacts.Count; i++)
            {
                if (!n.Contacts[i].Point)
                    continue;
                for (int j = i + 1; j < n.Contacts.Count; j++)
                {
                    if (n.Contacts[j].Point && Vector3.Angle(_sides[i], _sides[j]) >= 100f)
                    {
                        n.Pairs.Add(i);
                        n.Pairs.Add(j);
                    }
                }
            }
        }

        /// <summary>A piece's support from its neighbors, in one of the scenarios.</summary>
        internal static float Eval(Node n, Mode mode)
        {
            if (n.Ground || (n.GroundByRemoved && mode == Mode.Before))
                return n.Max;
            float best = 0f;
            var cs = n.Contacts;
            for (int i = 0; i < cs.Count; i++)
            {
                var c = cs[i];
                if (Present(c.N, mode))
                    best = Mathf.Max(best, ValueOf(c.N, mode) * c.K);
            }
            var pairs = n.Pairs;
            for (int i = 0; i < pairs.Count; i += 2)
            {
                var a = cs[pairs[i]];
                var b = cs[pairs[i + 1]];
                if (Present(a.N, mode) && Present(b.N, mode))
                    best = Mathf.Max(best, (ValueOf(a.N, mode) * a.KPoint + ValueOf(b.N, mode) * b.KPoint) * 0.5f);
            }
            return Mathf.Min(best, n.Max);
        }

        private static bool Present(Node m, Mode mode) =>
            mode == Mode.Before || !(m.Gone || (mode == Mode.WithoutMarked && m.Marked));

        private static float ValueOf(Node m, Mode mode) => mode == Mode.After && m.Marked ? m.Value : m.Current;

        private static Vector3 Com(WearNTear w) => w.transform.position + w.transform.rotation * w.m_comOffset;

        /// <summary>WearNTear.FindSupportPoint.</summary>
        private static Vector3 SupportPoint(Vector3 com, Vector3 otherCom, Collider c)
        {
            if (c is MeshCollider mesh && !mesh.convex)
            {
                if (mesh.Raycast(new Ray(com, Vector3.down), out var hit, 10f))
                    return hit.point;
                return (com + otherCom) * 0.5f;
            }
            return c.ClosestPoint(com);
        }

        /// <summary>
        /// The padded box the game checks around one collider (WearNTear.SetupColliders). The build
        /// ghost has some colliders switched off, which report no bounds: those use their mesh.
        /// </summary>
        private static bool TryBox(Collider c, bool ghost, out Box box)
        {
            box = default;
            Vector3 size;
            if (c is BoxCollider bc)
            {
                var tr = bc.transform;
                box.Rot = tr.rotation;
                box.Pos = tr.position + tr.TransformVector(bc.center);
                var s = tr.lossyScale;
                size = new Vector3(s.x * bc.size.x, s.y * bc.size.y, s.z * bc.size.z);
            }
            else
            {
                Bounds b;
                if (!ghost || (c.enabled && c.gameObject.activeInHierarchy))
                    b = c.bounds;
                else if (c is MeshCollider mc && mc.sharedMesh != null)
                    b = WorldBounds(mc.transform, mc.sharedMesh.bounds);
                else
                    return false;
                box.Rot = Quaternion.identity;
                box.Pos = b.center;
                size = b.size;
            }
            box.Half = (size + new Vector3(0.3f, 0.3f, 0.3f)) * 0.5f;
            return true;
        }

        private static Bounds WorldBounds(Transform tr, Bounds local)
        {
            var result = new Bounds(tr.TransformPoint(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                result.Encapsulate(tr.TransformPoint(corner));
            }
            return result;
        }
    }
}
