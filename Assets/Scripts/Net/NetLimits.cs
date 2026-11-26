// Limits for internet sessions (2026-11): relay bandwidth is metered, so a session
// shouldn't keep running with nobody playing (a PC left on with SoundStage open), and
// like Zoom's free tier a session has a time limit (3 hours).
//
// - Idle: the host watches the whole room - its own input (PlayerActivity) and each
//   joined player's, reported every 20 s. After 9 minutes with no input anywhere,
//   everyone gets a 60 s warning (any input cancels it); at 10 minutes the session ends.
// - Time limit: the session's start time is sent to every player (and kept through host
//   migration). A 2-minute warning, then the session ends.
// - Ended: every headset leaves (state Ended, "ROOM n: ENDED ..."), the patch stays as it
//   is, and nobody reconnects until someone presses MULTIPLAYER (a fresh 3 hours).
// Local-network sessions have no limits.

using System;
using Mirror;
using UnityEngine;

// reason: 0 = session info / warning over, 1 = idle warning, 2 = hour warning,
//         3 = ended (idle), 4 = ended (time limit)
public struct NetLimitMsg : NetworkMessage { public int reason; public int secondsLeft; public long startedUtc; }
public struct NetActivityMsg : NetworkMessage { public byte unused; }

public static class NetLimits {
  public const float IdleWarnAt = 9 * 60f, IdleEndAt = 10 * 60f;
  public const double HourEndAt = 3 * 60 * 60, HourWarnAt = HourEndAt - 2 * 60; // 3 hours, warning 2 min before
  static string LimitText { get { return (HourEndAt / 3600).ToString("0") + " HOUR" + (HourEndAt == 3600 ? "" : "S"); } }

  public static long startedUtc;         // unix seconds; 0 = not known yet
  public static int endedReason;         // 3 or 4 once a session has been ended
  static float roomLastActive;           // host: realtime of the last input anywhere in the room
  static int warning;                    // host: warning sent (1 / 2), 0 = none
  static float reportTimer;

