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
using System.Runtime.InteropServices;

public class fader : signalGenerator {
  float faderLength = 1;

  public Transform faderBody, faderSliderBar;
  public float mySliderFloat;
  public omniJack inputA, inputB;
  public signalGenerator incomingA, incomingB;
  public bool active = true;
  public slider fadeSlider;
  float lastpercent = 0f;

  float[] bufferB;

  [DllImport("SoundStageNative")] public static extern void SetArrayToSingleValue(float[] a, int length, float val);
  [DllImport("SoundStageNative")] public static extern void processFader(float[] buffer, int length, int channels, float[] bufferB, int lengthB, bool aSig, bool bSig, bool samePercent, float lastpercent, float sliderPercent);

  public override void Awake() {
    fadeSlider = GetComponentInChildren<slider>();
    EnlargeGrabBox();
    bufferB = new float[MAX_BUFFER_LENGTH];
    SetArrayToSingleValue(bufferB, bufferB.Length, 0.0f);
  }

  void Update() {
    if (incomingA != inputA.signal) incomingA = inputA.signal;
    if (incomingB != inputB.signal) incomingB = inputB.signal;
  }

  // The fader knob is thin (about 1.2 x 2.5 cm) and hard to pinch with a tracked hand.
  // Make its grab box larger than the knob - wider across the channel and taller - without
  // changing how it looks. Channels are 4 cm apart, so the box stays inside its own channel.
  void EnlargeGrabBox() {
    if (fadeSlider == null) return;
    BoxCollider box = fadeSlider.GetComponent<BoxCollider>();
    if (box == null || fadeSlider.transform.parent == null) return;
    Transform s = fadeSlider.transform;
    Vector3 travel = s.parent.right; // the slider moves along its parent's x
    Vector3 size = box.size;
    for (int i = 0; i < 3; i++) {
      Vector3 axis = Vector3.zero; axis[i] = 1;
      Vector3 w = s.TransformVector(axis);  // this local axis, at size 1, in world units
      float len = w.magnitude;
      if (len < 1e-6f) continue;
      bool along = Mathf.Abs(Vector3.Dot(w / len, travel)) > .7f;
      float target = along ? .018f : .026f; // metres: a little longer along the travel, much wider across
      size[i] = Mathf.Max(size[i], target / len);
    }
    box.size = size;
  }

  public void updateFaderLength(float f) {
    faderLength = f;
    faderBody.localScale = new Vector3(1, 1, faderLength);
    faderSliderBar.localScale = new Vector3(1, 1, faderLength - .25f);

    Vector3 jackPos = new Vector3(.0005f, .001f, faderLength * .14f - .015f);
    inputA.transform.localPosition = jackPos;
    jackPos.z *= -1;
    inputB.transform.localPosition = jackPos;

    //change slider bounds
    // (stops short of the jacks at each end: at full travel the knob used to sit against
    // a jack's grab sphere, and a hand pinch there picked up the jack or its cable instead)
    fadeSlider.xBound = .095f * faderLength - .0325f;

    //move slider
    if (fadeSlider.curState != manipObject.manipState.grabbed) fadeSlider.setPercent(fadeSlider.percent);
  }

  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (!active || (incomingA == null && incomingB == null)) {
      SetArrayToSingleValue(buffer, buffer.Length, 0.0f);
      return;
    }

    float sliderPercent = fadeSlider.percent;
    float p = sliderPercent;
    bool samePercent = System.Math.Abs(lastpercent - sliderPercent) < 0.00001;

    if (incomingA != null && incomingB != null) {
      if (bufferB.Length != buffer.Length)
        System.Array.Resize(ref bufferB, buffer.Length);

      SetArrayToSingleValue(bufferB, bufferB.Length, 0.0f);

      incomingA.Pull(buffer, dspTime, channels);
      incomingB.Pull(bufferB, dspTime, channels);
    } else if (incomingA != null) incomingA.Pull(buffer, dspTime, channels);
    else incomingB.Pull(buffer, dspTime, channels);

    processFader(buffer, buffer.Length, channels, bufferB, bufferB.Length, incomingA != null, incomingB != null, samePercent, lastpercent, sliderPercent);

    lastpercent = sliderPercent;
  }
}

