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

public class clipPlayerSimple : clipPlayer {

  public float playbackSpeed = 1;
  public int ID = 0;

  public float amplitude = 1;
  public signalGenerator seqGen;
  // (2026-11-25) The tape Timeline's decks: the sound follows the note through the
  // Timeline's ADSR panel (adsr) - attack and decay when the note starts, sustain while it
  // lasts, release when it ends or the timeline is stopped (then the sample stops). Without
  // a panel: a 5 ms fade in and a 150 ms release. Off for everything else (drums get a
  // short pulse and play the whole sample).
  public bool gateStops = false;
  public adsrInterface adsr;
  float env, envFrom, envT;
  int stage; // 0 idle, 1 attack, 2 decay, 3 sustain, 4 release
  bool gateHigh;

  bool active = false;
  double _lastBuffer = 0;
  float[] lastSeqGen;

  [DllImport("SoundStageNative")]
  public static extern double ClipSignalGeneratorD(float[] buffer, float[] speedBuffer, float[] ampBuffer, float[] seqBuffer, int length, float[] lastSeqGen, int channels, bool speedGen, bool ampGen, bool seqGen, double floatingBufferCount
     , int[] sampleBounds, float playbackSpeed, System.IntPtr clip, int clipChannels, float amplitude, bool playdirection, bool looping, double _sampleDuration, int bufferCount, ref bool active);

  float[] speedBuffer = new float[0];
  float[] ampBuffer = new float[0];

  void Start() {
    lastSeqGen = new float[] { 0, 0 };
  }

  public void Play(float speed = 1) {
    playbackSpeed = speed;
    _lastBuffer = sampleBounds[0];
    active = true;
  }

  public void Stop() {
    active = false;
  }

  // Drums pass their hit velocity to the sample they trigger.
  float TriggerVelocity() {
    drumSignalGenerator drum = seqGen as drumSignalGenerator;
    return drum != null ? drum.velocity : 1f;
  }

  float[] _scratch_seqBuffer; // reused audio-thread scratch (see AudioBufferPool)
  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (!loaded) return;
    floatingBufferCount = _lastBuffer;

    float[] seqBuffer = AudioBufferPool.GetCleared(ref _scratch_seqBuffer, buffer.Length);
    if (seqGen != null) seqGen.Pull(seqBuffer, dspTime, channels);


    floatingBufferCount = ClipSignalGeneratorD(buffer, speedBuffer, ampBuffer, seqBuffer, buffer.Length, lastSeqGen, channels, false, false, seqGen != null, floatingBufferCount, sampleBounds,
   playbackSpeed, m_ClipHandle.AddrOfPinnedObject(), clipChannels, amplitude * TriggerVelocity(), true, false, _sampleDuration, bufferCount, ref active);

    _lastBuffer = floatingBufferCount;

    if (gateStops && seqGen != null) GateRelease(buffer, seqBuffer, channels);
  }

  void GateRelease(float[] buffer, float[] seqBuffer, int channels) {
    float sr = _sampleRate > 0 ? (float)_sampleRate : 48000f;
    float dt = 1f / sr;
    float a = .005f, d = 0f, peak = 1f, sus = 1f, r = .15f;
    if (adsr != null && adsr.durations != null && adsr.volumes != null) {
      a = adsr.durations[0]; d = adsr.durations[1]; r = adsr.durations[2];
      peak = adsr.volumes[0]; sus = adsr.volumes[1];
    }
    a = Mathf.Max(a, .002f); r = Mathf.Max(r, .005f); // never a click
    int n = buffer.Length;
    for (int i = 0; i < n; i += channels) {
      bool high = seqBuffer[i] > 0;
      if (high && !gateHigh) { stage = 1; envFrom = env; envT = 0; }        // note on (the sample restarts)
      else if (!high && gateHigh && stage != 0) { stage = 4; envFrom = env; envT = 0; } // note off
      gateHigh = high;
      envT += dt;
      switch (stage) {
        case 1: env = Mathf.Lerp(envFrom, peak, envT / a); if (envT >= a) { stage = 2; envFrom = env; envT = 0; } break;
        case 2: env = d > 0 ? Mathf.Lerp(envFrom, sus, envT / d) : sus; if (envT >= d) { stage = 3; env = sus; } break;
        case 3: env = sus; break;
        case 4: env = Mathf.Lerp(envFrom, 0f, envT / r); if (envT >= r) { stage = 0; env = 0; } break;
        default: env = 0; break;
      }
      for (int c = 0; c < channels; c++) buffer[i + c] *= env;
    }
    if (stage == 0) active = false; // released: stop the sample
  }
}
