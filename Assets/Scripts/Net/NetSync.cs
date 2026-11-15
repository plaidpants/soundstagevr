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
public struct NetSpawnReqMsg : NetworkMessage { public long token; public int tempId; public byte[] data; }
public struct NetSpawnMsg : NetworkMessage { public long token; public int tempId; public int netId; public byte[] data; }
public struct NetPoseMsg : NetworkMessage { public int netId; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
public struct NetDeleteMsg : NetworkMessage { public int netId; }
public struct NetPatchUploadMsg : NetworkMessage { public byte[] patch; }

public static class NetSync {
  public static float poseRate = 20f;
  public static bool quitting;

  public static bool Active { get { return NetworkServer.active || NetworkClient.isConnected; } }
  static bool IsHost { get { return NetworkServer.active; } }

  static int nextId = 1;
  static int nextTempId = 1;
  static readonly long myToken = System.DateTime.Now.Ticks ^ (long)Random.Range(int.MinValue, int.MaxValue);
  static readonly Dictionary<int, NetDevice> pendingByTemp = new Dictionary<int, NetDevice>();
  static bool applyingPatch;

  [RuntimeInitializeOnLoadMethod]
  static void Init() { Application.quitting += () => quitting = true; }

  // ---------------- registration (SoundStageNetworkManager) ----------------

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetSpawnReqMsg>(OnServerSpawnReq);
    NetworkServer.RegisterHandler<NetPoseMsg>(OnServerPose);
    NetworkServer.RegisterHandler<NetDeleteMsg>(OnServerDelete);
    NetworkServer.RegisterHandler<NetPatchUploadMsg>(OnServerPatchUpload);
    // a new session: the host's devices get fresh IDs
    nextId = 1;
    NetDevice.byId.Clear();
    foreach (NetDevice d in NetDevice.all) {
      if (d == null) continue;
      d.netId = 0;
      if (d.IsDevice && !d.silentDestroy) d.SetId(nextId++);
    }
    Debug.Log("[NetSync] hosting a patch of " + NetDevice.byId.Count + " devices");
  }

  public static void RegisterClient() {
    if (IsHost) return; // host-mode client: the host's scene is the patch
    NetworkClient.RegisterHandler<NetPatchMsg>(OnClientPatch);
    NetworkClient.RegisterHandler<NetSpawnMsg>(OnClientSpawn);
    NetworkClient.RegisterHandler<NetPoseMsg>(OnClientPose);
    NetworkClient.RegisterHandler<NetDeleteMsg>(OnClientDelete);
    pendingByTemp.Clear();
  }

  // a player is ready: give them the patch
  public static void ServerSendPatch(NetworkConnectionToClient conn) {
    if (conn == NetworkServer.localConnection) return;
    conn.Send(BuildPatchMsg());
  }

  // ---------------- local changes (NetDevice / menus) ----------------

  public static void LocalDeviceCreated(NetDevice d) {
    if (!Active || applyingPatch) return;
    byte[] data = DeviceData(d);
    if (data == null) return;
    if (IsHost) {
      d.SetId(nextId++);
      SendToRemotes(new NetSpawnMsg { token = 0, netId = d.netId, data = data }, null);
    } else {
      d.tempId = nextTempId++;
      pendingByTemp[d.tempId] = d;
      NetworkClient.Send(new NetSpawnReqMsg { token = myToken, tempId = d.tempId, data = data });
    }
  }

  public static void LocalDevicePose(NetDevice d, bool final = false) {
    if (!Active || d.netId == 0) return;
    NetPoseMsg m = new NetPoseMsg { netId = d.netId, pos = d.transform.position, rot = d.transform.rotation, scale = d.transform.localScale };
    int channel = final ? Channels.Reliable : Channels.Unreliable;
    if (IsHost) SendToRemotes(m, null, channel);
    else NetworkClient.Send(m, channel);
  }

  public static void LocalDeviceDeleted(NetDevice d) {
    if (!Active || d.netId == 0) return;
    NetDeleteMsg m = new NetDeleteMsg { netId = d.netId };
    if (IsHost) SendToRemotes(m, null);
    else NetworkClient.Send(m);
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

      SendToRemotes(BuildPatchMsg(), null);
      Debug.Log("[NetSync] patch replaced here: sent to everyone");
    } else {
      List<deviceInterface> order = new List<deviceInterface>();
      byte[] bytes = SaveLoadInterface.instance.BuildSet(order).ToBytes();
      NetworkClient.Send(new NetPatchUploadMsg { patch = bytes });
      Debug.Log("[NetSync] patch replaced here: sent to the host (" + bytes.Length / 1024 + " KB)");
    }
  }

  // ---------------- host side ----------------

  static void OnServerSpawnReq(NetworkConnectionToClient conn, NetSpawnReqMsg m) {
    NetDevice d = SpawnFromBytes(m.data);
    if (d == null) return;
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
    SendToRemotes(BuildPatchMsg(), null); // everyone, uploader included, so IDs match
    Debug.Log("[NetSync] a player loaded a patch: shared with everyone");
  }

  static NetPatchMsg BuildPatchMsg() {
    List<deviceInterface> order = new List<deviceInterface>();
    xmlSaveLoad set = SaveLoadInterface.instance.BuildSet(order);
    int[] ids = new int[order.Count];
    for (int i = 0; i < order.Count; i++) {
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
    xmlSaveLoad set = xmlSaveLoad.FromBytes(m.patch);
    int[] ids = m.ids;
    ReplacePatch(set, (index, g) => {
      NetDevice d = g.GetComponent<NetDevice>();
      if (d != null && ids != null && index < ids.Length) d.SetId(ids[index]);
    });
    nextId = m.nextId;
    pendingByTemp.Clear();
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
  }

  static void OnClientPose(NetPoseMsg m) { ApplyPose(m); }
  static void OnClientDelete(NetDeleteMsg m) { ApplyDelete(m.netId); }

  // ---------------- shared ----------------

  static void ApplyPose(NetPoseMsg m) {
    if (NetDevice.byId.TryGetValue(m.netId, out NetDevice d) && d != null) d.SetRemotePose(m.pos, m.rot, m.scale);
  }

  static void ApplyDelete(int netId) {
    if (!NetDevice.byId.TryGetValue(netId, out NetDevice d) || d == null) return;
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
    } finally {
      NetDevice.silentCreate = false;
      applyingPatch = false;
    }
  }

  static NetDevice SpawnFromBytes(byte[] data) {
    InstrumentData d = FromDeviceBytes(data);
    if (d == null) return null;
    GameObject g;
    NetDevice.silentCreate = true;
    try { g = SaveLoadInterface.instance.SpawnFromData(d); }
    finally { NetDevice.silentCreate = false; }
    return g != null ? g.GetComponent<NetDevice>() : null;
  }

  static readonly XmlSerializer deviceSerializer = new XmlSerializer(typeof(InstrumentData));

  static byte[] DeviceData(NetDevice d) {
    deviceInterface di = d.GetComponent<deviceInterface>();
    if (di == null) return null;
    InstrumentData data = di.GetData();
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
