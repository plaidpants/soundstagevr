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
  // NonSerialized on purpose: as serialized fields, a scene loaded in the editor while
  // the defaults were still zero kept those zeros in memory and baked them into builds.
  static readonly Vector3 DefaultControllerOffset = new Vector3(0.0f, 0.0483f, 0.0441f);
  const float DefaultControllerPitch = -39.0f;
  [System.NonSerialized] public Vector3 controllerOffset = DefaultControllerOffset;
  [System.NonSerialized] public float controllerPitch = DefaultControllerPitch;
  public static bool calibrating = false; // static so SnapTurn can stay out of the way
  float calibLogTimer = 0;

  void LoadControllerCalibration() {
    controllerOffset = DefaultControllerOffset;
    controllerPitch = DefaultControllerPitch;
    string source = "defaults";
    if (PlayerPrefs.HasKey("ctrlCalPitch")) {
      controllerOffset = new Vector3(0, PlayerPrefs.GetFloat("ctrlCalY"), PlayerPrefs.GetFloat("ctrlCalZ"));
      controllerPitch = PlayerPrefs.GetFloat("ctrlCalPitch");
      source = "saved calibration";
    }
    LogControllerCalibration("applied from " + source);
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

    // Tracks the last applied state so we only switch input modes when the mode
    // actually changes, instead of every frame.
    int lastHandsActive = -1;

    void Update()
    {
        if (manips[0] == null || manips[1] == null) return;

        bool handsActive = HandInput.HandsActive();
        int h = handsActive ? 1 : 0;
        if (h != lastHandsActive) {
            lastHandsActive = h;
            SetHandsMode(handsActive);
        }

        if (handsActive) { UpdateHandPoses(); UpdateToolCalibration(); }
        else UpdateControllerCalibration();
    }

    // ---- Hand tracking (added 2026-11) ----
    // In hands mode the manipulators stay active (they used to be switched off, which
    // left hands with no way to interact). Each manipulator is moved from its controller
    // anchor to the tracking space, takes its rotation from the hand's aim (pointer)
    // pose, and is shifted so its grab collider sits at the real pinch point (midway
    // between thumb and index tips). The controller pincers are hidden. Every fingertip
    // also gets a FingertipPoker, so keys can be played by touching them.
    // Index pinch = trigger, pinky pinch or the left-hand system menu gesture = menu
    // (see manipulator.Update / HandInput).
    // Fallback when the hand skeleton isn't ready: grab point this far in front of
    // the aim-pose origin, in metres.
    public float handGrabForward = 0.02f;
    HandFingertips[] fingertips = new HandFingertips[2];

    Transform trackingSpace;
    Transform[] manipRoots = new Transform[2];
    Transform[] controllerParents = new Transform[2];
    GameObject[] handVisuals = new GameObject[2];
    bool handsMode = false;

    void SetHandsMode(bool on) {
        if (on && !FindTrackingSpace()) on = false;
        handsMode = on;
        for (int i = 0; i < 2; i++) {
            if (manipRoots[i] == null) {
                manipRoots[i] = manips[i].transform.parent;
                controllerParents[i] = manipRoots[i].parent;
            }
            manips[i].gameObject.SetActive(true);
            manips[i].SetHandsMode(on);
            manipRoots[i].SetParent(on ? trackingSpace : controllerParents[i], false);
        }
        if (on) {
            SpawnHandVisuals();
            UpdateHandPoses();
        } else {
            ApplyControllerPoseCorrection();
        }
        for (int i = 0; i < 2; i++) if (handVisuals[i] != null) handVisuals[i].SetActive(on);
        Debug.Log("[platformSetup] input mode: " + (on ? "hands" : "controllers"));
    }

    bool FindTrackingSpace() {
        if (trackingSpace != null) return true;
        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null) trackingSpace = rig.trackingSpace;
        if (trackingSpace == null) Debug.LogWarning("[platformSetup] no OVRCameraRig tracking space; hands mode disabled");
        return trackingSpace != null;
    }

    void SpawnHandVisuals() {
        OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
        if (rig == null) return;
        string[] names = { "Hands/HandLeft", "Hands/HandRight" };
        Transform[] anchors = { rig.leftHandAnchor, rig.rightHandAnchor };
        for (int i = 0; i < 2; i++) {
            if (handVisuals[i] != null || anchors[i] == null) continue;
            GameObject prefab = Resources.Load<GameObject>(names[i]);
            if (prefab == null) { Debug.LogWarning("[platformSetup] missing Resources/" + names[i]); continue; }
            handVisuals[i] = Instantiate(prefab, anchors[i], false);
            fingertips[i] = handVisuals[i].AddComponent<HandFingertips>();
            fingertips[i].owner = manips[i];
            fingertips[i].isLeft = i == 0;
        }
    }

    void UpdateHandPoses() {
        if (!handsMode) return;
        for (int i = 0; i < 2; i++) {
            HandInput.Hand hand = HandInput.Get(i);
            if (!hand.inputValid) continue; // hold the last pose while the hand is lost
            Transform root = manipRoots[i];
            // Holding a tool: follow the hand's bones so wrist flicks move it (drumming).
            if (manips[i].holdingTool && fingertips[i] != null && fingertips[i].HasHoldPose) {
                Vector3 holdPos; Quaternion holdRot;
                fingertips[i].GetHoldPose(out holdPos, out holdRot);
                root.rotation = holdRot;
                root.position += holdPos - manips[i].GrabPointWorld;
                continue;
            }
            root.localPosition = hand.pointerPosition;
            root.localRotation = hand.pointerRotation;
            // Shift the rig so the manipulator's grab collider lands at the pinch.
            Vector3 target = (fingertips[i] != null && fingertips[i].HasPinchPoint)
                ? fingertips[i].PinchPoint
                : trackingSpace.TransformPoint(hand.pointerPosition + hand.pointerRotation * (Vector3.forward * handGrabForward));
            root.position += target - manips[i].GrabPointWorld;
        }
    }

    // ---- Hand-held tool calibration (drumsticks / mallets) ----
    // While one hand holds a stick, pinch the stick with the other hand and move it: the
    // stick pivots at the holding hand's grip and its head follows the pinching fingers
    // (direction and how far it sticks out). Let go to save. Stored per kind of stick
    // (PlayerPrefs "handTool.drumstick" / "handTool.mallet"); mirrored for the other hand.
    int calibHand = -1;        // hand holding the tool being calibrated
    bool calibActive = false;  // free hand has fully pinched
    Vector3 calibDir;
    float calibGrip;

    void UpdateToolCalibration() {
        if (calibHand < 0) {
            for (int i = 0; i < 2; i++) {
                manipulator holder = manips[i];
                if (!holder.holdingTool || holder.heldTool == null || holder.heldToolKey == null) continue;
                int j = 1 - i;
                if (fingertips[j] == null || !fingertips[j].HasPinchPoint || manips[j].isGrabbing()) continue;
                HandInput.Hand free = HandInput.Get(j);
                if (!free.inputValid || free.pinchIndex < 0.3f) continue;
                Vector3 a = holder.heldTool.position;
                Vector3 b = a + holder.HeldToolAxis * holder.heldToolLength;
                Vector3 p = fingertips[j].PinchPoint;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((a + ab * t - p).magnitude > 0.04f) continue;
                calibHand = i;
                calibActive = false;
                manips[j].suppressHandTrigger = true; // don't grab anything else with this pinch
                break;
            }
            return;
        }

        manipulator h = manips[calibHand];
        int f = 1 - calibHand;
        HandInput.Hand fh = HandInput.Get(f);
        bool stillHolding = h.holdingTool && h.heldTool != null && h.heldToolKey != null;
        if (!stillHolding || !fh.inputValid || fh.pinchIndex < 0.3f) {
            if (calibActive && stillHolding) manipulator.SaveToolCalibration(h.heldToolKey, calibDir, calibGrip);
            manips[f].suppressHandTrigger = false;
            calibHand = -1;
            calibActive = false;
            return;
        }
        if (fh.pinchIndex >= 0.85f) calibActive = true;
        if (!calibActive || !fingertips[f].HasPinchPoint) return;

        // Direction in the holding hand's hold frame, stored in right-hand convention
        // (mirrored for the left hand) so the other hand gets the mirror image.
        Transform root = h.transform.parent;
        Vector3 d = fingertips[f].PinchPoint - h.GrabPointWorld;
        float dist = d.magnitude;
        if (dist < 0.02f) return;
        Vector3 inFrame = Quaternion.Inverse(root.rotation) * (d / dist);
        calibDir = h.IsLeftHand ? manipulator.MirrorX(inFrame) : inFrame;
        calibGrip = Mathf.Clamp(h.heldToolLength - dist, -0.25f, h.heldToolLength - 0.02f);
        h.ApplyToolPose(calibDir, calibGrip);
    }

    // Re-read the hands just before rendering so the manipulators don't lag a frame.
    void OnBeforeRender() {
        if (!handsMode) return;
        HandInput.Refresh(true);
        UpdateHandPoses();
    }

    void OnEnable() { Application.onBeforeRender += OnBeforeRender; }
    void OnDisable() { Application.onBeforeRender -= OnBeforeRender; }

    void oculusSwitch() {
    manips[0].invertScale(); // this actually makes the controller model L and R handed.
    manips[0].changeHW("oculus");
    manips[1].changeHW("oculus");
  }

  void Start() {
    manips[0].toggleTips(false);
    }

}