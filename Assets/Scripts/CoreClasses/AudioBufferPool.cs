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

// Scratch-buffer helper for the audio thread (added 2026-11 for Quest).
//
// processBuffer / OnAudioFilterRead used to allocate fresh float[] arrays on every
// audio callback (~86 times a second per device at 512 samples), which fed the GC
// and caused hitches. Each caller now keeps its own cached array in a field and
// asks for it here. The returned array is always zeroed and exactly `length` long,
// so behaviour matches the old `new float[length]`.

public static class AudioBufferPool {
  public static float[] GetCleared(ref float[] cache, int length) {
    if (cache == null || cache.Length != length) {
      cache = new float[length];
    } else {
      System.Array.Clear(cache, 0, length);
    }
    return cache;
  }
}
