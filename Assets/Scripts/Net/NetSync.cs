// Patch sharing for multiplayer (2026-11, milestone 1 of claude/multiplayer-plan.md).
//
// The host's patch is the session's patch:
//  * A player who joins gets the host's whole patch (the save-file XML, gzipped) and
//    loads it in place of their own, with each device's network ID.
//  * Loading a file, an example or NEW replaces the patch for everyone.
//  * Devices created, moved or deleted by anyone show up for everyone (NetDevice).
// Knob / cable changes come with milestone 2.
//
// Host-authoritative: clients send their changes to the host, which applies them and
// passes them on. The host never sends to its own local client (it already has
// everything), only to remote players.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using Mirror;
using UnityEngine;

public struct NetPatchMsg : NetworkMessage { public byte[] patch; public int[] ids; public int nextId; }
// host migration: your place in line to take over if the host leaves (1 = next host)
public struct NetSuccessionMsg : NetworkMessage { public int rank; }
public struct NetSpawnReqMsg : NetworkMessage { public long token; public int tempId; public byte[] data; }
public struct NetSpawnMsg : NetworkMessage { public long token; public int tempId; public int netId; public byte[] data; }
public struct NetPoseMsg : NetworkMessage { public int netId; public double time; public bool held; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
public struct NetDeleteMsg : NetworkMessage { public int netId; }
public struct NetPatchUploadMsg : NetworkMessage { public byte[] patch; }

public static class NetSync {
  public static float poseRate = 20f;
  public static bool quitting;

  // A joined headset sends nothing until the session's patch has arrived: whatever it
  // did before (a start example, a file or example still loading, a device pulled from
  // the menu) is replaced by that patch, and mustn't land in everyone else's.
  static bool awaitingPatch;
  public static bool Active { get { return (NetworkServer.active || NetworkClient.isConnected) && !awaitingPatch; } }
  // connected to a session, even while its patch is still loading (Active is false then)
  public static bool InSession { get { return NetworkServer.active || NetworkClient.isConnected; } }
  static bool IsHost { get { return NetworkServer.active; } }
  public static bool IsJoinedClient { get { return NetworkClient.isConnected && !NetworkServer.active; } }

  // Devices can sit under a scaled parent (the start example hangs under the example
  // manager at 0.75), and a received patch puts them at the top level, so scale is sent
  // as world scale and turned back into local scale on arrival.
  // While another player holds something, it can't be selected or grabbed here
  // (manipObject.netLocked, checked by manipulator): a drumstick in someone else's hand
  // would otherwise start the hand-tool calibration; two people dragging one device
  // would fight. Anything already selected here is let go of.
  public static void SetGrabBlocked(IEnumerable<manipObject> objects, bool blocked) {
    foreach (manipObject m in objects) {
      if (m == null) continue;
      m.netLocked = blocked;
      if (blocked && m.curState == manipObject.manipState.selected) m.setState(manipObject.manipState.none);
    }
  }

  public static void SetWorldScale(Transform t, Vector3 world) {
    Vector3 p = t.parent != null ? t.parent.lossyScale : Vector3.one;
    t.localScale = new Vector3(p.x != 0 ? world.x / p.x : world.x, p.y != 0 ? world.y / p.y : world.y, p.z != 0 ? world.z / p.z : world.z);
  }

  static int nextId = 1;

  // ---- host migration ----
  public static bool migrating;   // lost the host; the next patch may be the same as ours
  public static bool keepIds;     // becoming host after the host left: keep the IDs
  public static bool HadPatch { get { return NetDevice.byId.Count > 0; } }

  // host: tell each joined headset its place in line (join order)
  public static void SendSuccession() {
    if (!NetworkServer.active) return;
    var conns = new List<NetworkConnectionToClient>();
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != NetworkServer.localConnection && c.isReady) conns.Add(c);
    conns.Sort((a, b) => a.connectionId.CompareTo(b.connectionId));
    for (int i = 0; i < conns.Count; i++) conns[i].Send(new NetSuccessionMsg { rank = i + 1 });
  }

