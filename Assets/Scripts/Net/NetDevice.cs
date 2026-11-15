// A device's identity in a multiplayer session (2026-11, milestone 1).
//
// Added to every device by deviceInterface.Awake. Does nothing unless a session is
// running (NetSync.Active) and the object is a real device (tagged "instrument" - menu
// previews are untagged).
//  * Created here (menu, copy) -> NetSync announces it (the host gives it its ID).
//  * Moved here (one of its handles is grabbed) -> pose sent ~20x/s while it moves.
//  * Moved by someone else -> eases to their pose.
//  * Destroyed here (trash) -> NetSync tells the others.
// Patch loads and joins replace whole patches through NetSync, which marks the devices
// it creates or removes so they don't announce themselves.

using System.Collections.Generic;
using UnityEngine;

public class NetDevice : MonoBehaviour {
  public int netId;          // 0 = not known to the session yet
  public int tempId;         // a client's own number for it until the host's ID arrives
  [System.NonSerialized] public bool fromNetwork;    // created by NetSync, don't announce
  [System.NonSerialized] public bool silentDestroy;  // removed by NetSync / patch load

  public static readonly Dictionary<int, NetDevice> byId = new Dictionary<int, NetDevice>();
  public static readonly List<NetDevice> all = new List<NetDevice>();

  // Set around code that creates devices that must not announce themselves one by one
  // (network spawns, whole-patch loads; NetSync shares those as a patch).
  public static bool silentCreate;

  bool started;
  handle[] handles;
  float sendTimer;
  bool wasGrabbed;

  bool hasRemoteTarget;
  Vector3 targetPos, targetScale;
  Quaternion targetRot;

  public bool IsDevice { get { return CompareTag("instrument") && GetComponent<deviceInterface>() != null; } }

  void Awake() {
    fromNetwork = silentCreate;
    all.Add(this);
  }

  void Start() {
    started = true;
    if (!fromNetwork && IsDevice) NetSync.LocalDeviceCreated(this);
  }

  void OnDestroy() {
    all.Remove(this);
    if (netId != 0 && byId.TryGetValue(netId, out NetDevice d) && d == this) byId.Remove(netId);
    if (started && !silentDestroy && netId != 0 && !NetSync.quitting) NetSync.LocalDeviceDeleted(this);
  }

  public void SetId(int id) {
    if (netId != 0 && byId.TryGetValue(netId, out NetDevice d) && d == this) byId.Remove(netId);
    netId = id;
    if (id != 0) byId[id] = this;
  }

  // ---- movement ----

  public bool LocallyGrabbed() {
    if (handles == null) handles = GetComponentsInChildren<handle>(true);
    foreach (handle h in handles) if (h != null && h.curState == manipObject.manipState.grabbed) return true;
    return false;
  }

  public void SetRemotePose(Vector3 pos, Quaternion rot, Vector3 scale) {
    if (LocallyGrabbed()) return; // we're holding it: ours wins until we let go
    targetPos = pos; targetRot = rot; targetScale = scale;
    hasRemoteTarget = true;
  }

  void LateUpdate() {
    if (!NetSync.Active || netId == 0) return;

    bool grabbed = LocallyGrabbed();
    if (grabbed) {
      hasRemoteTarget = false;
      sendTimer += Time.unscaledDeltaTime;
      if (sendTimer >= 1f / NetSync.poseRate) { sendTimer = 0; NetSync.LocalDevicePose(this); }
    } else if (wasGrabbed) {
      NetSync.LocalDevicePose(this, true); // final resting pose, reliable
    }
    wasGrabbed = grabbed;

    if (hasRemoteTarget) {
      float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
      transform.position = Vector3.Lerp(transform.position, targetPos, k);
      transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
      transform.localScale = Vector3.Lerp(transform.localScale, targetScale, k);
      if ((transform.position - targetPos).sqrMagnitude < 1e-8f && Quaternion.Angle(transform.rotation, targetRot) < .01f) {
        transform.SetPositionAndRotation(targetPos, targetRot);
        transform.localScale = targetScale;
        hasRemoteTarget = false;
      }
    }
  }
}
