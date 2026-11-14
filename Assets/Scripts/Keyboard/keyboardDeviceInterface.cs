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

public class keyboardDeviceInterface : deviceInterface {
  public timelineComponentInterface _timeline;
  public midiComponentInterface _midiIn, _midiOut;
  public GameObject whiteKeyPrefab, blackKeyPrefab;
  public omniJack output, freqoutput, ampoutput;
  public button muteButton, midiInButton, midiOutButton, sequenceButton;

  public midiOutOfRange midiLow, midiHigh;

  public speaker _speaker;
  int keyCount = 25;
  key[] keys;

  // Resizable range (2026-11): drag the handle at the high end to add octaves (higher
  // notes). 2..5 octaves; with the octave switch up the top is 4 octaves so the highest
  // key never goes past C8, like a piano.
  public const int minOctaves = 2;
  int octaves = 2;
  const float keySeparation = .05f;
  const float octaveWidth = 7 * keySeparation;
  xHandle sizeHandle;
  float sizeHandleBaseX;
  Transform[] highEndParts;   // controls at the high end, moved out as keys are added
  Vector3[] highEndBase;
  StretchableHousing[] housing;   // frame / grab nodes, lengthened

  // Octave control: the 5-notch slider (-2..+2, like the xyloroll) once the prefab has
  // one (SoundStage > Give keyboard an octave slider), else the old on/off switch (0/+1).
  public sliderNotched octaveSlider;
  int OctaveShift {
    get {
      if (octaveSlider != null) return octaveSlider.switchVal - 2;
      return octaveToggle != null && octaveToggle.switchVal ? 1 : 0;
    }
  }

  // top key (MIDI 48 + 12*shift + 12*octaves) stays at or below C8 (108)
  int MaxOctaves { get { return Mathf.Clamp(5 - OctaveShift, minOctaves, 5); } }

  adsrInterface _adsrInterface;
  basicSwitch octaveToggle;

  keyFrequencySignalGenerator freqSignal;
  adsrSignalGenerator adsrSignal;
  int curKey;

  keyState[] keyStates = new keyState[25];

  public override void Awake() {
    base.Awake();

    curKey = -1;

    _adsrInterface = GetComponentInChildren<adsrInterface>();
    octaveToggle = GetComponentInChildren<basicSwitch>();

    freqSignal = GetComponent<keyFrequencySignalGenerator>();
    adsrSignal = GetComponent<adsrSignalGenerator>();

    freqoutput.homesignal = freqSignal;
    ampoutput.homesignal = adsrSignal;

    adsrSignal.durations = _adsrInterface.durations;
    adsrSignal.volumes = _adsrInterface.volumes;

    CaptureLayout();
    keys = new key[0];
    SetOctaves(minOctaves);
    CreateSizeHandle();
  }

  void CaptureLayout() {
    var parts = new System.Collections.Generic.List<Transform>();
    foreach (Transform c in transform) {
      if (c.localPosition.x < -0.45f) parts.Add(c); // MUTE, output jack, speaker, sequencer toggle, octave switch...
    }
    highEndParts = parts.ToArray();
    highEndBase = new Vector3[highEndParts.Length];
    for (int i = 0; i < highEndParts.Length; i++) highEndBase[i] = highEndParts[i].localPosition;

    var hs = new System.Collections.Generic.List<StretchableHousing>();
    foreach (string n in new string[] { "keyboardSeating", "stretchNode" }) {
      Transform t = transform.Find(n);
      if (t != null) hs.Add(new StretchableHousing(t));
    }
    housing = hs.ToArray();
  }

  void ApplyLayout() {
    float dx = -(octaves - minOctaves) * octaveWidth;
    for (int i = 0; i < highEndParts.Length; i++) {
      if (highEndParts[i] == null) continue;
      Vector3 p = highEndBase[i]; p.x += dx; highEndParts[i].localPosition = p;
    }
    for (int i = 0; i < housing.Length; i++) housing[i].SetExtra(-dx);
  }

  void OnDestroy() {
    if (housing != null) foreach (StretchableHousing h in housing) h.Destroy();
  }

