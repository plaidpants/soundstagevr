// Playing and adjusting devices in multiplayer (2026-11, milestone 2).
//
// What a player does to a device's controls happens on every headset:
//  * Values - dial, slider, uneven slider, notched slider, 2D slider, switch: while a
//    player holds one, its value is sent ~20x/s, and once more (reliably) on release.
//  * Events - buttons (incl. sequencer cells), keys (keyboard, xylophone), drum pad
//    hits with their velocity: sent the moment they happen, through hooks in button,
//    key and drumpad.
//  * Drag handles - the mixer's and splitter's width, the sequencers' corner size handle,
//    the Scale Sequencer's top-row handle, the ADSR points, the timeline's height: sent
//    live like values; every headset resizes the device itself (2026-11-22).
//  * Anything else on a device that a player grabs and lets go of (the camera's screen...)
//    may change the device's structure, so the whole patch is shared again afterwards
//    (NetSync.LocalPatchReplaced). Held instruments (maracas, drumsticks, airhorn,
//    microphone), tapes, the help button, the scale list etc. are skipped: nothing to share.
// Changes that arrive are applied through each control's own setter, so the device
// reacts as if it had been touched here. Only changes made by a player are sent - a
// device switching its own buttons (radio groups, looper record/play) does the same
// on every headset by itself.
//
// A control is named by its device's network ID, its kind, and its index among that
// device's controls of that kind (same prefab and same patch => same order).

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetControlMsg : NetworkMessage { public int netId; public byte kind; public int index; public float a, b, c; } // c: the Control Cube's third axis

public class NetControls : MonoBehaviour {
  public enum Kind : byte { Dial, Slider, SliderUneven, SliderNotched, Slider2D, Switch, Button, Key, DrumHit, Octaves, Cube, XHandle, FilterBand, XYHandle, YHandle, GridTop }

