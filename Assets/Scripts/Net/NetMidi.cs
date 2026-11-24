// MIDI from a real MIDI device, for multiplayer (2026-11).
//
// Hardware MIDI is connected to one machine (normally the PC, which has the drivers and
// the cables). A MIDI-in on a device there (keyboard, xylophone roll, MIDI device...)
// hears it; the other headsets have the same device but no hardware behind it, so each
// note / control change it hears is sent on (device ID + channel + note / controller +
// value) and played into the same device everywhere. Only the machine that really hears
// the MIDI sends, so nobody has to decide who handles which input.
// The MIDI callbacks may not come on the main thread: messages are queued and sent from
// Update.

using System.Collections.Concurrent;
using Mirror;
using UnityEngine;

// a machine's MIDI inputs (sent by machines that have MIDI - the PC - every few seconds)
public struct NetMidiListMsg : NetworkMessage { public int token; public string owner; public string[] names, outNames; }
// pick another machine's MIDI device for a device's MIDI in or out (to that machine), or
// show what's connected (from it): kind 0 request, 1 connected, 2 failed. (For MIDI out,
// that machine's copy of the device sends: every copy plays the same notes.)
public struct NetMidiSelectMsg : NetworkMessage { public byte kind; public int netId, component, token; public string name, owner; }

public struct NetMidiMsg : NetworkMessage { public int netId; public byte kind; public byte channel, a, b; } // kind 0 note (a pitch, b on), 1 CC (a controller, b value)

