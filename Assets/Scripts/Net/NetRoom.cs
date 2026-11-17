// Same-room play (2026-11, milestone 2b).
//
// Players in one physical room align their headsets to it, so they see each other's
// avatars on the real people and the devices in the same real places. Everyone else
// (in another room, or another country) stays in their own space and moves as before.
//
// ALIGN ROOM (settings panel): touch two spots in the room - two corners of a table,
// two pieces of tape on the floor - with the right controller tip (B to mark each) or,
// with hands, the right index fingertip (pinch to mark). A tone and a marker show each
// spot; the two markers stay in the room while you're in it. Two spots give the
// position and the direction; only position and turn are matched (the floor is already
// level on every headset).
// - The first to align sets where the room is in the shared world.
// - Anyone aligning after that is moved so their two spots land on the first player's.
// While in the room the stick (movement and snap turn) is off: moving on your own would
// put you out of line with the people next to you. MOVE ROOM turns it back on for a
// moment, and then the stick moves the whole room - everyone in it moves with you, so
// you all stay in line. LEAVE ROOM gives you back your own space (and the stick).
//
// Shared: the room's pose in the world (NetRoomMsg, via the host, which also gives it to
// players who join later). The host decides: if two players set up a room at the same
// time, the host keeps the first and sends it to the other, which then aligns to it. Each headset in the room keeps where its rig sits relative to
// the room, so a moved room puts every member's rig back in the same relative place.

using Mirror;
using UnityEngine;

public struct NetRoomMsg : NetworkMessage {
  public byte kind;    // 0 the room as it is (from the host), 1 new room here, 2 moved, 3 gone,
                       // 4 I'm in the room, 5 I left it
  public Vector3 pos;  // half way between the two spots
  public float yaw;    // direction from the first to the second
  public Vector3 a, b; // the two spots, relative to that
}

public class NetRoom : MonoBehaviour {
  public static NetRoom instance;
  public static event System.Action Changed;

  // the room, as everyone knows it
  public static bool RoomExists { get; private set; }
  static Vector3 roomPos;
  static float roomYaw;
  static Vector3 roomA = new Vector3(0, 0, -.5f), roomB = new Vector3(0, 0, .5f); // the spots, in the room
  static float RoomSpan { get { Vector3 d = roomB - roomA; d.y = 0; return d.magnitude; } }

  // this headset
  public static bool InRoom { get; private set; }
  public static bool MoveUnlocked { get; private set; }
  static Vector3 rigInRoomPos; // our rig relative to the room
  static float rigInRoomYaw;

  // the stick (ArtificialVRMovement, SnapTurn) is off while in the room, unless moving it
  public static bool StickBlocked { get { return InRoom && !MoveUnlocked; } }

