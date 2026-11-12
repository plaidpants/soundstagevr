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

// Master output limiter + audio load monitor (added 2026-11).
//
// Large patches could sound garbled. Mixers simply add their inputs (AddArrays), and
// nothing limited the final mix, so loud combinations hard-clipped at full scale.
// This component sits on the AudioListener, where OnAudioFilterRead sees the final
// mix of every speaker, and applies a transparent peak limiter:
//   * below the threshold (-1 dBFS) the signal is untouched
//   * peaks above it get instant gain reduction with a smooth release
//   * a soft-clip stage catches anything left over
//
// It also logs, every few seconds and only when something happened:
//   * how much limiting was applied (shows whether clipping was the problem)
//   * how much of each audio buffer's time budget the speakers' DSP used
//     (shows whether the audio thread is overloaded, which causes crackle)
// Look for "[masterLimiter]" in logcat.

using UnityEngine;
using System.Diagnostics;

public static class AudioLoadMonitor {
  // Written on the audio thread by speaker.OnAudioFilterRead, read on the main thread.
  public static long ticksThisBlock;
  public static double lastBlockDspTime = -1;
  public static volatile float maxLoad;     // worst fraction of the buffer budget used
  public static volatile float sumLoad;
  public static volatile int blocks;

  // Called by each speaker at the end of its callback with the ticks it used.
  public static void AddSpeakerTicks(long ticks, double dspTime) {
    if (dspTime != lastBlockDspTime) {
      lastBlockDspTime = dspTime;
      ticksThisBlock = 0;
    }
    ticksThisBlock += ticks;
  }
}

public class masterLimiter : MonoBehaviour {
  public static masterLimiter instance;

  public bool limiterEnabled = true;
  [Range(0.5f, 1f)] public float threshold = 0.891f;   // -1 dBFS
  public float releaseSeconds = 0.15f;

  float gain = 1f;
  float releaseCoef;
  int sampleRate;

  // stats (audio thread -> main thread)
  volatile float minGain = 1f;
  volatile int clippedSamples;
  float logTimer;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (instance != null) return;
    AudioListener listener = FindObjectOfType<AudioListener>();
    if (listener == null) {
      UnityEngine.Debug.LogWarning("[masterLimiter] no AudioListener found; limiter not installed");
      return;
    }
    listener.gameObject.AddComponent<masterLimiter>();
  }

  void Awake() {
    instance = this;
    sampleRate = AudioSettings.outputSampleRate;
    releaseCoef = Mathf.Exp(-1f / (releaseSeconds * sampleRate));
  }

  void OnAudioFilterRead(float[] data, int channels) {
    // Audio-thread load: speakers report their DSP time for this block.
    double budgetSec = (double)(data.Length / channels) / sampleRate;
    float load = (float)((double)AudioLoadMonitor.ticksThisBlock / Stopwatch.Frequency / budgetSec);
    AudioLoadMonitor.ticksThisBlock = 0;
    if (load > AudioLoadMonitor.maxLoad) AudioLoadMonitor.maxLoad = load;
    AudioLoadMonitor.sumLoad += load;
    AudioLoadMonitor.blocks++;

    if (!limiterEnabled) return;

    float g = gain, rc = releaseCoef, th = threshold, mg = minGain;
    int clipped = 0;
    for (int i = 0; i < data.Length; i += channels) {
      // frame peak across channels
      float peak = 0f;
      for (int c = 0; c < channels; c++) {
        float a = data[i + c]; if (a < 0) a = -a;
        if (a > peak) peak = a;
      }
      // target gain keeps this frame's peak at the threshold; attack is instant,
      // release eases back toward unity
      float target = (peak * g > th) ? th / peak : 1f;
      if (target < g) g = target;
      else g = target + (g - target) * rc;
      if (g < mg) mg = g;

      for (int c = 0; c < channels; c++) {
        float x = data[i + c] * g;
        // soft-clip safety stage above 0.95 (normally never reached)
        float ax = x < 0 ? -x : x;
        if (ax > 0.95f) {
          clipped++;
          float over = (ax - 0.95f) / 0.05f;
          float y = 0.95f + 0.05f * (over / (1f + over)); // asymptotically approaches 1.0
          x = x < 0 ? -y : y;
        }
        data[i + c] = x;
      }
    }
    gain = g;
    minGain = mg;
    clippedSamples += clipped;
  }

  void Update() {
    logTimer += Time.unscaledDeltaTime;
    if (logTimer < 5f) return;
    logTimer = 0;

    int blocks = AudioLoadMonitor.blocks;
    float avgLoad = blocks > 0 ? AudioLoadMonitor.sumLoad / blocks : 0f;
    float maxLoad = AudioLoadMonitor.maxLoad;
    float reductionDb = 20f * Mathf.Log10(Mathf.Max(minGain, 1e-4f));
    int clipped = clippedSamples;

    // only log when there's something worth knowing
    if (reductionDb < -0.5f || clipped > 0 || maxLoad > 0.5f) {
      UnityEngine.Debug.Log(string.Format(
        "[masterLimiter] last 5s: max gain reduction {0:F1} dB, soft-clipped samples {1}, audio DSP load avg {2:P0} max {3:P0}",
        reductionDb, clipped, avgLoad, maxLoad));
    }

    minGain = 1f; clippedSamples = 0;
    AudioLoadMonitor.maxLoad = 0f; AudioLoadMonitor.sumLoad = 0f; AudioLoadMonitor.blocks = 0;
  }
}
