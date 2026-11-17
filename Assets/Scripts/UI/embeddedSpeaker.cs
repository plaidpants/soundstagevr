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

public class embeddedSpeaker : MonoBehaviour {
  public omniJack speakerOut;
  signalGenerator signal;
  public speaker output;
#pragma warning disable 0108, 0109 // the editor still has the old Component.audio, the player build doesn't: one or the other always warned
  public AudioSource audio;
#pragma warning restore 0108, 0109
  public GameObject speakerRim;

  bool secondary = true;

  public bool activated = false;
  Material rimMat;
  void Awake() {
    signal = transform.parent.GetComponent<signalGenerator>();

    if (speakerRim != null) {
      rimMat = speakerRim.GetComponent<Renderer>().material;
      rimMat.SetFloat("_EmissionGain", .45f);
      speakerRim.SetActive(false);

    }
  }

  void Start() {
    masterControl.SetSpatial(audio, masterControl.instance.BinauralSetting == masterControl.BinauralMode.All);
  }

  public void updateSecondary(bool on) {
    secondary = on;
    updateSpeaker();
  }

  void updateSpeaker() {
    if (output.incoming == null || !secondary) {
      if (speakerRim != null) {
        activated = false;
        speakerRim.SetActive(false);
      }
    } else {
      if (speakerRim != null && secondary) {
        activated = true;
        speakerRim.SetActive(true);
      }
    }
  }

  signalGenerator curSignal, prevSignal;
  void Update() {
    if (speakerOut.near == null) {
      curSignal = signal;
    } else {
      curSignal = null;
    }

    if (prevSignal != curSignal) {
      output.incoming = prevSignal = curSignal;
      updateSpeaker();
    }
  }
}
