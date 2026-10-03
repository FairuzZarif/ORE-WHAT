using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where players (re)spawn. The spawn points are the map's existing PlayerStart zones (MapZone, Kind = Player Start;
/// the island map has one at the start clearing). Add more PlayerStart zones to get more spawn points; with none,
/// the fallback point (where the scene's Player started) is used.
///
/// A spot is a point inside a zone (its centre first, then rings around it) that has ground under it, room for a
/// standing player and nobody else standing there. Used by PlayerDeath (single player) and by the host
/// (NetworkPlayerHealth) when someone respawns.
/// </summary>
public static class PlayerSpawnPoints
{
    // Ignore Raycast, ViewModel, DroppedItem, Player: never what blocks a spawn (other players are checked by position).
    private const int IgnoredLayers = (1 << 2) | (1 << 6) | (1 << 7) | (1 << 8);

    /// <summary>
    /// A free standing spot (feet position). seed picks which zone / ring position is tried first (e.g. the player's id),
    /// so two players respawning together don't get the same spot; avoid = where other players are now.
    /// </summary>
    public static Vector3 FindSpot(Vector3 fallback, int seed, IList<Vector3> avoid, float radius = 0.4f, float height = 1.8f)
    {
        var zones = new List<MapZone>();
        foreach (MapZone z in Object.FindObjectsByType<MapZone>())
            if (z.Kind == MapZone.ZoneKind.PlayerStart) zones.Add(z);
        zones.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // the same order on every machine

        int count = Mathf.Max(1, zones.Count);
        for (int k = 0; k < count; k++)
        {
            int zi = Mathf.Abs(seed + k) % count;
            Vector3 centre = zones.Count > 0 ? zones[zi].transform.position : fallback;
            float zoneRadius = zones.Count > 0 ? zones[zi].Radius : 4f;
            // The centre, then 6 spots on a ring, then 12 on a ring twice as far.
            for (int i = 0; i < 19; i++)
            {
                int j = (i + Mathf.Abs(seed)) % 19;
                Vector3 offset = j == 0 ? Vector3.zero
                               : j <= 6 ? Quaternion.Euler(0f, j * 60f, 0f) * Vector3.forward * (zoneRadius * 0.45f)
                               : Quaternion.Euler(0f, (j - 7) * 30f + 15f, 0f) * Vector3.forward * (zoneRadius * 0.9f);
                if (TryStandAt(centre + offset, avoid, radius, height, out Vector3 feet)) return feet;
            }
        }
        Debug.LogWarning("[Ore What] No free spawn spot found; using the start point.");
        return zones.Count > 0 ? zones[0].transform.position : fallback;
    }

    private static bool TryStandAt(Vector3 point, IList<Vector3> avoid, float radius, float height, out Vector3 feet)
    {
        feet = point;
        int mask = ~IgnoredLayers;
        if (!Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit ground, 12f, mask, QueryTriggerInteraction.Ignore)) return false;
        feet = ground.point + Vector3.up * 0.05f;
        if (Physics.CheckCapsule(feet + Vector3.up * (radius + 0.05f), feet + Vector3.up * (height - radius), radius, mask, QueryTriggerInteraction.Ignore))
            return false;
        if (avoid != null)
            foreach (Vector3 other in avoid)
            {
                Vector3 d = other - feet; d.y = 0f;
                if (d.sqrMagnitude < 1.2f * 1.2f && Mathf.Abs(other.y - feet.y) < height) return false;
            }
        return true;
    }
}
