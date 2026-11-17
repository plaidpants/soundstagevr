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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class midiComponentInterface : componentInterface {
  deviceInterface _deviceInterface;

  public bool input = true;
  public string connectedDevice = "";
  int channel = 0;

  public GameObject midiPanelPrefab, statusLight;
  public TextMesh statusText;
  public sliderNotched channelSlider;

  midiPanel mainMidiPanel;
  List<midiPanel> midiPanelList = new List<midiPanel>();

  Material statusLightMat;
  Color connectedColor = new Color32(0x32, 0xA3, 0x23, 0xFF); //A32323FF
  Color disconnectedColor = new Color32(0xA3, 0x23, 0x23, 0xFF); //A32323FF
  Color connectingColor = new Color32(0x9A, 0xA3, 0x23, 0xFF); //9AA323FF

  MIDIdevice curMIDIdevice;

  void Awake() {
    _deviceInterface = GetComponentInParent<deviceInterface>();

    statusLightMat = statusLight.GetComponent<Renderer>().material;
    statusLightMat.SetFloat("_EmissionGain", .3f);
    statusLightMat.SetColor("_TintColor", connectingColor);

    createMainMidiPanel();
  }

  void createMainMidiPanel() {
    mainMidiPanel = (Instantiate(midiPanelPrefab, transform, false) as GameObject).GetComponent<midiPanel>();
    mainMidiPanel.transform.localPosition = new Vector3(0, 0, .015f);
    mainMidiPanel.transform.localRotation = Quaternion.identity;
    mainMidiPanel.transform.localScale = new Vector3(0.15f, .02f, 1.25f);
    mainMidiPanel.label.text = input ? "[SELECT MIDI INPUT DEVICE]" : "[SELECT MIDI OUTPUT DEVICE]";
    mainMidiPanel.buttonID = -1;
  }

  public void ConnectByName(string s) {
    remoteOwner = null;
    // In multiplayer the patch says which computer a connection belongs to
    // ("device|COMPUTER", see NetMidi.Tag): another computer's connection is left to it,
    // even if this one has a device with the same name, so two PCs with the same USB
    // interface don't both play.
    NetMidi.Untag(s, out string devName, out string owner);
    if (owner != null && owner != NetMidi.Me && NetSync.InSession) {
      remoteOwner = owner;
      connectedDevice = s; // keep it as it is, so every copy of the patch says the same
      listopen = false;
      CloseList();
      ShowRemoteStatus();
      return;
    }
    string saved = s;
    s = devName;
    MIDImaster.instance.RefreshDevices(input);
    int ID = MIDImaster.instance.GetIDbyName(input, s);
    if (ID == -1) {
      // Not on this machine. In multiplayer it's usually another machine's device (the
      // PC's MIDI keyboard, saved in the shared patch): show it as connected there, and
      // keep the name, so this copy of the patch says the same as the PC's (otherwise
      // the copies differ, get shared back and forth, and the PC's connection is lost).
      if (NetMidi.IsRemote(input, s, out string remote)) {
        NetShowRemote(s + " (" + remote + ")", true, s);
      } else {
        Debug.Log("[MIDI] " + s + " isn't on this machine (another player's?)");
        connectedDevice = saved; // (unplugged here: keep the saved connection as it was)
        mainMidiPanel.label.text = s;
      }
    } else {
      mainMidiPanel.label.text = input ? MIDImaster.instance.inputDevices[ID].name : MIDImaster.instance.outputDevices[ID].name;
      listopen = false;
      TryConnect(ID, s);
      CloseList();
    }
  }

  void OnDestroy() {
    // (try: when quitting, MIDImaster is closing inputs on another thread at the same time)
    if (curMIDIdevice != null && MIDImaster.instance != null) {
      try { MIDImaster.instance.Disconnect(curMIDIdevice, input, this); } catch (System.Exception) { }
    }
  }

  Coroutine _textKillRoutine;
  IEnumerator TextKillRoutine() {
    yield return new WaitForSeconds(3);
    statusText.text = "";
    statusText.gameObject.SetActive(false);
  }

  void TryConnect(int index, string name) {

    string s = "Connecting";
    MIDIdevice lastMidiDevice = curMIDIdevice;
    curMIDIdevice = MIDImaster.instance.Connect(this, index, input);
    statusText.gameObject.SetActive(true);
    if (lastMidiDevice != curMIDIdevice && lastMidiDevice != null) {
      MIDImaster.instance.Disconnect(lastMidiDevice, input, this);
    }

    NetMidi.LocalConnected(this, name, curMIDIdevice != null); // multiplayer: others show it too
    Debug.Log("[MIDI] " + (curMIDIdevice != null ? "connected " : "couldn't connect ") + name + (input ? " (in)" : " (out)") + " to " + (_deviceInterface != null ? _deviceInterface.name : name));
    if (curMIDIdevice != null) {
      connectedDevice = NetMidi.Tag(name);
      remoteOwner = null;
      s = "CONNECTED!";
      statusLightMat.SetColor("_TintColor", connectedColor);
      statusText.text = s;

    } else {
      connectedDevice = "";
      s = "Connection failed\nDevice may be connected to other software.";
      statusLightMat.SetColor("_TintColor", disconnectedColor);
      statusText.text = s;
    }

    if (gameObject.activeSelf) {
      if (_textKillRoutine != null) StopCoroutine(_textKillRoutine);
      _textKillRoutine = StartCoroutine(TextKillRoutine());
    }
  }

  void OnDisable() {
    panelOn = false;
    while (midiQueue.TryDequeue(out _)) { } // MIDI in switched off: drop what's waiting
    StopAllCoroutines();
    statusText.gameObject.SetActive(false);
  }

  void toggleList() {
    listopen = !listopen;
    if (listopen) OpenList();
    else CloseList();
  }

  void Update() {
    if (channelSlider != null) channel = channelSlider.switchVal;
    PlayQueuedMidi();
    if (remoteOwner != null && Time.unscaledTime - remoteChecked > 2f) ShowRemoteStatus();
  }

  // another computer's connection: show whether that computer is in the session now
  string remoteOwner;
  float remoteChecked;
  void ShowRemoteStatus() {
    remoteChecked = Time.unscaledTime;
    NetMidi.Untag(connectedDevice, out string devName, out string owner);
    bool online = NetMidi.OwnerOnline(owner);
    mainMidiPanel.label.text = devName + " (" + owner + ")" + (online ? "" : " - OFFLINE");
    statusLightMat.SetColor("_TintColor", online ? connectedColor : disconnectedColor);
  }

  bool listopen = false;
  public bool ListOpen { get { return listopen; } } // (the test harness)
  public override void hit(bool on, int ID = -1) {
    if (!on) return;
    if (ID == -1) toggleList();
    else if (ID >= RemoteBase) {
      int k = ID - RemoteBase;
      listopen = false;
      if (k < remoteShown.Count) {
        mainMidiPanel.label.text = remoteShown[k].name + " (" + remoteShown[k].owner + ")";
        statusLightMat.SetColor("_TintColor", connectingColor);
        NetMidi.RequestRemote(this, remoteShown[k]);
      }
      CloseList();
    }

    else {
      mainMidiPanel.label.text = input ? MIDImaster.instance.inputDevices[ID].name : MIDImaster.instance.outputDevices[ID].name;

      listopen = false;
      TryConnect(ID, mainMidiPanel.label.text);
      CloseList();
    }
  }

  void CloseList() {
    for (int i = 0; i < midiPanelList.Count; i++) {
      Destroy(midiPanelList[i].gameObject);
    }

    midiPanelList.Clear();
  }

  // ---- multiplayer (Net/NetMidi.cs): MIDI inputs on another player's machine (the PC) ----
  // listed after this machine's own, as "name (PC)"; picking one asks that machine to
  // connect it here - it then plays this device for everyone.
  System.Collections.Generic.List<NetMidi.RemoteInput> remoteShown = new System.Collections.Generic.List<NetMidi.RemoteInput>();
  const int RemoteBase = 1000;

  public void NetShowRemote(string label, bool connected, string deviceName = null) {
    remoteOwner = null;
    if (connected && deviceName != null) {
      NetMidi.Untag(deviceName, out _, out string owner);
      remoteOwner = owner;
    }
    mainMidiPanel.label.text = label;
    if (deviceName != null) connectedDevice = connected ? deviceName : "";
    statusLightMat.SetColor("_TintColor", connected ? connectedColor : disconnectedColor);
  }

  void OpenList() {
    MIDImaster.instance.RefreshDevices(input);

    int count = input ? MIDImaster.instance.inputDevices.Count : MIDImaster.instance.outputDevices.Count;
    remoteShown.Clear();
    remoteShown.AddRange(NetMidi.RemoteDevices(input));
    for (int k = 0; k < remoteShown.Count; k++) {
      midiPanel m = (Instantiate(midiPanelPrefab, transform, false) as GameObject).GetComponent<midiPanel>();
      m.transform.localPosition = new Vector3(0, (count + k) * .03f + .045f, .015f);
      m.transform.localRotation = Quaternion.identity;
      m.transform.localScale = new Vector3(0.15f, .02f, 1.25f);
      m.label.text = remoteShown[k].name + " (" + remoteShown[k].owner + ")";
      m.buttonID = RemoteBase + k;
      midiPanelList.Add(m);
    }
    if (remoteShown.Count > 0 && count == 0) return; // nothing local, but other players' inputs

    for (int i = 0; i < count; i++) {
      midiPanel m = (Instantiate(midiPanelPrefab, transform, false) as GameObject).GetComponent<midiPanel>();
      m.transform.localPosition = new Vector3(0, i * .03f + .045f, .015f);
      m.transform.localRotation = Quaternion.identity;
      m.transform.localScale = new Vector3(0.15f, .02f, 1.25f);
      m.label.text = input ? MIDImaster.instance.inputDevices[i].name : MIDImaster.instance.outputDevices[i].name;

      m.buttonID = i;
      midiPanelList.Add(m);
    }

    if (count == 0) {
      statusText.gameObject.SetActive(true);
      statusText.text = "NO MIDI DEVICES FOUND";
      if (gameObject.activeSelf) {
        if (_textKillRoutine != null) StopCoroutine(_textKillRoutine);
        _textKillRoutine = StartCoroutine(TextKillRoutine());
      }
      listopen = false;
      CloseList();
    }
  }

  // The MIDI library calls these on its own thread. Playing a device from there (Unity
  // calls, the keyboard's timeline...) threw, the library reported "DeviceException:
  // Error in the application", and input stopped after the first note. They're queued
  // now and played on the main thread in Update (2026-11).
  struct MidiIn { public byte kind, ch, a; public int b; } // kind 0 note on, 1 note off, 2 CC
  readonly System.Collections.Concurrent.ConcurrentQueue<MidiIn> midiQueue = new System.Collections.Concurrent.ConcurrentQueue<MidiIn>();

  public void InputNoteOn(Midi.NoteOnMessage msg) {
    if (!panelOn) return;
    midiQueue.Enqueue(new MidiIn { kind = (byte)(msg.Velocity != 0 ? 0 : 1), ch = (byte)((int)msg.Channel + 1), a = (byte)(int)msg.Pitch });
  }

  // raw short message from WinMidiIn (on the Windows MIDI callback thread: only queue it)
  public void RawMidiIn(int status, int d1, int d2) {
    int type = status & 0xF0;
    byte ch = (byte)((status & 0x0F) + 1);
    // (not switched off with the panel, unlike MIDI out: the panel's open/closed state
    // isn't shared, and an input another player connected here must play while this
    // machine's copy of the panel is closed)
    if (type == 0x90 && d2 > 0) {
      midiQueue.Enqueue(new MidiIn { kind = 0, ch = ch, a = (byte)d1 });
    } else if (type == 0x80 || type == 0x90) {
      midiQueue.Enqueue(new MidiIn { kind = 1, ch = ch, a = (byte)d1 });
    } else if (type == 0xB0) {
      midiQueue.Enqueue(new MidiIn { kind = 2, ch = ch, a = (byte)d1, b = d2 });
    }
  }

  public void InputNoteOff(Midi.NoteOffMessage msg) {
    midiQueue.Enqueue(new MidiIn { kind = 1, ch = (byte)((int)msg.Channel + 1), a = (byte)(int)msg.Pitch });
  }

  public void InputControlChange(Midi.ControlChangeMessage msg) {
    if (!panelOn) return;
    midiQueue.Enqueue(new MidiIn { kind = 2, ch = (byte)((int)msg.Channel + 1), a = (byte)(int)msg.Control, b = msg.Value });
  }

  // (also called by MIDImaster.Update: this component's own Update doesn't run while
  // its panel is closed)
  float lastInLog = -10;
  public void PlayQueuedMidi() {
    while (midiQueue.TryDequeue(out MidiIn m)) {
      if (Time.unscaledTime - lastInLog > 2f) {
        lastInLog = Time.unscaledTime;
        Debug.Log("[MIDI] in from " + connectedDevice + ": " + (m.kind == 2 ? "CC " + m.a + "=" + m.b : "note " + m.a + (m.kind == 0 ? " on" : " off")) + " ch " + m.ch + " -> " + (_deviceInterface != null ? _deviceInterface.name : "?") + (channel != 0 && channel != m.ch ? " (ignored: listening on channel " + channel + ")" : ""));
      }
      if (channel != 0 && channel != m.ch) continue;
      try {
        if (m.kind == 2) {
          _deviceInterface.OnMidiCC(m.ch, m.a, m.b);
          NetMidi.CC(_deviceInterface, m.ch, m.a, m.b); // multiplayer
        } else {
          _deviceInterface.OnMidiNote(m.ch, m.kind == 0, m.a);
          NetMidi.Note(_deviceInterface, m.ch, m.kind == 0, m.a); // multiplayer
        }
      } catch (System.Exception e) { Debug.LogWarning("[MIDI] " + e.Message); }
    }
  }

  // Switched off (the device's MIDI OUT / IN panel hidden): it stays connected - so it
  // comes back as it was - but sends and plays nothing. Before, a hidden MIDI OUT kept
  // sending. (A flag, not activeInHierarchy: notes can come from the audio thread.)
  volatile bool panelOn = true;
  void OnEnable() { panelOn = true; }

  public void OutputNote(bool on, int ID, bool add48 = true) {
    if (input || curMIDIdevice == null) return;
    if (!panelOn && on) return; // (note-offs still go, so nothing is left hanging)

    if (add48) ID += 48;
    MIDImaster.instance.outputNote(curMIDIdevice, on, ID, channel);
  }

  public void OutputCC(int val, int ID) {
    if (input || curMIDIdevice == null || !panelOn) return;
    MIDImaster.instance.outputCC(curMIDIdevice, val, ID, channel);
  }
}


