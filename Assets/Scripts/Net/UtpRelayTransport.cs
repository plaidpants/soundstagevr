// Mirror transport over Unity Transport through Unity Relay (milestone 5, internet play).
//
// NetInternet sets the relay data (from a Relay allocation) before Mirror starts the host
// or client; after that this behaves like any other Mirror transport. Host mode works as
// usual: the host's own client is Mirror's local connection and never reaches here.
//
// Channels
// - Unreliable: one Mirror batch per UTP packet, no pipeline (dropped if it can't be sent).
// - Reliable: Unity's reliable pipeline only carries packets up to the MTU and has a
//   limited window of packets in flight, but a whole patch can be far bigger. So every
//   reliable Mirror batch is cut into chunks here, sent in order, and put back together on
//   the other side (first byte of each chunk: 1 = last chunk of the batch). Chunks wait in
//   a queue while the window is full and go out on later frames.
//
// Encrypted (DTLS) between each player and the relay.

using System;
using System.Collections.Generic;
using Mirror;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities; // WithReliableStageParameters
using UnityEngine;
using NetworkConnection = Unity.Networking.Transport.NetworkConnection; // not Mirror's

public class UtpRelayTransport : Transport {
  public const int ChunkPayload = 1000;      // reliable chunk size (fits the MTU with relay + DTLS headers)
  public const int UnreliableMax = 1000;     // largest unreliable batch Mirror may hand us
  public const int ReliableMax = 4 * 1024 * 1024;
  public int timeoutMs = 30000;              // like KCP's: a headset taken off briefly isn't dropped
                                             // (but the relay itself drops a player silent for ~10 s)

  RelayServerData relayData;
  bool hasRelayData;
  bool isHost;

  NetworkDriver driver;
  NetworkPipeline reliablePipe;
  NetworkPipeline unreliablePipe = NetworkPipeline.Null;
  NativeArray<byte> sendBuf, recvBuf;

  // client
  NetworkConnection clientConn;
  bool clientConnected;
  Peer clientPeer;

  // server
  bool serverActive;
  int nextId = 1;
  readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
  readonly Dictionary<NetworkConnection, int> ids = new Dictionary<NetworkConnection, int>();

  // traffic counter, logged once a minute (relay bandwidth is metered)
  public static long bytesSent, bytesReceived;
  long loggedSent, loggedReceived;
  float logTimer;

  class Peer {
    public NetworkConnection conn;
    public readonly Queue<byte[]> pending = new Queue<byte[]>(); // reliable chunks, header included
    public byte[] assembly = new byte[4096];
    public int assembled;
  }

  // ---- setup (NetInternet) ----

  public void SetRelayData(RelayServerData data, bool host) {
    relayData = data;
    hasRelayData = true;
    isHost = host;
  }

  bool CreateDriver() {
    DisposeDriver();
    if (!hasRelayData) { Debug.LogError("[Utp] no relay data set"); return false; }
    var settings = new NetworkSettings(Allocator.Temp);
    settings.WithRelayParameters(ref relayData);
    settings.WithNetworkConfigParameters(disconnectTimeoutMS: timeoutMs, heartbeatTimeoutMS: 1000);
    settings.WithReliableStageParameters(windowSize: 256, maximumResendTime: 1000); // room for a whole patch in flight over a long link
    driver = NetworkDriver.Create(settings);
    reliablePipe = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
    if (!sendBuf.IsCreated) sendBuf = new NativeArray<byte>(2048, Allocator.Persistent);
    if (!recvBuf.IsCreated) recvBuf = new NativeArray<byte>(2048, Allocator.Persistent);
    return true;
  }

  void DisposeDriver() {
    if (driver.IsCreated) driver.Dispose();
    driver = default;
  }

  void OnDestroy() {
    DisposeDriver();
    if (sendBuf.IsCreated) sendBuf.Dispose();
    if (recvBuf.IsCreated) recvBuf.Dispose();
  }

  // ---- Transport ----

  public override bool Available() { return Application.platform != RuntimePlatform.WebGLPlayer; }
  public override bool IsEncrypted => true;
  public override string EncryptionCipher => "DTLS (Unity Relay)";

  public override int GetMaxPacketSize(int channelId = Channels.Reliable) {
    return channelId == Channels.Unreliable ? UnreliableMax : ReliableMax;
  }

  // small reliable batches, so a big patch doesn't hold up everything queued behind it
  public override int GetBatchThreshold(int channelId = Channels.Reliable) { return UnreliableMax; }

  public override bool ClientConnected() { return clientConnected; }

