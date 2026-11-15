// A player in a multiplayer session (2026-11, milestone 0).
//
// Head: SoundStage's headset model, tinted with the player's colour, moved by a
// NetworkTransformUnreliable (client-to-server) on the Head child.
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
  public float smoothing = 18f; // how quickly remote hands catch up with the last pose

  OVRCameraRig rig;
  MaterialPropertyBlock block;
  manipulator[] manips;
  NetHand leftNet, rightNet;
  float sendTimer;

  // remote side
  bool haveTarget;
  Vector3 leftTargetPos, rightTargetPos;
  Quaternion leftTargetRot = Quaternion.identity, rightTargetRot = Quaternion.identity;

  // local side, reused buffers
  static OVRPlugin.HandState handState = new OVRPlugin.HandState();

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

  // ---------- local player: read and send ----------

  void LateUpdate() {
    if (isLocalPlayer) { LocalUpdate(); return; }
    if (!haveTarget) return;
    float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
    leftHand.position = Vector3.Lerp(leftHand.position, leftTargetPos, k);
    leftHand.rotation = Quaternion.Slerp(leftHand.rotation, leftTargetRot, k);
    rightHand.position = Vector3.Lerp(rightHand.position, rightTargetPos, k);
    rightHand.rotation = Quaternion.Slerp(rightHand.rotation, rightTargetRot, k);
  }

  void LocalUpdate() {
    if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
    Transform h = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
    if (h != null) head.SetPositionAndRotation(h.position, h.rotation);

    sendTimer += Time.unscaledDeltaTime;
    if (sendTimer < 1f / sendRate) return;
    sendTimer = 0;

    HandsPacket p = new HandsPacket();
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
    leftTargetPos = p.leftPos; leftTargetRot = Compression.DecompressQuaternion(p.leftRot);
    rightTargetPos = p.rightPos; rightTargetRot = Compression.DecompressQuaternion(p.rightRot);
    if (!haveTarget) {
      leftHand.SetPositionAndRotation(leftTargetPos, leftTargetRot);
      rightHand.SetPositionAndRotation(rightTargetPos, rightTargetRot);
      haveTarget = true;
    }
    leftNet.SetBones(p.leftBones);
    rightNet.SetBones(p.rightBones);
    leftNet.Show(p.handTracking, p.leftOn);
    rightNet.Show(p.handTracking, p.rightOn);
  }
}
