using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only geometry budget and ore-reservation audit after a full map rebuild.</summary>
public static class CaveGeometryReview
{
    [MenuItem("Ore What/Island Map/Review Cave Geometry")]
    public static void Menu() => Debug.Log(Check());

    public static string Check()
    {
        var island = GameObject.Find("Island");
        if (island == null) return "No Island in the open scene.";
        var geology = island.transform.Find("Props/Cave Geology");
        var ores = island.GetComponentInChildren<OreSpawnSystem>();
        long triangles = 0, shellTriangles = 0, geologyTriangles = 0;
        var shared = new HashSet<Mesh>(); var materials = new HashSet<Material>();
        int formations = 0, colliders = 0, failures = 0;
        var log = new StringBuilder();
        foreach (var filter in island.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            int count = filter.sharedMesh.triangles.Length / 3;
            triangles += count;
            if (filter.transform.IsChildOf(island.transform.Find("Mountain/Generated"))) shellTriangles += count;
            if (geology == null || !filter.transform.IsChildOf(geology)) continue;
            formations++; geologyTriangles += count; shared.Add(filter.sharedMesh);
            var renderer = filter.GetComponent<MeshRenderer>();
            foreach (var material in renderer.sharedMaterials) materials.Add(material);
            var collider = filter.GetComponent<MeshCollider>();
            if (collider != null)
            {
                colliders++;
                if (collider.sharedMesh != filter.sharedMesh) { failures++; log.AppendLine("Collider differs from visible mesh: " + filter.name); }
            }
            if ((filter.name.StartsWith("CeilingCluster") || filter.name.StartsWith("SmallRubble") || filter.name.StartsWith("MediumRubble")) && collider != null)
            { failures++; log.AppendLine("Unnecessary detail collider: " + filter.name); }
            foreach (var socket in ores.sockets)
                if (renderer.bounds.SqrDistance(socket.position + socket.normal * .55f) < 1.2f * 1.2f || renderer.bounds.SqrDistance(socket.approach) < 1f)
                { failures++; log.AppendLine("Ore reservation intersected: " + socket.id + " / " + filter.name); }
        }
        log.AppendLine($"Island static mesh triangle instances: {triangles}; shell: {shellTriangles}; new geology: {geologyTriangles}.");
        log.AppendLine($"Geology: {formations} formations, {shared.Count} shared meshes, {materials.Count} shared material references (5 existing + 10 new), {colliders} visible-mesh colliders.");
        log.AppendLine($"Ore reservation / collider audit: {ores.sockets.Count} sockets; failures={failures}.");
        return log.ToString();
    }
}
