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

// A touchable labelled box: the Scale Sequencer's scale name button and the entries in
// its scale list. The collider and this script are on the root; the box and the label
// are children, so moving the root moves both.
public class scalePickItem : manipObject {
  public System.Action onPick;
  public TextMesh label;
  public Material glowBase, labelBase;
  public Renderer box;   // the visible box (a child, so the label isn't scaled with it)
  bool on = false;

  void Start() {
    Refresh();
  }

  public void SetOn(bool b) {
    on = b;
    Refresh();
  }

  void Refresh() {
    // dark blue box; the current scale a brighter blue; brighter still under the hand
    float gain = curState == manipState.none ? (on ? .3f : .12f) : .45f;
    Color c = Color.HSVToRGB(.62f, .85f, on ? .22f : .12f);
    if (glowBase != null && box != null) box.sharedMaterial = MaterialCache.Glow(glowBase, c, gain);
    if (label != null && labelBase != null) label.GetComponent<Renderer>().sharedMaterial = MaterialCache.Glow(labelBase, Color.white, curState == manipState.none ? .3f : .5f);
  }

  public override void setState(manipState state) {
    bool wasGrabbed = curState == manipState.grabbed;
    curState = state;
    if (state == manipState.grabbed && !wasGrabbed) {
      if (manipulatorObjScript != null) manipulatorObjScript.hapticPulse(500);
      if (onPick != null) onPick();
    }
    if (this != null && gameObject != null) Refresh();
  }
}
