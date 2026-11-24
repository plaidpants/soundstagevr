// Notes in the piano rolls and timelines, shared one change at a time (2026-11-24).
//
// The keyboard's and the xylophone's piano rolls and the Timeline device hold notes that no
// control message carries. Until now a change to them (recording, drawing, dragging,
// deleting a note) was noticed once a second by NetDevice and the WHOLE patch was sent
// again: every headset rebuilt every device, at most every 3 s, all the while someone
// played live into a roll - slow, and it dropped what other players were holding.
//
// Now each headset looks at its rolls five times a second and sends only what changed:
// the notes that appeared and the notes that went (a moved or resized note is one of
// each). Each note is its track and start / end. A note still being recorded, or one being
// dragged, waits until it's finished. Notes from two players at once both land.
//
// Only the player who plays a note records it: a key pressed by another player (a key
// message) or their MIDI plays on every headset but is recorded only on theirs
// (timelineComponentInterface.onTimelineEvent checks RemoteInput) - recording it
// everywhere made copies that never quite matched. Through the host like everything else.

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetTimelineNotesMsg : NetworkMessage {
  public int netId;
  public int[] addTrack; public float[] addIn, addOut;
  public int[] remTrack; public float[] remIn, remOut;
}

public class NetTimelines : MonoBehaviour {
  // a change from another player is being applied: don't record it, don't send it back
  public static bool RemoteInput { get { return NetControls.applying || NetMidi.applying || applying; } }
  static bool applying;

  const float scanInterval = .2f;

  struct NoteKey : System.IEquatable<NoteKey> {
    public int track, a, b; // start / end in 1/65536 grid units
    public NoteKey(int t, Vector2 io) { track = t; a = Mathf.RoundToInt(io.x * 65536f); b = Mathf.RoundToInt(io.y * 65536f); }
    public bool Equals(NoteKey o) { return track == o.track && a == o.a && b == o.b; }
    public override bool Equals(object o) { return o is NoteKey k && Equals(k); }
    public override int GetHashCode() { unchecked { return (track * 397 ^ a) * 397 ^ b; } }
  }

