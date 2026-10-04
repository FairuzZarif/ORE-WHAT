using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the rigged Mixamo character under Player/CharacterVisual with an Animator driven by
/// CharacterAnimator. Safe to run again (replaces the model under CharacterVisual).
/// Menu: Ore What > Add Character Body
///
/// - Imports the FBX as Humanoid (Avatar created from the model), clips loop, no root motion.
/// - Builds Assets/Animations/Character/CorporateMiner.controller with the states
///   Idle, Walk, Run, Jump, Fall, Land. Only clips that exist are assigned; empty states hold
///   the Humanoid default pose until a clip is dropped into them in the Animator window.
/// - Head parts render shadows only (the first-person camera sits inside the head).
/// </summary>
public static class CharacterSetupTool
{
    private const string ModelPath = "Assets/CorporateMiner (4)@Unarmed Run Forward.fbx";
    private const string ControllerFolder = "Assets/Animations/Character";
    private const string ControllerPath = ControllerFolder + "/CorporateMiner.controller";
    private const float ModelScale = 0.9f; // 1.98 m model -> eyes at the 1.6 m camera
    private const float LandTouchdownFrame = 16f; // Falling To Landing: first frame with the feet on the ground (60 fps)
    // Land exits (normalized time of the trimmed clip): the crouch bottoms out ~25%, standing by ~65%.
    private const float LandMovingExit = 0.3f;
    private const float LandIdleExit = 0.65f;
    // Idle ⇄ moving thresholds (m/s, the Speed parameter). Walk is 4 m/s, so both are passed within ~2-6 frames.
    // Stop must stay below Start, or accelerating through the gap would flicker Idle/Walk.
    private const float StartSpeed = 1.8f;
    private const float StopSpeed = 1.5f;

    private static readonly string[] HeadParts = { "Head_and_Neck", "Eye_L", "Eye_R", "Hardhat", "Headlamp" };

