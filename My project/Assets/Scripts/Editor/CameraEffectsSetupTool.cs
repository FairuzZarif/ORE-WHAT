using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Inserts a CameraRoot between the Player and its camera and adds CameraEffects to it:
///
///   Player
///   └── CameraRoot        CameraEffects   (head bob / jump / landing / sway, eye height)
///       └── PlayerCamera  Camera           (PlayerLook still rotates this for pitch)
///
/// Safe to run more than once. Menu: Ore What > Add Camera Effects
/// </summary>
public static class CameraEffectsSetupTool
{
    [MenuItem("Ore What/Add Camera Effects")]
    public static void AddMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[Ore What] No object tagged 'Player' in the open scene.");
            return;
        }
        AddToPlayer(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    public static CameraEffects AddToPlayer(GameObject player)
    {
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null)
        {
            Debug.LogError("[Ore What] The Player has no child Camera.");
            return null;
        }

        Transform root = cam.transform.parent;
        if (root == player.transform)
        {
            // Move the camera's eye-height offset onto a new parent; the camera keeps its
            // own rotation (PlayerLook's pitch) at zero local offset.
            root = new GameObject("CameraRoot").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Add CameraRoot");
            root.SetParent(player.transform, false);
            root.localPosition = cam.transform.localPosition;
            root.localRotation = Quaternion.identity;
            root.SetSiblingIndex(cam.transform.GetSiblingIndex());

            Undo.SetTransformParent(cam.transform, root, "Parent camera to CameraRoot");
            cam.transform.localPosition = Vector3.zero;
        }

        // Jump / fall / landing state shared by the camera and the first-person arms.
        if (player.GetComponent<PlayerMotionState>() == null) Undo.AddComponent<PlayerMotionState>(player);

        var effects = root.GetComponent<CameraEffects>();
        if (effects == null) effects = Undo.AddComponent<CameraEffects>(root.gameObject);
        Debug.Log($"[Ore What] Camera effects on {root.name}.");
        return effects;
    }
}