  // what this headset last sent or received for each roll
  static readonly Dictionary<timelineComponentInterface, HashSet<NoteKey>> known = new Dictionary<timelineComponentInterface, HashSet<NoteKey>>();
  static readonly Dictionary<NetDevice, timelineComponentInterface> rollOf = new Dictionary<NetDevice, timelineComponentInterface>();
  static float scanTimer;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetTimelines");
    DontDestroyOnLoad(g);
    g.AddComponent<NetTimelines>();
  }

  static readonly HashSet<NetDevice> noRoll = new HashSet<NetDevice>(); // devices without one

  static timelineComponentInterface RollOf(NetDevice d) {
    if (noRoll.Contains(d)) return null;
    if (rollOf.TryGetValue(d, out timelineComponentInterface t) && t != null) return t;
    t = d.GetComponentInChildren<timelineComponentInterface>(true); // (again if a reload replaced it)
    if (t == null) noRoll.Add(d); else rollOf[d] = t;
    return t;
  }

  void Update() { long _pp = PerfProbe.Start(); try { UpdateProbed(); } finally { PerfProbe.Add("NetTimelines.Update", _pp); } } // (PerfProbe: temporary)
  void UpdateProbed() {
    if (!NetSync.Active) {
      if (known.Count > 0 || rollOf.Count > 0 || noRoll.Count > 0) { known.Clear(); rollOf.Clear(); noRoll.Clear(); }
      return;
    }
    scanTimer += Time.unscaledDeltaTime;
    if (scanTimer < scanInterval) return;
    scanTimer = 0;

    foreach (NetDevice d in NetDevice.all) {
      if (d == null || d.netId == 0) continue;
      timelineComponentInterface roll = RollOf(d);
      if (roll != null) Scan(d, roll);
    }
    Prune();
  }

  static void Prune() {
    List<NetDevice> gone = null;
    foreach (var kv in rollOf) if (kv.Key == null) (gone ??= new List<NetDevice>()).Add(kv.Key);
    if (gone != null) foreach (NetDevice d in gone) rollOf.Remove(d);
    noRoll.RemoveWhere(d => d == null);
    List<timelineComponentInterface> goneRolls = null;
    foreach (var kv in known) if (kv.Key == null) (goneRolls ??= new List<timelineComponentInterface>()).Add(kv.Key);
    if (goneRolls != null) foreach (var r in goneRolls) known.Remove(r);
  }

  static bool BeingEdited(timelineEvent e) {
    return e.grabbed ||
           (e.body != null && e.body.curState == manipObject.manipState.grabbed) ||
           (e.edgeInHandle != null && e.edgeInHandle.curState == manipObject.manipState.grabbed) ||
           (e.edgeOutHandle != null && e.edgeOutHandle.curState == manipObject.manipState.grabbed);
  }

  static readonly HashSet<NoteKey> now = new HashSet<NoteKey>();

  static void Scan(NetDevice d, timelineComponentInterface roll) {
    now.Clear();
    List<timelineEvent> evs = roll._tlEvents;
    for (int i = 0; i < evs.Count; i++) {
      timelineEvent e = evs[i];
      if (e == null || e.pleaseDestroy || e.recording) continue; // still being recorded: later
      if (BeingEdited(e)) return;                                  // being dragged: once it's let go
      now.Add(new NoteKey(e.track, e.in_out));
    }

    if (!known.TryGetValue(roll, out HashSet<NoteKey> before)) {
      known[roll] = new HashSet<NoteKey>(now); // first look (after a patch load): that's the baseline
      return;
    }
    if (before.SetEquals(now)) return;

    var add = new List<NoteKey>(); var rem = new List<NoteKey>();
    foreach (NoteKey k in now) if (!before.Contains(k)) add.Add(k);
    foreach (NoteKey k in before) if (!now.Contains(k)) rem.Add(k);
    before.Clear(); before.UnionWith(now);

    var m = new NetTimelineNotesMsg { netId = d.netId };
    Pack(add, out m.addTrack, out m.addIn, out m.addOut);
    Pack(rem, out m.remTrack, out m.remIn, out m.remOut);
    Debug.Log("[NetTimelines] " + d.name + ": " + add.Count + " notes added, " + rem.Count + " removed here (" + now.Count + " in all): sending");
    if (NetworkServer.active) Relay(m, null);
    else NetworkClient.Send(m);
  }

  static void Pack(List<NoteKey> ks, out int[] t, out float[] a, out float[] b) {
    t = new int[ks.Count]; a = new float[ks.Count]; b = new float[ks.Count];
    for (int i = 0; i < ks.Count; i++) { t[i] = ks[i].track; a[i] = ks[i].a / 65536f; b[i] = ks[i].b / 65536f; }
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetTimelineNotesMsg>((conn, m) => { Apply(m); Relay(m, conn); });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetTimelineNotesMsg>(Apply);
  }

  static void Relay(NetTimelineNotesMsg m, NetworkConnectionToClient from) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m);
  }

  static void Apply(NetTimelineNotesMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice d) || d == null) return;
    timelineComponentInterface roll = RollOf(d);
    if (roll == null) return;
    int nAdd = m.addTrack != null ? m.addTrack.Length : 0, nRem = m.remTrack != null ? m.remTrack.Length : 0;
    if (!known.TryGetValue(roll, out HashSet<NoteKey> before)) {
      before = new HashSet<NoteKey>();
      foreach (timelineEvent e in roll._tlEvents) if (e != null && !e.pleaseDestroy && !e.recording) before.Add(new NoteKey(e.track, e.in_out));
      known[roll] = before;
    }

    applying = true;
    try {
      lock (roll.eventsLock) RemoveNotes(roll, m, nRem, before); // (the audio thread walks the list)
      // added notes (unless one is already there)
      for (int i = 0; i < nAdd; i++) {
        Vector2 io = new Vector2(m.addIn[i], m.addOut[i]);
        NoteKey k = new NoteKey(m.addTrack[i], io);
        bool have = false;
        foreach (timelineEvent e in roll._tlEvents)
          if (e != null && !e.pleaseDestroy && k.Equals(new NoteKey(e.track, e.in_out))) { have = true; break; }
        if (!have) roll.SpawnTimelineEvent(m.addTrack[i], io);
        before.Add(k);
      }
    } finally { applying = false; }
    Debug.Log("[NetTimelines] another player changed " + d.name + ": " + nAdd + " notes added, " + nRem + " removed");
  }

  static void RemoveNotes(timelineComponentInterface roll, NetTimelineNotesMsg m, int nRem, HashSet<NoteKey> before) {
    {
      // removed notes
      for (int i = 0; i < nRem; i++) {
        NoteKey k = new NoteKey(m.remTrack[i], new Vector2(m.remIn[i], m.remOut[i]));
        before.Remove(k);
        for (int j = roll._tlEvents.Count - 1; j >= 0; j--) {
          timelineEvent e = roll._tlEvents[j];
          if (e == null || e.recording || !k.Equals(new NoteKey(e.track, e.in_out))) continue;
          if (e.playing) {
            e.playing = false;
            if (roll._targetDeviceInterface != null) roll._targetDeviceInterface.onTimelineEvent(e.track, false);
          }
          roll._tlEvents.RemoveAt(j);
          Object.Destroy(e.gameObject);
          break;
        }
      }
    }
  }
}
