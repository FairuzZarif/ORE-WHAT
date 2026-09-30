using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hand-made finger pose: the local rotation of every finger joint of one hand, by bone name.
/// FirstPersonArmsIK plays it instead of its automatic finger curl while that view is held.
///
/// Make one: press Play, equip the item, PAUSE, rotate the finger joints (…HandIndex1/2/3,
/// …HandThumb1/2/3, ...) in the Scene view, then right-click the view's CharacterArmsIK component →
/// Capture Hand Poses. Assets keep changes made in Play mode, so the pose survives stopping.
/// Empty = not used (the automatic curl is used). It can also hold a hand-placed wrist (the hand
/// bone's rotation/position relative to its grip point), which replaces the automatic hand placement.
/// </summary>
[CreateAssetMenu(menuName = "Ore What/Hand Pose")]
public class HandPose : ScriptableObject
{
    [Serializable]
    public struct Joint
    {
        public string bone;
        public Quaternion localRotation;
    }

    [SerializeField] private List<Joint> joints = new List<Joint>();
    [Tooltip("The wrist was placed by hand: the hand's rotation and position relative to its grip point.")]
    [SerializeField] private bool hasWrist;
    [SerializeField] private Quaternion wristRotation = Quaternion.identity;
    [SerializeField] private Vector3 wristPosition;

    public bool HasPose => joints.Count > 0;
    public IReadOnlyList<Joint> Joints => joints;
    public bool HasWrist => hasWrist;
    /// <summary>Hand bone rotation relative to the grip point.</summary>
    public Quaternion WristRotation => wristRotation;
    /// <summary>Hand bone (wrist) position in the grip point's space.</summary>
    public Vector3 WristPosition => wristPosition;

    /// <summary>Records the hand bone's pose relative to its grip point.</summary>
    public void SetWrist(Transform hand, Transform grip)
    {
        hasWrist = true;
        wristRotation = Quaternion.Inverse(grip.rotation) * hand.rotation;
        wristPosition = grip.InverseTransformPoint(hand.position);
    }

    /// <summary>Records every joint below <paramref name="hand"/> (the fingers and thumb).</summary>
    public void Capture(Transform hand)
    {
        joints.Clear();
        foreach (Transform t in hand.GetComponentsInChildren<Transform>(true))
            if (t != hand) joints.Add(new Joint { bone = t.name, localRotation = t.localRotation });
    }

    public void Clear()
    {
        joints.Clear();
        hasWrist = false;
    }
}
