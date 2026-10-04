using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dressing for the mine network (IslandMapBuilder.MineNetwork.cs): timber supports, rails and carts, platforms,
/// huts, machinery, crystals, rubble, signs, lamps, ore and zones. Regenerated with the rest of the island's
/// dressing on every rebuild, using the CC0 "Mines and Cave" set in Assets/LoafbrrAssets where it has a piece and
/// simple built props (crates, barrels, machines) where it doesn't.
///
/// Walking is never blocked: anything solid is kept off the "routes" (every tunnel's middle, and lines from each
/// room's centre to every tunnel that opens into it), rails have no colliders, and stairs get a smooth ramp collider.
/// </summary>
public static partial class IslandMapBuilder
{
    private const string Loaf = "Assets/LoafbrrAssets/MInesAndCaveSet/prefabs/";

    // Lamps per space (overrides Dress()'s plan for the spaces listed). The hub and the Old Mine are well lit, the Deep
    // Mine has cold work lights, the Crystal Caverns are lit by their crystals, the Rift has no lamps at all.
    private static readonly Dictionary<string, int> MineLampPlan = new Dictionary<string, int>
    {
        { "OldMine_Adit", 2 }, { "OldMine_Office", 1 }, { "OldMine_Drift_1", 1 }, { "OldMine_Stope", 2 }, { "OldMine_Drift_2", 1 },
        { "OldMine_Ramp", 1 }, { "Collapsed_Drift", 1 }, { "OldMine_Storeroom", 1 },
        // The Old Mine's lower levels: fewer, dimmer lamps the deeper you go; none on the final approach.
        { "OldMine_SideDrift", 1 }, { "OldMine_LowerDrift_1", 1 }, { "OldShaft_Gallery", 1 }, { "OldShaft_Landing_1", 1 },
        { "OldShaft_Incline_2", 1 }, { "OldMine_WorkArea", 1 },
        { "RailStation", 2 }, { "RailTunnel_2", 2 }, { "RailTunnel_Link", 1 }, { "RailJunction", 2 }, { "RailTunnel_3", 1 },
        { "DeepMine_Incline_1", 1 }, { "DeepMine_Shaft", 1 }, { "DeepMine_Return", 1 }, { "DeepMine_Incline_2", 1 }, { "DeepMine_Landing", 1 },
        { "DeepHollow", 1 },
        { "Tunnel_02", 1 }, { "Cavern_02", 1 }, // Mine 3 now: mostly crystal light
    };

    // Minable rocks per space: c = copper, i = iron, g = gold, x = crystal (the ore system's existing items).
    // Each mine has small pockets, side chambers, bigger mining rooms and a deep, rich area.
    private static readonly Dictionary<string, string> MineRockPlan = new Dictionary<string, string>
    {
        { "OldMine_Office", "cc" }, { "OldMine_Drift_1", "c" }, { "OldMine_Stope", "ccii" }, { "OldMine_Drift_2", "i" }, { "OldMine_Ramp", "i" },
        { "Collapsed_Drift", "c" }, { "Collapsed_Chamber", "ggiig" },
        { "OldMine_Storeroom", "c" }, { "OldMine_SideDrift", "c" }, { "OldMine_Abandoned", "ccii" }, { "OldMine_AbandonedLink", "i" },
        { "OldMine_LowerDrift_2", "i" }, { "OldShaft_Gallery", "i" }, { "OldShaft_Landing_1", "ig" }, { "OldShaft_Landing_2", "g" },
        { "OldShaft", "ggi" }, { "OldMine_WorkArea", "iigg" }, { "OldMine_Pocket", "ggi" }, { "OldMine_CollapsedHall", "g" },
        { "OldMine_ApproachTurn", "g" },
        { "RailStation", "ic" }, { "RailJunction", "iig" }, { "RailTunnel_3", "g" }, { "DeepMine_Return", "i" },
        { "DeepMine_Incline_2", "g" }, { "DeepMine_Shaft", "ggi" }, { "DeepMine_Landing", "g" }, { "DeepHollow", "ggix" }, { "OldFacility", "gx" },
        { "Cavern_02", "igx" }, { "CrystalCavern", "xxxgg" }, { "Crystal_Descent_2", "x" }, { "Crystal_Descent_3", "gx" },
        { "Crystal_Lower_3", "x" }, { "Crystal_Geode", "xxxgg" },
        { "Rift_Cavern", "xg" }, { "Rift_Ledge", "xx" }, { "Rift_SideChamber", "xxg" }, { "Rift_Abyss", "xxx" }, { "Rift_Final", "xxxx" },
    };

    // Crystal clusters per space (every other one lights its surroundings): cyan in the Crystal Caverns, dark red in the Rift.
    private static readonly Dictionary<string, int> MineCrystalPlan = new Dictionary<string, int>
    {
        { "Collapsed_Chamber", 1 }, { "OldMine_Pocket", 1 }, { "DeepMine_Shaft", 1 },
        { "Tunnel_02", 4 }, { "Cavern_02", 6 }, { "CrystalTunnel_W", 3 },
        { "Crystal_Descent_1", 2 }, { "Crystal_Descent_2", 2 }, { "Crystal_Descent_3", 2 }, { "Crystal_Descent_4", 2 }, { "Crystal_Descent_5", 1 },
        { "Crystal_Lower_1", 2 }, { "Crystal_Lower_2", 2 }, { "Crystal_Lower_3", 2 }, { "Crystal_Lower_4", 2 }, { "Crystal_Lower_5", 2 },
        { "Rift_Abyss", 3 }, { "Rift_SideChamber", 2 }, { "Rift_Final", 4 }, { "Rift_Ledge", 1 },
    };

    /// <summary>Lamp brightness in generated spaces: bright near the hub, dim in the deep levels.</summary>
    private static float MineLampIntensity(float depth) => Mathf.Lerp(8f, 3.5f, Mathf.InverseLerp(0.15f, 0.9f, depth));

    /// <summary>Lamp colour by mine: warm (hub, Old Mine), cold work light (Deep Mine), pale cyan (Crystal), red (Rift).</summary>
    private static Color MineLampColor(string spaceName, Color fallback)
    {
        switch (MineOf(spaceName))
        {
            case DeepMine: return new Color(0.82f, 0.9f, 1f);
            case CrystalMine: return new Color(0.55f, 0.85f, 1f);
            case RiftMine: return new Color(1f, 0.25f, 0.15f);
            default: return fallback;
        }
    }

    private static Dictionary<string, ItemData[]> MineRocks(ItemData copper, ItemData iron, ItemData gold, ItemData crystal)
    {
        var plan = new Dictionary<string, ItemData[]>();
        foreach (var kv in MineRockPlan)
        {
            var ores = new ItemData[kv.Value.Length];
            for (int i = 0; i < ores.Length; i++)
                ores[i] = kv.Value[i] == 'c' ? copper : kv.Value[i] == 'i' ? iron : kv.Value[i] == 'g' ? gold : crystal;
            plan[kv.Key] = ores;
        }
        return plan;
    }

    // ---- Routes (kept clear of solid props) -------------------------------------------------------

    private struct Corridor { public Vector3 a, b; public float half; }

    private static List<Corridor> Corridors(CaveSpace[] spaces)
    {
        var list = new List<Corridor>();
        foreach (CaveSpace t in spaces)
        {
            if (t.SpaceKind == CaveSpace.Kind.Room || t.End == null) continue;
            list.Add(new Corridor { a = t.transform.position, b = t.End.position, half = t.Size.x * 0.55f });
            foreach (CaveSpace r in spaces)
            {
                if (r.SpaceKind != CaveSpace.Kind.Room) continue;
                foreach (Vector3 end in new[] { t.transform.position, t.End.position })
                    if (r.Distance(end + Vector3.up * 1.5f, out _) < t.Size.x * 0.5f)
                        list.Add(new Corridor { a = r.transform.position, b = end, half = 2.5f });
            }
        }
        return list;
    }