  // the same devices under the same IDs with the same settings (ignoring where they
  // stand and the jack / device IDs, which are per headset)
  static bool PatchMatches(xmlSaveLoad set, int[] ids) {
    if (ids == null || set == null || set.InstrumentList == null || set.InstrumentList.Count != ids.Length) return false;
    int mine = 0;
    foreach (KeyValuePair<int, NetDevice> kv in NetDevice.byId) if (kv.Value != null) mine++;
    if (mine != ids.Length) return false;
    for (int i = 0; i < ids.Length; i++) {
      if (!NetDevice.byId.TryGetValue(ids[i], out NetDevice d) || d == null) return false;
      deviceInterface di = d.GetComponent<deviceInterface>();
      if (di == null) return false;
      if (PortableHash(di.GetData()) != PortableHash(set.InstrumentList[i])) {
        Debug.Log("[NetSync] device " + ids[i] + " (" + d.name + ") differs");
        return false;
      }
    }
    return true;
  }

  static readonly System.Text.RegularExpressions.Regex idElements =
    new System.Text.RegularExpressions.Regex(@"<(\w*ID)>-?\d+</\1>");

  static int PortableHash(InstrumentData data) {
    Vector3 p = data.position, sc = data.scale; Quaternion q = data.rotation; int id = data.ID;
    data.position = Vector3.zero; data.rotation = Quaternion.identity; data.scale = Vector3.one; data.ID = 0;
    string xml;
    try {
      using (var w = new StringWriter()) { deviceSerializer.Serialize(w, data); xml = w.ToString(); }
    } finally { data.position = p; data.rotation = q; data.scale = sc; data.ID = id; }
    xml = idElements.Replace(xml, "");
    unchecked {
      int h = (int)2166136261;
      foreach (char ch in xml) h = (h ^ ch) * 16777619;
      return h;
    }
  }
  static int nextTempId = 1;
  static readonly long myToken = System.DateTime.Now.Ticks ^ (long)Random.Range(int.MinValue, int.MaxValue);
  static readonly Dictionary<int, NetDevice> pendingByTemp = new Dictionary<int, NetDevice>();
  static bool applyingPatch;

  [RuntimeInitializeOnLoadMethod]
  static void Init() { Application.quitting += () => quitting = true; }

  // ---------------- registration (SoundStageNetworkManager) ----------------

  public static void RegisterServer() {
    awaitingPatch = false;
    NetworkServer.RegisterHandler<NetSpawnReqMsg>(OnServerSpawnReq);
    NetworkServer.RegisterHandler<NetPoseMsg>(OnServerPose);
    NetworkServer.RegisterHandler<NetDeleteMsg>(OnServerDelete);
    NetworkServer.RegisterHandler<NetPatchUploadMsg>(OnServerPatchUpload);
    NetworkServer.RegisterHandler<NetToolMsg>(OnServerTool);
    NetworkServer.RegisterHandler<NetControlMsg>(OnServerControl);
    NetworkServer.RegisterHandler<NetCableMsg>(OnServerCable);
    NetworkServer.RegisterHandler<NetTempoMsg>(NetClock.OnServerTempo);
    NetworkServer.RegisterHandler<NetTapeMsg>(OnServerTape);
    NetworkServer.RegisterHandler<NetRoomMsg>(NetRoom.OnServer);
    NetworkServer.RegisterHandler<NetMidiMsg>((conn, m) => { NetMidi.Apply(m); SendToRemotes(m, conn); });
    NetworkServer.RegisterHandler<NetMidiListMsg>((conn, m) => { NetMidi.OnList(m); SendToRemotes(m, conn); });
    NetworkServer.RegisterHandler<NetMidiSelectMsg>((conn, m) => { NetMidi.OnSelect(m); SendToRemotes(m, conn); });
    NetRoom.ServerStarted();
    migrating = false;
    if (keepIds) {
      // taking over after the host left: everyone has this patch with these IDs
      keepIds = false;
      nextId = 1;
      foreach (int id in NetDevice.byId.Keys) if (id >= nextId) nextId = id + 1;
      Debug.Log("[NetSync] took over the session: hosting its patch of " + NetDevice.byId.Count + " devices");
      return;
    }
    // a new session: the host's devices get fresh IDs
    nextId = 1;
    NetDevice.byId.Clear();
    foreach (NetDevice d in NetDevice.all) {
      if (d == null) continue;
      d.netId = 0;
      if (d.IsDevice && !d.silentDestroy) d.SetId(nextId++);
    }
    NetCables.ForgetKnown();
    Debug.Log("[NetSync] hosting a patch of " + NetDevice.byId.Count + " devices");
  }

