// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// The Scope's display (2026-11-22): a triggered oscilloscope with an automatic timebase,
// so it needs no controls.
//  * The audio thread keeps the last ~5 s of the input (left channel) in a ring.
//  * Each frame the display looks at the recent signal: if it repeats (at least three
//    rising crossings of its middle level in the last 1.5 s) it shows about three
//    periods, starting at a rising crossing, so a steady wave stands still - from audio
//    rate down to slow LFOs. Anything that doesn't repeat (a slow sweep, noise, silence)
//    scrolls past instead, the last 2 s across the screen.
//  * Vertically it shows -1..1, like the oscillator's display.
//
// Controls (2026-11-27, scopeDeviceInterface sets these): TIME (AUTO as above, or a fixed
// 0.1 ms to 2 s across the screen), trigger LEVEL (in screen units, marked at the right
// edge), GAIN (x0.25 to x16) and POSITION (slides the picture in time: up to a screen of
// what came before the trigger, or after it - 2026-11-27, was up / down), MODE (AUTO: free
// runs when nothing crosses the level; NORMAL: keeps the last triggered picture) and
// SLOPE (rising / falling edge), marked by a tick at the screen's left edge.
// External trigger: with something in the TRIG jack, the picture locks to crossings of
// that signal (LEVEL in its own -1..1, not the screen's) instead of the input's.
// Drawn with the oscillator's GPU scope shader (Resources/Shaders/WaveScope): 512 column
// heights in a 512x1 texture.

using UnityEngine;

public class scopeViz : MonoBehaviour {
  public Renderer waverend;
  public Color32 waveLine = new Color32(240, 220, 110, 255);
  public Color32 waveBG = Color.black;

  const int Columns = 512;
  const int Rows = 64;
  const int RingSize = 1 << 18; // 262144 samples: ~5 s at 48 kHz
  const int RingMask = RingSize - 1;

  // audio thread -> main thread (tearing is harmless here)
  readonly float[] ring = new float[RingSize];
  long written; // samples written in total

  readonly byte[] heights = new byte[Columns];
  Texture2D heightTex;
  Material waveMat;
  int lastUpload = -100;
  int rate;

  // ---- controls ----
  [System.NonSerialized] public bool timeAuto = true;
  [System.NonSerialized] public float seconds = .01f;    // across the screen, when not auto
  [System.NonSerialized] public float triggerLevel = 0f; // screen units, -1..1
  [System.NonSerialized] public float gain = 1f;
  [System.NonSerialized] public float position = 0f;     // screen units (vertical; no control now)
  [System.NonSerialized] public float shift = 0f;        // screens: + shows what came before the trigger, - after
  [System.NonSerialized] public bool autoMode = true;    // AUTO / NORMAL
  [System.NonSerialized] public bool rising = true;

  Transform marker;
  static Material markerMat;

  // the TRIG input, kept alongside the input (same sample positions); live while plugged
  readonly float[] trigRing = new float[RingSize];
  volatile bool external;

  static Shader scopeShader;
  static readonly int idWaveTex = Shader.PropertyToID("_WaveTex");
  static readonly int idHead = Shader.PropertyToID("_Head");
  static readonly int idLine = Shader.PropertyToID("_LineColor");
  static readonly int idBG = Shader.PropertyToID("_BGColor");
  static readonly int idNoWrap = Shader.PropertyToID("_NoWrap");

  void Awake() {
    rate = AudioSettings.outputSampleRate;
    heightTex = new Texture2D(Columns, 1, TextureFormat.R8, false);
    heightTex.filterMode = FilterMode.Point;
    heightTex.wrapMode = TextureWrapMode.Repeat;
    byte mid = (byte)((Rows - 1) / 2);
    for (int i = 0; i < Columns; i++) heights[i] = mid;
    heightTex.SetPixelData(heights, 0);
    heightTex.Apply(false);
  }

  void Start() {
    if (scopeShader == null) scopeShader = Resources.Load<Shader>("Shaders/WaveScope");
    if (waverend == null) return;
    waveMat = waverend.material;
    if (scopeShader == null) { Debug.LogError("[scopeViz] Resources/Shaders/WaveScope shader not found"); return; }
    waveMat.shader = scopeShader;
    waveMat.SetTexture(idWaveTex, heightTex);
    waveMat.SetFloat("_MaxBright", ColumnDisplay.maxBright);
    waveMat.SetFloat(idHead, 0);
    waveMat.SetFloat(idNoWrap, 1); // a sweep, not a ring: no segment from the last column back to the first
    waveMat.SetColor(idLine, waveLine);
    waveMat.SetColor(idBG, waveBG);
    BuildMarker();
  }