  static bool Applies { get { return NetSession.CurrentMode == NetSession.Mode.Internet; } }
  static long NowUtc { get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); } }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    PlayerActivity.BecameActive += () => {
      // back after a while: tell the host at once, so a warning on screen goes away
      if (Applies && NetworkClient.isConnected && !NetworkServer.active) NetworkClient.Send(new NetActivityMsg());
    };
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetActivityMsg>((conn, m) => {
      roomLastActive = Time.realtimeSinceStartup;
    });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetLimitMsg>(OnClient);
  }

  // A fresh session (not a host migration): its hour starts now.
  public static void SessionStarted() {
    startedUtc = NowUtc;
    endedReason = 0;
    warning = 0;
    roomLastActive = Time.realtimeSinceStartup;
    Debug.Log("[NetLimits] new internet session: idle limit " + IdleEndAt / 60 + " min, ends after " + HourEndAt / 60 + " min");
  }

  // Became host by migration: same session, same hour.
  public static void TookOver() {
    if (startedUtc == 0) startedUtc = NowUtc;
    warning = 0;
    roomLastActive = Time.realtimeSinceStartup;
  }

  // the host tells a player who just joined when the session started
  public static void ServerWelcome(NetworkConnectionToClient conn) {
    // not to the host's own client: in host mode it registers no client handlers, and
    // Mirror drops a connection that gets a message it can't handle
    if (!Applies || conn == null || conn == NetworkServer.localConnection) return;
    conn.Send(new NetLimitMsg { reason = 0, startedUtc = startedUtc });
  }

  // every frame, from NetSession
  public static void Tick(bool host, bool connected) {
    if (!Applies || !connected) return;
    if (!host) {
      // joined: report input to the host every 20 s
      reportTimer += Time.unscaledDeltaTime;
      if (reportTimer >= 20f) {
        reportTimer = 0;
        if (PlayerActivity.IdleSeconds < 20f) NetworkClient.Send(new NetActivityMsg());
      }
      return;
    }
    roomLastActive = Mathf.Max(roomLastActive, PlayerActivity.LastActive);
    float idle = Time.realtimeSinceStartup - roomLastActive;
    double age = startedUtc > 0 ? NowUtc - startedUtc : 0;

    if (age >= HourEndAt) { End(4); return; }
    if (idle >= IdleEndAt) { End(3); return; }

    int want = age >= HourWarnAt ? 2 : idle >= IdleWarnAt ? 1 : 0;
    if (want != warning) {
      warning = want;
      int left = want == 2 ? (int)(HourEndAt - age) : want == 1 ? (int)(IdleEndAt - idle) : 0;
      Debug.Log("[NetLimits] " + (want == 2 ? "time limit almost up" : want == 1 ? "nobody playing" : "warning over") + (left > 0 ? ": ending in " + left + " s" : ""));
      Broadcast(new NetLimitMsg { reason = want, secondsLeft = left, startedUtc = startedUtc });
    }
  }

  // to every joined player, and handled directly here for the host's own player (never
  // sent to the local connection - see ServerWelcome)
  static void Broadcast(NetLimitMsg m) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != NetworkServer.localConnection) c.Send(m);
    OnClient(m);
  }

  static void End(int reason) {
    Debug.Log("[NetLimits] ending the session: " + (reason == 4 ? LimitText + " are up" : "nobody has played for " + IdleEndAt / 60 + " minutes"));
    Broadcast(new NetLimitMsg { reason = reason, startedUtc = startedUtc });
    // give the message a moment to go out before the host leaves
    if (NetSession.instance != null) NetSession.instance.EndInternetSoon(reason);
  }

  static void OnClient(NetLimitMsg m) {
    if (m.startedUtc > 0) startedUtc = m.startedUtc;
    switch (m.reason) {
      case 0: NetNotice.Hide(); break;
      case 1: NetNotice.Show("NOBODY IS PLAYING", "SESSION ENDS IN {0} - TOUCH ANYTHING TO STAY", m.secondsLeft); break;
      case 2: NetNotice.Show("THE " + (HourEndAt / 3600).ToString("0") + "-HOUR LIMIT IS ALMOST UP", "SESSION ENDS IN {0} - PRESS MULTIPLAYER ON AFTERWARDS TO START A NEW ONE", m.secondsLeft); break;
      case 3:
      case 4:
        NetNotice.Show(m.reason == 4 ? "THE " + (HourEndAt / 3600).ToString("0") + "-HOUR LIMIT IS UP" : "SESSION ENDED: NOBODY WAS PLAYING", "PRESS MULTIPLAYER ON TO START A NEW SESSION", 0, 8f);
        if (NetSession.instance != null && !NetworkServer.active) NetSession.instance.EndInternet(m.reason);
        break;
    }
  }

  public static string EndedText() {
    return endedReason == 4 ? "ENDED (" + LimitText + ")" : endedReason == 3 ? "ENDED (IDLE)" : "ENDED";
  }
}

// A message floating in front of the player (headset or desktop camera), with a chime.
public class NetNotice : MonoBehaviour {
  static NetNotice instance;
  TextMesh title, body;
  string bodyFormat;
  float endsAt, hideAt;
  AudioSource chime;

  public static void Show(string head, string text, int seconds, float showFor = 0) {
    if (instance == null) {
      GameObject g = new GameObject("NetNotice");
      DontDestroyOnLoad(g);
      instance = g.AddComponent<NetNotice>();
      instance.Build();
    }
    instance.gameObject.SetActive(true);
    instance.title.text = head;
    instance.bodyFormat = text;
    instance.endsAt = Time.realtimeSinceStartup + seconds;
    instance.hideAt = showFor > 0 ? Time.realtimeSinceStartup + showFor : seconds > 0 ? instance.endsAt + 1f : Time.realtimeSinceStartup + 8f;
    instance.Refresh();
    if (instance.chime != null) instance.chime.Play();
  }

  public static void Hide() {
    if (instance != null) instance.gameObject.SetActive(false);
  }

