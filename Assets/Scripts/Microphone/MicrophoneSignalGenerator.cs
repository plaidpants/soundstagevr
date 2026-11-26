// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class MicrophoneSignalGenerator : signalGenerator {

  [DllImport("SoundStageNative")]
  public static extern void SetArrayToSingleValue(float[] a, int length, float val);
  [DllImport("SoundStageNative")]
  public static extern void CopyArray(float[] a, float[] b, int length);
  [DllImport("SoundStageNative")]
  public static extern void MicFunction(float[] a, float[] b, int length, float val);

  // Which headset's microphone feeds this device (2026-11-22). In multiplayer every
  // headset has its own copy of each Microphone device, and each copy used to open that
  // headset's microphone - every player's mic was live, all playing into the patch (and,
  // in one room, into each other's speakers: feedback). Now, in a session, a copy listens
  // only while a player on this headset is holding the mic; everyone else's copy is
  // silent. Alone (or hosting with nobody joined) it's live as before. Several mics can be in the scene: they share the
  // headset's one microphone (MicCapture).
  //
  // Two ways in (2026-11-26):
  //  - AAudio, on Android (the Quest and the phone): Plugins/Android/SoundStageMic.cpp
  //    opens the microphone in low-latency mode and this device's processBuffer reads it
  //    straight from its ring buffer on the audio thread - no AudioSource, no 16 kHz
  //    chunks. The default there.
  //  - Unity's Microphone (the PC, the editor, and the fallback): the microphone fills a
  //    1 s looping clip, an AudioSource plays it, its OnAudioFilterRead hands the audio
  //    on through a small FIFO to processBuffer. Fixed 2026-11-25/26: playback is kept
  //    one measured chunk + marginMs behind the microphone (it used to overtake it:
  //    repeats and skips), the FIFO absorbs the two audio callbacks' changing order (a
  //    block was sometimes used twice or not at all), and it opens at the output rate
  //    when the device allows.
  // First run: Microphone.Start before the permission was granted gave a microphone that
  // never delivered, until the app was restarted. Now the permission is asked for first
  // (and logged), and a microphone that delivers nothing in 3 s is closed and reopened.
  //
  // Choosing the input: the MIC button in the settings menu (wirePanelComponentInterface)
  // steps through the AAudio presets, then Unity's devices, and remembers the choice.
  //
  // Debug: "[Microphone]" lines - permission, the input and rates when it opens, then
  // every 5 s while live: buffer fill, under/overruns, drift trim (AAudio) or the gap
  // behind the mic and resyncs (Unity), callback rates and the peak level.

  AudioSource source;
  float[] readBuffer;
  volatile bool activated = false; // mic audio is flowing into this device
  bool capturing;                  // holding a MicCapture reference
  volatile int nativeReader = -1;  // AAudio: this device's reader
  microphoneGrip grip;
  float retryAt;

  public Dictionary<float, float[]> freqBuffers = new Dictionary<float, float[]>();

  public float amp = 1;
  public bool active = true;

  // ---- Unity path: the FIFO between OnAudioFilterRead (in) and processBuffer (out) ----
  readonly object ringLock = new object();
  float[] ring = new float[0];
  int ringWrite, ringFill;
  bool primed;
  public static int primeBlocks = 1, maxBlocks = 6;

  // ---- statistics (written on the audio thread, read and reset every 5 s) ----
  int statIn, statOut, statUnder, statOver, statResync, statUnused, statFillMin = int.MaxValue, statFillMax;
  float statPeak, statGapMin = float.MaxValue, statGapMax, statsAt;
  int lastCallbacks;

  public override void Awake() {
    base.Awake();
    readBuffer = new float[MAX_BUFFER_LENGTH];
  }

  void Start() {
    source = GetComponent<AudioSource>();
    grip = GetComponentInChildren<microphoneGrip>(true);
  }

  bool WantLive() {
    if (!NetSync.Active) return true;
    if (NetPlayer.all.Count <= 1) return true; // (2026-11-26) hosting with nobody joined yet: as if alone
    return grip != null && grip.curState == manipObject.manipState.grabbed;
  }

  void Update() {
    if (source == null) return;
    bool want = WantLive();
    if (capturing) MicCapture.CheckNative(); // an AAudio stream that failed: reopen
    if (capturing && myGeneration != MicCapture.Generation) { StopCapture(); return; } // input switched: reopen
    if (want && !capturing && Time.unscaledTime >= retryAt) StartCapture();
    else if (!want && capturing) StopCapture();
    if (capturing) {
      if (nativeReader < 0) { MicCapture.Poll(); KeepUp(); }
      Stats();
    }
  }

  Coroutine _MicActivateRoutine;
  int myGeneration;
  void StartCapture() {
    if (!MicCapture.PermissionReady()) { retryAt = Time.unscaledTime + .5f; return; }
    if (!MicCapture.Acquire()) { retryAt = Time.unscaledTime + 2f; return; } // no microphone: stay silent, try again later
    capturing = true;
    myGeneration = MicCapture.Generation;
    ResetStats();
    if (MicCapture.Native) {
      source.Stop();
      source.clip = null;
      nativeReader = MicNative.OpenReader(AudioSettings.outputSampleRate, MicCapture.nativeTargetMs);
      lastCallbacks = MicNative.Callbacks();
      activated = true;
      Debug.Log("[Microphone] reading AAudio directly (reader " + nativeReader + ", keeping " + MicCapture.nativeTargetMs + " ms in hand)");
      return;
    }
    lock (ringLock) { ringWrite = 0; ringFill = 0; primed = false; }
    if (_MicActivateRoutine != null) StopCoroutine(_MicActivateRoutine);
    _MicActivateRoutine = StartCoroutine(MicActivateRoutine(MicCapture.Clip));
  }

  void StopCapture() {
    if (_MicActivateRoutine != null) { StopCoroutine(_MicActivateRoutine); _MicActivateRoutine = null; }
    activated = false;
    int r = nativeReader;
    nativeReader = -1;
    if (r >= 0) MicNative.CloseReader(r);
    if (source != null) { source.Stop(); source.clip = null; }
    if (capturing) { capturing = false; MicCapture.Release(myGeneration); }
  }

  static int Wrap(int i, int n) { return ((i % n) + n) % n; }

  IEnumerator MicActivateRoutine(AudioClip clip) {
    source.Stop();
    source.clip = clip;
    source.loop = true;
    // wait (at most 3 s) for the microphone to deliver two chunks, to learn its chunk size
    float t0 = Time.unscaledTime;
    while (MicCapture.Chunks < 2 && Time.unscaledTime - t0 < 3f) { MicCapture.Poll(); yield return null; }
    if (MicCapture.Chunks < 2) {
      Debug.LogWarning("[Microphone] no audio from the microphone in 3 s (position " + MicCapture.Position() +
                       "): closing it, trying again in 2 s");
      _MicActivateRoutine = null;
      StopCapture();
      retryAt = Time.unscaledTime + 2f;
      yield break;
    }
    // start right after a chunk lands
    int seen = MicCapture.Chunks;
    while (MicCapture.Chunks == seen && Time.unscaledTime - t0 < 3.5f) { MicCapture.Poll(); yield return null; }
    source.Play();
    source.timeSamples = Wrap(MicCapture.Position() - MicCapture.LeadSamples, clip.samples);
    Debug.Log("[Microphone] first audio after " + Mathf.RoundToInt((Time.unscaledTime - t0) * 1000) + " ms; chunks of " +
              MicCapture.ChunkMs.ToString("F0") + " ms, playing " + MicCapture.LeadMs.ToString("F0") + " ms behind it at most (" +
              MicCapture.marginMs + " ms at least; FIFO " + primeBlocks + " block) (source: spatialize " + source.spatialize + ", spatialBlend " + source.spatialBlend +
              ", mixer " + (source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "none") + ")");
    _MicActivateRoutine = null;
  }

  // Unity path: keep playback just behind the microphone (their clocks drift apart). The
  // mic position jumps a chunk at a time, so the gap is a sawtooth: largest just after a
  // chunk lands, then shrinking by a chunk. Put it back to one chunk + margin right after
  // a chunk lands when it has drifted long, or at once if it's about to overtake.
  void KeepUp() {
    AudioClip clip = source.clip;
    if (clip == null || !source.isPlaying) return;
    int len = clip.samples, lead = MicCapture.LeadSamples, margin = MicCapture.MarginSamples;
    int gap = Wrap(MicCapture.Position() - source.timeSamples, len);
    if (gap > len / 2) gap -= len; // overtaken: negative
    float gapMs = gap * 1000f / clip.frequency;
    if (gapMs < statGapMin) statGapMin = gapMs;
    if (gapMs > statGapMax) statGapMax = gapMs;
    bool danger = gap < margin / 2;
    bool drifted = MicCapture.JustArrived && gap > lead + Mathf.Max(margin, lead / 2);
    if (danger || drifted) {
      source.timeSamples = Wrap(MicCapture.Position() - lead, len);
      statResync++;
    }
  }

  void ResetStats() {
    statIn = statOut = statUnder = statOver = statResync = statUnused = 0;
    statFillMin = int.MaxValue; statFillMax = 0;
    statPeak = 0; statGapMin = float.MaxValue; statGapMax = 0;
    statsAt = Time.unscaledTime;
  }

  readonly int[] nstats = new int[8];
  void Stats() {
    float dt = Time.unscaledTime - statsAt;
    if (dt < 5f) return;
    if (nativeReader >= 0) {
      MicNative.Stats(nativeReader, nstats);
      int[] info = MicNative.Info();
      float rate = Mathf.Max(1, info[0]);
      int cb = nstats[6] - lastCallbacks; lastCallbacks = nstats[6];
      Debug.Log("[Microphone] last " + dt.ToString("F0") + " s (AAudio): in hand " +
                (nstats[3] < 0 ? "-" : (nstats[3] * 1000f / rate).ToString("F1") + "-" + (nstats[4] * 1000f / rate).ToString("F1")) +
                " ms (aim " + MicCapture.nativeTargetMs + "), reads " + (nstats[0] / dt).ToString("F0") + "/s, callbacks " + (cb / dt).ToString("F0") + "/s, " +
                "underruns " + nstats[1] + ", jumps " + nstats[2] + " (after a pause: button off / not patched), drift trim " + (nstats[5] / 1e4f).ToString("F2") + "%, xruns " + info[6] +
                ", peak " + (nstats[7] / 1000f).ToString("F2") + (nstats[7] >= 990 ? " (CLIPPING)" : ""));
      statsAt = Time.unscaledTime;
      return;
    }
    Debug.Log("[Microphone] last " + dt.ToString("F0") + " s: behind the mic " +
              (statGapMin == float.MaxValue ? "-" : statGapMin.ToString("F0") + "-" + statGapMax.ToString("F0")) + " ms (aim " + MicCapture.marginMs + "-" + MicCapture.LeadMs.ToString("F0") + "), " +
              "resyncs " + statResync + ", in " + (statIn / dt).ToString("F0") + "/s, out " + (statOut / dt).ToString("F0") + "/s, " +
              "FIFO " + (statFillMin == int.MaxValue ? "-" : statFillMin + "-" + statFillMax) + " samples (block " + lastBlock + "), " +
              "underruns " + statUnder + ", overruns " + statOver + (statUnused > 0 ? ", not pulled " + statUnused : "") + ", peak " + statPeak.ToString("F2") +
              (statPeak >= .99f ? " (CLIPPING)" : ""));
    ResetStats();
  }

  void OnDisable() { StopCapture(); }

  // Unity path: the microphone's audio, from the AudioSource playing its clip: into the
  // FIFO, and silenced here (the patch decides where it goes)
  private void OnAudioFilterRead(float[] buffer, int channels) {
    if (!capturing || nativeReader >= 0) return;
    activated = true;
    int n = buffer.Length;
    lock (ringLock) {
      if (ring.Length < n * (maxBlocks + 2)) { ring = new float[n * (maxBlocks + 2)]; ringWrite = 0; ringFill = 0; primed = false; }
      int cap = ring.Length;
      for (int i = 0; i < n; i++) {
        float v = buffer[i];
        ring[ringWrite] = v;
        ringWrite = ringWrite + 1 == cap ? 0 : ringWrite + 1;
        if (v > statPeak) statPeak = v; else if (-v > statPeak) statPeak = -v;
      }
      ringFill += n;
      if (ringFill > cap) { ringFill = cap; statUnused++; } // nothing reading it (not patched in)
      statIn++;
    }
    SetArrayToSingleValue(buffer, n, 0.0f);
  }

  int lastBlock;
  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (!active || !activated || !capturing) {
      return;
    }
    int n = buffer.Length;
    lastBlock = n;
    if (readBuffer.Length < n) readBuffer = new float[n];
    int reader = nativeReader;
    if (reader >= 0) { // AAudio: straight from the microphone
      MicNative.Read(reader, readBuffer, n, channels);
      MicFunction(buffer, readBuffer, n, amp);
      return;
    }
    lock (ringLock) {
      statOut++;
      int cap = ring.Length;
      if (cap == 0) return;
      if (ringFill < statFillMin) statFillMin = ringFill;
      if (ringFill > statFillMax) statFillMax = ringFill;
      if (!primed) {
        if (ringFill < n * primeBlocks) return; // filling up first: silence
        primed = true;
      }
      if (ringFill > n * maxBlocks) { ringFill = n * primeBlocks; statOver++; } // too far behind: drop the oldest
      int count = Mathf.Min(n, ringFill);
      int read = Wrap(ringWrite - ringFill, cap);
      for (int i = 0; i < count; i++) {
        readBuffer[i] = ring[read];
        read = read + 1 == cap ? 0 : read + 1;
      }
      ringFill -= count;
      if (count < n) {
        for (int i = count; i < n; i++) readBuffer[i] = 0;
        statUnder++;
        primed = false; // let it fill up again
      }
    }
    MicFunction(buffer, readBuffer, n, amp);
  }
}

