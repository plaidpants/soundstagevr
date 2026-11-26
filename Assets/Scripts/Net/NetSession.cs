// Multiplayer session (2026-11, milestone 0 of claude/multiplayer-plan.md).
//
// Zero-setup local-network play, after James's RocksRocketsAndRayguns: with
// MULTIPLAYER set to LOCAL the app listens for a SoundStage host on the LAN for a
// few seconds, joins the first one it finds, and otherwise becomes the host itself.
// With MULTIPLAYER OFF (the default) none of the network objects exist.
//
// The Mirror objects (NetworkManager, KCP transport, LAN discovery) are created in
// code on an inactive GameObject so their fields are set before Awake runs; the
// player prefab comes from Resources/Net/NetPlayer (SoundStage > Multiplayer >
// Create NetPlayer prefab).
//
// Milestone 0 shares only the players (head + hands). Patch sync comes next.
//
// Milestone 5: INTERNET works the same way over Unity Relay: everyone on the same build
// with the same ROOM CODE finds each other (NetInternet), the transport is
// UtpRelayTransport instead of KCP, and the rest of the game code doesn't know the
// difference.
//
// Settings (2026-11-18): MULTIPLAYER: LOCAL / INTERNET picks the kind, MULTIPLAYER ON /
// OFF starts and stops it (internet: through the room code dialog, NetCodeDialog), and a
// status line shows OFFLINE / SEARCHING... / HOSTING (n) 90MS / JOINED (n) 89MS.

using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

public class NetSession : MonoBehaviour {
  public enum Mode { Off = 0, Local = 1, Internet = 2 }
  public enum State { Off, Searching, Host, Client, Ended } // Ended: an internet session hit its idle / hour limit (NetLimits)

  public static NetSession instance;
  public static event System.Action StatusChanged;

