// A player in a multiplayer session (2026-11, milestone 0).
//
// Head: SoundStage's headset model, tinted with the player's colour. Sent with the
// hands (the prefab's NetworkTransform on the Head is switched off: its buffering
// showed the head ~100 ms late). Head and hands are shown where they should be now,
// carried forward from their last pose by PosePredictor (NetPredict.cs).
// Hands: what the player really uses - the glowing tracked hands (with finger poses) or
// the controller models - see NetHand. Sent by this script ~20 times a second,
// unreliable: the owner sends a HandsPacket to the server, which passes it on to the
// other players (the host sends straight to the others). Fingers are 24 compressed
// quaternions per hand, ~4 KB/s per player.
//
// Poses are in world space. For players in the same physical room, the room alignment
// step (planned) moves each headset's rig so world space matches; nothing here changes.

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct HandsPacket {
  public double time; // sender's network time when sampled
  public Vector3 headPos;
  public uint headRot;
  public bool handTracking;
  public bool leftOn, rightOn;
  public Vector3 leftPos, rightPos;
  public uint leftRot, rightRot;
  public uint[] leftBones, rightBones;
}

public class NetPlayer : NetworkBehaviour {
  public Transform head, leftHand, rightHand;
  public Renderer[] parts; // head renderers (tinted; hidden for your own player)

  public static readonly List<NetPlayer> all = new List<NetPlayer>();

  static readonly Color[] palette = {
    new Color(1f, .45f, .2f), new Color(.25f, .7f, 1f), new Color(.5f, 1f, .35f), new Color(1f, .35f, .8f)
  };

  [SyncVar(hook = nameof(OnColor))] public Color color = Color.white;

  public float sendRate = 20f;

  OVRCameraRig rig;
  MaterialPropertyBlock block;
  manipulator[] manips;
  NetHand leftNet, rightNet;
  float sendTimer;

  // remote side
  readonly PosePredictor headPredict = new PosePredictor(), leftPredict = new PosePredictor(), rightPredict = new PosePredictor();

  // local side, reused buffers
  static OVRPlugin.HandState handState = new OVRPlugin.HandState();

  void Awake() {
    foreach (NetworkTransformBase nt in GetComponentsInChildren<NetworkTransformBase>(true)) nt.enabled = false;
  }

  void OnEnable() { all.Add(this); }
  void OnDisable() { all.Remove(this); }

  public override void OnStartServer() {
    color = palette[(connectionToClient != null ? connectionToClient.connectionId : 0) % palette.Length];
  }

  public override void OnStartClient() {
    ApplyColor();
    if (!isLocalPlayer) {
      leftNet = NetHand.Create(leftHand, true);
      rightNet = NetHand.Create(rightHand, false);
    }
    Debug.Log("[NetPlayer] " + (isLocalPlayer ? "you" : "player") + " joined (" + all.Count + " in session)");
  }

  public override void OnStartLocalPlayer() {
    foreach (Renderer r in parts) if (r != null) r.enabled = false;
  }

  void OnColor(Color oldColor, Color newColor) { ApplyColor(); }

  void ApplyColor() {
    if (block == null) block = new MaterialPropertyBlock();
    block.SetColor("_Color", color);
    foreach (Renderer r in parts) if (r != null) r.SetPropertyBlock(block);
  }

  // The camera device (recording / broadcast) shows everyone's headset as the plain
  // grey original, not the player colours: the tint is taken off just for its renders.
  static readonly Dictionary<Camera, bool> isRecordingCam = new Dictionary<Camera, bool>();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void HookCameras() {
    Camera.onPreRender += cam => { if (IsRecordingCam(cam)) Tint(false); };
    Camera.onPostRender += cam => { if (IsRecordingCam(cam)) Tint(true); };
  }

  static bool IsRecordingCam(Camera cam) {
    if (cam == null || all.Count == 0) return false;
    if (!isRecordingCam.TryGetValue(cam, out bool rec)) {
      rec = cam.GetComponentInParent<cameraDeviceInterface>() != null;
      isRecordingCam[cam] = rec;
    }
    return rec;
  }

  static void Tint(bool on) {
    foreach (NetPlayer p in all) {
      if (p == null || p.isLocalPlayer) continue;
      if (on) p.ApplyColor();
      else foreach (Renderer r in p.parts) if (r != null) r.SetPropertyBlock(null);
    }
  }

