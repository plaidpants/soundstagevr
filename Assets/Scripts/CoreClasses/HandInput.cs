// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Hand-tracking input for Quest, read straight from OVRPlugin so it works with or
// without OVRHand components in the scene. Index 0 is the LEFT hand and index 1
// the RIGHT hand, matching manipulator.controllerIndex (manips[0] is the left
// controller; see QuestInput for the legacy "R"-means-left naming).
//
// State is cached per frame, so both manipulators and platformSetup can query
// it freely without extra plugin calls or allocations.

using UnityEngine;

public static class HandInput {
  // Middle-finger pinch (copy / delete) and ring-finger pinch (teleport, stage drag and
  // turn: SnapTurn) - off (2026-11-21). Quest hand tracking reads those two fingers much
  // less reliably than the index and pinky, so they fired the menu and grabs by accident.
  // Kept for a later try (maybe with Meta's microgestures after the SDK upgrade).
  public static bool extraPinches = false;

  public struct Hand {
    public bool tracked;        // hand pose is usable
    public bool inputValid;     // pointer pose + pinch data are usable
    public bool systemGesture;  // palm-up system gesture in progress: ignore pinches
    public bool menuPressed;    // system "menu" gesture (left palm-up pinch)
    public float pinchIndex, pinchMiddle, pinchRing, pinchPinky;
    public Vector3 pointerPosition;    // tracking-space aim pose
    public Quaternion pointerRotation;
  }

  static readonly Hand[] hands = new Hand[2];
  static OVRPlugin.HandState state = new OVRPlugin.HandState();
  static int lastFrame = -1;

  // The active controller only flips to Hands after the first hand input (a pinch),
  // which left the controller rig stuck on the hands until then. Hand tracking data is
  // only valid while the runtime is tracking hands (controllers + hands, not concurrent),
  // so a tracked hand is the faster signal.
  public static bool HandsActive() {
    if (!OVRSafe.Available) return false;
    OVRPlugin.Controller c = OVRPlugin.GetActiveController();
    if (c == OVRPlugin.Controller.Hands || c == OVRPlugin.Controller.LHand || c == OVRPlugin.Controller.RHand) return true;
    Refresh();
    return hands[0].tracked || hands[1].tracked;
  }

  public static Hand Get(int index) {
    Refresh();
    return hands[index == 0 ? 0 : 1];
  }

  // Forces a fresh read (used right before rendering for lower-latency poses).
  public static void Refresh(bool force = false) {
    if (!OVRSafe.Available) return;
    if (!force && lastFrame == Time.frameCount) return;
    lastFrame = Time.frameCount;
    Read(0, OVRPlugin.Hand.HandLeft);
    Read(1, OVRPlugin.Hand.HandRight);
  }

  static void Read(int i, OVRPlugin.Hand which) {
    Hand h = new Hand();
    if (OVRPlugin.GetHandState(OVRPlugin.Step.Render, which, ref state)) {
      h.tracked = (state.Status & OVRPlugin.HandStatus.HandTracked) != 0;
      h.inputValid = (state.Status & OVRPlugin.HandStatus.InputStateValid) != 0;
      h.systemGesture = (state.Status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0;
      h.menuPressed = (state.Status & OVRPlugin.HandStatus.MenuPressed) != 0;
      if (state.PinchStrength != null) {
        if (!h.systemGesture) {
          h.pinchIndex = state.PinchStrength[(int)OVRPlugin.HandFinger.Index];
          h.pinchMiddle = state.PinchStrength[(int)OVRPlugin.HandFinger.Middle];
          h.pinchPinky = state.PinchStrength[(int)OVRPlugin.HandFinger.Pinky];
        }
        // Keep ring pinch available even when palm-up (Quest flags palm-up as systemGesture for index menu pinch)
        h.pinchRing = state.PinchStrength[(int)OVRPlugin.HandFinger.Ring];
      }
      h.pointerPosition = state.PointerPose.Position.FromFlippedZVector3f();
      h.pointerRotation = state.PointerPose.Orientation.FromFlippedZQuatf();
    }
    hands[i] = h;
  }
}
