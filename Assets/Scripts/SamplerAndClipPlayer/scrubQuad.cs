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

public class scrubQuad : MonoBehaviour {
  Dictionary<manipulator, scrubber> manips = new Dictionary<manipulator, scrubber>();
  public Transform scrubBar;
  public Transform scrubIndicator;
  clipPlayerComplex player;

  scrubber scrubberActive = null;
  scrubber scrubberCandidate = null;
  Vector2 scrubrange = new Vector2(.2f, -.2f);
  class scrubber {
    public Transform trans { get; set; }
    public bool trigger { get; set; }
    public bool colliding { get; set; }

    public scrubber(Transform t, bool g, bool c) {
      trans = t;
      trigger = g;
      colliding = c;
    }
  }

  void Awake() {
    player = transform.parent.GetComponent<clipPlayerComplex>();
    Color c = Color.HSVToRGB(298 / 400f, 84 / 255f, .01f);
    scrubIndicator.GetComponent<Renderer>().material.SetColor("_TintColor", c);
  }

  void OnCollisionEnter(Collision coll) {
    manipulator m = coll.transform.GetComponent<manipulator>();
    if (m == null) return;
    if (manips.ContainsKey(m)) manips[m].colliding = true;
    else manips[m] = new scrubber(m.transform, m.triggerDown, true);
    m.hapticPulse();
    {
      scrubberCandidate = manips[m];
      scrubIndicator.gameObject.SetActive(true);
    }
  }


  void OnCollisionExit(Collision coll) {
    manipulator m = coll.transform.GetComponent<manipulator>();
    if (m == null) return;
    if (manips.ContainsKey(m)) {
      manips[m].colliding = false;
      if (scrubberCandidate == manips[m]) {
        scrubberCandidate = null;
        scrubIndicator.gameObject.SetActive(false);
      }
    }
  }


  // Letting go must clear scrubberActive: left set, Update kept moving the bar to the hand
  // after release, fighting clipPlayerComplex (which puts it back at the play position);
  // whichever ran last each frame won, so after a reload the bar could stay stuck to the
  // hand. Only the hand doing the scrubbing can end it (the other hand's trigger used to).
  void updateScrubbers(manipulator m) {
    scrubber s = manips[m];
    if (s.trigger && s.colliding && m.emptyGrab) {
      scrubberCandidate = scrubberActive = s;
      player.grabScrub(true);
    } else if (scrubberActive == s || scrubberActive == null) {
      scrubberActive = null;
      player.grabScrub(false);
    }
  }

  void Update() {
    List<manipulator> gone = null;
    foreach (manipulator m in new List<manipulator>(manips.Keys)) {
      if (m == null) { (gone ??= new List<manipulator>()).Add(m); continue; }
      if (manips[m].trigger != m.triggerDown) {
        manips[m].trigger = m.triggerDown;
        updateScrubbers(m);
      }
    }
    if (gone != null) foreach (manipulator m in gone) {
      if (scrubberActive == manips[m]) { scrubberActive = null; player.grabScrub(false); }
      if (scrubberCandidate == manips[m]) scrubberCandidate = null;
      manips.Remove(m);
    }

    if (scrubberCandidate != null) {
      Vector3 pos = transform.parent.InverseTransformPoint(scrubberCandidate.trans.position);
      Vector3 posB = scrubIndicator.localPosition;
      posB.x = Mathf.Clamp(pos.x, .2f - .4f * player.trackBounds.y, .2f - .4f * player.trackBounds.x);
      scrubIndicator.localPosition = posB;
    }

    if (scrubberActive != null) {
      Vector3 pos = transform.parent.InverseTransformPoint(scrubberActive.trans.position);
      Vector3 posB = scrubBar.localPosition;
      posB.x = Mathf.Clamp(pos.x, .2f - .4f * player.trackBounds.y, .2f - .4f * player.trackBounds.x);
      scrubBar.localPosition = posB;
    }
  }
}
