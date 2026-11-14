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

// Handle above the grid's far corner: drag up to add higher note rows, down to remove
// them, one row per cube height. Doesn't follow the hand; the device moves instead.
public class gridEdgeHandle : manipObject {
  public scaleSequencerDeviceInterface owner;
  public Material onMat;
  Renderer rend;
  Material offMat;
  float anchor;
  const float step = .04f;
  static readonly Color glowColor = Color.HSVToRGB(0, .5f, .1f);

  public override void Awake() {
    base.Awake();
    rend = GetComponent<Renderer>();
    offMat = rend.sharedMaterial;
  }

  float HandY() { return owner.transform.InverseTransformPoint(manipulatorObj.position).y; }

  public override void grabUpdate(Transform t) {
    if (owner == null || manipulatorObj == null) return;
    float d = HandY() - anchor;
    // after a change the device has moved a row, so the hand is back near the anchor
    if (d > step * .5f) { if (owner.ChangeTop(1)) Pulse(); }
    else if (d < -step * .5f) { if (owner.ChangeTop(-1)) Pulse(); }
  }

  void Pulse() {
    if (manipulatorObjScript != null) manipulatorObjScript.hapticPulse(750);
  }

  public override void setState(manipState state) {
    curState = state;
    if (curState == manipState.none) rend.sharedMaterial = offMat;
    else if (onMat != null) rend.sharedMaterial = MaterialCache.Glow(onMat, glowColor * (curState == manipState.grabbed ? 1f : .25f), .5f);
    if (curState == manipState.grabbed && manipulatorObj != null) anchor = HandY();
  }
}
