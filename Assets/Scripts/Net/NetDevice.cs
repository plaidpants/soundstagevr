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
  Vector3 targetScale;
  readonly PosePredictor predict = new PosePredictor(); // NetPredict.cs

  public bool IsDevice { get { return CompareTag("instrument") && GetComponent<deviceInterface>() != null; } }

  void Awake() {
    fromNetwork = silentCreate;
    all.Add(this);
    BuildTools();
  }

  // Hand-held parts (drumsticks, maracas, mic grip), listed once while they're all
  // still on the device: a held one is re-parented to the hand, so it can't be found
  // under the device then. Same prefab, same order on every headset (NetTools).
  [System.NonSerialized] public List<manipObject> tools;
  public static readonly Dictionary<manipObject, NetDevice> toolOwner = new Dictionary<manipObject, NetDevice>();

  // From Awake (a maraca or mic pulled from the menu is in the hand - re-parented -
  // before Start), and again in Start for anything set up in between.
  void BuildTools() {
    if (tools == null) tools = new List<manipObject>();
    foreach (manipObject m in GetComponentsInChildren<manipObject>(true)) {
      if (!NetTools.IsTool(m) || tools.Contains(m)) continue;
      tools.Add(m);
      toolOwner[m] = this;
    }
  }

  void Start() {
    started = true;
    BuildTools();
    if (!fromNetwork && IsDevice) NetSync.LocalDeviceCreated(this);
  }

  void OnDestroy() {
    all.Remove(this);
    if (tools != null) foreach (manipObject m in tools) if (m != null) toolOwner.Remove(m);
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

  public void SetRemotePose(Vector3 pos, Quaternion rot, Vector3 scale, bool held, double time) {
    if (LocallyGrabbed()) return; // we're holding it: ours wins until we let go
    // start from where it is here, so the first pose eases in
    if (!predict.Has) predict.Feed(transform.position, transform.rotation, time - .001, false);
    predict.Feed(pos, rot, time, held);
    targetScale = scale;
    hasRemoteTarget = true;
    SetRemotelyHeld(held);
  }

  // Someone else is moving it: its handles can't be grabbed here until they let go
  // (or nothing has been heard for 2 s, in case the release message never comes).
  bool remotelyHeld;
  float remoteHeldTime;

  void SetRemotelyHeld(bool on) {
    if (on) remoteHeldTime = Time.unscaledTime;
    if (on == remotelyHeld) return;
    remotelyHeld = on;
    if (handles == null) handles = GetComponentsInChildren<handle>(true);
    NetSync.SetGrabBlocked(handles, on);
  }

  // ---- content that no control message carries ----
  // Some devices hold content edited in ways that aren't individual controls (notes
  // drawn into a timeline, the keyboard's and the xylophone's piano rolls). Once a second their saved data
  // is compared with what it was; a change that no network message explains (applying
  // or sending one resets the comparison) means a player here edited it, and the whole
  // patch is shared again.
  static readonly HashSet<menuItem.deviceType> contentTypes = new HashSet<menuItem.deviceType> {
    menuItem.deviceType.Timeline, menuItem.deviceType.XyloRoll,
    menuItem.deviceType.Keyboard // its built-in sequencer is a timeline
  };
  float contentTimer;
  int contentKind; // 0 unknown, 1 checked, 2 not a content device
  int contentHash;
  bool haveContentHash;

  public void Rebaseline() { haveContentHash = false; }

  void CheckContent() {
    contentTimer += Time.unscaledDeltaTime;
    if (contentTimer < 1f) return;
    contentTimer = 0;
    deviceInterface di = GetComponent<deviceInterface>();
    if (di == null) return;
    if (contentKind == 0) contentKind = contentTypes.Contains(di.GetData().deviceType) ? 1 : 2;
    if (contentKind != 1) return; // not a device with free-form content
    foreach (manipObject m in GetComponentsInChildren<manipObject>(true))
      if (m.curState == manipObject.manipState.grabbed) return; // wait until they let go
    int h = NetSync.ContentHash(di, out _);
    if (!haveContentHash) { contentHash = h; haveContentHash = true; return; }
    if (h == contentHash) return;
    contentHash = h;
    Debug.Log("[NetDevice] " + name + " content changed here: sharing the patch");
    NetSync.LocalPatchReplaced();
  }

  void LateUpdate() {
    if (remotelyHeld && (!NetSync.Active || Time.unscaledTime - remoteHeldTime > 2f)) SetRemotelyHeld(false);
    if (!NetSync.Active || netId == 0) return;
    CheckContent();

    bool grabbed = LocallyGrabbed();
    if (grabbed) {
      hasRemoteTarget = false;
      predict.Reset();
      sendTimer += Time.unscaledDeltaTime;
      if (sendTimer >= 1f / NetSync.poseRate) { sendTimer = 0; NetSync.LocalDevicePose(this); }
    } else if (wasGrabbed) {
      NetSync.LocalDevicePose(this, true); // final resting pose, reliable
    }
    wasGrabbed = grabbed;

    if (hasRemoteTarget) {
      float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
      predict.Apply(transform, Time.deltaTime); // carried forward to now while it's moving
      NetSync.SetWorldScale(transform, Vector3.Lerp(transform.lossyScale, targetScale, k));
      if (predict.Settled && (transform.lossyScale - targetScale).sqrMagnitude < 1e-8f) {
        NetSync.SetWorldScale(transform, targetScale);
        predict.Reset();
        hasRemoteTarget = false;
      }
    }
  }
}
