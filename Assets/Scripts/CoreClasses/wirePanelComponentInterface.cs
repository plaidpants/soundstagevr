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
  uiPanelSinglePress msaapanel; // cloned from midipanel at runtime (buttonID 9)
  uiPanelSinglePress envpanel, windpanel, uisoundpanel, skypanel; // right column, buttonIDs 10-13

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
    CreateMsaaButton();
    envpanel = CloneButton("environmentPanel", 10, 0);
    windpanel = CloneButton("windPanel", 11, 1);
    uisoundpanel = CloneButton("uiSoundPanel", 12, 2);
    skypanel = CloneButton("skyPanel", 13, 3);
    UpdateEnvironmentLabels();
    if (PlayerPrefs.GetInt("midiOut") == 1) {
      string s = "DISABLE MIDI OUT";
      midipanel.label.text = s;
      midipanel.newColor(Color.HSVToRGB(0f, 230f / 255, 118f / 255));
    }

  }

  // Adds a "passthrough" toggle above the panel by cloning the MIDI OUT button, so the
  // MenuRoot prefab doesn't need editing. Clicks arrive in hit() with ID 6.
  void CreatePassthroughButton() {
    if (passthroughpanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = "passthroughPanel";
    g.transform.localPosition = AboveTop(0, midipanel.transform.localPosition.x);
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
    g.transform.localPosition = AboveTop(1, midipanel.transform.localPosition.x);
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
    g.transform.localPosition = AboveTop(2, midipanel.transform.localPosition.x);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    brightnesspanel = g.GetComponent<uiPanelSinglePress>();
    brightnesspanel.buttonID = 8;
    UpdateBrightnessLabel();
  }

  void CreateMsaaButton() {
    if (msaapanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = "msaaPanel";
    g.transform.localPosition = AboveTop(3, midipanel.transform.localPosition.x);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    msaapanel = g.GetComponent<uiPanelSinglePress>();
    msaapanel.buttonID = 9;
    UpdateMsaaLabel();
  }

  // The added buttons stack upward from the top of the panel (rows 0,1,2... going up),
  // so they stay clear of the menu gear that sits below the panel.
  Vector3 AboveTop(int row, float x) {
    Vector3 p = midipanel.transform.localPosition;
    p.x = x;
    p.y = 0.012f + 0.038f * row;
    return p;
  }

  // Right-hand column, above the panel
  uiPanelSinglePress CloneButton(string name, int id, int row) {
    if (midipanel == null) return null;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    g.name = name;
    g.transform.localPosition = AboveTop(row, 0.1665f);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    uiPanelSinglePress u = g.GetComponent<uiPanelSinglePress>();
    u.buttonID = id;
    return u;
  }

  void UpdateEnvironmentLabels() {
    EnvironmentControl env = EnvironmentControl.instance;
    if (envpanel != null) {
      int lv = env != null ? env.level : EnvironmentControl.defaultLevel;
      envpanel.label.text = "ENVIRONMENT: " + EnvironmentControl.levelNames[lv];
      envpanel.newColor(colorGreen);
    }
    if (windpanel != null) {
      bool on = env != null ? env.WindOn : PlayerPrefs.GetInt("envSound", 1) == 1;
      windpanel.label.text = on ? "WIND: ON" : "WIND: OFF";
      windpanel.newColor(on ? colorGreen : colorRed);
    }
    if (skypanel != null) {
      int s = env != null ? env.sky : 0;
      skypanel.label.text = "SKY: " + EnvironmentControl.skyNames[s];
      skypanel.newColor(colorGreen);
    }
    if (uisoundpanel != null) {
      bool on = UISound.Enabled;
      uisoundpanel.label.text = on ? "UI SOUNDS: ON" : "UI SOUNDS: OFF";
      uisoundpanel.newColor(on ? colorGreen : colorRed);
    }
  }

  void UpdateMsaaLabel() {
    if (msaapanel == null) return;
    int m = QuestPerformanceSetup.Msaa;
    msaapanel.label.text = "ANTI-ALIASING: " + (m > 0 ? m + "X" : "OFF");
    msaapanel.newColor(colorGreen);
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
    } else if (ID == 10) {
      if (EnvironmentControl.instance != null) EnvironmentControl.instance.Cycle();
      UpdateEnvironmentLabels();
    } else if (ID == 11) {
      if (EnvironmentControl.instance != null) EnvironmentControl.instance.SetWind(!EnvironmentControl.instance.WindOn);
      UpdateEnvironmentLabels();
    } else if (ID == 13) {
      if (EnvironmentControl.instance != null) EnvironmentControl.instance.CycleSky();
      UpdateEnvironmentLabels();
    } else if (ID == 12) {
      UISound.Enabled = !UISound.Enabled;
      UpdateEnvironmentLabels();
    } else if (ID == 9) {
      QuestPerformanceSetup.CycleMsaa();
      UpdateMsaaLabel();
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