    private static bool RouteClear(List<Corridor> routes, Vector3 p, float margin)
    {
        foreach (Corridor c in routes)
        {
            Vector3 ab = c.b - c.a;
            float t = Mathf.Clamp01(Vector3.Dot(p - c.a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            if (Vector3.Distance(p, c.a + ab * t) < c.half + margin) return false;
        }
        return true;
    }

    // ---- Small helpers ------------------------------------------------------------------------------

    private static readonly RaycastHit[] rayHits = new RaycastHit[32];

    /// <summary>Nearest hit on the generated rock only (props and lamps are ignored).</summary>
    private static bool RockRay(Dresser d, Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        hit = default;
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
            if (rayHits[i].distance < best && rayHits[i].collider.transform.IsChildOf(d.generated)) { best = rayHits[i].distance; hit = rayHits[i]; }
        return best < float.MaxValue;
    }

    /// <summary>Floor height of the rock under p (or p.y if there's none within reach).</summary>
    private static float GroundY(Dresser d, Vector3 p, float above = 3f) =>
        RockRay(d, p + Vector3.up * above, Vector3.down, above + 8f, out RaycastHit hit) ? hit.point.y : p.y;

    /// <summary>A piece of the mines set. Colliders kept only when asked (the set's pieces all come with one).</summary>
    private static GameObject Piece(string path, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, bool solid)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Loaf + path + ".prefab");
        if (prefab == null) { Debug.LogWarning("[Ore What] Missing mines set piece " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        if (!solid) foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    /// <summary>Like Place (scaled to a height, standing on pos), but keeps a simple box collider so it's solid.</summary>
    private static GameObject SolidPlace(string path, Transform parent, Vector3 pos, float yaw, float height, float widthScale = 1f)
    {
        GameObject go = Place(path, parent, pos, yaw, height);
        if (go == null) return null;
        go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(widthScale, 1f, widthScale));
        // The box comes from the rock's own mesh bounds in the object's local space (world-aligned bounds of a turned rock
        // are far bigger than the rock). Narrowed a little, but full height down to the floor: a box shrunk around the
        // centre would float above the ground - an invisible low ceiling beside a tall column.
        var filters = go.GetComponentsInChildren<MeshFilter>();
        if (filters.Length > 0 && filters[0].sharedMesh != null)
        {
            Bounds local = default;
            bool any = false;
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = go.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
                }
            }
            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = new Vector3(local.size.x * 0.75f, local.size.y, local.size.z * 0.75f);
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    /// <summary>A free floor spot near the edge of a space that keeps clear of the routes (and of other props).</summary>
    private static bool PropSpot(Dresser d, List<Corridor> routes, CaveSpace s, System.Random rnd, float edgeMin, float edgeMax,
                                 float margin, out Vector3 floor, float clearance = 2.2f)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            float f = Mathf.Lerp(edgeMin, edgeMax, (float)rnd.NextDouble());
            Vector3 p;
            if (s.SpaceKind == CaveSpace.Kind.Room)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                p = s.transform.position + s.transform.rotation * new Vector3(Mathf.Cos(a) * s.Size.x * f, 0f, Mathf.Sin(a) * s.Size.z * f);
            }
            else
            {
                Vector3 a = s.transform.position, b = s.End.position;
                Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized).normalized * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                p = Vector3.Lerp(a, b, Mathf.Lerp(0.15f, 0.85f, (float)rnd.NextDouble())) + side * s.Size.x * f;
            }
            if (!d.Floor(p, out floor, clearance) || !RouteClear(routes, floor, margin)) continue;
            d.used.Add(floor);
            return true;
        }
        floor = Vector3.zero;
        return false;
    }

    /// <summary>Where a tunnel's middle line leaves a room (its mouth), on the floor. False if it doesn't join the room.</summary>
    private static bool Mouth(Dresser d, CaveSpace tunnel, CaveSpace room, out Vector3 mouth, out Vector3 inward)
    {
        mouth = inward = Vector3.zero;
        if (tunnel == null || room == null || tunnel.End == null) return false;
        Vector3 a = tunnel.transform.position, b = tunnel.End.position;
        bool startIn = room.Distance(a + Vector3.up * 1.5f, out _) < 0f, endIn = room.Distance(b + Vector3.up * 1.5f, out _) < 0f;
        if (!startIn && !endIn) return false;
        Vector3 from = startIn ? a : b, to = startIn ? b : a;
        float length = Vector3.Distance(from, to);
        for (float s = 0f; s < length; s += 0.5f)
        {
            Vector3 p = Vector3.Lerp(from, to, s / length);
            if (room.Distance(p + Vector3.up * 3f, out _) > 1f)
            {
                mouth = p; mouth.y = GroundY(d, p);
                inward = to - from; inward.y = 0f; inward.Normalize();
                return true;
            }
        }
        return false;
    }

    // ---- Built props (one mesh asset each, shared by every copy) -------------------------------------

    private static Dictionary<string, GameObject> propTemplates;

    private static GameObject PropTemplate(Dresser d, string name)
    {
        if (propTemplates.TryGetValue(name, out GameObject t) && t != null) return t;
        PropMesh m;
        Vector3 size, centre;
        Material dark = d.metal;
        switch (name)
        {
            case "Crate":
                m = new PropMesh(d.wood, dark);
                m.Box(new Vector3(0f, 0.45f, 0f), Quaternion.identity, Vector3.one * 0.9f, 0);
                foreach (float y in new[] { 0.08f, 0.82f }) m.Box(new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(0.94f, 0.07f, 0.94f), 1);
                size = Vector3.one * 0.9f; centre = new Vector3(0f, 0.45f, 0f);
                break;
            case "Barrel":
                m = new PropMesh(d.wood, dark);
                m.Cyl(new Vector3(0f, 0.48f, 0f), Quaternion.identity, new Vector3(0.36f, 0.48f, 0.36f), 0); // Cylinder.fbx: radius 1, height 2
                foreach (float y in new[] { 0.15f, 0.81f }) m.Cyl(new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(0.375f, 0.03f, 0.375f), 1);
                m.Cyl(new Vector3(0f, 0.96f, 0f), Quaternion.identity, new Vector3(0.33f, 0.01f, 0.33f), 1);
                size = new Vector3(0.72f, 0.96f, 0.72f); centre = new Vector3(0f, 0.48f, 0f);
                break;
            case "Desk":
                m = new PropMesh(d.wood);
                m.Box(new Vector3(0f, 0.76f, 0f), Quaternion.identity, new Vector3(1.6f, 0.08f, 0.8f), 0);
                foreach (float x in new[] { -0.72f, 0.72f })
                    foreach (float z in new[] { -0.32f, 0.32f })
                        m.Box(new Vector3(x, 0.37f, z), Quaternion.identity, new Vector3(0.08f, 0.74f, 0.08f), 0);
                m.Box(new Vector3(0.3f, 0.85f, 0.1f), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.5f, 0.1f, 0.35f), 0); // a ledger
                size = new Vector3(1.6f, 0.8f, 0.8f); centre = new Vector3(0f, 0.4f, 0f);
                break;
            case "Winch":
                m = new PropMesh(d.wood, dark);
                m.Box(new Vector3(0f, 0.15f, 0f), Quaternion.identity, new Vector3(1.8f, 0.3f, 1.1f), 1);
                foreach (float x in new[] { -0.75f, 0.75f }) m.Box(new Vector3(x, 0.8f, 0f), Quaternion.identity, new Vector3(0.16f, 1.3f, 1.0f), 0);
                m.Cyl(new Vector3(0f, 1.05f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.38f, 0.68f, 0.38f), 1);
                m.Box(new Vector3(0.95f, 1.05f, 0f), Quaternion.identity, new Vector3(0.08f, 0.08f, 0.6f), 1);
                m.Box(new Vector3(0.95f, 0.8f, 0.27f), Quaternion.identity, new Vector3(0.08f, 0.5f, 0.08f), 1);
                size = new Vector3(1.9f, 1.5f, 1.1f); centre = new Vector3(0f, 0.75f, 0f);
                break;
            case "Pump":
                m = new PropMesh(d.wood, dark);
                m.Box(new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(1.3f, 1.1f, 0.9f), 1);
                m.Cyl(new Vector3(1.1f, 0.85f, 0f), Quaternion.identity, new Vector3(0.45f, 0.85f, 0.45f), 1);
                m.Cyl(new Vector3(-0.4f, 1.6f, 0f), Quaternion.identity, new Vector3(0.09f, 0.6f, 0.09f), 1);
                m.Cyl(new Vector3(-0.4f, 2.15f, 0.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.09f, 0.55f, 0.09f), 1);
                m.Box(new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Vector3(2.4f, 0.08f, 1.2f), 0);
                size = new Vector3(2.4f, 1.7f, 1.2f); centre = new Vector3(0.2f, 0.85f, 0f);
                break;
            case "Cage":
                m = new PropMesh(dark);
                foreach (float x in new[] { -0.85f, 0.85f })
                    foreach (float z in new[] { -0.85f, 0.85f })
                        m.Box(new Vector3(x, 1.2f, z), Quaternion.identity, new Vector3(0.1f, 2.4f, 0.1f), 0);
                foreach (float y in new[] { 0.05f, 1.2f, 2.4f })
                {
                    m.Box(new Vector3(0f, y, -0.85f), Quaternion.identity, new Vector3(1.8f, 0.08f, 0.08f), 0);
                    m.Box(new Vector3(0f, y, 0.85f), Quaternion.identity, new Vector3(1.8f, 0.08f, 0.08f), 0);
                    m.Box(new Vector3(-0.85f, y, 0f), Quaternion.identity, new Vector3(0.08f, 0.08f, 1.8f), 0);
                    m.Box(new Vector3(0.85f, y, 0f), Quaternion.identity, new Vector3(0.08f, 0.08f, 1.8f), 0);
                }
                m.Box(new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Vector3(1.75f, 0.06f, 1.75f), 0);
                size = new Vector3(1.8f, 2.4f, 1.8f); centre = new Vector3(0f, 1.2f, 0f);
                break;
            case "Counter":
                m = new PropMesh(d.wood, dark);
                m.Box(new Vector3(0f, 0.52f, 0f), Quaternion.identity, new Vector3(3.2f, 1.04f, 0.8f), 0);
                m.Box(new Vector3(0f, 1.07f, 0f), Quaternion.identity, new Vector3(3.35f, 0.07f, 0.95f), 1);
                m.Box(new Vector3(-0.9f, 1.25f, 0.1f), Quaternion.Euler(0f, 15f, 0f), new Vector3(0.5f, 0.3f, 0.4f), 0); // a ledger box
                size = new Vector3(3.3f, 1.1f, 0.9f); centre = new Vector3(0f, 0.55f, 0f);
                break;
            case "Bench":
                m = new PropMesh(d.wood);
                m.Box(new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(2f, 0.1f, 0.45f), 0);
                foreach (float x in new[] { -0.8f, 0.8f }) m.Box(new Vector3(x, 0.2f, 0f), Quaternion.identity, new Vector3(0.12f, 0.4f, 0.4f), 0);
                size = new Vector3(2f, 0.5f, 0.45f); centre = new Vector3(0f, 0.25f, 0f);
                break;
            default: // "Sign"
                m = new PropMesh(d.wood);
                m.Box(new Vector3(0f, 1.1f, 0.1f), Quaternion.identity, new Vector3(0.15f, 2.2f, 0.15f), 0);
                m.Box(new Vector3(0f, 1.95f, 0f), Quaternion.identity, new Vector3(2.4f, 0.9f, 0.08f), 0);
                size = Vector3.zero; centre = Vector3.zero;
                break;
        }
        GameObject go = m.Build(name, null, Vector3.zero, "Mine" + name);
        if (size != Vector3.zero)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = centre; box.size = size;
        }
        go.hideFlags = HideFlags.HideInHierarchy;
        propTemplates[name] = go;
        return go;
    }

    private static GameObject Prop(Dresser d, string name, Transform parent, Vector3 pos, float yaw, float scale = 1f)
    {
        GameObject go = Object.Instantiate(PropTemplate(d, name), parent);
        go.hideFlags = HideFlags.None;
        go.name = name;
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    // Signs placed in this dressing pass (no two boards on top of each other). Set by DressMineNetwork.
    private static List<Vector3> signSpots;

    /// <summary>
    /// A sign standing on the cave floor, its text facing <paramref name="viewer"/>. The board has to stand in open
    /// air (not in the rock or in a prop), with open space in front of its text, and at least 3.2 m from any other
    /// sign; when the asked-for spot isn't like that, the nearest spot that is (along the board's width, or a little
    /// toward the viewer) is used.
    /// </summary>
    /// <param name="onBuilt">True when <paramref name="at"/> is on a built surface (a deck): the sign stands at that
    /// height instead of on the rock floor under it.</param>
    private static void CaveSign(Dresser d, Transform parent, Vector3 at, Vector3 viewer, string text, bool onBuilt = false)
    {
        if (!FindSignSpot(d, at, viewer, onBuilt, out Vector3 spot))
            Debug.LogWarning($"[Ore What] Sign \"{text.Split('\n')[0]}\": no clear spot near {at:F0}, placed as asked.");
        signSpots?.Add(spot);
        GameObject go = Prop(d, "Sign", parent, spot, 0f);
        go.transform.rotation = Quaternion.LookRotation(Flat(at - viewer).normalized); // text on the -Z face, toward the viewer
        SignText(go.transform, text, true);
    }

    /// <summary>
    /// Where a sign asked for at <paramref name="at"/> can stand: that spot if it fits (SignFits), else the nearest
    /// one along the board's width or a little toward the viewer. False (spot = as asked) when none fits.
    /// </summary>
    private static bool FindSignSpot(Dresser d, Vector3 at, Vector3 viewer, bool onBuilt, out Vector3 spot)
    {
        Vector3 look = Flat(at - viewer).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, look);
        foreach (float toward in new[] { 0f, 1f, 2f })
            foreach (float across in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f, 2.4f, -2.4f, 3.2f, -3.2f })
            {
                if (onBuilt && (toward > 1f || Mathf.Abs(across) > 1.6f)) continue; // stay on the deck
                Vector3 p = at + side * across - look * toward;
                if (!onBuilt) p.y = GroundY(d, p);
                if (SignFits(d, p, look)) { spot = p; return true; }
            }
        spot = at;
        if (!onBuilt) spot.y = GroundY(d, at);
        return false;
    }

    /// <summary>True when a sign standing at <paramref name="foot"/> (text facing -<paramref name="look"/>) fits there.</summary>
    private static bool SignFits(Dresser d, Vector3 foot, Vector3 look)
    {
        if (signSpots != null)
            foreach (Vector3 s in signSpots)
                if (Flat(s - foot).sqrMagnitude < 3.2f * 3.2f && Mathf.Abs(s.y - foot.y) < 4f) return false;
        Quaternion rot = Quaternion.LookRotation(look);
        Vector3 board = foot + Vector3.up * SignBoardHeight;
        // The rock: the board's corners, edge middles and centre, a little out from both faces, well inside the air.
        for (int i = 0; i < 9; i++)
            foreach (float z in new[] { -0.3f, 0.3f })
                if (d.cave.Distance(board + rot * new Vector3((i % 3 - 1) * 1.3f, (i / 3 - 1) * 0.5f, z), false) > -0.4f) return false;
        // The real rock mesh and anything built (props, lamps, huts): nothing may cut through the board.
        Physics.SyncTransforms();
        if (Physics.CheckBox(board, new Vector3(1.3f, 0.5f, 0.15f), rot, ~0, QueryTriggerInteraction.Ignore)) return false;
        // Readable: open space in front of the text (no rock, booth, hut or prop right in front of it).
        return !Physics.Raycast(board - look * 0.1f, -look, 4f, ~0, QueryTriggerInteraction.Ignore);
    }

    // ---- Crystals -----------------------------------------------------------------------------------

    /// <summary>One crystal shard: a six-sided prism (radius 1, body height 1) with a pointed tip, flat-shaded, base at 0.</summary>
    private static Mesh CrystalShardMesh()
    {
        string path = $"{MeshFolder}/Props/CrystalShard.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null && existing.vertexCount > 0) return existing;
        var v = new List<Vector3>(); var t = new List<int>();
        Vector3 Ring(int i, float y) { float a = i * Mathf.PI / 3f; return new Vector3(Mathf.Cos(a), y, Mathf.Sin(a)); }
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int s = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(s); t.Add(s + 1); t.Add(s + 2); }
        Vector3 tip = new Vector3(0.1f, 1.45f, 0.05f); // a little off-centre: less regular
        for (int i = 0; i < 6; i++)
        {
            Vector3 b0 = Ring(i, 0f), b1 = Ring(i + 1, 0f), t0 = Ring(i, 1f) * 1f, t1 = Ring(i + 1, 1f);
            t0.x *= 0.85f; t0.z *= 0.85f; t1.x *= 0.85f; t1.z *= 0.85f; // slight taper
            Tri(b0, t1, b1); Tri(b0, t0, t1);  // side (clockwise seen from outside)
            Tri(t0, tip, t1);                  // tip facet
        }
        var mesh = new Mesh { name = "CrystalShard" };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return SaveMesh(mesh, path);
    }

    private static Material CrystalGlowMaterial() =>
        SimpleMaterial("CrystalGlow", new Color(0.32f, 0.82f, 1f), new Color(0.35f, 1.3f, 2.1f));

    /// <summary>A cluster of glowing crystal shards leaning out from a point on the floor (looks only, no collider).</summary>
    private static Transform CrystalCluster(Transform parent, Vector3 at, float size, System.Random rnd, Material material = null)
    {
        Mesh shard = CrystalShardMesh();
        Material glow = material != null ? material : CrystalGlowMaterial();
        var cluster = new GameObject("Crystal Cluster").transform;
        cluster.SetParent(parent, false);
        cluster.SetPositionAndRotation(at - Vector3.up * 0.15f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
        int count = 4 + rnd.Next(5);
        for (int i = 0; i < count; i++)
        {
            float lean = i == 0 ? (float)rnd.NextDouble() * 8f : Mathf.Lerp(18f, 48f, (float)rnd.NextDouble());
            float yaw = i * 360f / count + (float)rnd.NextDouble() * 30f;
            float height = size * (i == 0 ? 1f : Mathf.Lerp(0.35f, 0.8f, (float)rnd.NextDouble()));
            float radius = height * Mathf.Lerp(0.13f, 0.2f, (float)rnd.NextDouble());
            var go = new GameObject("Shard", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(cluster, false);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean, 0f, 0f);
            go.transform.localPosition = go.transform.localRotation * Vector3.forward * (i == 0 ? 0f : radius * 0.6f);
            go.transform.localScale = new Vector3(radius, height / 1.45f, radius);
            go.GetComponent<MeshFilter>().sharedMesh = shard;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        return cluster;
    }

    // ---- Timber, rails, platforms, huts -------------------------------------------------------------

    /// <summary>
    /// Timber sets along a tunnel every <paramref name="spacing"/> m: two posts against the walls and a cap beam,
    /// sized to the tunnel by raycasts. <paramref name="damaged"/> = share of broken / tilted / sagging sets.
    /// </summary>
    private static int TimberSets(Dresser d, CaveSpace s, Transform parent, float spacing, float damaged, System.Random rnd,
                                  Material steel = null, float maxSpan = 12f)
    {
        if (s == null || s.End == null) return 0;
        Vector3 a = s.transform.position, b = s.End.position;
        Vector3 dir = b - a; dir.y = 0f;
        float length = dir.magnitude;
        dir /= length;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Quaternion facing = Quaternion.LookRotation(dir);
        int sets = 0;
        for (float along = 5f; along < length - 4f; along += spacing)
        {
            Vector3 p = Vector3.Lerp(a, b, along / length);
            if (!RockRay(d, p + Vector3.up * 3f, Vector3.down, 8f, out RaycastHit floorHit)) continue;
            Vector3 f = floorHit.point;
            if (!RockRay(d, f + Vector3.up * 1.6f, side, s.Size.x + 5f, out RaycastHit right) ||
                !RockRay(d, f + Vector3.up * 1.6f, -side, s.Size.x + 5f, out RaycastHit left)) continue;
            float span = Vector3.Distance(left.point, right.point);
            if (span > maxSpan || span < 3f) continue;
            float top = RockRay(d, f + Vector3.up * 1f, Vector3.up, 14f, out RaycastHit ceiling) ? ceiling.point.y - 0.4f : f.y + 4.4f;
            top = Mathf.Min(top, f.y + 4.4f);
            if (top - f.y < 2.8f) continue;

            Vector3 footL = left.point - side * -0.35f, footR = right.point - side * 0.35f; // a little in from each wall
            footL.y = GroundY(d, footL); footR.y = GroundY(d, footR);
            bool broken = rnd.NextDouble() < damaged;
            int kind = broken ? rnd.Next(3) : -1;
            var set = new GameObject("Timber Set").transform;
            set.SetParent(parent, false);
            set.position = f;

            for (int i = 0; i < 2; i++)
            {
                Vector3 foot = i == 0 ? footL : footR;
                float h = top - foot.y + 0.15f;
                Quaternion rot = facing;
                if (kind == 0 && i == 1) rot = facing * Quaternion.Euler(0f, 0f, (rnd.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Lerp(10f, 17f, (float)rnd.NextDouble()));
                if (kind == 1 && i == 1) h *= 0.45f; // snapped off
                Piece("Posts/Post_Reinforced_A", set, foot - Vector3.up * 0.15f, rot, new Vector3(1.7f, h / 3.8f, 1.7f), true);
            }
            Vector3 mid = (footL + footR) * 0.5f; mid.y = top + 0.17f;
            float width = Vector3.Distance(new Vector3(footL.x, 0f, footL.z), new Vector3(footR.x, 0f, footR.z)) + 0.6f;
            if (kind == 1)
            {
                // The cap beam came down: it lies along the left wall (no collider, so nobody trips on it).
                Vector3 lying = footL + side * 0.6f + Vector3.up * 0.17f + dir * 0.4f;
                Piece("Posts/Beam_A", set, lying, facing * Quaternion.Euler(0f, 90f + (float)rnd.NextDouble() * 20f - 10f, 6f), new Vector3(width / 4f * 0.8f, 1.7f, 1.7f), false);
            }
            else
            {
                Quaternion beamRot = facing * Quaternion.Euler(0f, 0f, kind == 2 ? (rnd.NextDouble() < 0.5 ? -9f : 9f) : 0f);
                Piece("Posts/Beam_A", set, mid - (kind == 2 ? Vector3.up * 0.35f : Vector3.zero), beamRot, new Vector3(width / 4f, 1.7f, 1.7f), true);
                if (kind != 2) // knee braces
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        Vector3 corner = (sgn < 0 ? footL : footR); corner.y = top - 0.55f;
                        Piece("Posts/Beam_C", set, corner - side * sgn * 0.45f, facing * Quaternion.Euler(0f, 0f, sgn * 45f), new Vector3(0.6f, 1.2f, 1.2f), false);
                    }
            }
            sets++;
        }
        if (steel != null) // steel sets (Mine 2, the Rift): the same pieces in painted steel
            foreach (var r in parent.GetComponentsInChildren<Renderer>()) r.sharedMaterial = steel;
        return sets;
    }

    /// <summary>A heavier frame where a tunnel opens into a room, with a sign beside it (facing the room).</summary>
    private static void MouthFrame(Dresser d, CaveSpace tunnel, CaveSpace room, Transform parent, string sign, List<Corridor> routes)
    {
        if (!Mouth(d, tunnel, room, out Vector3 mouth, out Vector3 inward)) return;
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Quaternion facing = Quaternion.LookRotation(inward);
        Vector3 at = mouth + inward * 1.5f;
        if (RockRay(d, at + Vector3.up * 1.6f, side, tunnel.Size.x + 6f, out RaycastHit r) &&
            RockRay(d, at + Vector3.up * 1.6f, -side, tunnel.Size.x + 6f, out RaycastHit l))
        {
            float span = Vector3.Distance(l.point, r.point);
            float top = RockRay(d, at + Vector3.up, Vector3.up, 16f, out RaycastHit c) ? c.point.y - 0.5f : at.y + 5f;
            top = Mathf.Min(top, GroundY(d, at) + 5.2f);
            var frame = new GameObject("Mouth Frame " + tunnel.name).transform;
            frame.SetParent(parent, false);
            frame.position = at;
            Vector3 footL = l.point + side * 0.45f, footR = r.point - side * 0.45f;
            foreach (Vector3 foot0 in new[] { footL, footR })
            {
                Vector3 foot = foot0; foot.y = GroundY(d, foot);
                Piece("Posts/Post_Reinforced_A", frame, foot - Vector3.up * 0.15f, facing, new Vector3(2.4f, (top - foot.y + 0.2f) / 3.8f, 2.4f), true);
            }
            if (span <= 13f && top - at.y > 3f)
            {
                Vector3 mid = (footL + footR) * 0.5f; mid.y = top + 0.24f;
                float width = Vector3.Distance(new Vector3(footL.x, 0f, footL.z), new Vector3(footR.x, 0f, footR.z)) + 0.9f;
                Piece("Posts/Beam_A", frame, mid, facing, new Vector3(width / 4f, 2.4f, 2.4f), true);
                Piece("Posts/Beam_A", frame, mid + Vector3.up * 0.42f - inward * 0.1f, facing, new Vector3(width / 4f + 0.08f, 1.4f, 2.8f), false);
            }
            if (sign != null)
            {
                // Beside the opening on the room side, against the left post.
                Vector3 s = footL - inward * 2.2f + side * 0.9f;
                if (!RouteClear(routes, new Vector3(s.x, GroundY(d, s), s.z), 0f)) s = footL - inward * 2.2f + side * 0.4f;
                CaveSign(d, parent, s, mouth - inward * 10f, sign);
            }
        }
    }

    /// <summary>
    /// Rails along points on the floor (a smooth curve through them), 2 m pieces following the floor, no colliders.
    /// Bumpers at the ends that need one. Returns the curve (for carts and for keeping props off the rails).
    /// </summary>
    private static List<Vector3> Rails(Dresser d, Transform parent, List<Vector3> points, bool bumperStart, bool bumperEnd, List<Corridor> routes)
    {
        var curve = new List<Vector3>();
        if (points.Count < 2) return curve;
        // Catmull-Rom through the points, finely sampled, then resampled every 2 m.
        var fine = new List<Vector3>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Count - 1, i + 2)];
            for (float t = 0f; t < 1f; t += 0.05f)
            {
                float t2 = t * t, t3 = t2 * t;
                fine.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        fine.Add(points[points.Count - 1]);
        curve.Add(fine[0]);
        float carried = 0f;
        for (int i = 1; i < fine.Count; i++)
        {
            Vector3 a = fine[i - 1], b = fine[i];
            float seg = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            while (carried + seg >= 2f)
            {
                float need = 2f - carried;
                a = Vector3.Lerp(a, b, need / seg);
                seg -= need;
                carried = 0f;
                curve.Add(a);
            }
            carried += seg;
        }
        for (int i = 0; i < curve.Count; i++) { Vector3 c = curve[i]; c.y = GroundY(d, c); curve[i] = c; }
        var line = new GameObject("Rails").transform;
        line.SetParent(parent, false);
        for (int i = 1; i < curve.Count; i++)
        {
            Vector3 a = curve[i - 1], b = curve[i];
            Quaternion rot = Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, -90f, 0f); // the rail piece runs along its X
            Piece("Rails/Rail_B", line, (a + b) * 0.5f + Vector3.up * 0.02f, rot, Vector3.one, false);
            routes.Add(new Corridor { a = a, b = b, half = 0.9f });
        }
        if (bumperStart) Bumper(line, curve[0], curve[0] - curve[1]);
        if (bumperEnd) Bumper(line, curve[curve.Count - 1], curve[curve.Count - 1] - curve[curve.Count - 2]);
        return curve;
    }

    private static void Bumper(Transform parent, Vector3 at, Vector3 outward)
    {
        outward.y = 0f;
        Piece("Rails/Rail_End", parent, at, Quaternion.LookRotation(outward.normalized) * Quaternion.Euler(0f, -90f, 0f), Vector3.one * 1.2f, true);
    }

    /// <summary>A mine cart standing on the rail curve, `along` metres from its start.</summary>
    private static void CartOnRails(Transform parent, List<Vector3> curve, float along, string model)
    {
        int i = Mathf.Clamp(Mathf.RoundToInt(along / 2f), 1, curve.Count - 1);
        Vector3 a = curve[i - 1], b = curve[i];
        Piece("MineCart/" + model, parent, (a + b) * 0.5f + Vector3.up * 0.12f, Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, -90f, 0f), Vector3.one * 1.25f, true);
    }

    /// <summary>
    /// A wooden deck (nx × nz platform pieces) 2 m above the floor on posts, with stairs down from its front (-Z)
    /// edge. The stairs get a smooth ramp collider (their steps are taller than the player's step height).
    /// False (and nothing built) when the floor under it isn't flat enough.
    /// </summary>
    private static bool Deck(Dresser d, Transform parent, Vector3 floor, Quaternion rot, int nx, int nz, string name, out Transform deck)
    {
        deck = null;
        const float pw = 4.27f, pl = 4f;
        Vector3 stairsFoot = floor + rot * new Vector3(0f, 0f, -nz * pl * 0.5f - 3.6f);
        float y = GroundY(d, stairsFoot) + 2f;
        // Every corner must be below the deck (and not far below), else it's not a place for a deck.
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                Vector3 c = floor + rot * new Vector3((i - nx * 0.5f) * pw, 0f, (j - nz * 0.5f) * pl);
                float g = GroundY(d, new Vector3(c.x, y, c.z), 1.2f);
                if (g > y - 0.9f || g < y - 3.6f) return false;
            }
        deck = new GameObject(name).transform;
        deck.SetParent(parent, false);
        deck.SetPositionAndRotation(new Vector3(floor.x, y, floor.z), rot);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                Vector3 local = new Vector3((i - (nx - 1) * 0.5f) * pw, 0f, (j - (nz - 1) * 0.5f) * pl);
                Piece("WOodPlatforms/Wood_Platform_A", deck, deck.TransformPoint(local), rot, Vector3.one, true);
            }
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                Vector3 top = deck.TransformPoint(new Vector3((i - nx * 0.5f) * pw * 0.94f, -0.26f, (j - nz * 0.5f) * pl * 0.94f));
                float g = GroundY(d, top, 0.5f);
                Piece("WOodPlatforms/Wood_Platform_Post", deck, new Vector3(top.x, g - 0.1f, top.z), rot, new Vector3(1f, (top.y - g + 0.1f) / 4f, 1f), true);
            }
        GameObject stairs = Piece("WOodPlatforms/Wood_Platform_Stairs_A", deck, deck.TransformPoint(new Vector3(0f, -2f, -nz * pl * 0.5f)), rot, Vector3.one, false);
        if (stairs != null)
        {
            float angle = Mathf.Atan2(2.265f, 4f) * Mathf.Rad2Deg;
            var ramp = new GameObject("Stair Ramp", typeof(BoxCollider));
            ramp.transform.SetParent(stairs.transform, false);
            ramp.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            ramp.transform.localPosition = new Vector3(0f, 1.13f, -2f) - ramp.transform.localRotation * Vector3.up * 0.1f;
            ramp.GetComponent<BoxCollider>().size = new Vector3(4f, 0.2f, 4f / Mathf.Cos(angle * Mathf.Deg2Rad) + 0.3f);
        }
        // Edge boards along the open sides (looks finished; low, so they don't stop anyone).
        for (int i = 0; i < nx; i++)
        {
            float x = (i - (nx - 1) * 0.5f) * pw;
            Piece("WOodPlatforms/Wood_Platform_Planks_A_Side", deck, deck.TransformPoint(new Vector3(x, 0.4f, nz * pl * 0.5f - 0.1f)), rot * Quaternion.Euler(0f, 90f, 0f), Vector3.one, false);
        }
        return true;
    }

    /// <summary>A small timber hut (4 walls of the mines set, the door facing -Z) with a plank roof.</summary>
    private static Transform Hut(Dresser d, Transform parent, Vector3 floor, Quaternion rot, string name)
    {
        var hut = new GameObject(name).transform;
        hut.SetParent(parent, false);
        float y = float.MaxValue; // stand on the lowest corner (sinks a little into higher ground instead of floating)
        foreach (Vector3 c in new[] { new Vector3(-2.3f, 0f, -2.3f), new Vector3(2.3f, 0f, -2.3f), new Vector3(-2.3f, 0f, 2.3f), new Vector3(2.3f, 0f, 2.3f) })
            y = Mathf.Min(y, GroundY(d, floor + rot * c));
        hut.SetPositionAndRotation(new Vector3(floor.x, y, floor.z), rot);
        Vector3 P(float x, float z) => hut.TransformPoint(new Vector3(x, 0f, z));
        // Plank walls (4 m panels), the front one with a doorway; posts at the corners; a plank roof.
        Piece("WOodPlatforms/Panel_Wood_Entrance_A", hut, P(0f, -2f), rot, Vector3.one, true);
        Piece("WOodPlatforms/Panel_Wood_A", hut, P(0f, 2f), rot * Quaternion.Euler(0f, 180f, 0f), Vector3.one, true);
        Piece("WOodPlatforms/Panel_Wood_A", hut, P(-2f, 0f), rot * Quaternion.Euler(0f, 90f, 0f), Vector3.one, true);
        Piece("WOodPlatforms/Panel_Wood_A", hut, P(2f, 0f), rot * Quaternion.Euler(0f, -90f, 0f), Vector3.one, true);
        foreach (Vector3 c in new[] { new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f), new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f) })
            Piece("WOodPlatforms/Wood_Platform_Post", hut, hut.TransformPoint(c) - Vector3.up * 0.2f, rot, new Vector3(1f, 1.07f, 1f), false);
        Piece("WOodPlatforms/Wood_Platform_A", hut, hut.TransformPoint(new Vector3(0f, 4.15f, 0f)), rot, new Vector3(1.05f, 1f, 1.1f), true);
        Prop(d, "Desk", hut, hut.TransformPoint(new Vector3(0.6f, 0f, 1.2f)), rot.eulerAngles.y + 180f);
        Prop(d, "Crate", hut, hut.TransformPoint(new Vector3(-1.2f, 0f, 1.2f)), rot.eulerAngles.y + 20f);
        return hut;
    }

    /// <summary>A spot whose whole footprint (radius r) is reasonably flat floor, clear of the routes and of other props.</summary>
    private static bool Footprint(Dresser d, List<Corridor> routes, CaveSpace s, System.Random rnd, float r, float edgeMin, float edgeMax,
                                  out Vector3 floor, out Quaternion facingCentre)
    {
        facingCentre = Quaternion.identity;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            float f = Mathf.Lerp(edgeMin, edgeMax, (float)rnd.NextDouble());
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            Vector3 p = s.transform.position + s.transform.rotation * new Vector3(Mathf.Cos(a) * s.Size.x * f, 0f, Mathf.Sin(a) * s.Size.z * f);
            if (!d.Floor(p, out floor, 4.2f) || !RouteClear(routes, floor, r * 0.75f)) continue;
            bool flat = true, free = true;
            for (int k = 0; k < 8 && flat; k++)
            {
                float ang = k * Mathf.PI / 4f;
                Vector3 q = floor + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                float g = GroundY(d, q, 2f);
                flat = Mathf.Abs(g - floor.y) < 0.9f && d.cave.Distance(new Vector3(q.x, g + 2.5f, q.z), false) < -0.3f;
            }
            foreach (Vector3 u in d.used) if ((u - floor).sqrMagnitude < (r + 1f) * (r + 1f)) { free = false; break; }
            if (!flat || !free) continue;
            Vector3 toCentre = s.transform.position - floor; toCentre.y = 0f;
            facingCentre = Quaternion.LookRotation(-toCentre.normalized); // -Z (the front) toward the room's centre
            d.used.Add(floor);
            return true;
        }
        floor = Vector3.zero;
        return false;
    }

    // ---- The dressing itself -------------------------------------------------------------------------

    /// <summary>Called by Dress() after the per-space lamps/rocks/crystals. Returns things it couldn't place.</summary>
    private static List<string> DressMineNetwork(Dresser d, Dictionary<string, CaveSpace> spaces, Transform props, Transform lighting, Transform zones)
    {
        var missed = new List<string>();
        CaveSpace S(string n) => spaces.TryGetValue(n, out CaveSpace s) ? s : null;
        if (S("Cavern_01") == null || S("Rift_Portal") == null) return missed; // no hub + four mines layout in this cave
        foreach (string needed in new[] { "OldMine_Adit", "SideTunnel", "SideCave", "RailTunnel_Link", "RailStation", "RailTunnel_2", "RailJunction", "RailTunnel_3", "Tunnel_02" })
            if (S(needed) == null) { missed.Add("mine dressing: " + needed + " is missing, dressing skipped"); return missed; }
        propTemplates = new Dictionary<string, GameObject>();
        signSpots = new List<Vector3>();
        try { DressMineSpaces(d, S, props, lighting, zones, missed); }
        finally
        {
            foreach (GameObject t in propTemplates.Values) if (t != null) Object.DestroyImmediate(t); // never left in the scene
            propTemplates = null;
            signSpots = null;
        }
        return missed;
    }

    // Materials of the mine-specific structures (simple URP Lit, made once in Assets/Materials/Island).
    private static Material Steel => SimpleMaterial("MineSteel", new Color(0.3f, 0.32f, 0.35f), Color.black);
    private static Material Hazard => SimpleMaterial("MineHazardYellow", new Color(0.95f, 0.72f, 0.08f), Color.black);
    private static Material Concrete => SimpleMaterial("RiftConcrete", new Color(0.33f, 0.33f, 0.34f), Color.black);
    private static Material RiftGlow => SimpleMaterial("RiftGlow", new Color(0.45f, 0.04f, 0.07f), new Color(2.2f, 0.12f, 0.22f));
    private static Material WarningLamp => SimpleMaterial("WarningLampRed", new Color(1f, 0.15f, 0.1f), new Color(4f, 0.3f, 0.2f));

    private static void DressMineSpaces(Dresser d, System.Func<string, CaveSpace> S, Transform props, Transform lighting, Transform zones, List<string> missed)
    {
        var routes = Corridors(d.cave.Spaces);
        var rnd = new System.Random(4242);
        Transform root = Child(props, "Mine"), lights = Child(lighting, "Mine");
        Transform hubP = Child(root, "Hub"), m1 = Child(root, "Mine1_OldMine"), m2 = Child(root, "Mine2_DeepMine"),
                  m3 = Child(root, "Mine3_CrystalCaverns"), m4 = Child(root, "Mine4_TheRift");
        CaveSpace hub = S("Cavern_01");
        // Keep the hub side of every entrance clear (no booths or crates in front of a mine's mouth).
        foreach (string entrance in new[] { "MainTunnel", "OldMine_Adit", "SideTunnel", "Tunnel_02", "Rift_Portal" })
            if (Mouth(d, S(entrance), hub, out Vector3 eMouth, out Vector3 eIn))
                routes.Add(new Corridor { a = eMouth, b = eMouth - eIn * 11f, half = 5f });

        // ===== Mine 2's rails first (flat; everything else keeps off them): hub -> SideTunnel -> SideCave -> link ->
        //       RailTunnel_2 -> Junction B -> CombatArea, a branch back down RailTunnel_2 to Station 1, and a loading spur.
        var railPoints = new List<Vector3>();
        void Along(List<Vector3> list, string tunnel, float t, float side)
        {
            CaveSpace s = S(tunnel); if (s == null || s.End == null) return;
            Vector3 a = s.transform.position, b = s.End.position, dir = Flat(b - a).normalized;
            list.Add(Vector3.Lerp(a, b, t) + Vector3.Cross(Vector3.up, dir) * side);
        }
        CaveSpace sideTunnel = S("SideTunnel"), sideCave = S("SideCave"), station = S("RailStation"), junction = S("RailJunction");
        {
            Vector3 dir = Flat(sideTunnel.End.position - sideTunnel.transform.position).normalized;
            railPoints.Add(sideTunnel.transform.position - dir * 5f + Vector3.Cross(Vector3.up, dir) * 3f);
            Along(railPoints, "SideTunnel", 0f, 3f); Along(railPoints, "SideTunnel", 0.5f, 3f); Along(railPoints, "SideTunnel", 1f, 3f);
            Along(railPoints, "RailTunnel_Link", 0.1f, 2f); Along(railPoints, "RailTunnel_Link", 0.85f, 2f);
            Along(railPoints, "RailTunnel_2", 0.86f, 2f); Along(railPoints, "RailTunnel_2", 0.98f, 2f);
            railPoints.Add(junction.transform.position + new Vector3(0f, 0f, 1f));
            Along(railPoints, "RailTunnel_3", 0.1f, 2f); Along(railPoints, "RailTunnel_3", 0.55f, 2f); Along(railPoints, "RailTunnel_3", 0.82f, 2f);
        }
        List<Vector3> mainLine = Rails(d, m2, railPoints, true, true, routes);
        if (mainLine.Count > 10)
        {
            CartOnRails(m2, mainLine, 3f, "Mine_Cart_AB_grp");                          // waiting in the hub
            CartOnRails(m2, mainLine, (mainLine.Count - 3) * 2f, "Mine_Cart_CB_grp");    // at the CombatArea end
        }
        var stationPoints = new List<Vector3>();
        Along(stationPoints, "RailTunnel_2", 0.66f, 2f); Along(stationPoints, "RailTunnel_2", 0.35f, 2f); Along(stationPoints, "RailTunnel_2", 0.05f, 2f);
        stationPoints.Add(station.transform.position + new Vector3(-4f, 0f, -3f));
        List<Vector3> stationLine = Rails(d, m2, stationPoints, false, true, routes);
        if (stationLine.Count > 6) { CartOnRails(m2, stationLine, (stationLine.Count - 4) * 2f, "Mine_Cart_BB_grp"); CartOnRails(m2, stationLine, (stationLine.Count - 6) * 2f, "Mine_Cart_CA"); }
        var spur = new List<Vector3> { junction.transform.position + new Vector3(1f, 0f, -3f), junction.transform.position + new Vector3(7f, 0f, 2f),
                                       junction.transform.position + new Vector3(9f, 0f, 8f) };
        List<Vector3> spurLine = Rails(d, m2, spur, false, true, routes);
        if (spurLine.Count > 3) { CartOnRails(m2, spurLine, 4f, "Mine_Cart_AB_grp"); CartOnRails(m2, spurLine, 8f, "Mine_Cart_BB_grp"); }
        // Mine 1's old rails: a short abandoned line into the adit with a cart left on it.
        var oldPoints = new List<Vector3>();
        Along(oldPoints, "OldMine_Adit", 0.05f, 2.2f); Along(oldPoints, "OldMine_Adit", 0.5f, 2.2f); Along(oldPoints, "OldMine_Adit", 0.95f, 2.2f);
        List<Vector3> oldLine = Rails(d, m1, oldPoints, true, true, routes);
        if (oldLine.Count > 6) CartOnRails(m1, oldLine, (oldLine.Count - 3) * 2f, "Mine_Cart_CA");

        // ===== Supports: old timber in Mine 1, steel sets in Mine 2, a few wrecked steel sets at the top of the Rift.
        int timber = 0;
        foreach (var (name, spacing, damage, steel) in new[]
        {
            ("OldMine_Adit", 6f, 0.05f, false), ("OldMine_Drift_1", 6f, 0.1f, false), ("OldMine_Drift_2", 6f, 0.15f, false), ("OldMine_Ramp", 6f, 0.1f, false),
            ("Collapsed_Drift", 5f, 0.7f, false), ("DeepTunnel_A", 7f, 0.2f, false), ("DeepTunnel_B", 7f, 0.25f, false), ("DeepTunnel_C", 7f, 0.3f, false),
            // The Old Mine's expansion: tidy near the camp, more broken going down, almost nothing on the final approach.
            ("OldMine_SideDrift", 6f, 0.35f, false), ("OldMine_AbandonedLink", 6f, 0.3f, false),
            ("OldMine_LowerDrift_1", 7f, 0.35f, false), ("OldMine_LowerDrift_2", 7f, 0.4f, false),
            ("OldShaft_Incline_1", 8f, 0.45f, false), ("OldShaft_Incline_2", 8f, 0.5f, false), ("OldShaft_Incline_3", 8f, 0.55f, false),
            ("OldShaft_BottomDrift", 7f, 0.5f, false), ("OldMine_OldIncline", 8f, 0.6f, false), ("OldMine_PocketDrift", 6f, 0.7f, false),
            ("OldMine_Approach_1", 13f, 0.9f, false),
            ("SideTunnel", 7f, 0f, true), ("RailTunnel_Link", 6f, 0f, true), ("RailTunnel_2", 8f, 0f, true), ("RailTunnel_3", 8f, 0.05f, true),
            ("DeepMine_Incline_1", 7f, 0.15f, true), ("DeepMine_Return", 7f, 0.1f, true), ("DeepMine_Incline_2", 7f, 0.2f, true),
            ("DeepMine_Incline_3", 7f, 0.25f, true), ("Facility_Link", 6f, 0.4f, true),
            ("Rift_Descent_1", 11f, 0.8f, true), ("Rift_Lower_1", 12f, 0.9f, true),
        })
        {
            CaveSpace s = S(name);
            if (s == null) continue;
            int mine = MineOf(name);
            Transform parent = Child(mine == OldMine ? m1 : mine == DeepMine ? m2 : m4, name);
            timber += TimberSets(d, s, parent, spacing, damage, rnd, steel ? Steel : null, steel ? 16f : 12f);
        }
        foreach (string name in new[] { "SideTunnel", "RailTunnel_Link", "RailTunnel_2", "DeepMine_Incline_1", "DeepMine_Incline_2", "DeepMine_Incline_3", "DeepMine_Return" })
            Pipes(d, S(name), Child(m2, name));

        // ===== The Central Mining Hub.
        // Mine entrances: each looks like its mine.
        MouthFrame(d, S("OldMine_Adit"), hub, hubP, "MINE 1\nTHE OLD MINE", routes);
        IndustrialPortal(d, sideTunnel, hub, hubP, lights, routes, "MINE 2\nTHE DEEP MINE\nHARD HATS REQUIRED");
        CrystalPortal(d, S("Tunnel_02"), hub, hubP, lights, routes, rnd, "MINE 3\nCRYSTAL CAVERNS");
        RiftGate(d, S("Rift_Portal"), hub, hubP, lights, routes, rnd);
        if (S("MainTunnel") != null && Mouth(d, S("MainTunnel"), hub, out Vector3 inMouth, out Vector3 inDir))
            CaveSign(d, hubP, inMouth - inDir * 4f + Vector3.Cross(Vector3.up, -inDir) * 6f, inMouth + inDir * 6f, "CENTRAL MINING HUB\nMINES 1 - 4");
        if (Footprint(d, routes, hub, rnd, 5.5f, 0.45f, 0.7f, out Vector3 deckAt, out Quaternion deckRot) &&
            Deck(d, hubP, deckAt, deckRot, 2, 1, "Hub Deck", out Transform hubDeck))
        {
            Prop(d, "Winch", hubDeck, hubDeck.TransformPoint(new Vector3(-2.2f, 0.27f, 0.6f)), deckRot.eulerAngles.y);
            Prop(d, "Crate", hubDeck, hubDeck.TransformPoint(new Vector3(2.8f, 0.27f, 0.9f)), deckRot.eulerAngles.y + 15f);
            d.Lamp(hubDeck, hubDeck.TransformPoint(new Vector3(1f, 0.27f, 1.5f)), hub.transform.position, new Color(1f, 0.78f, 0.5f), 7f, 22f, true);
            Zone(zones, "Reserved_Overlook", MapZone.ZoneKind.Landmark, 1, deckAt, 5f, "Raised deck over the hub (future announcements / vendor).");
        }
        else missed.Add("Hub deck");
        // Reserved service areas (no systems yet): booths with counters, signs and lights.
        var services = new[]
        {
            ("ORE EXCHANGE", "Reserved: resource selling station."), ("UPGRADES", "Reserved: mining upgrade station."),
            ("OUTFITTER", "Reserved: weapon / equipment shop."), ("STORAGE", "Reserved: backpack / storage area."),
            ("EQUIPMENT", "Reserved: equipment station."), ("COMPANY OFFICE", "Reserved: NPC / service area."),
        };
        var booths = new List<(string, Vector3)>();
        foreach (var (title, note) in services)
        {
            if (!Footprint(d, routes, hub, rnd, 3.3f, 0.5f, 0.78f, out Vector3 at, out Quaternion rot)) { missed.Add("hub booth " + title); continue; }
            Booth(d, hubP, lights, at, rot, title);
            booths.Add(("Reserved_" + title.Replace(' ', '_'), at));
            Zone(zones, "Reserved_" + title.Replace(' ', '_'), MapZone.ZoneKind.Landmark, 1, at, 4f, note);
        }
        if (Footprint(d, routes, hub, rnd, 4f, 0.15f, 0.65f, out Vector3 meetAt, out Quaternion meetRot))
        {
            Gathering(d, hubP, lights, meetAt);
            Zone(zones, "Reserved_GatheringArea", MapZone.ZoneKind.Landmark, 1, meetAt, 5f, "Player gathering / social area.");
        }
        else missed.Add("hub gathering area");
        // The two hub boards stand where their text faces open floor (not the back of a booth).
        bool BoardSpot(out Vector3 at)
        {
            for (int k = 0; k < 15; k++)
                if (PropSpot(d, routes, hub, rnd, 0.6f, 0.85f, 1.2f, out at) && FindSignSpot(d, at, hub.transform.position, false, out _)) return true;
            at = Vector3.zero;
            return false;
        }
        if (BoardSpot(out Vector3 boardAt))
        {
            CaveSign(d, hubP, boardAt, hub.transform.position, "MINE ACCESS\n1 OLD MINE   2 DEEP MINE\n3 CRYSTAL CAVERNS   4 THE RIFT");
            Zone(zones, "Reserved_ProgressBoard", MapZone.ZoneKind.Landmark, 1, boardAt, 3f, "Future progression / key display.");
        }
        else missed.Add("hub MINE ACCESS board");
        if (BoardSpot(out Vector3 noticeAt))
            CaveSign(d, hubP, noticeAt, hub.transform.position, "COMPANY NOTICES\nShifts: dawn & dusk\nReport all cave-ins");
        else missed.Add("hub COMPANY NOTICES board");
        Stores(d, routes, hub, hubP, rnd, 2, missed);
        AddPointLight(lights, "Hub Work Light", hub.transform.position + Vector3.up * 13f, new Color(1f, 0.82f, 0.6f), 14f, 46f);
        d.lights++;

        // ===== Mine 1: The Old Mine (old timber, carts, office, collapsed workings, old deep tunnels, the arena).
        CaveSpace office = S("OldMine_Office"), stope = S("OldMine_Stope");
        if (Footprint(d, routes, office, rnd, 3.2f, 0.1f, 0.6f, out Vector3 hutAt, out Quaternion hutRot))
        {
            Hut(d, m1, hutAt, hutRot, "Mine Office");
            CaveSign(d, m1, hutAt + hutRot * new Vector3(2.9f, 0f, -3f), hutAt + hutRot * new Vector3(0f, 0f, -9f), "MINE OFFICE\nSHIFT BOARD: 0 / 12");
        }
        else missed.Add("Old Mine office hut");
        Stores(d, routes, office, m1, rnd, 2, missed);
        Stores(d, routes, stope, m1, rnd, 1, missed);
        Rubble(d, routes, stope, m1, rnd, 4, missed);
        Rubble(d, routes, S("Collapsed_Drift"), m1, rnd, 6, missed);
        Rubble(d, routes, S("Collapsed_Chamber"), m1, rnd, 3, missed);
        if (Mouth(d, S("Collapsed_Drift"), S("OreArea_01"), out Vector3 colMouth, out Vector3 colIn))
            CaveSign(d, m1, colMouth - colIn * 2.5f + Vector3.Cross(Vector3.up, colIn) * 3.2f, colMouth - colIn * 9f, "OLD MINE\nCOLLAPSED WORKINGS");
        string crawl = Crawl(d, S("Collapsed_Crawl"), m1, routes);
        if (crawl != null) missed.Add(crawl);
        if (Mouth(d, S("OldMine_Ramp"), S("MiningArea_02"), out Vector3 rampMouth, out Vector3 rampIn))
            CaveSign(d, m1, rampMouth + rampIn * 3f + Vector3.Cross(Vector3.up, rampIn) * 3f, rampMouth + rampIn * 10f, "OLD MINE\nup to the Hub");
        if (Mouth(d, S("DeepTunnel_A"), S("MiningArea_02"), out Vector3 lowMouth, out Vector3 lowIn))
            CaveSign(d, m1, lowMouth - lowIn * 2.5f + Vector3.Cross(Vector3.up, lowIn) * 4f, lowMouth - lowIn * 9f, "LOWER WORKINGS\nKEEP TO THE TIMBERS");
        Stores(d, routes, S("MiningArea_02"), m1, rnd, 2, missed);
        Stores(d, routes, S("DeepCavern"), m1, rnd, 2, missed);
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, S("MiningArea_02"), rnd, 0.6f, 0.85f, 1.5f, out Vector3 cartAt))
                Piece("MineCart/Mine_Cart_" + (i == 0 ? "AA" : "BA"), m1, cartAt + Vector3.up * 0.1f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, i == 1 ? 80f : 0f), Vector3.one * 1.25f, true); // one tipped over
        DressOldMineExpansion(d, S, Along, m1, lights, routes, rnd, missed);

        // ===== Mine 2: The Deep Mine (steel, pipes, pumps, rails, machinery, cold light).
        StationPlatform(d, station, stationLine, m2, routes, missed);
        CaveSign(d, m2, station.transform.position + new Vector3(-6f, 0f, 6f), station.transform.position + new Vector3(-6f, 0f, -4f), "STATION 1\nORE DEPOT");
        Stores(d, routes, station, m2, rnd, 2, missed);
        Stores(d, routes, junction, m2, rnd, 3, missed);
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, junction, rnd, 0.75f, 0.92f, 1f, out Vector3 sc))
                Piece("Rails/Rail_Scaffold_A", m2, sc, Quaternion.LookRotation(Flat(junction.transform.position - sc)), Vector3.one * 1.3f, false);
        CaveSign(d, m2, junction.transform.position + new Vector3(-7f, 0f, -6f), junction.transform.position + new Vector3(-3f, 0f, -14f), "JUNCTION B\nLOADING BAY");
        if (Mouth(d, S("RailTunnel_Link"), sideCave, out Vector3 linkMouth, out Vector3 linkIn))
            CaveSign(d, m2, linkMouth - linkIn * 2.5f + Vector3.Cross(Vector3.up, linkIn) * 4f, linkMouth - linkIn * 10f, "DEEP MINE RAIL LINE\nJunction B - Shaft No. 2");
        if (Mouth(d, S("RailTunnel_3"), S("CombatArea"), out Vector3 r3Mouth, out Vector3 r3In))
            CaveSign(d, m2, r3Mouth - r3In * 2.5f - Vector3.Cross(Vector3.up, r3In) * 4.5f, r3Mouth - r3In * 10f, "RAIL LINE\nto Junction B and the Hub");
        Machines(d, routes, S("CombatArea"), m2, rnd, 2, missed);
        Headframe(d, S("DeepMine_Shaft"), m2, lights, S("DeepMine_Incline_1"));
        if (Mouth(d, S("DeepMine_Incline_1"), S("CombatArea"), out Vector3 dmMouth, out Vector3 dmIn))
            CaveSign(d, m2, dmMouth - dmIn * 2.5f + Vector3.Cross(Vector3.up, dmIn) * 4f, dmMouth - dmIn * 10f, "SHAFT No. 2\nDEEP WORKINGS - DANGER");
        if (Mouth(d, S("DeepMine_Return"), S("DeepMine_Shaft"), out Vector3 retMouth, out Vector3 retIn))
            CaveSign(d, m2, retMouth - retIn * 2f + Vector3.Cross(Vector3.up, retIn) * 3.5f, retMouth - retIn * 8f, "RETURN INCLINE\nto Junction B");
        CaveSpace landing = S("DeepMine_Landing");
        if (PropSpot(d, routes, landing, rnd, 0.55f, 0.8f, 1.5f, out Vector3 pumpAt, 2.5f))
        {
            Prop(d, "Pump", m2, pumpAt, Quaternion.LookRotation(Flat(landing.transform.position - pumpAt)).eulerAngles.y + 90f);
            CaveSign(d, m2, pumpAt + Flat(landing.transform.position - pumpAt).normalized * 2f + Vector3.Cross(Vector3.up, Flat(landing.transform.position - pumpAt).normalized) * 2.2f,
                     landing.transform.position, "PUMP STATION\nLEVEL 4");
        }
        Stores(d, routes, landing, m2, rnd, 1, missed);
        Stores(d, routes, S("DeepMine_Shaft"), m2, rnd, 2, missed);
        // The Hollow: Mine 2's big chamber (future arena): two rock columns as cover, machinery, floodlights.
        CaveSpace hollow = S("DeepHollow");
        int pillars = 0;
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, hollow, rnd, 0.35f, 0.65f, 2.5f, out Vector3 pa, 6f) &&
                SolidPlace(Loaf + "Wall/Cave_Pillar_" + "AB"[i] + ".prefab", m2, pa - Vector3.up * 0.6f, i * 77f, Mathf.Lerp(12f, 16f, (float)rnd.NextDouble()), 1.1f) != null)
                pillars++;
        if (pillars < 2) missed.Add($"Hollow pillars ({pillars}/2)");
        Machines(d, routes, hollow, m2, rnd, 3, missed);
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, hollow, rnd, 0.7f, 0.9f, 1.5f, out Vector3 flAt, 6f))
                Floodlight(d, m2, lights, flAt, hollow.transform.position + Vector3.up * 2f, "Hollow " + i);
        if (Mouth(d, S("DeepMine_Incline_3"), hollow, out Vector3 hoMouth, out Vector3 hoIn))
            CaveSign(d, m2, hoMouth + hoIn * 3f + Vector3.Cross(Vector3.up, hoIn) * 3.5f, hoMouth - hoIn * 6f, "THE HOLLOW\nDEEP MINE LEVEL 5");
        // The old survey facility (a dead end off the pump station).
        CaveSpace facility = S("OldFacility");
        if (Footprint(d, routes, facility, rnd, 3.2f, 0.3f, 0.6f, out Vector3 fHut, out Quaternion fHutRot))
        {
            Hut(d, m2, fHut, fHutRot, "Survey Office");
            CaveSign(d, m2, fHut + fHutRot * new Vector3(2.9f, 0f, -3f), fHut + fHutRot * new Vector3(0f, 0f, -10f), "SURVEY STATION 4\nABANDONED");
        }
        else missed.Add("Facility hut");
        if (Footprint(d, routes, facility, rnd, 4.6f, 0.2f, 0.65f, out Vector3 mezAt, out Quaternion mezRot) &&
            Deck(d, m2, mezAt, mezRot, 2, 1, "Facility Mezzanine", out Transform mez))
        {
            Prop(d, "Desk", mez, mez.TransformPoint(new Vector3(-2.5f, 0.27f, 0.8f)), mezRot.eulerAngles.y + 180f);
            CaveSign(d, mez, mez.TransformPoint(new Vector3(0.5f, 0.3f, 1.4f)), mez.TransformPoint(new Vector3(0.5f, 0.3f, -6f)),
                     "SURVEY LOG, DAY 41\nSeismic readings from Mine 4\nare off the scale.", true);
        }
        else missed.Add("Facility mezzanine");
        for (int i = 0; i < 3; i++)
            if (PropSpot(d, routes, facility, rnd, 0.55f, 0.85f, 1.5f, out Vector3 panelAt))
                Piece("WOodPlatforms/Panel_Wood_" + "ABC"[i], m2, panelAt + Vector3.up * 0.15f,
                      Quaternion.Euler(-80f + i * 6f, (float)rnd.NextDouble() * 360f, 0f), Vector3.one, false); // toppled wall panels
        Stores(d, routes, facility, m2, rnd, 2, missed);
        AddPointLight(lights, "Facility Emergency Light", facility.transform.position + Vector3.up * 6f, new Color(1f, 0.25f, 0.15f), 4f, 20f);
        d.lights++;

        // ===== Crystal clusters (Mines 1-4): cyan, the Rift's dark red.
        int glowing = 0;
        foreach (var kv in MineCrystalPlan)
        {
            CaveSpace s = S(kv.Key);
            if (s == null) continue;
            int mine = MineOf(kv.Key);
            bool red = mine == RiftMine;
            Transform parent = mine == OldMine ? m1 : mine == DeepMine ? m2 : mine == CrystalMine ? m3 : m4;
            for (int i = 0; i < kv.Value; i++)
            {
                if (!PropSpot(d, routes, s, rnd, 0.7f, 0.95f, 0.5f, out Vector3 at, 1.5f)) { missed.Add(s.name + " crystal"); continue; }
                CrystalCluster(parent, at, Mathf.Lerp(red ? 2f : 1.2f, red ? 4.5f : 2.6f, (float)rnd.NextDouble()), rnd, red ? RiftGlow : null);
                if (glowing++ % 2 == 0)
                {
                    AddPointLight(lights, "Crystal Glow " + s.name, at + Vector3.up * 1.4f, red ? new Color(1f, 0.15f, 0.2f) : new Color(0.35f, 0.85f, 1f), red ? 2.5f : 3f, 10f);
                    d.lights++;
                }
            }
        }

        // ===== Mine 3: Crystal Caverns (crystal-lit, natural, two ways down to the Geode).
        string crystal = CrystalCavern(d, S("CrystalCavern"), m3, lights, routes, rnd);
        if (crystal != null) missed.Add(crystal);
        string geode = CrystalCavern(d, S("Crystal_Geode"), m3, lights, routes, rnd);
        if (geode != null) missed.Add(geode.Replace("Crystal Cavern", "Crystal Geode"));
        if (Mouth(d, S("CrystalTunnel_W"), S("Cavern_02"), out Vector3 cwMouth, out Vector3 cwIn))
            CaveSign(d, m3, cwMouth - cwIn * 2.5f + Vector3.Cross(Vector3.up, cwIn) * 4f, cwMouth - cwIn * 10f, "UPPER ROUTE\nCrystal Cavern");
        if (Mouth(d, S("Crystal_Lower_1"), S("Cavern_02"), out Vector3 clMouth, out Vector3 clIn))
            CaveSign(d, m3, clMouth - clIn * 2.5f + Vector3.Cross(Vector3.up, clIn) * 4f, clMouth - clIn * 10f, "LOWER ROUTE\nThe Geode");
        if (Mouth(d, S("Crystal_Lower_5"), S("Crystal_Geode"), out Vector3 gMouth, out Vector3 gIn))
            CaveSign(d, m3, gMouth + gIn * 3f + Vector3.Cross(Vector3.up, gIn) * 3.5f, gMouth + gIn * 10f, "LOWER ROUTE\nback to the Hub");
        if (Mouth(d, S("Crystal_Descent_5"), S("Crystal_Geode"), out Vector3 g2Mouth, out Vector3 g2In))
            CaveSign(d, m3, g2Mouth + g2In * 3f + Vector3.Cross(Vector3.up, g2In) * 3.5f, g2Mouth + g2In * 10f, "UPPER ROUTE\nback to the Hub");
        for (int i = 0; i < 3; i++)
            if (PropSpot(d, routes, S("Cavern_02"), rnd, 0.75f, 0.95f, 2f, out Vector3 fAt, 3f))
                Piece("Ground/Cave_Platform_" + "BCH"[i], m3, fAt - Vector3.up * 1.5f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), Vector3.one * 1.3f, true);

        // ===== Mine 4: The Rift (huge, almost no infrastructure, darkness, red glows).
        CaveSpace rift = S("Rift_Cavern"), abyss = S("Rift_Abyss"), final = S("Rift_Final");
        if (rift != null)
        {
            // A wrecked survey scaffold and dead lamps at the overlook over the chasm.
            for (int i = 0; i < 2; i++)
                if (PropSpot(d, routes, rift, rnd, 0.55f, 0.85f, 1.5f, out Vector3 scAt))
                    Piece("Rails/Rail_Scaffold_" + "AB"[i], m4, scAt, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, i * 18f), Vector3.one * 1.6f, false);
            for (int i = 0; i < 3; i++)
                if (PropSpot(d, routes, rift, rnd, 0.6f, 0.9f, 0.5f, out Vector3 deadAt))
                    d.Lamp(m4, deadAt, rift.transform.position, Color.white, 0f, 0f, false);
            Rubble(d, routes, rift, m4, rnd, 4, missed);
            if (Mouth(d, S("Rift_Descent_2"), rift, out Vector3 rcMouth, out Vector3 rcIn))
                CaveSign(d, m4, rcMouth + rcIn * 3f + Vector3.Cross(Vector3.up, rcIn) * 4f, rcMouth - rcIn * 6f, "SURVEY LIMIT\nNO LAMPS BEYOND THIS POINT");
            if (Mouth(d, S("Rift_Climb_6"), rift, out Vector3 climbMouth, out Vector3 climbIn))
                CaveSign(d, m4, climbMouth - climbIn * 2f + Vector3.Cross(Vector3.up, climbIn) * 3f, climbMouth - climbIn * 8f, "OLD SURVEY CLIMB\nto the chasm floor");
        }
        int giants = 0;
        foreach (var (space, count, minH, maxH) in new[] { (abyss, 3, 22f, 34f), (final, 4, 26f, 38f) }) // (the chasm floor is too narrow for them)
            for (int i = 0; i < count; i++)
                if (space != null && PropSpot(d, routes, space, rnd, 0.3f, 0.75f, 3f, out Vector3 gAt, 8f) &&
                    SolidPlace(Loaf + "Wall/Cave_Pillar_" + "ABC"[(i + giants) % 3] + ".prefab", m4, gAt - Vector3.up * 1f, (float)rnd.NextDouble() * 360f,
                               Mathf.Lerp(minH, maxH, (float)rnd.NextDouble()), 0.6f) != null)
                    giants++;
        if (giants < 5) missed.Add($"Rift rock columns ({giants}/7)");
        // Faint, very wide red glows high up: just enough to feel the size of the Rift's caverns in the dark.
        foreach (var (space, up, range) in new[] { (abyss, 30f, 55f), (final, 26f, 60f), (S("Rift_Chasm"), 8f, 40f) })
            if (space != null)
            {
                AddPointLight(lights, "Rift Depth Glow " + space.name, space.transform.position + Vector3.up * up, new Color(0.9f, 0.18f, 0.12f), 2.2f, range);
                d.lights++;
            }
        Rubble(d, routes, abyss, m4, rnd, 4, missed);
        Rubble(d, routes, final, m4, rnd, 6, missed);
        Rubble(d, routes, S("Rift_SideChamber"), m4, rnd, 2, missed);

        // ===== Zones: mine landmarks, reserved arenas (future major encounters), reserved vertical spaces.
        void ZoneAt(string zoneName, MapZone.ZoneKind kind, int tier, CaveSpace s, float radius, string note)
        {
            if (s != null) Zone(zones, zoneName, kind, tier, s.transform.position, radius, note);
        }
        ZoneAt("Landmark_CentralHub", MapZone.ZoneKind.Landmark, 1, hub, 22f, "Central Mining Hub: the four mine entrances, reserved service booths, gathering area.");
        ZoneAt("Landmark_Mine1_Entrance", MapZone.ZoneKind.Landmark, 1, S("OldMine_Adit"), 6f, "Mine 1 - Old Mine entrance (timber adit).");
        ZoneAt("Landmark_Mine2_Entrance", MapZone.ZoneKind.Landmark, 2, sideTunnel, 8f, "Mine 2 - Deep Mine entrance (steel portal, pipes, rails).");
        ZoneAt("Landmark_Mine3_Entrance", MapZone.ZoneKind.Landmark, 3, S("Tunnel_02"), 9f, "Mine 3 - Crystal Caverns entrance (crystal arch).");
        ZoneAt("Landmark_Mine4_Entrance", MapZone.ZoneKind.Landmark, 4, S("Rift_Portal"), 10f, "Mine 4 - The Rift entrance (the great gate).");
        ZoneAt("Landmark_MineOffice", MapZone.ZoneKind.Landmark, 1, office, 8f, "Old Mine office (old mining camp).");
        ZoneAt("Landmark_No3Workings", MapZone.ZoneKind.Landmark, 1, S("OldMine_Abandoned"), 12f, "The abandoned No. 3 workings (old rails, carts, the work table).");
        ZoneAt("Landmark_OldShaft", MapZone.ZoneKind.Landmark, 1, S("OldShaft"), 12f, "Old Shaft No. 1: the gallery looks down the shaft; three inclines lead round it to the bottom.");
        ZoneAt("Landmark_DigSite9", MapZone.ZoneKind.Landmark, 1, S("OldMine_WorkArea"), 16f, "Dig Site 9: the work area the miners abandoned, deep in the Old Mine.");
        ZoneAt("EnemyZone_Mine1_LowerLevels", MapZone.ZoneKind.Enemy, 1, S("OldMine_WorkArea"), 16f, "Reserved for future enemies.");
        ZoneAt("EnemyZone_Mine1_FinalApproach", MapZone.ZoneKind.Enemy, 2, S("OldMine_CollapsedHall"), 12f, "Reserved for future enemies.");
        ZoneAt("Landmark_Station1", MapZone.ZoneKind.Landmark, 2, station, 12f, "Deep Mine rail Station 1.");
        ZoneAt("Landmark_JunctionB", MapZone.ZoneKind.Landmark, 2, junction, 12f, "Deep Mine Junction B (loading bay).");
        ZoneAt("Landmark_Shaft2", MapZone.ZoneKind.Landmark, 2, S("DeepMine_Shaft"), 10f, "Shaft No. 2 headframe.");
        ZoneAt("Landmark_OldFacility", MapZone.ZoneKind.Landmark, 2, facility, 12f, "Survey Station 4 (abandoned; story signs).");
        ZoneAt("Landmark_CrystalCavern", MapZone.ZoneKind.Landmark, 3, S("CrystalCavern"), 18f, "Crystal Cavern (raised crystal ledge).");
        ZoneAt("Landmark_RiftOverlook", MapZone.ZoneKind.Landmark, 4, rift, 22f, "The Rift cavern: overlook over the chasm.");
        ZoneAt("BossZone_Mine2_TheHollow", MapZone.ZoneKind.Boss, 2, hollow, 24f, "Reserved: Mine 2's large encounter area (no encounter yet).");
        ZoneAt("BossZone_Mine3_CrystalGeode", MapZone.ZoneKind.Boss, 3, S("Crystal_Geode"), 22f, "Reserved: Mine 3's large encounter area (no encounter yet).");
        ZoneAt("BossZone_Mine4_RiftFinal", MapZone.ZoneKind.Boss, 4, final, 34f, "Reserved: Mine 4's final area (no encounter yet).");
        ZoneAt("Reserved_Vertical_RiftAbyss", MapZone.ZoneKind.Landmark, 4, abyss, 28f, "Huge vertical cavern (~60 m): reserved for future special traversal.");
        ZoneAt("Reserved_Vertical_RiftChasm", MapZone.ZoneKind.Landmark, 4, S("Rift_Chasm"), 24f, "The chasm under the Rift cavern (~60 m drop).");
        ZoneAt("EnemyZone_Mine2_DeepMine", MapZone.ZoneKind.Enemy, 2, S("CombatArea"), 18f, "Reserved for future enemies.");
        ZoneAt("EnemyZone_Mine3_Crystal", MapZone.ZoneKind.Enemy, 3, S("CrystalCavern"), 18f, "Reserved for future enemies.");
        ZoneAt("EnemyZone_Mine4_Abyss", MapZone.ZoneKind.Enemy, 4, abyss, 28f, "Reserved for future enemies.");

        if (timber < 40) missed.Add($"only {timber} timber/steel sets");
        Debug.Log($"[Ore What] Mine dressing: {timber} support sets, {mainLine.Count + stationLine.Count + spurLine.Count + oldLine.Count} rail pieces, {booths.Count} hub booths, {giants} giant rock columns.");
    }

    /// <summary>
    /// The Old Mine's expansion, getting older, darker and emptier the deeper it goes: the camp's store room, the
    /// abandoned No. 3 workings (rails, carts, a work table, dead lamps), the lower drifts and Old Shaft No. 1, Dig
    /// Site 9 (the work area the miners left), and the final approach (no lamps, rock formations, the last rails
    /// leading on to the arena's gate, one faint glow up at the gate).
    /// </summary>
    private static void DressOldMineExpansion(Dresser d, System.Func<string, CaveSpace> S, System.Action<List<Vector3>, string, float, float> Along,
                                              Transform m1, Transform lights, List<Corridor> routes, System.Random rnd, List<string> missed)
    {
        CaveSpace office = S("OldMine_Office"), stope = S("OldMine_Stope"), storeroom = S("OldMine_Storeroom"), abandoned = S("OldMine_Abandoned"),
                  workArea = S("OldMine_WorkArea"), hall = S("OldMine_CollapsedHall"), gate = S("OldMine_ArenaGate");
        if (abandoned == null || workArea == null || gate == null) { missed.Add("Old Mine expansion: spaces missing, dressing skipped"); return; }

        // The old camp's store room.
        MouthFrame(d, S("OldMine_StoreDoor"), office, m1, "STORES\nSIGN OUT ALL TOOLS", routes);
        Stores(d, routes, storeroom, m1, rnd, 2, missed);
        if (PropSpot(d, routes, storeroom, rnd, 0.35f, 0.7f, 0.8f, out Vector3 benchAt))
            Prop(d, "Bench", m1, benchAt, Quaternion.LookRotation(Flat(storeroom.transform.position - benchAt)).eulerAngles.y);

        // The abandoned No. 3 workings: old rails across the room with a cart left on them, a tipped cart, the work
        // table as they left it, lamps gone out.
        MouthFrame(d, S("OldMine_SideDrift"), stope, m1, "No. 3 WORKINGS\nCLOSED - BAD GROUND", routes);
        Transform ab = abandoned.transform;
        List<Vector3> abLine = Rails(d, m1, new List<Vector3> { ab.TransformPoint(new Vector3(-8f, 0f, -3f)), ab.TransformPoint(new Vector3(0f, 0f, 2f)), ab.TransformPoint(new Vector3(8f, 0f, 4f)) }, true, true, routes);
        if (abLine.Count > 4) CartOnRails(m1, abLine, (abLine.Count - 2) * 2f, "Mine_Cart_BA");
        if (PropSpot(d, routes, abandoned, rnd, 0.55f, 0.85f, 1.5f, out Vector3 tipAt))
            Piece("MineCart/Mine_Cart_AA", m1, tipAt + Vector3.up * 0.1f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 85f), Vector3.one * 1.25f, true);
        if (Footprint(d, routes, abandoned, rnd, 2.2f, 0.4f, 0.75f, out Vector3 deskAt, out Quaternion deskRot))
        {
            Prop(d, "Desk", m1, deskAt, deskRot.eulerAngles.y);
            Prop(d, "Barrel", m1, deskAt + deskRot * new Vector3(2f, 0f, 0.3f), 30f);
        }
        else missed.Add("No. 3 workings table");
        Stores(d, routes, abandoned, m1, rnd, 2, missed);
        Rubble(d, routes, abandoned, m1, rnd, 3, missed);
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, abandoned, rnd, 0.6f, 0.9f, 0.5f, out Vector3 deadAt)) d.Lamp(m1, deadAt, ab.position, Color.white, 0f, 0f, false);
        if (Mouth(d, S("OldMine_AbandonedLink"), S("MiningArea_02"), out Vector3 abMouth, out Vector3 abIn))
            CaveSign(d, m1, abMouth - abIn * 2.5f + Vector3.Cross(Vector3.up, abIn) * 3.5f, abMouth - abIn * 9f, "No. 3 WORKINGS\n(abandoned)");

        // The lower levels and Old Shaft No. 1.
        MouthFrame(d, S("OldMine_LowerDrift_1"), S("DeepCavern"), m1, "OLD SHAFT No. 1\nLOWER LEVELS", routes);
        string shaft = OldShaft(d, S("OldShaft_Gallery"), S("OldShaft"), m1, lights, routes, rnd, missed);
        if (shaft != null) missed.Add(shaft);
        CaveSpace landing = S("OldShaft_Landing_1");
        if (Mouth(d, S("OldShaft_Incline_2"), landing, out Vector3 l1Mouth, out Vector3 l1In))
            CaveSign(d, m1, l1Mouth - l1In * 2f + Vector3.Cross(Vector3.up, l1In) * 3f, l1Mouth - l1In * 8f, "SHAFT BOTTOM\nNo. 2 incline");
        if (Mouth(d, S("OldMine_OldIncline"), landing, out Vector3 oiMouth, out Vector3 oiIn))
            CaveSign(d, m1, oiMouth - oiIn * 2f - Vector3.Cross(Vector3.up, oiIn) * 3f, oiMouth - oiIn * 8f, "OLD INCLINE\nDig Site 9");
        Stores(d, routes, landing, m1, rnd, 1, missed);
        Rubble(d, routes, S("OldShaft_Landing_2"), m1, rnd, 2, missed);
        Rubble(d, routes, S("OldMine_Pocket"), m1, rnd, 1, missed);

        // Dig Site 9: the shaft line's rails end here, the foreman's shed, machines, a meal break nobody came back
        // from, the shift log.
        Transform wa = workArea.transform;
        var wPoints = new List<Vector3>();
        Along(wPoints, "OldShaft_BottomDrift", 0.05f, 1.8f); Along(wPoints, "OldShaft_BottomDrift", 0.6f, 1.8f); Along(wPoints, "OldShaft_BottomDrift", 1f, 1.8f);
        wPoints.Add(wa.TransformPoint(new Vector3(3f, 0f, 3f)));
        List<Vector3> wLine = Rails(d, m1, wPoints, true, true, routes);
        if (wLine.Count > 8) { CartOnRails(m1, wLine, (wLine.Count - 3) * 2f, "Mine_Cart_CA"); CartOnRails(m1, wLine, (wLine.Count - 6) * 2f, "Mine_Cart_AB_grp"); }
        if (Footprint(d, routes, workArea, rnd, 3.2f, 0.3f, 0.65f, out Vector3 shedAt, out Quaternion shedRot))
        {
            Hut(d, m1, shedAt, shedRot, "Foreman's Shed");
            CaveSign(d, m1, shedAt + shedRot * new Vector3(2.9f, 0f, -3f), shedAt + shedRot * new Vector3(0f, 0f, -10f), "DIG SITE 9\nFOREMAN");
        }
        else missed.Add("Dig Site 9 shed");
        if (Footprint(d, routes, workArea, rnd, 2.2f, 0.35f, 0.75f, out Vector3 mealAt, out Quaternion mealRot))
        {
            Prop(d, "Bench", m1, mealAt, mealRot.eulerAngles.y);
            Prop(d, "Barrel", m1, mealAt + mealRot * new Vector3(1.6f, 0f, -0.8f), 0f, 0.8f);
            Prop(d, "Crate", m1, mealAt + mealRot * new Vector3(-1.5f, 0f, -0.9f), mealRot.eulerAngles.y + 20f, 0.7f);
        }
        Machines(d, routes, workArea, m1, rnd, 2, missed);
        Stores(d, routes, workArea, m1, rnd, 3, missed);
        Rubble(d, routes, workArea, m1, rnd, 3, missed);
        for (int i = 0; i < 2; i++)
            if (PropSpot(d, routes, workArea, rnd, 0.6f, 0.9f, 0.5f, out Vector3 deadAt)) d.Lamp(m1, deadAt, wa.position, Color.white, 0f, 0f, false);
        if (PropSpot(d, routes, workArea, rnd, 0.5f, 0.8f, 1f, out Vector3 logAt))
            CaveSign(d, m1, logAt, wa.position, "SHIFT LOG, DAY 12\nKnocking heard below No. 9.\nWork stopped. Do not go on.");
        if (Mouth(d, S("OldMine_Approach_1"), workArea, out Vector3 apMouth, out Vector3 apIn))
            CaveSign(d, m1, apMouth - apIn * 2.5f + Vector3.Cross(Vector3.up, apIn) * 4f, apMouth - apIn * 10f, "NO ENTRY\nUNSAFE GROUND - NO LAMPS");

        // The final approach: the last rails the miners laid lead on to the arena's gate; big rock formations in the
        // collapsed hall; dead lamps; one faint red glow up at the gate (the only light ahead).
        var lastPoints = new List<Vector3>();
        Along(lastPoints, "OldMine_Approach_1", 0.12f, 2f); Along(lastPoints, "OldMine_Approach_1", 0.9f, 2f);
        Along(lastPoints, "OldMine_Approach_2", 0.08f, 2f); Along(lastPoints, "OldMine_Approach_2", 0.5f, 2f); Along(lastPoints, "OldMine_Approach_2", 0.93f, 2f);
        Along(lastPoints, "OldMine_ArenaGate", 0.15f, -2.5f); Along(lastPoints, "OldMine_ArenaGate", 0.75f, -2.5f);
        List<Vector3> lastLine = Rails(d, m1, lastPoints, true, true, routes);
        if (lastLine.Count > 20) CartOnRails(m1, lastLine, lastLine.Count * 1.2f, "Mine_Cart_BB_grp"); // left on the way down
        if (hall != null)
        {
            int formations = 0;
            for (int i = 0; i < 3; i++)
                if (PropSpot(d, routes, hall, rnd, 0.45f, 0.78f, 2.5f, out Vector3 fAt, 5f) &&
                    SolidPlace(Loaf + "Wall/Cave_Pillar_" + "ABC"[i] + ".prefab", m1, fAt - Vector3.up * 0.8f, (float)rnd.NextDouble() * 360f, Mathf.Lerp(9f, 12f, (float)rnd.NextDouble()), 0.8f) != null)
                    formations++;
            if (formations < 2) missed.Add($"Collapsed hall formations ({formations}/3)");
            Rubble(d, routes, hall, m1, rnd, 5, missed);
        }
        Rubble(d, routes, S("OldMine_ApproachTurn"), m1, rnd, 3, missed);
        foreach (string dark in new[] { "OldMine_Approach_2", "OldMine_ApproachTurn" })
            if (PropSpot(d, routes, S(dark), rnd, 0.6f, 0.9f, 0.5f, out Vector3 deadAt)) d.Lamp(m1, deadAt, S(dark).transform.position, Color.white, 0f, 0f, false);
        Vector3 gateIn = Flat(gate.End.position - gate.transform.position).normalized;
        AddPointLight(lights, "Arena Gate Glow", gate.End.position - gateIn * 6f + Vector3.up * 5f, new Color(1f, 0.38f, 0.22f), 3.5f, 26f);
        d.lights++;
    }

    /// <summary>
    /// Old Shaft No. 1: a timber collar where the gallery's floor falls away (posts, a cap beam with a pulley, a
    /// guard rail, a winch, the cable down the shaft), the cage lying wrecked at the bottom with broken timber and
    /// rubble, and a dim lamp down there so the drop reads from the gallery. Returns a note if it couldn't be built.
    /// </summary>
    private static string OldShaft(Dresser d, CaveSpace gallery, CaveSpace shaft, Transform parent, Transform lights, List<Corridor> routes, System.Random rnd, List<string> missed)
    {
        if (gallery == null || shaft == null) return "Old Shaft";
        Vector3 toShaft = Flat(shaft.transform.position - gallery.transform.position).normalized, side = Vector3.Cross(Vector3.up, toShaft);
        float galleryY = GroundY(d, gallery.transform.position);
        // The edge: from the gallery's middle toward the shaft, the first spot with no floor within 5 m below.
        Vector3 edge = Vector3.zero;
        bool found = false;
        for (float s = 0f; s < 25f && !found; s += 0.5f)
        {
            Vector3 p = gallery.transform.position + toShaft * s;
            if (!RockRay(d, new Vector3(p.x, galleryY + 1.5f, p.z), Vector3.down, 6.5f, out _)) { edge = p - toShaft * 1.4f; found = true; }
        }
        if (!found) return "Old Shaft collar (no edge found)";
        edge.y = GroundY(d, edge);
        Vector3 bottom = shaft.transform.position;
        bottom.y = GroundY(d, bottom + Vector3.up * 2f);
        float drop = edge.y - bottom.y;

        var collar = new GameObject("Old Shaft Collar").transform;
        collar.SetParent(parent, false);
        collar.position = edge;
        Quaternion facing = Quaternion.LookRotation(toShaft);
        foreach (float sgn in new[] { -1f, 1f })
        {
            Vector3 foot = edge + side * sgn * 3.3f;
            foot.y = GroundY(d, foot);
            Piece("Posts/Post_Reinforced_A", collar, foot - Vector3.up * 0.15f, facing, new Vector3(2f, (edge.y + 6.2f - foot.y) / 3.8f, 2f), true);
        }
        Piece("Posts/Beam_A", collar, edge + Vector3.up * 6.2f, facing, new Vector3(7.4f / 4f, 2f, 2f), false);   // the cap beam
        Piece("Posts/Beam_A", collar, edge + Vector3.up * 1.05f, facing, new Vector3(6.2f / 4f, 1.4f, 1.4f), true); // the guard rail (solid)
        var gear = new PropMesh(d.wood, d.metal);
        gear.Box(new Vector3(0f, 6.2f, 0.9f), Quaternion.identity, new Vector3(0.3f, 0.3f, 2.2f), 0);                  // an arm out over the shaft
        gear.Cyl(new Vector3(0f, 5.7f, 1.9f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.7f, 0.1f, 0.7f), 1);        // the pulley
        float cable = Mathf.Max(4f, drop + 5.7f - 1.5f);
        gear.Box(new Vector3(0.65f, 5.7f - cable * 0.5f, 1.9f), Quaternion.identity, new Vector3(0.06f, cable, 0.06f), 1); // the cable, down to the wreck
        GameObject head = gear.Build("Old Shaft Pulley", collar, edge, "OldShaftPulley");
        head.transform.rotation = facing;
        Vector3 winchAt = edge - toShaft * 3f - side * 5.2f;
        winchAt.y = GroundY(d, winchAt);
        if (d.cave.Distance(winchAt + Vector3.up * 2f, false) < -0.8f && RouteClear(routes, winchAt, 0.5f)) Prop(d, "Winch", parent, winchAt, facing.eulerAngles.y + 90f);
        CaveSign(d, parent, edge - toShaft * 2.5f + side * 5.5f, edge - toShaft * 10f, $"OLD SHAFT No. 1\nCAGE CONDEMNED - {drop:F0} m");

        // The bottom: the cage where it fell, broken timber, rubble, a dim lamp.
        GameObject cage = Prop(d, "Cage", parent, bottom + side * 1.2f, facing.eulerAngles.y + 25f);
        cage.transform.rotation *= Quaternion.Euler(0f, 0f, 18f);
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.1f + 0.4f;
            Vector3 at = bottom + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f;
            at.y = GroundY(d, at) + 0.15f;
            Piece("Posts/Beam_A", parent, at, Quaternion.Euler(0f, a * 57f, 4f), new Vector3(1.2f, 1.7f, 1.7f), false);
        }
        Rubble(d, routes, shaft, parent, rnd, 3, missed);
        // Bright enough to light the shaft's lower walls: from the gallery you see how far down it goes.
        AddPointLight(lights, "Old Shaft Bottom Lamp", bottom + Vector3.up * 3f, new Color(1f, 0.62f, 0.34f), 8f, 30f);
        d.lights++;
        if (Mouth(d, S2(shaft, "OldShaft_BottomDrift"), shaft, out Vector3 bdMouth, out Vector3 bdIn))
            CaveSign(d, parent, bdMouth - bdIn * 2.5f + Vector3.Cross(Vector3.up, bdIn) * 3.5f, bdMouth - bdIn * 9f, "DIG SITE 9\nthis way");
        return null;
    }

    /// <summary>A sibling cave space by name (spaces are all children of the cave).</summary>
    private static CaveSpace S2(CaveSpace sibling, string name)
    {
        Transform t = sibling.transform.parent != null ? sibling.transform.parent.Find(name) : null;
        return t != null ? t.GetComponent<CaveSpace>() : null;
    }

    private static void Zone(Transform zones, string zoneName, MapZone.ZoneKind kind, int tier, Vector3 at, float radius, string note)
    {
        var z = new GameObject(zoneName).AddComponent<MapZone>();
        z.transform.SetParent(zones, false);
        z.transform.position = at + Vector3.up * 2f;
        z.Set(kind, tier, radius, note);
    }

    /// <summary>A service booth (front = -Z toward the hub's centre): counter, plank back and side walls, roof on posts, a lamp, its sign.</summary>
    private static void Booth(Dresser d, Transform parent, Transform lights, Vector3 floor, Quaternion rot, string title)
    {
        var b = new GameObject("Booth " + title).transform;
        b.SetParent(parent, false);
        float y = float.MaxValue;
        foreach (Vector3 c in new[] { new Vector3(-2.1f, 0f, -1f), new Vector3(2.1f, 0f, -1f), new Vector3(-2.1f, 0f, 1.6f), new Vector3(2.1f, 0f, 1.6f) })
            y = Mathf.Min(y, GroundY(d, floor + rot * c));
        b.SetPositionAndRotation(new Vector3(floor.x, y, floor.z), rot);
        Vector3 P(float x, float h, float z) => b.TransformPoint(new Vector3(x, h, z));
        Prop(d, "Counter", b, P(0f, 0f, -0.7f), rot.eulerAngles.y);
        Piece("WOodPlatforms/Panel_Wood_A", b, P(0f, 0f, 1.6f), rot * Quaternion.Euler(0f, 180f, 0f), Vector3.one, true);
        Piece("WOodPlatforms/Panel_Wood_D", b, P(-2f, 0f, 0.6f), rot * Quaternion.Euler(0f, 90f, 0f), Vector3.one, true);
        Piece("WOodPlatforms/Panel_Wood_D", b, P(2f, 0f, 0.6f), rot * Quaternion.Euler(0f, -90f, 0f), Vector3.one, true);
        foreach (float x in new[] { -2f, 2f })
            Piece("WOodPlatforms/Wood_Platform_Post", b, P(x, -0.2f, -1.3f), rot, new Vector3(1f, 0.85f, 1f), true);
        Piece("WOodPlatforms/Wood_Platform_A", b, P(0f, 3.35f, 0.15f), rot, new Vector3(1.02f, 1f, 0.8f), false);
        Prop(d, "Crate", b, P(-1.3f, 0f, 1f), rot.eulerAngles.y + 10f);
        Prop(d, "Barrel", b, P(1.4f, 0f, 1.05f), 0f);
        // The booth's name on a board standing in front of its corner.
        CaveSign(d, b, P(2.9f, 0f, -1.8f), P(0f, 0f, -9f), title);
        AddPointLight(lights, "Booth Light " + title, P(0f, 2.9f, -0.2f), new Color(1f, 0.8f, 0.55f), 3.5f, 8f);
        d.lights++;
    }

    /// <summary>Benches around a lamp post: a place for players to gather.</summary>
    private static void Gathering(Dresser d, Transform parent, Transform lights, Vector3 centre)
    {
        var g = new GameObject("Gathering Area").transform;
        g.SetParent(parent, false);
        g.position = centre;
        d.Lamp(g, centre, centre + Vector3.forward, new Color(1f, 0.78f, 0.5f), 6f, 14f, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.4f;
            Vector3 at = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.4f;
            at.y = GroundY(d, at);
            Prop(d, "Bench", g, at, -a * Mathf.Rad2Deg + 90f);
        }
    }

    /// <summary>Industrial machines (pumps, winches) and crates near the walls.</summary>
    private static void Machines(Dresser d, List<Corridor> routes, CaveSpace s, Transform parent, System.Random rnd, int count, List<string> missed)
    {
        if (s == null) return;
        for (int i = 0; i < count; i++)
        {
            if (!PropSpot(d, routes, s, rnd, 0.6f, 0.88f, 2f, out Vector3 at, 2.5f)) { missed.Add(s.name + " machine"); continue; }
            Prop(d, i % 2 == 0 ? "Pump" : "Winch", parent, at, Quaternion.LookRotation(Flat(s.transform.position - at)).eulerAngles.y + 90f);
        }
    }

    /// <summary>Two pipes along a tunnel's left wall (about 1.3 / 1.8 m up), following the rock, no colliders.</summary>
    private static void Pipes(Dresser d, CaveSpace s, Transform parent)
    {
        if (s == null || s.End == null) return;
        Vector3 a = s.transform.position, b = s.End.position, dir = Flat(b - a).normalized, side = -Vector3.Cross(Vector3.up, dir);
        float length = Vector3.Distance(Flat(a), Flat(b));
        var pts = new List<Vector3>();
        for (float t = 3f; t < length - 2f; t += 4f)
        {
            Vector3 p = Vector3.Lerp(a, b, t / length);
            float g = GroundY(d, p);
            if (!RockRay(d, new Vector3(p.x, g + 1.5f, p.z), side, s.Size.x + 5f, out RaycastHit wall)) continue;
            pts.Add(new Vector3(wall.point.x, g, wall.point.z) - side * 0.45f);
        }
        if (pts.Count < 2) return;
        var mesh = new PropMesh(Steel, Hazard);
        Vector3 origin = pts[0];
        for (int i = 1; i < pts.Count; i++)
            foreach (var (h, r, mat) in new[] { (1.3f, 0.17f, 0), (1.85f, 0.11f, 0) })
            {
                Vector3 p0 = pts[i - 1] + Vector3.up * h, p1 = pts[i] + Vector3.up * h;
                Quaternion along = Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized);
                mesh.Cyl((p0 + p1) * 0.5f - origin, along, new Vector3(r, (p1 - p0).magnitude * 0.5f + 0.02f, r), mat);
                if (i % 3 == 0) mesh.Cyl(p0 - origin, along, new Vector3(r + 0.05f, 0.08f, r + 0.05f), 1); // yellow flange
            }
        mesh.Build("Pipes", parent, origin, "Pipes_" + s.name);
    }

    /// <summary>Mine 2's entrance: a riveted steel portal with hazard stripes, pipes running in, a pump, a cold floodlight.</summary>
    private static void IndustrialPortal(Dresser d, CaveSpace tunnel, CaveSpace room, Transform parent, Transform lights, List<Corridor> routes, string sign)
    {
        if (!Mouth(d, tunnel, room, out Vector3 mouth, out Vector3 inward)) return;
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        foreach (float depth in new[] { 1.5f, 6f })
        {
            Vector3 at = mouth + inward * depth;
            at.y = GroundY(d, at);
            if (!RockRay(d, at + Vector3.up * 1.6f, side, tunnel.Size.x + 6f, out RaycastHit r) ||
                !RockRay(d, at + Vector3.up * 1.6f, -side, tunnel.Size.x + 6f, out RaycastHit l)) continue;
            float top = RockRay(d, at + Vector3.up, Vector3.up, 16f, out RaycastHit c) ? c.point.y - 0.6f : at.y + 6f;
            top = Mathf.Min(top, at.y + 6.5f);
            Vector3 footL = l.point + side * 0.6f, footR = r.point - side * 0.6f;
            footL.y = GroundY(d, footL); footR.y = GroundY(d, footR);
            Quaternion rot = Quaternion.LookRotation(inward);
            var m = new PropMesh(Steel, Hazard, d.metal);
            var colliders = new List<(Vector3, Vector3)>();
            foreach (Vector3 foot in new[] { footL, footR })
            {
                float h = top - foot.y + 0.4f;
                m.Box(foot + Vector3.up * (h * 0.5f) - at, rot, new Vector3(0.75f, h, 0.75f), 0);
                m.Box(foot + Vector3.up * 0.15f - at, rot, new Vector3(1.2f, 0.3f, 1.2f), 2); // base plate
                colliders.Add((foot + Vector3.up * (h * 0.5f), new Vector3(0.75f, h, 0.75f)));
            }
            Vector3 mid = (footL + footR) * 0.5f; mid.y = top;
            float span = Vector3.Distance(Flat(footL), Flat(footR)) + 1.2f;
            m.Box(mid - at, rot, new Vector3(span, 0.8f, 0.8f), 0);
            if (depth < 2f) // hazard stripes on the front of the first portal
                for (float x = -span * 0.5f + 0.4f; x < span * 0.5f - 0.2f; x += 0.9f)
                    m.Box(mid + side * x - inward * 0.42f - at, rot * Quaternion.Euler(0f, 0f, 35f), new Vector3(0.38f, 0.75f, 0.04f), 1);
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vector3 corner = (sgn < 0 ? footL : footR); corner.y = top - 0.9f;
                m.Box(corner - side * sgn * 0.7f - at, rot * Quaternion.Euler(0f, 0f, sgn * 45f), new Vector3(0.3f, 2.2f, 0.3f), 0);
            }
            GameObject go = m.Build("Steel Portal", parent, at, depth < 2f ? "SteelPortal_" + tunnel.name : "SteelPortal2_" + tunnel.name);
            foreach (var (centre, size) in colliders)
            {
                var col = new GameObject("Post Collider", typeof(BoxCollider));
                col.transform.SetParent(go.transform, false);
                col.transform.SetPositionAndRotation(centre, rot);
                col.GetComponent<BoxCollider>().size = size;
            }
            if (depth < 2f)
            {
                AddPointLight(lights, "Deep Mine Portal Light", mid - inward * 1.2f - Vector3.up * 1f, new Color(0.82f, 0.9f, 1f), 7f, 20f);
                d.lights++;
                Vector3 pumpAt = footL - inward * 3.5f + side * 1.8f; pumpAt.y = GroundY(d, pumpAt);
                if (RouteClear(routes, pumpAt, 0.3f)) Prop(d, "Pump", parent, pumpAt, rot.eulerAngles.y);
                CaveSign(d, parent, footR - inward * 2.4f - side * 1.2f, mouth - inward * 10f, sign);
            }
        }
    }

    /// <summary>Mine 3's entrance: a natural rock arch of columns and big glowing crystals, no timber.</summary>
    private static void CrystalPortal(Dresser d, CaveSpace tunnel, CaveSpace room, Transform parent, Transform lights, List<Corridor> routes, System.Random rnd, string sign)
    {
        if (!Mouth(d, tunnel, room, out Vector3 mouth, out Vector3 inward)) return;
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 at = mouth - inward * 1f;
        if (!RockRay(d, at + Vector3.up * 1.6f, side, tunnel.Size.x + 8f, out RaycastHit r) ||
            !RockRay(d, at + Vector3.up * 1.6f, -side, tunnel.Size.x + 8f, out RaycastHit l)) return;
        int i = 0;
        foreach (Vector3 wall in new[] { l.point + side * 1.2f, r.point - side * 1.2f })
        {
            Vector3 foot = wall; foot.y = GroundY(d, foot);
            SolidPlace(Loaf + "Wall/Cave_Pillar_" + "AC"[i] + ".prefab", parent, foot - Vector3.up * 0.5f, i * 140f + 20f, 11f, 1.1f);
            Vector3 cAt = foot - inward * 2.2f + (i == 0 ? side : -side) * 1.4f; cAt.y = GroundY(d, cAt);
            CrystalCluster(parent, cAt, 3.6f, rnd);
            Vector3 cAt2 = foot + inward * 4f + (i == 0 ? side : -side) * 0.8f; cAt2.y = GroundY(d, cAt2);
            CrystalCluster(parent, cAt2, 2.4f, rnd);
            AddPointLight(lights, "Crystal Portal Glow", cAt + Vector3.up * 2f, new Color(0.35f, 0.85f, 1f), 6f, 16f);
            d.lights++;
            i++;
        }
        CaveSign(d, parent, l.point + side * 2.6f - inward * 4.5f, mouth - inward * 10f, sign);
    }

    /// <summary>
    /// Mine 4's entrance: a massive concrete-and-steel gate, damaged (a beam hangs loose), heavy equipment left at
    /// its foot, red warning lamps, warning signs - and nothing lit beyond it.
    /// </summary>
    private static void RiftGate(Dresser d, CaveSpace tunnel, CaveSpace room, Transform parent, Transform lights, List<Corridor> routes, System.Random rnd)
    {
        if (!Mouth(d, tunnel, room, out Vector3 mouth, out Vector3 inward)) return;
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 at = mouth + inward * 2f;
        at.y = GroundY(d, at);
        if (!RockRay(d, at + Vector3.up * 2f, side, tunnel.Size.x + 8f, out RaycastHit r) ||
            !RockRay(d, at + Vector3.up * 2f, -side, tunnel.Size.x + 8f, out RaycastHit l)) return;
        float top = RockRay(d, at + Vector3.up, Vector3.up, 24f, out RaycastHit c) ? c.point.y - 0.8f : at.y + 12f;
        top = Mathf.Min(top, at.y + 12.5f);
        Vector3 footL = l.point + side * 1.4f, footR = r.point - side * 1.4f;
        footL.y = GroundY(d, footL); footR.y = GroundY(d, footR);
        Quaternion rot = Quaternion.LookRotation(inward);
        var m = new PropMesh(Concrete, Steel, Hazard, WarningLamp);
        var colliders = new List<(Vector3, Vector3)>();
        foreach (Vector3 foot in new[] { footL, footR })
        {
            float h = top - foot.y + 1.2f;
            m.Box(foot + Vector3.up * (h * 0.5f) - at, rot, new Vector3(2.4f, h, 2.6f), 0);
            m.Box(foot + Vector3.up * 0.6f - at, rot, new Vector3(3.2f, 1.2f, 3.4f), 0);            // plinth
            m.Box(foot + Vector3.up * 2.4f - inward * 1.35f - at, rot, new Vector3(1.0f, 0.5f, 0.12f), 2); // stripe
            m.Box(foot + Vector3.up * (h - 1.6f) - inward * 1.35f - at, rot, new Vector3(0.45f, 0.45f, 0.2f), 3); // red lamp
            colliders.Add((foot + Vector3.up * (h * 0.5f), new Vector3(2.4f, h, 2.6f)));
        }
        Vector3 mid = (footL + footR) * 0.5f; mid.y = top + 0.6f;
        float span = Vector3.Distance(Flat(footL), Flat(footR)) + 2.4f;
        m.Box(mid - at, rot, new Vector3(span, 2f, 2.6f), 0);                                            // lintel
        m.Box(mid - Vector3.up * 1.3f - at, rot, new Vector3(span - 2.4f, 0.6f, 0.9f), 1);                // steel girder under it
        for (float x = -span * 0.5f + 1.2f; x < span * 0.5f - 1f; x += 1.4f)                               // hazard band
            m.Box(mid + side * x - inward * 1.32f + Vector3.up * 0.2f - at, rot * Quaternion.Euler(0f, 0f, 40f), new Vector3(0.55f, 1.4f, 0.05f), 2);
        // The damaged girder: torn loose at one end, hanging across the opening (high above the floor).
        Vector3 hangTop = mid + side * (span * 0.25f) - Vector3.up * 1.6f, hangLow = hangTop - side * 5f - Vector3.up * 3.2f;
        m.Box((hangTop + hangLow) * 0.5f - at, Quaternion.FromToRotation(Vector3.up, (hangTop - hangLow).normalized),
              new Vector3(0.45f, Vector3.Distance(hangTop, hangLow), 0.7f), 1);
        GameObject gate = m.Build("Rift Gate", parent, at, "RiftGate");
        foreach (var (centre, size) in colliders)
        {
            var col = new GameObject("Gate Column Collider", typeof(BoxCollider));
            col.transform.SetParent(gate.transform, false);
            col.transform.SetPositionAndRotation(centre, rot);
            col.GetComponent<BoxCollider>().size = size;
        }
        foreach (Vector3 foot in new[] { footL, footR })
        {
            AddPointLight(lights, "Rift Gate Warning Light", foot + Vector3.up * (top - foot.y - 0.4f) - inward * 1.8f, new Color(1f, 0.15f, 0.1f), 5f, 12f);
            d.lights++;
        }
        // Heavy equipment left at the gate: a big cable drum and a winch, outside the walkway.
        var drum = new PropMesh(Steel, d.wood);
        drum.Cyl(new Vector3(0f, 1.7f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.7f, 1.3f, 1.7f), 1);
        drum.Cyl(new Vector3(0f, 1.7f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.0f, 1.32f, 1.0f), 0);
        foreach (float x in new[] { -1.35f, 1.35f }) drum.Cyl(new Vector3(x, 1.7f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.75f, 0.06f, 1.75f), 0);
        Vector3 drumAt = footL - inward * 6f + side * 2.6f; drumAt.y = GroundY(d, drumAt);
        if (RouteClear(routes, drumAt, 0.3f))
        {
            GameObject dr = drum.Build("Cable Drum", parent, drumAt, "RiftCableDrum");
            dr.transform.rotation = rot;
            var box = dr.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 1.7f, 0f); box.size = new Vector3(2.8f, 3.4f, 3.4f);
        }
        Vector3 winchAt = footR - inward * 5f - side * 2.4f; winchAt.y = GroundY(d, winchAt);
        if (RouteClear(routes, winchAt, 0.3f)) Prop(d, "Winch", parent, winchAt, rot.eulerAngles.y + 90f, 1.6f);
        CaveSign(d, parent, footL - inward * 3.2f + side * 0.4f, mouth - inward * 12f, "MINE 4\nTHE RIFT");
        CaveSign(d, parent, footR - inward * 3.2f - side * 0.4f, mouth - inward * 12f, "DANGER - EXTREME DEPTH\nNO UNAUTHORISED ENTRY\nBY ORDER OF THE COMPANY");
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v : Vector3.forward; }

    /// <summary>Crates and barrels in small groups near the walls.</summary>
    private static void Stores(Dresser d, List<Corridor> routes, CaveSpace s, Transform parent, System.Random rnd, int groups, List<string> missed)
    {
        if (s == null) return;
        for (int g = 0; g < groups; g++)
        {
            if (!PropSpot(d, routes, s, rnd, 0.6f, 0.88f, 1.8f, out Vector3 at)) { missed.Add(s.name + " stores"); continue; }
            Vector3 toCentre = Flat(s.transform.position - at).normalized, along = Vector3.Cross(Vector3.up, toCentre);
            float yaw = Quaternion.LookRotation(toCentre).eulerAngles.y;
            int kind = rnd.Next(3);
            if (kind == 0)
            {
                Prop(d, "Crate", parent, at, yaw + 8f);
                Prop(d, "Crate", parent, at + along * 1f + Vector3.up * -0.02f, yaw - 12f, 0.9f);
                Prop(d, "Crate", parent, at + along * 0.45f + Vector3.up * 0.88f, yaw + 25f, 0.8f);
            }
            else if (kind == 1)
            {
                Prop(d, "Barrel", parent, at, yaw);
                Prop(d, "Barrel", parent, at + along * 0.8f, yaw + 40f);
                Prop(d, "Barrel", parent, at + along * 0.4f - toCentre * 0.7f, yaw + 90f);
            }
            else
            {
                Prop(d, "Crate", parent, at, yaw + 5f);
                Prop(d, "Barrel", parent, at + along * 1f, yaw);
            }
        }
    }

    /// <summary>Rock piles from the set (solid) and loose mounds near the walls.</summary>
    private static void Rubble(Dresser d, List<Corridor> routes, CaveSpace s, Transform parent, System.Random rnd, int piles, List<string> missed)
    {
        if (s == null) return;
        for (int i = 0; i < piles; i++)
        {
            float margin = s.SpaceKind == CaveSpace.Kind.Tunnel && s.Size.x < 5f ? 0.4f : 1.2f; // narrow tunnels: tight to the walls
            if (!PropSpot(d, routes, s, rnd, 0.6f, 0.95f, margin, out Vector3 at, 1.5f)) { missed.Add(s.name + " rubble"); continue; }
            float yaw = (float)rnd.NextDouble() * 360f;
            Piece("Ground/Ground_Mound_" + "ABCDE"[rnd.Next(5)], parent, at - Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f), Vector3.one * Mathf.Lerp(0.9f, 1.4f, (float)rnd.NextDouble()), false);
            Piece("Wall/Cave_Rocks_" + "ABCD"[rnd.Next(4)], parent, at + Vector3.up * 0.6f, Quaternion.Euler((float)rnd.NextDouble() * 20f, yaw + 60f, 0f), Vector3.one * Mathf.Lerp(0.8f, 1.3f, (float)rnd.NextDouble()), true);
        }
    }

    /// <summary>
    /// The Collapsed Mine's crawl: the roof came down part way, leaving 1.45 m under a plank lintel. An invisible
    /// slab across the whole tunnel is what makes you crouch (C); rubble above it is only looks.
    /// </summary>
    private static string Crawl(Dresser d, CaveSpace s, Transform parent, List<Corridor> routes)
    {
        if (s == null || s.End == null) return "Collapsed crawl";
        Vector3 a = s.transform.position, b = s.End.position, dir = Flat(b - a).normalized, side = Vector3.Cross(Vector3.up, dir);
        Vector3 c = Vector3.Lerp(a, b, 0.5f);
        // The gap is measured from the LOWEST floor along the middle (a narrow tunnel's floor is a shallow trough):
        // 1.65 m there, less anywhere higher, so standing (1.8 m) never fits and crouching (1.3 m) does in the middle.
        float floorLow = float.MaxValue;
        foreach (float x in new[] { -0.5f, 0f, 0.5f })
            for (float z = -1.3f; z <= 1.31f; z += 0.65f)
                floorLow = Mathf.Min(floorLow, GroundY(d, c + side * x + dir * z));
        float gapTop = floorLow + 1.65f;
        var crawl = new GameObject("Crawl").transform;
        crawl.SetParent(parent, false);
        crawl.SetPositionAndRotation(new Vector3(c.x, gapTop, c.z), Quaternion.LookRotation(dir));
        var slab = new GameObject("Fallen Roof (blocks standing)", typeof(BoxCollider));
        slab.transform.SetParent(crawl, false);
        slab.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        slab.GetComponent<BoxCollider>().size = new Vector3(14f, 5f, 2.6f);
        // Looks: a plank lintel at the gap's top, a cap beam, rubble filling the opening above.
        Piece("WOodPlatforms/Wood_Platform_Planks_B", crawl, crawl.TransformPoint(new Vector3(0f, 0.12f, 0f)), crawl.rotation * Quaternion.Euler(0f, 90f, 0f), new Vector3(1.4f, 1f, 2.2f), false);
        Piece("Posts/Beam_A", crawl, crawl.TransformPoint(new Vector3(0f, 0.32f, -1.1f)), crawl.rotation * Quaternion.Euler(0f, 0f, 4f), new Vector3(2.2f, 1.8f, 1.8f), false);
        Piece("Posts/Beam_A", crawl, crawl.TransformPoint(new Vector3(0f, 0.32f, 1.1f)), crawl.rotation * Quaternion.Euler(0f, 0f, -3f), new Vector3(2.2f, 1.8f, 1.8f), false);
        for (int i = 0; i < 6; i++)
        {
            float x = -4.5f + i * 1.8f;
            Piece("Wall/Cave_Wall_Rocks_" + "ABCDEFG"[i % 7], crawl, crawl.TransformPoint(new Vector3(x, 1.6f + (i % 2) * 0.6f, 0.2f * (i % 3 - 1))),
                  crawl.rotation * Quaternion.Euler(0f, i * 31f, 0f), Vector3.one * 1.15f, false);
        }
        foreach (float sgn in new[] { -1f, 1f }) // rubble heaped against both sides of the gap
            Piece("Wall/Cave_Rocks_" + (sgn < 0 ? "A" : "B"), crawl, crawl.TransformPoint(new Vector3(sgn * 2.3f, -0.6f, 0f)), crawl.rotation, Vector3.one * 1.3f, true);
        CaveSign(d, parent, a + dir * 1.5f + side * 2.3f, a - dir * 6f, "LOW ROOF\nCROUCH (C)");
        return null;
    }

    /// <summary>Station 1: a low plank platform beside the rails (low enough to walk onto), a lamp and stores.</summary>
    private static void StationPlatform(Dresser d, CaveSpace station, List<Vector3> rails, Transform parent, List<Corridor> routes, List<string> missed)
    {
        if (station == null || rails.Count < 4) return;
        int best = -1; float bestD = float.MaxValue;
        for (int i = 1; i < rails.Count - 1; i++)
        {
            float dd = Flat(rails[i] - station.transform.position).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        Vector3 dir = Flat(rails[best + 1] - rails[best - 1]).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        if (Vector3.Dot(side, station.transform.position - rails[best]) < 0f) side = -side; // the platform is on the room side
        var plat = new GameObject("Station Platform").transform;
        plat.SetParent(parent, false);
        int pieces = 0;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 c = rails[best] + dir * (k * 4f) + side * 2.9f;
            float g = GroundY(d, c);
            if (d.cave.Distance(new Vector3(c.x, g + 2.2f, c.z), false) > -0.5f) continue; // into the wall
            // Planks' top sits 0.27 m above the floor: below the player's step height, so it's walked onto, not jumped.
            Piece("WOodPlatforms/Wood_Platform_Planks_A", plat, new Vector3(c.x, g + 0.15f, c.z), Quaternion.LookRotation(dir), Vector3.one, true);
            pieces++;
        }
        if (pieces == 0) { missed.Add("Station platform"); return; }
        Vector3 lampAt = rails[best] + side * 4.4f + dir * 3f; lampAt.y = GroundY(d, lampAt) + 0.27f;
        d.Lamp(plat, lampAt, rails[best], new Color(1f, 0.78f, 0.5f), 6f, 18f, true);
        Vector3 crateAt = rails[best] + side * 4.2f - dir * 4f; crateAt.y = GroundY(d, crateAt) + 0.27f;
        Prop(d, "Crate", plat, crateAt, Quaternion.LookRotation(dir).eulerAngles.y);
        Prop(d, "Barrel", plat, crateAt + dir * 1.1f, 0f);
    }

    /// <summary>The Crystal Cavern's look: crystals on the floor edges and the ledge, cyan glow lights, rock formations.</summary>
    private static string CrystalCavern(Dresser d, CaveSpace s, Transform parent, Transform lights, List<Corridor> routes, System.Random rnd)
    {
        if (s == null) return "Crystal Cavern";
        int placed = 0;
        void Crystal(Vector3 at, float size) { CrystalCluster(parent, at, size, rnd); placed++; }
        for (int i = 0; i < 12; i++)
            if (PropSpot(d, routes, s, rnd, 0.72f, 0.96f, 0.5f, out Vector3 at, 1.5f)) Crystal(at, Mathf.Lerp(1.4f, 3.6f, (float)rnd.NextDouble()));
        // The ledge: a cluster of big crystals on top.
        Vector3 ledge = s.PlateauCentre;
        ledge.y = GroundY(d, ledge + Vector3.up * (s.PlateauHeight + 2f), 3f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.4f;
            Vector3 p = ledge + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Lerp(1.5f, 3f, (float)rnd.NextDouble());
            p.y = GroundY(d, p);
            Crystal(p, i == 0 ? 5f : Mathf.Lerp(2.2f, 3.4f, (float)rnd.NextDouble()));
        }
        // Light: one big glow over the ledge, four around the room.
        AddPointLight(lights, "Crystal Cavern Ledge Glow", ledge + Vector3.up * 3f, new Color(0.35f, 0.85f, 1f), 9f, 20f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.8f;
            Vector3 p = s.transform.position + s.transform.rotation * new Vector3(Mathf.Cos(a) * s.Size.x * 0.6f, 4f, Mathf.Sin(a) * s.Size.z * 0.6f);
            AddPointLight(lights, "Crystal Cavern Glow", p, i % 2 == 0 ? new Color(0.3f, 0.8f, 1f) : new Color(0.5f, 0.6f, 1f), 5f, 16f);
        }
        d.lights += 5;
        // Rock formations (solid) at the walls, around the ledge.
        for (int i = 0; i < 4; i++)
            if (PropSpot(d, routes, s, rnd, 0.8f, 0.95f, 2f, out Vector3 at, 3f))
                Piece("Ground/Cave_Platform_" + "BCHI"[i], parent, at + Vector3.up * (i < 2 ? -2.6f : 0.2f), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), Vector3.one * 1.2f, true);
        return placed < 12 ? $"Crystal Cavern ({placed}/16 crystals)" : null;
    }

    /// <summary>Shaft No. 2: a timber headframe over the old shaft collar (the pit), a cage at the bottom, a winch.</summary>
    private static void Headframe(Dresser d, CaveSpace shaft, Transform parent, Transform lights, CaveSpace wayIn)
    {
        if (shaft == null) return;
        Vector3 pit = shaft.PlateauCentre;
        pit.y = GroundY(d, pit + Vector3.up * 1f, 2f);
        var frame = new PropMesh(d.wood, d.metal);
        float h = 11f, half = 2.4f;
        var posts = new List<Vector3>();
        foreach (float x in new[] { -half, half })
            foreach (float z in new[] { -half, half })
            {
                frame.Box(new Vector3(x, h * 0.5f, z), Quaternion.identity, new Vector3(0.45f, h, 0.45f), 0);
                posts.Add(new Vector3(x, h * 0.5f, z));
            }
        foreach (float y in new[] { 3.6f, 7.2f, h })
        {
            frame.Box(new Vector3(0f, y, -half), Quaternion.identity, new Vector3(half * 2f + 0.5f, 0.35f, 0.35f), 0);
            frame.Box(new Vector3(0f, y, half), Quaternion.identity, new Vector3(half * 2f + 0.5f, 0.35f, 0.35f), 0);
            frame.Box(new Vector3(-half, y, 0f), Quaternion.identity, new Vector3(0.35f, 0.35f, half * 2f + 0.5f), 0);
            frame.Box(new Vector3(half, y, 0f), Quaternion.identity, new Vector3(0.35f, 0.35f, half * 2f + 0.5f), 0);
        }
        frame.Box(new Vector3(-half, 5.4f, 0f), Quaternion.Euler(40f, 0f, 0f), new Vector3(0.25f, 5.4f, 0.25f), 0); // one brace snapped (damaged)
        frame.Box(new Vector3(half, 4.2f, 0.6f), Quaternion.Euler(-62f, 0f, 0f), new Vector3(0.25f, 3.2f, 0.25f), 0);
        frame.Cyl(new Vector3(0f, h + 1.3f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.4f, 0.15f, 1.4f), 1);
        frame.Box(new Vector3(0f, h + 0.55f, 0f), Quaternion.identity, new Vector3(0.3f, 1.5f, 0.3f), 1);
        frame.Box(new Vector3(1.35f, (h + 1.3f + 2.6f) * 0.5f, 0f), Quaternion.identity, new Vector3(0.07f, h + 1.3f - 2.6f, 0.07f), 1);
        GameObject go = frame.Build("Shaft No. 2 Headframe", parent, pit, "ShaftHeadframe");
        foreach (Vector3 c in posts)
        {
            var col = new GameObject("Post Collider", typeof(BoxCollider));
            col.transform.SetParent(go.transform, false);
            col.transform.localPosition = c;
            col.GetComponent<BoxCollider>().size = new Vector3(0.5f, h, 0.5f);
        }
        Prop(d, "Cage", parent, pit + new Vector3(0.6f, 0f, 0.4f), 12f);
        Vector3 winchAt = pit + new Vector3(-half - 4f, 0f, 1f); winchAt.y = GroundY(d, winchAt);
        if (d.cave.Distance(winchAt + Vector3.up * 2f, false) < -0.5f) Prop(d, "Winch", parent, winchAt, 90f);
        Vector3 from = wayIn != null ? wayIn.End.position : pit + Vector3.forward * 10f; // where players arrive
        Vector3 toward = Flat(from - pit).normalized;
        CaveSign(d, parent, pit + toward * (half + 2.5f) + Vector3.Cross(Vector3.up, toward) * 3f, from, "SHAFT No. 2\nCAGE OUT OF ORDER");
        AddPointLight(lights, "Headframe Lamp", pit + Vector3.up * (h - 0.8f), new Color(1f, 0.6f, 0.34f), 7f, 22f);
        d.lights++;
    }
}
