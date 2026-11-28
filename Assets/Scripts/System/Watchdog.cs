// Watchdog (2026-11-27): notices when the main thread stops making frames, and says where
// it stopped. Two headsets (a Quest 3 and a Quest 2) came back from sleep with sound still
// playing but no picture, the main thread busy and nothing in the log - by the time anyone
// looked, the log had been overwritten. So:
//  - the main thread marks each step of its frame (Update, LateUpdate, before render, end
//    of frame) and code that runs at awkward moments marks what it's doing (Mark: pausing,
//    resuming, the host handover, the microphone opening and closing...)
//  - a background thread checks once a second. When the app should be running (Android
//    says the activity is resumed) but no frame has started for 5 s, it logs "[Watchdog]
//    main thread stuck" with the step it stopped in, the recent marks and the last log
//    lines, then again every 30 s, and "recovered" if frames come back
//  - the same report goes to hang.txt in the app's files folder. If the app is closed
//    while stuck, the next start logs it and keeps it as hang_prev.txt:
//      adb pull /sdcard/Android/data/com.HardLightLabsLLC.SoundStage/files/hang_prev.txt
//    (a stall that recovers - a long load - is kept as stall_last.txt instead)
// Android tells us about pause and resume on its own thread (ActivityLifecycleCallbacks),
// so this works when Unity's main thread never hears about the resume. Costs a few
// timestamps a frame.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

[DefaultExecutionOrder(-32000)]
public class Watchdog : MonoBehaviour {
  public const double StuckAfter = 5, ReportEvery = 30;

  static readonly Stopwatch clock = Stopwatch.StartNew();
  static long Ticks { get { return clock.Elapsed.Ticks; } }
  static double Secs(long ticks) { return ticks / (double)TimeSpan.TicksPerSecond; }

  // the main thread's progress (written there, read by the watch thread)
  static int frame;
  static long frameStart, lateStart, beforeRender, frameEnd;
  static volatile bool unityPaused, unityFocused = true, runInBackground = true;
  // Android's view, from its own thread
  static volatile bool androidResumed = true, lifecycleHooked;
  static long resumedAt;

  // ---- marks ----
  struct Entry { public long ticks; public int frame; public string what; }
  static readonly Entry[] marks = new Entry[48];
  static int markNext;
  static readonly object gate = new object();

  // remember a step, quietly (cheap: call it from anything that might block)
  public static void Mark(string what) {
    lock (gate) { marks[markNext % marks.Length] = new Entry { ticks = Ticks, frame = Volatile.Read(ref frame), what = what }; markNext++; }
  }
  // remember it and log it
  public static void Note(string what) {
    Mark(what);
    Debug.Log("[Watchdog] " + what + " (frame " + Volatile.Read(ref frame) + ")");
  }

  // ---- the last log lines (logcat on the headset wraps within minutes) ----
  static readonly string[] logs = new string[40];
  static int logNext;
  static void OnLog(string msg, string stack, LogType type) {
    if (msg == null) return;
    if (msg.Length > 300) msg = msg.Substring(0, 300) + "...";
    string line = Secs(Ticks).ToString("F1") + " " + (type == LogType.Log ? "" : type + ": ") + msg;
    lock (gate) { logs[logNext % logs.Length] = line; logNext++; }
  }

  static string dir, hangPath;
  static Thread watcher;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Boot() {
#if UNITY_EDITOR
    return; // breakpoints and the pause button stop the main thread on purpose
#else
    if (watcher != null) return;
    runInBackground = Application.runInBackground;
    Application.logMessageReceivedThreaded += OnLog;
    try {
      dir = Application.persistentDataPath;
      hangPath = Path.Combine(dir, "hang.txt");
      if (File.Exists(hangPath)) {
        string last = File.ReadAllText(hangPath);
        Debug.LogWarning("[Watchdog] the last run was closed while its main thread was stuck:\n" + last);
        File.Copy(hangPath, Path.Combine(dir, "hang_prev.txt"), true);
        File.Delete(hangPath);
      }
    } catch (Exception e) { Debug.LogWarning("[Watchdog] hang file: " + e.Message); }
    var go = new GameObject("Watchdog");
    go.hideFlags = HideFlags.HideInHierarchy;
    DontDestroyOnLoad(go);
    go.AddComponent<Watchdog>();
    HookLifecycle();
    Application.onBeforeRender += () => Interlocked.Exchange(ref beforeRender, Ticks);
    watcher = new Thread(Watch) { IsBackground = true, Name = "Watchdog", Priority = System.Threading.ThreadPriority.BelowNormal };
    watcher.Start();
    Debug.Log("[Watchdog] watching the main thread (reports after " + StuckAfter + " s without a frame" +
      (lifecycleHooked ? "" : "; no Android lifecycle, going by Unity's pause") + ")");
#endif
  }