  public static void RegisterClient() {
    if (IsHost) { awaitingPatch = false; return; } // host-mode client: the host's scene is the patch
    awaitingPatch = true;
    NetworkClient.RegisterHandler<NetPatchMsg>(OnClientPatch);
    NetworkClient.RegisterHandler<NetSpawnMsg>(OnClientSpawn);
    NetworkClient.RegisterHandler<NetPoseMsg>(OnClientPose);
    NetworkClient.RegisterHandler<NetDeleteMsg>(OnClientDelete);
    NetworkClient.RegisterHandler<NetToolMsg>(NetTools.Apply);
    NetworkClient.RegisterHandler<NetControlMsg>(NetControls.Apply);
    NetworkClient.RegisterHandler<NetCableMsg>(NetCables.Apply);
    NetworkClient.RegisterHandler<NetClockMsg>(NetClock.Apply);
    NetworkClient.RegisterHandler<NetTapeMsg>(NetTapes.Apply);
    NetworkClient.RegisterHandler<NetRoomMsg>(NetRoom.Apply);
    NetworkClient.RegisterHandler<NetMidiMsg>(NetMidi.Apply);
    NetworkClient.RegisterHandler<NetMidiListMsg>(NetMidi.OnList);
    NetworkClient.RegisterHandler<NetMidiSelectMsg>(NetMidi.OnSelect);
    NetworkClient.RegisterHandler<NetSuccessionMsg>(m => { if (NetSession.instance != null) NetSession.instance.successionRank = m.rank; });
    pendingByTemp.Clear();
  }

  // a player is ready: give them the patch
  public static void ServerSendPatch(NetworkConnectionToClient conn) {
    if (conn == NetworkServer.localConnection) return;
    conn.Send(BuildPatchMsg());
    if (NetRoom.RoomExists) conn.Send(NetRoom.Current()); // the shared room, to align to
  }

  // ---------------- local changes (NetDevice / menus) ----------------

  public static void LocalDeviceCreated(NetDevice d) {
    if (!Active || applyingPatch) return;
    byte[] data = DeviceData(d);
    if (data == null) { Debug.LogWarning("[NetSync] new device " + d.name + " has no data to send"); return; }
    Debug.Log("[NetSync] new device here: " + d.name + (IsHost ? " (sending)" : " (asking the host for an ID)"));
    if (IsHost) {
      d.SetId(nextId++);
      SendToRemotes(new NetSpawnMsg { token = 0, netId = d.netId, data = data }, null);
    } else {
      d.tempId = nextTempId++;
      pendingByTemp[d.tempId] = d;
      NetworkClient.Send(new NetSpawnReqMsg { token = myToken, tempId = d.tempId, data = data });
    }
  }

  // final: the resting pose (reliable). drift: moved by something other than a hand
  // (the example shelf following the player's height, an example landing) - not held,
  // so the handles stay free on the other headsets.
  public static void LocalDevicePose(NetDevice d, bool final = false, bool drift = false) {
    if (!Active || d.netId == 0) return;
    d.NoteSynced();
    NetPoseMsg m = new NetPoseMsg { netId = d.netId, time = NetworkTime.time, held = !final && !drift, pos = d.transform.position, rot = d.transform.rotation, scale = d.transform.lossyScale };
    int channel = final ? Channels.Reliable : Channels.Unreliable;
    if (IsHost) SendToRemotes(m, null, channel);
    else NetworkClient.Send(m, channel);
  }

  public static void LocalDeviceDeleted(NetDevice d) {
    if (!Active || d.netId == 0) return;
    NetDeleteMsg m = new NetDeleteMsg { netId = d.netId };
    Debug.Log("[NetSync] device " + d.netId + " deleted here: " + d.name);
    if (IsHost) SendToRemotes(m, null);
    else NetworkClient.Send(m);
  }

  public static void SendControl(NetControlMsg m, bool reliable) {
    if (!Active) return;
    int channel = reliable ? Channels.Reliable : Channels.Unreliable;
    if (IsHost) SendToRemotes(m, null, channel);
    else NetworkClient.Send(m, channel);
  }

  static void OnServerControl(NetworkConnectionToClient conn, NetControlMsg m) {
    NetControls.Apply(m);
    SendToRemotes(m, conn);
  }

