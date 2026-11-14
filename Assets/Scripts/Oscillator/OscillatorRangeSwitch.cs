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

// LFO dial range switch for the oscillator (2026-11).
//   LINEAR (default, switch up): the original dial, 0-8 Hz in a straight line, so
//                                existing patches play as they always did.
//   SLOW   (switch down):        the curved dial from ludzeller's fork, 0.008-10 Hz
//                                (about 2 minutes to 0.1 s per cycle).
// Only affects LFO mode; the audio-rate dial is unchanged.
// Normally part of the Oscillator prefab (editor menu SoundStage > Add LFO range switch
// to oscillator, then positioned by hand); prefabs without one get a copy of the Gain
// device's switch at runtime.

using UnityEngine;

public static class OscillatorRangeSwitch {
  public static Vector3 localPos = new Vector3(0.0555f, 0.035f, 0.0268f);
  public static readonly Quaternion localRot = new Quaternion(-0.5f, -0.5f, -0.5f, 0.5f); // same as the LFO switch
  public const float scale = 0.6f;

  public static basicSwitch Create(Transform device) {
    GameObject gain = Resources.Load("Prefabs/Gain") as GameObject;
    Transform src = gain != null ? gain.transform.Find("switch") : null;
    if (src == null) {
      Debug.LogWarning("[OscillatorRangeSwitch] Gain prefab switch not found");
      return null;
    }
    GameObject g = Object.Instantiate(src.gameObject, device, false);
    g.name = "lfoRangeSwitch";
    g.transform.localPosition = localPos;
    g.transform.localRotation = localRot;
    g.transform.localScale = Vector3.one * scale;
    basicSwitch sw = g.GetComponent<basicSwitch>();
    Label(sw);
    return sw;
  }

  public static void Label(basicSwitch sw) {
    sw.redOption = false;
    SetLabel(sw.onLabel, "LINEAR");
    SetLabel(sw.offLabel, "SLOW");
  }

  static void SetLabel(Transform t, string s) {
    if (t == null) return;
    TextMesh tm = t.GetComponent<TextMesh>();
    if (tm != null) tm.text = s;
  }
}
