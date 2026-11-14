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

// Scale Sequencer (2026-11): the step sequencer from James's UNTZtrument firmware
// (github.com/plaidpants/Adafruit_UNTZtrument, UNTZtrument_Step_Seq) as a SoundStage
// device. One device is one UNTZ "layer":
//   * The grid is the drum machine's (same cubes, play head, beat slider, swing, PLAY,
//     LOOP/ONCE, control input), but every row is a NOTE of the chosen scale, highest
//     at the top. There are 32 note rows in all; the grid shows a window of them.
//     The corner handle at the bottom adds/removes columns and LOWER rows; the handle
//     at the top corner adds/removes HIGHER rows (the device grows upward and the
//     cells you've placed stay where they are). Hidden rows keep their notes.
//     Root (the tape's own pitch) is the 9th row from the bottom of the 32.
//   * Sound is the xyloroll's polyphonic voice engine playing ONE tape (the sampler
//     panel at the left: tape slot, MUTE, output jack, speaker) through the ADSR.
//   * Scale: touch the scale name to open a list of the firmware's 34 scales. The LINK
//     switch next to it ties the scale to every other linked Scale Sequencer: picking a
//     scale on one changes them all; switching a device to LINK adopts the shared scale.
//   * JOIN switch (the firmware's "long notes"): lit cells next to each other in a row
//     play as one held note instead of retriggering, and light up together while the
//     note plays. Joining wraps round the ends of the
//     grid. Cells stay individually selectable. STEP plays every cell on its own.
// Several of these start together the same way drum machines do: one button / cable
// through a splitter into each control input.
// The prefab is a copy of Sequencer.prefab with this script. The extra parts are put
// into it with the editor menu SoundStage > Add Scale Sequencer parts to prefab and then
// positioned by hand; any still missing are made in Awake at the positions below.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class scaleSequencerDeviceInterface : deviceInterface {
  // same names as sequencerDeviceInterface so the copied prefab keeps its references
  public GameObject touchCubePrefab, jackOutPrefab, samplerPrefab;
  public Transform stretchNode;
  public xHandle stepSelect;
  public sliderNotched beatSlider;
  public omniJack controlInput;
  public button playButton;
  public TextMesh[] dimensionDisplays;
  public basicSwitch loopSwitch; // LOOP / ONCE
  public Font labelFont;          // scale button / list text (Arial Bold, set in the prefab)

  // where the added parts go (device-local; the viewer is on the +z side, +x is their left)
  public static Vector3 samplerPos = new Vector3(.081f, 0f, -.028f);       // where the drum machine's first row sampler sits
  public static Vector3 adsrPos = new Vector3(.19f, -.15f, -.01f);
  public static Vector3 scaleButtonPos = new Vector3(-.13f, .053f, .02f);
  public static Vector2 scaleButtonSize = new Vector2(.075f, .02f);
  public static Vector3 scaleListPos = new Vector3(0f, .09f, .02f);         // bottom centre of the scale list
  public static Vector2 scaleItemSize = new Vector2(.1f, .02f);
  public const int scaleListRows = 12;
  public static Vector3 joinSwitchPos = new Vector3(.074f, .053f, .028f);
  public static Vector3 linkSwitchPos = new Vector3(-.185f, .053f, .028f);
  public const int voiceCount = 8;

  public const int max = 32;        // columns, and note rows in all
  public const int rootSlot = 8;    // note row (from the bottom) that plays the tape at its own pitch
  const int defaultRows = 8;
  const float cubeConst = .04f;

  float swingPercent = 0;
  int beatSpeed = 0;

  public int[] dimensions = new int[] { 1, 1 };   // columns, rows shown
  int[] curDimensions = new int[] { 0, 0 };
  int topSlot = rootSlot + defaultRows - 1;       // note row shown at the top of the grid
  int LowSlot { get { return topSlot - curDimensions[1] + 1; } }
  Transform[][] cubes;              // [column][note row], null when not shown
  public bool[][] cubeStates;       // [column][note row]

  // Parts added on top of the drum machine. Normally in the prefab (editor menu
  // SoundStage > Add Scale Sequencer parts to prefab, then positioned by hand); any that
  // are missing are made in Awake at the positions above.
  public GameObject samplerPanel;   // tape slot, MUTE, output jack, speaker + voice engine
  public adsrInterface adsr;
  public basicSwitch joinSwitch, linkSwitch;
  public gridEdgeHandle topHandle;
  public scalePickItem scaleButton;

  dial swingDial;
  scaleSeqSignalGenerator voices;
  samplerLoad sampler;
  button muteButton;
  omniJack output;
  GameObject scaleList;
  Vector3 topHandleOffset;          // from the corner handle (x, z); y is kept as placed

  signalGenerator externalPulse;
  beatTracker _beatManager;
  double _phase = 0;
  double _sampleDuration = 0;
  float[] lastPlaySig = new float[] { 0, 0 };

  int scaleIndex = 0;
  bool join = true;
  bool link = false;

  // scale shared by all linked Scale Sequencers (-1: none picked yet)
  static int sharedScale = -1;
  static readonly List<scaleSequencerDeviceInterface> all = new List<scaleSequencerDeviceInterface>();

  // notes currently held, per note row (touched from the beat callback and the main thread)
  bool[] sounding = new bool[max];
  int[] soundingKey = new int[max];

  public override void Awake() {
    base.Awake();
    if (loopSwitch == null) loopSwitch = SequencerLoopSwitch.Create(transform);
    cubes = new Transform[max][];
    cubeStates = new bool[max][];
    for (int i = 0; i < max; i++) {
      cubes[i] = new Transform[max];
      cubeStates[i] = new bool[max];
    }

    beatSlider = GetComponentInChildren<sliderNotched>();
    swingDial = GetComponentInChildren<dial>();

    _sampleDuration = 1.0 / AudioSettings.outputSampleRate;
    _beatManager = ScriptableObject.CreateInstance<beatTracker>();

    for (int i = 0; i < dimensionDisplays.Length; i++) {
      dimensionDisplays[i].GetComponent<Renderer>().material.SetColor("_TintColor", Color.white);
      dimensionDisplays[i].gameObject.SetActive(false);
    }
    dimensionDisplays[0].GetComponent<Renderer>().material.SetFloat("_EmissionGain", .3f);
    dimensionDisplays[1].GetComponent<Renderer>().material.SetFloat("_EmissionGain", .3f);

    // the copied prefab's "?" plays the drum machine's help video; no video for this device yet
    foreach (string n in new string[] { "tooltip", "tooltipVideo" }) {
      Transform t = transform.Find(n);
      if (t != null) t.gameObject.SetActive(false);
    }

    // start with 8 note rows, root at the bottom
    Vector3 sp = stretchNode.localPosition;
    sp.y = StretchY(defaultRows);
    stretchNode.localPosition = sp;

    BuildMissingParts();

    // runtime hookup
    voices = samplerPanel.GetComponent<scaleSeqSignalGenerator>();
    sampler = samplerPanel.GetComponent<samplerLoad>();
    muteButton = samplerPanel.GetComponentInChildren<button>();
    output = samplerPanel.GetComponentInChildren<omniJack>();
    if (voices.monophonicPrefab != null) {
      if (adsr != null) voices.spawnVoices(voiceCount, adsr.volumes, adsr.durations);
      else voices.spawnVoices(voiceCount, new float[] { 1, .8f }, new float[] { .01f, .2f, .3f });
      voices.ready = true;
    } else Debug.LogWarning("[ScaleSequencer] xyloroll voice prefab not found; no sound");
    topHandle.owner = this;
    topHandleOffset = topHandle.transform.localPosition - stretchNode.localPosition;
    topHandleOffset.y = topHandle.transform.localPosition.y;
    scaleButton.onPick = ToggleScaleList;
    scaleButton.label.text = ScaleTable.names[scaleIndex];
  }

  static void Kill(Object o) {
    if (o == null) return;
    if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
  }

  static float StretchY(int rows) { return rows * -cubeConst - cubeConst * .75f; }

  // Makes whichever added parts are missing. Called from Awake, and by the editor tool
  // (Editor/ScaleSequencerPrefabTools.cs) on the prefab, where nothing is awake.
  public void BuildMissingParts() {
    GameObject xylo = Resources.Load("Prefabs/XyloRoll") as GameObject;

    // --- sampler panel + voice engine -------------------------------------------------
    // The drum machine's row sampler (tape slot, MUTE, output jack, speaker). Its own
    // one-shot player is swapped for the xyloroll voice engine before it wakes up, so
    // the jack and the speaker pick the engine up as "their" signal.
    if (samplerPanel == null) {
      GameObject holder = new GameObject("holder");
      holder.SetActive(false);
      holder.transform.SetParent(transform, false);
      GameObject s = Instantiate(samplerPrefab, holder.transform, false);
      s.name = "samplerPanel";
      miniSamplerComponentInterface oldPanel = s.GetComponent<miniSamplerComponentInterface>();
      if (oldPanel != null) DestroyImmediate(oldPanel);
      clipPlayerSimple oldPlayer = s.GetComponent<clipPlayerSimple>();
      if (oldPlayer != null) DestroyImmediate(oldPlayer);
      s.GetComponent<samplerLoad>().players = new clipPlayer[0];

      scaleSeqSignalGenerator gen = s.AddComponent<scaleSeqSignalGenerator>();
      xylorollSignalGenerator xyloGen = xylo != null ? xylo.GetComponent<xylorollSignalGenerator>() : null;
      if (xyloGen != null) gen.monophonicPrefab = xyloGen.monophonicPrefab;
      gen.oscInput = false; // tape, not the oscillator bank
      s.AddComponent<scaleSeqSamplerPanel>().voices = gen;

      s.transform.SetParent(transform, false); // wakes it up
      s.transform.localPosition = samplerPos;
      s.transform.localRotation = Quaternion.identity;
      s.transform.localScale = Vector3.one;
      Kill(holder);
      samplerPanel = s;
    }

    // --- ADSR (the xyloroll's) ----------------------------------------------------------
    if (adsr == null) {
      Transform adsrSrc = xylo != null ? xylo.transform.Find("ADSR") : null;
      if (adsrSrc != null) {
        GameObject a = Instantiate(adsrSrc.gameObject, transform, false);
        a.name = "ADSR";
        a.transform.localPosition = adsrPos;
        a.transform.localRotation = adsrSrc.localRotation;
        a.transform.localScale = adsrSrc.localScale;
        adsr = a.GetComponent<adsrInterface>();
      }
    }

    // --- JOIN and LINK switches: copies of the Gain device's switch -------------------------
    if (joinSwitch == null) joinSwitch = MakeSwitch("joinSwitch", joinSwitchPos, "JOIN", "STEP");
    if (linkSwitch == null) linkSwitch = MakeSwitch("linkSwitch", linkSwitchPos, "LINK", "OWN");

    // --- top-row handle: a copy of the corner stretch handle -------------------------------
    if (topHandle == null) {
      GameObject holder = new GameObject("holder");
      holder.SetActive(false);
      holder.transform.SetParent(transform, false);
      GameObject h = Instantiate(stretchNode.gameObject, holder.transform, false);
      h.name = "topRowHandle";
      xyHandle xy = h.GetComponent<xyHandle>();
      Material onMat = xy != null ? xy.onMat : null;
      if (xy != null) DestroyImmediate(xy);
      var kids = new List<GameObject>();
      foreach (Transform c in h.transform) kids.Add(c.gameObject); // the size label
      foreach (GameObject k in kids) DestroyImmediate(k);
      topHandle = h.AddComponent<gridEdgeHandle>();
      topHandle.onMat = onMat;
      topHandle.owner = this;
      h.transform.SetParent(transform, false);
      h.transform.localPosition = new Vector3(stretchNode.localPosition.x, topHandleY, stretchNode.localPosition.z);
      Kill(holder);
    }

    // --- scale name button ----------------------------------------------------------------
    if (scaleButton == null) scaleButton = MakeItem("scaleButton", scaleButtonPos, scaleButtonSize, ScaleTable.names[0]);
  }

  basicSwitch MakeSwitch(string name, Vector3 pos, string on, string off) {
    GameObject gain = Resources.Load("Prefabs/Gain") as GameObject;
    Transform swSrc = gain != null ? gain.transform.Find("switch") : null;
    if (swSrc == null) return null;
    GameObject g = Instantiate(swSrc.gameObject, transform, false);
    g.name = name;
    g.transform.localPosition = pos;
    g.transform.localRotation = SequencerLoopSwitch.localRot;
    g.transform.localScale = Vector3.one * SequencerLoopSwitch.scale;
    basicSwitch sw = g.GetComponent<basicSwitch>();
    sw.redOption = false;
    SetText(sw.onLabel, on);
    SetText(sw.offLabel, off);
    return sw;
  }

  static void SetText(Transform t, string s) {
    if (t == null) return;
    TextMesh tm = t.GetComponent<TextMesh>();
    if (tm != null) tm.text = s;
  }

  // a touchable box with a label (the scale button and the list entries)
  scalePickItem MakeItem(string name, Vector3 pos, Vector2 size, string text, Transform parent = null) {
    GameObject g = new GameObject(name);
    g.transform.SetParent(parent != null ? parent : transform, false);
    g.transform.localPosition = pos;
    g.transform.localRotation = Quaternion.identity;
    g.transform.localScale = Vector3.one;
    BoxCollider col = g.AddComponent<BoxCollider>();
    col.size = new Vector3(size.x, size.y, .006f);

    GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
    DestroyImmediate(b.GetComponent<Collider>());
    b.name = "box";
    b.transform.SetParent(g.transform, false);
    b.transform.localScale = new Vector3(size.x, size.y, .004f);
    Renderer r = b.GetComponent<Renderer>();
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    r.receiveShadows = false;
    Material glow = touchCubePrefab != null ? touchCubePrefab.GetComponent<button>().onMat : null;
    if (glow != null) r.sharedMaterial = glow;

    scalePickItem item = g.AddComponent<scalePickItem>();
    item.box = r;
    item.glowBase = glow;

    // label: a copy of the cell readout text, just in front of the box
    GameObject lab = Instantiate(dimensionDisplays[1].gameObject, g.transform, false);
    lab.name = "label";
    lab.SetActive(true);
    lab.transform.localPosition = new Vector3(0, 0, .0035f);
    lab.transform.localRotation = dimensionDisplays[1].transform.localRotation;
    lab.transform.localScale = dimensionDisplays[1].transform.localScale;
    item.label = lab.GetComponent<TextMesh>();
    if (labelFont != null) {
      item.label.font = labelFont;
      item.label.fontStyle = FontStyle.Normal;
      item.label.GetComponent<Renderer>().sharedMaterial = labelFont.material;
      item.label.color = Color.white;
    } else item.labelBase = item.label.GetComponent<Renderer>().sharedMaterial;
    item.label.text = text;
    item.label.anchor = TextAnchor.MiddleCenter;
    item.label.alignment = TextAlignment.Center;
    return item;
  }

  void ToggleScaleList() {
    if (scaleList != null) { CloseScaleList(); return; }
    scaleList = new GameObject("scaleList");
    scaleList.transform.SetParent(transform, false);
    scaleList.transform.localPosition = scaleListPos;
    scaleList.transform.localRotation = Quaternion.identity;
    int cols = Mathf.CeilToInt((float)ScaleTable.Count / scaleListRows);
    float w = scaleItemSize.x + .004f, hgt = scaleItemSize.y + .004f;
    for (int i = 0; i < ScaleTable.Count; i++) {
      int col = i / scaleListRows, row = i % scaleListRows;
      // first column on the viewer's left (+x), first row at the top
      Vector3 p = new Vector3(((cols - 1) * .5f - col) * w, (scaleListRows - .5f - row) * hgt, 0);
      scalePickItem it = MakeItem("scale" + i, p, scaleItemSize, ScaleTable.names[i], scaleList.transform);
      int pick = i;
      it.onPick = () => PickScale(pick);
      it.SetOn(i == scaleIndex);
    }
    scaleButton.SetOn(true);
  }

  void CloseScaleList() {
    if (scaleList != null) Destroy(scaleList);
    scaleList = null;
    if (scaleButton != null) scaleButton.SetOn(false);
  }

  void PickScale(int i) {
    SetScale(i);
    CloseScaleList();
    if (link) {
      sharedScale = scaleIndex;
      foreach (scaleSequencerDeviceInterface d in all) if (d != this && d != null && d.link) d.SetScale(sharedScale);
    }
  }

  void SetScale(int i) {
    scaleIndex = Mathf.Clamp(i, 0, ScaleTable.Count - 1);
    if (scaleButton != null) scaleButton.label.text = ScaleTable.names[scaleIndex];
    if (scaleList != null) { CloseScaleList(); ToggleScaleList(); } // refresh the highlight
  }

  void SetLink(bool on) {
    link = on;
    if (!on) { ForgetSharedIfUnused(); return; }
    if (sharedScale < 0) sharedScale = scaleIndex; // first linked device sets it
    else SetScale(sharedScale);
  }

  static void ForgetSharedIfUnused() {
    foreach (scaleSequencerDeviceInterface d in all) if (d != null && d.link) return;
    sharedScale = -1;
  }

  void OnEnable() { if (!all.Contains(this)) all.Add(this); }
  void OnDisable() {
    all.Remove(this);
    ForgetSharedIfUnused();
  }

  bool loopLoaded = false;
  void Start() {
    if (!loopLoaded && loopSwitch != null) loopSwitch.setSwitch(false); // ONCE, like the drum machine
    if (!loopLoaded && linkSwitch != null) linkSwitch.setSwitch(false);  // OWN scale (basicSwitch starts up)
    _beatManager.setTriggers(executeNextStep, resetSteps);
    _beatManager.updateBeatNoTriplets(beatSpeed);
    _beatManager.updateSwing(swingPercent);
  }

  // ---------------------------------------------------------------------------------
  // notes

  int KeyForSlot(int slot) {
    return 9 + ScaleTable.Semitones(scaleIndex, slot - rootSlot); // key 9 = the tape's own pitch
  }

  bool Visible(int slot) { return slot >= LowSlot && slot <= topSlot; }

  void NoteOff(int slot) {
    if (!sounding[slot]) return;
    sounding[slot] = false;
    if (voices != null && voices.ready) voices.updateVoices(soundingKey[slot], false);
  }

  void AllNotesOff() {
    for (int i = 0; i < max; i++) NoteOff(i);
  }

  // Play column `step`. A note that is already sounding carries on (tied) when JOIN is
  // on and the cell in this column is lit too; otherwise it is released, and lit cells
  // start a new note.
  void PlayColumn(int step) {
    for (int r = 0; r < max; r++) {
      bool want = Visible(r) && step >= 0 && step < max && cubeStates[step][r];
      if (sounding[r] && !(join && want)) NoteOff(r);
      if (want && !sounding[r]) {
        soundingKey[r] = KeyForSlot(r);
        sounding[r] = true;
        if (voices != null && voices.ready) voices.updateVoices(soundingKey[r], true);
      }
    }
  }

  // ---------------------------------------------------------------------------------
  // transport (as the drum machine)

  public void SetDimensions(int dimX, int dimY) {
    dimensions[0] = dimX;
    dimensions[1] = dimY;
    Vector3 p = stretchNode.localPosition;
    p.x = dimX * -cubeConst - cubeConst * .75f;
    p.y = StretchY(dimY);
    stretchNode.localPosition = p;
    UpdateDimensions();
  }

  int targetStep = 0;
  int selectedStep = 0;
  int curStep = 0;

  public void SelectStep(int s, bool silent = false) {
    selectedStep = targetStep = s;
    if (silent) return;
    PlayColumn(targetStep);
  }

  void SelectStepUpdate() {
    if (targetStep == curStep) return;
    curStep = targetStep;
    highlightDirty = true;
    stepSelect.updatePos(-cubeConst * curStep);
  }

  public void executeNextStep() {
    if (stepSelect.curState == manipObject.manipState.grabbed) return;

    int s = 1;
    bool minicheck = runningUpdated;
    if (runningUpdated) {
      s = 0;
      runningUpdated = false;
    }

    int next = (targetStep + s) % dimensions[0];
    bool looping = loopSwitch == null || loopSwitch.switchVal;
    if (next == 0 && !minicheck && !looping) forcePlay(false); // ONCE: one pass
    else SelectStep(next);
  }

  void Update() {
    SelectStepUpdate();

    dimensions[0] = Mathf.CeilToInt((stretchNode.localPosition.x + cubeConst * .75f) / -cubeConst);
    dimensions[1] = Mathf.CeilToInt((stretchNode.localPosition.y + cubeConst * .75f) / -cubeConst);
    dimensions[0] = Mathf.Clamp(dimensions[0], 1, max);
    dimensions[1] = Mathf.Clamp(dimensions[1], 1, topSlot + 1); // can't go below the lowest note row
    UpdateDimensions();
    UpdateStepSelect();
    PlaceTopHandle(); // follows the corner handle's column position

    if (beatSpeed != beatSlider.switchVal) {
      beatSpeed = beatSlider.switchVal;
      _beatManager.updateBeatNoTriplets(beatSpeed);
    }
    if (swingPercent != swingDial.percent) {
      swingPercent = swingDial.percent;
      _beatManager.updateSwing(swingPercent);
    }

    if (externalPulse != controlInput.signal) {
      externalPulse = controlInput.signal;
      _beatManager.toggleMC(externalPulse == null);
      if (externalPulse != null) forcePlay(false);
    }

    if (linkSwitch != null && link != linkSwitch.switchVal) SetLink(linkSwitch.switchVal);
    if (joinSwitch != null && join != joinSwitch.switchVal) {
      join = joinSwitch.switchVal;
      highlightDirty = true;
    }
    if (voices != null && muteButton != null) voices.muted = muteButton.isHit;

    if (highlightDirty) UpdateHighlight();
  }

  public void forcePlay(bool on) {
    togglePlay(on);
    playButton.phantomHit(on);
  }

  void resetSteps() {
    SelectStep(0, true);
    runningUpdated = true;
  }

  float[] _scratch_playBuffer; // reused audio-thread scratch (see AudioBufferPool)
  private void OnAudioFilterRead(float[] buffer, int channels) {
    if (externalPulse == null) return;

    double dspTime = AudioSettings.dspTime;
    float[] playBuffer = AudioBufferPool.GetCleared(ref _scratch_playBuffer, buffer.Length);
    externalPulse.processBuffer(playBuffer, dspTime, channels);

    for (int i = 0; i < playBuffer.Length; i += channels) {
      if (playBuffer[i] > lastPlaySig[1] && lastPlaySig[1] <= lastPlaySig[0]) {
        _beatManager.beatResetEvent();
        _phase = 0;
        forcePlay(true);
      }
      lastPlaySig[0] = lastPlaySig[1];
      lastPlaySig[1] = playBuffer[i];
    }

    for (int i = 0; i < buffer.Length; i += channels) {
      _phase += _sampleDuration;
      if (_phase > masterControl.instance.measurePeriod) _phase -= masterControl.instance.measurePeriod;
      _beatManager.beatUpdateEvent((float)(_phase / masterControl.instance.measurePeriod));
    }
  }

  void UpdateStepSelect() {
    int s = (int)Mathf.Round(stepSelect.transform.localPosition.x / -cubeConst);
    if (s == selectedStep) return;
    stepSelect.pulse();
    selectedStep = s;
    SelectStep(s);
  }

  bool runningUpdated = false;
  public void togglePlay(bool on) {
    _beatManager.toggle(on);
    if (!on) {
      SelectStep(0, true);
      AllNotesOff();
    } else runningUpdated = true;
  }

  public override void hit(bool on, int ID = -1) {
    togglePlay(on);
  }

  // cells report (column, note row)
  public override void hit(bool on, int IDx, int IDy) {
    cubeStates[IDx][IDy] = on;
    if (!on && IDx == targetStep) NoteOff(IDy); // switched off under the play head
    highlightDirty = true;
  }

  void OnDestroy() {
    Destroy(_beatManager);
  }

  // ---------------------------------------------------------------------------------
  // grid

  float RowY(int slot) { return -cubeConst * (topSlot - slot); }

  Color SlotColor(int slot) {
    return Color.HSVToRGB((float)(max - 1 - slot) / max, .9f, .05f);
  }

  void MakeCube(int col, int slot) {
    Transform t = (Instantiate(touchCubePrefab, Vector3.zero, Quaternion.identity) as GameObject).transform;
    t.parent = transform;
    t.localRotation = Quaternion.identity;
    t.localPosition = new Vector3(-cubeConst * col, RowY(slot), 0);
    t.localScale = Vector3.one;
    cubes[col][slot] = t;
    highlighted.Remove(col * max + slot); // a new cube starts unhighlighted
    t.GetComponent<button>().Setup(col, slot, cubeStates[col][slot], SlotColor(slot));
  }

  void KillCube(int col, int slot) {
    if (cubes[col][slot] == null) return;
    Destroy(cubes[col][slot].gameObject);
    cubes[col][slot] = null;
    highlighted.Remove(col * max + slot);
  }

  void UpdateDimensions() {
    if (dimensions[0] == curDimensions[0] && dimensions[1] == curDimensions[1]) return;

    stretchNode.GetComponent<xyHandle>().pulse();
    // columns
    while (curDimensions[0] < dimensions[0]) {
      for (int s = LowSlot; s <= topSlot; s++) MakeCube(curDimensions[0], s);
      curDimensions[0]++;
    }
    while (curDimensions[0] > dimensions[0]) {
      curDimensions[0]--;
      for (int s = LowSlot; s <= topSlot; s++) KillCube(curDimensions[0], s);
    }
    // rows at the bottom (lower notes)
    while (curDimensions[1] < dimensions[1]) {
      curDimensions[1]++;
      for (int c = 0; c < curDimensions[0]; c++) MakeCube(c, LowSlot);
    }
    while (curDimensions[1] > dimensions[1]) {
      int s = LowSlot;
      for (int c = 0; c < curDimensions[0]; c++) KillCube(c, s);
      NoteOff(s);
      curDimensions[1]--;
    }

    stepSelect.xBounds.x = -cubeConst * (curDimensions[0] - 1);
    stepSelect.updatePos(stepSelect.transform.localPosition.x);
    updateStepSelectVertical();
    PlaceTopHandle();
    dimensionDisplays[0].text = curDimensions[0] + " X " + curDimensions[1];
    highlightDirty = true;
  }

  // Top handle: +1 adds a higher note row above the grid, -1 removes the top row. The
  // device moves up/down by a row so the cells already there stay put in the room.
  public bool ChangeTop(int dir) {
    if (dir > 0 && topSlot >= max - 1) return false;
    if (dir < 0 && curDimensions[1] <= 1) return false;

    if (dir > 0) {
      topSlot++;
      curDimensions[1]++;
      for (int c = 0; c < curDimensions[0]; c++) MakeCube(c, topSlot);
    } else {
      for (int c = 0; c < curDimensions[0]; c++) KillCube(c, topSlot);
      NoteOff(topSlot);
      topSlot--;
      curDimensions[1]--;
    }
    dimensions[1] = curDimensions[1];

    // everything on the grid side moves down (up) a row, the device moves up (down) a row
    for (int c = 0; c < curDimensions[0]; c++) {
      for (int s = LowSlot; s <= topSlot; s++) {
        if (cubes[c][s] != null) cubes[c][s].localPosition = new Vector3(-cubeConst * c, RowY(s), 0);
      }
    }
    Vector3 sp = stretchNode.localPosition;
    sp.y = StretchY(curDimensions[1]);
    stretchNode.localPosition = sp;
    updateStepSelectVertical();
    transform.position += transform.TransformVector(Vector3.up * cubeConst * dir);

    dimensionDisplays[0].text = curDimensions[0] + " X " + curDimensions[1];
    highlightDirty = true;
    return true;
  }

  public static float topHandleY = cubeConst * .75f;

  void PlaceTopHandle() {
    if (topHandle == null) return;
    topHandle.transform.localPosition = new Vector3(stretchNode.localPosition.x + topHandleOffset.x, topHandleOffset.y, stretchNode.localPosition.z + topHandleOffset.z);
  }

  void updateStepSelectVertical() {
    Vector3 sPos = stepSelect.transform.localPosition;
    sPos.y = -cubeConst * (curDimensions[1]);
    stepSelect.transform.localPosition = sPos;
  }

  void ClearGrid() {
    AllNotesOff();
    for (int c = 0; c < max; c++) for (int s = 0; s < max; s++) KillCube(c, s);
    curDimensions[0] = curDimensions[1] = 0;
  }

  // ---------------------------------------------------------------------------------
  // play-head highlight: the cells in the current column, and with JOIN on, the whole
  // joined note (run of lit cells, wrapping round) that a lit cell there belongs to.

  bool highlightDirty = true;
  HashSet<int> highlighted = new HashSet<int>();
  HashSet<int> nextHighlight = new HashSet<int>();

  void UpdateHighlight() {
    highlightDirty = false;
    nextHighlight.Clear();
    int cols = curDimensions[0];
    if (cols > 0 && curStep < cols) {
      for (int s = LowSlot; s <= topSlot; s++) {
        nextHighlight.Add(curStep * max + s);
        if (!join || !cubeStates[curStep][s]) continue;
        for (int i = 1, c = (curStep + 1) % cols; i < cols && cubeStates[c][s]; i++, c = (c + 1) % cols) nextHighlight.Add(c * max + s);
        for (int i = 1, c = (curStep - 1 + cols) % cols; i < cols && cubeStates[c][s]; i++, c = (c - 1 + cols) % cols) nextHighlight.Add(c * max + s);
      }
    }
    foreach (int k in highlighted) if (!nextHighlight.Contains(k)) SetHighlight(k, false);
    foreach (int k in nextHighlight) if (!highlighted.Contains(k)) SetHighlight(k, true);
    HashSet<int> t = highlighted; highlighted = nextHighlight; nextHighlight = t;
  }

  void SetHighlight(int k, bool on) {
    Transform t = cubes[k / max][k % max];
    if (t != null) t.GetComponent<button>().Highlight(on);
  }

  // ---------------------------------------------------------------------------------
  // cell / size readouts (as the drum machine)

  public override void onSelect(bool on, int IDx, int IDy) {
    if (!on || cubes[IDx][IDy] == null) dimensionDisplays[1].gameObject.SetActive(false);
    else {
      dimensionDisplays[1].text = ((IDx + 1) + "X" + (topSlot - IDy + 1)).ToString();
      dimensionDisplays[1].gameObject.SetActive(true);
      Vector3 pos = cubes[IDx][IDy].localPosition;
      pos.z = .021f;
      dimensionDisplays[1].transform.localPosition = pos;
    }
  }

  Coroutine _rowDisplayFadeRoutine;
  public override void onSelect(bool on, int ID = -1) {
    if (_rowDisplayFadeRoutine != null) StopCoroutine(_rowDisplayFadeRoutine);
    if (on) {
      dimensionDisplays[0].GetComponent<Renderer>().material.SetColor("_TintColor", Color.white);
      dimensionDisplays[0].gameObject.SetActive(true);
    } else _rowDisplayFadeRoutine = StartCoroutine(rowDisplayFadeRoutine());
  }

  IEnumerator rowDisplayFadeRoutine() {
    float t = 0;
    while (t < 1) {
      t = Mathf.Clamp01(t + Time.deltaTime);
      dimensionDisplays[0].GetComponent<Renderer>().material.SetColor("_TintColor", Color.Lerp(Color.white, Color.black, t));
      yield return null;
    }
    dimensionDisplays[0].gameObject.SetActive(false);
  }

  // ---------------------------------------------------------------------------------
  // save / load

  public override InstrumentData GetData() {
    ScaleSequencerData data = new ScaleSequencerData();
    data.deviceType = menuItem.deviceType.ScaleSequencer;
    GetTransformData(data);

    data.speedMult = beatSlider.switchVal;
    data.onSwitch = playButton.isHit;
    data.jackInID = controlInput.transform.GetInstanceID();
    data.dimensions = new int[] { curDimensions[0], curDimensions[1] };
    data.topRow = topSlot;
    data.cubeStates = cubeStates;
    data.swing = swingDial.percent;
    data.loop = loopSwitch == null || loopSwitch.switchVal;

    data.join = join;
    data.scaleType = scaleIndex;
    data.link = link;
    data.muted = muteButton != null && muteButton.isHit;
    data.jackOutID = output != null ? output.transform.GetInstanceID() : 0;
    data.sample = new string[] { "", "" };
    if (sampler != null) sampler.getTapeInfo(out data.sample[0], out data.sample[1]);
    data.ADSRdata = new Vector2[3];
    if (adsr != null) for (int i = 0; i < 3; i++) data.ADSRdata[i] = adsr.xyHandles[i].percent;

    return data;
  }

  public override void Load(InstrumentData d) {
    ScaleSequencerData data = d as ScaleSequencerData;
    base.Load(data);

    playButton.startToggled = data.onSwitch;
    controlInput.ID = data.jackInID;

    for (int i = 0; i < data.cubeStates.Length && i < max; i++) {
      for (int i2 = 0; i2 < data.cubeStates[i].Length && i2 < max; i2++) cubeStates[i][i2] = data.cubeStates[i][i2];
    }
    ClearGrid();
    topSlot = Mathf.Clamp(data.topRow, 0, max - 1);
    SetDimensions(Mathf.Clamp(data.dimensions[0], 1, max), Mathf.Clamp(data.dimensions[1], 1, topSlot + 1));
    for (int c = 0; c < max; c++) {
      for (int s = 0; s < max; s++) {
        if (cubeStates[c][s] && cubes[c][s] != null) cubes[c][s].GetComponent<button>().keyHit(true);
      }
    }

    beatSlider.setVal(data.speedMult);
    swingDial.setPercent(data.swing);
    if (loopSwitch != null) loopSwitch.setSwitch(data.loop);
    loopLoaded = true;

    if (joinSwitch != null) joinSwitch.setSwitch(data.join);
    SetScale(data.scaleType);
    if (linkSwitch != null) linkSwitch.setSwitch(data.link);
    link = data.link;
    if (link) sharedScale = scaleIndex; // the patch's linked devices were saved on the same scale
    if (muteButton != null) muteButton.startToggled = data.muted;
    if (output != null) output.ID = data.jackOutID;
    if (sampler != null && data.sample != null && data.sample.Length == 2) sampler.SetSample(data.sample[0], data.sample[1]);
    if (adsr != null && data.ADSRdata != null && data.ADSRdata.Length == 3) {
      for (int i = 0; i < 3; i++) adsr.xyHandles[i].setPercent(data.ADSRdata[i]);
      adsr.setDefaults = false;
    }
    highlightDirty = true;
  }
}