  void Start() {
    StartCoroutine(EndOfFrames());
    try {
      OVRManager.HMDMounted += () => Note("headset on");
      OVRManager.HMDUnmounted += () => Note("headset off");
      OVRManager.InputFocusAcquired += () => Note("input focus back");
      OVRManager.InputFocusLost += () => Note("input focus lost (system menu?)");
      OVRManager.VrFocusAcquired += () => Note("VR focus back");
      OVRManager.VrFocusLost += () => Note("VR focus lost");
    } catch (Exception e) { Debug.LogWarning("[Watchdog] OVRManager events: " + e.Message); }
  }

  void Update() {
    Interlocked.Increment(ref frame);
    Interlocked.Exchange(ref frameStart, Ticks);
  }
  void LateUpdate() { Interlocked.Exchange(ref lateStart, Ticks); }
  System.Collections.IEnumerator EndOfFrames() {
    var wait = new WaitForEndOfFrame();
    while (true) { yield return wait; Interlocked.Exchange(ref frameEnd, Ticks); }
  }

  void OnApplicationPause(bool paused) {
    unityPaused = paused;
    Note(paused ? "Unity: pausing" : "Unity: resuming (" + (Secs(Ticks - Interlocked.Read(ref resumedAt))).ToString("F1") + " s after Android resumed)");
  }
  void OnApplicationFocus(bool focused) {
    unityFocused = focused;
    Note(focused ? "Unity: focus back" : "Unity: focus lost");
  }

  // ---- Android's own pause and resume ----
#if UNITY_ANDROID && !UNITY_EDITOR
  static Lifecycle lifecycle;
  static void HookLifecycle() {
    try {
      lifecycle = new Lifecycle();
      using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
      using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
      using (var app = activity.Call<AndroidJavaObject>("getApplication"))
        app.Call("registerActivityLifecycleCallbacks", lifecycle);
      lifecycleHooked = true;
    } catch (Exception e) { Debug.LogWarning("[Watchdog] can't follow the Android lifecycle: " + e.Message); }
  }

