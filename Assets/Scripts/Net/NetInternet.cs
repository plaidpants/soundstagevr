// Internet sessions through Unity Gaming Services (milestone 5).
//
// Private rooms with codes (2026-11-18): everyone on the same build who enters the same
// 6-character ROOM CODE ends up in one session; whoever comes first hosts. A code is made
// with NEW (or typed in) and stays valid for as long as people keep it - recent codes are
// remembered. Rooms can't be found or joined without the code:
// - the host publishes a public Lobby tagged with the network version (S1), a slow hash
//   of the code (S2, PBKDF2 - so a listing doesn't give the code away) and its player
//   count (N1);
// - its Relay join code is stored encrypted with a key made from the code, so only
//   someone who knows the code can connect.
// A searching headset lists the lobbies with its code's hash, joins the fullest / newest
// one's relay, and otherwise hosts.
//
// - Sign-in is anonymous (a per-install player ID kept by Unity's Authentication);
//   ForgetId() deletes it from Unity (privacy policy: FORGET MY INTERNET ID).
// - The host heartbeats its lobby every 15 s; a lobby that stops (host crashed / lost)
//   drops out of the list after ~30 s. A lobby whose relay we couldn't join is skipped
//   for 2 minutes.
// - Host migration reuses NetSession's: the next in line hosts a new lobby (same code),
//   the others search again and find it.
// - A lone host checks the list every 10 s for another session with its code (two
//   headsets started together), with the same tie-break as on the local network.
//
// Needs the project linked to Unity Cloud (Project Settings > Services) with Relay and
// Lobby turned on in the dashboard.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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

  // ---- room codes ----

  public const int CodeLength = 6;
  // no 0/O, 1/I: easy to read out and type
  public const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

  // the code in use (the last one joined); "" before the first
  public static string Code {
    get { return Normalize(PlayerPrefs.GetString("netCode", "")); }
    set {
      string c = Normalize(value);
      if (c == Code) return;
      PlayerPrefs.SetString("netCode", c);
      AddRecent(c);
      lookup = null; // new code: work out its hash and key again
    }
  }

  // upper case, only code characters (a pasted or typed code: o -> 0 isn't a code character)
  public static string Normalize(string c) {
    var sb = new StringBuilder();
    foreach (char ch in (c ?? "").ToUpperInvariant()) if (CodeChars.IndexOf(ch) >= 0) sb.Append(ch);
    return sb.Length > CodeLength ? sb.ToString(0, CodeLength) : sb.ToString();
  }

  public static bool Valid(string c) { return Normalize(c).Length == CodeLength && Normalize(c) == c; }

  public static string NewCode() {
    var b = new byte[CodeLength];
    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
    var sb = new StringBuilder();
    foreach (byte x in b) sb.Append(CodeChars[x % CodeChars.Length]); // 256 % 32 == 0: no bias
    return sb.ToString();
  }

  public const int MaxRecent = 4;
  public static List<string> Recent() {
    var l = new List<string>();
    foreach (string c in PlayerPrefs.GetString("netRecentCodes", "").Split(','))
      if (Valid(c) && !l.Contains(c)) l.Add(c);
    return l;
  }

  static void AddRecent(string c) {
    if (!Valid(c)) return;
    List<string> l = Recent();
    l.Remove(c);
    l.Insert(0, c);
    if (l.Count > MaxRecent) l.RemoveRange(MaxRecent, l.Count - MaxRecent);
    PlayerPrefs.SetString("netRecentCodes", string.Join(",", l));
  }

  // What the code becomes for the Lobby: a tag to look the room up by, and a key for the
  // relay join code. PBKDF2 makes guessing codes from a lobby listing slow. Worked out
  // once per code, off the main thread.
  class Lookup { public string code, tag; public byte[] key; }
  static Lookup lookup;
  static async Task<Lookup> CodeLookup() {
    string c = Code;
    if (lookup != null && lookup.code == c) return lookup;
    Lookup l = await Task.Run(() => {
      byte[] salt = Encoding.UTF8.GetBytes("SoundStage room code v1");
      using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(c), salt, 20000, HashAlgorithmName.SHA256)) {
        byte[] d = kdf.GetBytes(40);
        var tag = new StringBuilder();
        for (int i = 0; i < 8; i++) tag.Append(d[i].ToString("x2"));
        byte[] key = new byte[32];
        Array.Copy(d, 8, key, 0, 32);
        return new Lookup { code = c, tag = tag.ToString(), key = key };
      }
    });
    lookup = l;
    return l;
  }

  // The relay join code, encrypted: "nonce:ciphertext:mac" (hex). Keystream and MAC are
  // HMAC-SHA256 with the code's key.
  static string Seal(string text, byte[] key) {
    byte[] nonce = new byte[12];
    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
    byte[] plain = Encoding.UTF8.GetBytes(text);
    byte[] ks = Keystream(key, nonce, plain.Length);
    for (int i = 0; i < plain.Length; i++) plain[i] ^= ks[i];
    return Hex(nonce) + ":" + Hex(plain) + ":" + Hex(Mac(key, nonce, plain));
  }

  static string Open(string sealedText, byte[] key) {
    try {
      string[] p = sealedText.Split(':');
      if (p.Length != 3) return null;
      byte[] nonce = Unhex(p[0]), data = Unhex(p[1]), mac = Unhex(p[2]);
      if (nonce == null || data == null || mac == null || data.Length > 64) return null;
      byte[] want = Mac(key, nonce, data);
      int diff = mac.Length ^ want.Length;
      for (int i = 0; i < Math.Min(mac.Length, want.Length); i++) diff |= mac[i] ^ want[i];
      if (diff != 0) return null; // another code's room (or tampered)
      byte[] ks = Keystream(key, nonce, data.Length);
      for (int i = 0; i < data.Length; i++) data[i] ^= ks[i];
      return Encoding.UTF8.GetString(data);
    } catch { return null; }
  }

  static byte[] Keystream(byte[] key, byte[] nonce, int n) {
    using (var h = new HMACSHA256(key)) {
      byte[] m = new byte[nonce.Length + 1];
      Array.Copy(nonce, m, nonce.Length);
      m[nonce.Length] = 1;
      byte[] k = h.ComputeHash(m);
      if (n > k.Length) throw new ArgumentException("too long");
      return k;
    }
  }

  static byte[] Mac(byte[] key, byte[] nonce, byte[] data) {
    using (var h = new HMACSHA256(key)) {
      byte[] m = new byte[nonce.Length + 1 + data.Length];
      Array.Copy(nonce, m, nonce.Length);
      m[nonce.Length] = 2;
      Array.Copy(data, 0, m, nonce.Length + 1, data.Length);
      return h.ComputeHash(m);
    }
  }

  static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (byte x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
  static byte[] Unhex(string s) {
    if (s == null || s.Length % 2 != 0) return null;
    var b = new byte[s.Length / 2];
    for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
    return b;
  }

  // ---- your anonymous ID (privacy) ----

  public static string PlayerId {
    get {
      try { return UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : ""; }
      catch { return ""; }
    }
  }

  // FORGET MY INTERNET ID: deletes this install's anonymous player from Unity (a new one
  // is made next time). Call with multiplayer off.
  public static async Task<bool> ForgetId() {
    try {
      if (!await SignIn()) return false;
      string id = AuthenticationService.Instance.PlayerId;
      await AuthenticationService.Instance.DeleteAccountAsync();
      Debug.Log("[NetInternet] deleted the anonymous player " + id + " from Unity");
      return true;
    } catch (Exception e) {
      Fail("couldn't delete the internet ID", e);
      return false;
    }
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

  // Sessions with our code, best first (most players, then newest).
  // Lobby allows about one query a second per player: space them out (the room list and
  // the search would otherwise collide and get "request_rate_limited").
  static float lastQuery = -10f;
  static async Task WaitForQuerySlot() {
    // reserve the next free slot first, so two callers waiting at once don't collide
    float now = Time.realtimeSinceStartup;
    float slot = Mathf.Max(now, lastQuery + 1.2f);
    lastQuery = slot;
    if (slot > now) await Task.Delay((int)((slot - now) * 1000));
  }

  public static async Task<List<Found>> List() {
    var result = new List<Found>();
    try {
      Lookup code = await CodeLookup();
      await WaitForQuerySlot();
      var opts = new QueryLobbiesOptions {
        Count = 10,
        Filters = new List<QueryFilter> {
          new QueryFilter(QueryFilter.FieldOptions.S1, Version, QueryFilter.OpOptions.EQ),
          new QueryFilter(QueryFilter.FieldOptions.S2, code.tag, QueryFilter.OpOptions.EQ),
          new QueryFilter(QueryFilter.FieldOptions.N1, MaxPlayers.ToString(), QueryFilter.OpOptions.LT),
        },
        Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) },
      };
      QueryResponse r = await LobbyService.Instance.QueryLobbiesAsync(opts);
      foreach (Lobby l in r.Results) {
        if (l.Id == LobbyId) continue; // our own
        if (badLobbies.TryGetValue(l.Id, out float until) && Time.realtimeSinceStartup < until) continue;
        if (l.Data == null || !l.Data.TryGetValue("relay", out DataObject sealedCode)) continue;
        string joinCode = Open(sealedCode.Value, code.key);
        if (joinCode == null) continue; // not this code's room
        int n = 1;
        if (l.Data.TryGetValue("players", out DataObject pl)) int.TryParse(pl.Value, out n);
        result.Add(new Found { lobbyId = l.Id, joinCode = joinCode, players = n, created = l.Created });
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
      Debug.Log("[NetInternet] joining room " + Code + " session " + f.lobbyId + " (" + f.players + " players), relay " + a.RelayServer.IpV4);
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
      Lookup code = await CodeLookup();
      var data = new Dictionary<string, DataObject> {
        { "ver", new DataObject(DataObject.VisibilityOptions.Public, Version, DataObject.IndexOptions.S1) },
        { "room", new DataObject(DataObject.VisibilityOptions.Public, code.tag, DataObject.IndexOptions.S2) },
        { "players", new DataObject(DataObject.VisibilityOptions.Public, "1", DataObject.IndexOptions.N1) },
        { "relay", new DataObject(DataObject.VisibilityOptions.Public, Seal(hostJoinCode, code.key)) },
      };
      Lobby l = await LobbyService.Instance.CreateLobbyAsync("SoundStage", MaxPlayers,
        new CreateLobbyOptions { IsPrivate = false, Data = data });
      LobbyId = l.Id;
      publishedPlayers = 1;
      heartbeatTimer = playersTimer = 0;
      status = "";
      Debug.Log("[NetInternet] hosting room " + Code + " (session " + l.Id + ")");
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