  void CreateSizeHandle() {
    Transform seat = transform.Find("keyboardSeating");
    Renderer seatRend = seat != null ? seat.GetComponent<Renderer>() : null;
    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
    g.SetActive(false);
    g.name = "sizeHandle";
    g.layer = 9;
    g.transform.SetParent(transform, false);
    float leftmost = 0;
    foreach (Vector3 v in highEndBase) leftmost = Mathf.Min(leftmost, v.x);
    sizeHandleBaseX = leftmost - 0.045f;
    g.transform.localPosition = new Vector3(sizeHandleBaseX, -0.01f, -0.04f);
    g.transform.localScale = new Vector3(0.02f, 0.03f, 0.12f);
    Renderer r = g.GetComponent<Renderer>();
    if (seatRend != null) r.sharedMaterial = seatRend.sharedMaterial;
    sizeHandle = g.AddComponent<xHandle>();
    sizeHandle.onMat = muteButton != null ? muteButton.onMat : r.sharedMaterial;
    sizeHandle.rend = r;
    g.SetActive(true);
    UpdateHandleBounds();
  }

  void UpdateHandleBounds() {
    if (sizeHandle == null) return;
    sizeHandle.xBounds = new Vector2(sizeHandleBaseX - (MaxOctaves - minOctaves) * octaveWidth - 0.02f, sizeHandleBaseX + 0.02f);
  }

  void UpdateSizeHandle() {
    if (sizeHandle == null) return;
    UpdateHandleBounds();
    float target = sizeHandleBaseX - (octaves - minOctaves) * octaveWidth;
    if (sizeHandle.curState == manipObject.manipState.grabbed) {
      int want = minOctaves + Mathf.RoundToInt((sizeHandleBaseX - sizeHandle.transform.localPosition.x) / octaveWidth);
      want = Mathf.Clamp(want, minOctaves, MaxOctaves);
      if (want != octaves) { SetOctaves(want); sizeHandle.pulse(); }
    } else {
      if (octaves > MaxOctaves) SetOctaves(MaxOctaves); // octave control went up
      Vector3 p = sizeHandle.transform.localPosition;
      if (Mathf.Abs(p.x - target) > 0.0005f) { p.x = target; sizeHandle.transform.localPosition = p; }
    }
  }

  // rebuild the keys for n octaves (12n + 1 keys)
  void SetOctaves(int n) {
    n = Mathf.Clamp(n, minOctaves, 5);
    int newCount = 12 * n + 1;

    // release anything held on keys that will disappear
    if (curKey >= newCount) {
      adsrSignal.hit(false);
      curKey = -1;
    }
    for (int i = newCount; i < keys.Length; i++) if (keys[i] != null) Destroy(keys[i].gameObject);

    key[] old = keys;
    keys = new key[newCount];
    for (int i = 0; i < newCount; i++) keys[i] = i < old.Length ? old[i] : null;

    keyState[] oldStates = keyStates;
    keyStates = new keyState[newCount];
    for (int i = 0; i < newCount; i++) keyStates[i] = i < oldStates.Length ? oldStates[i] : new keyState(false);

    octaves = n;
    keyCount = newCount;
    SpawnKeys();
    ApplyLayout();
    if (_timeline != null) {
      if (_timeline.IsSetUp) _timeline.updateTrackCount(keyCount);
      else _timeline.setStartTracks(keyCount);
    }
  }

  void SpawnKeys() {
    float separation = keySeparation;
    int whiteCount = 0;
    for (int i = 0; i < keyCount; i++) {
      bool black = i % 12 == 1 || i % 12 == 3 || i % 12 == 6 || i % 12 == 8 || i % 12 == 10;
      if (keys[i] != null) { if (!black) whiteCount++; continue; } // already there
      GameObject g;
      if (i % 12 == 1 || i % 12 == 3 || i % 12 == 6 || i % 12 == 8 || i % 12 == 10) {
        g = Instantiate(blackKeyPrefab, transform, false) as GameObject;
        g.transform.localPosition = new Vector3(-separation * whiteCount + separation / 2 + .15f, .03f, -.025f);
      } else {
        g = Instantiate(whiteKeyPrefab, transform, false) as GameObject;
        g.transform.localPosition = new Vector3(-separation * whiteCount + .15f, -.007f, .005f);
        whiteCount++;
      }
      keys[i] = g.GetComponent<key>();
      keys[i].keyValue = i;
      keys[i].isKeyboard = true;
      keys[i].sticky = false;
    }
  }

