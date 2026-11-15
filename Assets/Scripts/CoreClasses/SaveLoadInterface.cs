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
using System;
using System.Xml.Serialization;

public class SaveLoadInterface : MonoBehaviour {
  xmlSaveLoad synthSet;
  public GameObject plugPrefab;
  public static SaveLoadInterface instance;
  public metronome nome;
  Dictionary<menuItem.deviceType, GameObject> instrumentPrefabs;

  void Awake() {
    instance = this;
    instrumentPrefabs = new Dictionary<menuItem.deviceType, GameObject>();

    for (int i = 0; i < (int)menuItem.deviceType.Max; i++) {
      instrumentPrefabs[(menuItem.deviceType)i] = Resources.Load("Prefabs/" + ((menuItem.deviceType)i).ToString()) as GameObject;
    }

    instrumentPrefabs[menuItem.deviceType.TapeGroup] = Resources.Load("Prefabs/" + (menuItem.deviceType.TapeGroup).ToString()) as GameObject;
    instrumentPrefabs[menuItem.deviceType.Pano] = Resources.Load("Prefabs/" + (menuItem.deviceType.Pano).ToString()) as GameObject;
  }

  public void Load(string filename, bool tutorial = false) {
    synthSet = xmlSaveLoad.LoadFromFile(filename);
    masterControl.instance.currentScene = filename;
    LoadSynthSet(filename, null);
  }

  // Multiplayer (Net/NetSync): load a patch received over the network. onDevice gets
  // each device with its index in set.InstrumentList, so both sides can give the same
  // device the same network ID.
  public void LoadFromSet(xmlSaveLoad set, System.Action<int, GameObject> onDevice) {
    synthSet = set;
    LoadSynthSet(null, onDevice);
  }

  void LoadSynthSet(string filename, System.Action<int, GameObject> onDevice) {
    float v = systemLoad(synthSet.SystemList[0]);
    UpgradeOldData(synthSet.InstrumentList);

    if (v == 0 && filename != null) {
      xmlUpdate _xmlUpdate = new xmlUpdate();
      List<InstrumentData> dataB = _xmlUpdate.UpdateFile(filename);
      UpgradeOldData(dataB);
      foreach (InstrumentData dB in dataB) {
        GameObject g = Instantiate(instrumentPrefabs[dB.deviceType], Vector3.zero, Quaternion.identity) as GameObject;
        g.GetComponent<deviceInterface>().Load(dB);
      }
    }

    int c = synthSet.InstrumentList.Count;
    for (int i = 0; i < c; i++) {
      GameObject g = Instantiate(instrumentPrefabs[synthSet.InstrumentList[c - 1 - i].deviceType], Vector3.zero, Quaternion.identity) as GameObject;
      g.GetComponent<deviceInterface>().Load(synthSet.InstrumentList[c - 1 - i]);
      if (onDevice != null) onDevice(c - 1 - i, g);
    }

    LoadPlugs();
    ClearSynthSetList();
  }

  // Settings added after a file was saved get the value that plays it the way it
  // always played (2026-11, file version .77):
  //   drum machine / CV sequencer LOOP-ONCE: a sequencer used to loop when started with
  //   PLAY and play once per trigger from its control input, so it gets LOOP unless
  //   something is plugged into that input.
  // (Oscillator LFO range, keyboard octave, keyboard/xyloroll size already default to
  // the original behaviour.)
  void UpgradeOldData(List<InstrumentData> list) {
    HashSet<int> pluggedJacks = new HashSet<int>();
    foreach (PlugData p in synthSet.PlugList) pluggedJacks.Add(p.connected);
    foreach (InstrumentData d in list) {
      SequencerData sd = d as SequencerData;
      if (sd != null && !sd.loopSpecified) sd.loop = !pluggedJacks.Contains(sd.jackInID);
      cvSequencerData cd = d as cvSequencerData;
      if (cd != null && !cd.loopSpecified) cd.loop = !pluggedJacks.Contains(cd.jackInID);
    }
  }

  public bool PreviewLoad(string filename, Transform par) {
    synthSet = xmlSaveLoad.LoadFromFile(filename);
    float v = systemLoad(synthSet.SystemList[0], true);

    foreach (InstrumentData data in synthSet.InstrumentList) {
      Transform t = (Instantiate(menuManager.instance.refObjects[data.deviceType], par, false) as GameObject).transform;
      t.localPosition = data.position;
      t.localRotation = data.rotation;
      t.localScale = data.scale;
    }

    ClearSynthSetList();
    return (v != 0);
  }

  void ClearSynthSetList() {
    synthSet.InstrumentList.Clear();
    synthSet.PlugList.Clear();
    synthSet.SystemList.Clear();
  }

  void systemSave() { systemSave(synthSet); }

  void systemSave(xmlSaveLoad set) {
    SystemData s = new SystemData();
    s.version = masterControl.versionNumber;
    s.BPM = masterControl.instance.bpm;
    set.SystemList.Add(s);
  }

  float systemLoad(SystemData s, bool justpreview = false) {
    if (justpreview) return s.version;

    masterControl.instance.setBPM(s.BPM);
    nome.SetBPM(s.BPM);
    synthSet.SystemList.Add(s);
    return s.version;
  }

  public void Save(string filename) {
    masterControl.instance.currentScene = filename;
    BuildSet(null).SaveToFile(filename);
  }