public class ScaleSequencerData : InstrumentData {
  public bool onSwitch;
  public int jackInID;
  public int[] dimensions;      // columns, rows shown
  public int topRow = scaleSequencerDeviceInterface.rootSlot + 7; // note row (0-31) at the top of the grid
  public bool[][] cubeStates;   // [column][note row 0-31]
  public int speedMult;
  public float swing;
  public bool loop = false;

  public bool join = true;
  public int scaleType = 0; // not "scale": InstrumentData.scale is the device's size, and a second
                            // field of that name breaks XmlSerializer for every device type
  public bool link = false;
  public bool muted;
  public int jackOutID;
  public string[] sample = new string[] { "", "" };
  public Vector2[] ADSRdata;
}

// The firmware's scales, as semitone steps within an octave.
public static class ScaleTable {
  public static readonly string[] names = {
    "MAJOR", "MINOR", "CHROMATIC", "BLUES", "BLUES DIM", "FULL MINOR", "HARM MAJOR", "HARM MINOR",
    "JAZZ MINOR", "HAWAIIAN", "ORIENTAL", "MAJOR-MINOR", "GENUS CHROM", "DORIAN", "INDIAN", "LOCRIAN",
    "LYDIAN", "MELODIC MIN", "MIXOLYDIAN", "PENTATONIC", "PHRYGIAN", "TURKISH", "WHOLE-HALF", "WHOLE TONE",
    "SPANISH", "PELOG", "KUMOI", "IWATO", "IN-SEN", "HIROJOSHI", "HUNGARIAN MIN", "BHAIRAV",
    "SUPER LOCRIAN", "MINOR PENTA"
  };

