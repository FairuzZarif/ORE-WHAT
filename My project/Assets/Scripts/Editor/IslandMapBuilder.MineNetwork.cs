using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The map's structure: the Central Mining Hub (Cavern_01, reached from the mountain entrance through MainTunnel)
/// and FOUR self-contained mines, each with its own entrance in the hub and no way into another mine except back
/// through the hub:
///
///   Hub (0)              Entrance (ravine), MainTunnel, Cavern_01
///   Mine 1 Old Mine      hub SW (timber adit): office + store room, stope, the abandoned No. 3 workings (a loop),
///                        MiningArea_02, the old deep tunnels, DeepCavern, the lower drifts, the Old Shaft (gallery,
///                        three inclines round it, the bottom), the abandoned work area, the dark final approach, and
///                        BossArena (its arena). Loops: Collapsed drift back to the hub's OreArea_01, the No. 3
///                        workings, the old incline (work area <-> first shaft landing); the shaft is a drop shortcut.
///   Mine 2 Deep Mine     hub NE (SideTunnel): SideCave, the rail line, Station 1, Junction B, CombatArea, Shaft
///                        No. 2, the pump station, the Hollow (its arena), the old facility. Loop: the shaft's
///                        return incline up to Junction B.
///   Mine 3 Crystal       hub N (Tunnel_02): Cavern_02, the Crystal Cavern, the upper descent and the lower route
///                        down to the Crystal Geode (its arena) - two ways down = a loop.
///   Mine 4 The Rift      hub SE (the big portal): descents to the Rift Cavern over the chasm, the lower spiral to a
///                        ledge in the Abyss, the Abyss floor ~100 m below the hub, the Final Area (its arena), and
///                        a climb back up under Mine 1 (a loop).
///
/// Every space is a CaveSpace child of Environment/Island/Cave (carved by the island map builder), so rooms and
/// tunnels can still be moved in the Scene view. The layout below is applied ONCE (Apply Hub And Four Mines Layout,
/// or automatically by the first rebuild after this change); after that a rebuild only adds spaces that are missing.
/// Dressing is in IslandMapBuilder.MineDressing.cs.
/// </summary>
public static partial class IslandMapBuilder
{
    public const int Hub = 0, OldMine = 1, DeepMine = 2, CrystalMine = 3, RiftMine = 4;
    public static readonly string[] MineNames = { "Central Mining Hub", "Mine 1 - Old Mine", "Mine 2 - Deep Mine", "Mine 3 - Crystal Caverns", "Mine 4 - The Rift" };

    private struct MineSpace
    {
        public string name;
        public int mine;
        public CaveSpace.Kind kind;
        public Vector3 floor, end;   // x / z are world positions, y is relative to the hub floor (Cavern_01, 17.3 m)
        public Vector3 size;         // rooms: (half width, height, half length); tunnels: (half width, height, -)
        public float yaw, depth, endDepth, wallNoise;
    }

    private static MineSpace R(int mine, string name, float x, float dy, float z, Vector3 size, float yaw, float depth, float noise)
        => new MineSpace { mine = mine, name = name, kind = CaveSpace.Kind.Room, floor = new Vector3(x, dy, z), size = size, yaw = yaw, depth = depth, endDepth = depth, wallNoise = noise };

    private static MineSpace T(int mine, string name, Vector3 a, Vector3 b, float halfWidth, float height, float d0, float d1, float noise)
        => new MineSpace { mine = mine, name = name, kind = CaveSpace.Kind.Tunnel, floor = a, end = b, size = new Vector3(halfWidth, height, 0f), depth = d0, endDepth = d1, wallNoise = noise };

    private static Vector3 V(float x, float dy, float z) => new Vector3(x, dy, z);

