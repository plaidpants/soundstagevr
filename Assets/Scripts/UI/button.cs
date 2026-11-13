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

public class button : manipObject {
  public bool isToggle = false;
  public bool toggleKey = false;
  public int buttonID;
  public int[] button2DID = new int[] { 0, 0 };
  public bool glowMatOnToggle = true;
  public Material onMat;
  Renderer rend;
  // Materials are shared variants from MaterialCache (2026-11): buttons used to copy
  // their materials, which stopped Unity batching e.g. the sequencer's cells.
  Material offBase, labelBase;
  float glowGain;
  bool highlighted = false;
  bool showingGlow = false;
  public componentInterface _componentInterface;
  public GameObject selectOverlay;
  public float glowHue = 0;
  Color glowColor = Color.HSVToRGB(0, .5f, .25f);

  public bool onlyOn = false;

  bool singleID = true;

  Renderer labelRend;
  public Color labelColor = new Color(0.75f, .75f, 1f);
  public float labelEmission = .4f;
  public float glowEmission = .5f;

  Queue<bool> hits = new Queue<bool>();
  public bool startToggled = false;
  public bool disregardStartToggled = false;

  public bool changeOverlayGlow = false;

  public override void Awake() {
    base.Awake();
    toggleKey = false;
    glowColor = Color.HSVToRGB(glowHue, .5f, .25f);

    if (_componentInterface == null) {
      if (transform.parent) _componentInterface = transform.parent.GetComponent<componentInterface>();
    }

    rend = GetComponent<Renderer>();
    offBase = rend.sharedMaterial;
    glowGain = glowEmission;
    selectOverlay.SetActive(false);

    if (changeOverlayGlow) {
      Renderer overlay = selectOverlay.GetComponent<Renderer>();
      overlay.sharedMaterial = MaterialCache.Glow(overlay.sharedMaterial, glowColor, glowEmission);
    }

    if (GetComponentInChildren<TextMesh>() != null) labelRend = GetComponentInChildren<TextMesh>().transform.GetComponent<Renderer>();
    if (labelRend != null) {
      labelBase = labelRend.sharedMaterial;
      SetLabelGain(.1f);
    }
  }

  Material OffMat() {
    return highlighted ? MaterialCache.Colored(offBase, glowColor) : offBase;
  }

  Material GlowMat() {
    return MaterialCache.Glow(onMat, glowColor, glowGain);
  }

  void ShowGlow(bool on) {
    showingGlow = on;
    rend.sharedMaterial = on ? GlowMat() : OffMat();
  }

  void SetLabelGain(float gain) {
    if (labelRend == null) return;
    labelRend.sharedMaterial = MaterialCache.Glow(labelBase, labelColor, gain);
  }

  void Start() {
    if (disregardStartToggled) return;
    keyHit(startToggled);
  }

  public void Highlight(bool on) {
    highlighted = on;
    glowGain = on ? .9f - .1f : .7f - .1f;
    ShowGlow(showingGlow);
  }

  public void Setup(int IDx, int IDy, bool on, Color c) {
    singleID = false;

    if (_componentInterface == null) {
      if (transform.parent.GetComponent<componentInterface>() != null) _componentInterface = transform.parent.GetComponent<componentInterface>();
      else _componentInterface = transform.parent.parent.GetComponent<componentInterface>();
    }
    button2DID[0] = IDx;
    button2DID[1] = IDy;
    glowColor = c;
    keyHit(on);
    startToggled = on;
    ShowGlow(showingGlow);
  }

  public void setOnAtStart(bool on) {
    keyHit(on);
    startToggled = on;
  }

  public void phantomHit(bool on) {
    hits.Enqueue(on);
  }

  void Update() {
    for (int i = 0; i < hits.Count; i++) {
      bool on = hits.Dequeue();
      isHit = on;
      toggled = on;
      if (on) {
        if (glowMatOnToggle) ShowGlow(true);
        SetLabelGain(labelEmission);
      } else {
        if (glowMatOnToggle) ShowGlow(false);
        SetLabelGain(.1f);

      }
    }
  }

  public bool isHit = false;
  public void keyHit(bool on) {
    isHit = on;
    toggled = on;

    if (on) {
      if (singleID) _componentInterface.hit(on, buttonID);
      else _componentInterface.hit(on, button2DID[0], button2DID[1]);

      if (glowMatOnToggle) ShowGlow(true);
      SetLabelGain(labelEmission);
    } else {
      if (singleID) _componentInterface.hit(on, buttonID);
      else _componentInterface.hit(on, button2DID[0], button2DID[1]);

      if (glowMatOnToggle) ShowGlow(false);
      SetLabelGain(.1f);
    }

  }

  bool toggled = false;

  public override void setState(manipState state) {
    if (curState == manipState.grabbed && state != curState) {
      if (!isToggle) keyHit(false);
      if (!glowMatOnToggle) {
        ShowGlow(false);
      }
    }
    curState = state;
    if (curState == manipState.none) {
      if (!singleID) _componentInterface.onSelect(false, button2DID[0], button2DID[1]);
      selectOverlay.SetActive(false);
    } else if (curState == manipState.selected) {
      if (!singleID) _componentInterface.onSelect(true, button2DID[0], button2DID[1]);
      selectOverlay.SetActive(true);
    } else if (curState == manipState.grabbed) {
      if (!singleID) _componentInterface.onSelect(true, button2DID[0], button2DID[1]);
      if (isToggle) {
        toggled = !toggled;
        if (toggled) keyHit(true);
        else if (!onlyOn) keyHit(false);
      } else keyHit(true);

      if (!glowMatOnToggle) {
        ShowGlow(true);
      }
    }
  }

  public override void onTouch(bool on, manipulator m) {
    if (m != null) {
      if (m.emptyGrab) {
        if (!on) {
          if (!isToggle) keyHit(false);
          if (!glowMatOnToggle) {
            ShowGlow(false);
          }
        } else {
          if (isToggle) {
            toggled = !toggled;
            if (toggled) keyHit(true);
            else if (!onlyOn) keyHit(false);
          } else keyHit(true);

          if (!glowMatOnToggle) {
            ShowGlow(true);
          }
        }
      }
    }
  }
}
