using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds the Pistol and Assault Rifle from the Flat Guns West pack as usable weapons: each view gets a
/// WeaponController (fire / R reload, infinite ammo) wired to the gun's rig bones (slide/bolt, magazine,
/// charging handle) and a muzzle flash on the rig's Attach_Muzzle bone. Rerunning resets the controller
/// settings to the values in the Spec below (tune them there, or on the view afterwards).
/// Safe to run again (the views are rebuilt; the items, prefabs and scene pickups are kept if they exist).
/// Menu: Ore What > Add Weapons
///
/// For each weapon:
///   Assets/Prefabs/Weapons/*Model.prefab   the rigged FBX, turned so the muzzle points +Z, pivot on the
///                                          pistol grip (the right hand's grip point); the rig is kept as is
///   Assets/Items/*.asset                   ItemData: Weapon, not stackable, can't mine
///   Assets/Prefabs/Items/*.prefab          world object: Rigidbody + collider + DroppedItem (E / G / Q work)
///   PlayerCamera/*ViewModel                its first-person view: a copy of FirstPersonViewModel (the same
///                                          arms, IK, idle/run bob) with the pickaxe removed, holding the gun
///   Scene "* (pickup)"                     TEST ITEMS: lying near the player's start, to pick up with E.
///                                          Delete these scene objects to remove them.
/// </summary>
public static class WeaponSetupTool
{
    private class Spec
    {
        public string key, displayName, itemId, description;
        public string fbxPath;
        public int value; public float mass;
        // Model space after the 180° turn (muzzle +Z), relative to the FBX origin.
        public Vector3 gripCentre;      // centre of the pistol grip = the right hand's grip point
        public Vector3 gripAxis;        // along the pistol grip, toward the top of the gun
        public bool supportHand;        // left hand on the handguard (rifle) or lowered out of view (pistol)
        public Vector3 supportPoint;    // model space, relative to the grip point
        public Vector3 supportAxis;
        // Where the grip sits in camera space, and the gun's turn (degrees).
        public Vector3 holdPosition, holdEuler;
        // Shooting (WeaponController defaults; tune them on the view's Weapon Controller afterwards).
        public bool automatic;
        public float fireCooldown, reloadDuration, range, damage;
        public Vector3 recoilKick; public Vector2 recoilRotation; public float cameraKick, maxRecoilStack;
        public string slideBone, chargingBone; public float slideTravel, chargingTravel;
        // Magazine: capacity, how far it slides straight out of the well, its length below the bone,
        // and where the hand carries it (camera space) during a reload.
        public int magazineCapacity; public float magazineExtract, magazineLength; public Vector3 magazineStow;
        // Reload pose: raised toward the centre and rolled so the magazine well faces into the view.
        public Vector3 reloadOffset, reloadTilt;
        public float flashSize;
        public string ModelPath => $"Assets/Prefabs/Weapons/{key}Model.prefab";
        public string ItemPath => $"Assets/Items/{key}.asset";
        public string WorldPath => $"Assets/Prefabs/Items/{key}.prefab";
        public string ViewName => $"{key}ViewModel";
        public string PickupName => $"{displayName} (pickup)";
    }

    private const string PackFolder = "Assets/flat_guns_west/Flat Guns West/FBX/";

