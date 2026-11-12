// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Lives on a spawned OVR hand (Resources/Hands/*). Once the hand skeleton is up it
// puts a FingertipPoker on every fingertip and exposes the thumb/index tips so
// platformSetup can put the manipulator's grab point at the actual pinch.

using UnityEngine;

public class HandFingertips : MonoBehaviour {
  public manipulator owner;
  public float tipRadius = 0.008f;
  public Transform thumbTip, indexTip;

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

  public bool HasPinchPoint { get { return built && thumbTip != null && indexTip != null; } }
  public Vector3 PinchPoint { get { return (thumbTip.position + indexTip.position) * 0.5f; } }

  void Update() {
    if (built || skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null || owner == null) return;
    foreach (OVRBone b in skeleton.Bones) {
      switch (b.Id) {
        case OVRSkeleton.BoneId.Hand_ThumbTip: thumbTip = b.Transform; break;
        case OVRSkeleton.BoneId.Hand_IndexTip: indexTip = b.Transform; break;
        case OVRSkeleton.BoneId.Hand_MiddleTip:
        case OVRSkeleton.BoneId.Hand_RingTip:
        case OVRSkeleton.BoneId.Hand_PinkyTip:
          break;
        default: continue;
      }
      FingertipPoker.Create(b.Transform, owner, tipRadius);
    }
    built = true;
    Debug.Log("[HandFingertips] fingertip pokers ready on " + name);
  }
}