  void Build() {
    TextMesh source = CommonText(); // borrow SoundStage's own font
    title = MakeText("title", .022f, new Vector3(0, .035f, 0), source);
    body = MakeText("body", .011f, new Vector3(0, -.02f, 0), source);
    title.color = new Color(1f, .75f, .3f);
    chime = gameObject.AddComponent<AudioSource>();
    chime.spatialBlend = 0;
    chime.playOnAwake = false;
    chime.volume = .5f;
    chime.clip = MakeChime();
  }

  // the font + material most of SoundStage's labels use (2026-11-26: it took the first
  // TextMesh found, which on a first start - the samples unpacking - was an odd one, and
  // the notices were unreadable for the rest of the session)
  static TextMesh CommonText() {
    var count = new System.Collections.Generic.Dictionary<string, int>();
    var pick = new System.Collections.Generic.Dictionary<string, TextMesh>();
    TextMesh best = null; int bestN = 0;
    foreach (TextMesh t in FindObjectsOfType<TextMesh>(true)) {
      MeshRenderer r = t.GetComponent<MeshRenderer>();
      if (t.font == null || t.font.material == null || r == null) continue;
      string key = t.font.name;
      int n; count.TryGetValue(key, out n); count[key] = ++n;
      if (!pick.ContainsKey(key)) pick[key] = t;
      if (n > bestN) { bestN = n; best = pick[key]; }
    }
    if (best != null) Debug.Log("[NetNotice] font " + best.font.name + " (" + bestN + " labels use it), material " + best.font.material.name + " (the font's own)");
    return best;
  }

  TextMesh MakeText(string name, float size, Vector3 pos, TextMesh source) {
    GameObject g = new GameObject(name);
    g.transform.SetParent(transform, false);
    g.transform.localPosition = pos;
    TextMesh t = g.AddComponent<TextMesh>();
    if (source != null) {
      t.font = source.font;
      t.fontSize = source.fontSize;
      t.characterSize = source.characterSize;
      // the font's own material (its glyph atlas): a label's material can be one made for
      // something else - "InstrumentSelectMat (Quest boost)" - and drew blocks (2026-11-26)
      g.GetComponent<MeshRenderer>().sharedMaterial = source.font.material;
    }
    t.anchor = TextAnchor.MiddleCenter;
    t.alignment = TextAlignment.Center;
    // scale so a capital letter is about 'size' metres tall, whatever the font's units
    t.text = "X";
    float h = g.GetComponent<MeshRenderer>().bounds.size.y;
    g.transform.localScale = Vector3.one * (h > 1e-5f ? size / h : 1f);
    return t;
  }

  static AudioClip MakeChime() {
    const int rate = 44100;
    int n = (int)(rate * .5f);
    float[] d = new float[n];
    for (int i = 0; i < n; i++) {
      float t = i / (float)rate;
      float f = t < .25f ? 880f : 660f;
      float env = Mathf.Exp(-((t % .25f) * 10f));
      d[i] = Mathf.Sin(2 * Mathf.PI * f * t) * env * .6f;
    }
    AudioClip c = AudioClip.Create("NetNoticeChime", n, 1, rate, false);
    c.SetData(d, 0);
    return c;
  }

  void Refresh() {
    int left = Mathf.Max(0, Mathf.CeilToInt(endsAt - Time.realtimeSinceStartup));
    string l = left >= 60 ? (left / 60) + ":" + (left % 60).ToString("00") : left + " S";
    body.text = bodyFormat.Contains("{0}") ? string.Format(bodyFormat, l) : bodyFormat;
  }

  void LateUpdate() {
    if (Time.realtimeSinceStartup > hideAt) { gameObject.SetActive(false); return; }
    Refresh();
    Camera cam = Camera.main;
    if (cam == null) return;
    Transform c = cam.transform;
    Vector3 fwd = Vector3.ProjectOnPlane(c.forward, Vector3.up);
    if (fwd.sqrMagnitude < .01f) fwd = c.forward;
    fwd.Normalize();
    transform.position = c.position + fwd * .8f + Vector3.down * .05f;
    transform.rotation = Quaternion.LookRotation(transform.position - c.position, Vector3.up);
  }
}
