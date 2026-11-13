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

public class touchpadDeviceInterface : deviceInterface {

  touchpadSignalGenerator pad;
  omniJack output;
  button padButton;

  // Latch (2026-11): hold the button for latchHoldTime to latch the output on (it stays
  // on and lit after release); press again to release it. Short presses still act as a
  // momentary trigger. Lets one button start several sequencers together and hold a gate.
  public static float latchHoldTime = 0.6f;
  bool latched = false;
  bool unlatchPress = false;
  bool pressed = false; // a real press is in progress (Start() also sends a release)
  float pressTime;

  public override void Awake() {
    base.Awake();
    pad = GetComponent<touchpadSignalGenerator>();
    output = GetComponentInChildren<omniJack>();
    padButton = GetComponentInChildren<button>();
  }

  public override void hit(bool on, int ID = -1) {
    if (on) {
      if (latched) {
        // press on a latched pad releases it
        latched = false;
        unlatchPress = true;
        pad.signalOn = false;
        return;
      }
      pressTime = Time.time;
      pressed = true;
      pad.signalOn = true;
    } else {
      if (unlatchPress) { unlatchPress = false; return; }
      bool wasPressed = pressed;
      pressed = false;
      if (latched) return; // e.g. the button's own Start() release after a load
      if (wasPressed && Time.time - pressTime >= latchHoldTime) {
        latched = true; // keep the output on
        if (padButton != null) padButton.phantomHit(true); // keep it lit
        return;
      }
      pad.signalOn = false;
    }
  }

  void SetLatched(bool on) {
    latched = on;
    pad.signalOn = on;
    if (padButton != null) padButton.phantomHit(on);
  }

  public override InstrumentData GetData() {
    TouchPadData data = new TouchPadData();
    data.deviceType = menuItem.deviceType.TouchPad;
    GetTransformData(data);

    data.jackOutID = output.transform.GetInstanceID();
    data.latched = latched;
    return data;
  }

  public override void Load(InstrumentData d) {
    TouchPadData data = d as TouchPadData;
    base.Load(data);
    output.ID = data.jackOutID;
    if (data.latched) SetLatched(true);
  }
}

public class TouchPadData : InstrumentData {
  public int jackOutID;
  public bool latched;
}