  bool muted = false;
  public void toggleMute(bool on) {
    muted = on;
    _speaker.volume = muted ? 0 : 1;
  }

  public override void onTimelineEvent(int track, bool on) {
    asynchKeyHit(on, track, keyInput.seq);
  }

  bool midiLowDesired = false;
  bool midiHighDesired = false;

  public override void OnMidiNote(int channel, bool on, int pitch) {
    int ID = pitch - 48;
    if (ID < 0) {
      if (on) midiLowDesired = true;
    } else if (ID > keyCount - 1) {
      if (on) midiHighDesired = true;
    } else {
      asynchKeyHit(on, ID, keyInput.midi);
    }
  }

  public void asynchKeyHit(bool on, int ID, keyInput k) {
    if (ID < 0 || ID >= keyCount || ID >= keyStates.Length) return; // e.g. sequencer notes above a shrunk range
    if (k == keyInput.midi) keyStates[ID].midiState = on;
    else if (k == keyInput.seq) keyStates[ID].seqState = on;
    else if (k == keyInput.touch) keyStates[ID].touchState = on;

    if (keyStates[ID].nonSeqStateChange()) {
      keyStates[ID].currentNonSeqState = keyStates[ID].getNonSeqState();
      _timeline.onTimelineEvent(ID, keyStates[ID].currentNonSeqState);
    }

    if (keyStates[ID].stateChange()) {
      on = keyStates[ID].currentState = keyStates[ID].getState();
      keys[ID].phantomHit(on);
      keyHitEvent(on, ID);
    }
  }

  void keyHitEvent(bool on, int ID) {
    if (on) {
      if (curKey != ID) {
        int prev = curKey;
        curKey = ID;

        if (prev != -1) {
          adsrSignal.hit(false);
          if (_midiOut != null) _midiOut.OutputNote(false, prev);
        }

        if (_midiOut != null) _midiOut.OutputNote(on, ID);
        freqSignal.UpdateKey(curKey);
        adsrSignal.hit(true);
      }
    } else {
      if (curKey == ID) {
        _midiOut.OutputNote(false, ID);
        adsrSignal.hit(false);
        curKey = -1;
      }
    }
  }

  void toggleMIDIin(bool on) {
    _midiIn.gameObject.SetActive(on);
  }

  void toggleMIDIout(bool on) {
    _midiOut.gameObject.SetActive(on);
  }

  void toggleSequencer(bool on) {
    _timeline.gameObject.SetActive(on);
  }

  public override void hit(bool on, int ID = -1) {
    if (ID == -1) {
      toggleMute(on);
    } else if (ID == -2) {
      toggleMIDIin(on);
    } else if (ID == -3) {
      toggleMIDIout(on);
    } else if (ID == -4) {
      toggleSequencer(on);
    } else {
      asynchKeyHit(on, ID, keyInput.touch);
    }
  }

  void Update() {
    UpdateSizeHandle();
    freqSignal.octave = OctaveShift;

    if (midiLowDesired) {
      midiLowDesired = false;
      midiLow.gameObject.SetActive(true);
      midiLow.Activate();
    }

    if (midiHighDesired) {
      midiHighDesired = false;
      midiHigh.gameObject.SetActive(true);
      midiHigh.Activate();
    }
  }

