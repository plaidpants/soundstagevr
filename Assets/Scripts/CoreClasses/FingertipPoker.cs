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

  public static FingertipPoker Create(Transform bone, manipulator owner, float radius) {
    GameObject go = new GameObject("fingertipPoker");
    go.layer = owner.gameObject.layer;
    go.transform.SetParent(bone, false);
    Rigidbody rb = go.AddComponent<Rigidbody>();
    rb.isKinematic = true;
    rb.useGravity = false;
    SphereCollider c = go.AddComponent<SphereCollider>();
    c.isTrigger = true;
    c.radius = radius;
    FingertipPoker p = go.AddComponent<FingertipPoker>();
    p.owner = owner;
    return p;
  }

  static manipObject Find(Collider other) {
    manipObject o = other.GetComponent<manipObject>();
    if (o == null && other.attachedRigidbody != null) o = other.attachedRigidbody.GetComponent<manipObject>();
    return o;
  }

  void OnTriggerEnter(Collider other) {
    manipObject o = Find(other);
    if (o != null && touching.Add(o)) TouchTracker.Touch(o, true, owner);
  }

  void OnTriggerExit(Collider other) {
    manipObject o = Find(other);
    if (o != null && touching.Remove(o)) TouchTracker.Touch(o, false, owner);
  }

  // Hands lost or switched to controllers: release anything still held down.
  void OnDisable() {
    foreach (manipObject o in touching) if (o != null) TouchTracker.Touch(o, false, owner);
    touching.Clear();
  }
}
