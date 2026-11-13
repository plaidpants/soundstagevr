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
using System.Runtime.InteropServices;

public class speaker : MonoBehaviour {

  public float volume = 1;
  public signalGenerator incoming;
  // Stereo setup (ludzeller's fork, 2026-11): a spatialized speaker only plays the left
  // channel, so "RIGHT ONLY" copies the right channel into it. Two speakers set to
  // left / right play a stereo signal as a stereo pair.
  public bool leftOn = true;

  [DllImport("SoundStageNative")]
  public static extern void MultiplyArrayBySingleValue(float[] buffer, int length, float val);

  private void OnAudioFilterRead(float[] buffer, int channels) {
    if (incoming == null) return;
    long start = System.Diagnostics.Stopwatch.GetTimestamp();
    double dspTime = AudioSettings.dspTime;
    incoming.processBuffer(buffer, dspTime, channels);
    if (!leftOn && channels == 2) {
      for (int n = 0; n < buffer.Length; n += 2) buffer[n] = buffer[n + 1];
    }
    if (volume != 1) MultiplyArrayBySingleValue(buffer, buffer.Length, volume);
    // report this speaker's DSP time to the load monitor (see masterLimiter.cs)
    AudioLoadMonitor.AddSpeakerTicks(System.Diagnostics.Stopwatch.GetTimestamp() - start, dspTime);
  }
}
