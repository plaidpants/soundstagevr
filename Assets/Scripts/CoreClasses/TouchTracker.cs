// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Reference-counted manipObject.onTouch. With hand tracking several colliders
// (the pinch point plus every fingertip) can touch the same key at once; without
// counting, one finger leaving would turn the key off while another still holds it.
//
// Fingertip touches also wait a moment before letting go (2026-11-24): a tracked finger
// resting on a key jitters in and out of it, and each flicker released and re-pressed the
// key (logs showed the same key hit twice 40-100 ms apart). Every flicker restarted the
// note - crackle - and, recording on overdub, left a sliver of a note in the roll that
// played back as more crackle every time round. A fingertip that comes back within
// releaseDelay now just keeps the key held.

using System.Collections.Generic;
using UnityEngine;

public static class TouchTracker {
  public static float releaseDelay = .08f;

  static readonly Dictionary<manipObject, int> counts = new Dictionary<manipObject, int>();
  struct Pending { public float time; public manipulator m; }
  static readonly Dictionary<manipObject, Pending> pending = new Dictionary<manipObject, Pending>();
  static readonly List<manipObject> due = new List<manipObject>();

  public static bool IsTouched(manipObject o) { return o != null && (counts.ContainsKey(o) || pending.ContainsKey(o)); }

  // debounce: a fingertip (FingertipPoker) - its release waits releaseDelay
  public static void Touch(manipObject o, bool on, manipulator m, bool debounce = false) {
    if (o == null) return;
    int c;
    counts.TryGetValue(o, out c);
    if (on && c == 0 && pending.Remove(o)) { counts[o] = 1; return; } // back within the delay: still held
    int next = on ? c + 1 : c - 1;
    if (next <= 0) counts.Remove(o); else counts[o] = next;
    if (on && c == 0) o.onTouch(true, m);
    else if (!on && c > 0 && next <= 0) {
      if (debounce && releaseDelay > 0) { pending[o] = new Pending { time = Time.unscaledTime, m = m }; Ticker.Ensure(); }
      else o.onTouch(false, m);
    }
  }

  static void Tick() {
    if (pending.Count == 0) return;
    float now = Time.unscaledTime;
    due.Clear();
    foreach (var kv in pending) if (kv.Key == null || now - kv.Value.time >= releaseDelay) due.Add(kv.Key);
    foreach (manipObject o in due) {
      Pending p = pending[o];
      pending.Remove(o);
      if (o != null) o.onTouch(false, p.m);
    }
  }

  class Ticker : MonoBehaviour {
    static Ticker instance;
    public static void Ensure() {
      if (instance != null) return;
      GameObject g = new GameObject("TouchTracker");
      Object.DontDestroyOnLoad(g);
      instance = g.AddComponent<Ticker>();
    }
    void Update() { Tick(); }
  }
}
