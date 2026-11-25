// Where the main thread's time goes (2026-11-24, temporary diagnosis).
//
// Recording into a piano roll got slower and slower as notes were added (72 fps down to
// 40, main thread 14 -> 26 ms) while playing the same notes back cost nothing. The usual
// suspects didn't explain it, so the scripts involved time themselves and every 5 s,
// while anything is slow, one line says how much each took per frame:
//   [Perf] 5 s, 44 fps (main 22.7 ms): scripts 9.8 ms (timelineEvent.Update 6.1 ms x 43, ...),
//          audio roll 0.3 ms/callback, rest (physics, rendering, engine) 12.9 ms
// "rest" is the frame minus all script Update/LateUpdate time measured between the first
// and the last script to run.

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class PerfProbe {
  public static bool enabled = true;
  static readonly double msPerTick = 1000.0 / Stopwatch.Frequency;

  class Acc { public long ticks; public int calls; }
  static readonly Dictionary<string, Acc> acc = new Dictionary<string, Acc>();
  // audio thread (timelinePlayer.timelineEventUpdate)
  static long audioTicks; static int audioCalls;

  public static long Start() { return enabled ? Stopwatch.GetTimestamp() : 0; }

  public static void Add(string id, long start) {
    if (!enabled || start == 0) return;
    long d = Stopwatch.GetTimestamp() - start;
    if (!acc.TryGetValue(id, out Acc a)) { a = new Acc(); acc[id] = a; }
    a.ticks += d; a.calls++;
  }

  public static void AddAudio(long start) {
    if (!enabled || start == 0) return;
    audioTicks += Stopwatch.GetTimestamp() - start; audioCalls++;
  }

  // ---- frame phases ----
  static long frameScriptsStart, scriptsTicks, fixedTicks, fixedStart;
  static int frames, fixedSteps;
  static float windowStart = -1, frameTime;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    // development builds and the Editor only (2026-11-25): store / release builds skip it
    enabled = Debug.isDebugBuild;
    if (!enabled) return;
    GameObject g = new GameObject("PerfProbe");
    Object.DontDestroyOnLoad(g);
    g.AddComponent<PerfProbeFirst>();
    g.AddComponent<PerfProbeLast>();
  }

  internal static void FirstUpdate() {
    if (!enabled) return;
    frameScriptsStart = Stopwatch.GetTimestamp();
    frames++;
    frameTime += Time.unscaledDeltaTime;
  }
  internal static void LastLateUpdate() {
    if (!enabled || frameScriptsStart == 0) return;
    scriptsTicks += Stopwatch.GetTimestamp() - frameScriptsStart;
    if (windowStart < 0) windowStart = Time.unscaledTime;
    if (Time.unscaledTime - windowStart >= 5f) Report();
  }
  internal static void FirstFixed() { if (enabled) { fixedStart = Stopwatch.GetTimestamp(); fixedSteps++; } }
  internal static void LastFixed() { if (enabled && fixedStart != 0) fixedTicks += Stopwatch.GetTimestamp() - fixedStart; }

  static void Report() {
    float secs = Time.unscaledTime - windowStart;
    double frameMs = frames > 0 ? frameTime * 1000.0 / frames : 0;
    // only when it's slow (or a roll is busy) - quiet otherwise
    if (frames > 0 && frameMs > 15.0) {
      var parts = new List<KeyValuePair<string, Acc>>(acc);
      parts.Sort((x, y) => y.Value.ticks.CompareTo(x.Value.ticks));
      var sb = new System.Text.StringBuilder();
      double measured = 0;
      foreach (var kv in parts) measured += kv.Value.ticks * msPerTick / frames;
      double scripts = scriptsTicks * msPerTick / frames;
      sb.Append("[Perf] ").Append(secs.ToString("F0")).Append(" s, ").Append((frames / secs).ToString("F0")).Append(" fps (frame ")
        .Append(frameMs.ToString("F1")).Append(" ms): all scripts ").Append(scripts.ToString("F1")).Append(" ms, of which probed ")
        .Append(measured.ToString("F1")).Append(" ms (");
      int n = 0;
      foreach (var kv in parts) {
        if (n++ >= 8) break;
        if (n > 1) sb.Append(", ");
        sb.Append(kv.Key).Append(' ').Append((kv.Value.ticks * msPerTick / frames).ToString("F2")).Append(" ms x")
          .Append((kv.Value.calls / (float)frames).ToString("F0"));
      }
      sb.Append("); fixed-step scripts ").Append((fixedTicks * msPerTick / frames).ToString("F1")).Append(" ms, ")
        .Append((fixedSteps / (float)frames).ToString("F1")).Append(" steps/frame");
      sb.Append("; audio roll ").Append(audioCalls > 0 ? (audioTicks * msPerTick / audioCalls).ToString("F2") + " ms/callback" : "-");
      sb.Append("; rest (physics, rendering, engine) ").Append((frameMs - scripts).ToString("F1")).Append(" ms");
      Debug.Log(sb.ToString());
    }
    acc.Clear(); audioTicks = 0; audioCalls = 0;
    scriptsTicks = 0; fixedTicks = 0; frames = 0; fixedSteps = 0; frameTime = 0;
    windowStart = Time.unscaledTime;
  }
}
