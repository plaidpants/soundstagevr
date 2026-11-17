// Internet sessions through Unity Gaming Services (milestone 5).
//
// Same idea as the local network, no codes to type: everyone on the same build who picks
// the same ROOM number (1-9, settings panel) ends up in one session. A host publishes a
// public Lobby carrying its Relay join code, tagged with the network version (S1), the
// room (S2) and its player count (N1); a searching headset lists the lobbies for its
// room, joins the fullest / newest one's relay, and otherwise hosts.
//
// - Sign-in is anonymous (a per-install player ID kept by Unity's Authentication).
// - The host heartbeats its lobby every 15 s; a lobby that stops (host crashed / lost)
//   drops out of the list after ~30 s. A lobby whose relay we couldn't join is skipped
//   for 2 minutes.
// - Host migration reuses NetSession's: the next in line hosts a new lobby, the others
//   search again and find it.
// - A lone host checks the list every 10 s for another session in the room (two
//   headsets started together), with the same tie-break as on the local network.
//
// Needs the project linked to Unity Cloud (Project Settings > Services) with Relay and
// Lobby turned on in the dashboard.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public static class NetInternet {
  public const int MaxPlayers = 4;
  const string Protocol = "dtls";

  public static string status = "";      // shown on the settings button while busy
  public static string lastError = "";
  public static string LobbyId { get; private set; }

  static readonly Dictionary<string, float> badLobbies = new Dictionary<string, float>();
  static float heartbeatTimer, playersTimer, publishRetry;
  static bool hosting;                    // relay host running; LobbyId null = not listed yet
  static int publishedPlayers = -1;
  static bool busy;

  public static int Room {
    get { return Mathf.Clamp(PlayerPrefs.GetInt("netRoom", 1), 1, 9); }
    set { PlayerPrefs.SetInt("netRoom", Mathf.Clamp(value, 1, 9)); }
  }

  static string Version { get { return ((uint)NetSession.Handshake).ToString("X8"); } }

  // ---- sign-in ----

  public static async Task<bool> SignIn() {
    try {
      if (UnityServices.State == ServicesInitializationState.Uninitialized) {
        status = "CONNECTING...";
        await UnityServices.InitializeAsync();
      }
      while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Delay(100);
      if (!AuthenticationService.Instance.IsSignedIn) {
        status = "SIGNING IN...";
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        Debug.Log("[NetInternet] signed in as " + AuthenticationService.Instance.PlayerId);
      }
      return true;
    } catch (Exception e) {
      Fail("sign-in failed", e);
      return false;
    }
  }

  // ---- finding a session ----

  public struct Found { public string lobbyId, joinCode; public int players; public DateTime created; }

  // Sessions in our room, best first (most players, then newest).
  public static async Task<List<Found>> List() {
    var result = new List<Found>();
    try {
      var opts = new QueryLobbiesOptions {
        Count = 10,
        Filters = new List<QueryFilter> {
          new QueryFilter(QueryFilter.FieldOptions.S1, Version, QueryFilter.OpOptions.EQ),
          new QueryFilter(QueryFilter.FieldOptions.S2, Room.ToString(), QueryFilter.OpOptions.EQ),
          new QueryFilter(QueryFilter.FieldOptions.N1, MaxPlayers.ToString(), QueryFilter.OpOptions.LT),
        },
        Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) },
      };
      QueryResponse r = await LobbyService.Instance.QueryLobbiesAsync(opts);
      foreach (Lobby l in r.Results) {
        if (l.Id == LobbyId) continue; // our own
        if (badLobbies.TryGetValue(l.Id, out float until) && Time.realtimeSinceStartup < until) continue;
        if (l.Data == null || !l.Data.TryGetValue("relay", out DataObject code)) continue;
        int n = 1;
        if (l.Data.TryGetValue("players", out DataObject pl)) int.TryParse(pl.Value, out n);
        result.Add(new Found { lobbyId = l.Id, joinCode = code.Value, players = n, created = l.Created });
      }
      result.Sort((a, b) => a.players != b.players ? b.players.CompareTo(a.players) : b.created.CompareTo(a.created));
    } catch (Exception e) {
      Fail("couldn't list sessions", e);
    }
    return result;
  }

  public static void MarkBad(string lobbyId) {
    if (!string.IsNullOrEmpty(lobbyId)) badLobbies[lobbyId] = Time.realtimeSinceStartup + 120f;
  }

  // ---- relay ----

  public static async Task<bool> PrepareJoin(UtpRelayTransport utp, Found f) {
    try {
      status = "JOINING...";
      JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(f.joinCode);
      utp.SetRelayData(a.ToRelayServerData(Protocol), false);
      Debug.Log("[NetInternet] joining room " + Room + " session " + f.lobbyId + " (" + f.players + " players), relay " + a.RelayServer.IpV4);
      return true;
    } catch (Exception e) {
      MarkBad(f.lobbyId);
      Fail("couldn't join that session's relay", e);
      return false;
    }
  }

  // Allocates a relay for hosting; the lobby is published once the host is running.
  static string hostJoinCode;
  public static async Task<bool> PrepareHost(UtpRelayTransport utp) {
    try {
      status = "STARTING...";
      Allocation a = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
      hostJoinCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
      utp.SetRelayData(a.ToRelayServerData(Protocol), true);
      Debug.Log("[NetInternet] relay allocated in " + a.Region + " (join code " + hostJoinCode + ")");
      return true;
    } catch (Exception e) {
      Fail("couldn't get a relay", e);
      return false;
    }
  }

  public static bool Listed { get { return !string.IsNullOrEmpty(LobbyId); } }

  public static async Task Publish() {
    hosting = true;
    publishRetry = 0;
    try {
      var data = new Dictionary<string, DataObject> {
        { "ver", new DataObject(DataObject.VisibilityOptions.Public, Version, DataObject.IndexOptions.S1) },
        { "room", new DataObject(DataObject.VisibilityOptions.Public, Room.ToString(), DataObject.IndexOptions.S2) },
        { "players", new DataObject(DataObject.VisibilityOptions.Public, "1", DataObject.IndexOptions.N1) },
        { "relay", new DataObject(DataObject.VisibilityOptions.Public, hostJoinCode) },
      };
      Lobby l = await LobbyService.Instance.CreateLobbyAsync("SoundStage room " + Room, MaxPlayers,
        new CreateLobbyOptions { IsPrivate = false, Data = data });
      LobbyId = l.Id;
      publishedPlayers = 1;
      heartbeatTimer = playersTimer = 0;
      status = "";
      Debug.Log("[NetInternet] hosting room " + Room + " (session " + l.Id + ")");
    } catch (Exception e) {
      // nobody can find us until this works: HostUpdate tries again every 15 s
      Fail("couldn't publish the session (is Lobby turned on in the Unity dashboard?)", e);
    }
  }

  // Called by NetSession every frame while hosting over the internet.
  public static void HostUpdate(int players) {
    if (busy) return;
    if (string.IsNullOrEmpty(LobbyId)) {
      if (!hosting || string.IsNullOrEmpty(hostJoinCode)) return;
      publishRetry += Time.unscaledDeltaTime;
      if (publishRetry < 15f) return;
      publishRetry = 0;
      busy = true;
      Publish().ContinueWith(_ => busy = false, TaskScheduler.FromCurrentSynchronizationContext());
      return;
    }
    heartbeatTimer += Time.unscaledDeltaTime;
    playersTimer += Time.unscaledDeltaTime;
    if (heartbeatTimer >= 15f) {
      heartbeatTimer = 0;
      Run(async () => await LobbyService.Instance.SendHeartbeatPingAsync(LobbyId), "heartbeat failed");
    } else if (players != publishedPlayers && playersTimer >= 2f) {
      playersTimer = 0;
      publishedPlayers = players;
      string id = LobbyId;
      Run(async () => await LobbyService.Instance.UpdateLobbyAsync(id, new UpdateLobbyOptions {
        Data = new Dictionary<string, DataObject> {
          { "players", new DataObject(DataObject.VisibilityOptions.Public, players.ToString(), DataObject.IndexOptions.N1) }
        }
      }), "couldn't update the player count");
    }
  }

  // Stop hosting: take the lobby down so nobody tries to join it.
  public static void Unpublish() {
    hosting = false;
    string id = LobbyId;
    LobbyId = null;
    publishedPlayers = -1;
    if (string.IsNullOrEmpty(id)) return;
    Run(async () => await LobbyService.Instance.DeleteLobbyAsync(id), "couldn't remove the session");
    Debug.Log("[NetInternet] session " + id + " closed");
  }

  // A lobby's ID compares the same way on every headset, like the local server ID.
  public static bool OurIdIsHigher(string other) {
    if (!Listed) return true; // we aren't listed (yet): the listed one wins
    return string.CompareOrdinal(LobbyId ?? "", other ?? "") > 0;
  }

  static async void Run(Func<Task> f, string what) {
    busy = true;
    try { await f(); } catch (Exception e) { Fail(what, e); } finally { busy = false; }
  }

  static void Fail(string what, Exception e) {
    lastError = what;
    status = "";
    string msg = e.Message;
    if (e is LobbyServiceException le) msg = le.Reason + ": " + le.Message;
    else if (e is RelayServiceException re) msg = re.Reason + ": " + re.Message;
    Debug.LogWarning("[NetInternet] " + what + ": " + msg);
  }
}
