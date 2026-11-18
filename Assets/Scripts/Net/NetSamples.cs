// Sample transfer between players (milestone 4, step 3).
//
// A tape whose sample this machine doesn't have (no local file with its hash, nothing in
// SampleCache) asks for it by hash. Everything goes through the host:
//   joined headset  --Want(hash)-->  host
//   host has it     --Chunks-->      that headset
//   host doesn't    --Ask(hash)-->   the other players; the first that has it uploads it
//                   <--Chunks--      to the host, which checks and stores it
//                                    (SampleCache.Accept) and then sends it on.
//   nobody has it   --Missing-->     the headset (tape shows MISSING)
// Every receiver checks the bytes against the hash it asked for (SampleCache.Accept), so a
// player can't send something else under a sample's name. Only hashes that were asked
// for are accepted (an Ask is the only way a client uploads; a Want the only way it
// receives); sizes are capped (SampleCache.MaxReceiveBytes, 300 MB per session; over the
// internet 5 MB per sample - a 30 MB transfer through the relay got players dropped, and
// the relay is metered - so bigger ones show TOO BIG there).
// What's sent are the original bytes (a local file, or the verified original kept in the
// cache), never our re-encoded WAV - so the next receiver's hash check holds.
// Sending is paced (fast on the local network, gentler over the metered relay) and runs
// a chunk at a time from Update, so it never holds up the music; over the relay a chunk
// only goes when the previous ones have left, so a big file never queues up in front of
// controls, notes and keep-alives.
// A transfer cut off (connection lost) keeps what arrived: asking again resumes from
// there ('from' in Want / Ask) - the hash check covers the whole file in the end. Samples
// still missing are asked for again when this machine joins a session or a player joins.

using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

public struct NetSampleWantMsg : NetworkMessage { public string hash; public long from; }
public struct NetSampleAskMsg : NetworkMessage { public string hash; public long from; }
public struct NetSampleChunkMsg : NetworkMessage { public string hash; public long size; public long offset; public byte[] data; }
public struct NetSampleMissingMsg : NetworkMessage { public string hash; public byte reason; } // 0 nobody has it, 1 too big / refused

public class NetSamples : MonoBehaviour {
  public enum State { None, Loading, Missing, NoSpace, TooBig }

  public const int ChunkBytes = 16 * 1024;
  public const long SessionLimit = 300L * 1024 * 1024;
  public const long InternetMaxBytes = 5L * 1024 * 1024;  // per sample, through the relay
  static bool OverRelay { get { return Transport.active is UtpRelayTransport; } }
  static long MaxBytes { get { return OverRelay ? InternetMaxBytes : SampleCache.MaxReceiveBytes; } }
  const float AskTimeout = 6f;      // host: nobody started sending by then -> missing
  const float StallTimeout = 30f;   // a transfer with no chunk for this long is dropped

  public static event Action<string> Arrived;              // the sample is now here
  public static event Action<string, State> StateChanged;  // for tape markers

  static NetSamples instance;
  static readonly Dictionary<string, State> states = new Dictionary<string, State>();
  static long receivedThisSession;

  // a file being received
  class Incoming { public string path; public long size, written; public float lastChunk; public FileStream stream; public int fromConn; }
  // a file being sent: to a client (conn) or, from a client, to the host (conn == null)
  class Outgoing { public NetworkConnectionToClient conn; public string hash, path; public long size, offset; }

  static readonly Dictionary<string, Incoming> incoming = new Dictionary<string, Incoming>(); // by hash
  static readonly Dictionary<string, Incoming> partials = new Dictionary<string, Incoming>(); // cut off: resume from here
  static long ResumeFrom(string hash) { return partials.TryGetValue(hash, out Incoming p) ? p.written : 0; }
  static readonly List<Outgoing> outgoing = new List<Outgoing>();

