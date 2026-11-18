// Is someone actually using SoundStage on this machine? (2026-11, internet session limits)
//
// Real input only: controller buttons, triggers, grips, thumbsticks, hand pinches, the
// keyboard, mouse movement and wheel - and on a headset, simply wearing it (someone
// listening to the jam is using it). The test harness's own actions don't count.
// Checked a few times a second; LastActive is in Time.realtimeSinceStartup.

using System;
using UnityEngine;

public class PlayerActivity : MonoBehaviour {
  public static float LastActive { get; private set; }
  public static event Action BecameActive; // after 20 s or more without input

  static PlayerActivity instance;
  Vector3 lastMouse;
  float mouseTravel, checkTimer, handsTimer;
  OVRHand[] hands = new OVRHand[0];

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("PlayerActivity");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<PlayerActivity>();
    LastActive = Time.realtimeSinceStartup;
  }

  public static float IdleSeconds { get { return Time.realtimeSinceStartup - LastActive; } }

  void Update() {
    // keys and the mouse every frame (a key press lasts one frame)
    Vector3 m = Input.mousePosition;
    mouseTravel = Mathf.Max(0, mouseTravel + (m - lastMouse).magnitude - 2f); // ignore jitter
    lastMouse = m;
    bool active = Input.anyKeyDown || Input.mouseScrollDelta.sqrMagnitude > 0 || mouseTravel > 60f;
    if (mouseTravel > 60f) mouseTravel = 0;

    checkTimer += Time.unscaledDeltaTime;
    if (!active && checkTimer >= .25f) {
      checkTimer = 0;
      active = VRActive();
    }
    if (!active) return;
    bool wasIdle = IdleSeconds >= 20f;
    LastActive = Time.realtimeSinceStartup;
    if (wasIdle) BecameActive?.Invoke();
  }

  bool VRActive() {
    if (!OVRSafe.Available) return false;
    try {
      if (OVRPlugin.userPresent) return true; // headset on someone's head
      if (OVRInput.Get(OVRInput.Button.Any, OVRInput.Controller.Touch)) return true;
      foreach (OVRInput.Controller c in new[] { OVRInput.Controller.LTouch, OVRInput.Controller.RTouch }) {
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c) > .35f) return true;
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, c) > .35f) return true;
        if (OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c).magnitude > .5f) return true;
      }
      handsTimer -= .25f;
      if (handsTimer <= 0) { hands = FindObjectsOfType<OVRHand>(); handsTimer = 5f; }
      foreach (OVRHand h in hands)
        if (h != null && h.IsTracked && h.GetFingerIsPinching(OVRHand.HandFinger.Index)) return true;
    } catch (Exception) { }
    return false;
  }
}
