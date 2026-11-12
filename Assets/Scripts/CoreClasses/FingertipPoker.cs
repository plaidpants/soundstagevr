// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// A small trigger sphere on a tracked fingertip. Touching a manipObject (a key,
// a button...) sends it onTouch exactly like the controller's grab tip does, so
// keys can be played with any finger, several at a time.

using System.Collections.Generic;
using UnityEngine;

public class FingertipPoker : MonoBehaviour {
  public manipulator owner;
  readonly HashSet<manipObject> touching = new HashSet<manipObject>();

  // TEMP debug: draw each touch sphere (dim when idle, bright while touching something).
  public static bool showDebugSpheres = true;
  Material debugMat;
  static readonly Color idleColor = new Color(0.10f, 0.10f, 0.02f, 0.5f);
  static readonly Color touchColor = new Color(0.10f, 0.45f, 0.10f, 0.5f);

  void AddDebugSphere(float radius) {
    GameObject viz = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    Destroy(viz.GetComponent<Collider>());
    viz.name = "debugSphere";
    viz.layer = gameObject.layer;
    viz.transform.SetParent(transform, false);
    viz.transform.localScale = Vector3.one * radius * 2f; // primitive sphere is 1 unit across
    Renderer r = viz.GetComponent<Renderer>();
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    Shader sh = Shader.Find("Sonic Ether/Particles/Additive");
    if (sh != null) {
      debugMat = new Material(sh);
      debugMat.SetFloat("_EmissionGain", 0.3f);
      r.sharedMaterial = debugMat;
      UpdateDebugColor();
    }
  }

  void UpdateDebugColor() {
    if (debugMat != null) debugMat.SetColor("_TintColor", touching.Count > 0 ? touchColor : idleColor);
  }

  public static FingertipPoker Create(Transform bone, manipulator owner, float radius) {
    GameObject go = new GameObject("fingertipPoker");
    go.layer = owner.gameObject.layer;
    go.transform.SetParent(bone, false);
    // Non-kinematic on purpose: with the project's default Contact Pairs Mode, Unity sends
    // no trigger events for kinematic-vs-kinematic pairs, and keys sit on kinematic device
    // bodies, so a kinematic poker never saw them. Frozen + trigger = no physical response.
    Rigidbody rb = go.AddComponent<Rigidbody>();
    rb.isKinematic = false;
    rb.useGravity = false;
    rb.constraints = RigidbodyConstraints.FreezeAll;
    SphereCollider c = go.AddComponent<SphereCollider>();
    c.isTrigger = true;
    c.radius = radius;
    FingertipPoker p = go.AddComponent<FingertipPoker>();
    p.owner = owner;
    if (showDebugSpheres) p.AddDebugSphere(radius);
    return p;
  }

  static manipObject Find(Collider other) {
    manipObject o = other.GetComponent<manipObject>();
    if (o == null && other.attachedRigidbody != null) o = other.attachedRigidbody.GetComponent<manipObject>();
    return o;
  }

  void OnTriggerEnter(Collider other) {
    manipObject o = Find(other);
    if (o != null && touching.Add(o)) { TouchTracker.Touch(o, true, owner); UpdateDebugColor(); }
  }

  void OnTriggerExit(Collider other) {
    manipObject o = Find(other);
    if (o != null && touching.Remove(o)) { TouchTracker.Touch(o, false, owner); UpdateDebugColor(); }
  }

  // Hands lost or switched to controllers: release anything still held down.
  void OnDisable() {
    foreach (manipObject o in touching) if (o != null) TouchTracker.Touch(o, false, owner);
    touching.Clear();
    UpdateDebugColor();
  }
}