public class NetMidi : MonoBehaviour {
  static NetMidi instance;
  static readonly ConcurrentQueue<(deviceInterface di, NetMidiMsg m)> pending = new ConcurrentQueue<(deviceInterface, NetMidiMsg)>();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetMidi");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetMidi>();
  }

  // midiComponentInterface: real MIDI came in here
  public static void Note(deviceInterface di, int channel, bool on, int pitch) {
    if (applying || di == null) return;
    pending.Enqueue((di, new NetMidiMsg { kind = 0, channel = (byte)channel, a = (byte)pitch, b = (byte)(on ? 1 : 0) }));
  }
  public static void CC(deviceInterface di, int channel, int controller, int value) {
    if (applying || di == null) return;
    pending.Enqueue((di, new NetMidiMsg { kind = 1, channel = (byte)channel, a = (byte)controller, b = (byte)value }));
  }

  void Update() {
    publishTimer += Time.unscaledDeltaTime;
    if (publishTimer > 5f) { publishTimer = 0; Publish(); }
    while (pending.TryDequeue(out var p)) {
      if (!NetSync.Active || p.di == null) continue;
      NetDevice d = p.di.GetComponent<NetDevice>();
      if (d == null || d.netId == 0) continue;
      NetMidiMsg m = p.m; m.netId = d.netId;
      NetSync.SendMidi(m);
      if (Time.unscaledTime - lastSendLog > 2f) { lastSendLog = Time.unscaledTime; Debug.Log("[NetMidi] sending MIDI from " + d.name + " (ID " + d.netId + ") to the other players"); }
      d.Rebaseline(); // what MIDI changed (the xylophone roll's held notes) is shared by this message, not the patch
    }
  }

  // ---------- other machines' MIDI inputs ----------

  public struct RemoteInput { public int token; public string owner, name; }

  // this computer's name: shown next to its MIDI devices on the other machines, and saved
  // with its connections in the patch ("device|COMPUTER"), so a computer that drops out
  // and comes back (in a new session) picks its own connections up again
  public static readonly string Me = SafeMachineName();
  static string SafeMachineName() {
    try { string n = System.Environment.MachineName; if (!string.IsNullOrEmpty(n)) return n.Replace("|", ""); } catch (System.Exception) { }
    return "PC";
  }
  public static string Tag(string device) => string.IsNullOrEmpty(device) || !DesktopMode.IsWindows ? device : device + "|" + Me;
  public static void Untag(string s, out string device, out string owner) {
    device = s; owner = null;
    if (string.IsNullOrEmpty(s)) return;
    int i = s.LastIndexOf('|');
    if (i <= 0 || i == s.Length - 1) return;
    device = s.Substring(0, i); owner = s.Substring(i + 1);
  }
  // is that computer in the session (it sent its device list in the last 20 s)?
  public static bool OwnerOnline(string owner) {
    if (owner == null || !NetSync.InSession) return false;
    foreach (var kv in lists) if (kv.Value.owner == owner && Time.unscaledTime - kv.Value.seen <= 20f) return true;
    return false;
  }

  static readonly int myToken = new System.Random().Next(1, int.MaxValue); // (UnityEngine.Random can't be used in a static initializer)
  static readonly System.Collections.Generic.Dictionary<int, (string owner, string[] names, string[] outNames, float seen)> lists =
    new System.Collections.Generic.Dictionary<int, (string, string[], string[], float)>();
  float publishTimer = 3;
  static float lastSendLog = -10, lastApplyLog = -10;

  // other machines' MIDI inputs (input) or outputs
  public static System.Collections.Generic.List<RemoteInput> RemoteDevices(bool input) {
    var r = new System.Collections.Generic.List<RemoteInput>();
    if (!NetSync.Active) return r;
    foreach (var kv in lists) {
      if (kv.Key == myToken || Time.unscaledTime - kv.Value.seen > 20f) continue;
      foreach (string n in input ? kv.Value.names : kv.Value.outNames) r.Add(new RemoteInput { token = kv.Key, owner = kv.Value.owner, name = n });
    }
    return r;
  }

  // is this another machine's input? (a patch loading here names a device it doesn't have)
  public static bool IsRemote(bool input, string name, out string owner) {
    owner = null;
    foreach (var kv in lists) {
      if (kv.Key == myToken) continue;
      if (System.Array.IndexOf(input ? kv.Value.names : kv.Value.outNames, name) >= 0) { owner = kv.Value.owner; return true; }
    }
    return false;
  }

  // machines with MIDI (Windows) tell the session what inputs they have
  void Publish() {
    if (!DesktopMode.IsWindows || !NetSync.Active || MIDImaster.instance == null) return;
    MIDImaster.instance.RefreshDevices(true);
    MIDImaster.instance.RefreshDevices(false);
    var names = new System.Collections.Generic.List<string>();
    var outNames = new System.Collections.Generic.List<string>();
    // (Windows' own test endpoints and the built-in synth aren't instruments)
    foreach (MIDIdevice d in MIDImaster.instance.inputDevices) {
      if (!d.name.StartsWith("Service Test")) names.Add(d.name);
    }
    foreach (MIDIdevice d in MIDImaster.instance.outputDevices) {
      if (!d.name.StartsWith("Service Test") && !d.name.StartsWith("Microsoft GS")) outNames.Add(d.name);
    }
    NetMidiListMsg m = new NetMidiListMsg { token = myToken, owner = Me, names = names.ToArray(), outNames = outNames.ToArray() };
    if (NetworkServer.active) { OnList(m); NetSync.SendMidiRelay(m, null); }
    else NetworkClient.Send(m);
  }

  public static void OnList(NetMidiListMsg m) {
    lists[m.token] = (m.owner, m.names ?? new string[0], m.outNames ?? new string[0], Time.unscaledTime);
  }

  static bool Locate(midiComponentInterface c, out NetDevice d, out int index) {
    d = c.GetComponentInParent<NetDevice>();
    index = -1;
    if (d == null || d.netId == 0) return false;
    index = System.Array.IndexOf(d.GetComponentsInChildren<midiComponentInterface>(true), c);
    return index >= 0;
  }

  static midiComponentInterface Find(int netId, int index) {
    if (!NetDevice.byId.TryGetValue(netId, out NetDevice d) || d == null) return null;
    midiComponentInterface[] all = d.GetComponentsInChildren<midiComponentInterface>(true);
    return index >= 0 && index < all.Length ? all[index] : null;
  }

  // a player here picked another machine's input
  public static void RequestRemote(midiComponentInterface c, RemoteInput r) {
    if (!Locate(c, out NetDevice d, out int i)) return;
    SendSelect(new NetMidiSelectMsg { kind = 0, netId = d.netId, component = i, token = r.token, name = r.name, owner = r.owner });
  }

  // this machine connected (or failed to connect) a MIDI-in: show it everywhere
  public static void LocalConnected(midiComponentInterface c, string name, bool ok) {
    if (!NetSync.Active || !Locate(c, out NetDevice d, out int i)) return;
    SendSelect(new NetMidiSelectMsg { kind = (byte)(ok ? 1 : 2), netId = d.netId, component = i, token = myToken, name = name, owner = Me });
  }

  static void SendSelect(NetMidiSelectMsg m) {
    if (NetworkServer.active) { OnSelect(m); NetSync.SendMidiRelay(m, null); }
    else NetworkClient.Send(m);
  }

  public static void OnSelect(NetMidiSelectMsg m) {
    midiComponentInterface c = Find(m.netId, m.component);
    if (c == null) return;
    if (m.kind == 0) {
      if (m.token != myToken) return; // for another machine
      Debug.Log("[NetMidi] another player connected " + m.name + (c.input ? " (in)" : " (out)") + " here to " + c.GetComponentInParent<NetDevice>().name);
      c.ConnectByName(m.name); // reports back through LocalConnected
      return;
    }
    if (m.token == myToken) return; // our own connection: shown already
    c.NetShowRemote(m.name + " (" + m.owner + ")" + (m.kind == 2 ? " - FAILED" : ""), m.kind == 1, m.name + "|" + m.owner);
  }

  public static bool applying; // (NetTimelines: MIDI from another player isn't recorded here)
  public static void Apply(NetMidiMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice d) || d == null) {
      if (Time.unscaledTime - lastApplyLog > 2f) { lastApplyLog = Time.unscaledTime; Debug.Log("[NetMidi] MIDI for unknown device ID " + m.netId); }
      return;
    }
    if (Time.unscaledTime - lastApplyLog > 2f) { lastApplyLog = Time.unscaledTime; Debug.Log("[NetMidi] playing another player's MIDI on " + d.name); }
    deviceInterface di = d.GetComponent<deviceInterface>();
    if (di == null) return;
    applying = true;
    d.Rebaseline();
    try {
      if (m.kind == 0) di.OnMidiNote(m.channel, m.b == 1, m.a);
      else di.OnMidiCC(m.channel, m.a, m.b);
    } finally { applying = false; }
  }
}
