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

public class clipPlayerComplex : clipPlayer {

  public float playbackSpeed = 1;
  public float amplitude = 1;
  public float headTrim = 0.5f;
  public float tailTrim = 0.5f;

  public samplerDeviceInterface _sampleInterface;

  public Transform scrubTransform;
  public GameObject[] scrubIndicators;
  Vector2 scrubRange = new Vector2(.2f, -.2f);

  public signalGenerator speedGen, ampGen, seqGen;
  public signalGenerator headGen, tailGen;
  public float speedRange = 1;

  public bool active = true;

  float _lastBuffer = 0;
  float[] lastSeqGen;

  public Renderer waverend;
  public Color32 waveBG = Color.black;
  public Color32 waveLine = Color.white;
  int wavewidth = 512;
  int waveheight = 64;

  float[] speedBuffer;
  float[] ampBuffer;
  float[] seqBuffer;
  float[] headBuffer;
  float[] tailBuffer;

  [DllImport("SoundStageNative")]
  public static extern float ClipSignalGenerator(float[] buffer, float[] speedBuffer, float[] ampBuffer, float[] seqBuffer, int length, float[] lastSeqGen, int channels, bool speedGen, bool ampGen, bool seqGen, float floatingBufferCount
, int[] sampleBounds, float playbackSpeed, System.IntPtr clip, int clipChannels, float amplitude, bool playdirection, bool looping, double _sampleDuration, int bufferCount, ref bool active);

  public override void Awake() {
    base.Awake();
    speedBuffer = new float[MAX_BUFFER_LENGTH];
    ampBuffer = new float[MAX_BUFFER_LENGTH];
    seqBuffer = new float[MAX_BUFFER_LENGTH];
    headBuffer = new float[MAX_BUFFER_LENGTH];
    tailBuffer = new float[MAX_BUFFER_LENGTH];
  }

  void Start() {
    lastSeqGen = new float[] { 0, 0 };
    if (!loaded) toggleWaveDisplay(false);
  }

  public override menuItem.deviceType queryDeviceType() {
    return menuItem.deviceType.Sampler;
  }

  public override void toggleWaveDisplay(bool on) {
    if (_waveDisplayAnimation != null) StopCoroutine(_waveDisplayAnimation);
    _waveDisplayAnimation = StartCoroutine(waveDisplayAnimation(on));
  }

  Coroutine _waveDisplayAnimation;
  IEnumerator waveDisplayAnimation(bool on) {
    if (on) {

      waverend.gameObject.SetActive(on);
    } else {
      scrubTransform.gameObject.SetActive(on);
      for (int i = 0; i < scrubIndicators.Length; i++) scrubIndicators[i].SetActive(on);
    }

    if (on) {
      float timer = 0;
      while (timer < 1) {
        timer = Mathf.Clamp01(timer + Time.deltaTime * 12);
        waverend.material.SetFloat("_EmissionGain", Mathf.Lerp(0, 1f, timer));
        yield return null;
      }
      timer = 0;
      while (timer < 1) {
        timer = Mathf.Clamp01(timer + Time.deltaTime * 3);
        waverend.material.SetFloat("_EmissionGain", Mathf.Lerp(1, .25f, timer));
        yield return null;
      }
    } else {
      float timer = 0;
      while (timer < 1) {
        timer = Mathf.Clamp01(timer + Time.deltaTime * 5);
        waverend.material.SetFloat("_EmissionGain", Mathf.Lerp(.25f, 0, timer));
        yield return null;
      }
    }

    if (!on) {
      waverend.gameObject.SetActive(on);
    } else {
      scrubTransform.gameObject.SetActive(on);
      for (int i = 0; i < scrubIndicators.Length; i++) scrubIndicators[i].SetActive(on);
    }
  }

  public float headOffset = 0f;
  public float tailOffset = 0f;
  public void updateTrackBounds() {
    if (!loaded) return;
    sampleBounds[0] = (int)((clipSamples.Length / clipChannels - 1) * Mathf.Clamp01( (headGen == null) ? trackBounds.x : headOffset ));
    sampleBounds[1] = (int)((clipSamples.Length / clipChannels - 1) * Mathf.Clamp01( (tailGen == null) ? trackBounds.y : tailOffset ));
    if (sampleBounds[0] >= sampleBounds[1]) sampleBounds[0] = sampleBounds[1] - 10; // sanity check
  }

