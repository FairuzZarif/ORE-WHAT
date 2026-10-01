using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds health + stamina (PlayerAttributes) and their HUD bars (PlayerStatsHUD) to the Player.
/// Safe to run again. Menu: Ore What > Add Health And Stamina (also called by MiningSetupTool).
/// </summary>
public static class PlayerStatsSetupTool
{
    [MenuItem("Ore What/Add Health And Stamina")]
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
        if (player.GetComponent<PlayerAttributes>() == null) Undo.AddComponent<PlayerAttributes>(player);
        if (player.GetComponent<PlayerStatsHUD>() == null) Undo.AddComponent<PlayerStatsHUD>(player);
    }
}