  public override void ClientConnect(string address) {
    if (!CreateDriver()) { OnClientDisconnected?.Invoke(); return; }
    clientConn = driver.Connect(relayData.Endpoint);
    clientPeer = new Peer { conn = clientConn };
    Debug.Log("[Utp] connecting through the relay");
  }

  public override void ClientConnect(Uri uri) { ClientConnect(uri.Host); }

  public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable) {
    if (!clientConnected || clientPeer == null) return;
    Send(clientPeer, segment, channelId);
    OnClientDataSent?.Invoke(segment, channelId);
  }

  public override void ClientDisconnect() {
    if (driver.IsCreated && clientConn != default) {
      driver.Disconnect(clientConn);
      driver.ScheduleUpdate().Complete();
    }
    bool was = clientConnected || clientConn != default;
    clientConnected = false;
    clientConn = default;
    clientPeer = null;
    if (!serverActive) DisposeDriver();
    if (was) OnClientDisconnected?.Invoke();
  }

  public override Uri ServerUri() {
    return new Uri("relay://" + (hasRelayData ? relayData.Endpoint.ToString() : "none"));
  }

  public override bool ServerActive() { return serverActive; }

  public override void ServerStart() {
    if (!isHost || !CreateDriver()) { Debug.LogError("[Utp] can't host: no host relay data"); return; }
    if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0) { Debug.LogError("[Utp] bind to the relay failed"); return; }
    if (driver.Listen() != 0) { Debug.LogError("[Utp] listen failed"); return; }
    serverActive = true;
    Debug.Log("[Utp] hosting through the relay");
  }

  public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) {
    if (!peers.TryGetValue(connectionId, out Peer p)) return;
    Send(p, segment, channelId);
    OnServerDataSent?.Invoke(connectionId, segment, channelId);
  }

  public override void ServerDisconnect(int connectionId) {
    if (!peers.TryGetValue(connectionId, out Peer p)) return;
    if (driver.IsCreated) driver.Disconnect(p.conn);
    RemovePeer(connectionId);
    OnServerDisconnected?.Invoke(connectionId);
  }

  public override string ServerGetClientAddress(int connectionId) { return "relay#" + connectionId; }

  public override void ServerStop() {
    if (driver.IsCreated) {
      foreach (Peer p in peers.Values) driver.Disconnect(p.conn);
      driver.ScheduleUpdate().Complete();
    }
    peers.Clear();
    ids.Clear();
    serverActive = false;
    DisposeDriver();
  }

  public override void Shutdown() {
    ClientDisconnect();
    ServerStop();
  }

  // ---- sending ----

  void Send(Peer p, ArraySegment<byte> segment, int channelId) {
    if (channelId == Channels.Unreliable) {
      if (segment.Count > UnreliableMax) return;
      WritePacket(unreliablePipe, p.conn, segment.Array, segment.Offset, segment.Count, -1);
      return;
    }
    // reliable: cut into chunks, queue, send what the window allows
    int off = 0;
    do {
      int n = Math.Min(ChunkPayload, segment.Count - off);
      byte[] chunk = new byte[n + 1];
      chunk[0] = (byte)(off + n >= segment.Count ? 1 : 0);
      Buffer.BlockCopy(segment.Array, segment.Offset + off, chunk, 1, n);
      p.pending.Enqueue(chunk);
      off += n;
    } while (off < segment.Count);
    Flush(p);
  }

  void Flush(Peer p) {
    while (p.pending.Count > 0) {
      byte[] c = p.pending.Peek();
      if (!WritePacket(reliablePipe, p.conn, c, 0, c.Length, 0)) return; // window full: later
      p.pending.Dequeue();
    }
  }

  // header < 0: no header byte (the data already holds it, or none is wanted)
  bool WritePacket(NetworkPipeline pipe, NetworkConnection conn, byte[] data, int offset, int count, int header) {
    if (!driver.IsCreated) return false;
    if (driver.BeginSend(pipe, conn, out DataStreamWriter w) != 0) return false;
    NativeArray<byte>.Copy(data, offset, sendBuf, 0, count);
    w.WriteBytes(sendBuf.GetSubArray(0, count));
    if (w.HasFailedWrites) { driver.AbortSend(w); return false; }
    int r = driver.EndSend(w);
    if (r < 0) return false;
    bytesSent += count;
    return true;
  }

  // ---- receiving ----

  void Received(Peer p, NetworkPipeline pipe, ref DataStreamReader reader, Action<ArraySegment<byte>, int> deliver) {
    int len = reader.Length;
    if (len <= 0 || len > recvBuf.Length) return;
    reader.ReadBytes(recvBuf.GetSubArray(0, len));
    bytesReceived += len;
    if (pipe != reliablePipe) {
      byte[] b = new byte[len];
      NativeArray<byte>.Copy(recvBuf, 0, b, 0, len);
      deliver(new ArraySegment<byte>(b), Channels.Unreliable);
      return;
    }
    int n = len - 1;
    if (p.assembled + n > ReliableMax) { Debug.LogError("[Utp] reliable message too big; dropped"); p.assembled = 0; return; }
    if (p.assembled + n > p.assembly.Length) Array.Resize(ref p.assembly, Math.Max(p.assembly.Length * 2, p.assembled + n));
    NativeArray<byte>.Copy(recvBuf, 1, p.assembly, p.assembled, n);
    p.assembled += n;
    if (recvBuf[0] == 1) {
      var whole = new ArraySegment<byte>(p.assembly, 0, p.assembled);
      p.assembled = 0;
      deliver(whole, Channels.Reliable);
    }
  }

  public override void ClientEarlyUpdate() {
    if (!driver.IsCreated || serverActive || clientPeer == null) return;
    driver.ScheduleUpdate().Complete();
    NetworkEvent.Type ev;
    while ((ev = driver.PopEventForConnection(clientConn, out DataStreamReader reader, out NetworkPipeline pipe)) != NetworkEvent.Type.Empty) {
      switch (ev) {
        case NetworkEvent.Type.Connect:
          clientConnected = true;
          Debug.Log("[Utp] connected through the relay");
          OnClientConnected?.Invoke();
          break;
        case NetworkEvent.Type.Data:
          Received(clientPeer, pipe, ref reader, (seg, ch) => OnClientDataReceived?.Invoke(seg, ch));
          break;
        case NetworkEvent.Type.Disconnect:
          Debug.Log("[Utp] disconnected from the relay host");
          clientConnected = false;
          clientConn = default;
          clientPeer = null;
          DisposeDriver();
          OnClientDisconnected?.Invoke();
          return;
      }
      if (clientPeer == null) return;
    }
  }

  public override void ClientLateUpdate() {
    if (!driver.IsCreated || serverActive || clientPeer == null) return;
    Flush(clientPeer);
    driver.ScheduleFlushSend(default).Complete();
    LogTraffic();
  }

  public override void ServerEarlyUpdate() {
    if (!serverActive || !driver.IsCreated) return;
    driver.ScheduleUpdate().Complete();
    NetworkConnection c;
    while ((c = driver.Accept()) != default) {
      int id = nextId++;
      peers[id] = new Peer { conn = c };
      ids[c] = id;
      OnServerConnectedWithAddress?.Invoke(id, ServerGetClientAddress(id));
    }
    NetworkEvent.Type ev;
    while ((ev = driver.PopEvent(out c, out DataStreamReader reader, out NetworkPipeline pipe)) != NetworkEvent.Type.Empty) {
      if (!ids.TryGetValue(c, out int id)) continue;
      if (ev == NetworkEvent.Type.Data) {
        Received(peers[id], pipe, ref reader, (seg, ch) => OnServerDataReceived?.Invoke(id, seg, ch));
      } else if (ev == NetworkEvent.Type.Disconnect) {
        RemovePeer(id);
        OnServerDisconnected?.Invoke(id);
      }
    }
  }

  public override void ServerLateUpdate() {
    if (!serverActive || !driver.IsCreated) return;
    foreach (Peer p in peers.Values) Flush(p);
    driver.ScheduleFlushSend(default).Complete();
    LogTraffic();
  }

  void RemovePeer(int id) {
    if (!peers.TryGetValue(id, out Peer p)) return;
    ids.Remove(p.conn);
    peers.Remove(id);
  }

  void LogTraffic() {
    logTimer += Time.unscaledDeltaTime;
    if (logTimer < 60f) return;
    logTimer = 0;
    long s = bytesSent - loggedSent, r = bytesReceived - loggedReceived;
    loggedSent = bytesSent;
    loggedReceived = bytesReceived;
    int waiting = 0;
    if (clientPeer != null) waiting += clientPeer.pending.Count;
    foreach (Peer p in peers.Values) waiting += p.pending.Count;
    Debug.Log("[Utp] last minute: sent " + (s / 1024) + " KB, received " + (r / 1024) + " KB (" +
              (bytesSent / 1024) + " / " + (bytesReceived / 1024) + " KB this session" +
              (waiting > 0 ? ", " + waiting + " reliable chunks waiting" : "") + ")");
  }
}
