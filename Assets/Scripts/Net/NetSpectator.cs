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
      if (Application.platform == RuntimePlatform.Android) return Phone.Active; // a Quest: never; the phone build: always
      return !XRSettings.isDeviceActive;
    }
  }

  public float speed = 1f, lookSpeed = 3f;

  OVRCameraRig rig;
  float yaw, pitch;
  Vector3 lastMouse;
  bool haveMouse;
  public static Vector2 MouseDelta; // pixels this frame (DesktopMode uses it too)
  public static NetSpectator instance;
  Vector2 coastVel; // PC: the right-mouse look coasting after letting go (degrees per second)

  // the phone (PhoneMode): turn and move by touch instead of mouse and keys
  public float Yaw { get { return yaw; } }
  public void SetLook(float newYaw, float newPitch) { // MOTION: where the phone points
    yaw = newYaw;
    pitch = Mathf.Clamp(newPitch, -85f, 85f);
  }
  public void Look(float dYaw, float dPitch) {
    yaw += dYaw;
    pitch = Mathf.Clamp(pitch + dPitch, -85f, 85f);
  }
  // fly the eye to eyeTarget, looking at lookAt, over a moment (the phone's double-tap)
  public void FlyTo(Vector3 eyeTarget, Vector3 lookAt) {
    if (rig == null) return;
    Vector3 d = lookAt - eyeTarget;
    if (d.sqrMagnitude < 1e-6f) return;
    flyFrom = rig.transform.position;
    Transform eye = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : rig.transform;
    flyTo = rig.transform.position + (eyeTarget - eye.position);
    flyYaw0 = yaw; flyPitch0 = pitch;
    flyYaw1 = yaw + Mathf.DeltaAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
    flyPitch1 = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -85f, 85f);
    flyT = 0;
  }
  public static float flyTime = .4f;
  Vector3 flyFrom, flyTo;
  float flyYaw0, flyYaw1, flyPitch0, flyPitch1, flyT = -1;
  public bool Flying { get { return flyT >= 0; } }
  public void StopFlying() { flyT = -1; }

  // x right, y up, z forward, relative to where the camera faces (level)
  public void Fly(Vector3 local) {
    if (rig == null) return;
    rig.transform.position += Quaternion.Euler(0, yaw, 0) * new Vector3(local.x, 0, local.z) + Vector3.up * local.y;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!Active) return;
    GameObject g = new GameObject("NetSpectator");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetSpectator>();
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

    if (PointerInput.touch) { // the phone: PhoneMode calls Look / Fly / FlyTo
      MouseDelta = Vector2.zero;
      if (flyT >= 0) {
        flyT += Time.unscaledDeltaTime / flyTime;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flyT));
        rig.transform.position = Vector3.Lerp(flyFrom, flyTo, k);
        yaw = Mathf.Lerp(flyYaw0, flyYaw1, k);
        pitch = Mathf.Lerp(flyPitch0, flyPitch1, k);
        if (flyT >= 1f) flyT = -1;
      }
      rig.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
      return;
    }

    // mouse movement from its position (the project's Input Manager has no "Mouse X" / "Mouse Y")
    Vector3 mp = Input.mousePosition;
    Vector2 md = haveMouse ? (Vector2)(mp - lastMouse) : Vector2.zero;
    lastMouse = mp; haveMouse = true;
    MouseDelta = md;
    if (Input.GetMouseButton(1)) {
      yaw += md.x * lookSpeed * .1f;
      pitch = Mathf.Clamp(pitch - md.y * lookSpeed * .1f, -85f, 85f);
      // remember how fast it was turning, for the coast after letting go
      float dt = Mathf.Max(Time.unscaledDeltaTime, .001f);
      Vector2 v = Vector2.ClampMagnitude(new Vector2(md.x, -md.y) * lookSpeed * .1f / dt, PhoneMode.coastMaxDegPerSec);
      coastVel = Vector2.Lerp(coastVel, v, .4f);
    } else if (coastVel.sqrMagnitude > 0) {
      // let go: coast on a little, slowing to a stop (PhoneMode does the same for a finger)
      float dt = Time.unscaledDeltaTime;
      yaw += coastVel.x * dt;
      pitch = Mathf.Clamp(pitch + coastVel.y * dt, -85f, 85f);
      coastVel *= Mathf.Exp(-PhoneMode.coastDamping * dt);
      if (coastVel.magnitude < 3f) coastVel = Vector2.zero;
    }
    rig.transform.rotation = Quaternion.Euler(pitch, yaw, 0);

    Vector3 move = Vector3.zero;
    bool keys = !NetCodeDialog.Typing; // typing a room code on the keyboard
    if (keys && Input.GetKey(KeyCode.W)) move += Vector3.forward;
    if (keys && Input.GetKey(KeyCode.S)) move += Vector3.back;
    if (keys && Input.GetKey(KeyCode.A)) move += Vector3.left;
    if (keys && Input.GetKey(KeyCode.D)) move += Vector3.right;
    Vector3 up = Vector3.zero;
    if (keys && Input.GetKey(KeyCode.E)) up += Vector3.up;
    if (keys && Input.GetKey(KeyCode.Q)) up += Vector3.down;
    // Shift faster, Ctrl slower (for lining up on small controls)
    float mult = Input.GetKey(KeyCode.LeftShift) ? 3f : (Input.GetKey(KeyCode.LeftControl) ? .25f : 1f);
    float s = speed * mult * Time.deltaTime;
    rig.transform.position += (rig.transform.rotation * move + up) * s;
    // mouse wheel (when not holding anything): step forward / back
    if (!DesktopMode.Holding && Input.mouseScrollDelta.y != 0 && !Input.GetMouseButton(0))
      rig.transform.position += rig.transform.forward * Input.mouseScrollDelta.y * .1f * mult;
  }
}