  // host only: the shared clock, unreliable (the next one replaces a lost one)
  public static void SendClock(NetClockMsg m) {
    if (IsHost) SendToRemotes(m, null, Channels.Unreliable);
  }

  public static void SendCable(NetCableMsg m) {
    if (!Active) return;
    if (IsHost) SendToRemotes(m, null);
    else NetworkClient.Send(m);
  }

  static void OnServerCable(NetworkConnectionToClient conn, NetCableMsg m) {
    NetCables.Apply(m);
    SendToRemotes(m, conn);
  }

  public static void SendMidiRelay<T>(T m, NetworkConnectionToClient except) where T : struct, NetworkMessage { SendToRemotes(m, except); }

  public static void SendMidi(NetMidiMsg m) {
    if (!Active) return;
    if (IsHost) SendToRemotes(m, null);
    else NetworkClient.Send(m);
  }

  public static void SendRoomToRemotes(NetRoomMsg m, NetworkConnectionToClient except) { SendToRemotes(m, except); }

  public static void SendTape(NetTapeMsg m) {
    if (!Active) return;
    if (IsHost) SendToRemotes(m, null);
    else NetworkClient.Send(m);
  }

  static void OnServerTape(NetworkConnectionToClient conn, NetTapeMsg m) {
    NetTapes.Apply(m);
    SendToRemotes(m, conn);
  }

  public static void SendTool(NetToolMsg m) {
    if (!Active) return;
    int channel = m.held && m.press < 0 ? Channels.Unreliable : Channels.Reliable;
    if (IsHost) SendToRemotes(m, null, channel);
    else NetworkClient.Send(m, channel);
  }

  static void OnServerTool(NetworkConnectionToClient conn, NetToolMsg m) {
    NetTools.Apply(m);
    SendToRemotes(m, conn, m.held && m.press < 0 ? Channels.Unreliable : Channels.Reliable);
  }

  // Before a local clear (NEW, file load, example): the removed devices stay quiet;
  // the new patch is sent as a whole by LocalPatchReplaced.
  public static void MarkAllSilent() {
    foreach (NetDevice d in NetDevice.all) if (d != null) d.silentDestroy = true;
  }

  // Called after a file / example / NEW replaced the local patch.
  public static void LocalPatchReplaced() {
    if (!Active || applyingPatch) return;
    if (NetSession.instance != null) NetSession.instance.StartCoroutine(SendPatchNextFrame());
  }

  static IEnumerator SendPatchNextFrame() {
    yield return null; // destroyed devices are gone after this frame
    yield return null;
    if (!Active) yield break;
    if (IsHost) {
      AssignAllIds(); // loads create devices silently: number them all
      NetCables.ForgetKnown();

      SendToRemotes(BuildPatchMsg(), null);
      Debug.Log("[NetSync] patch replaced here: sent to everyone");
    } else {
      List<deviceInterface> order = new List<deviceInterface>();
      xmlSaveLoad set = SaveLoadInterface.instance.BuildSet(order);
      for (int i = 0; i < order.Count; i++) set.InstrumentList[i].scale = order[i].transform.lossyScale;
      byte[] bytes = set.ToBytes();
      NetworkClient.Send(new NetPatchUploadMsg { patch = bytes });
      Debug.Log("[NetSync] patch replaced here: sent to the host (" + bytes.Length / 1024 + " KB)");
    }
  }

  // ---------------- host side ----------------

  static void OnServerSpawnReq(NetworkConnectionToClient conn, NetSpawnReqMsg m) {
    NetDevice d = SpawnFromBytes(m.data);
    if (d == null) { Debug.LogWarning("[NetSync] couldn't create a player's new device"); return; }
    Debug.Log("[NetSync] player created " + d.name + ": ID " + nextId);
    d.SetId(nextId++);
    NetSpawnMsg s = new NetSpawnMsg { token = m.token, tempId = m.tempId, netId = d.netId, data = m.data };
    SendToRemotes(s, null); // the requester too: it learns the ID
  }

  static void OnServerPose(NetworkConnectionToClient conn, NetPoseMsg m) {
    ApplyPose(m);
    SendToRemotes(m, conn, Channels.Unreliable);
  }

  static void OnServerDelete(NetworkConnectionToClient conn, NetDeleteMsg m) {
    ApplyDelete(m.netId);
    SendToRemotes(m, conn);
  }

