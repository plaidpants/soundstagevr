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

public class key : manipObject {

  public int keyValue = 0;
  public Material onMat;
  Renderer rend;
  Material offMat; // shared (MaterialCache, 2026-11): keys no longer copy materials
  deviceInterface _deviceInterface;

  public bool sticky = true;

  public bool isKeyboard = false;

  public override void Awake() {
    base.Awake();
    _deviceInterface = transform.parent.GetComponent<deviceInterface>();
    rend = GetComponent<Renderer>();
    offMat = rend.sharedMaterial;
  }

  bool initialized = false;
  void Start() {
    initialized = true;
  }

  public void setOffMat(Material m) {
    rend.sharedMaterial = m;
    offMat = m;
  }

  public bool isHit = false;

  public void keyHitCheck() {
    if (!initialized) return;
    bool on = touching || curState == manipState.grabbed || toggled;

    if (on != isHit) {
      isHit = on;
      _deviceInterface.hit(on, keyValue);
    }
  }

  enum keyState {
    off,
    touched,
    grabbedOn,
    grabbedOff,
    selectedOff,
    selectedOn
  };

  int desireSetSelect = 0;
  public void setSelectAsynch(bool on) {
    desireSetSelect = on ? 1 : 2;
  }

  bool phantomHitUpdate = false;
  Queue<bool> hits = new Queue<bool>();
  public void phantomHit(bool on) {
    phantomHitUpdate = true;
    isHit = on;
  }

  bool prevToggle = false;
  void Update() {
    if (phantomHitUpdate) {
      curSelect = 0;
      phantomHitUpdate = false;
      if (isHit) {
        if (isKeyboard) setKeyFeedbackState(keyState.selectedOn);
        else {
          if (toggled) setKeyFeedbackState(keyState.touched);
          else setKeyFeedbackState(keyState.selectedOff);
        }
      } else {
        setKeyFeedbackState(keyState.off);
      }
    }

    if (!sticky || !isHit) return;

    if (desireSetSelect != 0) {
      curSelect = desireSetSelect;
      if (desireSetSelect == 1) {
        if (toggled) setKeyFeedbackState(keyState.grabbedOn);
        else setKeyFeedbackState(keyState.selectedOn);
      } else {
        if (toggled) setKeyFeedbackState(keyState.touched);
        else setKeyFeedbackState(keyState.selectedOff);
      }

      desireSetSelect = 0;
    }

    if (prevToggle != toggled && isHit) {
      prevToggle = toggled;
      if (curSelect == 1) {
        if (toggled) setKeyFeedbackState(keyState.grabbedOn);
        else setKeyFeedbackState(keyState.selectedOn);
      } else {
        if (toggled) setKeyFeedbackState(keyState.touched);
        else setKeyFeedbackState(keyState.selectedOff);
      }
    }

  }

  int curSelect = 0;

  bool touching = false;
  public override void onTouch(bool on, manipulator m) {
    touching = on;
    if (m != null) {
      if (on) m.hapticPulse(3000);
      else m.hapticPulse(700);
    }

    keyHitCheck();
  }


  public bool toggled = false;
  public override void setState(manipState state) {
    if (!sticky) return;

    bool lateHitCheck = false;
    if (curState == manipState.grabbed && state != manipState.grabbed) {
      lateHitCheck = true;
    }

    curState = state;

    if (curState == manipState.grabbed) {
      toggled = !toggled;
      keyHitCheck();
    } else if (lateHitCheck) keyHitCheck();
  }

  void setKeyFeedbackState(keyState s) {
    switch (s) {
      case keyState.off:
        rend.sharedMaterial = offMat;
        break;
      case keyState.touched:
        setKeyColor(.65f, .3f);
        break;
      case keyState.grabbedOn:
        setKeyColor(.4f, .4f);
        break;
      case keyState.grabbedOff:
        setKeyColor(.65f, .4f);
        break;
      case keyState.selectedOff:
        setKeyColor(.4f, .3f);
        break;
      case keyState.selectedOn:
        setKeyColor(.5f, .4f);
        break;
      default:
        break;
    }
  }

  public void setKeyColor(float hue, float gain) {
    rend.sharedMaterial = MaterialCache.Glow(onMat, Color.HSVToRGB(hue, .9f, .5f), gain);
  }
}