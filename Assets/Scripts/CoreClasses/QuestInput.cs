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

// Controller input for Quest via Meta's OVRInput (added 2026-11).
//
// Replaces the legacy Input Manager axes/buttons that manipulator.cs used
// (Input.GetAxis("triggerR"), Input.GetButtonDown("menuButtonL"), ...). Under
// Unity 2022.3 + the Oculus XR plugin those legacy joystick buttons stopped
// producing reliable press/release edges (e.g. the menu would open and close
// once, then never reopen). OVRInput reads the controllers straight from the
// Oculus runtime and computes edges each frame in OVRManager.Update.
//
// The legacy names are kept so call sites only change "Input." to "QuestInput.".
// NOTE the original naming is inverted relative to the hands: the "...R" names
// were mapped to the LEFT Touch controller (X/Y buttons, left stick and trigger)
// and "...L" to the RIGHT controller (A/B). That mapping is preserved exactly:
//
//   triggerR / triggerL             -> index trigger           (left / right)
//   menuButtonR / menuButtonL       -> Button.One   X / A      (left / right)
//   secondaryButtonR / ...L         -> Button.Two   Y / B      (left / right)
//   touchR / touchL                 -> thumbstick touched      (left / right)
//   pressR / pressL                 -> thumbstick clicked      (left / right)
//   touchAxisXR, touchAxisYR / ..L  -> thumbstick X / Y, up=+1 (left / right)

using UnityEngine;

public static class QuestInput {

  static OVRInput.Controller ControllerFor(string name) {
    // Every legacy name ends in R (left controller) or L (right controller).
    char side = name[name.Length - 1];
    if (side == 'R') return OVRInput.Controller.LTouch;
    if (side == 'L') return OVRInput.Controller.RTouch;
    Debug.LogWarning("[QuestInput] Unknown input name: " + name);
    return OVRInput.Controller.None;
  }

  static string Base(string name) {
    return name.Substring(0, name.Length - 1);
  }

  public static float GetAxis(string name) {
    OVRInput.Controller c = ControllerFor(name);
    switch (Base(name)) {
      case "trigger": return OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, c);
      case "touchAxisX": return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c).x;
      case "touchAxisY": return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, c).y;
    }
    Debug.LogWarning("[QuestInput] Unknown axis: " + name);
    return 0f;
  }

  public static bool GetButton(string name) {
    OVRInput.Controller c = ControllerFor(name);
    switch (Base(name)) {
      case "menuButton": return OVRInput.Get(OVRInput.Button.One, c);
      case "secondaryButton": return OVRInput.Get(OVRInput.Button.Two, c);
      case "touch": return OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, c);
      case "press": return OVRInput.Get(OVRInput.Button.PrimaryThumbstick, c);
    }
    Debug.LogWarning("[QuestInput] Unknown button: " + name);
    return false;
  }

  public static bool GetButtonDown(string name) {
    OVRInput.Controller c = ControllerFor(name);
    switch (Base(name)) {
      case "menuButton": return OVRInput.GetDown(OVRInput.Button.One, c);
      case "secondaryButton": return OVRInput.GetDown(OVRInput.Button.Two, c);
      case "touch": return OVRInput.GetDown(OVRInput.Touch.PrimaryThumbstick, c);
      case "press": return OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, c);
    }
    Debug.LogWarning("[QuestInput] Unknown button: " + name);
    return false;
  }

  public static bool GetButtonUp(string name) {
    OVRInput.Controller c = ControllerFor(name);
    switch (Base(name)) {
      case "menuButton": return OVRInput.GetUp(OVRInput.Button.One, c);
      case "secondaryButton": return OVRInput.GetUp(OVRInput.Button.Two, c);
      case "touch": return OVRInput.GetUp(OVRInput.Touch.PrimaryThumbstick, c);
      case "press": return OVRInput.GetUp(OVRInput.Button.PrimaryThumbstick, c);
    }
    Debug.LogWarning("[QuestInput] Unknown button: " + name);
    return false;
  }
}
