// Desktop spectator (2026-11, multiplayer milestone 0): lets the editor or a Windows
// build without a headset fly around and watch a session, so multiplayer can be
// tested with one Quest. Active only when no XR device is running.
//
// Hold the right mouse button to look around; W A S D to move, Q / E down / up,
// Shift to go faster. Moves the OVRCameraRig, so the NetPlayer head follows.

using UnityEngine;
using UnityEngine.XR;

public class NetSpectator : MonoBehaviour {
  public static bool Active {
    get {
      if (Application.platform == RuntimePlatform.Android) return false;
      return !XRSettings.isDeviceActive;
    }
  }

  public float speed = 1f, lookSpeed = 3f;

  OVRCameraRig rig;
  float yaw, pitch;
  Vector3 lastMouse;
  bool haveMouse;
  public static Vector2 MouseDelta; // pixels this frame (DesktopMode uses it too)

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!Active) return;
    GameObject g = new GameObject("NetSpectator");
    DontDestroyOnLoad(g);
    g.AddComponent<NetSpectator>();
    Debug.Log("[NetSpectator] no headset: desktop camera (right mouse = look, WASD/QE = move; the mouse hand is in Desktop/DesktopMode.cs)");
  }

  void Update() {
    if (rig == null) {
      rig = FindObjectOfType<OVRCameraRig>();
      if (rig == null) return;
      Vector3 e = rig.transform.eulerAngles;
      yaw = e.y;
      pitch = 0;
      // no headset, so no head height: the camera would sit on the floor
      Transform eye = rig.centerEyeAnchor;
      if (eye != null && eye.position.y < .5f) rig.transform.position += Vector3.up * 1.6f;
    }

    // mouse movement from its position (the project's Input Manager has no "Mouse X" / "Mouse Y")
    Vector3 mp = Input.mousePosition;
    Vector2 md = haveMouse ? (Vector2)(mp - lastMouse) : Vector2.zero;
    lastMouse = mp; haveMouse = true;
    MouseDelta = md;
    if (Input.GetMouseButton(1)) {
      yaw += md.x * lookSpeed * .1f;
      pitch = Mathf.Clamp(pitch - md.y * lookSpeed * .1f, -85f, 85f);
    }
    rig.transform.rotation = Quaternion.Euler(pitch, yaw, 0);

    Vector3 move = Vector3.zero;
    if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
    if (Input.GetKey(KeyCode.S)) move += Vector3.back;
    if (Input.GetKey(KeyCode.A)) move += Vector3.left;
    if (Input.GetKey(KeyCode.D)) move += Vector3.right;
    Vector3 up = Vector3.zero;
    if (Input.GetKey(KeyCode.E)) up += Vector3.up;
    if (Input.GetKey(KeyCode.Q)) up += Vector3.down;
    // Shift faster, Ctrl slower (for lining up on small controls)
    float mult = Input.GetKey(KeyCode.LeftShift) ? 3f : (Input.GetKey(KeyCode.LeftControl) ? .25f : 1f);
    float s = speed * mult * Time.deltaTime;
    rig.transform.position += (rig.transform.rotation * move + up) * s;
    // mouse wheel (when not holding anything): step forward / back
    if (!DesktopMode.Holding && Input.mouseScrollDelta.y != 0 && !Input.GetMouseButton(0))
      rig.transform.position += rig.transform.forward * Input.mouseScrollDelta.y * .1f * mult;
  }
}