  static NetControls instance;
  public static bool applying; // a received change is being applied: hooks don't send

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetControls");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetControls>();
  }

  // ---------- which kind a control is ----------

  static bool KindOf(Component c, out Kind k) {
    k = Kind.Dial;
    if (OnNote(c)) return false; // a note's own handles: NetTimelines shares the note
    if (c is dial) k = Kind.Dial;
    else if (c is sliderUneven) k = Kind.SliderUneven;
    else if (c is sliderNotched) k = Kind.SliderNotched;
    else if (c is slider2D) k = Kind.Slider2D;
    else if (c is slider) k = Kind.Slider;
    else if (c is basicSwitch) k = Kind.Switch;
    else if (c is button) k = Kind.Button;
    else if (c is key) k = Kind.Key;
    else if (c is drumpad) k = Kind.DrumHit;
    else if (c is cubeZone) k = Kind.Cube;
    // (2026-11-22) a drag handle (the mixer's width and fader length...) and the filter's
    // bands: their values go out live while held, as with a slider. They used to share the
    // whole patch on release - nothing moved on the other headsets while dragging, the
    // mixer's handle jumped when the reload came, and each reload made the filter's
    // middle band 2 cm wider (quadSection.setupPercents)
    else if (c is xHandle) k = Kind.XHandle;
    else if (c is quadSection) k = Kind.FilterBand;
    // (2026-11-22) the sequencers' corner size handle, the ADSR points, the timeline's
    // height handle and the Scale Sequencer's top-row handle: each used to share the whole
    // patch on release
    else if (c is xyHandle) k = Kind.XYHandle;
    else if (c is yHandle) k = Kind.YHandle;
    else if (c is gridEdgeHandle) k = Kind.GridTop; // the Control Cube's crosshairs (2026-11-21: they used to share the whole patch on release, and nothing while moving)
    else return false;
    return true;
  }

  static System.Type TypeOf(Kind k) {
    switch (k) {
      case Kind.Dial: return typeof(dial);
      case Kind.Slider: return typeof(slider);
      case Kind.SliderUneven: return typeof(sliderUneven);
      case Kind.SliderNotched: return typeof(sliderNotched);
      case Kind.Slider2D: return typeof(slider2D);
      case Kind.Switch: return typeof(basicSwitch);
      case Kind.Button: return typeof(button);
      case Kind.Key: return typeof(key);
      case Kind.Cube: return typeof(cubeZone);
      case Kind.XHandle: return typeof(xHandle);
      case Kind.FilterBand: return typeof(quadSection);
      case Kind.XYHandle: return typeof(xyHandle);
      case Kind.YHandle: return typeof(yHandle);
      case Kind.GridTop: return typeof(gridEdgeHandle);
      default: return typeof(drumpad);
    }
  }

  static bool Locate(Component c, Kind k, out NetDevice dev, out int index) {
    dev = c.GetComponentInParent<NetDevice>();
    index = -1;
    if (dev == null || dev.netId == 0) return false;
    index = System.Array.IndexOf(ControlList(dev, k), c);
    return index >= 0;
  }

  // Notes in a piano roll come and go, and each has two xHandles (its ends): counted in,
  // they shifted the numbering of the device's other xHandles differently on each headset.
  // They're left out; the notes themselves are shared by NetTimelines (2026-11-24).
  // a note's own handles, or the box that moves a multi-selection of notes (2026-11-24: letting
  // go of that box reloaded the whole patch on every other headset, restarting their timelines)
  static bool OnNote(Component c) { return c != null && (c.GetComponentInParent<timelineEvent>(true) != null || c.GetComponentInParent<timelineMultiSelect>(true) != null); }

  static Component[] ControlList(NetDevice dev, Kind k) {
    Component[] all = dev.GetComponentsInChildren(TypeOf(k), true);
    if (k != Kind.XHandle) return all;
    var keep = new List<Component>(all.Length);
    foreach (Component c in all) if (!OnNote(c)) keep.Add(c);
    return keep.ToArray();
  }

  static Component Find(NetControlMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return null;
    Component[] list = ControlList(dev, (Kind)m.kind);
    return m.index >= 0 && m.index < list.Length ? list[m.index] : null;
  }

  static void Send(Component c, Kind k, float a, float b, bool reliable, float c3 = 0) {
    if (!NetSync.Active || applying) return;
    if (!Locate(c, k, out NetDevice dev, out int index)) return;
    dev.Rebaseline();
    NetSync.SendControl(new NetControlMsg { netId = dev.netId, kind = (byte)k, index = index, a = a, b = b, c = c3 }, reliable);
  }

  // ---------- event hooks (button / key / drumpad) ----------

  static readonly HashSet<Component> sentOn = new HashSet<Component>();

  // button.keyHit: only presses made here by a player (grabbed, selected or touched),
  // plus the matching release of anything we sent as pressed.
  public static void ButtonHit(button b, bool on) {
    if (!NetSync.Active || applying) return;
    bool byPlayer = b.curState != manipObject.manipState.none || TouchTracker.IsTouched(b); // (the test harness grabs buttons too)
    if (on && byPlayer) { sentOn.Add(b); Send(b, Kind.Button, 1, 0, true); }
    else if (!on && (byPlayer || sentOn.Remove(b))) Send(b, Kind.Button, 0, 0, true);
  }

  // key.keyHitCheck: only keys a player here is touching or holding (a sequencer or
  // timeline playing the keyboard does the same on every headset by itself - sending
  // those made each headset replay the other's notes, and a stopped one kept getting
  // notes from the other), plus the release of any key we sent as pressed.
  public static void KeyHit(key k, bool on) {
    if (!NetSync.Active || applying) return;
    bool byPlayer = k.curState == manipObject.manipState.grabbed || TouchTracker.IsTouched(k);
    if (on && byPlayer) { sentOn.Add(k); Send(k, Kind.Key, 1, 0, true); }
    else if (!on && sentOn.Remove(k)) Send(k, Kind.Key, 0, 0, true);
  }

  // the test harness changed a value control (TestHarness): send it as a held one would be
  public static void HarnessValue(manipObject o, bool reliable) {
    if (o != null && ValueOf(o, out Kind k, out float a, out float b, out float c3)) Send(o, k, a, b, reliable, c3);
  }

  public static void DrumHit(drumpad p, float velocity) {
    if (!NetSync.Active || applying) return;
    Send(p, Kind.DrumHit, velocity, 0, true);
  }

  // ---------- value controls: poll what the local hands hold ----------

  class Held { public manipObject obj; public float a, b, c; public float timer; }
  readonly Held[] held = { new Held(), new Held() };
  manipulator[] manips;

  void LateUpdate() {
    if (!NetSync.Active) { held[0].obj = held[1].obj = null; return; }
    if (manips == null || manips[0] == null || manips[1] == null) {
      manips = new manipulator[2];
      foreach (manipulator m in FindObjectsOfType<manipulator>(true)) {
        int i = m.ControllerIndex;
        if (i == 0 || i == 1) manips[i] = m;
      }
    }
    for (int i = 0; i < 2; i++) Poll(held[i], manips[i] != null ? manips[i].getSelection() : null);
  }

  void Poll(Held h, manipObject now) {
    if (h.obj != now) {
      if (h.obj != null) Released(h.obj, h);
      h.obj = now;
      h.timer = 0;
      if (now != null && ValueOf(now, out Kind k0, out float a0, out float b0, out float c0)) {
        h.a = a0; h.b = b0; h.c = c0;
        if (k0 == Kind.XHandle) h.c = 0; // already grabbed when picked up here: send the hold
      }
    }
    if (h.obj == null || !ValueOf(h.obj, out Kind k, out float a, out float b, out float c)) return;
    h.timer += Time.unscaledDeltaTime;
    bool holdChanged = k == Kind.XHandle && c != h.c; // grabbed / let go: at once, reliably
    if ((a != h.a || b != h.b || c != h.c) && (h.timer >= 1f / NetSync.poseRate || holdChanged)) {
      h.a = a; h.b = b; h.c = c; h.timer = 0;
      Send(h.obj, k, a, b, holdChanged, c);
      SendLinked(h.obj, false);
    }
  }

  // An ADSR point pushes its neighbours along (adsrInterface): those move without being
  // held, so send them with it - otherwise the other headsets' envelopes came out different.
  static void SendLinked(manipObject o, bool reliable) {
    if (!(o is xyHandle)) return;
    adsrInterface ai = o.GetComponentInParent<adsrInterface>();
    if (ai == null || ai.xyHandles == null) return;
    foreach (xyHandle x in ai.xyHandles)
      if (x != null && x != o && ValueOf(x, out Kind k, out float a, out float b, out float c)) Send(x, k, a, b, reliable, c);
  }

  // A sequencer's grid changed size (on every headset, in its own Update): its cells and
  // jacks were renumbered, so the cables known by jack number are taken from the scene again.
  public static void Regridded() {
    if (!NetSync.Active || instance == null) return;
    instance.StartCoroutine(RescanCables());
  }

  static System.Collections.IEnumerator RescanCables() {
    yield return null; // removed cells and jacks go at the end of the frame
    if (NetSync.Active) NetCables.ForgetKnown();
  }

  void Released(manipObject obj, Held h) {
    if (obj == null) return;
    if (ValueOf(obj, out Kind k, out float a, out float b, out float c)) {
      Send(obj, k, a, b, true, c); // final value, reliable
      SendLinked(obj, true);
      return;
    }
    // Not a value control. On a device and not one of the things handled elsewhere?
    // Then it may have changed the device's structure: share the patch again.
    if (obj is handle || obj is button || obj is key || NetTools.IsTool(obj) || obj is omniPlug || obj is omniJack) return;
    if (obj is tape) return; // NetTapes
    if (obj is generalGrip && obj.GetComponentInParent<samplerDeviceInterface>() is samplerDeviceInterface sd) { NetSampler.TurntableMoved(sd); return; } // the sampler's turntable moved: just its place, not the whole patch (a reload emptied the other headsets' loopers)
    if (obj is generalGrip) return; // (2026-11-24) the keyboard's / xylophone's MIDI panels and sequencer, dragged about: where they sit isn't in the patch, so resending it shared nothing - it only reloaded every other headset, restarting their sequencers
    if (obj is turntableUI) return;
    if (obj is maracaUI || obj is drumstick || obj is airhornUI || obj is microphoneGrip) return; // held instruments: they go back to their place when let go, nothing in the patch changed
    if (OnNote(obj)) return; // a note moved, resized or recorded: NetTimelines sends just that note (this used to reload the whole patch on every headset - a big hitch each time a note was let go)
    if (obj is tooltips) return; // the help (?) button: showing help or its video is local - a patch reload here restarted every device // scratching only moves the playhead: nothing in the patch changes, and a whole-patch reload here dropped whatever the other hand was holding
    if (obj is scalePickItem) return; // the Scale Sequencer's scale button / list: opening the list is local (a patch reload closed it before a scale could be picked); a pick is sent on its own (NetScales)
    if (obj is midiPanel) return; // the MIDI device list: picking from it is local (and shared by NetMidi) - a patch reload here closed the list before anything could be picked
    if (obj.name == "sizeHandle" && (obj.GetComponentInParent<keyboardDeviceInterface>() != null || obj.GetComponentInParent<xylorollDeviceInterface>() != null)) return; // NetControls.Resized
    if (obj.GetComponentInParent<libraryDeviceInterface>() != null) return; // the tape library's wheels: browsing, nothing to share
    NetDevice dev = obj.GetComponentInParent<NetDevice>();
    if (dev == null || dev.netId == 0) return;
    Debug.Log("[NetControls] " + obj.GetType().Name + " on " + dev.name + " released: sharing the patch");
    NetSync.LocalPatchReplaced();
  }

  static bool ValueOf(manipObject o, out Kind k, out float a, out float b, out float c) {
    a = b = c = 0;
    if (!KindOf(o, out k)) return false;
    switch (k) {
      case Kind.Dial: a = ((dial)o).percent; return true;
      case Kind.Slider: a = ((slider)o).percent; return true;
      case Kind.SliderUneven: a = ((sliderUneven)o).percent; return true;
      case Kind.SliderNotched: a = ((sliderNotched)o).switchVal; return true;
      case Kind.Slider2D: a = ((slider2D)o).percent.x; b = ((slider2D)o).percent.y; return true;
      case Kind.Switch: a = ((basicSwitch)o).switchVal ? 1 : 0; return true;
      case Kind.Cube: { Vector3 p = ((cubeZone)o).Percent; a = p.x; b = p.y; c = p.z; return true; }
      case Kind.XHandle: a = o.transform.localPosition.x; c = o.curState == manipObject.manipState.grabbed ? 1 : 0; return true; // c: held
      case Kind.FilterBand: return ((quadSection)o).NetGetPercents(out a, out b);
      case Kind.XYHandle: a = o.transform.localPosition.x; b = o.transform.localPosition.y; return true;
      case Kind.YHandle: { yHandle y = (yHandle)o; a = y.targetTransform != null ? y.targetTransform.localPosition.y : o.transform.localPosition.y; return true; }
      case Kind.GridTop: { gridEdgeHandle g = (gridEdgeHandle)o; if (g.owner == null) return false; a = g.owner.NetTopSlot; return true; }
    }
    return false; // buttons and keys are events
  }

  // ---------- apply what arrives ----------

  // keyboard / xylophone roll range dragged here (a = octaves)
  public static void Resized(deviceInterface di, int octaves) {
    if (!NetSync.Active || applying) return;
    NetDevice dev = di.GetComponent<NetDevice>();
    if (dev == null || dev.netId == 0) return;
    dev.Rebaseline();
    NetSync.SendControl(new NetControlMsg { netId = dev.netId, kind = (byte)Kind.Octaves, index = 0, a = octaves }, true);
  }

  public static void Apply(NetControlMsg m) {
    if ((Kind)m.kind == Kind.Octaves) {
      if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice d) || d == null) return;
      applying = true;
      try {
        keyboardDeviceInterface kb = d.GetComponent<keyboardDeviceInterface>();
        if (kb != null) kb.NetSetOctaves(Mathf.RoundToInt(m.a));
        xylorollDeviceInterface xy = d.GetComponent<xylorollDeviceInterface>();
        if (xy != null) xy.NetSetOctaves(Mathf.RoundToInt(m.a));
      } finally { applying = false; }
      d.Rebaseline();
      return;
    }
    Component c = Find(m);
    if (NetDevice.byId.TryGetValue(m.netId, out NetDevice target) && target != null) target.Rebaseline();
    if (c == null) { Debug.Log("[NetControls] no control " + (Kind)m.kind + " #" + m.index + " on device " + m.netId); return; }
    if (c is manipObject mo && mo.curState == manipObject.manipState.grabbed) return; // we're holding it
    applying = true;
    try {
      switch ((Kind)m.kind) {
        case Kind.Dial: ((dial)c).setPercent(m.a); break;
        case Kind.Slider: ((slider)c).setPercent(m.a); break;
        case Kind.SliderUneven: ((sliderUneven)c).setPercent(m.a); break;
        case Kind.SliderNotched: ((sliderNotched)c).setVal(Mathf.RoundToInt(m.a)); break;
        case Kind.Slider2D: ((slider2D)c).setPercent(new Vector2(m.a, m.b)); break;
        case Kind.Switch: {
          basicSwitch sw = (basicSwitch)c;
          timelineComponentInterface tl = sw.GetComponentInParent<timelineComponentInterface>();
          if (tl != null && tl.snapSwitch == sw) tl.NetSnapSet(m.a > .5f); // the notes' new places come from NetTimelines
          sw.setSwitch(m.a > .5f);
          break;
        }
        case Kind.Button: ((button)c).keyHit(m.a > .5f); break;
        case Kind.Key: ((key)c).NetSetHit(m.a > .5f); break;
        case Kind.DrumHit: ((drumpad)c).keyHit(true, m.a); break;
        case Kind.Cube: ((cubeZone)c).NetSet(new Vector3(m.a, m.b, m.c)); break;
        case Kind.XHandle: ((xHandle)c).netHeld = m.c > .5f; ((xHandle)c).updatePos(m.a); ((xHandle)c).netMoved = true; break;
        case Kind.FilterBand: ((quadSection)c).NetSetPercents(m.a, m.b); break;
        case Kind.XYHandle: ((xyHandle)c).NetSetPos(new Vector2(m.a, m.b)); break;
        case Kind.YHandle: ((yHandle)c).updatePos(m.a); break;
        case Kind.GridTop: { gridEdgeHandle g = (gridEdgeHandle)c; if (g.owner != null) g.owner.NetSetTop(Mathf.RoundToInt(m.a)); break; }
      }
    } finally { applying = false; }
  }
}
