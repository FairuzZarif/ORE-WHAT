using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Reusable suit NPC and generation hook for the existing reserved Company Office.</summary>
public static class CompanyOfficeSetupTool
{
    public const string ModelPath = "Assets/company_worker_npc/CorporateWorker.fbx";
    public const string PrefabPath = "Assets/Prefabs/CompanyWorker.prefab";
    const string ControllerPath = "Assets/Animations/Character/CompanyWorkerIdle.controller";

    [MenuItem("Ore What/Company Office/Set Up Worker And Selling")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play Mode before setup.");
        foreach (string name in new[] { "CopperOre", "IronOre", "GoldOre", "Crystal" })
        {
            var path = AssetDatabase.FindAssets(name + " t:ItemData").Select(AssetDatabase.GUIDToAssetPath)
                .First(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<ItemData>(path));
            so.FindProperty("companyOre").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
        }
        ConfigureAvatar(); BuildPrefab();
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null) ConfigurePlayer(player.gameObject);
        var net = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Network/NetworkPlayer.prefab");
        try { ConfigureNetworkPlayer(net); PrefabUtility.SaveAsPrefabAsset(net, "Assets/Prefabs/Network/NetworkPlayer.prefab"); }
        finally { PrefabUtility.UnloadPrefabContents(net); }
        var booth = GameObject.Find("Booth COMPANY OFFICE");
        if (booth == null) throw new InvalidOperationException("Build the existing island map first; its Company Office booth is required.");
        DressBooth(booth.transform);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(booth.scene); EditorSceneManager.SaveScene(booth.scene);
        Debug.Log("[Ore What] Company Worker and per-player selling account configured.");
    }

    public static void ConfigurePlayer(GameObject player)
    {
        if (player.GetComponent<PlayerCurrency>() == null) player.AddComponent<PlayerCurrency>();
        if (player.GetComponent<CompanyOfficeUI>() == null) player.AddComponent<CompanyOfficeUI>();
    }
    public static void ConfigureNetworkPlayer(GameObject player)
    {
        if (player.GetComponent<NetworkPlayerEconomy>() == null) player.AddComponent<NetworkPlayerEconomy>();
    }
    static void ConfigureAvatar()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        var pairs = new[] {
            ("Hips","pelvis"), ("Spine","spine"), ("Chest","chest"), ("Neck","neck"), ("Head","head"),
            ("LeftShoulder","clavicle.L"), ("LeftUpperArm","upper_arm.L"), ("LeftLowerArm","forearm.L"), ("LeftHand","hand.L"),
            ("RightShoulder","clavicle.R"), ("RightUpperArm","upper_arm.R"), ("RightLowerArm","forearm.R"), ("RightHand","hand.R"),
            ("LeftUpperLeg","thigh.L"), ("LeftLowerLeg","shin.L"), ("LeftFoot","foot.L"), ("LeftToes","toe.L"),
            ("RightUpperLeg","thigh.R"), ("RightLowerLeg","shin.R"), ("RightFoot","foot.R"), ("RightToes","toe.R") };
        HumanDescription desc = importer.humanDescription;
        desc.human = pairs.Select(p => new HumanBone { humanName = p.Item1, boneName = p.Item2, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        desc.skeleton = source.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
        desc.upperArmTwist = desc.lowerArmTwist = desc.upperLegTwist = desc.lowerLegTwist = 0.5f;
        desc.armStretch = desc.legStretch = 0.05f;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription = desc; importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Company worker Humanoid Avatar is invalid.");
    }
    static void BuildPrefab()
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First(a => a.isHuman && a.isValid);
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/CorporateMiner (4)@Breathing Idle.fbx").OfType<AnimationClip>().First(a => !a.name.StartsWith("__"));
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var state = controller.layers[0].stateMachine.AddState("Breathing Idle"); state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
        }
        var root = new GameObject("CompanyWorker");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Suit Visual";
            // The FBX's studio floor also has the CW_ prefix, but is export scenery, not part of the worker.
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name.StartsWith("CW_Studio_")) UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (!(r is SkinnedMeshRenderer) || !r.name.StartsWith("CW_")) UnityEngine.Object.DestroyImmediate(r);
            foreach (var c in model.GetComponentsInChildren<Camera>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
            foreach (var l in model.GetComponentsInChildren<Light>(true)) if (l != null) UnityEngine.Object.DestroyImmediate(l.gameObject);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float scale = 1.78f / bounds.size.y;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = Vector3.up * (-bounds.min.y * scale);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.avatar = avatar; animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var npc = root.AddComponent<CompanyWorkerNPC>();
            var capsule = root.GetComponent<CapsuleCollider>(); capsule.height = 1.78f; capsule.radius = 0.28f; capsule.center = Vector3.up * 0.89f;
            var so = new SerializedObject(npc); var items = so.FindProperty("ores");
            var ores = AssetDatabase.FindAssets("t:ItemData").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemData>)
                .Where(i => i.CompanyOre).OrderBy(i => i.Value).ToArray();
            items.arraySize = ores.Length; for (int i = 0; i < ores.Length; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = ores[i];
            so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static void DressBooth(Transform booth)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return; // setup builds the reusable prefab; future map rebuilds use it
        var counter = booth.Find("Counter");
        if (counter != null) { var p = counter.localPosition; p.z = 0f; counter.localPosition = p; }
        var existing = booth.Find("Company Workstation"); if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        var workstation = new GameObject("Company Workstation").transform; workstation.SetParent(booth, false);
        var worker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, workstation);
        Vector3 foot = booth.TransformPoint(new Vector3(0, 0, 0.95f));
        if (Physics.Raycast(foot + Vector3.up * 2.5f, Vector3.down, out var ground, 5f, 1, QueryTriggerInteraction.Ignore)) foot.y = ground.point.y;
        worker.transform.SetPositionAndRotation(foot, booth.rotation * Quaternion.Euler(0, 180, 0));
        var npc = worker.GetComponent<CompanyWorkerNPC>(); npc.ConfigureOffice(booth); PrefabUtility.RecordPrefabInstancePropertyModifications(npc);
        var white = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/CompanyPaper.mat");
        if (white == null)
        {
            white = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CompanyPaper" };
            white.SetColor("_BaseColor", new Color(0.85f, 0.81f, 0.65f)); AssetDatabase.CreateAsset(white, "Assets/Materials/CompanyPaper.mat");
        }
        for (int i = 0; i < 3; i++)
        {
            var paper = GameObject.CreatePrimitive(PrimitiveType.Cube); paper.name = "Paperwork"; paper.transform.SetParent(workstation, false);
            paper.transform.localPosition = new Vector3(-0.65f, 1.12f + i * 0.006f, -0.04f);
            paper.transform.localScale = new Vector3(0.34f, 0.008f, 0.25f); paper.transform.localRotation = Quaternion.Euler(0, 8 - i * 5, 0);
            paper.GetComponent<Renderer>().sharedMaterial = white; UnityEngine.Object.DestroyImmediate(paper.GetComponent<Collider>());
        }
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null) ConfigurePlayer(player.gameObject);
        Physics.SyncTransforms();
    }
}
