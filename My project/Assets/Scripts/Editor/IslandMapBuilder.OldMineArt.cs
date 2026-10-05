using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Reusable crew equipment and authored compositions. All placements survive an island rebuild.</summary>
public static partial class IslandMapBuilder
{
    // Shared wood/iron palette, no textures or new materials. Small tools deliberately have no collider.
    private static bool OldMineKit(Dresser d, string name, out PropMesh m, out Vector3 size, out Vector3 centre)
    {
        var kitMesh = new PropMesh(d.wood, d.metal);
        m = kitMesh;
        size = centre = Vector3.zero;
        void B(float x, float y, float z, float sx, float sy, float sz, int material = 0, float tilt = 0f) =>
            kitMesh.Box(new Vector3(x, y, z), Quaternion.Euler(0f, 0f, tilt), new Vector3(sx, sy, sz), material);
        void Pick(float x, float y, float z, float tilt, bool lying = false)
        {
            Vector3 origin = new Vector3(x, y, z);
            Quaternion rotation = lying ? Quaternion.Euler(90, tilt, 0) : Quaternion.Euler(0, 0, tilt);
            kitMesh.Box(origin, rotation, new Vector3(.09f, 1.1f, .09f), 0);
            kitMesh.Box(origin + rotation * new Vector3(0, .43f, 0), rotation, new Vector3(.65f, .13f, .13f), 1);
            foreach (float side in new[] { -1f, 1f }) kitMesh.Box(origin + rotation * new Vector3(side * .34f, .37f, 0),
                rotation * Quaternion.Euler(0, 0, -side * 32), new Vector3(.22f, .09f, .12f), 1);
        }
        switch (name)
        {
            case "ToolRack":
                foreach (float x in new[] { -.9f, .9f }) { B(x, 1f, 0, .14f, 2f, .14f); B(x, .08f, 0, .4f, .16f, .65f); }
                B(0, 1.65f, 0, 2.1f, .18f, .17f); B(0, .45f, 0, 2f, .14f, .16f);
                Pick(-.55f, 1f, -.15f, -8); Pick(.25f, 1f, -.15f, 5);
                B(.8f, .96f, -.16f, .09f, 1.4f, .09f); B(.8f, .3f, -.16f, .3f, .4f, .08f, 1);
                size = new Vector3(2.2f, 2f, .65f); break;
            case "StoresShelf":
                foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -.35f, .35f }) B(x, 1.1f, z, .12f, 2.2f, .12f);
                foreach (float y in new[] { .12f, .86f, 1.6f }) B(0, y, 0, 2.2f, .12f, .85f);
                B(0, 1.1f, .37f, .12f, 2.6f, .1f, 0, 42);
                foreach (float x in new[] { -.7f, 0f, .7f }) { B(x, .43f, 0, .55f, .5f, .6f); B(x, .67f, 0, .58f, .06f, .63f, 1); }
                B(-.55f, 1.13f, 0, .65f, .42f, .58f); B(.5f, 1.83f, 0, .7f, .36f, .55f, 1);
                size = new Vector3(2.3f, 2.3f, .9f); break;
            case "CableReel":
                B(0, .1f, 0, 1.6f, .2f, 1.2f);
                foreach (float x in new[] { -.62f, .62f })
                    m.Cyl(new Vector3(x, .72f, 0), Quaternion.Euler(0, 0, 90), new Vector3(.63f, .08f, .63f), 0);
                m.Cyl(new Vector3(0, .72f, 0), Quaternion.Euler(0, 0, 90), new Vector3(.4f, .58f, .4f), 1);
                B(0, .23f, -.48f, 1.3f, .12f, .12f, 1);
                size = new Vector3(1.6f, 1.4f, 1.25f); break;
            case "CrewTable":
                B(0, .78f, 0, 2.2f, .12f, 1.1f);
                foreach (float x in new[] { -.9f, .9f }) foreach (float z in new[] { -.4f, .4f }) B(x, .37f, z, .13f, .74f, .13f);
                foreach (float x in new[] { -.6f, .6f })
                {
                    B(x, .45f, -.9f, .55f, .12f, .45f);
                    foreach (float xx in new[] { -.2f, .2f }) B(x + xx, .21f, -.9f, .1f, .42f, .35f);
                    B(x, .9f, 0, .15f, .18f, .15f, 1); B(x, .91f, .25f, .4f, .05f, .3f, 1);
                }
                B(.25f, .91f, -.25f, .48f, .1f, .3f); // closed shift ledger
                size = new Vector3(2.2f, 1f, 2.25f); break;
            case "RepairBench":
                B(0, .85f, 0, 2.2f, .13f, 1f);
                foreach (float x in new[] { -.95f, .95f }) foreach (float z in new[] { -.35f, .35f }) B(x, .4f, z, .13f, .8f, .13f);
                B(0, .18f, 0, 2f, .1f, .65f);
                B(-.65f, 1f, 0, .4f, .3f, .45f, 1); B(-.65f, 1.05f, -.3f, .55f, .1f, .1f, 1);
                B(.4f, .98f, 0, .85f, .1f, .1f, 0, 4); B(.8f, 1.03f, 0, .2f, .22f, .35f, 1);
                B(.15f, .3f, 0, .6f, .25f, .45f, 1);
                size = new Vector3(2.2f, 1.2f, 1f); break;
            case "ToolBundle":
                Pick(-.24f, .08f, 0, 86, true); Pick(.25f, .13f, .22f, 70, true);
                B(0, .1f, -.2f, 1.1f, .1f, .1f); B(.5f, .12f, -.2f, .3f, .22f, .3f, 1);
                break;
            case "OreChute":
                foreach (float x in new[] { -.85f, .85f }) foreach (float z in new[] { -.8f, .8f }) B(x, 1.4f, z, .22f, 2.8f, .22f);
                foreach (float z in new[] { -.8f, .8f }) B(0, 2.9f, z, 2.1f, .22f, .22f);
                B(0, 2.1f, 0, 1.6f, .18f, 2.6f, 0, 0);
                // The long inclined tray feeds the cart at its front (-Z).
                m.Box(new Vector3(0, 1.8f, -.4f), Quaternion.Euler(-24, 0, 0), new Vector3(1.5f, .15f, 3f), 0);
                foreach (float x in new[] { -.8f, .8f })
                    m.Box(new Vector3(x, 2.05f, -.4f), Quaternion.Euler(-24, 0, 0), new Vector3(.14f, .5f, 3f), 0);
                B(0, .3f, .7f, 2.1f, .16f, .16f, 1);
                size = new Vector3(2.15f, 3f, 3.5f); break;
            default: return false;
        }
        centre = new Vector3(0, size.y * .5f, 0);
        return true;
    }

    private static bool DressOldMineStores(Dresser d, CaveSpace s, Transform parent, Vector3 at, float yaw, int group, int kind)
    {
        string kit;
        switch (s.name)
        {
            case "OldMine_Office": kit = group == 0 ? "ToolRack" : "CrewTable"; break;
            case "OldMine_Storeroom": kit = "StoresShelf"; break;
            case "OldMine_Stope": kit = "OreChute"; break;
            case "MiningArea_02": kit = group == 0 ? "RepairBench" : "CableReel"; break;
            case "OldMine_Abandoned": kit = group == 0 ? "ToolRack" : "StoresShelf"; break;
            case "DeepCavern": kit = group == 0 ? "CableReel" : "RepairBench"; break;
            case "OldMine_WorkArea": kit = group == 0 ? "RepairBench" : group == 1 ? "CableReel" : "StoresShelf"; break;
            default: return false;
        }
        // New assemblies must fit the actual rock, not merely the nominal room envelope.
        // Chute is larger than the original crate cluster: shrink it to the reserved stores footprint.
        float scale = kit == "OreChute" ? .85f : 1f;
        Quaternion rot = Quaternion.Euler(0, yaw + 180f, 0);
        float halfX = kit == "CableReel" ? .85f : 1.15f;
        float halfZ = kit == "OreChute" ? 1.5f : kit == "CrewTable" ? 1.15f : .6f;
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            if (d.cave.Distance(at + rot * new Vector3(x * halfX, 1.5f, z * halfZ), false) > -.25f) return false;
        Transform station = Child(parent, "Crew Station - " + s.name + " " + group);
        var go = Prop(d, kit, station, at, rot.eulerAngles.y, scale);
        if (s.name == "OldMine_Abandoned")
        {
            go.transform.rotation *= Quaternion.Euler(0, 0, group == 0 ? 67f : 8f);
            GroundArtObject(d, go);
            Prop(d, "ToolBundle", station, at + rot * new Vector3(.5f, .02f, -.7f), yaw);
        }
        return true;
    }

    private static void GroundArtObject(Dresser d, GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        Vector3 pos = go.transform.position;
        pos.y += GroundY(d, new Vector3(b.center.x, pos.y, b.center.z), 5f) - b.min.y;
        go.transform.position = pos;
    }

    private static void DressOldMineArt(Dresser d, System.Func<string, CaveSpace> S, Transform props, Transform lighting, List<string> missed)
    {
        Transform mine = props.Find("Mine/Mine1_OldMine"), arena = props.Find("BossArena");
        if (mine == null || arena == null) return;
        Transform art = Child(mine, "Old Mine Art"), lights = Child(lighting, "Old Mine Art");
        var routes = Corridors(d.cave.Spaces);
        var rnd = new System.Random(583091); // isolated art sequence; no changes to any other mine
        Material stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Island/Cave_Rock.mat");

        // Keep all carts and the new stations grounded even when rotated or on a sloping floor.
        foreach (Transform t in mine.GetComponentsInChildren<Transform>())
        {
            bool rubble = t.name.StartsWith("Ground_Mound_") || t.name.StartsWith("Cave_Rocks_");
            if (t.name.StartsWith("Ground_Mound_"))
            {
                // Some source mounds are almost ten metres wide: their low rim floats over uneven
                // ground and can engulf a work table. Keep loose spoil within the reserved footprint.
                var r = t.GetComponentInChildren<Renderer>();
                if (r != null) t.localScale *= Mathf.Min(1f, 3f / Mathf.Max(r.bounds.size.x, r.bounds.size.z));
            }
            if (t.name.StartsWith("Mine_Cart_") || rubble) GroundArtObject(d, t.gameObject);
            if (rubble && stone != null) foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
        }

        // Extend the service line into the workings. Each tunnel is sampled inside its own carved air;
        // room lines form a readable fork rather than crossing solid corners between distant tunnels.
        void Track(string name, float side, bool broken)
        {
            CaveSpace s = S(name); if (s == null || s.End == null) return;
            Vector3 a = s.transform.position, b = s.End.position, dir = Flat(b - a).normalized;
            Vector3 offset = Vector3.Cross(Vector3.up, dir) * side;
            int runs = broken ? 2 : 1;
            for (int i = 0; i < runs; i++)
            {
                float start = broken ? .12f + i * .48f : .12f, end = broken ? start + .27f : .86f;
                var points = new List<Vector3>();
                for (int k = 0; k < 5; k++) points.Add(Vector3.Lerp(a, b, Mathf.Lerp(start, end, k / 4f)) + offset);
                Rails(d, Child(art, "Service Rails - " + name), points, false, false, routes);
            }
        }
        Track("OldMine_Drift_1", 1.65f, false); Track("OldMine_Drift_2", 1.65f, false);
        Track("OldMine_SideDrift", -1.4f, true); Track("OldMine_LowerDrift_1", 1.5f, true);
        CaveSpace stope = S("OldMine_Stope");
        Transform junction = Child(art, "Landmark - Sorting Junction");
        Vector3 J(float x, float z) => stope.transform.TransformPoint(new Vector3(x, 0, z));
        Rails(d, junction, new List<Vector3> { J(3, -8), J(2.5f, 0), J(5, 7) }, false, false, routes);
        Rails(d, junction, new List<Vector3> { J(2.5f, 0), J(-1, 2), J(-7, 4) }, false, false, routes);
        Zone(props.parent.Find("Zones"), "Landmark_OldMine_SortingJunction", MapZone.ZoneKind.Landmark, 1,
            stope.transform.position, 10f, "Maintained sorting chute and service line; broken branch leads to No. 3 workings.");

        // Dress the existing hut interiors, not a second office dropped on top of them.
        Transform office = mine.Find("Mine Office"), shed = mine.Find("Foreman's Shed");
        if (office != null)
        {
            Prop(d, "ToolRack", office, office.TransformPoint(new Vector3(-1.45f, .05f, -.2f)), office.eulerAngles.y + 90);
            Prop(d, "Bench", office, office.TransformPoint(new Vector3(.2f, .05f, -.1f)), office.eulerAngles.y, .65f);
            // A warm interior pool reveals the ledger and tools through the doorway.
            AddPointLight(lights, "Office Lantern", office.TransformPoint(new Vector3(0, 2.4f, 0)), new Color(1, .72f, .43f), 5f, 10f); d.lights++;
        }
        if (shed != null)
            Prop(d, "ToolBundle", shed, shed.TransformPoint(new Vector3(.5f, .86f, 1.2f)), shed.eulerAngles.y + 90);

        // Work stations supply composition anchors: a chute feeds an ore cart; tools belong beside benches.
        var stations = new List<Transform>();
        foreach (Transform t in mine.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Crew Station - ")) stations.Add(t);
        foreach (Transform station in stations)
        {
            if (station.childCount == 0) continue;
            Transform equipment = station.GetChild(0);
            if (equipment.name == "OreChute")
            {
                Vector3 cartAt = equipment.TransformPoint(new Vector3(0, 0, -2.7f)); cartAt.y = GroundY(d, cartAt);
                if (RouteClear(routes, cartAt, .8f) && d.cave.Distance(cartAt + Vector3.up, false) < -.5f)
                {
                    var cart = Piece("MineCart/Mine_Cart_CB_grp", station, cartAt, equipment.rotation * Quaternion.Euler(0, -90, 0), Vector3.one * 1.25f, true);
                    if (cart != null) GroundArtObject(d, cart);
                }
                AddPointLight(lights, "Sorting Lamp", equipment.position + Vector3.up * 3.3f, new Color(1, .74f, .45f), 7f, 14f); d.lights++;
            }
            if (equipment.name == "RepairBench")
                Prop(d, "ToolBundle", station, equipment.TransformPoint(new Vector3(.3f, .95f, 0)), equipment.eulerAngles.y);
        }

        // Angular wall fins turn smooth room envelopes into distinct silhouettes. Perimeter only,
        // with mesh colliders on the exact visible geometry rather than expanded bounding boxes.
        foreach (var spec in new[] { ("MiningArea_02", 2, 4f), ("DeepCavern", 5, 7f), ("OldMine_WorkArea", 3, 6f), ("OldMine_CollapsedHall", 2, 7f) })
        {
            CaveSpace room = S(spec.Item1); if (room == null) continue;
            Transform group = Child(art, "Wall Silhouettes - " + room.name);
            for (int i = 0; i < spec.Item2; i++)
            {
                if (!PropSpot(d, routes, room, rnd, .73f, .9f, 1.5f, out Vector3 at, 3f)) continue;
                var rock = Place(Loaf + "Wall/Cave_Wall_Rocks_" + "ABCDEFG"[i % 7] + ".prefab", group,
                    at - Vector3.up * .5f, Quaternion.LookRotation(Flat(room.transform.position - at)).eulerAngles.y, spec.Item3 + i % 2);
                if (rock == null) continue;
                foreach (var mf in rock.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null)
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                if (stone != null) foreach (var r in rock.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
            }
        }

        // A layered buttress on the back wall gives the large lower cavern a geological focal point.
        CaveSpace cavern = S("DeepCavern");
        if (cavern != null)
        {
            Transform buttress = Child(art, "Lower Cavern - Layered Rock Buttress");
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = cavern.transform.position + new Vector3(-12 + i * 7, 0, 19);
                at.y = GroundY(d, at);
                if (!RouteClear(routes, at, 2f) || d.cave.Distance(at + Vector3.up * 3, false) > -.5f) continue;
                var fin = Place(Loaf + "Wall/Cave_Pillar_" + "ABC"[i] + ".prefab", buttress,
                    at - Vector3.up * .3f, 165 + i * 12, 8 + i % 2 * 2);
                if (fin == null) continue;
                fin.transform.localScale = Vector3.Scale(fin.transform.localScale, new Vector3(1.3f, 1, 1.5f));
                if (stone != null) foreach (var r in fin.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
                foreach (var f in fin.GetComponentsInChildren<MeshFilter>()) if (f.sharedMesh != null)
                    f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }
        }

        // Mineral floor ridges, wall fins and a broken canopy distinguish the abandoned worksite.
        CaveSpace abandoned = S("OldMine_Abandoned");
        if (Footprint(d, routes, abandoned, rnd, 1.8f, .52f, .78f, out Vector3 collapseAt, out Quaternion collapseRot))
        {
            Transform collapse = Child(art, "No 3 - Fallen Working Canopy");
            Vector3 postAt = collapseAt + collapseRot * new Vector3(-1.3f, 0, .4f); postAt.y = GroundY(d, postAt);
            Piece("Posts/Post_Reinforced_A", collapse, postAt, collapseRot, new Vector3(1.6f, .7f, 1.6f), true);
            Piece("WOodPlatforms/Panel_Wood_C", collapse, collapseAt + Vector3.up * .6f, collapseRot * Quaternion.Euler(20, 0, 18), new Vector3(.7f, .65f, .7f), false);
            Prop(d, "ToolBundle", collapse, collapseAt + collapseRot * new Vector3(.8f, .1f, -.8f), collapseRot.eulerAngles.y);
            var rack = Prop(d, "ToolRack", collapse, collapseAt, collapseRot.eulerAngles.y);
            rack.transform.rotation *= Quaternion.Euler(0, 0, 67);
            GroundArtObject(d, rack);
            var rocks = Piece("Wall/Cave_Rocks_D", collapse, collapseAt + collapseRot * new Vector3(-.8f, .4f, .5f), collapseRot, Vector3.one * 1.3f, false);
            if (rocks != null && stone != null) foreach (var r in rocks.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
        }

        // The shaft keeps its existing drop, collar, guard and cage. A parked repair reel and ladder are
        // equipment at the safe gallery edge; no new bridge is invented across the traversable incline.
        if (PropSpot(d, routes, S("OldShaft_Gallery"), rnd, .65f, .82f, 1.1f, out Vector3 shaftGear))
        {
            var reel = Prop(d, "CableReel", art, shaftGear, 90);
            Vector3 ladderAt = shaftGear + Vector3.right * 1.5f;
            ladderAt.y = GroundY(d, ladderAt);
            if (d.cave.Distance(ladderAt + Vector3.up * 2, false) < -.5f)
                Piece("WOodPlatforms/Ladder_Tall", art, ladderAt, Quaternion.Euler(0, 90, -12), Vector3.one * .75f, false);
        }

        // Cables are high wall-side runs with a break at each support; no colliders or unique mesh assets.
        foreach (string n in new[] { "OldMine_Adit", "OldMine_Drift_1", "OldMine_Drift_2", "OldMine_Ramp", "OldMine_LowerDrift_1" })
        {
            CaveSpace s = S(n); if (s == null || s.End == null) continue;
            Vector3 a = s.transform.position, b = s.End.position, dir = Flat(b - a).normalized, side = Vector3.Cross(Vector3.up, dir);
            Transform group = Child(art, "Cable Run - " + n);
            float length = Vector3.Distance(a, b);
            for (float t = 4; t < length - 5; t += 6)
            {
                Vector3 p = Vector3.Lerp(a, b, t / length), q = Vector3.Lerp(a, b, (t + 5) / length);
                p += side * (s.Size.x * .65f); q += side * (s.Size.x * .65f);
                p.y = GroundY(d, p) + 3.25f; q.y = GroundY(d, q) + 3.25f;
                if (d.cave.Distance(p, false) > -.2f || d.cave.Distance(q, false) > -.2f) continue;
                var cable = GameObject.CreatePrimitive(PrimitiveType.Cube); cable.name = "Iron cable";
                cable.transform.SetParent(group, false); cable.transform.position = (p + q) * .5f;
                cable.transform.rotation = Quaternion.FromToRotation(Vector3.forward, q - p);
                cable.transform.localScale = new Vector3(.045f, .045f, Vector3.Distance(p, q));
                Object.DestroyImmediate(cable.GetComponent<Collider>()); cable.GetComponent<Renderer>().sharedMaterial = d.metal;
                GameObjectUtility.SetStaticEditorFlags(cable, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        // Perimeter story in the final chamber. The 18 m combat reservation and entry throat stay clear.
        Vector3 centre = S("BossArena").transform.position;
        // Recolour the earlier blue cover rocks into the existing Old Mine stone palette.
        if (stone != null) foreach (Transform t in arena.GetComponentsInChildren<Transform>())
            if (t.name == "Arena Edge Cover") foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
        foreach (var site in new[] { new Vector3(-23, 0, -12), new Vector3(20, 0, 17) })
        {
            Vector3 at = centre + site; at.y = GroundY(d, at);
            if (d.cave.Distance(at + Vector3.up * 2f, false) > -.6f) continue;
            Transform wreck = Child(arena, "Perimeter - Abandoned Maintenance " + site.x);
            Prop(d, "CableReel", wreck, at, site.x < 0 ? 15 : 125);
            Prop(d, "ToolBundle", wreck, at + new Vector3(1.2f, .05f, -.8f), 35);
            Piece("WOodPlatforms/Panel_Wood_D", wreck, at + new Vector3(2, .4f, 1), Quaternion.Euler(12, 40, 17), new Vector3(.8f, .65f, .8f), false);
            var pile = Piece("Wall/Cave_Rocks_B", wreck, at + new Vector3(-1.5f, .3f, 1.5f), Quaternion.Euler(0, 20, 0), Vector3.one * 1.5f, false);
            if (pile != null && stone != null) foreach (var r in pile.GetComponentsInChildren<Renderer>()) r.sharedMaterial = stone;
        }

        // The loading gallery was torn off its supports. Reuse modular platforms and beams at the rim;
        // every part stays beyond the combat disk and the entrance lane.
        Transform galleryWreck = Child(arena, "Perimeter - Collapsed Loading Gallery");
        Vector3 galleryAt = centre + new Vector3(-23, 0, 10); galleryAt.y = GroundY(d, galleryAt);
        Quaternion galleryRot = Quaternion.Euler(0, 22, 0);
        for (int i = 0; i < 2; i++)
        {
            Vector3 at = galleryAt + galleryRot * new Vector3(0, 0, i * 4);
            if (d.cave.Distance(at + Vector3.up * 3, false) > -.5f) continue;
            foreach (float x in new[] { -1.8f, 1.8f })
            {
                Vector3 foot = at + galleryRot * new Vector3(x, 0, 0); foot.y = GroundY(d, foot);
                Piece("Posts/Post_Reinforced_A", galleryWreck, foot, galleryRot * Quaternion.Euler(0, 0, x < 0 ? 9 : -18),
                    new Vector3(1.5f, i == 0 ? 1.3f : .55f, 1.5f), true);
            }
            Piece("WOodPlatforms/Wood_Platform_A", galleryWreck, at + Vector3.up * (i == 0 ? 3.8f : .5f),
                galleryRot * Quaternion.Euler(i == 0 ? 12 : 30, 0, i == 0 ? -9 : 17), new Vector3(1, 1, .9f), false);
            Piece("Posts/Beam_A", galleryWreck, at + Vector3.up * .2f + Vector3.back * 2,
                galleryRot * Quaternion.Euler(0, 65, 5), new Vector3(1.5f, 1.5f, 1.5f), false);
        }

        // Retune existing lamps rather than adding a point light to every prop.
        foreach (string n in new[] { "OldMine_Adit", "OldMine_Office", "OldMine_Stope", "OldMine_Drift_1", "OldMine_Drift_2", "MiningArea_02", "DeepCavern", "OldShaft_Gallery", "OldShaft_Landing_1", "OldMine_WorkArea" })
        {
            Transform group = lighting.Find("Cave/" + n); if (group == null) continue;
            bool early = n == "OldMine_Adit" || n == "OldMine_Office" || n == "OldMine_Stope";
            foreach (Light l in group.GetComponentsInChildren<Light>())
            {
                l.intensity = early ? 10f : n == "DeepCavern" ? 4f : n == "OldMine_WorkArea" ? 5f : 6f;
                l.color = early ? new Color(1, .77f, .48f) : new Color(1, .64f, .35f);
                l.shadows = LightShadows.None;
            }
        }
        foreach (Light l in lights.GetComponentsInChildren<Light>()) l.shadows = LightShadows.None;
    }
}
