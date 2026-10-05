using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Old Mine's final destination. Layout migrates once; all dressing regenerates on every map build.</summary>
public static partial class IslandMapBuilder
{
    private const string OldMineBossMarker = "_Layout: Old Mine Boss Arena";
    private const float OldMineCombatRadius = 18f;

    private static string ApplyOldMineBossLayout(CaveLayout cave)
    {
        if (cave.transform.Find(OldMineBossMarker) != null) return "";
        Transform hub = cave.transform.Find("Cavern_01");
        Transform arena = cave.transform.Find("BossArena");
        if (hub == null || arena == null) return "Old Mine boss layout: missing hub or arena. ";
        // Keep the existing large cavern and every earlier route. Only shorten the last incline and add its throat.
        foreach (MineSpace space in MineSpaces)
            if (space.name == "OldMine_ArenaGate" || space.name == "OldMine_BossThreshold")
                SetSpace(cave.transform, space, hub.position.y);
        arena.GetComponent<CaveSpace>().SetFloorShape(0f, 0f, 0.5f, 0f, Vector2.zero, 5f, 4f);
        new GameObject(OldMineBossMarker).transform.SetParent(cave.transform, false);
        cave.Prepare();
        return "Old Mine boss layout: flat entrance throat added; existing arena and routes preserved. ";
    }

    /// <summary>Grounded timber wreckage around an empty fighting floor; no encounters or blocking doors.</summary>
    private static string DressOldMineBossArena(Dresser d, CaveSpace arena, CaveSpace threshold,
                                               Transform props, Transform lights, Transform zones)
    {
        if (arena == null || threshold == null) return "Old Mine boss arena or threshold missing";
        Vector3 centre = arena.transform.position;
        if (!RockRay(d, centre + Vector3.up * 4f, Vector3.down, 10f, out RaycastHit floor))
            return "Old Mine boss arena floor missing";
        centre.y = floor.point.y;
        var missed = new List<string>();
        Vector3 entry = threshold.transform.position;
        Vector3 inward = Flat(threshold.End.position - entry).normalized;
        Quaternion facing = Quaternion.LookRotation(inward);
        Vector3 side = Vector3.Cross(Vector3.up, inward);

        // A paired, reinforced timber threshold at the mouth. Nothing crosses the walking opening below 7 m.
        for (int row = 0; row < 2; row++)
        {
            Vector3 origin = entry + inward * (row * 6f);
            origin.y = GroundY(d, origin);
            var timbers = new PropMesh(d.wood, d.metal);
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 foot = side * (sign * 3.9f);
                timbers.Box(foot + Vector3.up * 3.8f, facing, new Vector3(0.85f, 8f, 0.85f), 0);
                foreach (float y in new[] { 0.8f, 3f, 6.8f })
                    timbers.Box(foot + Vector3.up * y, facing, new Vector3(0.94f, 0.25f, 0.94f), 1);
                timbers.Box(foot - side * (sign * 0.9f) + Vector3.up * 6.7f,
                    facing * Quaternion.Euler(0f, 0f, sign * 38f), new Vector3(0.4f, 2.9f, 0.4f), 0);
            }
            timbers.Box(Vector3.up * 7.8f, facing, new Vector3(8.8f, 0.95f, 0.95f), 0);
            // Splintered upper reinforcement; these fragments stay high above the route.
            timbers.Box(side * -2.6f + Vector3.up * 8.35f, facing * Quaternion.Euler(0f, 0f, -9f),
                new Vector3(3.4f, 0.32f, 0.55f), 0);
            timbers.Box(side * 2.8f + Vector3.up * 8.1f, facing * Quaternion.Euler(0f, 0f, 14f),
                new Vector3(2.8f, 0.32f, 0.55f), 0);
            GameObject frame = timbers.Build("Boss Entrance - Heavy Timbers " + (row + 1), props, origin,
                "OldMineBossEntrance" + row);
            frame.AddComponent<MeshCollider>().sharedMesh = frame.GetComponent<MeshFilter>().sharedMesh;
        }
        CaveSign(d, props, entry - inward * 3f + side * 4f, entry - inward * 12f,
            "LAST WORKING\nCREW ACCESS SUSPENDED");
        AddPointLight(lights, "Boss Entrance Amber", entry + Vector3.up * 6f,
            new Color(1f, 0.55f, 0.24f), 5f, 20f);
        d.lights++;

