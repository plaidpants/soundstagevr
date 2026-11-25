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
// Multiplayer join / leave chimes go through here too, so the same switch mutes them.

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

  // ---- multiplayer chimes: a rising two-note chime when someone joins, falling when
  // someone leaves. Made in code (no asset), played 2D so it's heard wherever you are.
  static AudioSource chimeSource;
  static AudioClip joinClip, leaveClip;

  public static void PlayerJoined() { Chime(true); }
  public static void PlayerLeft() { Chime(false); }

  static void Chime(bool up) {
    if (!Enabled) return;
    if (chimeSource == null) {
      GameObject g = new GameObject("UISoundChimes");
      Object.DontDestroyOnLoad(g);
      chimeSource = g.AddComponent<AudioSource>();
      chimeSource.spatialBlend = 0f;
      chimeSource.playOnAwake = false;
    }
    if (joinClip == null) joinClip = MakeChime("PlayerJoined", 659.26f, 987.77f); // E5 -> B5
    if (leaveClip == null) leaveClip = MakeChime("PlayerLeft", 987.77f, 659.26f); // B5 -> E5
    chimeSource.PlayOneShot(up ? joinClip : leaveClip, .3f);
  }

  static AudioClip MakeChime(string name, float f1, float f2) {
    int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
    float noteLen = .22f, gap = .09f;
    int n = (int)(rate * (gap + noteLen));
    float[] d = new float[n];
    AddNote(d, rate, 0, noteLen, f1);
    AddNote(d, rate, (int)(gap * rate), noteLen, f2);
    AudioClip c = AudioClip.Create(name, n, 1, rate, false);
    c.SetData(d, 0);
    return c;
  }

  // soft bell: sine plus a quiet octave, 4 ms attack, exponential decay
  static void AddNote(float[] d, int rate, int start, float len, float f) {
    int n = Mathf.Min((int)(len * rate), d.Length - start);
    float attack = .004f * rate;
    for (int i = 0; i < n; i++) {
      float t = (float)i / rate;
      float env = Mathf.Min(1f, i / attack) * Mathf.Exp(-t * 14f);
      float s = Mathf.Sin(2f * Mathf.PI * f * t) + .25f * Mathf.Sin(4f * Mathf.PI * f * t);
      d[start + i] += .5f * env * s;
    }
  }
}