  // ---------- local player: read and send ----------

  void LateUpdate() {
    if (isLocalPlayer) { LocalUpdate(); return; }
    if (!headPredict.Has) return;
    float dt = Time.deltaTime;
    headPredict.Apply(head, dt);
    leftPredict.Apply(leftHand, dt);
    rightPredict.Apply(rightHand, dt);
  }

  void LocalUpdate() {
    if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
    Transform h = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
    if (h != null) head.SetPositionAndRotation(h.position, h.rotation);

    sendTimer += Time.unscaledDeltaTime;
    if (sendTimer < 1f / sendRate) return;
    sendTimer = 0;

    HandsPacket p = new HandsPacket();
    p.time = NetworkTime.time;
    p.headPos = head.position;
    p.headRot = Compression.CompressQuaternion(head.rotation);
    p.handTracking = HandInput.HandsActive();
    Transform l = HandSource(0, p.handTracking), r = HandSource(1, p.handTracking);
    if (l != null) { p.leftOn = l.gameObject.activeInHierarchy; p.leftPos = l.position; p.leftRot = Compression.CompressQuaternion(l.rotation); }
    if (r != null) { p.rightOn = r.gameObject.activeInHierarchy; p.rightPos = r.position; p.rightRot = Compression.CompressQuaternion(r.rotation); }
    if (p.handTracking) {
      p.leftBones = ReadBones(OVRPlugin.Hand.HandLeft, ref p.leftOn);
      p.rightBones = ReadBones(OVRPlugin.Hand.HandRight, ref p.rightOn);
    }

    if (isServer) RpcHands(p);
    else CmdHands(p);

    // drumsticks, maracas, mic grip held in either hand
    if (manips == null || manips.Length < 2 || manips[0] == null) HandSource(0, false);
    NetTools.LocalHands(manips != null ? manips[0] : null, manips != null && manips.Length > 1 ? manips[1] : null);
  }

  // Where this player's hand / controller visual is: in hand tracking the rig's hand
  // anchor (the hand model sits on it); with controllers the controller model itself
  // (it carries the controller calibration offset).
  Transform HandSource(int index, bool handTracking) {
    if (handTracking) return rig == null ? null : (index == 0 ? rig.leftHandAnchor : rig.rightHandAnchor);
    if (manips == null || manips.Length < 2 || manips[0] == null) {
      manips = new manipulator[2];
      foreach (manipulator m in FindObjectsOfType<manipulator>(true)) {
        int i = m.ControllerIndex;
        if (i == 0 || i == 1) manips[i] = m;
      }
    }
    manipulator mp = manips[index];
    return mp != null && mp.controllerRep != null ? mp.controllerRep.transform : null;
  }

  static uint[] ReadBones(OVRPlugin.Hand which, ref bool on) {
    if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, which, ref handState) ||
        (handState.Status & OVRPlugin.HandStatus.HandTracked) == 0 ||
        handState.BoneRotations == null) {
      on = false;
      return null;
    }
    uint[] packed = new uint[handState.BoneRotations.Length];
    for (int i = 0; i < packed.Length; i++) {
      OVRPlugin.Quatf q = handState.BoneRotations[i];
      packed[i] = Compression.CompressQuaternion(new Quaternion(q.x, q.y, q.z, q.w));
    }
    return packed;
  }

  // ---------- network ----------

  [Command(channel = Channels.Unreliable)]
  void CmdHands(HandsPacket p) {
    if (!isLocalPlayer) ApplyHands(p); // the host's copy of this player
    RpcHands(p);
  }

  [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
  void RpcHands(HandsPacket p) {
    if (isServer && !isLocalPlayer) return; // host already applied it in CmdHands
    ApplyHands(p);
  }

  // ---------- remote player: show ----------

  void ApplyHands(HandsPacket p) {
    if (isLocalPlayer || leftNet == null) return;
    headPredict.Feed(p.headPos, Compression.DecompressQuaternion(p.headRot), p.time);
    leftPredict.Feed(p.leftPos, Compression.DecompressQuaternion(p.leftRot), p.time);
    rightPredict.Feed(p.rightPos, Compression.DecompressQuaternion(p.rightRot), p.time);
    leftNet.SetBones(p.leftBones);
    rightNet.SetBones(p.rightBones);
    leftNet.Show(p.handTracking, p.leftOn);
    rightNet.Show(p.handTracking, p.rightOn);
  }
}