  static void OnServerPatchUpload(NetworkConnectionToClient conn, NetPatchUploadMsg m) {
    xmlSaveLoad set = xmlSaveLoad.FromBytes(m.patch);
    ReplacePatch(set, null);
    AssignAllIds();
    NetCables.ForgetKnown();
    // everyone, uploader included, so IDs match - but only once the new devices have
    // run their first Update: some (the oscillator's LFO switch) only take a loaded
    // setting into the value they save there, so saving them at once lost it
    if (NetSession.instance != null) NetSession.instance.StartCoroutine(SharePatchSoon());
  }

  static IEnumerator SharePatchSoon() {
    yield return null;
    yield return null;
    if (!IsHost) yield break;
    SendToRemotes(BuildPatchMsg(), null);
    Debug.Log("[NetSync] a player loaded a patch: shared with everyone");
  }

  static NetPatchMsg BuildPatchMsg() {
    List<deviceInterface> order = new List<deviceInterface>();
    xmlSaveLoad set = SaveLoadInterface.instance.BuildSet(order);
    int[] ids = new int[order.Count];
    for (int i = 0; i < order.Count; i++) {
      set.InstrumentList[i].scale = order[i].transform.lossyScale; // devices load at the top level
      NetDevice d = order[i].GetComponent<NetDevice>();
      ids[i] = d != null ? d.netId : 0;
    }
    return new NetPatchMsg { patch = set.ToBytes(), ids = ids, nextId = nextId };
  }

  static void AssignAllIds() {
    NetDevice.byId.Clear();
    foreach (NetDevice d in NetDevice.all) {
      if (d == null) continue;
      d.netId = 0;
      if (d.IsDevice && !d.silentDestroy) d.SetId(nextId++);
    }
  }

