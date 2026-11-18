// Logs frames that take too long (added 2026-11-18), so a freeze shows up in Player.log
// with what was happening just before it. Over the internet a frame of 10 s or more
// makes the relay drop us ("player timed out due to inactivity"), so long frames matter.

using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class HitchLog : MonoBehaviour {
  const float LogOver = 0.5f;   // seconds
  const int MaxLogs = 40;       // per run, so a bad run can't flood the log

  static readonly Queue<string> recent = new Queue<string>();
  static int mainThread;
  float lastFrame;
  int logged;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Boot() {
    mainThread = Thread.CurrentThread.ManagedThreadId;
    Application.logMessageReceivedThreaded += Remember;
    GameObject g = new GameObject("HitchLog");
    g.hideFlags = HideFlags.HideInHierarchy;
    DontDestroyOnLoad(g);
    g.AddComponent<HitchLog>();
  }

  // the last few lines logged on the main thread: the likely culprit is among them
  static void Remember(string msg, string stack, LogType type) {
    if (Thread.CurrentThread.ManagedThreadId != mainThread || msg.StartsWith("[Hitch]")) return;
    if (msg.Length > 120) msg = msg.Substring(0, 120);
    lock (recent) {
      recent.Enqueue(msg);
      while (recent.Count > 3) recent.Dequeue();
    }
  }

  void Update() {
    float now = Time.realtimeSinceStartup;
    float dt = now - lastFrame;
    lastFrame = now;
    if (dt < LogOver || Time.frameCount < 3 || logged >= MaxLogs) return;
    logged++;
    string before;
    lock (recent) before = string.Join(" | ", recent.ToArray());
    Debug.Log("[Hitch] a frame took " + Mathf.RoundToInt(dt * 1000) + " ms at " + now.ToString("F1") + " s; logged just before: " + before);
  }
}