  // the trigger level: a short tick at the screen's left edge
  void BuildMarker() {
    Transform d = waverend.transform;
    Vector3 ls = d.lossyScale;
    float sx = Mathf.Max(1e-5f, Mathf.Abs(ls.x)), sy = Mathf.Max(1e-5f, Mathf.Abs(ls.y));
    if (markerMat == null) {
      Shader sh = Shader.Find("Sprites/Default");
      if (sh == null) sh = Shader.Find("Unlit/Transparent");
      markerMat = new Material(sh) { color = new Color(1f, .6f, .2f, .9f) };
    }
    // trigger level: a short tick at the screen's edge
    GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
    Destroy(q.GetComponent<Collider>());
    q.name = "triggerMarker";
    q.transform.SetParent(d, false);
    q.transform.localScale = new Vector3(.006f / sx, .0012f / sy, 1);
    q.transform.localPosition = new Vector3(-.5f + .003f / sx, 0, -.025f); // (-x: your left)
    MeshRenderer qr = q.GetComponent<MeshRenderer>();
    qr.sharedMaterial = markerMat;
    qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    marker = q.transform;
  }

  void OnDestroy() {
    if (heightTex != null) Destroy(heightTex);
  }

  // audio thread
  public void Capture(float[] buffer, int channels, float[] trig = null) {
    if (channels < 1) return;
    long w = written;
    if (trig != null) for (int i = 0, k = 0; i < buffer.Length; i += channels, k++) trigRing[(int)((w + k) & RingMask)] = trig[i];
    for (int i = 0; i < buffer.Length; i += channels) ring[(int)(w++ & RingMask)] = buffer[i];
    external = trig != null;
    written = w;
  }

  float At(long i) { return ring[(int)(i & RingMask)]; }
  // what the trigger and the timebase look at: the TRIG input when plugged, else the input
  float Src(long i) { return external ? trigRing[(int)(i & RingMask)] : ring[(int)(i & RingMask)]; }

  // ---- analysis (main thread) ----
  float level, hyst;      // trigger level (middle of the signal) and hysteresis
  int period;             // samples, 0 = doesn't repeat
  int window;             // samples shown
  int analyseIn;          // frames until the period is measured again
  readonly long[] crossBuf = new long[17];
  readonly int[] diffBuf = new int[16];

  void Analyse(long head) {
    int[] spans = { rate / 4, rate * 3 / 2 }; // short first (cheap); longer for slow LFOs
    period = 0;
    foreach (int span in spans) {
      int n = (int)System.Math.Min(span, System.Math.Min(head, (long)RingSize / 2));
      if (n < 64) return;
      float mn = float.MaxValue, mx = float.MinValue;
      for (long i = head - n; i < head; i++) { float v = Src(i); if (v < mn) mn = v; if (v > mx) mx = v; }
      if (mx - mn < .01f) { level = 0; hyst = 0; return; } // flat
      level = (mn + mx) * .5f;
      hyst = (mx - mn) * .1f;
      // rising crossings: below level-hyst, then at or above level
      long[] cross = crossBuf;
      int count = 0;
      bool armed = false;
      for (long i = head - n; i < head; i++) {
        float v = Src(i);
        if (v < level - hyst) armed = true;
        else if (armed && v >= level) {
          armed = false;
          cross[count % cross.Length] = i;
          count++;
        }
      }
      if (count >= 3) {
        int k = Mathf.Min(count, cross.Length);
        int[] d = k - 1 == diffBuf.Length ? diffBuf : new int[k - 1];
        for (int j = 0; j < k - 1; j++) {
          long a = cross[(count - k + j) % cross.Length], b = cross[(count - k + j + 1) % cross.Length];
          d[j] = (int)(b - a);
        }
        System.Array.Sort(d);
        period = d[d.Length / 2];
        return;
      }
    }
  }

  bool haveTrace;

  static byte Height(float v) { return (byte)((Rows - 1) * Mathf.Clamp01((v + 1f) * .5f)); }

