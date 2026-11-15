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

  public float speed = 2f, lookSpeed = 3f;

  OVRCameraRig rig;
  float yaw, pitch;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!Active) return;
    GameObject g = new GameObject("NetSpectator");
    DontDestroyOnLoad(g);
    g.AddComponent<NetSpectator>();
    Debug.Log("[NetSpectator] no headset: desktop spectator (right mouse = look, WASD/QE = move)");
  }

  void Update() {
    if (rig == null) {
      rig = FindObjectOfType<OVRCameraRig>();
      if (rig == null) return;
      Vector3 e = rig.transform.eulerAngles;
      yaw = e.y;
      pitch = 0;
    }

    if (Input.GetMouseButton(1)) {
      yaw += Input.GetAxis("Mouse X") * lookSpeed;
      pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -85f, 85f);
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
    float s = speed * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f) * Time.deltaTime;
    rig.transform.position += (rig.transform.rotation * move + up) * s;
  }
}