// The AAudio microphone (Plugins/Android/SoundStageMic.cpp, compiled into the Android
// build). Elsewhere these do nothing and Start fails, so the Unity path is used.
public static class MicNative {
#if UNITY_ANDROID && !UNITY_EDITOR
  [DllImport("__Internal")] static extern int SSMic_Start(int preset, int exclusive);
  [DllImport("__Internal")] static extern void SSMic_Stop();
  [DllImport("__Internal")] static extern System.IntPtr SSMic_ErrorText(int r);
  [DllImport("__Internal")] static extern void SSMic_Info(int[] info);
  [DllImport("__Internal")] static extern int SSMic_OpenReader(int outRate, float targetMs);
  [DllImport("__Internal")] static extern void SSMic_CloseReader(int id);
  [DllImport("__Internal")] static extern void SSMic_Read(int id, float[] buffer, int len, int channels);
  [DllImport("__Internal")] static extern void SSMic_Stats(int id, int[] stats);
  static bool linked = true;

  public static bool Available { get { return linked; } }
  public static int Start(int preset, bool exclusive) {
    try { return SSMic_Start(preset, exclusive ? 1 : 0); }
    catch (System.Exception e) { linked = false; Debug.LogWarning("[Microphone] the AAudio plugin isn't in this build (" + e.GetType().Name + ")"); return -1000; }
  }
  public static void Stop() { if (linked) SSMic_Stop(); }
  public static string ErrorText(int r) { return linked ? Marshal.PtrToStringAnsi(SSMic_ErrorText(r)) : "not in this build"; }
  public static int[] Info() { var a = new int[10]; if (linked) SSMic_Info(a); return a; }
  public static int OpenReader(int outRate, float targetMs) { return SSMic_OpenReader(outRate, targetMs); }
  public static void CloseReader(int id) { SSMic_CloseReader(id); }
  public static void Read(int id, float[] buffer, int len, int channels) { SSMic_Read(id, buffer, len, channels); }
  public static void Stats(int id, int[] stats) { SSMic_Stats(id, stats); }
  public static int Callbacks() { return Info()[8]; }
#else
  public static bool Available { get { return false; } }
  public static int Start(int preset, bool exclusive) { return -1000; }
  public static void Stop() { }
  public static string ErrorText(int r) { return "AAudio is Android only"; }
  public static int[] Info() { return new int[10]; }
  public static int OpenReader(int outRate, float targetMs) { return -1; }
  public static void CloseReader(int id) { }
  public static void Read(int id, float[] buffer, int len, int channels) { System.Array.Clear(buffer, 0, len); }
  public static void Stats(int id, int[] stats) { }
  public static int Callbacks() { return 0; }
#endif
}

