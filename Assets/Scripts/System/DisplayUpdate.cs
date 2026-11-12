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

// Rate limiting for the CPU-drawn display textures (added 2026-11).
//
// The oscilloscope, spectrum, looper and recorder displays redraw a texture on the
// CPU and upload it with SetPixels32 + Apply. Doing that every frame for every
// device added up fast (a looper/recorder is 2 MB per frame, an oscilloscope 128 KB),
// dropping a ~10-oscillator patch to ~40 FPS on Quest. Displays now upload only
// when visible and at most every N frames.

using UnityEngine;

public static class DisplayUpdate {
  public const int ScopeInterval = 2;     // 36 Hz at 72 fps
  public const int TransportInterval = 4; // 18 Hz: looper/recorder waveforms (scrub head still moves every frame)

  // True if the display should be redrawn/uploaded this frame.
  public static bool Due(Renderer r, ref int lastFrame, int everyNFrames) {
    if (r != null && !r.isVisible) return false;
    int f = Time.frameCount;
    if (f - lastFrame < everyNFrames) return false;
    lastFrame = f;
    return true;
  }
}
