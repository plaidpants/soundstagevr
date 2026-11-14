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

// Interface sounds (menu open/close/select/grab, trash, tape and library previews,
// "no sample loaded" clicks) can be turned off from the settings panel so a
// performance only carries the instruments (added 2026-11). PlayerPrefs "uiSounds".

using UnityEngine;

public static class UISound {
  static int cached = -1;

  public static bool Enabled {
    get {
      if (cached < 0) cached = PlayerPrefs.GetInt("uiSounds", 1);
      return cached == 1;
    }
    set {
      cached = value ? 1 : 0;
      PlayerPrefs.SetInt("uiSounds", cached);
    }
  }

  public static void Play(AudioSource source, AudioClip clip, float volume = 1f) {
    if (!Enabled || source == null || clip == null) return;
    source.PlayOneShot(clip, volume);
  }
}