    // Grip/handguard points were measured from the meshes (Magazine / Trigger bones and the mesh outline).
    private static readonly Spec[] Weapons =
    {
        new Spec
        {
            key = "Pistol", displayName = "Pistol", itemId = "weapon_pistol",
            description = "A sidearm. Not functional yet.",
            fbxPath = PackFolder + "Pistol_Full_West.Rig.fbx", value = 150, mass = 1f,
            gripCentre = new Vector3(0f, -0.055f, -0.05f), gripAxis = new Vector3(0f, 0.98f, 0.17f),
            supportHand = false,
            holdPosition = new Vector3(0.14f, -0.15f, 0.42f), holdEuler = new Vector3(0f, -4f, 0f),
            automatic = false, fireCooldown = 0.15f, reloadDuration = 1.8f, range = 60f, damage = 20f,
            magazineCapacity = 12, magazineExtract = 0.11f, magazineLength = 0.11f, magazineStow = new Vector3(-0.08f, -0.25f, -0.05f),
            reloadOffset = new Vector3(-0.06f, 0.07f, 0.03f), reloadTilt = new Vector3(-12f, -12f, -35f),
            recoilKick = new Vector3(0.004f, 0.012f, 0.04f), recoilRotation = new Vector2(7f, 1.5f), cameraKick = 0.7f, maxRecoilStack = 1.5f,
            slideBone = "Slide", slideTravel = 0.028f, chargingBone = null, chargingTravel = 0f,
            flashSize = 0.09f,
        },
        new Spec
        {
            key = "AssaultRifle", displayName = "Assault Rifle", itemId = "weapon_assault_rifle",
            description = "An assault rifle. Not functional yet.",
            fbxPath = PackFolder + "Rifle_Assault_West.Rig.fbx", value = 300, mass = 3.5f,
            gripCentre = new Vector3(0f, -0.085f, -0.12f), gripAxis = new Vector3(0f, 0.95f, 0.31f),
            supportHand = true,
            // Left hand on the magazine well, just ahead of the trigger: the arm can reach it comfortably.
            supportPoint = new Vector3(0f, 0.07f, 0.17f), supportAxis = Vector3.forward,
            holdPosition = new Vector3(0.14f, -0.23f, 0.31f), holdEuler = new Vector3(0f, -6f, 0f),
            automatic = true, fireCooldown = 0.1f, reloadDuration = 2.4f, range = 120f, damage = 25f,
            magazineCapacity = 30, magazineExtract = 0.07f, magazineLength = 0.17f, magazineStow = new Vector3(-0.08f, -0.25f, -0.06f),
            reloadOffset = new Vector3(-0.08f, 0.11f, 0f), reloadTilt = new Vector3(-10f, -10f, -28f),
            recoilKick = new Vector3(0.003f, 0.006f, 0.02f), recoilRotation = new Vector2(2.5f, 1f), cameraKick = 0.35f, maxRecoilStack = 3f,
            slideBone = "Bolt", slideTravel = 0.03f, chargingBone = "Charging Handle", chargingTravel = 0.06f,
            flashSize = 0.13f,
        },
    };

    // A pistol is held one-handed for now: the left hand rests low, below the bottom of the screen.
    private static readonly Vector3 LoweredLeftHand = new Vector3(-0.18f, -0.52f, 0.18f);