    /// <summary>
    /// The generated spaces. Each tunnel starts and ends inside the space it joins, at that space's floor height, and
    /// reaches that height before it crosses the room's wall (no drops at the joins). Depth (0..1) drives the cave's
    /// darkness (CaveAtmosphere) and lamp brightness.
    /// </summary>
    private static readonly MineSpace[] MineSpaces =
    {
        // --- Mine 1: Old Mine (west of the hub; small timbered rooms, warm light).
        T(OldMine, "OldMine_Adit",    V(485, 0, 712),  V(452, 0, 701),  4.5f, 7f,   0.17f, 0.2f,  1.5f),
        R(OldMine, "OldMine_Office",  444, 0, 698,     new Vector3(12f, 9f, 11f),   0f,    0.2f,  1.5f),
        T(OldMine, "OldMine_Drift_1", V(446, 0, 707),  V(437, -2, 736), 4f,   6.5f, 0.2f,  0.23f, 1.5f),
        R(OldMine, "OldMine_Stope",   433, -2, 745,    new Vector3(11f, 9f, 10f),   20f,   0.24f, 2f),
        T(OldMine, "OldMine_Drift_2", V(437, -2, 754), V(448, -4, 777), 4f,   6.5f, 0.25f, 0.28f, 1.5f),
        T(OldMine, "OldMine_Ramp",    V(448, -4, 777), V(440, -10, 806), 4.5f, 7f,  0.28f, 0.34f, 1.5f),
        T(OldMine, "Collapsed_Drift",   V(470, 0, 737),     V(441, -2, 749),  3.5f, 6f,   0.2f,  0.24f, 1.2f),
        T(OldMine, "Collapsed_Crawl",   V(458, -0.8f, 742), V(461.5f, -1, 756), 3.5f, 5f, 0.23f, 0.25f, 0.8f),
        R(OldMine, "Collapsed_Chamber", 462, -1, 761,       new Vector3(8f, 7f, 8f),  0f, 0.26f, 2f),
        // Old camp: a store room through a narrow door off the office.
        T(OldMine, "OldMine_StoreDoor", V(440, 0, 692),     V(432, 0, 681.5f),   2.5f, 4.5f, 0.2f, 0.2f, 0.8f),
        R(OldMine, "OldMine_Storeroom", 430, 0, 678,        new Vector3(7f, 6.5f, 6f), 0f, 0.2f, 1.2f),
        // The abandoned No. 3 workings: a side drift from the stope to an abandoned working room, then down into
        // MiningArea_02 (a loop beside the main Drift_2 / Ramp line).
        T(OldMine, "OldMine_SideDrift",     V(425, -2, 748), V(404, -3, 790),     3.5f, 6f,   0.24f, 0.28f, 1.5f),
        R(OldMine, "OldMine_Abandoned",     402, -3, 798,    new Vector3(12f, 9f, 11f), 10f, 0.3f, 2f),
        T(OldMine, "OldMine_AbandonedLink", V(406, -3, 805), V(425, -10, 819.5f), 4f,   6.5f, 0.3f,  0.35f, 1.5f),
        // The lower levels (BossGate is retired: DeepCavern no longer opens onto the arena). DeepCavern -> the lower
        // drifts -> the Old Shaft gallery (looks down the shaft) -> three inclines around the shaft (turn rooms at the
        // corners) -> the shaft bottom -> the abandoned work area (Dig Site 9). The old incline is the shortcut
        // between the work area and the first landing.
        T(OldMine, "OldMine_LowerDrift_1",  V(377, -34, 804),     V(380, -39, 846),     4f,   7f,   0.53f, 0.55f, 2f),
        T(OldMine, "OldMine_LowerDrift_2",  V(380, -39, 846),     V(410, -45.3f, 866),  4f,   7f,   0.55f, 0.58f, 2f),
        R(OldMine, "OldShaft_Gallery",      419, -45.3f, 866,     new Vector3(11f, 8f, 8f),  0f, 0.58f, 1.5f),
        R(OldMine, "OldShaft",              433, -67.3f, 871,     new Vector3(11f, 32f, 11f), 0f, 0.64f, 1.5f),
        T(OldMine, "OldShaft_Incline_1",    V(415, -45.3f, 859.5f), V(448, -57.3f, 833.5f), 4.5f, 7.5f, 0.58f, 0.6f, 2f),
        R(OldMine, "OldShaft_Landing_1",    452, -57.3f, 830,     new Vector3(7f, 8f, 7f),   0f, 0.6f, 1.5f),
        T(OldMine, "OldShaft_Incline_2",    V(453, -57.3f, 836),  V(465, -61.3f, 885.5f), 4.5f, 7.5f, 0.6f, 0.62f, 2f),
        R(OldMine, "OldShaft_Landing_2",    463, -61.3f, 893,     new Vector3(9f, 8f, 9f),   0f, 0.62f, 1.5f),
        T(OldMine, "OldShaft_Incline_3",    V(455.5f, -61.3f, 894.5f), V(440, -67.3f, 877),  4.5f, 7.5f, 0.62f, 0.64f, 2f),
        T(OldMine, "OldShaft_BottomDrift",  V(432, -67.3f, 862),  V(418, -67.3f, 826.5f), 4f,  7f,   0.64f, 0.66f, 2f),
        R(OldMine, "OldMine_WorkArea",      412, -67.3f, 815,     new Vector3(18f, 14f, 15f), 0f, 0.66f, 2f),
        T(OldMine, "OldMine_OldIncline",    V(426, -67.3f, 810),  V(448, -57.3f, 827),  4f,   7f,   0.66f, 0.6f, 2f),
        T(OldMine, "OldMine_PocketDrift",   V(399, -67.3f, 821),  V(383, -67.3f, 836),  3f,   5.5f, 0.66f, 0.68f, 2f),
        R(OldMine, "OldMine_Pocket",        380, -67.3f, 840,     new Vector3(6f, 6f, 6f),   0f, 0.68f, 2f),
        // The final approach: dark, no lamps, big rock formations; under DeepCavern and the arena, then up into the
        // arena from the south-east (its gate).
        T(OldMine, "OldMine_Approach_1",    V(402, -67.3f, 806),  V(393, -63.3f, 759),  4.5f, 8f,   0.68f, 0.72f, 2.5f),
        R(OldMine, "OldMine_CollapsedHall", 392, -63.3f, 750,     new Vector3(11f, 12f, 11f), 0f, 0.72f, 2.5f),
        T(OldMine, "OldMine_Approach_2",    V(393, -63.3f, 741),  V(400, -53.3f, 656),  5f,   9f,   0.72f, 0.78f, 2.5f),
        R(OldMine, "OldMine_ApproachTurn",  406, -53.3f, 650,     new Vector3(12f, 9f, 9f),  0f, 0.78f, 2.5f),
        T(OldMine, "OldMine_ArenaGate",     V(415, -53.3f, 648),  V(430, -36, 703),     6f,   10f,  0.78f, 0.8f, 2f),

        // --- Mine 2: Deep Mine (north-east; industrial). Entered from the hub through SideTunnel and SideCave.
        R(DeepMine, "RailStation",      575, -1, 708,       new Vector3(14f, 10f, 11f), 0f, 0.3f, 2f),
        T(DeepMine, "RailTunnel_2",     V(583, -1, 714),    V(604, -6, 782),  5f,   8f, 0.3f,  0.38f, 1.5f),
        T(DeepMine, "RailTunnel_Link",  V(578, -3, 762),    V(597, -4.6f, 763), 5.5f, 8f, 0.27f, 0.3f, 1.5f),
        R(DeepMine, "RailJunction",     600, -6, 792,       new Vector3(13f, 10f, 12f), 0f, 0.4f, 2f),
        T(DeepMine, "RailTunnel_3",     V(594, -6, 802),    V(577, -12, 824), 5f,   8f, 0.42f, 0.5f,  1.5f),
        T(DeepMine, "DeepMine_Incline_1", V(560, -12, 816), V(536, -22, 781), 4.5f, 7.5f, 0.55f, 0.62f, 1.5f),
        R(DeepMine, "DeepMine_Shaft",     531, -22, 775,    new Vector3(11f, 22f, 11f), 0f, 0.64f, 2f),
        T(DeepMine, "DeepMine_Return",    V(540, -22, 776), V(591, -6, 788), 4.5f, 7.5f, 0.62f, 0.4f, 1.5f), // shortcut back up to Junction B
        T(DeepMine, "DeepMine_Incline_2", V(526, -22, 768), V(517, -31.3f, 740), 4.5f, 7.5f, 0.66f, 0.7f,  1.5f),
        R(DeepMine, "DeepMine_Landing",   515, -31.3f, 737,  new Vector3(9f, 8f, 9f),    0f, 0.72f, 2f),
        T(DeepMine, "DeepMine_Incline_3", V(508, -31.3f, 744), V(499, -36, 751), 4.5f, 8f,   0.74f, 0.78f, 1.5f),
        R(DeepMine, "DeepHollow",         482, -36, 768,    new Vector3(28f, 22f, 22f), 0f, 0.8f, 2.5f),
        R(DeepMine, "OldFacility",        561, -31, 714,    new Vector3(13f, 11f, 12f), 0f, 0.76f, 1.2f),
        T(DeepMine, "Facility_Link",      V(553, -31, 720), V(522, -31.3f, 736), 3.5f, 6f, 0.76f, 0.72f, 1.2f),

        // --- Mine 3: Crystal Caverns (north). Upper route: the Crystal Cavern and five descents; lower route: five
        //     inclines straight down from Cavern_02. Both end in the Crystal Geode. (Turns between legs stay wide:
        //     a leg doubling back alongside the one above carves under its end and leaves a cliff.)
        T(CrystalMine, "CrystalTunnel_W",   V(504, -10, 829),   V(513, -16, 851), 5f,   8f, 0.38f, 0.45f, 2f),
        R(CrystalMine, "CrystalCavern",     518, -16, 862,      new Vector3(20f, 18f, 16f), 10f, 0.5f, 2.5f),
        T(CrystalMine, "Crystal_Descent_1", V(500, -16, 872),     V(474, -26.3f, 880), 5f, 8f, 0.52f, 0.58f, 2f),
        T(CrystalMine, "Crystal_Descent_2", V(474, -26.3f, 880),  V(474, -37.3f, 852), 5f, 8f, 0.58f, 0.63f, 2f),
        T(CrystalMine, "Crystal_Descent_3", V(474, -37.3f, 852),  V(500, -48.3f, 862), 5f, 8f, 0.63f, 0.68f, 2f),
        T(CrystalMine, "Crystal_Descent_4", V(500, -48.3f, 862),  V(536, -61.3f, 870), 5f, 8f, 0.68f, 0.73f, 2f),
        T(CrystalMine, "Crystal_Descent_5", V(536, -61.3f, 870),  V(530, -62.3f, 842), 5f, 8f, 0.73f, 0.76f, 2f), // nearly flat into the Geode
        T(CrystalMine, "Crystal_Lower_1",   V(505, -10, 800),   V(530, -21.3f, 810), 5f, 8f, 0.4f,  0.48f, 2f),
        T(CrystalMine, "Crystal_Lower_2",   V(530, -21.3f, 810), V(548, -32.3f, 836), 5f, 8f, 0.48f, 0.56f, 2f),
        T(CrystalMine, "Crystal_Lower_3",   V(548, -32.3f, 836), V(570, -43.3f, 812), 5f, 8f, 0.56f, 0.64f, 2f),
        T(CrystalMine, "Crystal_Lower_4",   V(570, -43.3f, 812), V(560, -54.3f, 786), 5f, 8f, 0.64f, 0.7f,  2f),
        T(CrystalMine, "Crystal_Lower_5",   V(560, -54.3f, 786), V(536, -62.3f, 820), 5f, 8f, 0.7f,  0.76f, 2f),
        R(CrystalMine, "Crystal_Geode",     515, -62.3f, 830,   new Vector3(26f, 20f, 22f), 15f, 0.78f, 3f),

        // --- Mine 4: The Rift (south-east portal, then down under everything). Huge, sparse, dark.
        T(RiftMine, "Rift_Portal",       V(519, 0, 710),       V(546, -10, 684),     8f, 15f, 0.25f, 0.42f, 2.5f),
        T(RiftMine, "Rift_Descent_1",    V(546, -10, 684),     V(504, -23, 668),     7f, 13f, 0.42f, 0.55f, 2.5f),
        T(RiftMine, "Rift_Descent_2",    V(504, -23, 668),     V(490, -37.3f, 698),  7f, 13f, 0.55f, 0.65f, 2.5f),
        R(RiftMine, "Rift_Cavern",       484, -37.3f, 710,     new Vector3(26f, 20f, 20f), 350f, 0.7f, 3f),
        R(RiftMine, "Rift_Chasm",        470, -97.3f, 712,     new Vector3(10f, 63f, 26f), 20f, 0.9f, 3f),
        T(RiftMine, "Rift_ChasmLink",    V(476, -97.3f, 730),  V(484, -97.3f, 750),  6f, 10f, 0.91f, 0.92f, 2.5f), // chasm floor -> abyss floor
        R(RiftMine, "Rift_Abyss",        488, -97.3f, 765,     new Vector3(30f, 40f, 26f), 0f, 0.93f, 3f),
        T(RiftMine, "Rift_Lower_1",      V(504, -37.3f, 710),  V(540, -47.3f, 700),  5.5f, 9f, 0.72f, 0.76f, 2f),
        T(RiftMine, "Rift_Lower_2",      V(540, -47.3f, 700),  V(556, -59.3f, 728),  5.5f, 9f, 0.76f, 0.8f,  2f),
        T(RiftMine, "Rift_Lower_3",      V(556, -59.3f, 728),  V(533, -71.3f, 746),  5.5f, 9f, 0.8f,  0.84f, 2f),
        R(RiftMine, "Rift_Ledge",        522, -71.3f, 752,     new Vector3(11f, 9f, 11f), 0f, 0.85f, 2f),
        T(RiftMine, "Rift_Lower_4",      V(530, -71.3f, 758),  V(545, -84.3f, 787),  5.5f, 9f, 0.86f, 0.9f,  2f),
        R(RiftMine, "Rift_Landing",      546, -84.3f, 795,     new Vector3(8f, 9f, 8f), 0f, 0.9f, 2f),
        T(RiftMine, "Rift_Lower_5",      V(539, -84.3f, 797),  V(500, -97.3f, 786),  5.5f, 9f, 0.9f,  0.93f, 2f),
        R(RiftMine, "Rift_SideChamber",  456, -97.3f, 780,     new Vector3(11f, 9f, 10f), 0f, 0.94f, 2.5f),
        T(RiftMine, "Rift_DeepDescent",  V(508, -97.3f, 758),  V(541, -110.3f, 741), 7f, 12f, 0.95f, 0.98f, 2.5f),
        R(RiftMine, "Rift_Final",        568, -110.3f, 722,    new Vector3(36f, 34f, 30f), 0f, 1f, 3f),
        // The old survey climb: a squared spiral (90-degree turns) from the chasm floor back up to the Rift cavern.
        T(RiftMine, "Rift_Climb_1",      V(463, -97.3f, 722),  V(428, -85.3f, 726),  5f, 9f, 0.92f, 0.88f, 2f),
        T(RiftMine, "Rift_Climb_2",      V(428, -85.3f, 726),  V(424, -73.3f, 760),  5f, 9f, 0.88f, 0.84f, 2f),
        T(RiftMine, "Rift_Climb_3",      V(424, -73.3f, 760),  V(455, -61.3f, 764),  5f, 9f, 0.84f, 0.8f,  2f),
        T(RiftMine, "Rift_Climb_4",      V(455, -61.3f, 764),  V(455, -49.3f, 730),  5f, 9f, 0.8f,  0.76f, 2f),
        T(RiftMine, "Rift_Climb_5",      V(455, -49.3f, 730),  V(458, -37.3f, 696),  5f, 9f, 0.76f, 0.72f, 2f),
        T(RiftMine, "Rift_Climb_6",      V(458, -37.3f, 696),  V(474, -37.3f, 704),  5f, 9f, 0.72f, 0.7f,  2f),
    };

