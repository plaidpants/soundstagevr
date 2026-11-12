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

public class wirePanelComponentInterface : componentInterface {
  public pauseMenu rootMenu;
  public menuManager menuMgr;
  public slider glowSlider;

  public UIpanel[] panels;
  int curSelect = 0;

  public uiPanelSinglePress handlepanel, jackpanel, midipanel;
  uiPanelSinglePress passthroughpanel; // cloned from midipanel at runtime (buttonID 6)
  uiPanelSinglePress glowqualitypanel; // cloned from midipanel at runtime (buttonID 7)
  uiPanelSinglePress brightnesspanel; // cloned from midipanel at runtime (buttonID 8)

  Color colorGreen = Color.HSVToRGB(.4f, 230f / 255, 118f / 255);
  Color colorRed = Color.HSVToRGB(0f, 230f / 255, 118f / 255);

  void Start() {
    midipanel.newColor(colorGreen);
    jackpanel.newColor(colorGreen);
    handlepanel.newColor(colorGreen);

    glowSlider.setPercent(masterControl.instance.glowVal);
    for (int i = 0; i < panels.Length; i++) {
      panels[i].keyHit(i == curSelect);
    }

    CreatePassthroughButton();
    CreateGlowQualityButton();
    CreateBrightnessButton();
    if (PlayerPrefs.GetInt("midiOut") == 1) {
      string s = "DISABLE MIDI OUT";
      midipanel.label.text = s;
      midipanel.newColor(Color.HSVToRGB(0f, 230f / 255, 118f / 255));
    }

  }

  // Adds a "passthrough" toggle under the MIDI OUT button by cloning it, so the
  // MenuRoot prefab doesn't need editing. Clicks arrive in hit() with ID 6.
  void CreatePassthroughButton() {
    if (passthroughpanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = "passthroughPanel";
    g.transform.localPosition = midipanel.transform.localPosition + new Vector3(0, -0.038f, 0);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    passthroughpanel = g.GetComponent<uiPanelSinglePress>();
    passthroughpanel.buttonID = 6;
    UpdatePassthroughLabel();
  }

  void CreateGlowQualityButton() {
    if (glowqualitypanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = "glowQualityPanel";
    g.transform.localPosition = midipanel.transform.localPosition + new Vector3(0, -0.076f, 0);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    glowqualitypanel = g.GetComponent<uiPanelSinglePress>();
    glowqualitypanel.buttonID = 7;
    UpdateGlowQualityLabel();
  }

  void CreateBrightnessButton() {
    if (brightnesspanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = "brightnessPanel";
    g.transform.localPosition = midipanel.transform.localPosition + new Vector3(0, -0.114f, 0);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    brightnesspanel = g.GetComponent<uiPanelSinglePress>();
    brightnesspanel.buttonID = 8;
    UpdateBrightnessLabel();
  }

  void UpdateBrightnessLabel() {
    if (brightnesspanel == null) return;
    int pct = Mathf.RoundToInt(masterControl.emissionLevels[masterControl.instance.emissionLevelIndex] * 100);
    brightnesspanel.label.text = "GLOW BRIGHTNESS: " + pct + "%";
    brightnesspanel.newColor(colorGreen);
  }

  void UpdateGlowQualityLabel() {
    if (glowqualitypanel == null) return;
    bool high = masterControl.instance.glowQuality == 2;
    glowqualitypanel.label.text = high ? "GLOW QUALITY: HIGH" : "GLOW QUALITY: LOW";
    glowqualitypanel.newColor(colorGreen);
  }

  void UpdatePassthroughLabel() {
    if (passthroughpanel == null) return;
    bool on = passthroughControl.instance != null && passthroughControl.instance.isOn;
    passthroughpanel.label.text = on ? "DISABLE PASSTHROUGH" : "ENABLE PASSTHROUGH";
    passthroughpanel.newColor(on ? colorRed : colorGreen);
  }

  void Update() {
    if (glowSlider.percent != masterControl.instance.glowVal) {
      masterControl.instance.setGlowLevel(glowSlider.percent);
    }
  }

  public override void hit(bool on, int ID = -1) {
    if (!on) return;
    if (ID == -2) //okay
    {
      rootMenu.cancelFileMenu(); //not right
      transform.gameObject.SetActive(false);
    } else if (ID == 3) {
      bool b = !masterControl.instance.handlesEnabled;
      masterControl.instance.toggleHandles(b);
      string s = b ? "ENABLE POS LOCK" : "DISABLE POS LOCK";
      handlepanel.label.text = s;
      handlepanel.newColor(b ? colorGreen : colorRed);
    } else if (ID == 4) {
      bool b = !masterControl.instance.jacksEnabled;
      masterControl.instance.toggleJacks(b);
      string s = b ? "ENABLE JACK LOCK" : "DISABLE JACK LOCK";
      jackpanel.label.text = s;
      jackpanel.newColor(b ? colorGreen : colorRed);
    } else if (ID == 5) {
      bool b = !menuMgr.midiOutEnabled;
      menuMgr.toggleMidiOut(b);
      string s = b ? "DISABLE MIDI OUT" : "ENABLE MIDI OUT";
      midipanel.label.text = s;
      midipanel.newColor(b ? colorRed : colorGreen);
    } else if (ID == 8) {
      masterControl.instance.setEmissionLevel(masterControl.instance.emissionLevelIndex + 1);
      UpdateBrightnessLabel();
    } else if (ID == 7) {
      masterControl.instance.setGlowQuality(masterControl.instance.glowQuality == 2 ? 1 : 2);
      UpdateGlowQualityLabel();
    } else if (ID == 6) {
      if (passthroughControl.instance != null) passthroughControl.instance.Toggle();
      UpdatePassthroughLabel();
    } else {
      curSelect = ID;
      masterControl.instance.updateWireSetting(curSelect);
      for (int i = 0; i < panels.Length; i++) {
        if (i != ID) panels[i].keyHit(false);
      }
    }
  }
}