  enum Step { None, First, Second }
  Step step = Step.None;
  Vector3 spotA;
  bool pressedLast;
  OVRCameraRig rig;
  manipulator right;
  GameObject prompt, markerA, roomMarkers;
  AudioSource beep;
  AudioClip beepLow, beepHigh;
  TextMesh promptText;
  float sendTimer;
  bool movedSinceSend;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetRoom");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetRoom>();
  }

  public static string StatusText() {
    if (instance != null && instance.step != Step.None) return "ROOM: MARK THE SPOTS...";
    if (InRoom) return "LEAVE ROOM";
    return RoomExists ? "ALIGN TO ROOM" : "ALIGN ROOM";
  }
  public static string MoveText() {
    if (!InRoom) return "MOVE ROOM (ALIGN FIRST)";
    return MoveUnlocked ? "MOVE ROOM: STICK ON" : "MOVE ROOM: STICK OFF";
  }

  // settings panel buttons
  public static void AlignButton() {
    if (instance == null) return;
    if (instance.step != Step.None) { instance.CancelAlign(); return; }
    if (InRoom) { Leave(); return; }
    instance.StartAlign();
  }
  public static void MoveButton() {
    if (!InRoom) return;
    MoveUnlocked = !MoveUnlocked;
    if (!MoveUnlocked && instance != null) instance.SendRoom(2); // where it ended up
    Changed?.Invoke();
  }

  public static void Leave() {
    bool was = InRoom;
    InRoom = false;
    MoveUnlocked = false;
    if (instance != null) instance.PlaceMarkers();
    if (was) Membership(false);
    Debug.Log("[NetRoom] left the room: own space, stick on");
    Changed?.Invoke();
  }

  // ---- marking the two spots ----

  // the pinch / press that hit ALIGN ROOM mustn't also mark spot 1: marking only starts
  // once that's been let go and a moment has passed
  float armedAt;
  void StartAlign() {
    pressedLast = true;               // a press already down doesn't count
    armedAt = Time.unscaledTime + .6f;
    step = Step.First;
    ShowPrompt(RoomExists ? "ROOM ALIGN\nTouch the room's spot 1 (the one the first player touched first)\nwith your right controller tip and press B  (hands: index fingertip, pinch)" : "ROOM ALIGN\nTouch spot 1 with your right controller tip\nand press B  (hands: right index fingertip, then pinch)");
    Changed?.Invoke();
  }

  void CancelAlign() {
    step = Step.None;
    HidePrompt();
    Changed?.Invoke();
  }

  bool Pressed() {
    bool now;
    if (HandInput.HandsActive()) now = HandInput.Get(1).pinchIndex > (pressedLast ? .5f : .85f);
    else if (!OVRSafe.Available) now = Input.GetKey(KeyCode.Delete);
    else now = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch); // B (A opens the menu)
    bool edge = now && !pressedLast;
    pressedLast = now;
    if (Time.unscaledTime < armedAt) return false;
    return edge;
  }

  Vector3 TipPosition() {
    if (right == null) {
      foreach (manipulator m in FindObjectsOfType<manipulator>(true)) if (m.ControllerIndex == 1) right = m;
    }
    if (HandInput.HandsActive()) {
      // the right index fingertip (HandFingertips: thumb, index, ... then the palm)
      foreach (HandFingertips hf in HandFingertips.active)
        if (hf.owner == right && hf.indexTip != null) return hf.indexTip.position;
    }
    return right != null ? right.transform.position : Vector3.zero;
  }

  void Update() {
    if (rig == null) rig = FindObjectOfType<OVRCameraRig>();
    if (step != Step.None) UpdateAlign();
    if (InRoom && MoveUnlocked) UpdateMoving();
  }

  // where the tip was a moment ago: pinching pulls the index fingertip away from the spot
  readonly Vector3[] tipHistory = new Vector3[64];
  readonly float[] tipTimes = new float[64];
  int tipNext;
  Vector3 TipBefore(float seconds) {
    float t = Time.unscaledTime - seconds;
    Vector3 best = tipHistory[(tipNext + 63) % 64];
    for (int i = 1; i <= 64; i++) {
      int k = (tipNext - i + 128) % 64;
      if (tipTimes[k] == 0) break;
      best = tipHistory[k];
      if (tipTimes[k] <= t) break;
    }
    return best;
  }

  void UpdateAlign() {
    Vector3 tip = TipPosition();
    tipHistory[tipNext] = tip; tipTimes[tipNext] = Time.unscaledTime; tipNext = (tipNext + 1) % 64;
    if (HandInput.HandsActive()) tip = TipBefore(.3f);
    if (prompt != null && Camera.main != null) {
      prompt.transform.position = tip + Vector3.up * .12f;
      prompt.transform.rotation = Quaternion.LookRotation(prompt.transform.position - Camera.main.transform.position);
    }
    if (!Pressed()) return;
    if (step == Step.First) {
      spotA = tip;
      markerA = Marker(spotA);
      Beep(false);
      step = Step.Second;
      ShowPrompt(RoomExists ? "ROOM ALIGN\nNow the room's spot 2 - press B / pinch" : "ROOM ALIGN\nNow touch spot 2 and press B / pinch\n(at least half a metre away; a metre or more is better)");
      return;
    }
    Vector3 spotB = tip;
    Vector3 d = spotB - spotA; d.y = 0;
    if (d.magnitude < .3f) { ShowPrompt("ROOM ALIGN\nToo close to the first spot -\ntouch one at least half a metre away, press B / pinch"); return; }
    if (RoomExists && Mathf.Abs(d.magnitude - RoomSpan) > .25f) {
      // not the room's two spots (or a bad touch): try again rather than align wrongly
      Beep(false);
      Debug.Log("[NetRoom] spots " + d.magnitude.ToString("F2") + " m apart, the room's are " + RoomSpan.ToString("F2") + " m: not aligned");
      if (markerA != null) Destroy(markerA);
      step = Step.First;
      ShowPrompt("ROOM ALIGN\nThose spots are " + d.magnitude.ToString("F2") + " m apart, the room's are " + RoomSpan.ToString("F2") + " m.\nTouch the SAME two spots (first, then second) - press B / pinch");
      return;
    }
    step = Step.None;
    HidePrompt();
    Beep(true);
    Aligned(spotA, spotB);
  }

  // the two spots, measured in our current world. The room's frame sits half way between
  // them (an error in either touch counts half), facing from the first to the second.
  void Aligned(Vector3 a, Vector3 b) {
    if (rig == null) return;
    // same spots touched in the other order? If the room's spots differ in height, that
    // tells which is which (otherwise the order is what counts - the prompt says so)
    float roomDy = roomB.y - roomA.y, myDy = b.y - a.y;
    if (RoomExists && Mathf.Abs(roomDy) > .1f && Mathf.Abs(myDy) > .1f && Mathf.Sign(roomDy) != Mathf.Sign(myDy)) {
      Vector3 t = a; a = b; b = t;
      Debug.Log("[NetRoom] spots touched in the other order: swapped");
    }
    Vector3 d = b - a; d.y = 0;
    Vector3 measuredPos = (a + b) * .5f;
    float measuredYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    // our rig relative to what we measured
    Vector3 lp; float ly;
    ToLocal(measuredPos, measuredYaw, rig.transform.position, rig.transform.eulerAngles.y, out lp, out ly);
    if (!RoomExists) {
      // the first: the room is where we measured it
      roomPos = measuredPos; roomYaw = measuredYaw; RoomExists = true;
      if (NetworkServer.active) members.Add(HostId);
      roomA = Quaternion.Euler(0, -roomYaw, 0) * (a - roomPos);
      roomB = Quaternion.Euler(0, -roomYaw, 0) * (b - roomPos);
      Debug.Log("[NetRoom] room set here (spots " + d.magnitude.ToString("F2") + " m apart)");
      SendRoom(1);
    } else {
      Debug.Log("[NetRoom] aligned to the room (spots " + d.magnitude.ToString("F2") + " m apart here, " + RoomSpan.ToString("F2") + " m for the room)");
      Membership(true);
    }
    rigInRoomPos = lp; rigInRoomYaw = ly;
    InRoom = true;
    MoveUnlocked = false;
    PlaceRig();
    Changed?.Invoke();
  }

  // ---- the room's pose ----

  static void ToLocal(Vector3 framePos, float frameYaw, Vector3 pos, float yaw, out Vector3 lp, out float ly) {
    lp = Quaternion.Euler(0, -frameYaw, 0) * (pos - framePos);
    ly = yaw - frameYaw;
  }

  void PlaceRig() {
    if (rig == null) return;
    rig.transform.position = roomPos + Quaternion.Euler(0, roomYaw, 0) * rigInRoomPos;
    rig.transform.rotation = Quaternion.Euler(0, roomYaw + rigInRoomYaw, 0);
    PlaceMarkers();
  }

  // the room's two spots, shown while in the room
  void PlaceMarkers() {
    if (!InRoom) { if (roomMarkers != null) roomMarkers.SetActive(false); return; }
    if (roomMarkers == null) {
      roomMarkers = new GameObject("RoomMarkers");
      DontDestroyOnLoad(roomMarkers);
      for (int i = 0; i < 2; i++) {
        GameObject m = Marker(Vector3.zero);
        m.transform.SetParent(roomMarkers.transform, false);
        TextMesh t = Label(m.transform, i == 0 ? "1" : "2");
        t.transform.localPosition = Vector3.up * 2.5f; // above the dot (in dot sizes)
      }
    }
    roomMarkers.SetActive(true);
    roomMarkers.transform.SetPositionAndRotation(roomPos, Quaternion.Euler(0, roomYaw, 0));
    roomMarkers.transform.GetChild(0).localPosition = roomA;
    roomMarkers.transform.GetChild(1).localPosition = roomB;
  }

  // stick on: our rig moved -> the room moves with it (and everyone in it)
  void UpdateMoving() {
    if (rig == null) return;
    float yaw = rig.transform.eulerAngles.y - rigInRoomYaw;
    Vector3 pos = rig.transform.position - Quaternion.Euler(0, yaw, 0) * rigInRoomPos;
    if ((pos - roomPos).sqrMagnitude > 1e-8f || Mathf.Abs(Mathf.DeltaAngle(yaw, roomYaw)) > .01f) {
      roomPos = pos; roomYaw = yaw;
      movedSinceSend = true;
      PlaceMarkers();
    }
    sendTimer += Time.unscaledDeltaTime;
    if (movedSinceSend && sendTimer >= .05f) { sendTimer = 0; movedSinceSend = false; SendRoom(2); }
  }

  void SendRoom(byte kind) {
    if (!NetSync.Active) return;
    NetRoomMsg m = Current(); m.kind = kind;
    if (NetworkServer.active) { m.kind = 0; NetSync.SendRoomToRemotes(m, null); }
    else NetworkClient.Send(m);
  }

  // ---- who's in the room (kept by the host) ----
  // When nobody is in the room any more it's forgotten, so the next ALIGN ROOM sets up a
  // new one (to start over after a bad alignment: everyone LEAVE ROOM, then align again).
  static readonly System.Collections.Generic.HashSet<int> members = new System.Collections.Generic.HashSet<int>();
  const int HostId = -1;

  static void Membership(bool inRoom) {
    if (NetworkServer.active) ServerMember(HostId, inRoom);
    else if (NetSync.Active) NetworkClient.Send(new NetRoomMsg { kind = (byte)(inRoom ? 4 : 5) });
    else if (!inRoom) { RoomExists = false; Changed?.Invoke(); } // on our own: nobody else to keep it
  }

  static void ServerMember(int id, bool inRoom) {
    if (inRoom) members.Add(id); else members.Remove(id);
    if (members.Count == 0 && RoomExists) {
      Debug.Log("[NetRoom] nobody in the room: forgotten");
      RoomExists = false;
      NetSync.SendRoomToRemotes(new NetRoomMsg { kind = 3 }, null);
      Changed?.Invoke();
    }
  }

  // host: a player left the session
  public static void ServerDisconnected(NetworkConnectionToClient conn) {
    if (members.Contains(conn.connectionId)) ServerMember(conn.connectionId, false);
  }

  // host just started (new session or taking over): only we are known to be in the room;
  // the others say so when they join (OnJoinedSession)
  public static void ServerStarted() {
    members.Clear();
    if (InRoom) members.Add(HostId);
    else if (RoomExists && instance != null) instance.Invoke(nameof(PurgeIfEmpty), 5f); // give members time to say so
  }

  void PurgeIfEmpty() {
    if (!NetworkServer.active || members.Count > 0 || !RoomExists) return;
    Debug.Log("[NetRoom] nobody in the room after taking over: forgotten");
    RoomExists = false;
    NetSync.SendRoomToRemotes(new NetRoomMsg { kind = 3 }, null);
    Changed?.Invoke();
  }

  // joined a host (first time or after the host left): tell it we're in its room
  public static void OnJoinedSession() {
    if (InRoom && NetSync.Active) NetworkClient.Send(new NetRoomMsg { kind = 4 });
  }

  // host: from a joined player
  public static void OnServer(NetworkConnectionToClient conn, NetRoomMsg m) {
    if (m.kind == 4 || m.kind == 5) { ServerMember(conn.connectionId, m.kind == 4); return; }
    if (m.kind == 1 && RoomExists) {
      // a room already exists (set up here first, or by someone else): that one counts -
      // the sender aligns to it
      Debug.Log("[NetRoom] a second room was set up at the same time: keeping the first");
      members.Add(conn.connectionId);
      conn.Send(Current());
      return;
    }
    if (m.kind == 1) members.Add(conn.connectionId);
    Take(m);
    NetRoomMsg out_ = Current();
    NetSync.SendRoomToRemotes(out_, conn);
  }

  // joined: the room from the host
  public static void Apply(NetRoomMsg m) { Take(m); }

  static void Take(NetRoomMsg m) {
    if (m.kind == 3) { RoomExists = false; if (InRoom) Leave(); Changed?.Invoke(); return; }
    RoomExists = true;
    roomPos = m.pos; roomYaw = m.yaw;
    if (m.a != m.b) { roomA = m.a; roomB = m.b; }
    if (InRoom && instance != null) {
      if (MoveUnlocked && m.kind == 2) MoveUnlocked = false; // someone else is moving the room: follow them
      instance.PlaceRig();
    }
    Changed?.Invoke();
  }

  public static NetRoomMsg Current() { return new NetRoomMsg { kind = 0, pos = roomPos, yaw = roomYaw, a = roomA, b = roomB }; }

  // ---- feedback ----

  void Beep(bool high) {
    if (beep == null) {
      beep = gameObject.AddComponent<AudioSource>();
      beep.spatialBlend = 0;
      beepLow = Tone(660); beepHigh = Tone(990);
    }
    beep.PlayOneShot(high ? beepHigh : beepLow, .5f);
  }

  static AudioClip Tone(float hz) {
    int rate = AudioSettings.outputSampleRate, n = rate / 8;
    float[] d = new float[n];
    for (int i = 0; i < n; i++) d[i] = Mathf.Sin(2 * Mathf.PI * hz * i / rate) * Mathf.Clamp01((n - i) / (float)n * 3);
    AudioClip c = AudioClip.Create("roomBeep" + hz, n, 1, rate, false);
    c.SetData(d, 0);
    return c;
  }

  // ---- prompt ----

  void ShowPrompt(string s) {
    if (prompt == null) {
      prompt = new GameObject("RoomAlignPrompt");
      promptText = prompt.AddComponent<TextMesh>();
      Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      promptText.font = f;
      prompt.GetComponent<MeshRenderer>().sharedMaterial = f.material;
      promptText.fontSize = 48;
      promptText.characterSize = .006f;
      promptText.anchor = TextAnchor.LowerCenter;
      promptText.alignment = TextAlignment.Center;
      promptText.color = new Color(.6f, 1f, .7f);
    }
    promptText.text = s;
    prompt.SetActive(true);
  }

  void HidePrompt() {
    if (prompt != null) prompt.SetActive(false);
    if (markerA != null) Destroy(markerA);
  }

  static TextMesh Label(Transform parent, string s) {
    GameObject g = new GameObject("label");
    g.transform.SetParent(parent, false);
    TextMesh t = g.AddComponent<TextMesh>();
    Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    t.font = f;
    g.GetComponent<MeshRenderer>().sharedMaterial = f.material;
    t.text = s; t.fontSize = 64; t.characterSize = .4f;
    t.anchor = TextAnchor.LowerCenter;
    t.color = new Color(.6f, 1f, .7f);
    g.AddComponent<FaceCamera>();
    return t;
  }

  class FaceCamera : MonoBehaviour {
    void LateUpdate() { if (Camera.main != null) transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position); }
  }

  static GameObject Marker(Vector3 p) {
    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    Destroy(g.GetComponent<Collider>());
    g.transform.position = p;
    g.transform.localScale = Vector3.one * .02f;
    return g;
  }
}
