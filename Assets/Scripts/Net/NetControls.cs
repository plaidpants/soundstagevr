// Playing and adjusting devices in multiplayer (2026-11, milestone 2).
//
// What a player does to a device's controls happens on every headset:
//  * Values - dial, slider, uneven slider, notched slider, 2D slider, switch: while a
//    player holds one, its value is sent ~20x/s, and once more (reliably) on release.
//  * Events - buttons (incl. sequencer cells), keys (keyboard, xylophone), drum pad
//    hits with their velocity: sent the moment they happen, through hooks in button,
//    key and drumpad.
//  * Anything else on a device that a player grabs and lets go of (size handles, grid
//    edges, the scale list, tapes...) can change the device's structure, so the whole
//    patch is shared again afterwards (NetSync.LocalPatchReplaced).
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

public struct NetControlMsg : NetworkMessage { public int netId; public byte kind; public int index; public float a, b; }

public class NetControls : MonoBehaviour {
  public enum Kind : byte { Dial, Slider, SliderUneven, SliderNotched, Slider2D, Switch, Button, Key, DrumHit, Octaves }

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
    if (c is dial) k = Kind.Dial;
    else if (c is sliderUneven) k = Kind.SliderUneven;
    else if (c is sliderNotched) k = Kind.SliderNotched;
    else if (c is slider2D) k = Kind.Slider2D;
    else if (c is slider) k = Kind.Slider;
    else if (c is basicSwitch) k = Kind.Switch;
    else if (c is button) k = Kind.Button;
    else if (c is key) k = Kind.Key;
    else if (c is drumpad) k = Kind.DrumHit;
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
      default: return typeof(drumpad);
    }
  }

  static bool Locate(Component c, Kind k, out NetDevice dev, out int index) {
    dev = c.GetComponentInParent<NetDevice>();
    index = -1;
    if (dev == null || dev.netId == 0) return false;
    index = System.Array.IndexOf(dev.GetComponentsInChildren(TypeOf(k), true), c);
    return index >= 0;
  }

  static Component Find(NetControlMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return null;
    Component[] list = dev.GetComponentsInChildren(TypeOf((Kind)m.kind), true);
    return m.index >= 0 && m.index < list.Length ? list[m.index] : null;
  }

  static void Send(Component c, Kind k, float a, float b, bool reliable) {
    if (!NetSync.Active || applying) return;
    if (!Locate(c, k, out NetDevice dev, out int index)) return;
    dev.Rebaseline();
    NetSync.SendControl(new NetControlMsg { netId = dev.netId, kind = (byte)k, index = index, a = a, b = b }, reliable);
  }

  // ---------- event hooks (button / key / drumpad) ----------

  static readonly HashSet<Component> sentOn = new HashSet<Component>();

  // button.keyHit: only presses made here by a player (grabbed, selected or touched),
  // plus the matching release of anything we sent as pressed.
  public static void ButtonHit(button b, bool on) {
    if (!NetSync.Active || applying) return;
    bool byPlayer = b.curState != manipObject.manipState.none || TouchTracker.IsTouched(b);
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

  public static void DrumHit(drumpad p, float velocity) {
    if (!NetSync.Active || applying) return;
    Send(p, Kind.DrumHit, velocity, 0, true);
  }

  // ---------- value controls: poll what the local hands hold ----------

  class Held { public manipObject obj; public float a, b; public float timer; }
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
      if (now != null && ValueOf(now, out _, out float a0, out float b0)) { h.a = a0; h.b = b0; }
    }
    if (h.obj == null || !ValueOf(h.obj, out Kind k, out float a, out float b)) return;
    h.timer += Time.unscaledDeltaTime;
    if ((a != h.a || b != h.b) && h.timer >= 1f / NetSync.poseRate) {
      h.a = a; h.b = b; h.timer = 0;
      Send(h.obj, k, a, b, false);
    }
  }

  void Released(manipObject obj, Held h) {
    if (obj == null) return;
    if (ValueOf(obj, out Kind k, out float a, out float b)) {
      Send(obj, k, a, b, true); // final value, reliable
      return;
    }
    // Not a value control. On a device and not one of the things handled elsewhere?
    // Then it may have changed the device's structure: share the patch again.
    if (obj is handle || obj is button || obj is key || NetTools.IsTool(obj) || obj is omniPlug || obj is omniJack) return;
    if (obj is tape) return; // NetTapes
    if (obj.name == "sizeHandle" && (obj.GetComponentInParent<keyboardDeviceInterface>() != null || obj.GetComponentInParent<xylorollDeviceInterface>() != null)) return; // NetControls.Resized
    if (obj.GetComponentInParent<libraryDeviceInterface>() != null) return; // the tape library's wheels: browsing, nothing to share
    NetDevice dev = obj.GetComponentInParent<NetDevice>();
    if (dev == null || dev.netId == 0) return;
    Debug.Log("[NetControls] " + obj.GetType().Name + " on " + dev.name + " released: sharing the patch");
    NetSync.LocalPatchReplaced();
  }

  static bool ValueOf(manipObject o, out Kind k, out float a, out float b) {
    a = b = 0;
    if (!KindOf(o, out k)) return false;
    switch (k) {
      case Kind.Dial: a = ((dial)o).percent; return true;
      case Kind.Slider: a = ((slider)o).percent; return true;
      case Kind.SliderUneven: a = ((sliderUneven)o).percent; return true;
      case Kind.SliderNotched: a = ((sliderNotched)o).switchVal; return true;
      case Kind.Slider2D: a = ((slider2D)o).percent.x; b = ((slider2D)o).percent.y; return true;
      case Kind.Switch: a = ((basicSwitch)o).switchVal ? 1 : 0; return true;
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
        case Kind.Switch: ((basicSwitch)c).setSwitch(m.a > .5f); break;
        case Kind.Button: ((button)c).keyHit(m.a > .5f); break;
        case Kind.Key: ((key)c).NetSetHit(m.a > .5f); break;
        case Kind.DrumHit: ((drumpad)c).keyHit(true, m.a); break;
      }
    } finally { applying = false; }
  }
}