    /// <summary>The original cave spaces: which part of the map they belong to now, and their depth (0..1).</summary>
    private static readonly Dictionary<string, (int mine, float depth, float endDepth)> OriginalSpaces =
        new Dictionary<string, (int, float, float)>
    {
        { "Entrance", (Hub, 0f, 0f) }, { "MainTunnel", (Hub, 0f, 0.1f) }, { "Cavern_01", (Hub, 0.12f, 0.12f) },
        { "OreArea_01", (OldMine, 0.17f, 0.17f) }, { "MiningArea_02", (OldMine, 0.35f, 0.35f) },
        { "DeepTunnel_A", (OldMine, 0.38f, 0.42f) }, { "DeepTunnel_B", (OldMine, 0.42f, 0.46f) }, { "DeepTunnel_C", (OldMine, 0.46f, 0.5f) },
        { "DeepCavern", (OldMine, 0.52f, 0.52f) }, { "BossArena", (OldMine, 0.8f, 0.8f) },
        { "SideTunnel", (DeepMine, 0.15f, 0.22f) }, { "SideCave", (DeepMine, 0.25f, 0.25f) }, { "CombatArea", (DeepMine, 0.52f, 0.52f) },
        { "Tunnel_02", (CrystalMine, 0.15f, 0.3f) }, { "Cavern_02", (CrystalMine, 0.35f, 0.35f) },
    };