// The headset's microphone, shared by every Microphone device that is listening: opened
// when the first one starts, closed when the last one stops.
public static class MicCapture {
  static AudioClip clip;
  static string device;
  static bool opened;
  static int users, openGeneration = -1;
  public static bool Native { get; private set; }   // opened through AAudio
  public static AudioClip Clip { get { return clip; } }
  public static float marginMs = 20f;      // Unity path: playback at least this far behind the microphone
  public static float nativeTargetMs = 8f; // AAudio: audio kept in hand against jitter (Unity's audio thread pulls in bursts)
  public static int Generation { get; private set; } // bumped when the input is switched

  // ---- the inputs to choose from: AAudio presets (Android), then Unity's devices ----
  // (2026-11-26 on the Quest 3: only "unprocessed" got AAudio's low-latency path - 5 ms
  // bursts; the others ran 20 ms bursts, "NOT low latency". So it's first, the default.)
  static readonly int[] presets = { 9, 6, 10, 1, 5 };
  static readonly string[] presetNames = { "unprocessed", "voice recognition", "voice performance", "generic", "camcorder" };

  static List<string> Choices() {
    var l = new List<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
    if (MicNative.Available) foreach (int p in presets) l.Add("aaudio:" + p);
#endif
    foreach (string d in Microphone.devices) l.Add("unity:" + d);
    return l;
  }