  public override InstrumentData GetData() {
    KeyboardData data = new KeyboardData();
    data.deviceType = menuItem.deviceType.Keyboard;
    GetTransformData(data);
    data.muted = muted;
    data.octaveSwitch = OctaveShift > 0;
    data.octaveSetting = octaveSlider != null ? octaveSlider.switchVal : (OctaveShift > 0 ? 3 : 2);
    data.octaves = octaves;
    data.ADSRdata = new Vector2[3];
    for (int i = 0; i < 3; i++) {
      data.ADSRdata[i] = _adsrInterface.xyHandles[i].percent;
    }

    data.jackOutID = output.transform.GetInstanceID();
    data.freqOutID = freqoutput.transform.GetInstanceID();
    data.ampOutID = ampoutput.transform.GetInstanceID();

    data.midiInConnection = _midiIn.connectedDevice;
    data.midiOutConnection = _midiOut.connectedDevice;
    data.sequencerEnabled = _timeline.gameObject.activeSelf;

    data.timelinePresent = true;
    data.timelineData = _timeline.GetTimelineData();
    data.timelineHeight = _timeline.heightHandle.transform.localPosition.y;
    List<timelineEvent.eventData> tempevents = new List<timelineEvent.eventData>();
    for (int i = 0; i < _timeline._tlEvents.Count; i++) {
      if (_timeline._tlEvents[i] != null) tempevents.Add(_timeline._tlEvents[i].getEventInfo());
    }
    data.timelineEvents = tempevents.ToArray();

    return data;
  }

  public override void Load(InstrumentData d) {
    KeyboardData data = d as KeyboardData;
    base.Load(data);

    output.ID = data.jackOutID;
    freqoutput.ID = data.freqOutID;
    ampoutput.ID = data.ampOutID;

    for (int i = 0; i < 3; i++) _adsrInterface.xyHandles[i].setPercent(data.ADSRdata[i]);
    _adsrInterface.setDefaults = false;

    muteButton.startToggled = data.muted;
    if (octaveToggle != null) octaveToggle.setSwitch(data.octaveSwitch);
    if (octaveSlider != null) {
      int v = data.octaveSetting >= 0 ? data.octaveSetting : (data.octaveSwitch ? 3 : 2); // older saves: switch only
      octaveSlider.setVal(Mathf.Clamp(v, 0, octaveSlider.notchCount - 1));
    }
    if (data.octaves > minOctaves) SetOctaves(Mathf.Min(data.octaves, MaxOctaves)); // before the timeline is set up

    if (data.midiInConnection != null & data.midiInConnection != "") {
      midiInButton.startToggled = true;
      _midiIn.ConnectByName(data.midiInConnection);
    }
    if (data.midiOutConnection != null & data.midiOutConnection != "") {
      midiOutButton.startToggled = true;
      _midiOut.ConnectByName(data.midiOutConnection);
    }

    sequenceButton.startToggled = data.sequencerEnabled;

    if (data.timelinePresent) {
      _timeline.SetTimelineData(data.timelineData);

      Vector3 pos = _timeline.heightHandle.transform.localPosition;
      pos.y = data.timelineHeight;
      _timeline.heightHandle.transform.localPosition = pos;
      _timeline.setStartHeight(data.timelineHeight);

      for (int i = 0; i < data.timelineEvents.Length; i++) {
        _timeline.SpawnTimelineEvent(data.timelineEvents[i].track, data.timelineEvents[i].in_out);
      }
    }
  }

  public enum keyInput {
    seq,
    midi,
    touch
  }

  struct keyState {
    public bool seqState;
    public bool midiState;
    public bool touchState;

    public bool currentState;
    public bool currentNonSeqState;

    public keyState(bool on) {
      currentNonSeqState = currentState = seqState = midiState = touchState = on;
    }

    public bool getState() {
      return seqState || midiState || touchState;
    }

    public bool getNonSeqState() {
      return midiState || touchState;
    }

    public bool stateChange() {
      return getState() != currentState;
    }

    public bool nonSeqStateChange() {
      return getNonSeqState() != currentNonSeqState;
    }
  };
}


public class KeyboardData : InstrumentData {
  public Vector2[] ADSRdata;
  public bool muted;
  public bool octaveSwitch;
  public int octaveSetting = -1; // octave slider notch 0-4 (2 = middle); -1 in older saves
  public int octaves = 2; // keyboard range in octaves (2-5); older saves are 2
  public bool sequencerEnabled;
  public int jackOutID;
  public int freqOutID;
  public int ampOutID;
  public string midiInConnection;
  public string midiOutConnection;

  public bool timelinePresent;
  public TimelineComponentData timelineData;
  public timelineEvent.eventData[] timelineEvents;
  public float timelineHeight;
}