  // host: who's waiting for a hash (connection IDs; -1 = the host itself), and when it asked
  static readonly Dictionary<string, HashSet<int>> waiting = new Dictionary<string, HashSet<int>>();
  static readonly Dictionary<string, float> askedAt = new Dictionary<string, float>();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("NetSamples");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetSamples>();
  }

  public static State StateOf(string hash) { return states.TryGetValue(hash, out State s) ? s : State.None; }

  static void SetState(string hash, State s) {
    if (StateOf(hash) == s) return;
    states[hash] = s;
    StateChanged?.Invoke(hash, s);
  }

  static void Done(string hash) {
    states.Remove(hash);
    Debug.Log("[NetSamples] " + Short(hash) + " is here");
    Arrived?.Invoke(hash);
  }

  static string Short(string h) { return h != null && h.Length > 10 ? h.Substring(0, 10) : h; }

  // ---- asking ----

  // A tape here needs this sample (not on this machine). Safe to call repeatedly.
  public static void Need(string hash) {
    if (string.IsNullOrEmpty(hash)) return;
    State s = StateOf(hash);
    if (s == State.Loading) return;
    if (s == State.TooBig && OverRelay) return; // won't get smaller
    if (SampleIndex.LocalSource(hash) != null || SampleCache.Find(hash) != null) { Done(hash); return; }
    // one of the app's own samples (deleted here, or newer than this library): from the
    // bundled archive, no network needed (BuiltinSamples)
    if (BuiltinSamples.Has(hash) && !builtinTried.Contains(hash)) {
      builtinTried.Add(hash);
      SetState(hash, State.Loading);
      RestoreBuiltin(hash);
      return;
    }
    Ask(hash);
  }

  static readonly HashSet<string> builtinTried = new HashSet<string>(); // once per run: if that fails, ask the players

  static async void RestoreBuiltin(string hash) {
    bool ok = false;
    try { ok = await BuiltinSamples.Restore(hash); } catch (Exception e) { Debug.LogWarning("[NetSamples] built-in restore failed: " + e.Message); }
    if (ok) { Done(hash); return; }
    states.Remove(hash);
    Ask(hash);
  }

  static void Ask(string hash) {
    if (!NetSync.InSession) { SetState(hash, State.Missing); return; }
    SetState(hash, State.Loading);
    Debug.Log("[NetSamples] need " + Short(hash) + ": asking " + (NetworkServer.active ? "the other players" : "the host"));
    if (NetworkServer.active) HostWant(hash, -1, 0);
    else NetworkClient.Send(new NetSampleWantMsg { hash = hash, from = ResumeFrom(hash) });
  }

  // ---- registration (NetSync) ----

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetSampleWantMsg>((conn, m) => { if (IsHash(m.hash)) HostWant(m.hash, conn.connectionId, m.from); });
    NetworkServer.RegisterHandler<NetSampleChunkMsg>((conn, m) => OnHostChunk(conn, m));
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetSampleAskMsg>(m => OnAsk(m.hash, m.from));
    NetworkClient.RegisterHandler<NetSampleChunkMsg>(m => OnClientChunk(m));
    NetworkClient.RegisterHandler<NetSampleMissingMsg>(m => {
      if (StateOf(m.hash) != State.Loading) return;
      Debug.Log("[NetSamples] " + Short(m.hash) + ": " + (m.reason == 2 ? "too big to send over the internet" : m.reason == 1 ? "refused (too big)" : "nobody in the session has it"));
      SetState(m.hash, m.reason == 2 ? State.TooBig : State.Missing);
    });
  }

  static bool IsHash(string h) {
    if (h == null || h.Length != 64) return false;
    foreach (char c in h) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
    return true;
  }

  // ---- host ----

  // who wants it from where (a resumed download starts part-way)
  static readonly Dictionary<string, Dictionary<int, long>> wantFrom = new Dictionary<string, Dictionary<int, long>>();

  static void HostWant(string hash, int connId, long from) {
    string src = SampleIndex.LocalSource(hash);
    if (src != null) {
      if (connId >= 0) SendTo(connId, hash, src, from);
      return;
    }
    if (connId >= 0) {
      if (!wantFrom.TryGetValue(hash, out Dictionary<int, long> wf)) wantFrom[hash] = wf = new Dictionary<int, long>();
      wf[connId] = from;
    }
    if (!waiting.TryGetValue(hash, out HashSet<int> set)) waiting[hash] = set = new HashSet<int>();
    set.Add(connId);
    if (incoming.ContainsKey(hash)) return; // already coming
    if (askedAt.TryGetValue(hash, out float t) && Time.realtimeSinceStartup - t < AskTimeout) return;
    askedAt[hash] = Time.realtimeSinceStartup;
    int asked = 0;
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values) {
      if (c == null || c == NetworkServer.localConnection || c.connectionId == connId) continue;
      c.Send(new NetSampleAskMsg { hash = hash, from = ResumeFrom(hash) });
      asked++;
    }
    Debug.Log("[NetSamples] host: asked " + asked + " player(s) for " + Short(hash));
    if (asked == 0) HostMissing(hash, 0);
  }

  static void SendTo(int connId, string hash, string src, long from) {
    if (!NetworkServer.connections.TryGetValue(connId, out NetworkConnectionToClient c) || c == null) return;
    outgoing.RemoveAll(o => o.conn == c && o.hash == hash); // asked again: start over from 'from'
    long size = new FileInfo(src).Length;
    if (size > MaxBytes) {
      c.Send(new NetSampleMissingMsg { hash = hash, reason = (byte)(size > SampleCache.MaxReceiveBytes ? 1 : 2) });
      if (size <= SampleCache.MaxReceiveBytes) Debug.Log("[NetSamples] not sending " + Short(hash) + " (" + (size >> 10) + " KB): over " + (InternetMaxBytes >> 20) + " MB on the internet");
      return;
    }
    if (from < 0 || from >= size) from = 0;
    outgoing.Add(new Outgoing { conn = c, hash = hash, path = src, size = size, offset = from });
    Debug.Log("[NetSamples] sending " + Short(hash) + " (" + (size >> 10) + " KB" + (from > 0 ? ", from " + (from >> 10) + " KB" : "") + ") to " + c.address);
  }

  static void HostMissing(string hash, byte reason) {
    askedAt.Remove(hash);
    if (!waiting.TryGetValue(hash, out HashSet<int> set)) return;
    waiting.Remove(hash);
    wantFrom.Remove(hash);
    foreach (int id in set) {
      if (id < 0) SetState(hash, reason == 1 ? State.NoSpace : reason == 2 ? State.TooBig : State.Missing);
      else if (NetworkServer.connections.TryGetValue(id, out NetworkConnectionToClient c) && c != null)
        c.Send(new NetSampleMissingMsg { hash = hash, reason = reason });
    }
    Debug.Log("[NetSamples] host: " + Short(hash) + (reason == 1 ? " couldn't be stored" : reason == 2 ? " is too big for the internet" : ": nobody has it"));
  }

  // an upload from a client, after an Ask
  static void OnHostChunk(NetworkConnectionToClient conn, NetSampleChunkMsg m) {
    if (!IsHash(m.hash) || !waiting.ContainsKey(m.hash)) return; // never asked for: ignore
    if (incoming.TryGetValue(m.hash, out Incoming inc) && inc.fromConn != conn.connectionId) return; // another player is already sending it
    // someone answered: the ask can't time out any more (a transfer that stops is the
    // stall timeout's; 2026-11-18: an 866 KB sample still being checked after the last
    // chunk was declared "nobody has it" 6 s after the ask)
    askedAt.Remove(m.hash);
    Receive(m, conn.connectionId, ok => {
      if (ok == null) {
        if (!waiting.TryGetValue(m.hash, out HashSet<int> set)) set = new HashSet<int> { -1 };
        waiting.Remove(m.hash);
        askedAt.Remove(m.hash);
        wantFrom.TryGetValue(m.hash, out Dictionary<int, long> wf);
        wantFrom.Remove(m.hash);
        string src = SampleIndex.LocalSource(m.hash);
        foreach (int id in set) {
          if (id < 0) Done(m.hash);
          else if (src != null) SendTo(id, m.hash, src, wf != null && wf.TryGetValue(id, out long f) ? f : 0);
        }
      } else HostMissing(m.hash, (byte)(ok == "NO SPACE" ? 1 : ok == "too big" && OverRelay ? 2 : 0));
    });
  }

  // ---- clients ----

  // the host asks who has a sample: upload it if this machine does
  static void OnAsk(string hash, long from) {
    if (!IsHash(hash)) return;
    string src = SampleIndex.LocalSource(hash);
    if (src == null) return;
    outgoing.RemoveAll(o => o.conn == null && o.hash == hash); // asked again: start over from 'from'
    long size = new FileInfo(src).Length;
    if (size > MaxBytes) {
      // tell the host it's too big (an empty first chunk with the size: refused there as
      // "too big"), rather than letting it time out and show MISSING
      Debug.Log("[NetSamples] the host needs " + Short(hash) + " (" + (size >> 10) + " KB): too big to send" + (OverRelay ? " over the internet" : ""));
      NetworkClient.Send(new NetSampleChunkMsg { hash = hash, size = size, offset = 0, data = new byte[0] });
      return;
    }
    if (from < 0 || from >= size) from = 0;
    outgoing.Add(new Outgoing { conn = null, hash = hash, path = src, size = size, offset = from });
    Debug.Log("[NetSamples] the host needs " + Short(hash) + ": sending it (" + (size >> 10) + " KB" + (from > 0 ? ", from " + (from >> 10) + " KB" : "") + ")");
  }

  static void OnClientChunk(NetSampleChunkMsg m) {
    if (!IsHash(m.hash) || StateOf(m.hash) != State.Loading) return; // not asked for: ignore
    Receive(m, -1, ok => {
      if (ok == null) Done(m.hash);
      else {
        Debug.LogWarning("[NetSamples] " + Short(m.hash) + " refused: " + ok);
        SetState(m.hash, ok == "NO SPACE" ? State.NoSpace : ok == "too big" && OverRelay ? State.TooBig : State.Missing);
      }
    });
  }

  // ---- receiving (both sides) ----

  static void Receive(NetSampleChunkMsg m, int fromConn, Action<string> finished) {
    Incoming inc;
    if (!incoming.TryGetValue(m.hash, out inc) && partials.TryGetValue(m.hash, out Incoming part)) {
      // resuming a cut-off transfer: the sender starts where we stopped
      partials.Remove(m.hash);
      if (m.offset == part.written && m.size == part.size && File.Exists(part.path)) {
        try {
          part.stream = new FileStream(part.path, FileMode.Append, FileAccess.Write);
          part.fromConn = fromConn;
          incoming[m.hash] = inc = part;
          Debug.Log("[NetSamples] resuming " + Short(m.hash) + " at " + (part.written >> 10) + " of " + (part.size >> 10) + " KB");
        } catch { try { File.Delete(part.path); } catch { } }
      } else { try { File.Delete(part.path); } catch { } }
    }
    if (inc == null) {
      if (m.offset != 0) return;
      if (m.size <= 0 || m.size > MaxBytes || receivedThisSession + m.size > SessionLimit) {
        finished(m.size > MaxBytes ? "too big" : "session limit");
        return;
      }
      inc = new Incoming { path = SampleCache.NewIncomingPath(), size = m.size, fromConn = fromConn };
      try { inc.stream = new FileStream(inc.path, FileMode.CreateNew, FileAccess.Write); }
      catch (Exception e) { Debug.LogWarning("[NetSamples] can't receive: " + e.Message); finished("NO SPACE"); return; }
      incoming[m.hash] = inc;
      Debug.Log("[NetSamples] receiving " + Short(m.hash) + " (" + (m.size >> 10) + " KB)");
    }
    if (m.size != inc.size || m.offset != inc.written || m.data == null || m.data.Length == 0 ||
        m.data.Length > ChunkBytes || inc.written + m.data.Length > inc.size) {
      Abort(m.hash, inc);
      finished("bad transfer");
      return;
    }
    try { inc.stream.Write(m.data, 0, m.data.Length); }
    catch (Exception e) { Abort(m.hash, inc); Debug.LogWarning("[NetSamples] write failed: " + e.Message); finished("NO SPACE"); return; }
    inc.written += m.data.Length;
    inc.lastChunk = Time.realtimeSinceStartup;
    receivedThisSession += m.data.Length;
    if (inc.written < inc.size) return;
    inc.stream.Dispose();
    incoming.Remove(m.hash);
    Finish(m.hash, inc, finished);
  }

  static async void Finish(string hash, Incoming inc, Action<string> finished) {
    string result = await SampleCache.Accept(inc.path, hash, inc.size);
    finished(result);
  }

  static void Abort(string hash, Incoming inc) {
    try { inc.stream?.Dispose(); } catch { }
    try { File.Delete(inc.path); } catch { }
    incoming.Remove(hash);
  }

  // ---- sending, paced ----

  float budget;

  void Update() {
    if (!NetSync.InSession) {
      if (incoming.Count > 0 || outgoing.Count > 0 || waiting.Count > 0) ResetAll();
      receivedThisSession = 0;
      lastPlayers = 0; // so joining again counts as players arriving (retry)
      return;
    }
    float now = Time.realtimeSinceStartup;

    // host: asks nobody answered
    if (NetworkServer.active && askedAt.Count > 0) {
      foreach (string h in new List<string>(askedAt.Keys))
        if (now - askedAt[h] > AskTimeout && !incoming.ContainsKey(h)) HostMissing(h, 0);
    }
    // transfers that stopped
    foreach (string h in new List<string>(incoming.Keys)) {
      Incoming inc = incoming[h];
      if (now - inc.lastChunk > StallTimeout) {
        Debug.LogWarning("[NetSamples] " + Short(h) + " stopped arriving: keeping " + (inc.written >> 10) + " KB to resume");
        Keep(h, inc);
        if (NetworkServer.active) HostMissing(h, 0); else SetState(h, State.Missing);
      }
    }

    // ask again for samples still missing when this machine joins a session or a player
    // joins (they may have it, or a cut-off transfer can resume)
    int players = NetPlayer.all.Count;
    if (players > lastPlayers) retryAt = now + 2f;
    lastPlayers = players;
    if (retryAt > 0 && now >= retryAt && NetSync.Active) {
      retryAt = 0;
      foreach (string h in new List<string>(states.Keys))
        if (states[h] == State.Missing) { Debug.Log("[NetSamples] asking again for " + Short(h)); Need(h); }
    }

    if (outgoing.Count == 0) { budget = 0; return; }
    UtpRelayTransport utp = Transport.active as UtpRelayTransport;
    bool relay = utp != null;
    budget = Mathf.Min(budget + Time.unscaledDeltaTime * (relay ? 150f : 1000f) * 1024f, ChunkBytes * 4);
    while (budget >= ChunkBytes && outgoing.Count > 0) {
      Outgoing o = outgoing[0];
      if (o.conn != null && !NetworkServer.connections.ContainsKey(o.conn.connectionId)) { outgoing.RemoveAt(0); continue; }
      // over the relay: wait until what's queued for that link has left
      if (relay && utp.PendingReliable(o.conn != null ? o.conn.connectionId : 0) > 24) break;
      int n = (int)Math.Min(ChunkBytes, o.size - o.offset);
      byte[] data = new byte[n];
      try {
        using (var f = new FileStream(o.path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
          f.Seek(o.offset, SeekOrigin.Begin);
          int got = 0;
          while (got < n) { int r = f.Read(data, got, n - got); if (r <= 0) break; got += r; }
          if (got < n) throw new IOException("file shorter than announced");
        }
      } catch (Exception e) {
        Debug.LogWarning("[NetSamples] can't send " + Short(o.hash) + ": " + e.Message);
        outgoing.RemoveAt(0);
        continue;
      }
      var msg = new NetSampleChunkMsg { hash = o.hash, size = o.size, offset = o.offset, data = data };
      if (o.conn != null) o.conn.Send(msg); else NetworkClient.Send(msg);
      o.offset += n;
      budget -= n;
      // take turns between transfers
      outgoing.RemoveAt(0);
      if (o.offset < o.size) outgoing.Add(o);
      else Debug.Log("[NetSamples] sent " + Short(o.hash));
    }
  }

  int lastPlayers;
  float retryAt;

  // a cut-off transfer: keep what arrived, to resume later (deleted at the next start-up
  // if never resumed - SampleCache cleans its incoming folder)
  static void Keep(string hash, Incoming inc) {
    try { inc.stream?.Dispose(); } catch { }
    inc.stream = null;
    incoming.Remove(hash);
    if (inc.written > 0) partials[hash] = inc; else { try { File.Delete(inc.path); } catch { } }
  }

  static void ResetAll() { // (not "Reset": that name is a Unity editor message)
    foreach (string h in new List<string>(incoming.Keys)) Keep(h, incoming[h]);
    wantFrom.Clear();
    outgoing.Clear();
    waiting.Clear();
    askedAt.Clear();
    foreach (string h in new List<string>(states.Keys))
      if (states[h] == State.Loading || states[h] == State.TooBig) SetState(h, State.Missing); // TOO BIG only holds for that internet session
  }
}