        // The broken winding tower is at the far edge, visible across the empty floor from the entrance.
        Vector3 landmark = centre + new Vector3(-9f, 0f, 20f);
        landmark.y = GroundY(d, landmark);
        var wreck = new PropMesh(d.wood, d.metal);
        foreach (float x in new[] { -3f, 3f })
            foreach (float z in new[] { -2f, 2f })
            {
                float height = z > 0f ? 4.5f : 11.5f;
                wreck.Box(new Vector3(x, height * 0.5f - 0.15f, z), Quaternion.identity,
                    new Vector3(0.75f, height + 0.3f, 0.75f), 0);
                foreach (float y in new[] { 1.2f, 3.4f })
                    wreck.Box(new Vector3(x, y, z), Quaternion.identity, new Vector3(0.84f, 0.25f, 0.84f), 1);
            }
        wreck.Box(new Vector3(0f, 11.3f, -2f), Quaternion.Euler(0f, 0f, -5f), new Vector3(7.6f, 0.7f, 0.8f), 0);
        wreck.Box(new Vector3(-1.2f, 5.1f, 0.4f), Quaternion.Euler(32f, 0f, -18f), new Vector3(0.5f, 10f, 0.5f), 0);
        wreck.Box(new Vector3(0.5f, 0.45f, 2.8f), Quaternion.Euler(0f, 18f, -3f), new Vector3(9f, 0.7f, 0.7f), 0);
        wreck.Cyl(new Vector3(0.5f, 11.5f, -2f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.4f, 0.25f, 1.4f), 1);
        wreck.Box(new Vector3(1f, 6.5f, -2f), Quaternion.Euler(0f, 0f, 8f), new Vector3(0.08f, 8f, 0.08f), 1);
        GameObject headframe = wreck.Build("Landmark - Wrecked Crew Headframe", props, landmark, "OldMineBossHeadframe");
        headframe.AddComponent<MeshCollider>().sharedMesh = headframe.GetComponent<MeshFilter>().sharedMesh;
        // An overturned cart below the dangling cable and a collapsed working deck.
        Piece("MineCart/Mine_Cart_AA", props, landmark + new Vector3(2f, 0.3f, 1.5f),
            Quaternion.Euler(0f, 25f, 78f), Vector3.one * 1.25f, true);
        Piece("WOodPlatforms/Panel_Wood_A", props, landmark + new Vector3(-2f, 0.7f, 2.5f),
            Quaternion.Euler(10f, 30f, 8f), Vector3.one, true);
        Floodlight(d, props, lights, centre + new Vector3(15f, 0f, 20f), landmark + Vector3.up * 6f,
            "Wrecked Headframe", "OldMineBossFloodlight");
        var headframeLight = lights.Find("Floodlight Wrecked Headframe").GetComponent<Light>();
        headframeLight.intensity = 110f;
        headframeLight.spotAngle = 30f;
        AddPointLight(lights, "Wreckage Warm Spill", landmark + Vector3.up * 7f,
            new Color(1f, 0.63f, 0.32f), 14f, 24f);
        d.lights++;

        // Dim warm pools define the oval. The centre gets broad, subdued fill rather than crystal light.
        AddPointLight(lights, "Arena Warm Fill", centre + Vector3.up * 10f, new Color(1f, 0.68f, 0.38f), 32f, 48f);
        d.lights++;
        int pillars = 0;
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2f / 10f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * arena.Size.x * 0.82f, 0f,
                Mathf.Sin(angle) * arena.Size.z * 0.82f);
            Vector3 at = centre + offset;
            // Wide opening, clear central combat disk, and space for the single landmark.
            if (Vector3.Distance(Flat(at), Flat(entry)) < 13f || Vector3.Distance(Flat(at), Flat(landmark)) < 9f) continue;
            if (!RockRay(d, at + Vector3.up * 5f, Vector3.down, 12f, out RaycastHit ground)) continue;
            GameObject rock = Place(RockFolder + "PT_Generic_Rock_01.prefab", props,
                ground.point - Vector3.up * 0.6f, i * 53f, 6f + i % 3);
            if (rock == null) { missed.Add("arena perimeter rock " + i); continue; }
            rock.name = "Arena Edge Cover";
            rock.transform.localScale = Vector3.Scale(rock.transform.localScale, new Vector3(0.55f, 1f, 0.55f));
            foreach (MeshFilter filter in rock.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            pillars++;
            if ((i & 1) == 0)
            {
                d.Lamp(lights, ground.point, centre, new Color(1f, 0.66f, 0.35f), 4.5f, 28f, true);
                Prop(d, "Crate", props, ground.point + offset.normalized * -2f, i * 29f, 0.7f);
            }
            else d.Lamp(props, ground.point, centre, Color.white, 0f, 0f, false);
        }
        if (pillars < 4) missed.Add("arena edge cover: " + pillars + "/4");

        // A crew supply point left mid-shift, kept at the edge rather than becoming central clutter.
        Vector3 camp = centre + new Vector3(-23f, 0f, -5f);
        camp.y = GroundY(d, camp);
        Prop(d, "Desk", props, camp, 80f);
        Prop(d, "Crate", props, camp + new Vector3(2f, 0f, 1.5f), 25f, 0.8f);
        Prop(d, "Barrel", props, camp + new Vector3(-1.5f, 0f, 2f), 0f, 0.8f);
        Piece("MineCart/Mine_Cart_BA", props, camp + new Vector3(0f, 0.2f, -3f),
            Quaternion.Euler(0f, -35f, 18f), Vector3.one, true);
        d.Lamp(props, camp + new Vector3(1f, 0f, -1.5f), centre, Color.white, 0f, 0f, false);

        void Reserve(string name, Vector3 at, float radius, MapZone.ZoneKind kind, string notes)
        {
            var zone = new GameObject(name).AddComponent<MapZone>();
            zone.transform.SetParent(zones, false);
            zone.transform.position = at + Vector3.up;
            zone.Set(kind, 1, radius, notes);
        }
        Reserve("Reserved_OldMine_BossArena", centre, OldMineCombatRadius, MapZone.ZoneKind.Boss,
            "Mine 1 endpoint. Clear 36 m diameter fighting floor inside a 68 x 60 m cavern. No boss gameplay yet.");
        Reserve("Reserved_OldMine_BossSpawn", centre + Vector3.forward * 7f, 5f, MapZone.ZoneKind.Boss,
            "Future boss spawn; empty ground, no encounter logic.");
        Reserve("Reserved_OldMine_ArenaCenter", centre, 3f, MapZone.ZoneKind.Landmark, "Arena centre reference.");
        Reserve("Reserved_OldMine_PlayerEntrance", threshold.End.position, 4f, MapZone.ZoneKind.Landmark,
            "Walkable arena entrance and return to Old Mine. No locking door.");
        return missed.Count == 0 ? null : string.Join(", ", missed);
    }
}