  void Update() {
    if (waveMat == null || scopeShader == null) return;
    if (marker != null) { Vector3 mp = marker.localPosition; mp.y = Mathf.Clamp(triggerLevel, -1f, 1f) * .5f; marker.localPosition = mp; }
    if (!DisplayUpdate.Due(waverend, ref lastUpload, 1)) return;

    long head = written;
    if (--analyseIn <= 0) { analyseIn = 6; Analyse(head); }

    int want;
    if (!timeAuto) want = Mathf.Max(4, Mathf.RoundToInt(seconds * rate));
    else if (period > 0) want = Mathf.Clamp(period * 3, 128, rate * 2);
    else want = rate * 2; // roll
    // keep the timebase steady: only change it when it is more than 10% off
    if (window == 0 || Mathf.Abs(want - window) > window / 10 || !timeAuto) window = want;
    int w = (int)System.Math.Min(window, System.Math.Max(0, head - 2));
    w = System.Math.Min(w, RingSize / 2);
    if (w < 2) return;

    // the trigger level in signal units (the dial is in screen units: undo gain and position)
    float g = Mathf.Max(1e-4f, gain);
    bool ext = external;
    float L = ext ? triggerLevel : (triggerLevel - position) / g; // (TRIG: in its own units)
    float h = ext ? Mathf.Max(hyst, .01f) : Mathf.Max(hyst, .01f / g);

    // start: the latest crossing of L in the chosen direction that leaves a full window
    // after it, where the signal was on the other side of L (by the hysteresis) in the
    // stretch before it, so noise riding on the wave doesn't trigger it
    double start = head - w - 1; // (the newest sample, head - 1, is the last one drawn)
    bool found = false;
    {
      // One pass forward over the stretch before the window (2026-11-27: it used to walk
      // back from every crossing looking for the dip before it, which on a long TIME
      // setting with a signal that never dipped below the level scanned millions of
      // samples a frame and froze the headset for seconds). Armed once the signal is on
      // the far side of L (by the hysteresis); the last armed crossing wins.
      int span = period > 0 ? period : 4096;
      int searchLen = Mathf.Clamp(span * 2, 4096, rate / 2); // at most half a second back
      // the trigger lands shift screens into the picture, so it may be that much later
      // (before-trigger view) or must be that much earlier (after-trigger view)
      long sh = (long)(Mathf.Clamp(shift, -1f, 1f) * w);
      long to = head - w - 1 + sh;
      long from = System.Math.Max(to - searchLen, head - RingSize + w + 2);
      bool armed = false;
      long hit = -1;
      for (long i = from + 1; i <= to; i++) {
        float v = Src(i), p = Src(i - 1);
        if (rising ? p < L - h : p > L + h) armed = true;
        if (armed && (rising ? (p < L && v >= L) : (p > L && v <= L))) { hit = i; armed = false; }
      }
      if (hit >= 0) {
        float v = Src(hit), p = Src(hit - 1);
        start = (hit - 1) + (L - p) / (v - p) - sh; // between the two samples, slid by shift
        found = true;
      }
    }
    if (!found && !autoMode && haveTrace) return; // NORMAL: keep the last triggered picture

    double step = (double)w / Columns;
    if (step <= 2) {
      // zoomed in: each column is a point on the wave, between two samples
      for (int c = 0; c < Columns; c++) {
        double pos = start + c * step;
        long i0 = (long)System.Math.Floor(pos);
        float f = (float)(pos - i0);
        heights[c] = Height((At(i0) + (At(i0 + 1) - At(i0)) * f) * g + position);
      }
    } else {
      // Peak detect (2026-11-27): more samples than columns. Taking one sample per column
      // aliased: a note near a multiple of the column rate showed up as a big slow sine
      // that wasn't there. Each column now covers its own stretch of samples and the
      // trace alternates between their highest and lowest, so it fills the band the
      // signal really covers (a note shows as its envelope).
      for (int c = 0; c < Columns; c++) {
        long a0 = (long)System.Math.Floor(start + c * step);
        long a1 = System.Math.Max(a0 + 1, (long)System.Math.Floor(start + (c + 1) * step));
        float mn = float.MaxValue, mx = float.MinValue;
        for (long i = a0; i < a1; i++) { float v = At(i); if (v < mn) mn = v; if (v > mx) mx = v; }
        heights[c] = Height(((c & 1) == 0 ? mx : mn) * g + position);
      }
    }
    heightTex.SetPixelData(heights, 0);
    heightTex.Apply(false);
    haveTrace = found || autoMode;
  }
}
