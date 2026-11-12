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

public class platformSetup : MonoBehaviour {
  public GameObject hmdPrefab, controllerPrefab;

  public Transform hmdTargetVive, controllerLTargetVive, controllerRTargetVive;
  public Transform hmdTargetOculus, controllerLTargetOculus, controllerRTargetOculus;
  public GameObject hiresCamSelect;

  manipulator[] manips = new manipulator[2];

  // Controller rig alignment (added 2026-11).
  // The controller rig (model, grab tip, button sprites) was authored for the old
  // VrApi controller pose and the ring-style Touch controller. On the OpenXR backend
  // with Touch Plus it no longer lines up with the real controllers, which is obvious
  // in passthrough. The correction is calibrated in the headset rather than guessed:
  //
  //   Click BOTH thumbsticks together  -> enter / leave calibration (controllers buzz)
  //   Left stick up/down               -> move model forward / back
  //   Right stick up/down              -> move model up / down
  //   Left stick left/right            -> tilt (pitch)
  //
  // Leaving calibration saves to PlayerPrefs and logs the values
  // ("[platformSetup] controller calibration ...") so they can become the defaults below.
  // A saved calibration (PlayerPrefs) overrides these defaults.
  // Defaults calibrated in passthrough on Quest 3 / Touch Plus (2026-11-11).
  public Vector3 controllerOffset = new Vector3(0.0f, 0.0483f, 0.0441f);
  public float controllerPitch = -39.0f;
  bool calibrating = false;
  float calibLogTimer = 0;

  void LoadControllerCalibration() {
    if (PlayerPrefs.HasKey("ctrlCalPitch")) {
      controllerOffset = new Vector3(0, PlayerPrefs.GetFloat("ctrlCalY"), PlayerPrefs.GetFloat("ctrlCalZ"));
      controllerPitch = PlayerPrefs.GetFloat("ctrlCalPitch");
    }
  }

  void ApplyControllerPoseCorrection() {
    if (manips[0] == null || manips[1] == null) return;
    for (int i = 0; i < 2; i++) {
      Transform root = manips[i].transform.parent;
      root.localPosition = controllerOffset;
      root.localRotation = Quaternion.AngleAxis(controllerPitch, Vector3.right);
    }
  }

  void LogControllerCalibration(string what) {
    Debug.Log("[platformSetup] controller calibration " + what + ": offsetY=" + controllerOffset.y.ToString("F4") +
              " offsetZ=" + controllerOffset.z.ToString("F4") + " pitch=" + controllerPitch.ToString("F1"));
  }

  void UpdateControllerCalibration() {
    bool lClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
    bool rClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
    bool lDown = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
    bool rDown = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
    if ((lDown && rClick) || (rDown && lClick)) {
      calibrating = !calibrating;
      manips[0].hapticPulse(3000);
      manips[1].hapticPulse(3000);
      if (calibrating) {
        LogControllerCalibration("started");
      } else {
        PlayerPrefs.SetFloat("ctrlCalY", controllerOffset.y);
        PlayerPrefs.SetFloat("ctrlCalZ", controllerOffset.z);
        PlayerPrefs.SetFloat("ctrlCalPitch", controllerPitch);
        PlayerPrefs.Save();
        LogControllerCalibration("saved");
      }
    }
    if (!calibrating) return;

    Vector2 l = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
    Vector2 r = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
    const float dead = 0.2f, moveSpeed = 0.04f, pitchSpeed = 30f; // metres/s, degrees/s
    if (Mathf.Abs(l.y) > dead) controllerOffset.z += l.y * moveSpeed * Time.deltaTime;
    if (Mathf.Abs(r.y) > dead) controllerOffset.y += r.y * moveSpeed * Time.deltaTime;
    if (Mathf.Abs(l.x) > dead) controllerPitch += l.x * pitchSpeed * Time.deltaTime;
    ApplyControllerPoseCorrection();

    calibLogTimer += Time.deltaTime;
    if (calibLogTimer > 1f) { calibLogTimer = 0; LogControllerCalibration("current"); }
  }

  void Awake() {
    masterControl MC = GetComponent<masterControl>();

    if (MC.currentPlatform == masterControl.platform.Vive) {

      Instantiate(hmdPrefab, hmdTargetVive, false);
      manips[0] = (Instantiate(controllerPrefab, controllerLTargetVive, false) as GameObject).GetComponentInChildren<manipulator>();
      manips[1] = (Instantiate(controllerPrefab, controllerRTargetVive, false) as GameObject).GetComponentInChildren<manipulator>();

      manips[0].transform.parent.localPosition = Vector3.zero;
      manips[1].transform.parent.localPosition = Vector3.zero;

      if (UnityEngine.XR.XRSettings.loadedDeviceName == "Oculus") oculusSwitch();

      manips[0].SetDeviceIndex(0);
      manips[1].SetDeviceIndex(1);

      LoadControllerCalibration();
      ApplyControllerPoseCorrection();
    }
  }

    // Tracks the last applied state so we only toggle the controller objects when the
    // input mode actually changes, instead of calling SetActive on both every frame.
    int lastControllersActive = -1;

    void Update()
    {
        if (manips[0] == null || manips[1] == null) return;

        UpdateControllerCalibration();

        OVRPlugin.Controller control = OVRPlugin.GetActiveController(); //get current controller scheme
        bool handsActive = (OVRPlugin.Controller.Hands == control) || (OVRPlugin.Controller.LHand == control) || (OVRPlugin.Controller.RHand == control);
        int controllersActive = handsActive ? 0 : 1; //if current controller is hands disable the controllers
        if (controllersActive == lastControllersActive) return;
        lastControllersActive = controllersActive;

        manips[0].gameObject.SetActive(!handsActive);
        manips[1].gameObject.SetActive(!handsActive);
    }

    void oculusSwitch() {
    manips[0].invertScale(); // this actually makes the controller model L and R handed.
    manips[0].changeHW("oculus");
    manips[1].changeHW("oculus");
  }

  void Start() {
    manips[0].toggleTips(false);
    }

}