using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class KayKitMiningReview
{
    const string Output = "../Builds/KayKitMiningReview/";

    [MenuItem("Ore What/Mining/Capture KayKit Crack Stages")]
    public static void CaptureStages()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<OreNodeCatalog>(KayKitMiningSetup.CatalogPath);
        Directory.CreateDirectory(Output);
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode; bool oldFog = RenderSettings.fog;
        var previous = RenderTexture.active;
        var root = new GameObject("Temporary Node Art Review");
        var camera = new GameObject("Review Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
        camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .085f, .105f);
        camera.fieldOfView = 38; camera.aspect = 4f / 3f;
        camera.transform.position = new Vector3(0, 1.65f, -2.35f); camera.transform.LookAt(new Vector3(0, .5f, 0));
        var light = new GameObject("Review Light").AddComponent<Light>(); light.transform.SetParent(root.transform);
        light.type = LightType.Directional; light.intensity = 1.3f; light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(40, -35, 0);
        var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var target = RenderTexture.GetTemporary(300, 225, 24); camera.targetTexture = target;
        try
        {
            RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .5f, .5f);
            for (int row = 0; row < catalog.entries.Length; row++)
            {
                var model = Object.Instantiate(catalog.entries[row].visual, root.transform);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                var view = model.GetComponent<OreNodeVisualView>();
                for (int stage = 0; stage < 4; stage++)
                {
                    view.SetHealth(4 - stage, 4); camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 300, 225), stage * 300, (3 - row) * 225);
                }
                Object.DestroyImmediate(model);
            }
            texture.Apply(); File.WriteAllBytes(Output + "resource-crack-stages.png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture); Object.DestroyImmediate(root);
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode; RenderSettings.fog = oldFog;
        }
        Debug.Log("[Ore What] Saved resource-crack-stages.png: Copper / Iron / Gold / Crystal rows; intact / light / medium / heavy columns.");
    }

    public static string CheckNodes()
    {
        Physics.SyncTransforms(); int nodes = 0, missing = 0, rayFailures = 0, meshColliders = 0, oldParts = 0;
        foreach (var rock in Object.FindObjectsByType<RockHealth>())
        {
            nodes++;
            if (rock.GetComponent<OreNodeVisual>() == null || rock.transform.Find("Visual") == null) missing++;
            meshColliders += rock.GetComponentsInChildren<MeshCollider>().Length;
            if (rock.transform.Find("Main") != null || rock.transform.Find("Lump_A") != null) oldParts++;
            // All-around casts isolate this node's simple gameplay colliders from cave walls and neighbours.
            var colliders = rock.GetComponents<Collider>();
            for (int direction = 0; direction < 16; direction++)
            {
                Vector3 offset = Quaternion.Euler(0, direction * 22.5f, 0) * new Vector3(0, .65f, -2);
                Vector3 point = rock.transform.TransformPoint(new Vector3(0, .53f, 0));
                Vector3 start = rock.transform.TransformPoint(offset);
                var ray = new Ray(start, (point - start).normalized);
                if (!colliders.Any(c => c.Raycast(ray, out _, 4))) rayFailures++;
            }
        }
        return $"nodes={nodes} missingVisuals={missing} oldPlaceholderParts={oldParts} meshColliders={meshColliders} failedMiningApproachRays={rayFailures}/{nodes * 16}";
    }
}
