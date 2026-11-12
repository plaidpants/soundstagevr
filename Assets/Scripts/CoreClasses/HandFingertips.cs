// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Lives on a spawned OVR hand (Resources/Hands/*). Once the hand skeleton is up it
// puts a FingertipPoker on every fingertip and exposes the thumb/index tips so
// platformSetup can put the manipulator's grab point at the actual pinch.

using System.Collections.Generic;
using UnityEngine;

public class HandFingertips : MonoBehaviour {
  public manipulator owner;
  public float tipRadius = 0.008f;
  public Transform thumbTip, indexTip;
  public bool isLeft;

  // Bones for holding tools (drumsticks, maracas...) and for hitting drums.
  Transform wrist, index1, pinky1, thumb2, palmCenter;
  readonly List<Transform> hitPoints = new List<Transform>(); // 5 fingertips + palm

  public static readonly List<HandFingertips> active = new List<HandFingertips>();
  void OnEnable() { if (!active.Contains(this)) active.Add(this); }
  void OnDisable() { active.Remove(this); }

  public IList<Transform> HitPoints { get { return hitPoints; } }

  OVRSkeleton skeleton;
  bool built = false;

  void Awake() {
    skeleton = GetComponent<OVRSkeleton>();
    ApplyGlowMaterial();
  }

  // The stock hand material (legacy diffuse) renders almost black in this scene. Use the
  // same additive glow shader as the module displays for faint "ghost" hands. Must be set
  // before OVRMeshRenderer initialises, since it remembers the original material.
  public static Color handTint = new Color(0.04f, 0.07f, 0.12f, 0.5f);
  void ApplyGlowMaterial() {
    SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
    Shader sh = Shader.Find("Sonic Ether/Particles/Additive");
    if (smr == null || sh == null) return;
    Material m = new Material(sh);
    m.SetColor("_TintColor", handTint);
    m.SetFloat("_EmissionGain", 0.2f);
    smr.sharedMaterial = m;
    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  }

  // Pose for a held tool. The aim (pointer) pose used for the pinch follows a ray from
  // the shoulder and barely turns with the wrist, so a held drumstick wouldn't follow a
  // wrist flick. This uses the hand's own bones: forward = wrist -> index/pinky knuckles,
  // up = back of the hand; anchored between the index knuckle and the thumb, where a
  // stick sits in a real grip (stable even when the fingers open).
  public bool HasHoldPose { get { return built && wrist != null && index1 != null && pinky1 != null && thumb2 != null; } }
  public void GetHoldPose(out Vector3 position, out Quaternion rotation) {
    Vector3 knuckles = (index1.position + pinky1.position) * 0.5f;
    Vector3 forward = knuckles - wrist.position;
    Vector3 side = index1.position - pinky1.position; // towards the thumb
    Vector3 up = Vector3.Cross(side, forward);
    if (isLeft) up = -up;
    if (forward.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f) {
      position = index1.position; rotation = wrist.rotation; return;
    }
    rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
    position = Vector3.Lerp(index1.position, thumb2.position, 0.5f);
  }

  public bool HasPinchPoint { get { return built && thumbTip != null && indexTip != null; } }
  public Vector3 PinchPoint { get { return (thumbTip.position + indexTip.position) * 0.5f; } }

  void Update() {
    if (built || skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null || owner == null) return;
    foreach (OVRBone b in skeleton.Bones) {
      switch (b.Id) {
        case OVRSkeleton.BoneId.Hand_WristRoot: wrist = b.Transform; continue;
        case OVRSkeleton.BoneId.Hand_Index1: index1 = b.Transform; continue;
        case OVRSkeleton.BoneId.Hand_Pinky1: pinky1 = b.Transform; continue;
        case OVRSkeleton.BoneId.Hand_Thumb2: thumb2 = b.Transform; continue;
        case OVRSkeleton.BoneId.Hand_Middle1: palmCenter = b.Transform; continue;
        case OVRSkeleton.BoneId.Hand_ThumbTip: thumbTip = b.Transform; break;
        case OVRSkeleton.BoneId.Hand_IndexTip: indexTip = b.Transform; break;
        case OVRSkeleton.BoneId.Hand_MiddleTip:
        case OVRSkeleton.BoneId.Hand_RingTip:
        case OVRSkeleton.BoneId.Hand_PinkyTip:
          break;
        default: continue;
      }
      FingertipPoker.Create(b.Transform, owner, tipRadius);
      hitPoints.Add(b.Transform);
    }
    if (palmCenter != null && wrist != null) {
      // Palm centre: between the wrist and the middle knuckle.
      GameObject palm = new GameObject("palmPoint");
      palm.transform.SetParent(wrist, false);
      palm.transform.position = Vector3.Lerp(wrist.position, palmCenter.position, 0.6f);
      hitPoints.Add(palm.transform);
    }
    built = true;
    Debug.Log("[HandFingertips] fingertip pokers ready on " + name);
  }
}
