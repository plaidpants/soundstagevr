// Shared clock for multiplayer (2026-11, milestone 3).
//
// Each headset's master clock (masterControl: the measure position that drum
// machines, timelines and the metronome follow) and each slow oscillator (LFO) runs
// on its own audio hardware, so they drift apart. Twice a second the host sends its
// measure position, BPM and every LFO's phase, stamped with the network time. Joined
// headsets work out where the host is *now* (Mirror's NetworkTime corrects for the
// trip), and ease their own clock and LFOs towards it by running them up to a few
// percent fast or slow (a jump would skip or repeat a beat); only big differences
// (joining, a reset) jump.

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetClockMsg : NetworkMessage {
  public double time;      // host NetworkTime when sampled
  public float bpm;
  public double measure;   // seconds into the measure
  public bool running;     // metronome started
  public long measureCount;  // measures since the clock started
  public int[] seqIds;       // running step sequencers: device ID,
  public int[] seqAnchor;    // step minus ticks played (see sequencerDeviceInterface.NetAnchor)
  public int[] seqLen;       // steps and ticks per measure (only compared when the same here)
  public int[] lfoIds;     // device ID of each LFO oscillator
  public double[] lfoPhase; // its phase, 0..1
  public float[] lfoFreq;
}

// a joined headset turned its tempo dial: the host takes it on and shares it
// (and pressed the metronome's start / stop or reset)
public struct NetTempoMsg : NetworkMessage {
  public float bpm;   // 0: unchanged
  public sbyte run;   // -1 unchanged, 0 stop, 1 start
  public bool reset;
}

public class NetClock : MonoBehaviour {
  public static float sendInterval = .5f;
  public static double snapSeconds = .25;  // further off than this: jump (joining, a reset)
  public static double deadband = .002;    // closer than this: leave it
  public static double lfoMaxRate = .05;   // LFOs run up to 5% fast / slow to catch up
  public static double lfoSnapSeconds = .25;

  // LFOs being eased towards the host: phase (cycles) still to make up
  static readonly Dictionary<oscillatorSignalGenerator, double> lfoLeft = new Dictionary<oscillatorSignalGenerator, double>();