    [MenuItem("Ore What/Add Weapons")]
    public static void AddWeaponsMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        if (AddWeapons(player))
        {
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log("[Ore What] Weapons added. Pick them up with E, then select their hotbar slot to hold them.");
        }
    }

    public static bool AddWeapons(GameObject player)
    {
        ItemSetupTool.EnsureAssets(); // layers + physics material
        Camera cam = player.GetComponentInChildren<Camera>(true);
        Transform pickaxeView = cam != null ? cam.transform.Find("FirstPersonViewModel") : null;
        if (pickaxeView == null)
        {
            Debug.LogError("[Ore What] No FirstPersonViewModel under the camera. Run Ore What > Rebuild First-Person Viewmodel first.");
            return false;
        }
        foreach (Spec spec in Weapons)
        {
            GameObject model = GetOrCreateModel(spec);
            if (model == null) return false;
            ItemData item = GetOrCreateItem(spec, model);
            GameObject view = BuildView(spec, cam.transform, pickaxeView, model, out Transform heldMesh);
            ItemSetupTool.LinkView(player, item, view, heldMesh);
            PlacePickup(player, spec, item);
        }
        HotbarSetupTool.GenerateIcons(overwrite: false); // icons for the new items only
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>The FBX (rig untouched) inside a root whose pivot is the pistol grip and whose +Z is the muzzle.</summary>
    private static GameObject GetOrCreateModel(Spec spec)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
        if (existing != null) return existing;
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbxPath);
        if (fbx == null) { Debug.LogError($"[Ore What] {spec.fbxPath} not found."); return null; }

        var root = new GameObject(spec.key + "Model");
        var gun = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
        gun.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // the pack's muzzles point -Z
        gun.transform.localPosition = -spec.gripCentre;
        ItemSetupTool.EnsureFolder("Assets/Prefabs", "Weapons");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, spec.ModelPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static ItemData GetOrCreateItem(Spec spec, GameObject model)
    {
        var data = AssetDatabase.LoadAssetAtPath<ItemData>(spec.ItemPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, spec.ItemPath);
            var so = new SerializedObject(data);
            so.FindProperty("category").enumValueIndex = (int)ItemData.ItemCategory.Weapon;
            so.FindProperty("canMine").boolValue = false;
            so.FindProperty("itemId").stringValue = spec.itemId;
            so.FindProperty("displayName").stringValue = spec.displayName;
            so.FindProperty("description").stringValue = spec.description;
            so.FindProperty("value").intValue = spec.value;
            so.FindProperty("stackable").boolValue = false;
            so.FindProperty("maxStackSize").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var world = AssetDatabase.LoadAssetAtPath<GameObject>(spec.WorldPath);
        if (world == null)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            root.name = spec.key;
            // One box around the whole gun (skinned meshes don't size colliders by themselves).
            var bounds = new Bounds(); bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = root.transform.InverseTransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; } else bounds.Encapsulate(c);
                }
            }
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center; box.size = bounds.size;
            box.sharedMaterial = ItemSetupTool.GetOrCreatePhysicsMaterial();
            var item = root.AddComponent<DroppedItem>();
            var body = root.GetComponent<Rigidbody>();
            body.mass = spec.mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var iso = new SerializedObject(item);
            iso.FindProperty("item").objectReferenceValue = data;
            iso.ApplyModifiedPropertiesWithoutUndo();
            int layer = LayerMask.NameToLayer(ItemDrops.LayerName);
            if (layer >= 0)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            ItemSetupTool.EnsureFolder("Assets/Prefabs", "Items");
            world = PrefabUtility.SaveAsPrefabAsset(root, spec.WorldPath);
            Object.DestroyImmediate(root);
        }

        var dso = new SerializedObject(data);
        if (dso.FindProperty("worldPrefab").objectReferenceValue == null)
        {
            dso.FindProperty("worldPrefab").objectReferenceValue = world;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(data);
        return data;
    }

    /// <summary>The pickaxe view's arms and rig, pickaxe removed, holding the gun.</summary>
    private static GameObject BuildView(Spec spec, Transform cam, Transform pickaxeView, GameObject model, out Transform heldMesh)
    {
        Transform old = cam.Find(spec.ViewName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        GameObject view = Object.Instantiate(pickaxeView.gameObject, cam);
        view.name = spec.ViewName;
        view.transform.localPosition = pickaxeView.localPosition;
        view.transform.localRotation = pickaxeView.localRotation;
        view.SetActive(true);
        Undo.RegisterCreatedObjectUndo(view, "Add " + spec.displayName + " View");

        PickaxeSwing swing = view.GetComponentInChildren<PickaxeSwing>(true);
        Transform right = swing.transform.Find("RightHandGrip");
        Transform left = swing.transform.Find("LeftHandGrip");

        // The gun, placed in camera space; pivot = the right hand's grip point.
        var holder = new GameObject("WeaponHolder").transform;
        holder.SetParent(view.transform, false);
        holder.SetPositionAndRotation(cam.TransformPoint(spec.holdPosition), cam.rotation * Quaternion.Euler(spec.holdEuler));
        holder.gameObject.layer = view.layer;

        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
        mesh.name = spec.key + "Mesh";
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localRotation = Quaternion.identity;
        foreach (Transform t in mesh.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = view.layer;
            if (t.TryGetComponent(out Renderer r)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        // Grip points: up (green) = the bar the fist closes around.
        right.SetParent(holder, false);
        right.localPosition = Vector3.zero;
        right.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.right, spec.gripAxis).normalized, spec.gripAxis);
        left.SetParent(holder, false);
        if (spec.supportHand)
        {
            left.localPosition = spec.supportPoint;
            left.localRotation = Quaternion.LookRotation(Vector3.down, spec.supportAxis);
        }
        else
        {
            left.position = cam.TransformPoint(LoweredLeftHand);
            left.rotation = cam.rotation;
        }

        // Drop the pickaxe (mesh, swing, mount) and its mining controller.
        Object.DestroyImmediate(swing.transform.parent.gameObject);
        var miningTool = view.GetComponent<MiningToolController>();
        if (miningTool != null) Object.DestroyImmediate(miningTool);
        var motion = view.GetComponent<ViewModelMotion>();
        if (motion != null)
        {
            var mso = new SerializedObject(motion);
            mso.FindProperty("pickaxeSwing").objectReferenceValue = null; // no swing on this view
            mso.ApplyModifiedPropertiesWithoutUndo();
        }

        // Re-grip: right hand wraps the pistol grip from behind, thumb curled round it;
        // left hand clamps the magazine well from its left side (rifle) or hangs low (pistol).
        // (From-the-left was the only left-hand option without a ~125° wrist twist.)
        var ik = view.GetComponentInChildren<FirstPersonArmsIK>(true);
        var iso = new SerializedObject(ik);
        SetArm(iso.FindProperty("rightArm"), new Vector3(0.3f, 0f, -1f), 1f, -1f, 0f, 45f);
        if (spec.supportHand) SetArm(iso.FindProperty("leftArm"), new Vector3(-1f, 0f, 0f), 1f, -1f, 0.5f, 20f);
        else SetArm(iso.FindProperty("leftArm"), Vector3.zero, 0f, -1f, 0f, 30f);
        iso.ApplyModifiedPropertiesWithoutUndo();
        ik.CalibrateGrips();

        AddWeaponController(spec, view, cam.GetComponent<Camera>(), holder, mesh.transform);
        view.SetActive(false); // PlayerEquipment shows it while this weapon is selected
        heldMesh = mesh.transform;
        return view;
    }

    /// <summary>Shooting/reloading on the view, wired to the gun's rig bones and a muzzle flash.</summary>
    private static void AddWeaponController(Spec spec, GameObject view, Camera camera, Transform holder, Transform mesh)
    {
        Transform muzzle = FindDeep(mesh, "Attach_Muzzle");
        ParticleSystem flash = null; Light flashLight = null;
        if (muzzle != null) BuildMuzzleFlash(mesh, muzzle.position, spec.flashSize, view.layer, out flash, out flashLight);

        int viewModel = LayerMask.NameToLayer("ViewModel"), playerLayer = LayerMask.NameToLayer("Player");
        int mask = ~0;
        if (viewModel >= 0) mask &= ~(1 << viewModel);
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        var wc = view.AddComponent<WeaponController>();
        var so = new SerializedObject(wc);
        so.FindProperty("automatic").boolValue = spec.automatic;
        so.FindProperty("fireCooldown").floatValue = spec.fireCooldown;
        so.FindProperty("range").floatValue = spec.range;
        so.FindProperty("damage").floatValue = spec.damage;
        so.FindProperty("hitLayers").intValue = mask;
        so.FindProperty("reloadDuration").floatValue = spec.reloadDuration;
        so.FindProperty("recoilKick").vector3Value = spec.recoilKick;
        so.FindProperty("recoilRotation").vector2Value = spec.recoilRotation;
        so.FindProperty("cameraKick").floatValue = spec.cameraKick;
        so.FindProperty("maxRecoilStack").floatValue = spec.maxRecoilStack;
        so.FindProperty("slide").objectReferenceValue = string.IsNullOrEmpty(spec.slideBone) ? null : FindDeep(mesh, spec.slideBone);
        so.FindProperty("slideTravel").floatValue = spec.slideTravel;
        so.FindProperty("magazine").objectReferenceValue = FindDeep(mesh, "Magazine");
        so.FindProperty("reloadOffset").vector3Value = spec.reloadOffset;
        so.FindProperty("reloadTilt").vector3Value = spec.reloadTilt;
        so.FindProperty("magazineCapacity").intValue = spec.magazineCapacity;
        so.FindProperty("magazineExtract").floatValue = spec.magazineExtract;
        so.FindProperty("magazineLength").floatValue = spec.magazineLength;
        so.FindProperty("magazineStowOffset").vector3Value = spec.magazineStow;
        so.FindProperty("supportHandGrip").objectReferenceValue = holder.Find("LeftHandGrip");
        so.FindProperty("chargingHandle").objectReferenceValue = string.IsNullOrEmpty(spec.chargingBone) ? null : FindDeep(mesh, spec.chargingBone);
        so.FindProperty("chargingTravel").floatValue = spec.chargingTravel;
        so.FindProperty("muzzleFlash").objectReferenceValue = flash;
        so.FindProperty("muzzleLight").objectReferenceValue = flashLight;
        so.FindProperty("impactMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Prototype/RockChips.mat");
        so.FindProperty("playerCamera").objectReferenceValue = camera;
        so.FindProperty("weaponRoot").objectReferenceValue = holder;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A few short-lived glowing sprites + a quick light, at the muzzle, on the ViewModel layer.</summary>
    private static void BuildMuzzleFlash(Transform mesh, Vector3 muzzlePosition, float size, int layer, out ParticleSystem ps, out Light light)
    {
        var go = new GameObject("MuzzleFlash");
        go.layer = layer;
        go.transform.SetParent(mesh, false);
        go.transform.position = muzzlePosition;
        go.transform.localRotation = Quaternion.identity;
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = 0.045f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // stays on the muzzle while the gun recoils
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = 10;
        var emission = ps.emission; emission.enabled = false; // WeaponController emits per shot
        var shape = ps.shape; shape.enabled = false;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;
        var grow = ps.sizeOverLifetime; grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.2f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = GetOrCreateFlashMaterial();

        var lightGO = new GameObject("MuzzleLight");
        lightGO.transform.SetParent(go.transform, false);
        light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.35f);
        light.range = 5f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        light.enabled = false;
    }

    private const string FlashTexturePath = "Assets/Materials/Weapons/MuzzleFlash.png";
    private const string FlashMaterialPath = "Assets/Materials/Weapons/MuzzleFlash.mat";

    /// <summary>Additive URP particle material with a generated star-burst texture.</summary>
    private static Material GetOrCreateFlashMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);
        if (mat != null) return mat;
        ItemSetupTool.EnsureFolder("Assets/Materials", "Weapons");

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) / size * 2f - Vector2.one;
                float r = p.magnitude, angle = Mathf.Atan2(p.y, p.x);
                float core = Mathf.Exp(-r * r * 9f);
                float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 3f)), 10f) * Mathf.Clamp01(1f - r) * 0.8f;
                float a = Mathf.Clamp01(core + spikes);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        System.IO.File.WriteAllBytes(FlashTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(FlashTexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(FlashTexturePath);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "MuzzleFlash" };
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FlashTexturePath));
        mat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.42f, 1f));
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 2f);     // additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, FlashMaterialPath);
        return mat;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static void SetArm(SerializedProperty arm, Vector3 handSide, float sideWeight, float minPalmUp, float thumbAlong, float thumbCurl)
    {
        arm.FindPropertyRelative("preferredHandSide").vector3Value = handSide;
        arm.FindPropertyRelative("handSideWeight").floatValue = sideWeight;
        arm.FindPropertyRelative("palmTowardViewer").floatValue = 0f;
        arm.FindPropertyRelative("minPalmUp").floatValue = minPalmUp;
        arm.FindPropertyRelative("thumbAlongShaft").floatValue = thumbAlong;
        arm.FindPropertyRelative("thumbCurlOverride").floatValue = thumbCurl;
    }

    /// <summary>TEST ITEM: the weapon lying near the player's start (delete the scene object to remove it).</summary>
    private static void PlacePickup(GameObject player, Spec spec, ItemData item)
    {
        if (GameObject.Find(spec.PickupName) != null || item.WorldPrefab == null) return;
        Vector3 forward = player.transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 spot = player.transform.position + forward * (spec.supportHand ? 3.2f : 2.5f) + right * (spec.supportHand ? 0f : -0.8f);
        float y = player.transform.position.y;
        if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, 1, QueryTriggerInteraction.Ignore))
            y = hit.point.y;
        var pickup = (GameObject)PrefabUtility.InstantiatePrefab(item.WorldPrefab);
        pickup.name = spec.PickupName;
        pickup.transform.SetPositionAndRotation(new Vector3(spot.x, y + 0.1f, spot.z),
                                                Quaternion.LookRotation(right) * Quaternion.Euler(0f, 0f, 90f));
        SceneManagerMove(pickup, player);
        Undo.RegisterCreatedObjectUndo(pickup, "Place " + spec.displayName);
    }

    private static void SceneManagerMove(GameObject go, GameObject player)
    {
        if (go.scene != player.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, player.scene);
    }
}