  static string Choice() {
    var l = Choices();
    if (l.Count == 0) return "";
    string c = PlayerPrefs.GetString("micChoice2", "");
    return l.Contains(c) ? c : l[0];
  }

  static string Label(string c) {
    if (c.StartsWith("aaudio:")) {
      int i = System.Array.IndexOf(presets, int.Parse(c.Substring(7)));
      return "AAudio " + (i >= 0 ? presetNames[i] : c.Substring(7));
    }
    return "Unity " + c.Substring(6);
  }

  public static event System.Action Changed;

  public static void NextInput() {
    var l = Choices();
    if (l.Count == 0) return;
    int i = (l.IndexOf(Choice()) + 1) % l.Count;
    PlayerPrefs.SetString("micChoice2", l[i]);
    PlayerPrefs.Save();
    Generation++; // anything listening reopens on the new input
    Debug.Log("[Microphone] input switched to " + Label(l[i]) + " (" + (i + 1) + " of " + l.Count + ")");
    if (Changed != null) Changed();
  }

  // for the settings menu's button: "MIC: UNPROCESSED", "MIC: UNITY VOICE RECOGNITION", ...
  public static string MenuLabel() {
    var l = Choices();
    if (l.Count == 0) return "MIC: NONE";
    string c = Choice(), s;
    if (c.StartsWith("aaudio:")) {
      int i = System.Array.IndexOf(presets, int.Parse(c.Substring(7)));
      s = i >= 0 ? presetNames[i] : c.Substring(7);
    } else {
      s = c.Substring(6);
#if UNITY_ANDROID && !UNITY_EDITOR
      // the Quest's: "Android audio input", "Android camcorder input", "Android voice recognition input"
      s = s.Replace("Android ", "").Replace(" input", "").Replace("voice recognition", "voice").Replace("audio", "default");
      s = "unity " + s; // (beside the AAudio ones)
#endif
    }
    s = s.ToUpper();
    if (s.Length > 17) s = s.Substring(0, 16) + "."; // (the button fits about 22 characters)
    return "MIC: " + s;
  }