    /// <summary>
    /// Old spaces that linked two mines together (or the hub-side rail start that the Rift portal replaced), and
    /// BossGate (DeepCavern straight into the arena; the Old Mine's lower levels lead there now).
    /// </summary>
    private static readonly string[] RetiredSpaces = { "CombatTunnel", "CrystalTunnel_E", "DeepPassage", "DeepRift_Descent", "Facility_Hall", "RailTunnel_1", "BossGate" };

    /// <summary>Spaces reused under a new name.</summary>
    private static readonly (string from, string to)[] RenamedSpaces = { ("DeepRift", "Rift_Cavern") };

    /// <summary>Rooms that overlap on purpose (tunnel joins are recognised automatically).</summary>
    private static readonly (string a, string b)[] RoomJoins =
    {
        ("Rift_Chasm", "Rift_Cavern"), ("Rift_Chasm", "Rift_Abyss"), ("Rift_Abyss", "Rift_SideChamber"), ("Rift_Abyss", "Rift_Ledge"),
        ("OreArea_01", "Cavern_01"),
    };

    // Floor shapes of the generated spaces (same meaning as FloorShapes): ledges, pits, rolling floors.
    private static readonly Dictionary<string, (float roll, float steps, float share, float plateau, Vector2 offset, float radius, float ramp)> MineFloorShapes =
        new Dictionary<string, (float, float, float, float, Vector2, float, float)>
    {
        { "OldMine_Stope",   (0.4f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "Collapsed_Chamber", (0.3f, 0f, 0.5f, 0f,  Vector2.zero,           5f,   4f) },
        { "OldMine_Abandoned", (0.4f, 0f, 0.5f, 0f,  Vector2.zero,           5f,   4f) },
        { "OldMine_WorkArea",  (0.3f, 0f, 0.5f, 0f,  Vector2.zero,           5f,   4f) },
        { "OldShaft",          (0.3f, 0f, 0.5f, 0f,  Vector2.zero,           5f,   4f) },
        { "OldMine_CollapsedHall", (0.6f, 0f, 0.5f, 0f, Vector2.zero,        5f,   4f) },
        { "RailStation",     (0.2f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "RailJunction",    (0.2f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "CrystalCavern",   (0.4f, 0f, 0.5f, 3f,    new Vector2(-9f, 5f),   5f,   7f) },   // the crystal ledge (NW)
        { "DeepMine_Shaft",  (0.2f, 0f, 0.5f, -3f,   Vector2.zero,           3.5f, 6f) },   // the old shaft collar (a pit)
        { "DeepHollow",      (0.6f, 0f, 0.5f, 3f,    new Vector2(10f, 10f),  5f,   7f) },   // a ledge in the NE
        { "OldFacility",     (0.1f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "Crystal_Geode",   (0.5f, 0f, 0.5f, 3f,    new Vector2(-12f, 8f),  5f,   7f) },   // the crystal shelf
        // Legs that leave a switchback joint start flat for a few metres (one broad "step" with a long ramp), so their
        // lower floor doesn't cut under the end of the leg above (that left a ledge the player couldn't climb).
        { "Crystal_Lower_3", (0.2f, 11f, 0.85f, 0f,  Vector2.zero,           5f,   4f) },
        { "Crystal_Lower_5", (0.2f, 8f,  0.85f, 0f,  Vector2.zero,           5f,   4f) },
        { "Rift_Cavern",     (0.5f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "Rift_Chasm",      (0.6f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "Rift_Abyss",      (0.8f, 0f, 0.5f, 0f,    Vector2.zero,           5f,   4f) },
        { "Rift_Final",      (0.6f, 0f, 0.5f, 4f,    new Vector2(14f, -10f), 7f,   9f) },   // a raised shelf in the final area
    };

    /// <summary>Which part of the map a space belongs to: 0 = hub, 1..4 = mine, -1 = unknown.</summary>
    public static int MineOf(string spaceName)
    {
        foreach (MineSpace m in MineSpaces) if (m.name == spaceName) return m.mine;
        return OriginalSpaces.TryGetValue(spaceName, out var o) ? o.mine : -1;
    }

    private static Dictionary<string, int> mineCache;

    /// <summary>
    /// Which cave material a wall belongs to, from its nearest space: each mine has its own rock colour so you can
    /// tell where you are (1 = warm hub / Old Mine rock, 3 = Deep Mine grey, 4 = Crystal blue, 5 = Rift black).
    /// </summary>
    private static int CaveSubmesh(CaveSpace nearest, float depth)
    {
        if (mineCache == null)
        {
            mineCache = new Dictionary<string, int>();
            foreach (MineSpace m in MineSpaces) mineCache[m.name] = m.mine;
            foreach (var kv in OriginalSpaces) mineCache[kv.Key] = kv.Value.mine;
        }
        int mine = nearest != null && mineCache.TryGetValue(nearest.name, out int mm) ? mm : -1;
        switch (mine)
        {
            case Hub: case OldMine: return 1;
            case DeepMine: return 3;
            case CrystalMine: return 4;
            case RiftMine: return 5;
            default: return depth >= 0.55f ? 2 : 1;
        }
    }

    /// <summary>True for spaces generated by this layout (false for the original cave spaces).</summary>
    private static bool IsMineSpace(string spaceName)
    {
        foreach (MineSpace m in MineSpaces) if (m.name == spaceName) return true;
        return false;
    }

    private const string LayoutMarker = "_Layout: Hub And Four Mines";

    /// <summary>
    /// Rebuild entry point: the first time, applies the whole layout (retires the cross-links, moves the spaces that
    /// change mine, adds the new ones); after that only adds spaces that are missing, so hand edits stay.
    /// </summary>
    private static string AddMineNetwork(CaveLayout cave)
    {
        if (cave.transform.Find(LayoutMarker) == null) return ApplyMineLayout(cave);
        if (cave.transform.Find(OldMineMarker) == null) return ApplyOldMineExpansion(cave);
        Transform hub = cave.transform.Find("Cavern_01");
        if (hub == null) return "Mine network: no Cavern_01 (the hub) in the cave, nothing added.";
        var added = new List<string>();
        foreach (MineSpace m in MineSpaces)
            if (cave.transform.Find(m.name) == null) { SetSpace(cave.transform, m, hub.position.y); added.Add(m.name); }
        cave.Prepare();
        return added.Count == 0 ? "Mine network: all spaces already there." : $"Mine network: added {added.Count} spaces ({string.Join(", ", added)}).";
    }

    private const string OldMineMarker = "_Layout: Old Mine Expansion";

    /// <summary>
    /// The Old Mine expansion on a cave that already has the hub + four mines layout: retires BossGate, sets the Old
    /// Mine's depths and every Old Mine space in the table (adds the new ones). Applied once by a rebuild (marker),
    /// or again from the menu (which resets the Old Mine's generated spaces to the table).
    /// </summary>
    private static string ApplyOldMineExpansion(CaveLayout cave)
    {
        Transform root = cave.transform, hub = root.Find("Cavern_01");
        if (hub == null) return "Old Mine expansion: no Cavern_01 (the hub) in the cave, nothing changed.";
        Transform gate = root.Find("BossGate");
        if (gate != null) Undo.DestroyObjectImmediate(gate.gameObject);
        foreach (var kv in OriginalSpaces)
        {
            if (kv.Value.mine != OldMine || root.Find(kv.Key) == null) continue;
            var so = new SerializedObject(root.Find(kv.Key).GetComponent<CaveSpace>());
            so.FindProperty("depth").floatValue = kv.Value.depth;
            so.FindProperty("endDepth").floatValue = kv.Value.endDepth;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        int set = 0;
        foreach (MineSpace m in MineSpaces)
            if (m.mine == OldMine) { SetSpace(root, m, hub.position.y); set++; }
        if (root.Find(OldMineMarker) == null) new GameObject(OldMineMarker).transform.SetParent(root, false);
        cave.Prepare();
        return $"Old Mine expansion: {(gate != null ? "BossGate retired, " : "")}{set} Old Mine spaces set.";
    }

    /// <summary>Re-applies the Old Mine part of the layout table to the open scene's cave (no rebuild).</summary>
    [MenuItem("Ore What/Island Map/Apply Old Mine Expansion")]
    public static void ApplyOldMineExpansionMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Ore What] Leave Play mode first."); return; }
        var caveT = GameObject.Find("Island") != null ? GameObject.Find("Island").transform.Find("Cave") : null;
        if (caveT == null) { Debug.LogError("[Ore What] No Environment/Island/Cave in the open scene."); return; }
        Debug.Log("[Ore What] " + ApplyOldMineExpansion(caveT.GetComponent<CaveLayout>()) + " Run Build Or Rebuild Island Map to carve it.");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(caveT.gameObject.scene);
    }

    /// <summary>Applies the whole hub + four mines layout to the cave (spaces in the table are set to their table values).</summary>
    private static string ApplyMineLayout(CaveLayout cave)
    {
        Transform root = cave.transform, hub = root.Find("Cavern_01");
        if (hub == null) return "Mine layout: no Cavern_01 (the hub) in the cave, nothing changed.";
        float hubFloor = hub.position.y;
        var log = new StringBuilder("Mine layout (hub + four mines): ");

        foreach (var (from, to) in RenamedSpaces)
        {
            Transform t = root.Find(from);
            if (t != null && root.Find(to) == null) { t.name = to; log.Append($"{from} -> {to}; "); }
        }
        int retired = 0;
        foreach (string name in RetiredSpaces)
        {
            Transform t = root.Find(name);
            if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); retired++; }
        }
        log.Append($"{retired} cross-links retired; ");

        // Original spaces that change: MiningArea_02 moves off Cavern_02 (Mine 3) into Mine 1, the old deep route
        // now starts from it, SideTunnel (Mine 2's entrance) is widened and the hub enlarged. Depths follow the new structure.
        foreach (var kv in OriginalSpaces)
        {
            CaveSpace s = root.Find(kv.Key) != null ? root.Find(kv.Key).GetComponent<CaveSpace>() : null;
            if (s == null) continue;
            var so = new SerializedObject(s);
            so.FindProperty("depth").floatValue = kv.Value.depth;
            so.FindProperty("endDepth").floatValue = kv.Value.endDepth;
            if (kv.Key == "SideTunnel") so.FindProperty("size").vector3Value = new Vector3(7.5f, 10f, 0f);
            if (kv.Key == "Cavern_01") so.FindProperty("size").vector3Value = new Vector3(30f, 18f, 25f); // the hub: room for the service booths and groups
            so.ApplyModifiedPropertiesWithoutUndo();
            if (kv.Key == "MiningArea_02") s.transform.position = new Vector3(434f, hubFloor - 10f, 812f);
            if (kv.Key == "DeepTunnel_A" && s.End != null)
            {
                Vector3 end = s.End.position;
                s.transform.position = new Vector3(437f, hubFloor - 10f, 820f);
                s.End.position = end;
            }
            EditorUtility.SetDirty(s);
        }

        foreach (MineSpace m in MineSpaces) SetSpace(root, m, hubFloor);
        if (root.Find(LayoutMarker) == null) new GameObject(LayoutMarker).transform.SetParent(root, false);
        if (root.Find(OldMineMarker) == null) new GameObject(OldMineMarker).transform.SetParent(root, false); // the table includes it
        cave.Prepare();
        log.Append($"{MineSpaces.Length} generated spaces set.");
        return log.ToString();
    }

    /// <summary>Creates the space, or sets an existing one to the table's position, size, depth and floor shape.</summary>
    private static void SetSpace(Transform root, MineSpace m, float hubFloor)
    {
        Vector3 a = new Vector3(m.floor.x, hubFloor + m.floor.y, m.floor.z);
        Vector3? b = m.kind == CaveSpace.Kind.Tunnel ? new Vector3(m.end.x, hubFloor + m.end.y, m.end.z) : (Vector3?)null;
        Transform t = root.Find(m.name);
        CaveSpace space = t != null ? t.GetComponent<CaveSpace>() : null;
        if (space == null) space = NewSpace(root, m.name, a, m.kind, m.size, m.depth, m.endDepth, b);
        space.transform.SetPositionAndRotation(a, Quaternion.Euler(0f, m.yaw, 0f));
        var so = new SerializedObject(space);
        so.FindProperty("kind").enumValueIndex = (int)m.kind;
        so.FindProperty("size").vector3Value = m.size;
        so.FindProperty("depth").floatValue = m.depth;
        so.FindProperty("endDepth").floatValue = m.endDepth;
        so.FindProperty("wallNoise").floatValue = m.wallNoise;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (b.HasValue)
        {
            if (space.End == null)
            {
                var e = new GameObject("End").transform;
                e.SetParent(space.transform, false);
                var so2 = new SerializedObject(space);
                so2.FindProperty("end").objectReferenceValue = e;
                so2.ApplyModifiedPropertiesWithoutUndo();
            }
            space.End.position = b.Value;
        }
        if (MineFloorShapes.TryGetValue(m.name, out var f)) space.SetFloorShape(f.roll, f.steps, f.share, f.plateau, f.offset, f.radius, f.ramp);
        else space.SetFloorShape(0.2f, 0f, 0.5f, 0f, Vector2.zero, 5f, 4f);
        EditorUtility.SetDirty(space);
    }

    /// <summary>Applies the hub + four mines layout to the open scene's cave (no rebuild).</summary>
    [MenuItem("Ore What/Island Map/Apply Hub And Four Mines Layout")]
    public static void ApplyMineLayoutMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Ore What] Leave Play mode first."); return; }
        var caveT = GameObject.Find("Island") != null ? GameObject.Find("Island").transform.Find("Cave") : null;
        if (caveT == null) { Debug.LogError("[Ore What] No Environment/Island/Cave in the open scene."); return; }
        Debug.Log("[Ore What] " + ApplyMineLayout(caveT.GetComponent<CaveLayout>()) + " Run Build Or Rebuild Island Map to carve it.");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(caveT.gameObject.scene);
    }

    /// <summary>
    /// Layout check (no changes): connections between different mines (anything but the hub joining them), thin walls
    /// between different mines, and generated spaces with too little rock above them.
    /// </summary>
    [MenuItem("Ore What/Island Map/Check Mine Network")]
    public static void CheckMineNetworkMenu() => Debug.Log("[Ore What] " + CheckMineNetwork());

    public static string CheckMineNetwork()
    {
        var island = GameObject.Find("Island");
        var cave = island != null ? island.transform.Find("Cave")?.GetComponent<CaveLayout>() : null;
        var mountain = island != null ? island.transform.Find("Mountain")?.GetComponent<MountainShape>() : null;
        if (cave == null || mountain == null) return "No island map in the open scene.";
        cave.Prepare();
        var log = new StringBuilder();
        CaveSpace[] spaces = cave.Spaces;
        int problems = 0;

        foreach (CaveSpace s in spaces)
            if (MineOf(s.name) < 0) { problems++; log.AppendLine($"UNASSIGNED: {s.name} belongs to no mine (add it to MineSpaces or OriginalSpaces)."); }

        // 1. Rock cover above every generated space.
        foreach (CaveSpace s in spaces)
        {
            if (!IsMineSpace(s.name)) continue;
            float worst = float.MaxValue; Vector3 at = Vector3.zero;
            Bounds b = s.Bounds;
            for (float x = b.min.x; x <= b.max.x; x += 2f)
                for (float z = b.min.z; z <= b.max.z; z += 2f)
                    for (float y = b.max.y; y >= b.min.y; y -= 1f)
                        if (s.Distance(new Vector3(x, y, z), out _) < 0f)
                        {
                            float cover = mountain.SurfaceHeight(x, z) - y;
                            if (cover < worst) { worst = cover; at = new Vector3(x, y, z); }
                            break; // highest air point of this column
                        }
            if (worst < 8f) { problems++; log.AppendLine($"THIN ROCK: {s.name} has only {worst:F1} m of rock above {at:F0}."); }
        }

        // 2. Which spaces touch (share air). Between different mines that's a leak; a thin wall between them may open.
        var touching = new Dictionary<CaveSpace, List<CaveSpace>>();
        foreach (CaveSpace s in spaces) touching[s] = new List<CaveSpace>();
        for (int i = 0; i < spaces.Length; i++)
            for (int j = i + 1; j < spaces.Length; j++)
            {
                CaveSpace a = spaces[i], c = spaces[j];
                if (a.SpaceKind == CaveSpace.Kind.OpenCut || c.SpaceKind == CaveSpace.Kind.OpenCut || !a.Bounds.Intersects(c.Bounds)) continue;
                int ma = MineOf(a.name), mc = MineOf(c.name);
                bool sameMine = ma == mc || ma == Hub || mc == Hub;
                bool planned = Joined(a, c) || Joined(c, a) || IsRoomJoin(a.name, c.name);
                float closest = Closest(spaces, a, c, out Vector3 at, planned || sameMine);
                if (closest < 0f) { touching[a].Add(c); touching[c].Add(a); }
                if (!sameMine && closest < 2f) { problems++; log.AppendLine($"{(closest < 0f ? "CROSS-MINE OPENING" : "THIN WALL BETWEEN MINES")}: {a.name} ({MineNames[ma]}) / {c.name} ({MineNames[mc]}) - {closest:F1} m at {at:F0}."); }
            }

        // 3. Walking (through shared air) from each mine without passing the hub must never reach another mine.
        for (int mine = OldMine; mine <= RiftMine; mine++)
        {
            var seen = new HashSet<CaveSpace>();
            var queue = new Queue<CaveSpace>();
            foreach (CaveSpace s in spaces) if (MineOf(s.name) == mine) { seen.Add(s); queue.Enqueue(s); }
            var reached = new HashSet<int>();
            while (queue.Count > 0)
            {
                CaveSpace s = queue.Dequeue();
                foreach (CaveSpace n in touching[s])
                {
                    int m = MineOf(n.name);
                    if (m == Hub || seen.Contains(n)) continue;
                    if (m != mine) reached.Add(m);
                    seen.Add(n); queue.Enqueue(n);
                }
            }
            foreach (int m in reached) { problems++; log.AppendLine($"LEAK: {MineNames[mine]} reaches {MineNames[m]} without going through the hub."); }
        }
        log.Insert(0, problems == 0 ? "Map layout OK: four separate mines joined only by the hub; rock cover >= 8 m.\n" : $"{problems} layout problems:\n");
        return log.ToString();
    }

    /// <summary>The smallest "both walls" distance between two spaces (negative = they share air), sampled.</summary>
    private static float Closest(CaveSpace[] spaces, CaveSpace a, CaveSpace c, out Vector3 at, bool airOnly)
    {
        Bounds both = a.Bounds;
        both.SetMinMax(Vector3.Max(a.Bounds.min, c.Bounds.min), Vector3.Min(a.Bounds.max, c.Bounds.max));
        Vector3 size = both.size;
        float step = Mathf.Max(1.5f, Mathf.Pow(Mathf.Max(1f, size.x * size.y * size.z) / 60000f, 1f / 3f));
        float closest = float.MaxValue; at = Vector3.zero;
        for (float x = both.min.x; x <= both.max.x; x += step)
            for (float y = both.min.y; y <= both.max.y; y += step)
                for (float z = both.min.z; z <= both.max.z; z += step)
                {
                    var p = new Vector3(x, y, z);
                    float gap = Mathf.Max(a.Distance(p, out _), c.Distance(p, out _)); // < 0: inside both
                    if (gap >= closest) continue;
                    if (gap >= 0f && airOnly) continue;                    // only "do they touch" matters
                    if (gap >= 0f && InsideOther(spaces, p, a, c)) continue; // a third space's air: no wall there
                    closest = gap; at = p;
                    if (airOnly && gap < 0f) return gap;
                }
        return closest;
    }

    private static bool IsRoomJoin(string a, string b)
    {
        foreach (var (x, y) in RoomJoins) if ((x == a && y == b) || (x == b && y == a)) return true;
        return false;
    }

    /// <summary>True when one of tunnel t's ends opens into space s (a planned join: the end cap overlaps it).</summary>
    private static bool Joined(CaveSpace t, CaveSpace s)
    {
        if (t.SpaceKind != CaveSpace.Kind.Tunnel || t.End == null) return false;
        foreach (Vector3 end in new[] { t.transform.position, t.End.position })
            if (s.Distance(end + Vector3.up * 1.5f, out _) < t.Size.x * 0.5f) return true;
        return false;
    }

    private static bool InsideOther(CaveSpace[] spaces, Vector3 p, CaveSpace a, CaveSpace b)
    {
        foreach (CaveSpace s in spaces)
            if (s != a && s != b && s.SpaceKind != CaveSpace.Kind.OpenCut && s.Bounds.Contains(p) && s.Distance(p, out _) < 0f) return true;
        return false;
    }
}