  static readonly int[][] steps = {
    new int[] { 0, 2, 4, 5, 7, 9, 11 },           // major
    new int[] { 0, 2, 3, 5, 7, 8, 10 },           // minor
    new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, // chromatic
    new int[] { 0, 3, 5, 6, 7, 10 },              // blues
    new int[] { 0, 1, 3, 4, 6, 7, 9, 10 },        // blues diminished
    new int[] { 0, 2, 3, 5, 7, 8, 9, 10, 11 },    // full minor
    new int[] { 0, 2, 4, 5, 7, 8, 11 },           // harmonic major
    new int[] { 0, 2, 3, 5, 7, 8, 11 },           // harmonic minor
    new int[] { 0, 2, 3, 5, 7, 9, 11 },           // jazz minor
    new int[] { 0, 2, 3, 7, 9, 11 },              // hawaiian
    new int[] { 0, 1, 4, 5, 6, 9, 10 },           // oriental
    new int[] { 0, 2, 4, 5, 6, 7, 10 },           // major-minor
    new int[] { 0, 1, 3, 4, 5, 7, 8, 9, 11 },     // genus chromaticum
    new int[] { 0, 2, 3, 5, 7, 9, 10 },           // dorian
    new int[] { 0, 1, 1, 4, 5, 8, 10 },           // indian (as in the firmware)
    new int[] { 0, 1, 3, 5, 6, 8, 10 },           // locrian
    new int[] { 0, 2, 4, 6, 7, 9, 10 },           // lydian (as in the firmware)
    new int[] { 0, 2, 3, 5, 7, 8, 9, 10, 11 },    // melodic minor
    new int[] { 0, 2, 4, 5, 7, 9, 10 },           // mixolydian
    new int[] { 0, 2, 4, 7, 9 },                  // pentatonic
    new int[] { 0, 1, 3, 5, 7, 8, 10 },           // phrygian
    new int[] { 0, 1, 3, 5, 7, 10, 11 },          // turkish
    new int[] { 0, 2, 3, 4, 5, 7, 9, 10, 11 },    // whole-half
    new int[] { 0, 2, 4, 6, 8, 10 },              // whole tone
    new int[] { 0, 1, 3, 4, 5, 6, 8, 10 },        // spanish
    new int[] { 0, 1, 3, 4, 7, 8 },               // pelog
    new int[] { 0, 2, 3, 7, 9 },                  // kumoi
    new int[] { 0, 1, 5, 6, 10 },                 // iwato
    new int[] { 0, 1, 5, 7, 10 },                 // in-sen
    new int[] { 0, 2, 3, 7, 8 },                  // hirojoshi
    new int[] { 0, 2, 3, 6, 7, 8, 11 },           // hungarian minor
    new int[] { 0, 1, 4, 5, 7, 8, 11 },           // bhairav
    new int[] { 0, 1, 3, 4, 6, 8, 10 },           // super locrian
    new int[] { 0, 3, 5, 7, 10 },                 // minor pentatonic
  };

  public static int Count { get { return steps.Length; } }
  public static int Length(int scale) { return steps[scale].Length; }

  // semitones from the root for a scale degree (negative degrees go below the root)
  public static int Semitones(int scale, int degree) {
    int[] s = steps[Mathf.Clamp(scale, 0, steps.Length - 1)];
    int oct = Mathf.FloorToInt((float)degree / s.Length);
    int idx = degree - oct * s.Length;
    return oct * 12 + s[idx];
  }
}