  // The whole patch as a save-file set. order (optional) gets the device behind each
  // InstrumentList entry, in the same order (used by multiplayer for network IDs).
  public xmlSaveLoad BuildSet(List<deviceInterface> order) {
    xmlSaveLoad set = new xmlSaveLoad();
    systemSave(set);
    deviceInterface[] devices = FindObjectsOfType(typeof(deviceInterface)) as deviceInterface[];
    foreach (deviceInterface d in devices) {
      set.InstrumentList.Add(d.GetData());
      if (order != null) order.Add(d);
    }

    omniPlug[] plugs = FindObjectsOfType(typeof(omniPlug)) as omniPlug[];
    foreach (omniPlug p in plugs) {
      set.PlugList.Add(p.GetData());
    }
    return set;
  }

  // One device from its saved data (multiplayer spawn), like Copy without the grab.
  public GameObject SpawnFromData(InstrumentData data) {
    GameObject prefab;
    if (data == null || !instrumentPrefabs.TryGetValue(data.deviceType, out prefab) || prefab == null) return null;
    GameObject g = Instantiate(prefab, Vector3.zero, Quaternion.identity) as GameObject;
    g.GetComponent<deviceInterface>().Load(data);
    return g;
  }

  void LoadPlugs() {
    Dictionary<int, omniPlug> temp = new Dictionary<int, omniPlug>();
    List<PlugData> ResortedPlugList = new List<PlugData>();

    for (int i = 0; i < synthSet.PlugList.Count; i++) {
      GameObject g = Instantiate(plugPrefab, synthSet.PlugList[i].position, synthSet.PlugList[i].rotation) as GameObject;
      omniPlug o = g.GetComponent<omniPlug>();
      o.ID = synthSet.PlugList[i].ID;
      o.outputPlug = synthSet.PlugList[i].outputPlug;
      temp[o.ID] = o;

      if (!o.outputPlug) ResortedPlugList.Insert(0, synthSet.PlugList[i]);
      else ResortedPlugList.Add(synthSet.PlugList[i]);
    }

    omniJack[] jacks = FindObjectsOfType(typeof(omniJack)) as omniJack[];

    for (int i = 0; i < ResortedPlugList.Count; i++) {
      omniJack targetJack = null;
      for (int i2 = 0; i2 < jacks.Length; i2++) {
        if (jacks[i2].ID == ResortedPlugList[i].connected) {
          targetJack = jacks[i2];
          break;
        }
      }
      if (targetJack == null) Debug.LogError("NO JACK FOR " + ResortedPlugList[i].connected);
      temp[ResortedPlugList[i].ID].Activate(temp[ResortedPlugList[i].otherPlug], targetJack, ResortedPlugList[i].plugPath, ResortedPlugList[i].cordColor);
    }
  }

  public void Copy(GameObject g, manipulator m) {
    if (g.GetComponent<deviceInterface>() != null) {
      InstrumentData data = g.GetComponent<deviceInterface>().GetData();
      GameObject g2 = Instantiate(instrumentPrefabs[data.deviceType], Vector3.zero, Quaternion.identity) as GameObject;
      g2.GetComponent<deviceInterface>().Load(data);

      g2.transform.position = g.transform.position;
      g2.transform.rotation = g.transform.rotation;
      g2.transform.localScale = g.transform.localScale;

      m.ForceGrab(g2.GetComponentInChildren<handle>());
    }
  }

}

[XmlInclude(typeof(OscillatorData))]
[XmlInclude(typeof(SpeakerData))]
[XmlInclude(typeof(CameraData))]
[XmlInclude(typeof(ControlCubeData))]
[XmlInclude(typeof(DrumData))]
[XmlInclude(typeof(NoiseData))]
[XmlInclude(typeof(FilterData))]
[XmlInclude(typeof(MaracaData))]
[XmlInclude(typeof(MicrophoneData))]
[XmlInclude(typeof(TouchPadData))]
[XmlInclude(typeof(ValveData))]
[XmlInclude(typeof(ReverbData))]
[XmlInclude(typeof(TapeGroupData))]
[XmlInclude(typeof(MixerData))]
[XmlInclude(typeof(SplitterData))]
[XmlInclude(typeof(LooperData))]
[XmlInclude(typeof(RecorderData))]
[XmlInclude(typeof(SamplerData))]
[XmlInclude(typeof(KeyboardData))]
[XmlInclude(typeof(SequencerData))]
[XmlInclude(typeof(XyloRollData))]
[XmlInclude(typeof(AirhornData))]
[XmlInclude(typeof(PanoData))]
[XmlInclude(typeof(MIDIinData))]
[XmlInclude(typeof(MIDIoutData))]
[XmlInclude(typeof(TimelineData))]
[XmlInclude(typeof(ADSRData))]
[XmlInclude(typeof(GainData))]
[XmlInclude(typeof(GlideData))]
[XmlInclude(typeof(cvSequencerData))]
[XmlInclude(typeof(ScaleSequencerData))]
public class InstrumentData {
  public int ID;
  public menuItem.deviceType deviceType;
  public Vector3 position;
  public Vector3 scale = Vector3.one;
  public Quaternion rotation;
}

public class SystemData {
  public float BPM;
  public float version;
}


public class JackData : InstrumentData {
  public int connected;
  public int homePort;
  public Vector3[] jackPath;
  public Color cordColor;
  public int signalID;
  public int signalClass;
}

public class PlugData : InstrumentData {
  public bool outputPlug;
  public int connected;
  public int otherPlug;
  public Vector3[] plugPath;
  public Color cordColor;
}