  static void SendToRemotes<T>(T msg, NetworkConnectionToClient except, int channel = Channels.Reliable) where T : struct, NetworkMessage {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values) {
      if (c == except || c == NetworkServer.localConnection) continue;
      c.Send(msg, channel);
    }
  }

  // ---------------- client side ----------------

  static void OnClientPatch(NetPatchMsg m) {
    if (awaitingPatch) {
      exampleManager em = Object.FindObjectOfType<exampleManager>();
      if (em != null) em.CancelLoad();
    }
    xmlSaveLoad set = xmlSaveLoad.FromBytes(m.patch);
    int[] ids = m.ids;
    if (migrating) {
      migrating = false;
      if (PatchMatches(set, ids)) {
        // rejoined after the host left, and the new host has the same patch: keep ours
        // (no reload, nothing stops playing)
        nextId = m.nextId;
        pendingByTemp.Clear();
        awaitingPatch = false;
        Debug.Log("[NetSync] rejoined the session: same patch (" + ids.Length + " devices), kept");
        NetRoom.OnJoinedSession();
        return;
      }
      Debug.Log("[NetSync] rejoined the session: the patch differs, loading it");
    }
    ReplacePatch(set, (index, g) => {
      NetDevice d = g.GetComponent<NetDevice>();
      if (d != null && ids != null && index < ids.Length) d.SetId(ids[index]);
    });
    nextId = m.nextId;
    pendingByTemp.Clear();
    awaitingPatch = false;
    NetRoom.OnJoinedSession();
    Debug.Log("[NetSync] loaded the session's patch (" + (ids != null ? ids.Length : 0) + " devices, " + m.patch.Length / 1024 + " KB)");
  }

  static void OnClientSpawn(NetSpawnMsg m) {
    if (m.token == myToken && pendingByTemp.TryGetValue(m.tempId, out NetDevice mine)) {
      pendingByTemp.Remove(m.tempId);
      if (mine != null) {
        mine.SetId(m.netId);
        LocalDevicePose(mine, true); // in case it was moved before the ID came back
      }
      return;
    }
    NetDevice d = SpawnFromBytes(m.data);
    if (d != null) d.SetId(m.netId);
    Debug.Log("[NetSync] device " + m.netId + " created by another player: " + (d != null ? d.name : "FAILED"));
  }

  static void OnClientPose(NetPoseMsg m) { ApplyPose(m); }
  static void OnClientDelete(NetDeleteMsg m) { ApplyDelete(m.netId); }

  // ---------------- shared ----------------

  static void ApplyPose(NetPoseMsg m) {
    if (NetDevice.byId.TryGetValue(m.netId, out NetDevice d) && d != null) d.SetRemotePose(m.pos, m.rot, m.scale, m.held, m.time);
  }

  static void ApplyDelete(int netId) {
    if (!NetDevice.byId.TryGetValue(netId, out NetDevice d) || d == null) { Debug.Log("[NetSync] delete of unknown device " + netId); return; }
    Debug.Log("[NetSync] device " + netId + " deleted by another player: " + d.name);
    d.silentDestroy = true;
    NetDevice.byId.Remove(netId);
    Object.Destroy(d.gameObject);
  }

  // Clear the patch (like pauseMenu NEW) and load set, quietly.
  static void ReplacePatch(xmlSaveLoad set, System.Action<int, GameObject> onDevice) {
    applyingPatch = true;
    try {
      // Destroyed immediately, not at the end of the frame: the old devices' jacks would
      // otherwise still be found when the new patch's cables are connected by jack ID
      // (two headsets that both started with the same example have the same IDs).
      foreach (GameObject g in GameObject.FindGameObjectsWithTag("instrument")) {
        NetDevice d = g.GetComponent<NetDevice>();
        if (d != null) d.silentDestroy = true;
        Object.DestroyImmediate(g);
      }
      GameObject exampleParent = GameObject.Find("exampleParent");
      if (exampleParent != null) Object.DestroyImmediate(exampleParent);
      NetDevice.byId.Clear();

      NetDevice.silentCreate = true;
      SaveLoadInterface.instance.LoadFromSet(set, onDevice);
      NetCables.ForgetKnown();
    } finally {
      NetDevice.silentCreate = false;
      applyingPatch = false;
    }
  }

  static NetDevice SpawnFromBytes(byte[] data) {
    InstrumentData d = FromDeviceBytes(data);
    if (d == null) return null;
    GameObject g = null;
    NetDevice.silentCreate = true;
    try { g = SaveLoadInterface.instance.SpawnFromData(d); }
    catch (System.Exception e) { Debug.LogError("[NetSync] creating " + d.deviceType + " failed: " + e); }
    finally { NetDevice.silentCreate = false; }
    return g != null ? g.GetComponent<NetDevice>() : null;
  }

  // A device's saved data without its place or instance IDs, hashed (NetDevice content
  // check).
  public static int ContentHash(deviceInterface di, out menuItem.deviceType type) {
    InstrumentData data = di.GetData();
    type = data.deviceType;
    data.position = Vector3.zero; data.rotation = Quaternion.identity; data.scale = Vector3.one; data.ID = 0;
    // The xylophone roll's held keys (in the order pressed, for the arpeggiator) change
    // with every note and differ in order between machines; key presses are shared as
    // key messages, so they're no reason to share the whole patch (2026-11: every mallet
    // hit made the session reload).
    if (data is XyloRollData xr) xr.selectedKeys = null;
    using (var ms = new MemoryStream()) {
      using (var w = new StreamWriter(ms, new UTF8Encoding(false))) deviceSerializer.Serialize(w, data);
      byte[] b = ms.ToArray();
      unchecked {
        int h = (int)2166136261;
        for (int i = 0; i < b.Length; i++) h = (h ^ b[i]) * 16777619;
        return h;
      }
    }
  }

  static readonly XmlSerializer deviceSerializer = new XmlSerializer(typeof(InstrumentData));

  static byte[] DeviceData(NetDevice d) {
    deviceInterface di = d.GetComponent<deviceInterface>();
    if (di == null) return null;
    InstrumentData data;
    try { data = di.GetData(); }
    catch (System.Exception e) { Debug.LogError("[NetSync] reading " + d.name + " to send it failed: " + e); return null; }
    data.scale = d.transform.lossyScale;
    using (var ms = new MemoryStream()) {
      using (var w = new StreamWriter(ms, new UTF8Encoding(false))) deviceSerializer.Serialize(w, data);
      return ms.ToArray();
    }
  }

  static InstrumentData FromDeviceBytes(byte[] bytes) {
    if (bytes == null) return null;
    using (var ms = new MemoryStream(bytes)) return deviceSerializer.Deserialize(ms) as InstrumentData;
  }
}
