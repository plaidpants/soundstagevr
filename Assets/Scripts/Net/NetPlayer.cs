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
  public float leftScale, rightScale; // hand size (tracked hands)
  public Vector3[] leftTips, rightTips; // fingertips relative to the hand (for the PC, which can't build the hand mesh)
}

public class NetPlayer : NetworkBehaviour {
  public Transform head, leftHand, rightHand;
  public Renderer[] parts; // head renderers (tinted; hidden for your own player)

  public static readonly List<NetPlayer> all = new List<NetPlayer>();

  static readonly Color[] palette = {
    new Color(1f, .45f, .2f), new Color(.25f, .7f, 1f), new Color(.5f, 1f, .35f), new Color(1f, .35f, .8f),
    // (2026-11-26) up to 8 players on local Wi-Fi
    new Color(1f, .9f, .25f), new Color(.65f, .45f, 1f), new Color(.2f, 1f, .85f), new Color(1f, .3f, .3f)
  };

  [SyncVar(hook = nameof(OnColor))] public Color color = Color.white;
  // (2026-11-27) this player is on a PC: it never sleeps, so it's first in line to take
  // over as host (NetSync.SendSuccession)
  [SyncVar] public bool isPc;

  [Command]
  void CmdSetPc(bool pc) {
    isPc = pc;
    NetSync.SendSuccession(); // the line may change
  }

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
    // the first colour no other player has (connection IDs aren't 0, 1, 2..., so taking
    // one modulo the palette could give two players the same colour)
    int pick = 0;
    for (int i = 0; i < palette.Length; i++) {
      bool used = false;
      foreach (NetPlayer p in all) if (p != null && p != this && p.color == palette[i]) used = true;
      if (!used) { pick = i; break; }
      pick = (i + 1) % palette.Length;
    }
    color = palette[pick];
  }

  public override void OnStartClient() {
    ApplyColor();
    if (!isLocalPlayer) {
      leftNet = NetHand.Create(leftHand, true);
      rightNet = NetHand.Create(rightHand, false);
    }
    Debug.Log("[NetPlayer] " + (isLocalPlayer ? "you" : "player") + " joined (" + all.Count + " in session)");
    // chime for someone arriving after we're in; the players already there when we
    // join spawn alongside us and stay quiet (we get one chime of our own instead)
    if (!isLocalPlayer && NetworkClient.localPlayer != null && Time.unscaledTime > quietUntil) UISound.PlayerJoined();
  }

  static float quietUntil;

  public override void OnStopClient() {
    // only when they left a session we're still in: not when we leave, the host goes
    // away or the session is being torn down
    if (isLocalPlayer || !NetworkClient.isConnected || NetworkClient.localPlayer == null) return;
    Debug.Log("[NetPlayer] player left (" + (all.Count - 1) + " in session)");
    UISound.PlayerLeft();
  }

  public override void OnStartLocalPlayer() {
    CmdSetPc(Application.platform != RuntimePlatform.Android);
    quietUntil = Time.unscaledTime + 2f; // the others' objects are still spawning
    if (!NetworkServer.active) UISound.PlayerJoined(); // we joined someone (a host starting alone stays quiet)
    foreach (Renderer r in parts) if (r != null) r.enabled = false;
  }

  void OnColor(Color oldColor, Color newColor) { ApplyColor(); }

  void ApplyColor() {
    if (block == null) block = new MaterialPropertyBlock();
    block.SetColor("_Color", color);
    foreach (Renderer r in parts) if (r != null) r.SetPropertyBlock(block);
  }

  // Passthrough shows the camera feed wherever the picture's alpha is 0, which the
  // avatars' glow materials leave it: an extra material on every renderer of a remote
  // player marks it solid in alpha only (Resources/Net/NetAlphaWrite.shader), so other
  // players stay visible in passthrough. Checked now and then (hands are built later).
  static Material alphaWrite;
  readonly HashSet<Renderer> solid = new HashSet<Renderer>();
  float solidTimer = 1;
  void MarkSolid() {
    solidTimer += Time.unscaledDeltaTime;
    if (solidTimer < 1) return;
    solidTimer = 0;
    if (alphaWrite == null) {
      Shader sh = Resources.Load<Shader>("Net/NetAlphaWrite");
      if (sh == null) return;
      alphaWrite = new Material(sh);
    }
    foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) {
      if (r == null || solid.Contains(r) || r is ParticleSystemRenderer) continue;
      Material[] ms = r.sharedMaterials;
      if (System.Array.IndexOf(ms, alphaWrite) >= 0) { solid.Add(r); continue; }
      Material[] more = new Material[ms.Length + 1];
      ms.CopyTo(more, 0);
      more[ms.Length] = alphaWrite;
      r.sharedMaterials = more;
      solid.Add(r);
    }
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

  // A remote player whose poses stop arriving (headset taken off or switched to Link, app
  // paused) still counts as connected until the 30 s timeout. Hide their head and hands
  // after a moment instead of leaving them frozen in place - possibly right in front of
  // you, if it's your own headset now on Link. They reappear with the next pose.
  const float StaleAfter = 1.5f;
  float lastPoseAt;
  bool stale;

  void LateUpdate() {
    if (isLocalPlayer) { LocalUpdate(); return; }
    if (!headPredict.Has) return;
    if (!stale && Time.realtimeSinceStartup - lastPoseAt > StaleAfter) {
      stale = true;
      foreach (Renderer r in parts) if (r != null) r.enabled = false;
      if (leftNet != null) leftNet.Show(false, false);
      if (rightNet != null) rightNet.Show(false, false);
      Debug.Log("[NetPlayer] no poses from a player for " + StaleAfter + " s: hiding their avatar");
    }
    if (stale) return;
    MarkSolid();
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
    // over the internet relay (metered bandwidth) 12 poses a second instead of 20;
    // prediction covers the gaps, and the link's own delay is far bigger anyway
    float rate = Transport.active is UtpRelayTransport ? Mathf.Min(sendRate, 12f) : sendRate;
    if (sendTimer < 1f / rate) return;
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
      p.leftBones = ReadBones(OVRPlugin.Hand.HandLeft, ref p.leftOn, out p.leftScale);
      p.rightBones = ReadBones(OVRPlugin.Hand.HandRight, ref p.rightOn, out p.rightScale);
      p.leftTips = ReadTips(0, l);
      p.rightTips = ReadTips(1, r);
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

  static readonly Vector3[][] tipBuf = { new Vector3[5], new Vector3[5] };
  static Vector3[] ReadTips(int index, Transform hand) {
    if (hand == null) return null;
    foreach (HandFingertips hf in HandFingertips.active) {
      if (hf.owner == null || hf.owner.ControllerIndex != index) continue;
      System.Collections.Generic.IList<Transform> pts = hf.HitPoints;
      if (pts.Count < 5) return null;
      for (int i = 0; i < 5; i++) tipBuf[index][i] = pts[i] != null ? hand.InverseTransformPoint(pts[i].position) : Vector3.zero;
      return tipBuf[index];
    }
    return null;
  }

  static uint[] ReadBones(OVRPlugin.Hand which, ref bool on, out float scale) {
    scale = 1f;
    if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, which, ref handState) ||
        (handState.Status & OVRPlugin.HandStatus.HandTracked) == 0 ||
        handState.BoneRotations == null) {
      on = false;
      return null;
    }
    scale = handState.HandScale;
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
    lastPoseAt = Time.realtimeSinceStartup;
    if (stale) {
      stale = false;
      foreach (Renderer r in parts) if (r != null) r.enabled = true;
    }
    headPredict.Feed(p.headPos, Compression.DecompressQuaternion(p.headRot), p.time);
    leftPredict.Feed(p.leftPos, Compression.DecompressQuaternion(p.leftRot), p.time);
    rightPredict.Feed(p.rightPos, Compression.DecompressQuaternion(p.rightRot), p.time);
    if (p.handTracking) {
      leftNet.SetScale(p.leftScale); rightNet.SetScale(p.rightScale);
      leftNet.SetTips(p.leftTips); rightNet.SetTips(p.rightTips);
    }
    leftNet.SetBones(p.leftBones);
    rightNet.SetBones(p.rightBones);
    leftNet.Show(p.handTracking, p.leftOn);
    rightNet.Show(p.handTracking, p.rightOn);
  }
}
