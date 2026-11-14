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

// Distance detail for devices (2026-11). Seen from across the stage every label, jack,
// knob cap and LED is still its own draw call although it covers a few pixels; with
// ~30 devices in view that was ~1300 draw calls and ~36 fps. One frame after a device
// appears this gives it a LODGroup:
//   LOD0  everything
//   LOD1  only the big parts (housing, panels, keys...) once the device is small on
//         screen (farDetailScreenHeight of the view height)
//   culled when it's tiny (cullScreenHeight)
// "Small" = text, or a renderer whose largest side is under smallPartFraction of the
// device's largest side. Renderers created later (keys added by resizing, timeline
// notes) aren't in the group and always draw. Parts already under a LODGroup (tapes)
// are left alone. Quality lodBias (0.7 on Quest) scales these thresholds.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DeviceLOD : MonoBehaviour {
  public static float farDetailScreenHeight = 0.07f;
  public static float cullScreenHeight = 0.004f;
  public static float smallPartFraction = 0.12f;
  public static bool enabledGlobally = true;

  IEnumerator Start() {
    yield return null; // let the device spawn its keys/parts (and menu previews strip scripts)
    if (!enabledGlobally || this == null) yield break;
    Build();
  }

  void Build() {
    if (GetComponent<LODGroup>() != null) return;

    // renderers that already belong to a LODGroup further down (e.g. tapes) stay out
    HashSet<Renderer> owned = new HashSet<Renderer>();
    foreach (LODGroup lg in GetComponentsInChildren<LODGroup>(true)) {
      foreach (LOD l in lg.GetLODs()) foreach (Renderer r in l.renderers) if (r != null) owned.Add(r);
    }

    Renderer[] all = GetComponentsInChildren<Renderer>(true);
    if (all.Length < 8) return; // small device: not worth it

    Bounds total = new Bounds();
    bool have = false;
    List<Renderer> mine = new List<Renderer>();
    foreach (Renderer r in all) {
      if (r == null || owned.Contains(r)) continue;
      if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
      mine.Add(r);
      if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
      if (have) total.Encapsulate(r.bounds); else { total = r.bounds; have = true; }
    }
    if (!have || mine.Count < 8) return;

    float devSize = Mathf.Max(total.size.x, total.size.y, total.size.z);
    float small = devSize * smallPartFraction;
    List<Renderer> big = new List<Renderer>();
    foreach (Renderer r in mine) {
      if (r.GetComponent<TextMesh>() != null) continue;
      Vector3 s = r.bounds.size;
      // inactive parts report empty bounds; keep them (they're often panels shown later)
      if (s == Vector3.zero || Mathf.Max(s.x, s.y, s.z) >= small) big.Add(r);
    }
    if (big.Count == mine.Count) return;

    LODGroup g = gameObject.AddComponent<LODGroup>();
    g.fadeMode = LODFadeMode.None;
    g.SetLODs(new LOD[] {
      new LOD(farDetailScreenHeight, mine.ToArray()),
      new LOD(cullScreenHeight, big.ToArray()),
    });
    g.RecalculateBounds();
  }
}
