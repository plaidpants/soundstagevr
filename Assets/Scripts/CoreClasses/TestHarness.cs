// Test harness (2026-11): the app plays with itself, to shake out bugs and crashes,
// alone or in a multiplayer session where other machines may be running it too.
//
// Switched on from the settings panel (TEST HARNESS), or on the PC with the command
// line argument -harness (optionally -harnessSeed N). Every so often it does one random
// thing, going through the same code a player's hands would, so the multiplayer code
// sees ordinary player actions:
//   spawn a device from the menu list, delete one (as the trash does), move one (as if
//   held), turn dials / move sliders / flip switches, press buttons, play keys and drum
//   pads, plug and pull cables (never making a loop), put tapes into decks and take
//   them out, pick MIDI devices on MIDI in / out panels.
// It stops for good (until switched on again) the moment it sees real input: a
// controller button, trigger, grip or stick, a pinch, the headset being put on, or the
// keyboard or mouse - after a few seconds' grace for the click that started it.
//
// Logging: every action, a status line every 10 s, a per-device state snapshot every
// 30 s (compare two machines' logs to find a desync), and every error or exception
// with its stack trace, go to harness_<date>.log in Application.persistentDataPath
// (written line by line, so it survives a crash), and the actions also to Player.log.
//   Quest: /sdcard/Android/data/<package>/files/   PC: %USERPROFILE%\AppData\LocalLow\<company>\<product>\
// Machines that sit unattended: the Quest pauses when the headset isn't worn - cover
// the proximity sensor, or turn it off from the developer tools.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TestHarness : MonoBehaviour {
  public static TestHarness instance;
  public static bool Running => instance != null && instance.running;
  public static event System.Action Changed;

  // device types it won't spawn or touch: the microphone asks for permission, the camera
  // renders another view, the recorder writes files
  static readonly HashSet<menuItem.deviceType> excluded = new HashSet<menuItem.deviceType> {
    menuItem.deviceType.Microphone, menuItem.deviceType.Camera,
    menuItem.deviceType.Recorder, menuItem.deviceType.Max,
    menuItem.deviceType.TapeGroup, menuItem.deviceType.Pano
  };

  const int MaxDevices = 14;     // in the whole scene: above this it deletes instead of spawning
  const float Grace = 5f;        // input ignored this long after starting

  bool running;
  float startedAt;
  int seed;
  System.Random rng;
  int actions, errors;
  StreamWriter log;
  readonly object logLock = new object();
  string logPath;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("TestHarness");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<TestHarness>();
    string[] args = System.Environment.GetCommandLineArgs();
    int s = 0;
    bool auto = false;
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-harness") auto = true;
      if (args[i] == "-harnessSeed" && i + 1 < args.Length) int.TryParse(args[i + 1], out s);
    }
    if (auto) instance.StartCoroutine(instance.AutoStart(s));
  }

  IEnumerator AutoStart(int s) {
    yield return new WaitForSeconds(8f); // let the menu and the session come up
    StartHarness(s);
  }

  public static void Toggle() {
    if (instance == null) return;
    if (instance.running) instance.Stop("switched off");
    // the press on the menu button is itself real input: it has stopped the harness
    // already, so it mustn't start it again
    else if (Time.unscaledTime - instance.stoppedAt > 1f) instance.StartHarness(0);
  }
  float stoppedAt = -10;

  // ---------- start / stop ----------

  void StartHarness(int s) {
    if (running) return;
    seed = s != 0 ? s : System.Environment.TickCount & 0x7fffffff;
    rng = new System.Random(seed);
    actions = errors = 0;
    errorCounts.Clear();
    OpenLog();
    running = true;
    startedAt = Time.unscaledTime;
    lastMouse = Input.mousePosition;
    mouseTravel = 0;
    wasPresent = UserPresent();
    Application.logMessageReceivedThreaded += OnLog;
    Write("started: seed " + seed + ", " + SystemInfo.deviceModel + " / " + SystemInfo.deviceName + ", SoundStage " + Application.version +
          ", " + NetSession.StatusText() + "; log " + logPath);
    StartCoroutine(Loop());
    StartCoroutine(StatusLoop());
    Changed?.Invoke();
  }

  void Stop(string why) {
    if (!running) return;
    running = false;
    stoppedAt = Time.unscaledTime;
    StopAllCoroutines();
    ReleaseAll();
    Write("stopped (" + why + "): " + actions + " actions, " + errors + " errors in " + (Time.unscaledTime - startedAt).ToString("0") + " s");
    Application.logMessageReceivedThreaded -= OnLog;
    lock (logLock) { if (log != null) { log.Dispose(); log = null; } }
    Changed?.Invoke();
  }

  void OnApplicationQuit() { Stop("quitting"); }

  // ---------- real input stops it ----------

  Vector3 lastMouse;
  float mouseTravel;
  bool wasPresent;

  void Update() {
    if (!running) return;
    Vector3 m = Input.mousePosition;
    float moved = (m - lastMouse).magnitude;
    lastMouse = m;
    if (Time.unscaledTime - startedAt < Grace) { mouseTravel = 0; wasPresent = UserPresent(); return; }
    mouseTravel = Mathf.Max(0, mouseTravel + moved - 2f); // ignore jitter; a real move adds up quickly
    string why = null;
    if (Input.anyKeyDown) why = "key / button";
    else if (Input.mouseScrollDelta.sqrMagnitude > 0) why = "mouse wheel";
    else if (mouseTravel > 60f) why = "mouse moved";
    else why = VRInput();
    if (why != null) Stop("real input: " + why);
  }

  string VRInput() {
    if (!OVRSafe.Available) return null;
    try {
      if (OVRInput.Get(OVRInput.Button.Any, OVRInput.Controller.Touch)) return "controller button";
      foreach (OVRInput.Controller c in new[] { OVRInput.Controller.LTouch, OVRInput.Controller.RTouch }) {
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c) > .35f) return "trigger";
        if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, c) > .35f) return "grip";
        if (OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c).magnitude > .5f) return "thumbstick";
      }
      foreach (OVRHand h in FindObjectsOfType<OVRHand>())
        if (h.IsTracked && h.GetFingerIsPinching(OVRHand.HandFinger.Index)) return "pinch";
      bool present = UserPresent();
      if (present && !wasPresent) return "headset put on";
      wasPresent = present;
    } catch (System.Exception) { }
    return null;
  }

  static bool UserPresent() {
    if (!OVRSafe.Available) return false;
    try { return OVRPlugin.userPresent; } catch (System.Exception) { return false; }
  }

  // ---------- the action loop ----------

  IEnumerator Loop() {
    yield return new WaitForSeconds(1f);
    while (running) {
      yield return new WaitForSeconds(Range(.25f, 1.2f));
      if (NetSync.InSession && !NetSync.Active) continue; // a patch is loading
      IEnumerator act = null;
      try { act = Pick(); } catch (System.Exception e) { Fail("choosing", e); }
      if (act == null) continue;
      actions++;
      yield return Guard(act);
    }
  }

  // runs an action, logging (not throwing) what goes wrong in it
  IEnumerator Guard(IEnumerator act) {
    while (true) {
      object cur;
      try {
        if (!act.MoveNext()) yield break;
        cur = act.Current;
      } catch (System.Exception e) { Fail("action " + actions, e); yield break; }
      yield return cur;
    }
  }

  IEnumerator Pick() {
    List<NetDevice> devs = Devices();
    if (devs.Count == 0 || (devs.Count < 4 && Chance(.5f))) return Spawn();
    double r = rng.NextDouble();
    if (r < .08) return devs.Count >= MaxDevices ? Delete(devs) : Spawn();
    if (r < .13) return Delete(devs);
    if (r < .20) return Move(devs);
    if (r < .32) return Connect(devs);
    if (r < .38) return Disconnect();
    if (r < .44) return Tape(devs);
    if (r < .48) return Midi(devs);
    if (r < .70) return Adjust(devs);
    return Play(devs);
  }

  // devices it may act on
  List<NetDevice> Devices() {
    var list = new List<NetDevice>();
    foreach (NetDevice d in NetDevice.all) {
      if (d == null || !d.isActiveAndEnabled || !d.IsDevice || d.RemotelyHeld) continue;
      if (NetSync.InSession && d.netId == 0) continue; // not known to the session yet
      deviceInterface di = d.GetComponent<deviceInterface>();
      if (di == null || excluded.Contains(TypeOf(di))) continue;
      list.Add(d);
    }
    return list;
  }

  static menuItem.deviceType TypeOf(deviceInterface di) {
    try { return di.GetData().deviceType; } catch (System.Exception) { return menuItem.deviceType.Max; }
  }

  // ---------- spawn / delete / move ----------

  IEnumerator Spawn() {
    var types = new List<menuItem.deviceType>();
    for (int i = 0; i < (int)menuItem.deviceType.Max; i++)
      if (!excluded.Contains((menuItem.deviceType)i)) types.Add((menuItem.deviceType)i);
    menuItem.deviceType t = types[rng.Next(types.Count)];
    GameObject prefab = Resources.Load<GameObject>("Prefabs/" + t);
    if (prefab == null) { Write("spawn " + t + ": no prefab"); yield break; }
    Transform head = Camera.main != null ? Camera.main.transform : null;
    Vector3 basePos = head != null ? head.position : new Vector3(0, 1.2f, 0);
    Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
    if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
    fwd.Normalize();
    Vector3 right = Vector3.Cross(Vector3.up, fwd);
    Vector3 pos = basePos + fwd * Range(.5f, 1.1f) + right * Range(-.7f, .7f);
    pos.y = Mathf.Clamp(basePos.y - Range(.15f, .5f), .6f, 1.6f);
    Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
    GameObject g = Instantiate(prefab, pos, rot);
    // (as the menu does it)
    if (t == menuItem.deviceType.Tapes) g.transform.Translate(.1f, .02f, -.185f, Space.Self);
    else if (t != menuItem.deviceType.Filter && t != menuItem.deviceType.Airhorn && t != menuItem.deviceType.ADSR) g.transform.Rotate(0, 180, 0, Space.Self);
    Write("spawn " + t + " at " + pos.ToString("F2"));
    yield return null;
  }

  IEnumerator Delete(List<NetDevice> devs) {
    NetDevice d = devs[rng.Next(devs.Count)];
    Write("delete " + Name(d));
    Destroy(d.gameObject);
    yield return null;
  }

  // as if a hand held it: the network sends its pose while a handle is grabbed
  IEnumerator Move(List<NetDevice> devs) {
    NetDevice d = devs[rng.Next(devs.Count)];
    handle h = d.GetComponentInChildren<handle>();
    if (h == null) yield break;
    Vector3 from = d.transform.position;
    Vector3 to = from + new Vector3(Range(-.3f, .3f), Range(-.1f, .1f), Range(-.3f, .3f));
    to.y = Mathf.Clamp(to.y, .5f, 1.8f);
    Quaternion r0 = d.transform.rotation, r1 = r0 * Quaternion.Euler(0, Range(-40f, 40f), 0);
    Write("move " + Name(d) + " " + from.ToString("F2") + " -> " + to.ToString("F2"));
    held.Add(h);
    h.curState = manipObject.manipState.grabbed;
    float dur = Range(.4f, 1.5f);
    for (float t = 0; t < dur; t += Time.deltaTime) {
      if (d == null || h == null) break;
      float k = Mathf.SmoothStep(0, 1, t / dur);
      d.transform.position = Vector3.Lerp(from, to, k);
      d.transform.rotation = Quaternion.Slerp(r0, r1, k);
      yield return null;
    }
    if (h != null) h.curState = manipObject.manipState.none;
    held.Remove(h);
  }

  // ---------- controls ----------

  static bool Usable(Component c) {
    if (c == null || !c.gameObject.activeInHierarchy) return false;
    if (c.GetComponentInParent<midiComponentInterface>() != null) return false; // MIDI device lists
    return true;
  }

  List<T> Controls<T>(NetDevice d) where T : Component {
    var list = new List<T>();
    foreach (T c in d.GetComponentsInChildren<T>(false)) if (Usable(c)) list.Add(c);
    return list;
  }

  IEnumerator Adjust(List<NetDevice> devs) {
    NetDevice d = devs[rng.Next(devs.Count)];
    var all = new List<manipObject>();
    all.AddRange(Controls<dial>(d)); all.AddRange(Controls<slider>(d)); all.AddRange(Controls<sliderUneven>(d));
    all.AddRange(Controls<sliderNotched>(d)); all.AddRange(Controls<slider2D>(d)); all.AddRange(Controls<basicSwitch>(d));
    if (all.Count == 0) yield break;
    manipObject o = all[rng.Next(all.Count)];
    string what = Name(d) + " " + o.GetType().Name + " '" + o.name + "'";

    if (o is basicSwitch sw) {
      sw.setSwitch(!sw.switchVal);
      NetControls.HarnessValue(sw, true);
      Write("switch " + what + " -> " + sw.switchVal);
      yield break;
    }
    if (o is sliderNotched sn) {
      int v = rng.Next(Mathf.Max(1, sn.notchCount));
      sn.setVal(v);
      NetControls.HarnessValue(sn, true);
      Write("notch " + what + " -> " + v);
      yield break;
    }
    // sweep to a new value over a moment, sending as a held control does
    float a0 = 0, a1 = (float)rng.NextDouble();
    Vector2 v0 = Vector2.zero, v1 = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
    if (o is dial di) a0 = di.percent;
    else if (o is slider sl) a0 = sl.percent;
    else if (o is sliderUneven su) a0 = su.percent;
    else if (o is slider2D s2) v0 = s2.percent;
    Write("adjust " + what + " " + (o is slider2D ? v0.ToString("F2") + " -> " + v1.ToString("F2") : a0.ToString("F2") + " -> " + a1.ToString("F2")));
    float dur = Range(.2f, 1.2f), sendT = 0;
    for (float t = 0; t <= dur; t += Time.deltaTime) {
      if (o == null) yield break;
      float k = Mathf.Clamp01(t / dur);
      SetValue(o, Mathf.Lerp(a0, a1, k), Vector2.Lerp(v0, v1, k));
      sendT += Time.deltaTime;
      if (sendT > .05f) { sendT = 0; NetControls.HarnessValue(o, false); }
      yield return null;
    }
    if (o == null) yield break;
    SetValue(o, a1, v1);
    NetControls.HarnessValue(o, true);
  }

  static void SetValue(manipObject o, float a, Vector2 v) {
    if (o is dial di) di.setPercent(a);
    else if (o is slider sl) sl.setPercent(a);
    else if (o is sliderUneven su) su.setPercent(a);
    else if (o is slider2D s2) s2.setPercent(v);
  }

  IEnumerator Play(List<NetDevice> devs) {
    NetDevice d = devs[rng.Next(devs.Count)];
    List<key> keys = Controls<key>(d);
    List<button> buttons = Controls<button>(d);
    buttons.RemoveAll(b => b.name == "SAVE"); // writes a recording file every time
    List<drumpad> pads = Controls<drumpad>(d);
    int total = keys.Count + buttons.Count + pads.Count;
    if (total == 0) yield break;
    int pick = rng.Next(total);

    if (pick < keys.Count) {
      // a few notes, sometimes a chord
      int n = 1 + rng.Next(Chance(.3f) ? 3 : 1);
      var playing = new List<key>();
      for (int i = 0; i < n; i++) {
        key k = keys[rng.Next(keys.Count)];
        if (playing.Contains(k) || TouchTracker.IsTouched(k)) continue;
        playing.Add(k);
        held.Add(k);
        TouchTracker.Touch(k, true, null);
      }
      Write("play " + Name(d) + " key" + (playing.Count > 1 ? "s " : " ") + string.Join(",", playing.ConvertAll(x => x.keyValue.ToString())));
      yield return new WaitForSeconds(Range(.08f, .7f));
      foreach (key k in playing) { if (k != null) TouchTracker.Touch(k, false, null); held.Remove(k); }
    } else if (pick < keys.Count + buttons.Count) {
      button b = buttons[pick - keys.Count];
      Write("press " + Name(d) + " button '" + b.name + "' (ID " + b.buttonID + (b.isToggle ? ", toggle" : "") + ")");
      held.Add(b);
      b.setState(manipObject.manipState.grabbed);
      yield return new WaitForSeconds(Range(.05f, .4f));
      if (b != null) b.setState(manipObject.manipState.none);
      held.Remove(b);
    } else {
      drumpad p = pads[pick - keys.Count - buttons.Count];
      int hits = 1 + rng.Next(4);
      Write("drum " + Name(d) + " '" + p.name + "' x" + hits);
      for (int i = 0; i < hits && p != null; i++) {
        p.keyHit(true, Range(.2f, 1f));
        yield return new WaitForSeconds(Range(.08f, .3f));
      }
    }
  }

  // ---------- tapes ----------

  // Put a tape into a deck (drum pad, sampler, looper, xylophone, scale sequencer ...) or
  // take one out, as dropping one in from the tape library does. Only the app's own
  // samples: a custom sample or recording isn't on the other headsets.
  IEnumerator Tape(List<NetDevice> devs) {
    var decks = new List<samplerLoad>();
    foreach (NetDevice d in devs) foreach (samplerLoad s in Controls<samplerLoad>(d)) decks.Add(s);
    if (decks.Count == 0) yield break;
    samplerLoad deck = decks[rng.Next(decks.Count)];
    NetDevice dev = deck.GetComponentInParent<NetDevice>();
    if (deck.hasTape() && Chance(.35f)) {
      deck.getTapeInfo(out string old, out _);
      Write("eject tape '" + old + "' from " + Name(dev) + " '" + deck.name + "'");
      deck.ForceEject();
      NetTapes.Ejected(deck);
      yield break;
    }
    if (sampleManager.instance == null || sampleManager.instance.sampleDictionary == null) yield break;
    var samples = new List<KeyValuePair<string, string>>();
    foreach (var cat in sampleManager.instance.sampleDictionary.Values)
      foreach (var kv in cat) if (kv.Value.StartsWith("APP")) samples.Add(kv);
    if (samples.Count == 0) { Write("tape: no samples found"); yield break; }
    var pick = samples[rng.Next(samples.Count)];
    Write("tape '" + pick.Key + "' (" + pick.Value + ") into " + Name(dev) + " '" + deck.name + "'" + (deck.hasTape() ? " (replacing one)" : ""));
    deck.ForceEject();
    deck.SetSample(pick.Key, pick.Value);
    if (deck.hasTape()) NetTapes.Loaded(deck, pick.Key, pick.Value);
    else Write("tape didn't load (file missing?)");
    yield return null;
  }

  // ---------- MIDI ----------

  // Pick a MIDI device on a MIDI in / out panel (keyboard, xylophone roll, MIDI IN / OUT
  // devices - only panels that are switched on), as a player does: open the list, touch
  // an entry. This machine's own devices (the PC: USB MIDI, the Windows wavetable synth)
  // and other players' (NetMidi: a Quest picking the PC's) are both in the list.
  IEnumerator Midi(List<NetDevice> devs) {
    var panels = new List<midiComponentInterface>();
    foreach (NetDevice d in devs)
      foreach (midiComponentInterface c in d.GetComponentsInChildren<midiComponentInterface>(false)) panels.Add(c);
    if (panels.Count == 0 || MIDImaster.instance == null) yield break;
    midiComponentInterface p = panels[rng.Next(panels.Count)];
    NetDevice dev = p.GetComponentInParent<NetDevice>();
    MIDImaster.instance.RefreshDevices(p.input);
    List<MIDIdevice> local = p.input ? MIDImaster.instance.inputDevices : MIDImaster.instance.outputDevices;
    List<NetMidi.RemoteInput> remote = NetMidi.RemoteDevices(p.input);
    var choices = new List<int>(); // buttonIDs: local index, or 1000 + remote index (as in the list)
    var names = new List<string>();
    if (remote.Count == 0 || local.Count > 0)
      for (int i = 0; i < local.Count; i++) if (!local[i].name.StartsWith("Service Test")) { choices.Add(i); names.Add(local[i].name); }
    for (int k = 0; k < remote.Count; k++) { choices.Add(1000 + k); names.Add(remote[k].name + " (" + remote[k].owner + ")"); }
    if (choices.Count == 0) yield break;
    int c0 = rng.Next(choices.Count);
    Write("midi " + (p.input ? "in" : "out") + " on " + Name(dev) + ": " + names[c0] + (p.connectedDevice != "" ? " (was " + p.connectedDevice + ")" : ""));
    if (!p.ListOpen) p.hit(true, -1); // open the list
    yield return new WaitForSeconds(Range(.2f, .8f));
    if (p == null) yield break;
    if (!p.ListOpen) p.hit(true, -1);
    p.hit(true, choices[c0]);
  }

  // ---------- cables ----------

  IEnumerator Connect(List<NetDevice> devs) {
    var outs = new List<omniJack>();
    var ins = new List<omniJack>();
    foreach (NetDevice d in devs)
      foreach (omniJack j in Controls<omniJack>(d)) {
        if (j.near != null || j.plugged) continue;
        (j.outgoing ? outs : ins).Add(j);
      }
    if (outs.Count == 0 || ins.Count == 0) yield break;
    Dictionary<NetDevice, HashSet<NetDevice>> graph = CableGraph();
    for (int tries = 0; tries < 12; tries++) {
      omniJack o = outs[rng.Next(outs.Count)], i = ins[rng.Next(ins.Count)];
      NetDevice a = o.GetComponentInParent<NetDevice>(), b = i.GetComponentInParent<NetDevice>();
      if (a == null || b == null || a == b || Reaches(graph, b, a)) continue; // no loops
      Color c = Color.HSVToRGB(omniJack.QuantizeHue((float)rng.NextDouble()), .8f, .5f);
      Write("connect " + Name(a) + " '" + o.name + "' -> " + Name(b) + " '" + i.name + "'");
      NetCables.Connect(o, i, c, null);
      NetCables.LocalCablesChanged();
      yield break;
    }
  }

  IEnumerator Disconnect() {
    var cables = new List<omniPlug>();
    foreach (omniPlug p in FindObjectsOfType<omniPlug>()) {
      if (!p.outputPlug || p.connected == null || p.otherPlug == null || p.otherPlug.connected == null) continue;
      if (p.curState != manipObject.manipState.none || p.otherPlug.curState != manipObject.manipState.none) continue;
      NetDevice a = p.connected.GetComponentInParent<NetDevice>(), b = p.otherPlug.connected.GetComponentInParent<NetDevice>();
      if (a == null || b == null || a.RemotelyHeld || b.RemotelyHeld) continue;
      cables.Add(p);
    }
    if (cables.Count == 0) yield break;
    omniPlug c = cables[rng.Next(cables.Count)];
    omniPlug other = c.otherPlug;
    Write("disconnect " + Name(c.connected.GetComponentInParent<NetDevice>()) + " '" + c.connected.name + "' -> " +
          Name(other.connected.GetComponentInParent<NetDevice>()) + " '" + other.connected.name + "'");
    c.connected.endConnection();
    other.connected.endConnection();
    Destroy(other.gameObject);
    Destroy(c.gameObject);
    NetCables.LocalCablesChanged();
    yield return null;
  }

  static Dictionary<NetDevice, HashSet<NetDevice>> CableGraph() {
    var g = new Dictionary<NetDevice, HashSet<NetDevice>>();
    foreach (omniPlug p in FindObjectsOfType<omniPlug>()) {
      if (!p.outputPlug || p.connected == null || p.otherPlug == null || p.otherPlug.connected == null) continue;
      NetDevice a = p.connected.GetComponentInParent<NetDevice>(), b = p.otherPlug.connected.GetComponentInParent<NetDevice>();
      if (a == null || b == null) continue;
      if (!g.TryGetValue(a, out var s)) g[a] = s = new HashSet<NetDevice>();
      s.Add(b);
    }
    return g;
  }

  static bool Reaches(Dictionary<NetDevice, HashSet<NetDevice>> g, NetDevice from, NetDevice to) {
    var seen = new HashSet<NetDevice>();
    var stack = new Stack<NetDevice>();
    stack.Push(from);
    while (stack.Count > 0) {
      NetDevice n = stack.Pop();
      if (n == to) return true;
      if (!seen.Add(n) || !g.TryGetValue(n, out var next)) continue;
      foreach (NetDevice m in next) stack.Push(m);
    }
    return false;
  }

  // ---------- things held when it stops ----------

  readonly List<Component> held = new List<Component>();

  void ReleaseAll() {
    foreach (Component c in held.ToArray()) {
      try {
        if (c == null) continue;
        if (c is key k) { if (TouchTracker.IsTouched(k)) TouchTracker.Touch(k, false, null); }
        else if (c is button b) b.setState(manipObject.manipState.none);
        else if (c is handle h) h.curState = manipObject.manipState.none;
      } catch (System.Exception) { }
    }
    held.Clear();
  }

  // ---------- status and state snapshots ----------

  IEnumerator StatusLoop() {
    int frames = 0;
    float t = 0, snap = 0;
    while (running) {
      yield return null;
      frames++;
      t += Time.unscaledDeltaTime;
      snap += Time.unscaledDeltaTime;
      if (t >= 10f) {
        int cables = 0;
        foreach (omniPlug p in FindObjectsOfType<omniPlug>()) if (p.outputPlug && p.connected != null) cables++;
        Write("status: " + (frames / t).ToString("0") + " fps, " + NetDevice.all.Count + " devices, " + cables + " cables, " + actions +
              " actions, " + errors + " errors, " + (System.GC.GetTotalMemory(false) >> 20) + " MB managed, " +
              (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20) + " MB total; " + NetSession.StatusText());
        frames = 0; t = 0;
      }
      if (snap >= 30f) { snap = 0; Snapshot(); }
    }
  }

  // each device's ID, type and saved-content hash: the same on every machine when the
  // session is in sync (the moment of an action in flight aside)
  void Snapshot() {
    var parts = new List<string>();
    var devs = new List<NetDevice>(NetDevice.all);
    devs.Sort((a, b) => a.netId.CompareTo(b.netId));
    foreach (NetDevice d in devs) {
      if (d == null || !d.IsDevice) continue;
      deviceInterface di = d.GetComponent<deviceInterface>();
      try {
        int h = NetSync.ContentHash(di, out menuItem.deviceType type);
        parts.Add(d.netId + ":" + type + ":" + h.ToString("x8"));
      } catch (System.Exception e) { parts.Add(d.netId + ":?" + e.GetType().Name); }
    }
    Write("state @" + System.DateTime.Now.ToString("HH:mm:ss") + " " + parts.Count + " devices: " + string.Join(" ", parts));
  }

  // ---------- logging ----------

  void OpenLog() {
    try {
      logPath = Path.Combine(Application.persistentDataPath, "harness_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
      lock (logLock) log = new StreamWriter(logPath, false) { AutoFlush = true };
    } catch (System.Exception e) { Debug.LogWarning("[Harness] can't write a log file: " + e.Message); log = null; logPath = "(none)"; }
  }

  void Write(string s) {
    string line = System.DateTime.Now.ToString("HH:mm:ss.fff") + " #" + actions + " " + s;
    lock (logLock) { try { log?.WriteLine(line); } catch (System.Exception) { } }
    Debug.Log("[Harness] " + s);
  }

  void Fail(string where, System.Exception e) {
    errors++;
    Write("FAILED in " + where + ": " + e);
  }

  readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>();

  // every error and exception logged anywhere in the app (any thread)
  void OnLog(string condition, string stack, LogType type) {
    if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
    lock (logLock) {
      errors++;
      errorCounts.TryGetValue(condition, out int n);
      errorCounts[condition] = ++n;
      if (n > 20 && n % 100 != 0) return; // a repeating error: keep the log readable
      try {
        log?.WriteLine(System.DateTime.Now.ToString("HH:mm:ss.fff") + " !! " + type + (n > 1 ? " (x" + n + ")" : "") + ": " + condition);
        if (!string.IsNullOrEmpty(stack)) log?.WriteLine("     " + stack.Replace("\n", "\n     ").TrimEnd());
      } catch (System.Exception) { }
    }
  }

  // ---------- helpers ----------

  float Range(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
  bool Chance(float p) { return rng.NextDouble() < p; }
  static string Name(NetDevice d) { return d == null ? "(gone)" : d.name.Replace("(Clone)", "") + "#" + d.netId; }
}
