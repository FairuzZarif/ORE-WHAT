using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Separates the first-person LOOK of the held item from the character's REAL pose.
/// Put it on the first-person camera (PlayerCamera).
///
/// The first-person views (pickaxe, hammer, guns, held ore) were laid out around shoulders sitting
/// just below the eyes. The character's real shoulders are much lower (big head) and don't pitch
/// with the camera. So every frame this moves the active view (item + grip points) so that its
/// layout's shoulder point lands on the character's real shoulders, and the character's own arms
/// reach it with a normal, anatomical skeleton: in the world the tool is held at chest/waist height
/// and tilts with where you look. That world pose is what shadows and (later) other players see.
///
/// The local player's overlay camera (ViewModelCamera: arms + held item) gets the same offset, so
/// on screen nothing moved: first person looks exactly as before. Only rendering is offset; no
/// bone is ever displaced.
///
/// World things that must line up with the first-person image use the presented positions:
/// the carry point for carried ores (<see cref="carryPoint"/>) and throws (<see cref="ToPresented"/>).
/// </summary>
[DefaultExecutionOrder(90)] // after the Animator, CameraEffects and the views' own motion; before FirstPersonArmsIK (100)
public class FirstPersonPresentation : MonoBehaviour
{
    [Tooltip("The player character. Its real shoulders (upper-arm joints) are where the held item's layout is anchored.")]
    [SerializeField] private Animator character;
    [Tooltip("The shoulder point the first-person layouts were designed around, in camera space " +
             "(midpoint between the shoulders). This point is moved onto the character's real shoulders.")]
    [SerializeField] private Vector3 layoutShoulders = new Vector3(-0.009f, -0.151f, 0.134f);
    [Tooltip("The overlay camera that draws the arms and the held item. Gets the same offset, so the image doesn't change.")]
    [SerializeField] private Transform viewModelCamera;
    [Tooltip("Optional. Kept at Carry Holder's presented position: OreCarryController pulls carried ores here, " +
             "so a carried (world) ore appears in the hands on screen.")]
    [SerializeField] private Transform carryPoint;
    [Tooltip("The held-item view's holder that the hands close around (HeldResourceView.Holder).")]
    [SerializeField] private Transform carryHolder;

    /// <summary>How far the held item's real (world) pose is from where the local camera shows it. World = presented + this.</summary>
    public Vector3 WorldOffset { get; private set; }

    /// <summary>A world point on the held item → where the local camera shows it (e.g. where a throw should start).</summary>
    public Vector3 ToPresented(Vector3 world) => world - WorldOffset;

    private Transform leftShoulder, rightShoulder;
    private readonly List<Transform> views = new List<Transform>();
    private readonly List<ViewModelMotion> motions = new List<ViewModelMotion>();
    private readonly List<Vector3> applied = new List<Vector3>(); // camera-space offset currently added to each view

    private void Awake()
    {
        // The views: camera children whose arms are posed by FirstPersonArmsIK.
        foreach (Transform child in transform)
            if (child.GetComponentInChildren<FirstPersonArmsIK>(true) != null)
            {
                views.Add(child);
                motions.Add(child.GetComponent<ViewModelMotion>());
                applied.Add(Vector3.zero);
            }
    }

    private void OnDisable()
    {
        for (int i = 0; i < views.Count; i++) SetOffset(i, Vector3.zero);
        if (viewModelCamera != null) viewModelCamera.localPosition = Vector3.zero;
        WorldOffset = Vector3.zero;
    }

    private void LateUpdate()
    {
        if ((leftShoulder == null || rightShoulder == null) && character != null && character.isInitialized)
        {
            // Looked up here, not in Awake: the Animator may not be initialised yet when this wakes.
            leftShoulder = character.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightShoulder = character.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        if (leftShoulder == null || rightShoulder == null) return;

        Vector3 realShoulders = (leftShoulder.position + rightShoulder.position) * 0.5f;
        WorldOffset = realShoulders - transform.TransformPoint(layoutShoulders);
        Vector3 local = transform.InverseTransformVector(WorldOffset);

        for (int i = 0; i < views.Count; i++)
            SetOffset(i, views[i] != null && views[i].gameObject.activeInHierarchy ? local : Vector3.zero);
        if (viewModelCamera != null) viewModelCamera.localPosition = local;

        if (carryPoint != null && carryHolder != null)
        {
            bool holderMoved = carryHolder.gameObject.activeInHierarchy;
            carryPoint.SetPositionAndRotation(carryHolder.position - (holderMoved ? WorldOffset : Vector3.zero), carryHolder.rotation);
        }
    }

    /// <summary>
    /// Puts the view at its own pose + offset. ViewModelMotion rewrites an active view's pose every
    /// Update, so for those the pose is fresh; otherwise the previous offset is taken off first.
    /// </summary>
    private void SetOffset(int i, Vector3 offset)
    {
        Transform v = views[i];
        if (v == null) return;
        bool rewritten = v.gameObject.activeInHierarchy && motions[i] != null && motions[i].isActiveAndEnabled;
        Vector3 own = rewritten ? v.localPosition : v.localPosition - applied[i];
        v.localPosition = own + offset;
        applied[i] = offset;
    }
}