    [MenuItem("Ore What/Add Character Body")]
    public static void Add()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }

        ConfigureImport(ModelPath, null, true);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
        if (avatar == null || !avatar.isValid || !avatar.isHuman) { Debug.LogError("[Ore What] The character's Humanoid Avatar is not valid."); return; }

        AnimatorController controller = SetUpAnimations(avatar);

        Transform visual = player.transform.Find("CharacterVisual");
        if (visual == null)
        {
            visual = new GameObject("CharacterVisual").transform;
            visual.SetParent(player.transform, false);
            visual.localPosition = new Vector3(0f, -0.05f, 0f); // CharacterController skin width
            visual.gameObject.layer = 8;
        }
        for (int i = visual.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(visual.GetChild(i).gameObject);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, visual);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * ModelScale;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = visual.gameObject.layer;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = System.Array.IndexOf(HeadParts, r.name) >= 0 ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // keep shadows animating
        model.AddComponent<CharacterAnimator>();
        Undo.RegisterCreatedObjectUndo(model, "Add Character Body");
        FirstPersonCharacterArmsTool.Apply(player); // its arms are the first-person arms
        CrouchSetupTool.Add(player);                // crouch pose on the new body
        HeadlampSetupTool.Add(player);              // headlamp follows the new body's head
        CombatSetupTool.Add(player);                // the new body ragdolls when the player dies

        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        Debug.Log("[Ore What] Character body added.");
    }

    /// <summary>
    /// Re-imports the animation clips and adds anything missing to the existing controller
    /// (Walk Backward, playback-speed parameters) without rebuilding it or touching the scene.
    /// </summary>
    [MenuItem("Ore What/Update Character Animations")]
    public static void UpdateAnimations()
    {
        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
        if (avatar == null || !avatar.isHuman) { Debug.LogError("[Ore What] Run 'Add Character Body' first."); return; }
        SetUpAnimations(avatar);
        Debug.Log("[Ore What] Character animations updated.");
    }

    private class Clips { public AnimationClip Idle, Walk, WalkBack, Run, Jump, Fall, Land, StrafeLeft, StrafeRight; }

    /// <summary>Imports all clips, then builds the controller (only if it doesn't exist yet) and upgrades it.</summary>
    private static AnimatorController SetUpAnimations(Avatar avatar)
    {
        // Mixamo "Without Skin" clips: Humanoid, retargeted through the character's avatar.
        // Jump/Fall bake their height "based upon feet" (one constant offset that puts the FIRST
        // frame's feet on the root), so the tucked-leg fall pose hangs from the player's feet.
        // Land must NOT use feet: its first 16 frames (of 64 at 60 fps) are still falling, feet 0.99 m
        // up, so the feet offset pushed the whole landing ~1 m into the ground (then it popped back
        // up on Land -> Idle). Instead it keeps its Original height and starts at touchdown (frame 16);
        // the Fall state already covers the airborne part.
        var clips = new Clips
        {
            Run = LoadClip(ModelPath),
            Idle = ImportClip("Breathing Idle", avatar, true, false),
            Walk = ImportClip("Walking", avatar, true, false),
            WalkBack = ImportClip("Walking Backward", avatar, true, false),
            Jump = ImportClip("Jumping Up", avatar, false, true),
            Fall = ImportClip("Falling Idle", avatar, true, true),
            Land = ImportClip("Falling To Landing", avatar, false, false, LandTouchdownFrame),
            // Sideways (Mixamo "Jog Strafe"). Their sideways travel is taken out of the pose (not baked in): the Left one
            // was downloaded with root motion and would otherwise slide 0.67 m per step and snap back.
            StrafeLeft = ImportClip("Jog Strafe Left", avatar, true, false, -1f, bakeXZ: false),
            StrafeRight = ImportClip("Jog Strafe Right", avatar, true, false, -1f, bakeXZ: false),
        };
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ac == null) ac = BuildController(clips);
        UpgradeController(ac, clips);
        return ac;
    }

    /// <summary>
    /// Finds the clip file named "...@[name].fbx" (e.g. "CorporateMiner (4)@Walking.fbx") or just "[name].fbx", configures
    /// it and returns its clip (renamed to [name]), or null.
    /// </summary>
    private static AnimationClip ImportClip(string name, Avatar avatar, bool loop, bool heightFromFeet, float firstFrame = -1f, bool bakeXZ = true)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("@" + name + ".fbx", System.StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/" + name + ".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
            ConfigureImport(path, avatar, loop, heightFromFeet, firstFrame, bakeXZ, name);
            return LoadClip(path);
        }
        Debug.LogWarning($"[Ore What] No '@{name}.fbx' animation found; its state uses a placeholder.");
        return null;
    }

    private static AnimationClip LoadClip(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    /// <summary>Humanoid import. sourceAvatar null = create the avatar from this model (the skinned character).</summary>
    /// bakeXZ false = the clip's sideways/forward travel becomes root motion (unused: the CharacterController moves the
    /// player) instead of moving the body away from its root; needed for clips that weren't downloaded "In Place".
    /// clipName renames Mixamo's generic "mixamo.com" clip.
    private static void ConfigureImport(string path, Avatar sourceAvatar, bool loop, bool heightFromFeet = false, float firstFrame = -1f,
                                        bool bakeXZ = true, string clipName = null)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.animationType = ModelImporterAnimationType.Human;
        if (sourceAvatar == null) imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        else { imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; imp.sourceAvatar = sourceAvatar; }
        ModelImporterClipAnimation[] clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
        foreach (ModelImporterClipAnimation c in clips)
        {
            c.loopTime = loop;
            c.loopPose = loop;
            // Bake all root movement into the pose: the player's CharacterController does the moving.
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = bakeXZ;
            if (clipName != null && c.name == "mixamo.com") c.name = clipName;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = !heightFromFeet; // Based Upon: Original, or Feet (feet stay on the ground)
            c.heightFromFeet = heightFromFeet;
            c.keepOriginalPositionXZ = true;
            if (firstFrame >= 0f) c.firstFrame = firstFrame;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    private static AnimatorController BuildController(Clips clips)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(ControllerFolder)) AssetDatabase.CreateFolder("Assets/Animations", "Character");
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        ac.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        AnimatorControllerParameter[] ps = ac.parameters; // a copy: edit, then assign back
        ps[1].defaultBool = true; // Grounded
        ac.parameters = ps;

        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        AnimatorState idle = sm.AddState("Idle", new Vector3(300, 0));
        AnimatorState walk = sm.AddState("Walk", new Vector3(300, 100));
        AnimatorState runS = sm.AddState("Run", new Vector3(300, 200));
        AnimatorState jump = sm.AddState("Jump", new Vector3(600, 0));
        AnimatorState fall = sm.AddState("Fall", new Vector3(600, 100));
        AnimatorState land = sm.AddState("Land", new Vector3(600, 200));
        sm.defaultState = idle;
        // A missing clip gets a placeholder (a Humanoid state with no clip drops the hips to the
        // feet, sinking the body): Walk = run played slower, the others = run frozen (speed 0).
        AnimationClip run = clips.Run;
        runS.motion = run;
        Assign(walk, clips.Walk, run, 0.6f);
        Assign(idle, clips.Idle, run, 0f);
        Assign(jump, clips.Jump, run, 0f);
        Assign(fall, clips.Fall, run, 0f);
        Assign(land, clips.Land, run, 1f); // Land leaves by exit time, so it must play

        // Walk 4 m/s, sprint 7 m/s (PlayerMovement).
        Link(idle, walk, AnimatorConditionMode.Greater, "Speed", 0.3f);
        Link(walk, idle, AnimatorConditionMode.Less, "Speed", 0.2f);
        Link(walk, runS, AnimatorConditionMode.Greater, "Speed", 5.2f);
        Link(runS, walk, AnimatorConditionMode.Less, "Speed", 4.8f);

        AnimatorStateTransition toJump = sm.AddAnyStateTransition(jump);
        toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        toJump.canTransitionToSelf = false;
        toJump.duration = 0.1f;

        Link(jump, fall, AnimatorConditionMode.Less, "VerticalSpeed", 0f);
        foreach (AnimatorState s in new[] { idle, walk, runS }) Link(s, fall, AnimatorConditionMode.IfNot, "Grounded", 0f); // walked off a ledge
        foreach (AnimatorState s in new[] { jump, fall })
        {
            AnimatorStateTransition t = Link(s, land, AnimatorConditionMode.If, "Grounded", 0f);
            t.duration = 0.08f;
        }
        // Land plays briefly, then back to Idle (which moves on to Walk/Run by Speed).
        AnimatorStateTransition back = land.AddTransition(idle);
        back.hasExitTime = true;
        back.exitTime = 0.5f;
        back.duration = 0.2f;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    /// <summary>
    /// Adds to an existing controller (idempotent, keeps its states and hand edits):
    /// - ForwardSpeed + Walk/Run/WalkBackPlayback parameters; Walk, Run, Walk Backward play at
    ///   their Playback parameter (CharacterAnimator matches it to the real speed, so feet don't slide);
    /// - a Walk Backward state (moving backward = ForwardSpeed below -0.3) with its transitions;
    /// - Land exits straight into Run / Walk Backward / Walk when still moving, before Land → Idle.
    /// </summary>
    private static void UpgradeController(AnimatorController ac, Clips clips)
    {
        foreach (string p in new[] { "ForwardSpeed", "WalkPlayback", "RunPlayback", "WalkBackPlayback" })
            if (System.Array.FindIndex(ac.parameters, x => x.name == p) < 0)
            {
                ac.AddParameter(p, AnimatorControllerParameterType.Float);
                if (p != "ForwardSpeed")
                {
                    AnimatorControllerParameter[] ps = ac.parameters;
                    ps[ps.Length - 1].defaultFloat = 1f;
                    ac.parameters = ps;
                }
            }

        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        AnimatorState idle = Find(sm, "Idle"), walk = Find(sm, "Walk"), runS = Find(sm, "Run"), fall = Find(sm, "Fall"), land = Find(sm, "Land");
        UsePlaybackParameter(walk, "WalkPlayback");
        UsePlaybackParameter(runS, "RunPlayback");

        AnimatorState back = Find(sm, "Walk Backward");
        if (back == null && clips.WalkBack != null)
        {
            back = sm.AddState("Walk Backward", new Vector3(0, 100));
            // Forward moves: only when not moving backward (sideways-only counts as forward).
            foreach (AnimatorStateTransition t in idle.transitions)
                if (t.destinationState == walk) t.AddCondition(AnimatorConditionMode.Greater, -0.3f, "ForwardSpeed");
            foreach (AnimatorState s in new[] { idle, walk, runS })
            {
                AnimatorStateTransition t = Link(s, back, AnimatorConditionMode.Less, -0.3f, "ForwardSpeed");
                t.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
                MoveToFront(s, t); // checked before Idle → Walk
            }
            Link(back, walk, AnimatorConditionMode.Greater, 0.1f, "ForwardSpeed");
            Link(back, idle, AnimatorConditionMode.Less, 0.2f, "Speed");
            Link(back, fall, AnimatorConditionMode.IfNot, 0f, "Grounded");

            // After landing, go straight back to moving instead of via Idle (checked in this order).
            AddLandExit(land, runS, ("Speed", AnimatorConditionMode.Greater, 5.2f));
            AddLandExit(land, back, ("ForwardSpeed", AnimatorConditionMode.Less, -0.3f), ("Speed", AnimatorConditionMode.Greater, 0.3f));
            AddLandExit(land, walk, ("Speed", AnimatorConditionMode.Greater, 0.3f));
        }
        if (back != null)
        {
            back.motion = clips.WalkBack;
            UsePlaybackParameter(back, "WalkBackPlayback");
        }
        AddStrafing(ac, clips, walk, runS, back);
        TuneStopping(idle, walk, runS, back);
        if (clips.Land != null) land.motion = clips.Land;
        foreach (AnimatorStateTransition t in land.transitions)
            if (t.hasExitTime) t.exitTime = t.destinationState == idle ? LandIdleExit : LandMovingExit;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Sideways movement: Walk, Run and Walk Backward keep their states, transitions and playback parameters, but each
    /// now plays a 2D direction blend (Freeform Directional, MoveX = right, MoveY = forward, the movement direction
    /// relative to the facing, from CharacterAnimator): its forward clip at (0, 1), Walking Backward at (0, -1) and the
    /// jog strafes at (-1, 0) / (1, 0). W+A, S+D... blend the two nearest clips. The strafes serve walking and running
    /// sideways (only one speed of strafe exists); CharacterAnimator scales the playback to the real speed and direction.
    /// Without the strafe clips nothing changes.
    /// </summary>
    private static void AddStrafing(AnimatorController ac, Clips clips, AnimatorState walk, AnimatorState runS, AnimatorState back)
    {
        if (clips.StrafeLeft == null || clips.StrafeRight == null || clips.WalkBack == null) return;
        foreach (string p in new[] { "MoveX", "MoveY" })
            if (System.Array.FindIndex(ac.parameters, x => x.name == p) < 0)
            {
                ac.AddParameter(p, AnimatorControllerParameterType.Float);
                if (p == "MoveY")
                {
                    AnimatorControllerParameter[] ps = ac.parameters;
                    ps[ps.Length - 1].defaultFloat = 1f; // straight ahead until told otherwise
                    ac.parameters = ps;
                }
            }
        if (walk != null && clips.Walk != null) walk.motion = DirectionTree(ac, "Walk (directions)", clips.Walk, clips.WalkBack, clips.StrafeLeft, clips.StrafeRight, clips.Walk);
        if (runS != null && clips.Run != null) runS.motion = DirectionTree(ac, "Run (directions)", clips.Run, clips.WalkBack, clips.StrafeLeft, clips.StrafeRight, clips.Run);
        if (back != null && clips.Walk != null) back.motion = DirectionTree(ac, "Walk Backward (directions)", clips.Walk, clips.WalkBack, clips.StrafeLeft, clips.StrafeRight, clips.WalkBack);
    }

    /// <summary>
    /// A 2D directional blend tree stored inside the controller asset (reused by name, so re-running doesn't pile up
    /// copies). A blend plays its clips in step by cycle fraction, so every clip gets a cycle offset that puts its LEFT
    /// foot down at the same moment as the state's main clip (main): otherwise a diagonal mixes one clip's left step
    /// with another's right step and the feet shuffle (measured: Jog Strafe Right plants the left foot half a cycle
    /// away from Walking).
    /// </summary>
    private static BlendTree DirectionTree(AnimatorController ac, string name, AnimationClip forward, AnimationClip backward,
                                           AnimationClip left, AnimationClip right, AnimationClip main)
    {
        BlendTree tree = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (o is BlendTree t && t.name == name) tree = t;
        if (tree == null)
        {
            tree = new BlendTree { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, ac);
        }
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        tree.useAutomaticThresholds = false;
        tree.children = new ChildMotion[0];
        tree.AddChild(forward, new Vector2(0f, 1f));
        tree.AddChild(backward, new Vector2(0f, -1f));
        tree.AddChild(left, new Vector2(-1f, 0f));
        tree.AddChild(right, new Vector2(1f, 0f));
        float mainPhase = LeftFootDownPhase(main);
        ChildMotion[] children = tree.children; // a copy: edit, then assign back
        for (int i = 0; i < children.Length; i++)
            children[i].cycleOffset = Mathf.Repeat(LeftFootDownPhase((AnimationClip)children[i].motion) - mainPhase, 1f);
        tree.children = children;
        EditorUtility.SetDirty(tree);
        return tree;
    }

    /// <summary>When (fraction of its cycle) a locomotion clip has its left foot lowest, sampled on the character.</summary>
    private static float LeftFootDownPhase(AnimationClip clip)
    {
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            Transform foot = model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftFoot);
            float lowest = float.MaxValue, phase = 0f;
            AnimationMode.StartAnimationMode();
            for (int i = 0; i < 200; i++)
            {
                float u = i / 200f;
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(model, clip, clip.length * u);
                AnimationMode.EndSampling();
                float y = model.transform.InverseTransformPoint(foot.position).y;
                if (y < lowest) { lowest = y; phase = u; }
            }
            return phase;
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(model);
        }
    }

    /// <summary>
    /// Snappier starts/stops. PlayerMovement eases the speed (4 → 0.2 m/s takes ~0.22 s), so a
    /// 0.2 m/s Idle threshold kept the legs cycling in place. Moving ⇄ Idle now switch at
    /// StartSpeed / StopSpeed, Run has its own exit to Idle (and Walk → Idle may interrupt
    /// Run → Walk), and the blend into Idle is a little longer so the legs settle smoothly.
    /// </summary>
    private static void TuneStopping(AnimatorState idle, AnimatorState walk, AnimatorState runS, AnimatorState back)
    {
        foreach (AnimatorStateTransition t in idle.transitions)
            if (t.destinationState == walk || t.destinationState == back) SetThreshold(t, "Speed", StartSpeed);

        foreach (AnimatorState s in new[] { walk, runS, back })
        {
            if (s == null) continue;
            AnimatorStateTransition toIdle = System.Array.Find(s.transitions, t => t.destinationState == idle);
            if (toIdle == null) toIdle = Link(s, idle, AnimatorConditionMode.Less, "Speed", StopSpeed);
            SetThreshold(toIdle, "Speed", StopSpeed);
            toIdle.duration = 0.25f;
            MoveToFront(s, toIdle); // a stop wins over Run → Walk
        }

        // Stopping from a run passes Run → Walk's threshold first; let Walk → Idle cut that blend short.
        foreach (AnimatorStateTransition t in runS.transitions)
            if (t.destinationState == walk) t.interruptionSource = TransitionInterruptionSource.Destination;
    }

    private static void SetThreshold(AnimatorStateTransition t, string parameter, float value)
    {
        AnimatorCondition[] cs = t.conditions; // a copy: edit, then assign back
        for (int i = 0; i < cs.Length; i++) if (cs[i].parameter == parameter) cs[i].threshold = value;
        t.conditions = cs;
    }

    private static AnimatorState Find(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState c in sm.states) if (c.state.name == name) return c.state;
        return null;
    }

    private static void UsePlaybackParameter(AnimatorState s, string parameter)
    {
        s.speed = 1f;
        s.speedParameter = parameter;
        s.speedParameterActive = true;
    }

    private static void MoveToFront(AnimatorState s, AnimatorStateTransition t)
    {
        var list = new System.Collections.Generic.List<AnimatorStateTransition>(s.transitions);
        list.Remove(t);
        list.Insert(0, t);
        s.transitions = list.ToArray();
    }

    /// <summary>Land → target once the landing is 35% through, if the conditions hold. Inserted before the existing exits (Land → Idle stays last).</summary>
    private static void AddLandExit(AnimatorState land, AnimatorState target, params (string param, AnimatorConditionMode mode, float value)[] conditions)
    {
        AnimatorStateTransition t = land.AddTransition(target);
        foreach (var c in conditions) t.AddCondition(c.mode, c.value, c.param);
        t.hasExitTime = true;
        t.exitTime = LandMovingExit;
        t.duration = 0.2f;
        var list = new System.Collections.Generic.List<AnimatorStateTransition>(land.transitions);
        list.Remove(t);
        int idleExit = list.FindIndex(x => x.destinationState != null && x.destinationState.name == "Idle");
        list.Insert(idleExit < 0 ? list.Count : idleExit, t);
        land.transitions = list.ToArray();
    }

    private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold, string param)
        => Link(from, to, mode, param, threshold);

    private static void Assign(AnimatorState state, AnimationClip clip, AnimationClip placeholder, float placeholderSpeed)
    {
        state.motion = clip != null ? clip : placeholder;
        state.speed = clip != null ? 1f : placeholderSpeed;
    }

    private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, string param, float threshold)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.AddCondition(mode, threshold, param);
        t.hasExitTime = false;
        t.duration = 0.15f;
        return t;
    }
}