  // Runs on Android's UI thread: only timestamps and marks here, no Unity objects.
  // (Every method of the interface, the Pre/Post ones included, so none is missing.)
  class Lifecycle : AndroidJavaProxy {
    public Lifecycle() : base("android.app.Application$ActivityLifecycleCallbacks") { }
    public void onActivityResumed(AndroidJavaObject a) {
      Interlocked.Exchange(ref resumedAt, Ticks);
      androidResumed = true;
      Note("Android: resumed");
    }
    public void onActivityPaused(AndroidJavaObject a) { androidResumed = false; Note("Android: paused"); }
    public void onActivityStopped(AndroidJavaObject a) { Mark("Android: stopped"); }
    public void onActivityStarted(AndroidJavaObject a) { Mark("Android: started"); }
    public void onActivityDestroyed(AndroidJavaObject a) { Note("Android: destroyed"); }
    public void onActivityCreated(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivitySaveInstanceState(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivityPreCreated(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivityPostCreated(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivityPreSaveInstanceState(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivityPostSaveInstanceState(AndroidJavaObject a, AndroidJavaObject b) { }
    public void onActivityPreStarted(AndroidJavaObject a) { }
    public void onActivityPostStarted(AndroidJavaObject a) { }
    public void onActivityPreResumed(AndroidJavaObject a) { }
    public void onActivityPostResumed(AndroidJavaObject a) { }
    public void onActivityPrePaused(AndroidJavaObject a) { }
    public void onActivityPostPaused(AndroidJavaObject a) { }
    public void onActivityPreStopped(AndroidJavaObject a) { }
    public void onActivityPostStopped(AndroidJavaObject a) { }
    public void onActivityPreDestroyed(AndroidJavaObject a) { }
    public void onActivityPostDestroyed(AndroidJavaObject a) { }
  }
#else
  static void HookLifecycle() { }
#endif

  // should frames be coming?
  static bool ShouldRun { get { return lifecycleHooked ? androidResumed : !unityPaused && (runInBackground || unityFocused); } }

  // ---- the watch thread ----
  static void Watch() {
    int seen = -1;
    long seenAt = Ticks;
    bool stuck = false;
    double lastReport = 0;
    string lastText = null;
    while (true) {
      Thread.Sleep(1000);
      try {
        int f = Volatile.Read(ref frame);
        long now = Ticks;
        if (f != seen) {
          if (stuck) {
            double gone = Secs(now - seenAt);
            Debug.LogWarning("[Watchdog] main thread recovered after " + gone.ToString("F1") + " s");
            try {
              File.WriteAllText(Path.Combine(dir, "stall_last.txt"), lastText + "\nrecovered after " + gone.ToString("F1") + " s\n");
              File.Delete(hangPath);
            } catch { }
          }
          seen = f; seenAt = now; stuck = false;
          continue;
        }
        if (!ShouldRun) { seenAt = now; continue; } // paused: no frames expected
        long since = Math.Max(seenAt, Interlocked.Read(ref resumedAt));
        double still = Secs(now - since);
        if (still < StuckAfter) continue;
        if (stuck && still - lastReport < ReportEvery) continue;
        lastText = Report(still, f);
        Debug.LogError(lastText);
        try { File.WriteAllText(hangPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + lastText + "\n"); } catch { }
        stuck = true; lastReport = still;
      } catch (Exception e) {
        try { Debug.LogWarning("[Watchdog] " + e.Message); } catch { }
      }
    }
  }

  static string Report(double still, int f) {
    long now = Ticks;
    long fs = Interlocked.Read(ref frameStart), ls = Interlocked.Read(ref lateStart);
    long br = Interlocked.Read(ref beforeRender), fe = Interlocked.Read(ref frameEnd);
    // which step of the frame came last
    string where;
    long latest = Math.Max(Math.Max(fs, ls), Math.Max(br, fe));
    if (latest == fs) where = "in Update, a coroutine or an Invoke (started the frame, never reached LateUpdate)";
    else if (latest == ls) where = "in LateUpdate or Unity's work before rendering";
    else if (latest == br) where = "rendering or handing the frame to the headset (XR)";
    else where = "between frames (waiting for the headset's next frame, input, physics, or a pause / focus handler)";
    var sb = new StringBuilder();
    sb.Append("[Watchdog] main thread stuck: no frame for ").Append(still.ToString("F1")).Append(" s, frame ").Append(f)
      .Append(", stopped ").Append(where).Append('\n');
    sb.Append("  Android ").Append(lifecycleHooked ? (androidResumed ? "resumed" : "paused") : "(not followed)")
      .Append(", resumed ").Append(Secs(now - Interlocked.Read(ref resumedAt)).ToString("F1")).Append(" s ago")
      .Append("; Unity ").Append(unityPaused ? "paused" : "running").Append(unityFocused ? ", focused" : ", not focused").Append('\n');
    sb.Append("  last frame: Update ").Append(Ago(now, fs)).Append(", LateUpdate ").Append(Ago(now, ls))
      .Append(", before render ").Append(Ago(now, br)).Append(", end of frame ").Append(Ago(now, fe)).Append('\n');
    lock (gate) {
      sb.Append("  marks (latest last):\n");
      int n = Math.Min(markNext, marks.Length);
      for (int i = markNext - n; i < markNext; i++) {
        Entry e = marks[i % marks.Length];
        sb.Append("    ").Append(Ago(now, e.ticks)).Append(" f").Append(e.frame).Append(' ').Append(e.what).Append('\n');
      }
      sb.Append("  log (latest last):\n");
      n = Math.Min(logNext, logs.Length);
      for (int i = logNext - n; i < logNext; i++) sb.Append("    ").Append(logs[i % logs.Length]).Append('\n');
    }
    return sb.ToString();
  }

  static string Ago(long now, long t) { return t == 0 ? "never" : Secs(now - t).ToString("F1") + " s ago"; }
}
