using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds crouching: PlayerCrouch on the Player, CharacterCrouchPose on the character body's Animator
/// object, and IK Pass on the Animator controller's Base Layer (the pose needs it).
/// Safe to run again. Menu: Ore What > Add Crouch (also called by MiningSetupTool and CharacterSetupTool).
/// </summary>
public static class CrouchSetupTool
{
    [MenuItem("Ore What/Add Crouch")]
    public static void Menu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        Add(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    public static void Add(GameObject player)
    {
        if (player.GetComponent<PlayerCrouch>() == null) Undo.AddComponent<PlayerCrouch>(player);

        var body = player.GetComponentInChildren<CharacterAnimator>(true);
        if (body == null) return; // no character body yet: crouching still works, just no pose
        if (body.GetComponent<CharacterCrouchPose>() == null) Undo.AddComponent<CharacterCrouchPose>(body.gameObject);

        var controller = body.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
        if (controller == null) return;
        AnimatorControllerLayer[] layers = controller.layers; // a copy: change it and assign it back
        if (layers.Length == 0 || layers[0].iKPass) return;
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }
}