  // Waveform drawn by the GPU like the recorder / looper (2026-11): a column texture of
  // per-column peaks (ColumnDisplay + Resources/Shaders/WaveColumns) instead of a
  // 512x64 RGBA texture built on the CPU from one sample per column, which aliased.
  ColumnDisplay display;

  public override void DrawClipTex() {
    if (display == null) display = new ColumnDisplay(waverend, wavewidth, waveheight, waveLine, waveLine, waveBG, 0);
    int frames = clipSamples.Length / clipChannels;
    int perColumn = Mathf.Max(1, Mathf.CeilToInt((float)frames / wavewidth));
    for (int c = 0; c < wavewidth; c++) {
      int start = c * perColumn;
      if (start >= frames) { display.SetColumn(c, 0); continue; }
      int end = Mathf.Min(start + perColumn, frames);
      float peak = 0;
      for (int f = start; f < end; f++) {
        for (int ch = 0; ch < clipChannels; ch++) {
          float v = Mathf.Abs(clipSamples[f * clipChannels + ch]);
          if (v > peak) peak = v;
        }
      }
      display.SetColumn(c, Mathf.FloorToInt((waveheight - 1) * .5f * Mathf.Clamp01(peak)));
    }
    display.Upload();
  }

  public bool looping = true;
  public bool playdirection = true;
  public void Play() {
    _lastBuffer = sampleBounds[0];
    active = true;
  }

  public void Back() {
    _lastBuffer = playdirection ? sampleBounds[0] : sampleBounds[1];
  }

  public void togglePause(bool on) {
    active = on;
  }

  public void Loop() {
    active = true;
    looping = true;
  }

  float lastScrubTarg = 0;
  public void grabScrub(bool on) {
    scrubGrabbed = on;
    float targ = Mathf.InverseLerp(scrubRange.x, scrubRange.y, scrubTransform.localPosition.x);
    lastScrubTarg = scrubTarg = (int)((clipSamples.Length / clipChannels - 1) * targ);
  }

  public void updateTurntableDelta(float d) {
    if (!scrubGrabbed) turntableDelta += d;
  }

  public bool turntableGrabbed = false;
  public float turntableDelta = 0; //seconds scrubbed

  public bool scrubGrabbed = false;
  float samplePos = 0;
  float scrubTarg = 0;

  float cumScrubAmount = 0;
  void Update() {
    if (!scrubGrabbed) {
      Vector3 pos = scrubTransform.localPosition;
      pos.x = Mathf.Lerp(scrubRange.x, scrubRange.y, samplePos);
      scrubTransform.localPosition = pos;


    } else if (scrubGrabbed) {
      float targ = Mathf.InverseLerp(scrubRange.x, scrubRange.y, scrubTransform.localPosition.x);
      scrubTarg = (int)((clipSamples.Length / clipChannels - 1) * targ);
      cumScrubAmount += (scrubTarg - lastScrubTarg);
      lastScrubTarg = scrubTarg;

    }
  }

  public float getScrubAmount() {
    float s = cumScrubAmount * (float)_sampleDuration;
    cumScrubAmount = 0;
    return s;
  }

  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (!loaded) return;
    floatingBufferCount = _lastBuffer;

    if (speedBuffer.Length != buffer.Length)
      System.Array.Resize(ref speedBuffer, buffer.Length);
    if (ampBuffer.Length != buffer.Length)
      System.Array.Resize(ref ampBuffer, buffer.Length);
    if (seqBuffer.Length != buffer.Length)
      System.Array.Resize(ref seqBuffer, buffer.Length);

    if (headBuffer.Length != buffer.Length)
      System.Array.Resize(ref headBuffer, buffer.Length);
    if (tailBuffer.Length != buffer.Length)
      System.Array.Resize(ref tailBuffer, buffer.Length);


    if (speedGen != null) speedGen.processBuffer(speedBuffer, dspTime, channels);
    if (ampGen != null) ampGen.processBuffer(ampBuffer, dspTime, channels);
    if (seqGen != null) seqGen.processBuffer(seqBuffer, dspTime, channels);

