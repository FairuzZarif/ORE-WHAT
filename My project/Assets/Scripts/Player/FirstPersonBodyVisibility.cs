using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Which parts of the player's own character the first-person camera sees. Put it on
/// CharacterVisual. The character is the only body: there are no separate first-person arms.
///
/// Local player in first person (Local First Person on):
///   - head, torso, legs: shadows only (the camera sits inside the head),
///   - arms: while a first-person view is posing them (FirstPersonArmsIK active, i.e. something is
///     held), they're drawn on the ViewModel layer together with the held item, by the same overlay
///     camera, so the fingers wrap the handle correctly and nothing pokes into walls. With empty
///     hands they're shadows only, like the rest of the body.
/// Anyone else / third person (Local First Person off): everything renders normally, including the
/// held item (moved from the ViewModel layer onto the body's layer, casting shadows). Its world pose
/// is the real one (FirstPersonPresentation keeps it in the character's real hands), so this is the
/// same pose other players will see.
///
/// Only renderer layers and shadow modes change; the character, its bones and its Animator don't.
/// </summary>
public class FirstPersonBodyVisibility : MonoBehaviour
{
    [Tooltip("On for the local player's own character. Off = render the whole body normally " +
             "(other players, or to inspect your own character from outside).")]
    [SerializeField] private bool localFirstPerson = true;
    [Tooltip("The character's arm renderers (shown in first person while holding something).")]
    [SerializeField] private Renderer[] arms = new Renderer[0];
    [Tooltip("Everything else of the character (head, torso, legs): shadows only in first person.")]
    [SerializeField] private Renderer[] hiddenInFirstPerson = new Renderer[0];
    [Tooltip("The first-person camera. The FirstPersonArmsIK components under it pose the arms.")]
    [SerializeField] private Transform firstPersonViews;
    [SerializeField] private Animator animator;
    [Tooltip("Layer the arms use while held items are shown (the ViewModel layer, drawn by the overlay camera).")]
    [SerializeField] private int firstPersonArmsLayer = 6;

    /// <summary>On for the local player's first-person view; off = full body (third person / other players).</summary>
    public bool LocalFirstPerson
    {
        get => localFirstPerson;
        set { localFirstPerson = value; Apply(force: true); }
    }

    private FirstPersonArmsIK[] posers = new FirstPersonArmsIK[0];
    private int bodyLayer;
    private int appliedState = -1; // 0 = full body, 1 = first person (hands empty), 2 = first person (holding)

    // Held-item renderers shown in third person, with what to put back.
    private struct Shown { public Renderer r; public int layer; public ShadowCastingMode shadows; public bool receive; }
    private readonly List<Shown> shownHeld = new List<Shown>();
    private readonly List<Renderer> scratch = new List<Renderer>();

    private void Awake()
    {
        bodyLayer = gameObject.layer;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (firstPersonViews != null) posers = firstPersonViews.GetComponentsInChildren<FirstPersonArmsIK>(true);
    }

    private void OnEnable() => Apply(force: true);

    private void OnDisable() => ShowHeldItem(null);

    private void LateUpdate()
    {
        Apply(force: false);
        ShowHeldItem(localFirstPerson ? null : ActiveView());
    }

    private bool ArmsPosed() => ActiveView() != null;

    /// <summary>The first-person view whose IK is posing the arms (it holds the item), or null.</summary>
    private Transform ActiveView()
    {
        foreach (FirstPersonArmsIK p in posers)
            if (p != null && p.isActiveAndEnabled) return p.transform.parent;
        return null;
    }

    /// <summary>Third person: the held item's renderers join the body (visible, with shadows). Null = put everything back.</summary>
    private void ShowHeldItem(Transform view)
    {
        for (int i = shownHeld.Count - 1; i >= 0; i--)
        {
            Shown s = shownHeld[i];
            if (s.r != null && view != null && s.r.transform.IsChildOf(view)) continue;
            if (s.r != null) { s.r.gameObject.layer = s.layer; s.r.shadowCastingMode = s.shadows; s.r.receiveShadows = s.receive; }
            shownHeld.RemoveAt(i);
        }
        if (view == null) return;
        view.GetComponentsInChildren(false, scratch); // also catches models built later (e.g. a newly held ore)
        foreach (Renderer r in scratch)
        {
            if (r.gameObject.layer != firstPersonArmsLayer) continue;
            shownHeld.Add(new Shown { r = r, layer = r.gameObject.layer, shadows = r.shadowCastingMode, receive = r.receiveShadows });
            r.gameObject.layer = bodyLayer;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }

    private void Apply(bool force)
    {
        int state = !localFirstPerson ? 0 : ArmsPosed() ? 2 : 1;
        if (!force && state == appliedState) return;
        appliedState = state;

        foreach (Renderer r in hiddenInFirstPerson)
            if (r != null) r.shadowCastingMode = state == 0 ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;

        foreach (Renderer r in arms)
        {
            if (r == null) continue;
            r.gameObject.layer = state == 2 ? firstPersonArmsLayer : bodyLayer;
            r.shadowCastingMode = state == 0 ? ShadowCastingMode.On
                                : state == 1 ? ShadowCastingMode.ShadowsOnly
                                : ShadowCastingMode.Off; // the ViewModel layer isn't in the shadow pass anyway
            r.receiveShadows = state != 2; // like the old viewmodel: no world shadows on the held-item overlay
        }

        // A shadow-only body may count as unseen; the arms and the shadow must keep animating.
        if (animator != null)
            animator.cullingMode = localFirstPerson ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
    }

    [ContextMenu("Toggle Third-Person Check")]
    private void ToggleThirdPersonCheck() => LocalFirstPerson = !LocalFirstPerson;
}