  // an AAudio stream that reported an error (the device went away, ...): reopen everyone
  static int checkedFrame = -1;
  public static void CheckNative() {
    if (!Native || !opened || Time.frameCount == checkedFrame) return;
    checkedFrame = Time.frameCount;
    if (Time.frameCount % 30 != 0) return;
    if (MicNative.Info()[7] != 0) {
      Debug.LogWarning("[Microphone] the AAudio stream stopped with an error: reopening");
      Generation++;
    }
  }

  // ---- Unity path: the microphone's chunks (Poll, once a frame) ----
  static int lastPos, lastFrame = -1, chunkMax, chunkPrev;
  static float chunkWindowAt;
  public static int Chunks { get; private set; }
  public static bool JustArrived { get; private set; }
  static int Freq { get { return clip != null ? clip.frequency : 16000; } }
  public static int MarginSamples { get { return Mathf.Max(1, (int)(Freq * marginMs / 1000f)); } }
  static int ChunkSamples { get { return Mathf.Min(Mathf.Max(chunkMax, chunkPrev), Freq * 15 / 100); } } // at most 150 ms (hitches)
  public static int LeadSamples { get { return ChunkSamples + MarginSamples; } }
  public static float ChunkMs { get { return ChunkSamples * 1000f / Freq; } }
  public static float LeadMs { get { return LeadSamples * 1000f / Freq; } }

