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

public class filterDeviceInterface : deviceInterface {

  public quadSection[] quads;
  public button[] buttons;
  public omniJack input, controlInput, output;

  spectrumDisplay spectrum;
  int ID = 0;
  filterSignalGenerator filter;

  public float[] percentages = new float[] { .3f, .6f };

  public bool[] startState = new bool[3];

  public override void Awake() {
    base.Awake();
    filter = GetComponent<filterSignalGenerator>();
    spectrum = GetComponentInChildren<spectrumDisplay>();

    for (int i = 0; i < 3; i++) {
      buttons[i].startToggled = startState[i];
    }
  }

  void Start() {
    setupPercentages();
  }

  public override InstrumentData GetData() {
    FilterData data = new FilterData();
    data.deviceType = menuItem.deviceType.Filter;
    GetTransformData(data);

    data.jackInID = input.transform.GetInstanceID();
    data.jackOutID = output.transform.GetInstanceID();
    data.jackControlInID = controlInput.transform.GetInstanceID();

    data.LP = quads[0].toggled;
    data.BP = quads[1].toggled;
    data.HP = quads[2].toggled;

    data.percentA = percentages[0];
    data.percentB = percentages[1];

    return data;
  }

  public override void Load(InstrumentData d) {
    FilterData data = d as FilterData;
    base.Load(data);

    ID = data.ID;
    input.ID = data.jackInID;
    output.ID = data.jackOutID;
    controlInput.ID = data.jackControlInID;

    buttons[0].startToggled = startState[0] = data.LP;
    buttons[1].startToggled = startState[1] = data.BP;
    buttons[2].startToggled = startState[2] = data.HP;

    percentages[0] = data.percentA;
    percentages[1] = data.percentB;
    setupPercentages();
  }

  void setupPercentages() {
    quads[1].setupPercents(percentages[0], percentages[1]);
  }

  void Update() {
    updatePercentages();
    if (filter.incoming != input.signal) {
      filter.incoming = input.signal;
      spectrum.toggleActive(filter.incoming != null);
    }

    if (filter.controlIncoming != controlInput.signal) {
      filter.controlIncoming = controlInput.signal;
    }

    if (filter.controlIncoming != null) {
      float per = (filter.controlFloat + 1) / 2f;
      if (quads[1].curState != manipObject.manipState.grabbed) {
        if (quads[0].curState != manipObject.manipState.grabbed && quads[2].curState != manipObject.manipState.grabbed) {
          quads[1].updatePercentage(per);
        } else if (quads[0].curState == manipObject.manipState.grabbed && quads[2].curState != manipObject.manipState.grabbed) {
          quads[2].updatePercentage(per);
        } else if (quads[2].curState == manipObject.manipState.grabbed && quads[0].curState != manipObject.manipState.grabbed) {
          quads[0].updatePercentage(per);
        }
      }
    }
  }

  float dif, spread;

  void updatePercentages() {
    float a, b;
    quads[1].getPercentages(out a, out b);

    bool bp = filter.curType == filterSignalGenerator.filterType.BP;
    if (bp != coefBP) { coefBP = bp; coefSet = false; }
    if (!coefSet || percentages[0] != a) { percentages[0] = Mathf.Clamp01(a); filter.frequency[0] = Coefficient(percentages[0], bp); }
    if (!coefSet || percentages[1] != b) { percentages[1] = Mathf.Clamp01(b); filter.frequency[1] = Coefficient(percentages[1], bp); }
    coefSet = true;
  }

  bool coefSet, coefBP;

  // The spectrum display draws the band at (.01 + .3p) / .32 of the way to Nyquist,
  // and the native Moog filter peaks a little below its coefficient (about .94x at
  // resonance .5, more at the top). Convert the drawn position to the coefficient
  // whose peak lands there, so the sound sits in the band you see.
  static readonly float[] coefs = { 0f, .0025f, .01f, .03f, .06f, .1f, .15f, .2f, .25f, .3f, .35f, .4f, .45f, .5f, .55f, .6f, .65f, .7f, .75f, .8f, .85f, .9f, .94f, .97f, .99f, 1f };
  static readonly float[] peaks = { 0f, .0023f, .0093f, .0280f, .0563f, .0944f, .1423f, .1902f, .2378f, .2849f, .3314f, .3777f, .4236f, .4692f, .5148f, .5602f, .6057f, .6514f, .6973f, .7437f, .7911f, .8407f, .8840f, .9224f, .9574f, 1f };

  public static float Coefficient(float percent, bool bandPass) {
    float d = (.01f + .3f * Mathf.Clamp01(percent)) / .32f;
    // In band-pass the high end still sat low (the skirts get lopsided near Nyquist); nudge it up.
    if (bandPass && d > .4f) d += .3f * (d - .4f) * (d - .4f);
    for (int i = 1; i < peaks.Length; i++) {
      if (d <= peaks[i]) return Mathf.Min(.995f, Mathf.Lerp(coefs[i - 1], coefs[i], (d - peaks[i - 1]) / (peaks[i] - peaks[i - 1])));
    }
    return .995f;
  }

  void updateSelection() {
    if (quads[0].toggled && quads[1].toggled && quads[2].toggled) //all on
    {
      filter.updateFilterType(filterSignalGenerator.filterType.pass);
    } else if (!quads[0].toggled && !quads[1].toggled && !quads[2].toggled) //all off
      {
      filter.updateFilterType(filterSignalGenerator.filterType.none);
    } else if (quads[0].toggled && !quads[1].toggled && !quads[2].toggled) //low pass
      {
      filter.updateFilterType(filterSignalGenerator.filterType.LP);
    } else if (quads[0].toggled && quads[1].toggled && !quads[2].toggled) //low pass long
      {
      filter.updateFilterType(filterSignalGenerator.filterType.LP_long);
    } else if (!quads[0].toggled && !quads[1].toggled && quads[2].toggled) //hi pass 
      {
      filter.updateFilterType(filterSignalGenerator.filterType.HP);
    } else if (!quads[0].toggled && quads[1].toggled && quads[2].toggled) //hi pass long
      {
      filter.updateFilterType(filterSignalGenerator.filterType.HP_long);
    } else if (!quads[0].toggled && quads[1].toggled && !quads[2].toggled) //band pass
      {
      filter.updateFilterType(filterSignalGenerator.filterType.BP);
    } else if (quads[0].toggled && !quads[1].toggled && quads[2].toggled) //notch
      {
      filter.updateFilterType(filterSignalGenerator.filterType.Notch);
    }
  }

  public override void hit(bool on, int ID = -1) {
    quads[ID].setToggle(on);
    updateSelection();
  }
}


public class FilterData : InstrumentData {
  public float percentA, percentB;
  public bool LP, BP, HP;
  public int jackOutID;
  public int jackInID;
  public int jackControlInID;
}