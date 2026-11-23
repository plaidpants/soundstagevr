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
  }

  void OnDestroy() {
    if (heightTex != null) Destroy(heightTex);
  }

  // audio thread
  public void Capture(float[] buffer, int channels) {
    if (channels < 1) return;
    long w = written;
    for (int i = 0; i < buffer.Length; i += channels) ring[(int)(w++ & RingMask)] = buffer[i];
    written = w;
  }

  float At(long i) { return ring[(int)(i & RingMask)]; }

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
      for (long i = head - n; i < head; i++) { float v = At(i); if (v < mn) mn = v; if (v > mx) mx = v; }
      if (mx - mn < .01f) { level = 0; hyst = 0; return; } // flat
      level = (mn + mx) * .5f;
      hyst = (mx - mn) * .1f;
      // rising crossings: below level-hyst, then at or above level
      long[] cross = crossBuf;
      int count = 0;
      bool armed = false;
      for (long i = head - n; i < head; i++) {
        float v = At(i);
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

  void Update() {
    if (waveMat == null || scopeShader == null) return;
    if (!DisplayUpdate.Due(waverend, ref lastUpload, 1)) return;

    long head = written;
    if (--analyseIn <= 0) { analyseIn = 6; Analyse(head); }

    int want;
    if (period > 0) want = Mathf.Clamp(period * 3, 128, rate * 2);
    else want = rate * 2; // roll
    // keep the timebase steady: only change it when it is more than 10% off
    if (window == 0 || Mathf.Abs(want - window) > window / 10) window = want;
    int w = (int)System.Math.Min(window, System.Math.Max(0, head - 2));
    if (w < 2) return;

    // start: the latest rising crossing (p < level <= v) that leaves a full window after
    // it, where the signal was below level - hyst in the half period before (so noise
    // riding on the wave doesn't trigger it)
    double start = head - w - 1; // (the newest sample, head - 1, is the last one drawn)
    if (period > 0) {
      long limit = System.Math.Max(head - w - System.Math.Max(period * 2, 4096), head - RingSize + period + 2);
      int back = Mathf.Max(2, period / 2);
      for (long i = head - w - 1; i > limit; i--) {
        float v = At(i), p = At(i - 1);
        if (!(p < level && v >= level)) continue;
        bool dipped = false;
        for (long j = i - 1; j >= i - back; j--) if (At(j) < level - hyst) { dipped = true; break; }
        if (!dipped) continue;
        start = (i - 1) + (level - p) / (v - p); // between the two samples
        break;
      }
    }

    double step = (double)w / Columns;
    for (int c = 0; c < Columns; c++) {
      double pos = start + c * step;
      long i0 = (long)System.Math.Floor(pos);
      float f = (float)(pos - i0);
      float v = At(i0) + (At(i0 + 1) - At(i0)) * f;
      float t = Mathf.Clamp01((v + 1f) * .5f);
      heights[c] = (byte)((Rows - 1) * t);
    }
    heightTex.SetPixelData(heights, 0);
    heightTex.Apply(false);
  }
}