  float timer;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    GameObject g = new GameObject("NetClock");
    DontDestroyOnLoad(g);
    g.AddComponent<NetClock>();
    Debug.Log("[NetClock] ready");
  }

  static int sentCount;

  static float lastBpm = -1;        // joined: the tempo as last set by the host
  static float ignoreHostBpmUntil;  // joined: after a local change, until the host has it
  static float ignoreHostRunUntil;
  static float lastSentBpm = -1, sendBpmTimer;
  static bool dialWasHeld;
  static bool sendNow, applying;

  static metronome Metronome() { return FindObjectOfType<metronome>(); }

  static void SetTempo(float bpm) {
    metronome nome = Metronome(); // its dial drives the tempo
    if (nome != null) nome.SetBPM(bpm);
    else masterControl.instance.setBPM(bpm);
  }

  // start / stop as if the metronome's button had been pressed (so it lights up)
  static void SetRunning(bool on) {
    metronome nome = Metronome();
    applying = true;
    try {
      button b = null;
      if (nome != null)
        foreach (button x in nome.GetComponentsInChildren<button>(true))
          if (x.buttonID == 0 && x._componentInterface == nome) { b = x; break; }
      if (b != null) b.keyHit(on);
      else masterControl.instance.toggleBeatUpdate(on);
    } finally { applying = false; }
  }

  // metronome.hit: start / stop (ID 0) or reset (ID 1) pressed here
  public static void LocalMetronome(int id, bool on) {
    if (applying || !NetSync.Active) return;
    if (id != 0 && !(id == 1 && on)) return;
    if (NetSync.IsJoinedClient) {
      if (id == 0) ignoreHostRunUntil = Time.unscaledTime + 1.5f;
      NetworkClient.Send(new NetTempoMsg { run = (sbyte)(id == 0 ? (on ? 1 : 0) : -1), reset = id == 1 });
    } else if (NetworkServer.active) sendNow = true;
  }

  // host: a joined headset changed the tempo, started / stopped or reset
  public static void OnServerTempo(NetworkConnectionToClient conn, NetTempoMsg m) {
    masterControl mc = masterControl.instance;
    if (mc == null) return;
    if (m.bpm > 0) SetTempo(m.bpm);
    if (m.run >= 0 && (m.run == 1) != mc.BeatRunning) {
      Debug.Log("[NetClock] " + (m.run == 1 ? "start" : "stop") + " from a joined headset");
      SetRunning(m.run == 1);
    }
    if (m.reset) { Debug.Log("[NetClock] reset from a joined headset"); mc.resetClock(); }
    sendNow = true;
  }

  static readonly List<oscillatorSignalGenerator> lfoKeys = new List<oscillatorSignalGenerator>();

  // ease each LFO a little each frame: at most 5% of its own speed
  static void EaseLfos(float dt) {
    if (lfoLeft.Count == 0) return;
    lfoKeys.Clear(); lfoKeys.AddRange(lfoLeft.Keys);
    foreach (oscillatorSignalGenerator o in lfoKeys) {
      double left = lfoLeft[o];
      if (o == null || !o.lfo) { lfoLeft.Remove(o); continue; }
      double max = lfoMaxRate * o.frequency * dt;
      double step = left > max ? max : left < -max ? -max : left;
      double p = o._phase + step;
      o._phase = p - System.Math.Floor(p);
      left -= step;
      if (System.Math.Abs(left) < 1e-7) lfoLeft.Remove(o); else lfoLeft[o] = left;
    }
  }

  void Update() {
    masterControl mc = masterControl.instance;
    if (mc == null) return;
    sendBpmTimer += Time.unscaledDeltaTime;
    if (NetSync.IsJoinedClient) EaseLfos(Time.unscaledDeltaTime); else lfoLeft.Clear();

    if (NetSync.IsJoinedClient) {
      // only a tempo dial a player here is turning counts as a change made here
      // (anything else - the host's tempo arriving - must not be sent back)
      metronome nome = Metronome();
      bool held = nome != null && nome.bpmDial != null && nome.bpmDial.curState == manipObject.manipState.grabbed;
      if (held || dialWasHeld) {
        bool changed = lastBpm < 0 || Mathf.Abs(mc.bpm - lastBpm) > .05f;
        bool final = dialWasHeld && !held;
        if (changed && (sendBpmTimer >= .05f || final)) {
          lastBpm = mc.bpm;
          sendBpmTimer = 0;
          NetworkClient.Send(new NetTempoMsg { bpm = mc.bpm, run = -1 });
        }
        if (changed || held) ignoreHostBpmUntil = Time.unscaledTime + 1f;
      }
      dialWasHeld = held;
      return;
    }
    lastBpm = -1;
    dialWasHeld = false;

    if (!NetworkServer.active) return;
    // a tempo change here goes out at once (20 a second while turning)
    if (Mathf.Abs(mc.bpm - lastSentBpm) > .05f && sendBpmTimer >= .05f) sendNow = true;
    timer += Time.unscaledDeltaTime;
    if (timer < sendInterval && !sendNow) return;
    sendNow = false;
    timer = 0;
    sendBpmTimer = 0;
    NetClockMsg m = Sample();
    lastSentBpm = m.bpm;
    NetSync.SendClock(m);
    if (sentCount++ % 20 == 0)
      Debug.Log("[NetClock] host: sent clock to " + (NetworkServer.connections.Count - 1) + " headset(s), bpm " + m.bpm.ToString("F1") + ", " + m.lfoIds.Length + " LFO(s), running=" + m.running);
  }

  static NetClockMsg Sample() {
    var ids = new List<int>();
    var phases = new List<double>();
    var freqs = new List<float>();
    foreach (oscillatorSignalGenerator o in FindObjectsOfType<oscillatorSignalGenerator>()) {
      if (!o.lfo) continue;
      NetDevice d = o.GetComponentInParent<NetDevice>();
      if (d == null || d.netId == 0) continue;
      ids.Add(d.netId);
      phases.Add(o._phase);
      freqs.Add(o.frequency);
    }
    var sIds = new List<int>(); var sAnchor = new List<int>(); var sLen = new List<int>();
    foreach (sequencerDeviceInterface sq in FindObjectsOfType<sequencerDeviceInterface>()) {
      if (!sq.NetSyncable) continue;
      NetDevice d = sq.GetComponent<NetDevice>();
      if (d == null || d.netId == 0) continue;
      sIds.Add(d.netId); sAnchor.Add(sq.NetAnchor()); sLen.Add(sq.NetLength * 1000 + sq.NetTicks);
    }
    return new NetClockMsg {
      time = NetworkTime.time,
      bpm = masterControl.instance.bpm,
      measure = masterControl.instance.MeasurePhaseSeconds,
      running = masterControl.instance.BeatRunning,
      measureCount = masterControl.instance.measureCount,
      seqIds = sIds.ToArray(), seqAnchor = sAnchor.ToArray(), seqLen = sLen.ToArray(),
      lfoIds = ids.ToArray(), lfoPhase = phases.ToArray(), lfoFreq = freqs.ToArray()
    };
  }

  static double Wrap(double x, double period) {
    x %= period;
    if (x > period / 2) x -= period;
    if (x < -period / 2) x += period;
    return x;
  }

  static int logCount;

  // a sequencer off from the host's by the same number of steps in two messages in a
  // row (one could catch a step mid-change) is moved into line
  static readonly Dictionary<int, int> seqOff = new Dictionary<int, int>();
  static void ApplySequencers(NetClockMsg m, bool clockSettled) {
    if (m.seqIds == null || !clockSettled) return;
    for (int i = 0; i < m.seqIds.Length; i++) {
      if (!NetDevice.byId.TryGetValue(m.seqIds[i], out NetDevice d) || d == null) continue;
      sequencerDeviceInterface sq = d.GetComponent<sequencerDeviceInterface>();
      if (sq == null || !sq.NetSyncable || sq.NetLength * 1000 + sq.NetTicks != m.seqLen[i]) { seqOff.Remove(m.seqIds[i]); continue; }
      int len = sq.NetLength;
      int off = ((m.seqAnchor[i] - sq.NetAnchor()) % len + len) % len;
      if (off == 0) { seqOff.Remove(m.seqIds[i]); continue; }
      if (seqOff.TryGetValue(m.seqIds[i], out int last) && last == off) {
        Debug.Log("[NetClock] " + d.name + " was " + off + " step(s) off the host: moved into line");
        sq.NetShiftSteps(off);
        seqOff.Remove(m.seqIds[i]);
      } else seqOff[m.seqIds[i]] = off;
    }
  }

  public static void Apply(NetClockMsg m) {
    masterControl mc = masterControl.instance;
    if (mc == null) return;
    double elapsed = NetworkTime.time - m.time; // how long ago the host sampled it
    if (elapsed < 0) elapsed = 0;

    bool tempoSettling = Time.unscaledTime < ignoreHostBpmUntil;
    if (!tempoSettling) {
      if (Mathf.Abs(m.bpm - mc.bpm) > .05f) SetTempo(m.bpm);
      lastBpm = mc.bpm;
    }
    if (Time.unscaledTime >= ignoreHostRunUntil && m.running != mc.BeatRunning) SetRunning(m.running);

    // where the host is in its measure now, as a share of the measure (so a
    // tempo change on the way doesn't count as being off), in our seconds
    double hostPeriod = 480.0 / Mathf.Max(1f, m.bpm);
    double hostCycle = (m.measure + (m.running ? elapsed : 0)) / hostPeriod;
    hostCycle -= System.Math.Floor(hostCycle);
    double period = mc.measurePeriod;
    double error = Wrap(hostCycle * period - mc.MeasurePhaseSeconds, period);
    // measure count: take the host's (where it is now, whole measures)
    double hostAbs = m.measureCount + (m.measure + (m.running ? elapsed : 0)) / hostPeriod;
    double myAbs = mc.measureCount + mc.MeasurePhaseSeconds / period;
    long fix = (long)System.Math.Round(hostAbs - myAbs);
    if (fix != 0) mc.AddMeasures(fix);
    ApplySequencers(m, fix == 0 && System.Math.Abs(error) < .03);

    if (tempoSettling) return; // while the tempo is being turned here, leave the beat be
    // small gaps: run a little fast / slow until closed (a jump skips or repeats beats)
    if (System.Math.Abs(error) > snapSeconds) mc.AdjustClock(error);
    else mc.SlewClock(System.Math.Abs(error) < deadband ? 0 : error);
    if (logCount++ % 20 == 0) // first one at once, then every ~10 s
      Debug.Log("[NetClock] measure off by " + (error * 1000).ToString("F1") + " ms (ping " + (NetworkTime.rtt * 1000).ToString("F0") + " ms)");

    // LFOs
    if (m.lfoIds == null) return;
    for (int i = 0; i < m.lfoIds.Length; i++) {
      if (!NetDevice.byId.TryGetValue(m.lfoIds[i], out NetDevice d) || d == null) continue;
      oscillatorSignalGenerator o = d.GetComponentInChildren<oscillatorSignalGenerator>();
      // only an LFO set the same way here (another setting: leave it, the patch or
      // its controls will bring it in line - correcting a different oscillator
      // would chop its wave up)
      if (o == null || !o.lfo || Mathf.Abs(o.frequency - m.lfoFreq[i]) > .02f * Mathf.Max(.01f, m.lfoFreq[i])) continue;
      double target = m.lfoPhase[i] + m.lfoFreq[i] * elapsed;
      target -= System.Math.Floor(target);
      double err = Wrap(target - o._phase, 1.0);
      if (System.Math.Abs(err) / Mathf.Max(.0001f, o.frequency) > lfoSnapSeconds) {
        double p = o._phase + err; // far off (joining): jump
        o._phase = p - System.Math.Floor(p);
        lfoLeft.Remove(o);
      } else lfoLeft[o] = err;
    }
  }
}
