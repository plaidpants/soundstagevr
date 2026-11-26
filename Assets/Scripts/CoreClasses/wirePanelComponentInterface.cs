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
  uiPanelSinglePress netpanel; // right column row 4, buttonID 14: MULTIPLAYER: LOCAL / INTERNET (Net/NetSession)
  uiPanelSinglePress netonpanel; // left column row 4, buttonID 19: MULTIPLAYER: ON / OFF (internet ON: NetCodeDialog)
  TextMesh netstatus; // left column row 5: OFFLINE / SEARCHING... / HOSTING (n) 90MS / JOINED (n) 89MS
  uiPanelSinglePress roompanel, roommovepanel; // right column rows 5, 6, buttonIDs 15, 16 (Net/NetRoom)
  uiPanelSinglePress harnesspanel; // right column row 8, buttonID 18: the test harness (TestHarness)
  uiPanelSinglePress sharedsamplespanel; // left column row 8, buttonID 21: size of / clear the received-samples cache (SampleCache)
  float clearArmedUntil; // the shared-samples button clears on a second press within 4 s
  uiPanelSinglePress binauralpanel; // right column row 7, buttonID 17: 3D audio off / speakers / all
  uiPanelSinglePress micpanel; // left column row 6, buttonID 22: the microphone input (MicCapture)
  uiPanelSinglePress wirepanel; // right column row -3, buttonID 23: WIRES: CURVED / STRAIGHT / INVISIBLE (replaces the three wire-type buttons)

  Color colorGreen = Color.HSVToRGB(.4f, 230f / 255, 118f / 255);
  Color colorRed = Color.HSVToRGB(0f, 230f / 255, 118f / 255);

  void Start() {
    UpdateLockLabels();

    glowSlider.setPercent(masterControl.instance.glowVal);
    for (int i = 0; i < panels.Length; i++) {
      panels[i].keyHit(i == curSelect);
    }

    CreatePassthroughButton();
    CreateGlowQualityButton();
    // (2026-11-26: GLOW BRIGHTNESS is a slider now, under GLOW LEVEL: see LayoutLowerRows)
    CreateMsaaButton();
    envpanel = CloneButton("environmentPanel", 10, 0);
    windpanel = CloneButton("windPanel", 11, 1);
    uisoundpanel = CloneButton("uiSoundPanel", 12, 2);
    skypanel = CloneButton("skyPanel", 13, 3);
    netpanel = CloneButton("multiplayerPanel", 14, 4);
    roompanel = CloneButton("roomAlignPanel", 15, 5);
    roommovepanel = CloneButton("roomMovePanel", 16, 6);
    binauralpanel = CloneButton("binauralPanel", 17, 7);
    UpdateBinauralLabel();
    if (Debug.isDebugBuild) harnesspanel = CloneButton("harnessPanel", 18, 8); // development builds only (TestHarness)
    UpdateHarnessLabel();
    netonpanel = CloneButton("multiplayerOnPanel", 19, 4);
    if (netonpanel != null) netonpanel.transform.localPosition = AboveTop(4, midipanel.transform.localPosition.x);
    CreateNetStatusLabel();
    sharedsamplespanel = CloneButton("sharedSamplesPanel", 21, 7);
    if (sharedsamplespanel != null) sharedsamplespanel.transform.localPosition = AboveTop(7, midipanel.transform.localPosition.x);
    UpdateSharedSamplesLabel();
    micpanel = CloneButton("micInputPanel", 22, 6);
    if (micpanel != null) micpanel.transform.localPosition = AboveTop(6, midipanel.transform.localPosition.x);
    UpdateMicLabel();
    MicCapture.Changed += UpdateMicLabel;
    SampleCache.Changed += UpdateSharedSamplesLabel;
    TestHarness.Changed += UpdateHarnessLabel;
    NetSession.StatusChanged += UpdateNetLabel;
    NetRoom.Changed += UpdateNetLabel;
    UpdateNetLabel();
    UpdateEnvironmentLabels();
    LayoutLowerRows();
    CreateVersionLabel();
    UpdateLockLabels(); // (MIDI OUT: menuManager has read its setting by now)

  }

  // Adds a "passthrough" toggle above the panel by cloning the MIDI OUT button, so the
  // MenuRoot prefab doesn't need editing. Clicks arrive in hit() with ID 6.
  void CreatePassthroughButton() {
    if (passthroughpanel != null || midipanel == null) return;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    HideMidiNote(g);
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
    HideMidiNote(g);
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
    HideMidiNote(g);
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
    HideMidiNote(g);
    g.name = "msaaPanel";
    g.transform.localPosition = AboveTop(3, midipanel.transform.localPosition.x);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    msaapanel = g.GetComponent<uiPanelSinglePress>();
    msaapanel.buttonID = 9;
    UpdateMsaaLabel();
  }

  // App version above the stacked buttons, so it's easy to tell which build is running.
  void CreateVersionLabel() {
    Transform src = transform.Find("WireLabel");
    if (src == null || midipanel == null) return;
    GameObject g = Instantiate(src.gameObject, src.parent, false);
    g.name = "versionLabel";
    Vector3 p = src.localPosition;
    p.x = (0.1665f + midipanel.transform.localPosition.x) * .5f;
    p.y = 0.012f + 0.038f * 10;
    g.transform.localPosition = p;
    g.transform.localScale = src.localScale * .7f;
    g.GetComponent<TextMesh>().text = "SOUNDSTAGE " + Application.version;
  }

  // The multiplayer status line: a text, not a button, under MULTIPLAYER ON / OFF.
  void CreateNetStatusLabel() {
    Transform src = transform.Find("WireLabel");
    if (src == null || midipanel == null) return;
    GameObject g = Instantiate(src.gameObject, src.parent, false);
    g.name = "netStatusLabel";
    NetCodeDialog.MeasureButton(midipanel, out float w, out float cx);
    Vector3 p = src.localPosition;
    p.x = midipanel.transform.localPosition.x + cx;
    p.y = 0.012f + 0.038f * 5;
    g.transform.localPosition = p;
    g.transform.localScale = src.localScale * .7f;
    netstatus = g.GetComponent<TextMesh>();
    netstatus.anchor = TextAnchor.MiddleCenter;
    netstatus.alignment = TextAlignment.Center;
    netStatusWidth = w;
  }
  float netStatusWidth;

  // Buttons cloned from MIDI OUT also copy its "(REQUIRES VIRTUAL MIDI DEVICE)" note.
  static void HideMidiNote(GameObject g) {
    foreach (TextMesh t in g.GetComponentsInChildren<TextMesh>(true))
      if (t.text.Contains("REQUIRES")) t.gameObject.SetActive(false);
  }

  // The added buttons stack upward from the top of the panel (rows 0,1,2... going up),
  // so they stay clear of the menu gear that sits below the panel.
  Vector3 AboveTop(float row, float x) {
    Vector3 p = midipanel.transform.localPosition;
    p.x = x;
    p.y = 0.012f + 0.038f * row;
    return p;
  }

  // Right-hand column, above the panel
  uiPanelSinglePress CloneButton(string name, int id, int row) {
    if (midipanel == null) return null;
    GameObject g = Instantiate(midipanel.gameObject, midipanel.transform.parent, false);
    HideMidiNote(g);
    g.name = name;
    g.transform.localPosition = AboveTop(row, 0.1665f);
    g.transform.localRotation = midipanel.transform.localRotation;
    g.transform.localScale = midipanel.transform.localScale;
    uiPanelSinglePress u = g.GetComponent<uiPanelSinglePress>();
    u.buttonID = id;
    return u;
  }

  void UpdateNetLabel() {
    if (netpanel == null) return;
    bool internet = NetSession.Kind == NetSession.Mode.Internet;
    netpanel.label.text = internet ? "MULTIPLAYER: INTERNET" : "MULTIPLAYER: LOCAL";
    netpanel.newColor(colorGreen);
    bool on = NetSession.On && !NetSession.Ended;
    if (netonpanel != null) {
      netonpanel.label.text = !on ? "MULTIPLAYER: OFF" : internet ? "ROOM " + NetInternet.Code + ": ON" : "MULTIPLAYER: ON";
      netonpanel.newColor(on ? colorGreen : colorRed);
    }
    if (netstatus != null) {
      netstatus.text = NetSession.StatusText();
      SetTextColor(netstatus, StatusBad(netstatus.text) ? colorRed : colorGreen);
      netstatus.transform.localScale = transform.Find("WireLabel") != null ? transform.Find("WireLabel").localScale * .7f : netstatus.transform.localScale;
      Renderer r = netstatus.GetComponent<Renderer>();
      if (r != null && netStatusWidth > 0) {
        Bounds lb = r.localBounds;
        Vector3 a = transform.InverseTransformPoint(netstatus.transform.TransformPoint(lb.min));
        Vector3 b = transform.InverseTransformPoint(netstatus.transform.TransformPoint(lb.max));
        float w = Mathf.Abs(b.x - a.x);
        if (w > netStatusWidth && w > 1e-5f) netstatus.transform.localScale *= netStatusWidth / w;
      }
    }
    if (roompanel != null) {
      roompanel.label.text = NetRoom.StatusText();
      roompanel.newColor(NetRoom.InRoom ? colorGreen : colorRed);
    }
    if (roommovepanel != null) {
      roommovepanel.label.text = NetRoom.MoveText();
      roommovepanel.newColor(NetRoom.InRoom && NetRoom.MoveUnlocked ? colorGreen : colorRed);
    }
  }

  // a status that reports something off, full, unavailable or failed
  static bool StatusBad(string t) {
    string u = t.ToUpperInvariant();
    string[] bad = { "OFFLINE", "FULL", "FAIL", "ERROR", "ENDED", "NOT ", "NO ", "LOST", "CAN'T", "CANNOT", "UNAVAILABLE", "DENIED", "TIMED OUT", "TIMEOUT", "REFUSED", "BLOCKED" };
    foreach (string b in bad) if (u.Contains(b)) return true;
    return false;
  }

  static void SetTextColor(TextMesh t, Color c) {
    Renderer r = t.GetComponent<Renderer>();
    if (r != null && r.material.HasProperty("_TintColor")) r.material.SetColor("_TintColor", c);
    else t.color = c;
  }

  // Samples received from other players (SampleCache): their size, and a two-press clear.
  void UpdateSharedSamplesLabel() {
    if (sharedsamplespanel == null) return;
    if (Time.unscaledTime < clearArmedUntil) {
      sharedsamplespanel.label.text = "PRESS AGAIN TO CLEAR";
      sharedsamplespanel.newColor(colorRed);
      return;
    }
    long bytes = SampleCache.TotalBytes();
    string size = bytes == 0 ? "NONE" : bytes < (1 << 20) ? System.Math.Max(1, (bytes + 512) >> 10) + " KB" : ((bytes + (1 << 19)) >> 20) + " MB";
    sharedsamplespanel.label.text = "SHARED SAMPLES: " + size;
    sharedsamplespanel.newColor(colorGreen);
  }

  void OnEnable() {
    clearArmedUntil = 0;
    UpdateSharedSamplesLabel(); // the size may have changed while the menu was closed
  }

  void UpdateHarnessLabel() {
    if (harnesspanel == null) return;
    harnesspanel.label.text = TestHarness.Running ? "TEST HARNESS: RUNNING" : "TEST HARNESS: OFF";
    harnesspanel.newColor(TestHarness.Running ? colorGreen : colorRed);
  }

  void OnDestroy() {
    TestHarness.Changed -= UpdateHarnessLabel;
    NetSession.StatusChanged -= UpdateNetLabel;
    NetRoom.Changed -= UpdateNetLabel;
    SampleCache.Changed -= UpdateSharedSamplesLabel;
    MicCapture.Changed -= UpdateMicLabel;
  }

  // (2026-11-26) The prefab's lower part had its own spacing (rows 0.035-0.06 apart) and
  // three buttons plus a big "WIRE TYPE" title for the wire type. Now it's one WIRES
  // button and the buttons sit on the same 0.038 rows as the ones added above (rows
  // -1 to -3 here), with OKAY centred under both columns.
  void LayoutLowerRows() {
    if (midipanel == null) return;
    float lx = midipanel.transform.localPosition.x, rx = 0.1665f;
    if (handlepanel != null) handlepanel.transform.localPosition = AboveTop(-1, lx);
    if (jackpanel != null) jackpanel.transform.localPosition = AboveTop(-2, lx);
    midipanel.transform.localPosition = AboveTop(-3, lx);

    wirepanel = CloneButton("wirePanel", 23, -3);
    UpdateWireLabel();
    foreach (UIpanel u in panels) if (u != null) u.gameObject.SetActive(false);
    Transform wl = transform.Find("WireLabel");
    if (wl != null) wl.GetComponent<TextMesh>().text = ""; // (kept: other labels are cloned from it)
    // The x = 0.1665 column (the viewer's left: the panel faces them), in groups, bottom up:
    // WIRES; ENVIRONMENT + SKY; 3D AUDIO + WIND + UI SOUNDS; then MULTIPLAYER LOCAL /
    // INTERNET level with MULTIPLAYER ON / OFF in the other column (the status line above
    // that one), with ALIGN ROOM and MOVE ROOM above it; the test harness on top.
    uiPanelSinglePress[] col = { envpanel, skypanel, binauralpanel, windpanel, uisoundpanel, netpanel, roompanel, roommovepanel, harnesspanel };
    for (int i = 0; i < col.Length; i++)
      if (col[i] != null) col[i].transform.localPosition = AboveTop(i - 2, rx);

    // The other column lost its GLOW BRIGHTNESS button (now a slider): close that gap too.
    if (msaapanel != null) msaapanel.transform.localPosition = AboveTop(2, lx);
    if (netonpanel != null) netonpanel.transform.localPosition = AboveTop(3, lx);
    if (netstatus != null) { Vector3 q = netstatus.transform.localPosition; q.y = AboveTop(4, lx).y; netstatus.transform.localPosition = q; }
    if (micpanel != null) micpanel.transform.localPosition = AboveTop(5, lx);
    if (sharedsamplespanel != null) sharedsamplespanel.transform.localPosition = AboveTop(6, lx);

    // GLOW LEVEL and GLOW BRIGHTNESS: two sliders stacked between the columns, in the
    // middle of the panel's height, each title above its slider at the prefab's spacing
    Transform glow = transform.Find("GlowLevel"), glowBar = transform.Find("SliderContainer");
    if (glow != null && glowBar != null) {
      float gap = glow.localPosition.y - glowBar.localPosition.y;
      float c = AboveTop(2f, rx).y;
      Transform brightTitle = Instantiate(glow.gameObject, glow.parent, false).transform;
      brightTitle.name = "GlowBrightness";
      TextMesh bt = brightTitle.GetComponent<TextMesh>();
      bt.text = "GLOW\nBRIGHTNESS";
      Transform brightBar = Instantiate(glowBar.gameObject, glowBar.parent, false).transform;
      brightBar.name = "BrightnessSliderContainer";
      glow.localPosition = new Vector3(glow.localPosition.x, c + 1.5f * gap, glow.localPosition.z);
      glowBar.localPosition = new Vector3(glowBar.localPosition.x, c + .5f * gap, glowBar.localPosition.z);
      brightTitle.localPosition = new Vector3(glow.localPosition.x, c - .5f * gap, glow.localPosition.z);
      brightBar.localPosition = new Vector3(glowBar.localPosition.x, c - 1.5f * gap, glowBar.localPosition.z);
      brightTitleToFit = bt; brightBarToFit = brightBar.Find("sliderBar"); // (fitted once showing: LateUpdate)
      brightSlider = brightBar.GetComponentInChildren<slider>();
      if (brightSlider != null) {
        brightPercent = Mathf.InverseLerp(BrightMin, 1f, masterControl.instance.emissionBrightness);
        brightSlider.setPercent(brightPercent);
      }
    }

    // OKAY: centred under the two columns, a row and a half below MIDI OUT (room for its note)
    Transform ok = transform.Find("Load");
    if (ok != null) {
      NetCodeDialog.MeasureButton(midipanel, out float w, out float cx);
      float centre = (lx + rx) * .5f + cx;
      float okCx = 0;
      Transform o = ok.Find("Outline");
      Renderer r = o != null ? o.GetComponent<Renderer>() : null;
      if (r != null) {
        Bounds lb = r.localBounds;
        Vector3 a = transform.InverseTransformPoint(o.TransformPoint(lb.min)), b = transform.InverseTransformPoint(o.TransformPoint(lb.max));
        okCx = (a.x + b.x) * .5f - ok.localPosition.x;
      }
      ok.localPosition = new Vector3(centre - okCx, AboveTop(-4.5f, lx).y, ok.localPosition.z);
    }
  }

  void UpdateWireLabel() {
    if (wirepanel == null) return;
    masterControl.WireMode m = masterControl.instance.WireSetting;
    wirepanel.label.text = "WIRES: " + (m == masterControl.WireMode.Curved ? "CURVED" : m == masterControl.WireMode.Straight ? "STRAIGHT" : "INVISIBLE");
    wirepanel.newColor(m == masterControl.WireMode.Invisible ? colorRed : colorGreen);
  }

  void UpdateMicLabel() {
    if (micpanel == null) return;
    micpanel.label.text = MicCapture.MenuLabel();
    micpanel.newColor(micpanel.label.text == "MIC: NONE" ? colorRed : colorGreen);
  }

  void UpdateEnvironmentLabels() {
    EnvironmentControl env = EnvironmentControl.instance;
    if (envpanel != null) {
      int lv = env != null ? env.level : EnvironmentControl.defaultLevel;
      envpanel.label.text = "ENVIRONMENT: " + EnvironmentControl.levelNames[lv];
      envpanel.newColor(lv == 0 ? colorRed : colorGreen); // (level 0: OFF)
    }
    if (windpanel != null) {
      bool on = env != null ? env.WindOn : PlayerPrefs.GetInt("envSound", 1) == 1;
      windpanel.label.text = on ? "WIND: ON" : "WIND: OFF";
      windpanel.newColor(on ? colorGreen : colorRed);
    }
    if (skypanel != null) {
      int s = env != null ? env.sky : 0;
      bool envOn = (env != null ? env.level : EnvironmentControl.defaultLevel) != 0;
      skypanel.label.text = envOn ? "SKY: " + EnvironmentControl.skyNames[s] : "SKY: OFF"; // (no sky with the environment off)
      skypanel.newColor(envOn ? colorGreen : colorRed);
    }
    if (uisoundpanel != null) {
      bool on = UISound.Enabled;
      uisoundpanel.label.text = on ? "UI SOUNDS: ON" : "UI SOUNDS: OFF";
      uisoundpanel.newColor(on ? colorGreen : colorRed);
    }
  }

  void UpdateBinauralLabel() {
    if (binauralpanel == null) return;
    masterControl.BinauralMode m = masterControl.instance.BinauralSetting;
    binauralpanel.label.text = "3D AUDIO: " + (m == masterControl.BinauralMode.None ? "OFF" : m == masterControl.BinauralMode.Speaker ? "SPEAKERS" : "ALL");
    binauralpanel.newColor(m == masterControl.BinauralMode.None ? colorRed : colorGreen);
  }

  void UpdateMsaaLabel() {
    if (msaapanel == null) return;
    int m = QuestPerformanceSetup.Msaa;
    msaapanel.label.text = "ANTI-ALIASING: " + (m > 0 ? m + "X" : "OFF");
    msaapanel.newColor(m > 0 ? colorGreen : colorRed);
  }

  void UpdateBrightnessLabel() {
    if (brightnesspanel == null) return;
    int pct = Mathf.RoundToInt(masterControl.emissionLevels[masterControl.instance.emissionLevelIndex] * 100);
    brightnesspanel.label.text = "GLOW BRIGHTNESS: " + pct + "%";
    brightnesspanel.newColor(colorGreen);
  }

  // (2026-11-26) Labels say the state, and an off / unlocked / failed state is red.
  // POS LOCK / JACK LOCK enabled = handles / jacks can't be grabbed.
  void UpdateLockLabels() {
    if (handlepanel != null) {
      bool locked = !masterControl.instance.handlesEnabled;
      handlepanel.label.text = locked ? "POS LOCK ENABLED" : "POS LOCK DISABLED";
      handlepanel.newColor(locked ? colorGreen : colorRed);
    }
    if (jackpanel != null) {
      bool locked = !masterControl.instance.jacksEnabled;
      jackpanel.label.text = locked ? "JACK LOCK ENABLED" : "JACK LOCK DISABLED";
      jackpanel.newColor(locked ? colorGreen : colorRed);
    }
    if (midipanel != null && menuMgr != null) {
      bool on = menuMgr.midiOutEnabled;
      midipanel.label.text = on ? "MIDI OUT ENABLED" : "MIDI OUT DISABLED";
      midipanel.newColor(on ? colorGreen : colorRed);
    }
  }

  // GLOW: the button steps HIGH -> LOW -> OFF; OFF is the GLOW LEVEL slider at the bottom
  // (the slider can turn it off too), and HIGH from OFF brings the last level back.
  static bool GlowOff { get { return masterControl.instance.glowVal <= .001f; } } // (masterControl switches the bloom off there)
  float glowBeforeOff = 1f;
  void CycleGlow() {
    masterControl mc = masterControl.instance;
    if (GlowOff) {
      mc.setGlowQuality(2);
      SetGlowLevel(glowBeforeOff > .05f ? glowBeforeOff : 1f);
    } else if (mc.glowQuality == 2) {
      mc.setGlowQuality(1);
    } else {
      glowBeforeOff = mc.glowVal;
      SetGlowLevel(0);
    }
    UpdateGlowQualityLabel();
  }
  void SetGlowLevel(float v) {
    masterControl.instance.setGlowLevel(v);
    glowSlider.setPercent(v);
  }

  void UpdateGlowQualityLabel() {
    if (glowqualitypanel == null) return;
    if (GlowOff) {
      glowqualitypanel.label.text = "GLOW: OFF";
      glowqualitypanel.newColor(colorRed);
      return;
    }
    bool high = masterControl.instance.glowQuality == 2;
    glowqualitypanel.label.text = high ? "GLOW QUALITY: HIGH" : "GLOW QUALITY: LOW";
    glowqualitypanel.newColor(colorGreen);
  }

  void UpdatePassthroughLabel() {
    if (passthroughpanel == null) return;
    bool on = passthroughControl.instance != null && passthroughControl.instance.isOn;
    passthroughpanel.label.text = on ? "PASSTHROUGH ENABLED" : "PASSTHROUGH DISABLED";
    passthroughpanel.newColor(on ? colorGreen : colorRed);
  }

  void Update() {
    if (glowSlider.percent != masterControl.instance.glowVal) {
      bool wasOff = GlowOff;
      masterControl.instance.setGlowLevel(glowSlider.percent);
      if (GlowOff != wasOff) UpdateGlowQualityLabel();
    }
    if (brightSlider != null && brightSlider.percent != brightPercent) {
      brightPercent = brightSlider.percent;
      masterControl.instance.setEmissionBrightness(Mathf.Lerp(BrightMin, 1f, brightPercent));
    }
  }
  slider brightSlider;
  float brightPercent = -1;
  static float BrightMin { get { return masterControl.emissionLevels[masterControl.emissionLevels.Length - 1]; } }

  // ---- labels that fit their buttons (2026-11-26) ----
  // A long label ("MULTIPLAYER: INTERNET", "GLOW BRIGHTNESS: 100%", a room code...) ran
  // past its button. Each frame the panel is showing, any button whose text changed is
  // measured and, if too wide, its label is shrunk to fit (never grown past its own size).
  readonly System.Collections.Generic.Dictionary<uiPanelSinglePress, string> fitted = new System.Collections.Generic.Dictionary<uiPanelSinglePress, string>();
  readonly System.Collections.Generic.Dictionary<uiPanelSinglePress, Vector3> labelBase = new System.Collections.Generic.Dictionary<uiPanelSinglePress, Vector3>();
  uiPanelSinglePress[] buttons;
  public static float labelFill = .88f; // of the button's width

  TextMesh brightTitleToFit; Transform brightBarToFit;
  // the wider GLOW BRIGHTNESS title: no wider than its slider bar (measured once it's built)
  void FitBrightTitle() {
    Transform t = brightTitleToFit.transform, bar = brightBarToFit;
    Renderer tr = brightTitleToFit.GetComponent<Renderer>(), br = bar != null ? bar.GetComponent<Renderer>() : null;
    if (tr == null || br == null) { brightTitleToFit = null; return; }
    if (tr.localBounds.size.x < 1e-6f) return; // not built yet
    float tw = Mathf.Abs(transform.InverseTransformVector(t.TransformVector(new Vector3(tr.localBounds.size.x, 0, 0))).x);
    float bw = Mathf.Abs(transform.InverseTransformVector(bar.TransformVector(new Vector3(br.localBounds.size.x, 0, 0))).x);
    if (tw > bw && tw > 1e-5f) t.localScale *= bw / tw;
    brightTitleToFit = null;
  }

  void LateUpdate() {
    if (brightTitleToFit != null && brightTitleToFit.gameObject.activeInHierarchy) FitBrightTitle();
    if (buttons == null || Time.frameCount % 120 == 0) buttons = GetComponentsInChildren<uiPanelSinglePress>(true);
    foreach (uiPanelSinglePress b in buttons) {
      if (b == null || b.label == null || !b.label.gameObject.activeInHierarchy) continue;
      if (midipanel != null && b.transform.parent != midipanel.transform.parent) continue; // (NetCodeDialog fits its own)
      string t = b.label.text;
      if (fitted.TryGetValue(b, out string done) && done == t) continue;
      if (FitLabel(b)) fitted[b] = t;
    }
  }

  bool FitLabel(uiPanelSinglePress b) {
    Transform lt = b.label.transform, p = b.transform.parent;
    if (!labelBase.TryGetValue(b, out Vector3 baseScale)) { baseScale = lt.localScale; labelBase[b] = baseScale; }
    lt.localScale = baseScale;
    Renderer r = b.label.GetComponent<Renderer>();
    if (r == null || p == null) return true;
    Bounds lb = r.localBounds;
    if (lb.size.x < 1e-6f) return false; // not built yet: next frame
    float w = Mathf.Abs(p.InverseTransformVector(lt.TransformVector(new Vector3(lb.size.x, 0, 0))).x);
    NetCodeDialog.MeasureButton(b, out float room, out float cx);
    room *= labelFill;
    if (w > room && w > 1e-5f) lt.localScale = baseScale * (room / w);
    return true;
  }

  public override void hit(bool on, int ID = -1) {
    if (!on) return;
    if (ID == -2) //okay
    {
      rootMenu.cancelFileMenu(); //not right
      transform.gameObject.SetActive(false);
    } else if (ID == 3) {
      masterControl.instance.toggleHandles(!masterControl.instance.handlesEnabled);
      UpdateLockLabels();
    } else if (ID == 4) {
      masterControl.instance.toggleJacks(!masterControl.instance.jacksEnabled);
      UpdateLockLabels();
    } else if (ID == 5) {
      menuMgr.toggleMidiOut(!menuMgr.midiOutEnabled);
      UpdateLockLabels();
    } else if (ID == 10) {
      if (EnvironmentControl.instance != null) EnvironmentControl.instance.Cycle();
      UpdateEnvironmentLabels();
    } else if (ID == 11) {
      if (EnvironmentControl.instance != null) EnvironmentControl.instance.SetWind(!EnvironmentControl.instance.WindOn);
      UpdateEnvironmentLabels();
    } else if (ID == 18) {
      TestHarness.Toggle();
      UpdateHarnessLabel();
    } else if (ID == 17) {
      masterControl.instance.CycleBinaural();
      UpdateBinauralLabel();
    } else if (ID == 15) {
      NetRoom.AlignButton();
      UpdateNetLabel();
    } else if (ID == 16) {
      NetRoom.MoveButton();
      UpdateNetLabel();
    } else if (ID == 14) {
      NetSession.ToggleKind(); // LOCAL <-> INTERNET (turns multiplayer off if it was on)
      UpdateNetLabel();
    } else if (ID == 19) {
      if (NetSession.On && !NetSession.Ended) NetSession.SetOn(false);
      else if (NetSession.Kind == NetSession.Mode.Local) NetSession.SetOn(true);
      else {
        Transform wl = transform.Find("WireLabel");
        NetCodeDialog.Open(midipanel, wl != null ? wl.GetComponent<TextMesh>() : null, 0.1665f); // terms, then the room code
      }
      UpdateNetLabel();
    } else if (ID == 23) {
      curSelect = ((int)masterControl.instance.WireSetting + 1) % 3;
      masterControl.instance.updateWireSetting(curSelect);
      UpdateWireLabel();
    } else if (ID == 22) {
      MicCapture.NextInput();
      UpdateMicLabel();
    } else if (ID == 21) {
      if (Time.unscaledTime < clearArmedUntil) {
        clearArmedUntil = 0;
        SampleCache.Clear();
      } else {
        clearArmedUntil = Time.unscaledTime + 4f;
        Invoke(nameof(UpdateSharedSamplesLabel), 4.05f); // back to the size if not pressed again
      }
      UpdateSharedSamplesLabel();
    } else if (ID == 13) {
      if (EnvironmentControl.instance != null && EnvironmentControl.instance.level != 0) EnvironmentControl.instance.CycleSky(); // (off with the environment)
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
      CycleGlow();
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