  public static void Poll() {
    if (Time.frameCount == lastFrame || clip == null) return;
    lastFrame = Time.frameCount;
    int pos = Position();
    int jump = ((pos - lastPos) % clip.samples + clip.samples) % clip.samples;
    JustArrived = jump > 0 && jump < clip.samples / 2;
    if (JustArrived) {
      if (lastPos >= 0) { Chunks++; if (Chunks >= 2 && jump > chunkMax) chunkMax = jump; } // the first one may be partial
      lastPos = pos;
    } else if (lastPos < 0) lastPos = pos;
    if (Time.unscaledTime - chunkWindowAt > 2f) { chunkPrev = chunkMax; chunkMax = 0; chunkWindowAt = Time.unscaledTime; }
  }

  // ---- permission (Android): ask before opening, so the first run works ----
  static bool asking, granted, never;
  static float askAgainAt;

  public static bool PermissionReady() {
#if UNITY_ANDROID && !UNITY_EDITOR
    if (Permission.HasUserAuthorizedPermission(Permission.Microphone)) {
      if (!granted) { granted = true; Debug.Log("[Microphone] permission: granted"); }
      return true;
    }
    if (asking || never || Time.unscaledTime < askAgainAt) return false;
    asking = true;
    Debug.Log("[Microphone] permission: not granted yet - asking");
    var cb = new PermissionCallbacks();
    cb.PermissionGranted += p => { asking = false; Debug.Log("[Microphone] permission: granted just now"); };
    cb.PermissionDenied += p => { asking = false; askAgainAt = Time.unscaledTime + 30f; Debug.LogWarning("[Microphone] permission: denied (asking again in 30 s if a mic is used)"); };
    cb.PermissionDeniedAndDontAskAgain += p => { asking = false; never = true; Debug.LogWarning("[Microphone] permission: denied for good - allow it in the headset's app permissions"); };
    Permission.RequestUserPermission(Permission.Microphone, cb);
    return false;
#else
    return true;
#endif
  }

