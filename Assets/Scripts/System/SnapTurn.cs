// Snap turn on the right thumbstick (2026-11).
//
// Flick the right stick left or right to turn the view by snapAngle degrees around your
// head. It only reacts when the right hand isn't holding or pointing at anything (devices
// use the stick while selected, e.g. the touchpad / timeline) and not during controller
// calibration. Off in hands mode (no stick). PlayerPrefs "snapTurn" (1 = on, default).

using UnityEngine;

public class SnapTurn : MonoBehaviour {
  public static float snapAngle = 30f;
  public static float flickThreshold = 0.75f; // stick x past this turns
  public static float rearmThreshold = 0.3f;  // stick must come back inside this before the next turn

  static SnapTurn instance;
  OVRCameraRig rig;
  manipulator rightHand;
  bool armed = true;

  public static bool Enabled {
    get { return PlayerPrefs.GetInt("snapTurn", 1) == 1; }
    set { PlayerPrefs.SetInt("snapTurn", value ? 1 : 0); }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (instance != null) return;
    GameObject go = new GameObject("SnapTurn");
    DontDestroyOnLoad(go);
    instance = go.AddComponent<SnapTurn>();
  }

  void Update() {
    if (!Enabled || platformSetup.calibrating) return;
    if (rig == null) { rig = FindObjectOfType<OVRCameraRig>(); if (rig == null) return; }
    if (rightHand == null) {
      foreach (manipulator m in FindObjectsOfType<manipulator>()) if (!m.IsLeftHand) rightHand = m;
    }

    float x = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
    if (Mathf.Abs(x) < rearmThreshold) { armed = true; return; }
    if (!armed || Mathf.Abs(x) < flickThreshold) return;
    if (rightHand != null && rightHand.IsBusy) return;

    armed = false;
    Transform head = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main.transform;
    rig.transform.RotateAround(head.position, Vector3.up, Mathf.Sign(x) * snapAngle);
  }
}
