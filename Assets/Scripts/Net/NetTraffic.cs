using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Mirror;

// Network traffic, measured (2026-11-26). Every 10 s while in a session: what this
// headset sent and received (KB/s) and the biggest kinds of message, plus totals when the
// session ends. Logged as "[NetTraffic]".
//
// Bytes are Mirror's message sizes (Mirror's NetworkDiagnostics), without the transport's
// own headers (KCP / UTP, roughly 10-30% more). Over the internet everything goes through
// Unity's relay, so the relay's billed traffic (what it sends to players) is about the sum
// of every player's "in" figure; on local Wi-Fi nothing goes through the relay.
public class NetTraffic : MonoBehaviour {
  static NetTraffic instance;
  public static float interval = 10f;

  readonly Dictionary<string, long> outBy = new Dictionary<string, long>();
  long outBytes, inBytes, outMsgs, inMsgs, sessionOut, sessionIn;
  float windowAt, sessionAt = -1;
  int peakPlayers;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("NetTraffic");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetTraffic>();
  }

  void OnEnable() {
    NetworkDiagnostics.OutMessageEvent += OnOut;
    NetworkDiagnostics.InMessageEvent += OnIn;
  }

  void OnDisable() {
    NetworkDiagnostics.OutMessageEvent -= OnOut;
    NetworkDiagnostics.InMessageEvent -= OnIn;
  }

  // (Mirror calls these on the main thread)
  void OnOut(NetworkDiagnostics.MessageInfo m) {
    long b = (long)m.bytes * m.count;
    outBytes += b; outMsgs += m.count;
    string k = m.message != null ? m.message.GetType().Name : "?";
    outBy.TryGetValue(k, out long v); outBy[k] = v + b;
  }

  void OnIn(NetworkDiagnostics.MessageInfo m) {
    inBytes += m.bytes; inMsgs++;
  }

  static bool InSession { get { return NetworkServer.active || NetworkClient.isConnected; } }

  void Update() {
    bool live = InSession;
    if (live && sessionAt < 0) { sessionAt = Time.unscaledTime; windowAt = sessionAt; sessionOut = sessionIn = 0; peakPlayers = 0; Clear(); }
    if (!live) {
      if (sessionAt >= 0) {
        Window(true);
        float mins = (Time.unscaledTime - sessionAt) / 60f;
        Debug.Log("[NetTraffic] session over: " + mins.ToString("F1") + " min, up to " + peakPlayers + " players; sent " + MB(sessionOut) +
                  ", received " + MB(sessionIn) + " (" + MB(PerHour(sessionIn, mins)) + " an hour received)");
        sessionAt = -1;
      }
      Clear();
      return;
    }
    peakPlayers = Mathf.Max(peakPlayers, NetPlayer.all.Count);
    if (Time.unscaledTime - windowAt >= interval) Window(false);
  }

  void Window(bool final) {
    float dt = Mathf.Max(.001f, Time.unscaledTime - windowAt);
    sessionOut += outBytes; sessionIn += inBytes;
    if (outBytes + inBytes > 0 || !final) {
      var top = new List<KeyValuePair<string, long>>(outBy);
      top.Sort((a, b) => b.Value.CompareTo(a.Value));
      var sb = new StringBuilder();
      for (int i = 0; i < top.Count && i < 4; i++) {
        if (i > 0) sb.Append(", ");
        sb.Append(top[i].Key.Replace("Message", "").Replace("Msg", "")).Append(' ').Append(KBs(top[i].Value, dt));
      }
      string role = NetworkServer.active ? "host" : "client";
      string kind = NetSession.CurrentMode == NetSession.Mode.Internet ? "internet (relay)" : "local";
      Debug.Log("[NetTraffic] last " + dt.ToString("F0") + " s, " + role + ", " + kind + ", " + NetPlayer.all.Count + " players: sent " +
                KBs(outBytes, dt) + " (" + (outMsgs / dt).ToString("F0") + " msg/s), received " + KBs(inBytes, dt) +
                " (" + (inMsgs / dt).ToString("F0") + " msg/s); most sent: " + (sb.Length > 0 ? sb.ToString() : "-"));
    }
    Clear();
  }

  void Clear() {
    outBytes = inBytes = outMsgs = inMsgs = 0;
    outBy.Clear();
    windowAt = Time.unscaledTime;
  }

  static string KBs(long bytes, float dt) { return (bytes / 1024f / dt).ToString("F1") + " KB/s"; }
  static string MB(long bytes) { return (bytes / 1048576f).ToString("F1") + " MB"; }
  static long PerHour(long bytes, float mins) { return mins > .01f ? (long)(bytes * 60f / mins) : 0; }
}