  public static bool Acquire() {
    if (opened && openGeneration != Generation) Close(); // input switched
    if (!opened) {
      string choice = Choice();
      AudioSettings.GetDSPBufferSize(out int bufLen, out int numBufs);
      int output = AudioSettings.outputSampleRate;
      if (choice.StartsWith("aaudio:")) {
        int preset = int.Parse(choice.Substring(7));
        int r = MicNative.Start(preset, false); // shared: Meta party / call chat keeps the microphone too
        if (r <= 0 && r != -1000 && preset != 6) { // a preset this device doesn't have: voice recognition, then Unity's
          Debug.LogWarning("[Microphone] " + Label(choice) + " didn't open: " + MicNative.ErrorText(r) + " (" + r + ") - trying voice recognition");
          choice = "aaudio:6";
          r = MicNative.Start(6, false);
        }
        if (r > 0) {
          int[] info = MicNative.Info();
          Native = true; opened = true;
          Debug.Log("[Microphone] " + Label(choice) + " on at " + info[0] + " Hz, " + info[1] + " ch, burst " + info[2] + " (" +
                    (info[2] * 1000f / Mathf.Max(1, info[0])).ToString("F1") + " ms), buffer " + info[3] + ", " +
                    (info[4] == 12 ? "low latency" : info[4] == 11 ? "POWER SAVING" : "NOT low latency (" + info[4] + ")") + ", " +
                    (info[5] == 0 ? "exclusive" : "shared") + " (output " + output + " Hz, DSP buffer " + bufLen + " x " + numBufs + ")");
        } else {
          Debug.LogWarning("[Microphone] " + Label(choice) + " didn't open: " + MicNative.ErrorText(r) + " (" + r + ") - using Unity's microphone");
          choice = "";
        }
      }
      if (!opened) {
        string[] devs = Microphone.devices;
        if (devs.Length == 0) { Debug.LogWarning("[Microphone] no microphone devices"); return false; }
        string want = choice.StartsWith("unity:") ? choice.Substring(6) : devs[devs.Length - 1]; // (the last: "voice recognition" on the Quest)
        device = System.Array.IndexOf(devs, want) >= 0 ? want : devs[0];
        Microphone.GetDeviceCaps(device, out int minF, out int maxF);
        int freq = (minF == 0 && maxF == 0) ? output : Mathf.Clamp(output, minF, maxF); // 0 / 0: any rate
        clip = Microphone.Start(device, true, 1, freq);
        if (clip == null) { Debug.LogWarning("[Microphone] " + device + " didn't open at " + freq + " Hz"); return false; }
        lastPos = -1; Chunks = 0; chunkMax = chunkPrev = 0; chunkWindowAt = Time.unscaledTime; lastFrame = -1;
        Native = false; opened = true;
        Debug.Log("[Microphone] Unity " + device + " on at " + clip.frequency + " Hz (device range " + minF + "-" + maxF +
                  ", output " + output + " Hz, DSP buffer " + bufLen + " x " + numBufs + "); devices: " + string.Join(", ", devs));
      }
      users = 0;
      openGeneration = Generation;
    }
    users++;
    return true;
  }

  public static void Release(int generation) {
    if (generation != openGeneration) return; // from before the input was switched: already closed
    if (users <= 0) return;
    if (--users > 0) return;
    Close();
  }

  static void Close() {
    if (!opened) return;
    if (Native) { MicNative.Stop(); Debug.Log("[Microphone] AAudio off"); }
    else { if (device != null) Microphone.End(device); Debug.Log("[Microphone] " + device + " off"); }
    opened = false; Native = false; clip = null; users = 0;
  }

  public static int Position() { return device != null && clip != null ? Microphone.GetPosition(device) : 0; }
}
