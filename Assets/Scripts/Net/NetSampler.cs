// The Sampler in multiplayer (2026-11-18).
//
// Every headset plays its own copy of a sample (local audio), but pressing PLAY, a
// trigger cable, BACK and scratching happened at slightly different moments on each one,
// or (scratching, a trigger from a cable being pulled out) on one headset only. So:
//  * Transport - the host says where each of its samplers is: whether it's playing and
//    how far into the sample, against the network clock (NetworkTime). It says so when
//    that changes (start, stop, a jump such as BACK or a retrigger) and once a second.
//    The others work out where that puts the sample now and move theirs there if it is
//    more than 30 ms off, start it if it should be playing, and stop it if it has been
//    playing on its own for a while (short hits from a sequencer start on every headset
//    by themselves at the same moment, so those aren't cut).
//  * Scratching - while a player holds the waveform's playhead or the disc, where it is
//    goes out ~20x/s; the other headsets scrub their copy to follow (playhead and disc
//    move too). Letting go sends the final place.
//  * The turntable moved on the sampler: only its place goes out (it used to share the
//    whole patch, and the reload emptied the other headsets' loopers).
// Positions are in seconds: the Quest runs at 48 kHz and the PC at 44.1 kHz.

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetSamplerMsg : NetworkMessage { public int netId; public byte what; public bool on; public float pos; public double time; public Vector3 p; public Quaternion r; }

public class NetSampler : MonoBehaviour {
  const byte Transport = 0, Scrub = 1, Turntable = 2;
  public static float heartbeat = 1f;     // host: transport at least this often (s)
  public static float tolerance = .03f;   // off by more than this (s): moved into line
  public static float soloStop = .5f;     // playing here but not on the host for this long: stopped

  static NetSampler instance;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetSampler");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<NetSampler>();
  }

  class State {
    public bool sentActive; public float sentSec; public float sentAt = -100; public float lastSec; public float lastT;
    public bool held; public float heldTimer;
    public bool wasActive; public float activeSince;
  }
  readonly Dictionary<clipPlayerComplex, State> states = new Dictionary<clipPlayerComplex, State>();
  clipPlayerComplex[] players = new clipPlayerComplex[0];
  float refresh;

  static bool LocallyHeld(clipPlayerComplex p) { return (p.scrubGrabbed && !p.netScrub) || p.turntableGrabbed; }
  static float Speed(clipPlayerComplex p) { return p.speedGen != null ? float.NaN : p.playbackSpeed; } // NaN: can't predict (speed from a cable)

  void Update() {
    if (!NetSync.Active) { if (states.Count > 0) states.Clear(); return; }
    refresh -= Time.unscaledDeltaTime;
    if (refresh <= 0) { refresh = .5f; players = FindObjectsOfType<clipPlayerComplex>(); }
    float now = Time.unscaledTime;
    foreach (clipPlayerComplex p in players) {
      if (p == null || !p.loaded) continue;
      NetDevice dev = p.GetComponentInParent<NetDevice>();
      if (dev == null || dev.netId == 0) continue;
      if (!states.TryGetValue(p, out State s)) states[p] = s = new State { lastSec = p.NetSeconds, lastT = now };
      float sec = p.NetSeconds;

      if (p.active && !s.wasActive) s.activeSince = now;
      s.wasActive = p.active;

      // scratching here
      bool held = LocallyHeld(p);
      if (held) {
        s.heldTimer += Time.unscaledDeltaTime;
        if (!s.held || s.heldTimer >= 1f / NetSync.poseRate) { s.heldTimer = 0; Send(new NetSamplerMsg { netId = dev.netId, what = Scrub, on = true, pos = sec }, false); }
      } else if (s.held) {
        Send(new NetSamplerMsg { netId = dev.netId, what = Scrub, on = false, pos = sec }, true);
        s.sentAt = -100; // and the host follows with its transport at once
      }
      s.held = held;

      // host: transport
      if (NetworkServer.active && !held && !p.netScrub) {
        float speed = Speed(p);
        bool jumped = false;
        if (p.active && !float.IsNaN(speed)) {
          float predicted = s.lastSec + speed * (now - s.lastT);
          jumped = Mathf.Abs(sec - predicted) > .05f;
        }
        if (p.active != s.sentActive || jumped || now - s.sentAt >= heartbeat) {
          s.sentActive = p.active; s.sentSec = sec; s.sentAt = now;
          Send(new NetSamplerMsg { netId = dev.netId, what = Transport, on = p.active, pos = sec, time = NetworkTime.time }, true);
        }
      }
      s.lastSec = sec; s.lastT = now;
    }
  }

  // the turntable was moved on this sampler (NetControls)
  public static void TurntableMoved(samplerDeviceInterface sd) {
    NetDevice dev = sd != null ? sd.GetComponent<NetDevice>() : null;
    if (dev == null || dev.netId == 0 || sd.turntableObject == null) return;
    Transform t = sd.turntableObject.transform;
    Send(new NetSamplerMsg { netId = dev.netId, what = Turntable, p = t.localPosition, r = t.localRotation }, true);
  }

  static void Send(NetSamplerMsg m, bool reliable) {
    int ch = reliable ? Channels.Reliable : Channels.Unreliable;
    if (NetworkServer.active) Relay(m, null, ch);
    else if (NetworkClient.isConnected) NetworkClient.Send(m, ch);
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetSamplerMsg>((conn, m) => {
      if (m.what == Transport) return; // only the host says that
      Apply(m);
      Relay(m, conn, m.what == Scrub && m.on ? Channels.Unreliable : Channels.Reliable);
    });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetSamplerMsg>(Apply);
  }

  static void Relay(NetSamplerMsg m, NetworkConnectionToClient from, int ch) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m, ch);
  }

  static void Apply(NetSamplerMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    if (m.what == Turntable) {
      samplerDeviceInterface sd = dev.GetComponent<samplerDeviceInterface>();
      if (sd == null || sd.turntableObject == null) return;
      sd.turntableObject.transform.localPosition = m.p;
      sd.turntableObject.transform.localRotation = m.r;
      return;
    }
    clipPlayerComplex p = dev.GetComponent<clipPlayerComplex>();
    if (p == null || !p.loaded || LocallyHeld(p)) return; // we're scratching it ourselves

    if (m.what == Scrub) {
      if (m.on) p.NetScrubTo(m.pos);
      else p.NetScrubEnd(m.pos);
      return;
    }

    // transport from the host: where is it now?
    if (p.netScrub) return;
    bool on = m.on;
    float sec = m.pos;
    float speed = Speed(p);
    if (on && !float.IsNaN(speed)) {
      sec += speed * (float)System.Math.Max(0, NetworkTime.time - m.time);
      float a = p.NetBoundSeconds(0), b = p.NetBoundSeconds(1), len = b - a;
      if (sec > b || sec < a) {
        if (p.looping && len > 0) sec = a + Mathf.Repeat(sec - a, len);
        else on = false; // it has run off the end on the host by now
      }
    }
    State s = instance != null && instance.states.TryGetValue(p, out State st) ? st : null;
    if (on) {
      if (!p.active) {
        p.NetSeek(sec);
        p.active = true;
        if (p._sampleInterface != null) p._sampleInterface.playEvent(true);
      } else if (!float.IsNaN(speed) && Mathf.Abs(p.NetSeconds - sec) > tolerance) {
        p.NetSeek(sec);
      }
    } else if (p.active && m.on == false && s != null && Time.unscaledTime - s.activeSince > soloStop) {
      p.active = false;
      if (p._sampleInterface != null) p._sampleInterface.playEvent(false);
    }
  }
}