  // Changes whenever the network protocol changes, so different builds don't connect.
  // "SSVR" + a fingerprint of every network message's fields: two builds whose messages
  // differ can't read each other (the joiner was dropped on the first message it
  // couldn't read, rejoined, got the patch again - over and over, wiping whatever it
  // made), so they don't see each other at all instead.
  static long handshake;
  public static long Handshake {
    get {
      if (handshake == 0) {
        var names = new List<string>();
        foreach (System.Type t in typeof(NetSession).Assembly.GetTypes()) {
          if (!t.IsValueType || !typeof(NetworkMessage).IsAssignableFrom(t)) continue;
          var sb = new System.Text.StringBuilder(t.Name);
          foreach (System.Reflection.FieldInfo f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            sb.Append('|').Append(f.Name).Append(':').Append(f.FieldType.Name);
          names.Add(sb.ToString());
        }
        names.Sort(System.StringComparer.Ordinal);
        uint h = 2166136261;
        foreach (string n in names) foreach (char ch in n) h = (h ^ ch) * 16777619;
        handshake = 0x5353565200000000L | h;
        Debug.Log("[NetSession] network version " + h.ToString("X8") + " (" + names.Count + " messages): only builds with the same version find each other");
      }
      return handshake;
    }
  }

  public State state { get; private set; } = State.Off;
  public int successionRank; // host migration: 1 = take over if the host leaves (sent by the host)

  SoundStageNetworkManager manager;
  kcp2k.KcpTransport kcp;
  UtpRelayTransport utp;
  int searchRun;          // internet: bumped to cancel a search still waiting on the network
  string joinedLobby;     // internet: the session we joined (skipped for a while if it fails)
  NetworkDiscovery discovery;
  NetworkDiscovery hostProbe; // a lone host listens for other hosts with this (see ProbeOtherHosts)
  readonly Dictionary<long, ServerResponse> found = new Dictionary<long, ServerResponse>();
  bool sessionOverride; // desktop spectator: LOCAL for this run without saving it
  bool sessionFull;     // the only session around is full: show SESSION FULL and look again later
  const float FullRetry = 15f;
  static bool IsFull(int players) { return players >= NetInternet.MaxPlayers; }
  string lastStatus = "";

  // What's running: Off, or the kind (Local / Internet) when multiplayer is on.
  public static Mode CurrentMode { get { return On ? Kind : Mode.Off; } }

  // LOCAL or INTERNET (kept while multiplayer is off)
  public static Mode Kind {
    get {
      MigratePrefs();
      return PlayerPrefs.GetInt("netKind", (int)Mode.Local) == (int)Mode.Internet ? Mode.Internet : Mode.Local;
    }
  }

  // MULTIPLAYER ON / OFF. The PC joins local sessions until its button is first used.
  public static bool On {
    get {
      MigratePrefs();
      if (!PlayerPrefs.HasKey("netOn")) return instance != null && instance.sessionOverride && Kind == Mode.Local;
      return PlayerPrefs.GetInt("netOn") == 1;
    }
  }

  // before 2026-11-18 one setting: netMode 0 off / 1 local / 2 internet
  static void MigratePrefs() {
    if (PlayerPrefs.HasKey("netKind") || !PlayerPrefs.HasKey("netMode")) return;
    int m = PlayerPrefs.GetInt("netMode");
    PlayerPrefs.SetInt("netKind", m == 2 ? (int)Mode.Internet : (int)Mode.Local);
    if (m != 0) PlayerPrefs.SetInt("netOn", 1);
    PlayerPrefs.DeleteKey("netMode");
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("NetSession");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetSession>();
  }

  void Start() {
    // The PC (editor or Windows build) can't easily press the settings button, so it
    // always joins in for that run - also with a Link headset attached.
    if ((NetSpectator.Active && !Phone.Active) || Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer) sessionOverride = true;
    Debug.Log("[NetSession] multiplayer " + CurrentMode + (sessionOverride ? " (PC: always on)" : "") +
              ", spectator=" + NetSpectator.Active + ", xr=" + UnityEngine.XR.XRSettings.isDeviceActive);
    // Internet never starts by itself: a session left on would keep using relay bandwidth
    // on later, solo days. It stays set to INTERNET, and MULTIPLAYER ON (room code
    // dialog, last code filled in) starts it. Local still starts on its own.
    if (CurrentMode == Mode.Internet) {
      PlayerPrefs.SetInt("netOn", 0);
      Debug.Log("[NetSession] internet multiplayer is off at start-up: press MULTIPLAYER ON to join a room");
    }
    if (CurrentMode != Mode.Off) StartSearch();
    RaiseStatus();
  }

  // an internet session that hit its idle / 3-hour limit (ON again starts a new one)
  public static bool Ended { get { return instance != null && instance.state == State.Ended; } }

  // MULTIPLAYER: LOCAL / INTERNET. Changing it while multiplayer is on turns it off.
  public static void SetKind(Mode k) {
    if (k == Mode.Off || k == Kind) return;
    bool wasOn = On;
    PlayerPrefs.SetInt("netKind", (int)k);
    if (wasOn) SetOn(false);
    instance?.RaiseStatus();
  }

  public static void ToggleKind() { SetKind(Kind == Mode.Local ? Mode.Internet : Mode.Local); }

  // MULTIPLAYER ON / OFF (internet ON goes through JoinRoom, after the code dialog)
  public static void SetOn(bool on) {
    PlayerPrefs.SetInt("netOn", on ? 1 : 0);
    if (instance == null) return;
    instance.sessionOverride = false;
    NetLimits.endedReason = 0;
    NetNotice.Hide();
    if (!on) {
      instance.StopAll();
    } else if (instance.state == State.Off || instance.state == State.Ended) {
      instance.StartSearch();
    }
    instance.RaiseStatus();
  }

  // Internet: join (or start) the room with this code.
  public static void JoinRoom(string code) {
    if (!NetInternet.Valid(code)) return;
    bool sameRoom = code == NetInternet.Code && On && Kind == Mode.Internet && instance != null &&
                    (instance.state == State.Host || instance.state == State.Client || instance.state == State.Searching);
    if (sameRoom) return; // already there
    PlayerPrefs.SetInt("netKind", (int)Mode.Internet);
    if (instance != null) instance.StopAll();
    NetInternet.Code = code;
    SetOn(true);
  }

  void StartSearch() {
    if (CurrentMode == Mode.Internet) StartInternet(0f);
    else StartLocal();
  }

  // The transport has to be chosen while nothing is running.
  void UseTransport(Transport t) {
    manager.transport = t;
    Transport.active = t;
  }

  // ---- network objects ----

  void EnsureNetwork() {
    if (manager != null) return;
    GameObject g = new GameObject("Network");
    g.SetActive(false); // so Awake runs after the fields below are set
    DontDestroyOnLoad(g);

    kcp = g.AddComponent<kcp2k.KcpTransport>();
    // Taking a headset off pauses the app; give it 30 s before the others drop it.
    kcp.Timeout = 30000;

    utp = g.AddComponent<UtpRelayTransport>();

    manager = g.AddComponent<SoundStageNetworkManager>();
    manager.transport = kcp;
    manager.networkAddress = "localhost"; // the relay transport ignores it
    manager.playerPrefab = Resources.Load<GameObject>("Net/NetPlayer");
    if (manager.playerPrefab == null) Debug.LogError("[NetSession] Resources/Net/NetPlayer missing: run SoundStage > Multiplayer > Create NetPlayer prefab");
    manager.autoCreatePlayer = true;
    manager.maxConnections = NetInternet.MaxPlayers;
    manager.sendRate = 30;
    manager.offlineScene = "";
    manager.onlineScene = "";

    discovery = g.AddComponent<SoundStageDiscovery>();
    discovery.transport = kcp;
    discovery.secretHandshake = Handshake;
    discovery.OnServerFound = new ServerFoundUnityEvent<ServerResponse>();
    discovery.OnServerFound.AddListener(OnServerFound);

    // NetworkDiscovery can't advertise and search at once (each stops the other), so a
    // second one does the searching while we host. It needs its own GameObject: only
    // one NetworkDiscovery is allowed per object.
    GameObject probe = new GameObject("HostProbe");
    probe.transform.SetParent(g.transform, false);
    hostProbe = probe.AddComponent<SoundStageDiscovery>();
    hostProbe.transport = kcp;
    hostProbe.secretHandshake = Handshake;
    hostProbe.OnServerFound = new ServerFoundUnityEvent<ServerResponse>();
    hostProbe.OnServerFound.AddListener(OnOtherHostFound);

    g.SetActive(true);
  }

  // ---- find-or-host ----

  void StartLocal() {
    EnsureNetwork();
    CancelInvoke();
    UseTransport(kcp);
    found.Clear();
    SetState(State.Searching);
    discovery.StartDiscovery();
    // wait so we hear any host already out there; the random part keeps two headsets
    // started together from both becoming host
    Invoke(nameof(Decide), 2f + Random.Range(0f, 3f));
    Debug.Log("[NetSession] searching the local network");
  }

  void OnServerFound(ServerResponse r) {
    if (found.ContainsKey(r.serverId)) return;
    found[r.serverId] = r;
    Debug.Log("[NetSession] found host " + r.EndPoint + " (" + r.uri + ")");
    if (IsFull(SoundStageDiscovery.Players(r))) return; // Decide shows SESSION FULL if it's the only one
    if (state == State.Searching) Join(r);
  }

  void Decide() {
    if (state != State.Searching) return;
    bool anyFull = false;
    foreach (ServerResponse r in found.Values) {
      if (IsFull(SoundStageDiscovery.Players(r))) { anyFull = true; continue; }
      Join(r); return;
    }
    if (anyFull) { WaitWhileFull(); return; }
    BecomeHost();
  }

  // Every session we can see is full. Don't host a separate one next to it (the merge
  // check would only try to join the full one); say so and look again every so often.
  void WaitWhileFull() {
    if (!sessionFull) Debug.Log("[NetSession] session is full (" + NetInternet.MaxPlayers + " players): checking again every " + FullRetry + " s");
    sessionFull = true;
    if (discovery != null) discovery.StopDiscovery();
    RaiseStatus();
    Invoke(nameof(StartSearch), FullRetry);
  }

  void Join(ServerResponse r) {
    CancelInvoke(nameof(Decide));
    discovery.StopDiscovery();
    SetState(State.Client);
    manager.StartClient(r.uri);
    Debug.Log("[NetSession] joining " + r.uri);
  }

  void BecomeHost() {
    if (CurrentMode == Mode.Internet) { HostInternet(++searchRun); return; }
    discovery.StopDiscovery();
    SetState(State.Host);
    manager.StartHost();
    discovery.AdvertiseServer();
    Debug.Log("[NetSession] no host found: hosting");
    InvokeRepeating(nameof(ProbeOtherHosts), 3f, 6f);
  }

  // A host can end up alone next to another session: two headsets started together,
  // or a headset that slept, lost its connection, took over as host on waking (before
  // its wifi was back) while the real session carried on. While we host with nobody
  // joined, look for other hosts every few seconds, for as long as we stay alone. A
  // host that has players always wins; of two lone hosts the one with the higher
  // server ID gives up and joins the other (the rule is the same on both, so they
  // never swap back and forth).
  void ProbeOtherHosts() {
    if (state != State.Host || NetworkServer.connections.Count > 1) {
      hostProbe.StopDiscovery();
      return;
    }
    try {
      hostProbe.StartDiscovery();
    } catch (System.Exception e) {
      // no network yet (just woke up): try again next time
      Debug.Log("[NetSession] can't look for other hosts yet: " + e.Message);
      return;
    }
    Invoke(nameof(StopProbe), 2f);
  }

  void StopProbe() { if (hostProbe != null) hostProbe.StopDiscovery(); }

  void OnOtherHostFound(ServerResponse r) {
    if (state != State.Host || r.serverId == discovery.ServerId) return; // ourselves
    if (NetworkServer.connections.Count > 1) return; // someone joined us: stay host
    int theirs = SoundStageDiscovery.Players(r);
    if (IsFull(theirs)) return; // can't join it anyway
    if (theirs > 1) {
      Debug.Log("[NetSession] another host with " + theirs + " players is running (" + r.uri + "): joining it");
    } else {
      if (discovery.ServerId < r.serverId) return;   // two lone hosts: the other one gives up
      Debug.Log("[NetSession] another lone host is running (" + r.uri + "): joining it instead");
    }
    CancelInvoke();
    hostProbe.StopDiscovery();
    SetState(State.Searching); // so stopping the host doesn't count as losing a connection
    manager.StopHost();
    discovery.StopDiscovery();
    Join(r);
  }

  void StopAll() {
    CancelInvoke();
    searchRun++;
    NetInternet.Unpublish();
    NetInternet.status = "";
    State was = state;
    SetState(State.Off); // before stopping, so the disconnect callback doesn't restart the search
    if (manager != null) {
      if (NetworkServer.active && NetworkClient.active) manager.StopHost();
      else if (NetworkClient.active) manager.StopClient();
      else if (NetworkServer.active) manager.StopServer();
    }
    if (discovery != null) discovery.StopDiscovery();
    if (hostProbe != null) hostProbe.StopDiscovery();
    if (was != State.Off) Debug.Log("[NetSession] stopped");
  }

  // Called by SoundStageNetworkManager when our client connection ends (host left,
  // connect failed). Host migration: every headset has the whole patch, so the next in
  // line (rank 1) takes over at once, keeping the device IDs; the others look for it a
  // little longer than usual and, if its patch is the same as theirs, keep their own
  // (NetSync.PatchMatches) - nothing reloads and the music keeps playing.
  public void OnClientConnectionLost() {
    if (state != State.Client) return;
    if (CurrentMode == Mode.Internet) NetInternet.MarkBad(joinedLobby); // gone or unreachable: don't pick it again
    int rank = successionRank;
    successionRank = 0;
    bool hadPatch = NetSync.HadPatch;
    NetSync.migrating = hadPatch;
    if (hadPatch && rank == 1) {
      Debug.Log("[NetSession] lost the host: next in line, taking over");
      SetState(State.Searching);
      Invoke(nameof(TakeOver), .3f);
      return;
    }
    Debug.Log("[NetSession] lost the host; searching again" + (hadPatch && rank > 0 ? " (place " + rank + " in line)" : ""));
    SetState(State.Searching);
    if (hadPatch && rank > 1) {
      migrationRank = rank;
      Invoke(nameof(StartMigrationSearch), .3f);
      return;
    }
    Invoke(nameof(StartSearch), 1f + Random.Range(0f, 2f));
  }

  void TakeOver() {
    if (state != State.Searching) return;
    EnsureNetwork();
    NetSync.keepIds = true;
    migratingHost = true;
    BecomeHost();
  }

  // like StartLocal, but wait long enough for the next in line to be advertising (and
  // a bit longer the further back in line, in case it left too)
  int migrationRank = 2;
  void StartMigrationSearch() {
    if (CurrentMode == Mode.Internet) { StartInternet(3f + 2f * migrationRank); return; }
    EnsureNetwork();
    CancelInvoke();
    found.Clear();
    SetState(State.Searching);
    discovery.StartDiscovery();
    Invoke(nameof(Decide), 3f + 2f * migrationRank);
  }

  void SetState(State s) {
    state = s;
    if (s != State.Searching) sessionFull = false;
    RaiseStatus();
  }

  // ---- internet (NetInternet + UtpRelayTransport) ----

  // Sign in, list the sessions in our room, join the best one, otherwise host. Every
  // await can outlive the search (button pressed, mode changed): searchRun says whether
  // this one is still wanted.
  async void StartInternet(float wait) {
    EnsureNetwork();
    CancelInvoke();
    found.Clear();
    discovery.StopDiscovery();
    UseTransport(utp);
    SetState(State.Searching);
    int run = ++searchRun;
    Debug.Log("[NetSession] searching the internet, room " + NetInternet.Code + (wait > 0 ? " (in " + wait + " s)" : ""));
    if (!await NetInternet.SignIn()) { RetryInternetLater(run); return; }
    if (wait > 0) await Task.Delay((int)(wait * 1000));
    if (!Still(run)) return;
    NetInternet.status = "SEARCHING...";
    List<NetInternet.Found> list = await NetInternet.List();
    if (!Still(run)) return;
    if (list.Count == 0) {
      // nobody yet: look once more after a random wait, so two headsets started together
      // don't both host (and if they do, the lone-host check below merges them)
      await Task.Delay(Random.Range(1000, 3000));
      if (!Still(run)) return;
      list = await NetInternet.List();
      if (!Still(run)) return;
    }
    bool anyFull = false;
    foreach (NetInternet.Found f in list) {
      if (IsFull(f.players)) { anyFull = true; continue; }
      if (await NetInternet.PrepareJoin(utp, f)) {
        if (!Still(run)) return;
        JoinInternet(f);
        return;
      }
      if (!Still(run)) return;
    }
    if (anyFull) { NetInternet.status = ""; WaitWhileFull(); return; }
    HostInternet(run);
  }

  bool Still(int run) { return run == searchRun && state == State.Searching && CurrentMode == Mode.Internet; }

  void RetryInternetLater(int run) {
    if (!Still(run)) return;
    Debug.Log("[NetSession] internet not reachable (" + NetInternet.lastError + "): trying again in 10 s");
    Invoke(nameof(StartSearch), 10f);
  }

  void JoinInternet(NetInternet.Found f) {
    joinedLobby = f.lobbyId;
    NetInternet.status = "";
    SetState(State.Client);
    manager.StartClient();
  }

  bool migratingHost; // TakeOver: same session (same hour), not a new one

  async void HostInternet(int run) {
    if (run != searchRun) return;
    bool migration = migratingHost;
    migratingHost = false;
    SetState(State.Searching);
    if (!await NetInternet.PrepareHost(utp)) { RetryInternetLater(run); return; }
    if (!Still(run)) return;
    UseTransport(utp);
    if (migration) NetLimits.TookOver(); else NetLimits.SessionStarted();
    SetState(State.Host);
    manager.StartHost();
    await NetInternet.Publish();
    if (run != searchRun || state != State.Host) { NetInternet.Unpublish(); return; }
    InvokeRepeating(nameof(ProbeInternet), 10f, 10f);
  }

  // Internet version of ProbeOtherHosts: same rules, lobby IDs instead of server IDs.
  async void ProbeInternet() {
    if (state != State.Host || NetworkServer.connections.Count > 1) return;
    int run = searchRun;
    List<NetInternet.Found> list = await NetInternet.List();
    if (run != searchRun || state != State.Host || NetworkServer.connections.Count > 1) return;
    foreach (NetInternet.Found f in list) {
      if (IsFull(f.players)) continue; // can't join it anyway
      if (f.players > 1) Debug.Log("[NetSession] another session in room " + NetInternet.Code + " has " + f.players + " players: joining it");
      else if (NetInternet.OurIdIsHigher(f.lobbyId)) Debug.Log("[NetSession] another lone host in room " + NetInternet.Code + ": joining it instead");
      else continue;
      CancelInvoke();
      run = ++searchRun;
      NetInternet.Unpublish();
      SetState(State.Searching);
      manager.StopHost();
      if (await NetInternet.PrepareJoin(utp, f) && run == searchRun && state == State.Searching) JoinInternet(f);
      else if (run == searchRun) StartInternet(0f);
      return;
    }
  }

  // NetLimits: the session hit its idle / 3-hour limit. Leave and stay out until someone
  // presses MULTIPLAYER ON again (the patch stays as it is).
  public void EndInternet(int reason) {
    if (state == State.Ended) return;
    NetLimits.endedReason = reason;
    StopAll();
    SetState(State.Ended);
  }

  int pendingEnd;
  public void EndInternetSoon(int reason) { // host: let the "ended" message go out first
    if (pendingEnd != 0) return;
    pendingEnd = reason;
    Invoke(nameof(EndPending), .5f);
  }
  void EndPending() { int r = pendingEnd; pendingEnd = 0; EndInternet(r); }

  void OnApplicationQuit() {
    NetInternet.Unpublish(); // best effort: an unremoved session drops out of the list ~30 s later
  }

  // ---- status ----

  float statusTimer;
  void Update() {
    if (state == State.Host && CurrentMode == Mode.Internet) NetInternet.HostUpdate(NetworkServer.connections.Count);
    if (CurrentMode == Mode.Internet) {
      if (pendingEnd == 0) NetLimits.Tick(state == State.Host, state == State.Host || (state == State.Client && NetworkClient.isConnected));
    }
    statusTimer += Time.unscaledDeltaTime;
    if (statusTimer < 1f) return;
    statusTimer = 0;
    RaiseStatus();
  }

  void RaiseStatus() {
    string s = StatusText();
    if (s == lastStatus) return;
    lastStatus = s;
    StatusChanged?.Invoke();
  }

  public static int PlayerCount { get { return NetPlayer.all.Count; } }

  // The status line under the MULTIPLAYER buttons (same wording for local and internet).
  public static string StatusText() {
    if (instance == null) return "OFFLINE";
    if (instance.state == State.Ended) return NetLimits.EndedText();
    if (CurrentMode == Mode.Off) return "OFFLINE";
    bool internet = CurrentMode == Mode.Internet;
    switch (instance.state) {
      case State.Searching:
        if (instance.sessionFull) return "SESSION FULL (" + NetInternet.MaxPlayers + "/" + NetInternet.MaxPlayers + ")";
        return internet && NetInternet.status != "" ? NetInternet.status : "SEARCHING...";
      case State.Host:
        string h = "HOSTING (" + Mathf.Max(1, PlayerCount) + ")";
        int ping = HostPing();
        if (ping >= 0) h += " " + ping + "MS";
        if (internet && !NetInternet.Listed) h += " NOT LISTED";
        return h;
      case State.Client:
        if (!NetworkClient.isConnected) return "JOINING...";
        return "JOINED (" + PlayerCount + ") " + Mathf.RoundToInt((float)(NetworkTime.rtt * 1000)) + "MS";
    }
    return "OFFLINE";
  }

  // the host's average round trip to the players who joined it; -1 with nobody joined
  static int HostPing() {
    double sum = 0;
    int n = 0;
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values) {
      if (c == null || c == NetworkServer.localConnection) continue;
      sum += c.rtt;
      n++;
    }
    return n == 0 ? -1 : Mathf.RoundToInt((float)(sum / n * 1000));
  }
}

public class SoundStageNetworkManager : NetworkManager {
  public override void OnStartServer() {
    base.OnStartServer();
    NetSync.RegisterServer();
  }

  public override void OnStartClient() {
    base.OnStartClient();
    NetSync.RegisterClient();
  }

  // a joining player is ready for messages: send them the patch
  public override void OnServerReady(NetworkConnectionToClient conn) {
    base.OnServerReady(conn);
    NetSync.ServerSendPatch(conn);
    NetSync.SendSuccession();
    NetLimits.ServerWelcome(conn);
  }

  public override void OnServerConnect(NetworkConnectionToClient conn) {
    base.OnServerConnect(conn);
    Debug.Log("[NetSession] player connected: " + conn.address);
  }

  public override void OnServerDisconnect(NetworkConnectionToClient conn) {
    Debug.Log("[NetSession] player left: " + conn.address);
    base.OnServerDisconnect(conn);
    NetSync.SendSuccession();
    NetRoom.ServerDisconnected(conn);
  }

  public override void OnClientDisconnect() {
    base.OnClientDisconnect();
    if (NetSession.instance != null && !NetworkServer.active) NetSession.instance.OnClientConnectionLost();
  }
}