    if (headGen != null)
    {
      headGen.processBuffer(headBuffer, dspTime, channels);
      headOffset = (headBuffer[0] + 1) * 0.5f * headTrim; // -1,1 mapping 
    } else {
      headOffset = 0f;
    }

    if (tailGen != null)
    {
      tailGen.processBuffer(tailBuffer, dspTime, channels);
      tailOffset = 1 - (tailBuffer[0] + 1) * 0.5f * tailTrim; // inverted -1,1 mapping
    }
    else
    {
      tailOffset = 0f;
    }

    updateTrackBounds(); // might be overkill!

    if (!scrubGrabbed && !turntableGrabbed) {
      bool curActive = active;
      floatingBufferCount = ClipSignalGenerator(buffer, speedBuffer, ampBuffer, seqBuffer, buffer.Length, lastSeqGen, channels, speedGen != null, ampGen != null, seqGen != null, floatingBufferCount, sampleBounds,
          playbackSpeed, m_ClipHandle.AddrOfPinnedObject(), clipChannels, amplitude, playdirection, looping, _sampleDuration, bufferCount, ref active);
      if (curActive != active) _sampleInterface.playEvent(active);

      lp_filter[0] = buffer[buffer.Length - 2];
      lp_filter[1] = buffer[buffer.Length - 1];
    } else if (scrubGrabbed) // keeping scrub non-native because such an edge-case and maxes out at 2 instances
      {
      float amount = (scrubTarg - _lastBuffer) / (buffer.Length / channels);

      for (int i = 0; i < buffer.Length; i += channels) {
        float scrubPos = floatingBufferCount;
        bufferCount = Mathf.RoundToInt(floatingBufferCount);
        floatingBufferCount += amount;

        float endAmplitude = amplitude;
        if (ampGen != null) endAmplitude = endAmplitude * ((ampBuffer[i] + 1) / 2f);
        ScrubSample(buffer, i, scrubPos, amount, endAmplitude);

        dspTime += _sampleDuration;
      }
    } else {
      float amount = turntableDelta * (float)_sampleRate * channels / buffer.Length;
      for (int i = 0; i < buffer.Length; i += channels) {
        float scrubPos = floatingBufferCount;
        bufferCount = Mathf.RoundToInt(floatingBufferCount);
        floatingBufferCount += amount;

        if (bufferCount > sampleBounds[1]) floatingBufferCount = bufferCount = sampleBounds[0];

        else if (bufferCount < sampleBounds[0]) floatingBufferCount = bufferCount = sampleBounds[1];

        float endAmplitude = amplitude;
        if (ampGen != null) endAmplitude = endAmplitude * ((ampBuffer[i] + 1) / 2f);

        ScrubSample(buffer, i, scrubPos, amount, endAmplitude);

        dspTime += _sampleDuration;
      }
      turntableDelta = 0;

    }

    _lastBuffer = floatingBufferCount;
    samplePos = (float)floatingBufferCount * clipChannels / clipSamples.Length;
  }

  float[] lp_filter = new float[] { 0, 0 };

  // Scrub / turntable output (2026-11). It used to take the nearest sample and run it
  // through a fixed one-pole low-pass (~700 Hz), so even scrubbing at normal speed sounded
  // muffled. Now it interpolates between samples and only smooths when moving slowly
  // (where nearest-sample stepping buzzes); at about normal speed it is unfiltered.
  void ScrubSample(float[] buffer, int i, float pos, float speed, float amp) {
    float a = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(Mathf.Abs(speed)));
    int frames = clipSamples.Length / clipChannels;
    int i0 = Mathf.Clamp(Mathf.FloorToInt(pos), 0, frames - 1);
    int i1 = Mathf.Min(i0 + 1, frames - 1);
    float f = Mathf.Clamp01(pos - Mathf.Floor(pos));

    float l = Mathf.Lerp(clipSamples[i0 * clipChannels], clipSamples[i1 * clipChannels], f) * amp;
    buffer[i] = lp_filter[0] = lp_filter[0] + a * (l - lp_filter[0]);
    if (clipChannels == 2) {
      float r = Mathf.Lerp(clipSamples[i0 * clipChannels + 1], clipSamples[i1 * clipChannels + 1], f) * amp;
      buffer[i + 1] = lp_filter[1] = lp_filter[1] + a * (r - lp_filter[1]);
    } else buffer[i + 1] = buffer[i];
  }
}