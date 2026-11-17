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

public class speakerDeviceInterface : deviceInterface {
  public int ID = -1;
  public omniJack input;
  speaker output;
  public basicSwitch channelSwitcher; // LEFT ONLY / RIGHT ONLY (ludzeller's fork, 2026-11)
  public GameObject speakerRim;
#pragma warning disable 0108, 0109 // the editor still has the old Component.audio, the player build doesn't: one or the other always warned
  public AudioSource audio;
#pragma warning restore 0108, 0109

  SpeakerData data;

  public override void Awake() {
    base.Awake();
    output = GetComponent<speaker>();
    input = GetComponentInChildren<omniJack>();
    speakerRim.GetComponent<Renderer>().material.SetFloat("_EmissionGain", .45f);
    speakerRim.SetActive(false);
  }

  void Start() {
    masterControl.SetSpatial(audio, masterControl.instance.BinauralSetting != masterControl.BinauralMode.None);
  }

  public void Activate(int[] prevIDs) {
    ID = prevIDs[0];
    input.ID = prevIDs[1];
  }

  float lastScale = 0;

  void Update() {
    if (output.incoming != input.signal) {
      output.incoming = input.signal;
      if (output.incoming == null) speakerRim.SetActive(false);
      else speakerRim.SetActive(true);
    }

    if (output.incoming != null) {
      if (lastScale != transform.localScale.x) {
        lastScale = transform.localScale.x;
        output.volume = Mathf.Pow(lastScale + .2f, 2);
      }
    }

    if (channelSwitcher != null) output.leftOn = channelSwitcher.switchVal;
  }

  public override InstrumentData GetData() {
    SpeakerData data = new SpeakerData();
    data.deviceType = menuItem.deviceType.Speaker;
    GetTransformData(data);
    data.jackInID = input.transform.GetInstanceID();
    if (channelSwitcher != null) data.leftChannel = channelSwitcher.switchVal;
    return data;
  }

  public override void Load(InstrumentData d) {
    SpeakerData data = d as SpeakerData;

    transform.localPosition = data.position;
    transform.localRotation = data.rotation;
    transform.localScale = data.scale;

    ID = data.ID;
    input.ID = data.jackInID;
    if (channelSwitcher != null) channelSwitcher.setSwitch(data.leftChannel);
  }
}

public class SpeakerData : InstrumentData {
  public int jackInID;
  public bool leftChannel = true;
}