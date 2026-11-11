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
    }
  }

    // Tracks the last applied state so we only toggle the controller objects when the
    // input mode actually changes, instead of calling SetActive on both every frame.
    int lastControllersActive = -1;

    void Update()
    {
        if (manips[0] == null || manips[1] == null) return;

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