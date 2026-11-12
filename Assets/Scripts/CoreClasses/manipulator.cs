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
using static OVRHand;
//using System.Linq;
//using Valve.VR;

public class manipulator : MonoBehaviour {
  int controllerIndex = -1;
  public GameObject activeTip;
  public Transform tipL, tipR;
  public Transform triggerTrans;
  List<Transform> hitTransforms = new List<Transform>();
  Transform selectedTransform;
  manipObject selectedObject;

  public Renderer[] oculusSprites;
  public GameObject oculusContextButtonGlow;

  menuspawn _menuspawn;
  public touchpad _touchpad;
  public GameObject[] tipTexts;
  public GameObject oculusObjects, viveObjects, oculusMenuButton, oculusTrigger;
  public Transform tipPackage, glowMenuTransform, oculusManipTarget, oculusParentTarget, viveMenuButtonTransform;
  public GameObject controllerRep;
  bool usingOculus = false;
  public Color onColor = Color.HSVToRGB(208 / 359f, 234 / 255f, 93 / 255f);

  void Awake() {
    _menuspawn = GetComponent<menuspawn>();

    _touchpad.manip = this;
    activeTip.SetActive(false);
    Transform collViz = transform.Find("manipCollViz/viz");
    if (collViz != null) grabCollider = collViz.GetComponent<CapsuleCollider>();

    glowMenuTransform.position = viveMenuButtonTransform.position;
    glowMenuTransform.rotation = viveMenuButtonTransform.rotation;

    oculusSprites[0].material.SetColor("_TintColor", onColor);
    oculusSprites[0].material.SetFloat("_EmissionGain", .5f);
    oculusSprites[0].gameObject.SetActive(true);
    for (int i = 1; i < oculusSprites.Length; i++) {
      oculusSprites[i].material.SetColor("_TintColor", onColor);
      oculusSprites[i].material.SetFloat("_EmissionGain", .5f);
      oculusSprites[i].gameObject.SetActive(false);
    }

    oculusContextButtonGlow.GetComponent<Renderer>().material.SetColor("_TintColor", onColor);
    oculusContextButtonGlow.SetActive(false);
  }

  bool controllerVisible = true;
  public void toggleController(bool on) {
    controllerVisible = on;
    controllerRep.SetActive(on && !handsMode);
  }

  // Hand tracking: the controller model is hidden while hands drive this manipulator.
  // The grab tip pincers and the collider visual are hidden too: with hands the grab
  // point sits at the real pinch (see platformSetup.UpdateHandPoses).
  bool handsMode = false;
  public void SetHandsMode(bool on) {
    handsMode = on;
    if (on) {
      foreach (manipObject o in touchedObjects) if (o != null) TouchTracker.Touch(o, false, this);
      touchedObjects.Clear();
    }
    controllerRep.SetActive(controllerVisible && !on);
    if (on) toggleTips(false);
    activeTip.SetActive(on ? false : triggerDown);
    tipL.gameObject.SetActive(!on && !triggerDown);
    tipR.gameObject.SetActive(!on && !triggerDown);
    if (grabCollider != null) {
      foreach (Renderer r in grabCollider.transform.parent.GetComponentsInChildren<Renderer>(true)) r.enabled = !on;
      // The controller grab area is a 5 cm capsule along the pointing direction. With
      // hands that let it latch onto a neighbouring control, so shrink it to a 2 cm
      // sphere at the pinch.
      if (controllerGrabHeight < 0) controllerGrabHeight = grabCollider.height;
      grabCollider.height = on ? grabCollider.radius * 2f : controllerGrabHeight;
      SetPinchIndicator(on);
    }
  }

  float controllerGrabHeight = -1;

  // Hands: controls sit right next to the device's move handles, and the first hit used
  // to win (and then stuck). Prefer an actual control over a handle, and among those the
  // one nearest the pinch.
  Transform PickHandCandidate() {
    Vector3 p = GrabPointWorld;
    Transform best = null;
    bool bestIsHandle = true;
    float bestDist = float.MaxValue;
    foreach (Transform t in hitTransforms) {
      if (t == null) continue;
      manipObject o = t.GetComponent<manipObject>();
      if (o == null) continue;
      bool isCurrent = t == selectedTransform;
      if (!isCurrent && (o.curState == manipObject.manipState.grabbed || o.curState == manipObject.manipState.selected)) continue;
      bool isHandle = o is handle;
      float dist = (t.position - p).sqrMagnitude;
      if (best == null || (bestIsHandle && !isHandle) || (isHandle == bestIsHandle && dist < bestDist)) {
        best = t; bestIsHandle = isHandle; bestDist = dist;
      }
    }
    return best;
  }

  // Visible grab area in hands mode: dim when idle, brighter when hovering a control,
  // bright while grabbing. Matches the collider exactly (same transform, size).
  public static bool showPinchIndicator = true;
  GameObject pinchIndicator;
  Material pinchMat;
  static readonly Color pinchIdle = new Color(0.03f, 0.06f, 0.12f, 0.5f);
  static readonly Color pinchHover = new Color(0.08f, 0.16f, 0.30f, 0.5f);
  static readonly Color pinchGrab = new Color(0.20f, 0.35f, 0.60f, 0.5f);
  int pinchState = -1;

  void SetPinchIndicator(bool on) {
    if (!showPinchIndicator || grabCollider == null) return;
    if (pinchIndicator == null && on) {
      // Sphere, matching the grab collider in hands mode (capsule with height == 2 * radius).
      pinchIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere); // 1 unit across
      Destroy(pinchIndicator.GetComponent<Collider>());
      pinchIndicator.name = "pinchIndicator";
      pinchIndicator.transform.SetParent(grabCollider.transform, false);
      Renderer r = pinchIndicator.GetComponent<Renderer>();
      r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      Shader sh = Shader.Find("Sonic Ether/Particles/Additive");
      if (sh != null) {
        pinchMat = new Material(sh);
        pinchMat.SetFloat("_EmissionGain", 0.3f);
        r.sharedMaterial = pinchMat;
      }
      pinchState = -1;
    }
    if (pinchIndicator == null) return;
    pinchIndicator.SetActive(on);
    if (on) {
      float d = grabCollider.radius * 2f;
      pinchIndicator.transform.localPosition = grabCollider.center;
      pinchIndicator.transform.localScale = Vector3.one * d;
    }
  }

  void UpdatePinchIndicator() {
    if (pinchMat == null || !handsMode) return;
    int state = grabbing ? 2 : (selectedObject != null ? 1 : 0);
    if (state == pinchState) return;
    pinchState = state;
    pinchMat.SetColor("_TintColor", state == 2 ? pinchGrab : state == 1 ? pinchHover : pinchIdle);
  }

  // Centre of the grab collider (manipCollViz/viz) in world space.
  CapsuleCollider grabCollider;
  public Vector3 GrabPointWorld {
    get { return grabCollider != null ? grabCollider.transform.TransformPoint(grabCollider.center) : transform.position; }
  }

  public void SetDeviceIndex(int index) {
    controllerIndex = index;
  }

  bool grabbing;
  public bool emptyGrab;

  public bool isGrabbing() {
    return (selectedObject != null);
  }

  public void showTip() {
    if (!tipPackage.gameObject.activeSelf) tipPackage.gameObject.SetActive(true);
  }

  public void invertScale() {
    transform.parent.localScale = new Vector3(-1, 1, 1);
    Vector3 s = oculusSprites[0].gameObject.transform.localScale;
    s.x *= -1;
    oculusSprites[0].gameObject.transform.localScale = s;


    for (int i = 0; i < tipTexts.Length; i++) {
      s = tipTexts[i].gameObject.transform.localScale;
      s.x *= -1;
      tipTexts[i].gameObject.transform.localScale = s;
    }
  }

  void Grab(bool on) {
    grabbing = on;
    if (!on) { holdingTool = false; heldTool = null; heldToolKey = null; }
    showTip();

    if (selectedObject != null) {
      selectedObject.setGrab(grabbing, transform);
      if (!on) toggleController(true);
      else if (usingOculus) selectedObject.setTouch(true);
    } else emptyGrab = on;
  }

  public manipObject getSelection() {
    if (!grabbing) return null;
    return selectedObject;
  }

  public void ForceRelease() {
    showTip();
    if (grabbing == true) {
      release();

      grabbing = false;
    }
  }

  void release() {
    holdingTool = false; heldTool = null; heldToolKey = null;
    selectedObject.setState(manipObject.manipState.none);
    toggleController(true);
  }

  public void ForceGrab(manipObject o) {
    if (selectedObject != null) release();

    grabbing = true;
    selectedObject = o;
    selectedObject.setGrab(grabbing, transform);
    emptyGrab = false;
    selectedTransform = o.transform;
  }

  // Routed through TouchTracker so the grab tip and hand fingertips can share keys.
  // In hands mode the grab point (at the pinch) doesn't play keys by touch: the
  // fingertips do that, and the pinch point brushing past keys caused stray notes.
  // It still selects/grabs through OnCollisionStay as before.
  readonly HashSet<manipObject> touchedObjects = new HashSet<manipObject>();
  void OnCollisionEnter(Collision coll) {
    manipObject o = coll.transform.GetComponent<manipObject>();
    if (o != null && !handsMode && touchedObjects.Add(o)) TouchTracker.Touch(o, true, this);
  }

  void OnCollisionExit(Collision coll) {
    manipObject o = coll.transform.GetComponent<manipObject>();
    if (o != null && touchedObjects.Remove(o)) TouchTracker.Touch(o, false, this);
  }


  void OnCollisionStay(Collision coll) {
    hitTransforms.Add(coll.transform);
  }


  void FixedUpdate() {
    hitTransforms.Clear();
  }

  Coroutine pulseCoroutine;
  public void constantPulse(bool on) {
    if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
    if (on) {
      pulseCoroutine = StartCoroutine(pulseRoutine());
    }

  }

  IEnumerator pulseRoutine() {
    while (true) {
      hapticPulse(750);
      yield return new WaitForSeconds(0.3f);
    }
  }

  // Reused by hapticPulse/bigHaptic (called every frame by e.g. the control cube) to avoid allocating.
  List<UnityEngine.XR.InputDevice> hapticDevices = new List<UnityEngine.XR.InputDevice>();

  public void hapticPulse(ushort hapticPower = 750) {
        // if (masterControl.instance.currentPlatform == masterControl.platform.Vive) SteamVR_Controller.Input(controllerIndex).TriggerHapticPulse(hapticPower);
        //  else if (masterControl.instance.currentPlatform == masterControl.platform.Oculus) bigHaptic(hapticPower, .05f);
        List<UnityEngine.XR.InputDevice> devices = hapticDevices; // reused, the API clears it

        if (controllerIndex == 0)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, devices);
        }
        else if (controllerIndex == 1)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, devices);
        }

        foreach (var device in devices)
        {
            UnityEngine.XR.HapticCapabilities capabilities;
            if (device.TryGetHapticCapabilities(out capabilities))
            {
                if (capabilities.supportsImpulse)
                {
                    uint channel = 0;
                    float amplitude = hapticPower / 3999.0f;
                    float duration = 0.05f;
                    device.SendHapticImpulse(channel, amplitude, duration);
                }
            }
        }
    }
    
    public void bigHaptic(ushort hapticPower = 750, float dur = 0.1f) {
        //   if (_hapticCoroutine != null) StopCoroutine(_hapticCoroutine);
        //   _hapticCoroutine = StartCoroutine(hapticCoroutine(hapticPower, dur));
        List<UnityEngine.XR.InputDevice> devices = hapticDevices; // reused, the API clears it

        if (controllerIndex == 0)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, devices);
        }
        else if (controllerIndex == 1)
        {
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, devices);
        }

        foreach (var device in devices)
        {
            UnityEngine.XR.HapticCapabilities capabilities;
            if (device.TryGetHapticCapabilities(out capabilities))
            {
                if (capabilities.supportsImpulse)
                {
                    uint channel = 0;
                    float amplitude = hapticPower / 3999.0f;
                    device.SendHapticImpulse(channel, amplitude, dur);
                }
            }
        }
    }

  void LateUpdate() {
    UpdatePinchIndicator();
    if (grabbing) return;
    // Hands: once the pinch starts closing, keep the control that was highlighted, so
    // the fingers moving together can't hop the selection onto a neighbour.
    if (handsMode && selectedObject != null && HandInput.Get(controllerIndex).pinchIndex > 0.3f) return;
    Transform candidate = null;

    if (handsMode) {
      candidate = PickHandCandidate();
    } else if (selectedObject != null) {
      if (hitTransforms.Contains(selectedTransform)) candidate = selectedTransform;
    } else {
      foreach (Transform t in hitTransforms) {
        if (t != null) {
          manipObject o = t.GetComponent<manipObject>();
          if (o != null) {
            if (o.curState != manipObject.manipState.grabbed && o.curState != manipObject.manipState.selected) {
              candidate = t;
              break;
            }
          }
        }
      }
    }

    if (candidate != selectedTransform) {
      if (selectedObject != null) selectedObject.GetComponent<manipObject>().setSelect(false, transform);

      if (candidate != null) {
        candidate.GetComponent<manipObject>().setSelect(true, transform);
        if (candidate.GetComponent<handle>() != null) toggleCopy(true);
        else if (candidate.GetComponent<manipObject>().canBeDeleted) toggleDelete(true);
        hapticPulse();
        selectedTransform = candidate;
        selectedObject = candidate.GetComponent<manipObject>();
      } else {
        toggleCopy(false);
        toggleDelete(false);
        selectedTransform = null;
        selectedObject = null;
      }
    }
  }

  bool copyEnabled = false;
  void toggleCopy(bool on) {
    copyEnabled = on;
    if (!usingOculus) _touchpad.toggleCopy(on);
    else if (!copying) {
      oculusSprites[0].gameObject.SetActive(!on);
      oculusSprites[1].gameObject.SetActive(on);
      oculusSprites[2].gameObject.SetActive(false);
      oculusSprites[3].gameObject.SetActive(false);

    }

  }

  bool deleteEnabled = false;
  void toggleDelete(bool on) {
    if (multiselecting) return;

    deleteEnabled = on;
    if (!usingOculus) _touchpad.toggleDelete(on);
    else {
      oculusSprites[0].gameObject.SetActive(!on);
      oculusSprites[1].gameObject.SetActive(false);
      oculusSprites[2].gameObject.SetActive(on);
      oculusSprites[3].gameObject.SetActive(false);
    }

  }

  timelineGridUI multiselectGrid;

  bool multiselectEnabled = false;
  public void toggleMultiselect(bool on, timelineGridUI _grid) {
    if (!on && multiselecting) return;

    multiselectGrid = on ? _grid : null;
    multiselectEnabled = on;
    if (!usingOculus) _touchpad.toggleMultiselect(on);
    else {
      if (!deleteEnabled) oculusSprites[0].gameObject.SetActive(!on);
      oculusSprites[1].gameObject.SetActive(false);
      if (!deleteEnabled) oculusSprites[2].gameObject.SetActive(false);
      oculusSprites[3].gameObject.SetActive(on);
    }
  }

  public void SetTrigger(bool on) {
    triggerDown = on;

    if (selectedObject != null) {
      if (selectedObject.stickyGrip && selectedObject.curState == manipObject.manipState.grabbed) {
        if (on) Grab(!on);
        return;
      }
    }
    Grab(on);
  }

  bool multiselecting = false;
  public void MultiselectSelection(bool on) {
    multiselecting = on;

    if (multiselectGrid == null) {
      Debug.Log("HRM..");
      return;
    } else {
      multiselectGrid.onMultiselect(on, transform);
    }
  }

  bool deleting = false;
  public void DeleteSelection(bool on) {
    deleting = on;
    if (on && selectedObject != null) {
      selectedObject.selfdelete();
    }

    if (!on) {
      if (selectedObject == null) toggleDelete(false);
      else if (!selectedObject.canBeDeleted) toggleDelete(false);
    }
  }

  bool copying = false;
  public void SetCopy(bool on) {
    if (menuManager.instance.simple) return;

    copying = on;

    if (selectedObject != null) {
      if (on) {
        if (selectedObject.GetComponent<handle>() != null) SaveLoadInterface.instance.Copy(selectedObject.transform.parent.gameObject, this);
      } else if (selectedObject.GetComponent<handle>() != null && !triggerDown) Grab(false);
    }
  }

  void updateProngs() {
    float val = 0;
//    if (masterControl.instance.currentPlatform == masterControl.platform.Vive) val = SteamVR_Controller.Input(controllerIndex).GetAxis(EVRButtonId.k_EButton_Axis1).x;
            if (controllerIndex == 0)
            {
                val = QuestInput.GetAxis("triggerR");
            }
            else if (controllerIndex == 1)
            {
                val = QuestInput.GetAxis("triggerL");
            }
            else
            {
                val = 0;
            }

    if (!usingOculus) {
      triggerTrans.localRotation = Quaternion.Euler(Mathf.Lerp(0, 45, val), 180, 0);
    } else {
      triggerTrans.localRotation = Quaternion.Euler(Mathf.Lerp(0, -20, val), 0, 0);
    }
    tipL.localPosition = new Vector3(Mathf.Lerp(-.005f, 0, val), -.005f, -.018f);
    tipR.localPosition = new Vector3(Mathf.Lerp(.004f, -.001f, val), -.005f, -.018f);
  }

  bool showingTips = true;
  public void toggleTips(bool on) {
    showingTips = on;
    for (int i = 0; i < tipTexts.Length; i++) {
      tipTexts[i].SetActive(showingTips);
    }
    if (!usingOculus) _touchpad.setQuestionMark(showingTips);
  }


  public void changeHW(string s) {
    if (s == "oculus") {
      usingOculus = true;
      oculusObjects.SetActive(true);
      viveObjects.SetActive(false);
      glowMenuTransform.position = oculusMenuButton.transform.position;

      glowMenuTransform.rotation = oculusMenuButton.transform.rotation;
      glowMenuTransform.Translate(Vector3.up * .01f, Space.Self);
      tipPackage.localPosition = oculusManipTarget.localPosition;
      tipPackage.localRotation = oculusManipTarget.localRotation;
      triggerTrans = oculusTrigger.transform;
    }
  }

  // A held tool (drumstick, maraca, airhorn, microphone) is parented to this
  // manipulator's root. With hands, platformSetup turns the root with the hand's bones
  // (see HandFingertips.GetHoldPose) while holdingTool is set, and the tool's grip point
  // sits at the grab point, pointing along the hand, tilted down by handToolPitch.
  public bool holdingTool = false;
  public static float handToolPitch = 15f;
  public bool IsHandsMode { get { return handsMode; } }
  public int ControllerIndex { get { return controllerIndex; } }

  // Held tool (hands calibration, see platformSetup.UpdateToolCalibration).
  [System.NonSerialized] public Transform heldTool;
  [System.NonSerialized] public string heldToolKey;   // "drumstick", "mallet" or null
  [System.NonSerialized] public float heldToolLength; // butt (z = 0) to head, in root units
  [System.NonSerialized] public bool suppressHandTrigger;
  [System.NonSerialized] public bool heldToolAxisY;
  [System.NonSerialized] public Vector3 heldToolUpHint = Vector3.up;
  public Vector3 HeldToolAxis { get { return heldTool == null ? Vector3.forward : (heldToolAxisY ? heldTool.up : heldTool.forward); } } // the other hand is calibrating with this one

  // Point the held tool along dir (root space) with the point gripOffset along it at the
  // grab point.
  // dir is in the hand's hold frame (root rotation: forward along the hand, up = back of
  // the hand) using the RIGHT hand's convention; it is mirrored (x) for the left hand
  // here explicitly, so a calibration made in either hand is the mirror image in the
  // other. (It used to rely on the left rig's mirrored scale (-1,1,1), which came out
  // pointing down/backwards in the other hand.)
  public bool IsLeftHand { get { return controllerIndex == 0; } }
  public static Vector3 MirrorX(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }

  public void ApplyToolPose(Vector3 dir, float gripOffset) {
    if (heldTool == null || dir.sqrMagnitude < 1e-8f) return;
    Transform root = transform.parent;
    Quaternion frame = root.rotation; // hold pose (set by platformSetup), no scale
    Vector3 worldDir = frame * (IsLeftHand ? MirrorX(dir) : dir).normalized;
    Vector3 worldUp = Vector3.ProjectOnPlane(frame * heldToolUpHint, worldDir);
    if (worldUp.sqrMagnitude < 1e-6f) worldUp = Vector3.ProjectOnPlane(frame * Vector3.forward, worldDir);
    // Build the local rotation from local vectors so it holds under a mirrored parent:
    // the tool's long axis and up then point exactly along worldDir / worldUp.
    // InverseTransformVector includes the root's scale (the left rig is mirrored
    // (-1,1,1)), so the rendered tool really points along worldDir.
    Vector3 localDir = root.InverseTransformVector(worldDir);
    Vector3 localUp = root.InverseTransformVector(worldUp);
    heldTool.localRotation = Quaternion.LookRotation(localDir, localUp) * (heldToolAxisY ? Quaternion.Euler(90, 0, 0) : Quaternion.identity);
    heldTool.position = GrabPointWorld - worldDir * gripOffset;
  }

  public static void SaveToolCalibration(string key, Vector3 dir, float grip) {
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    PlayerPrefs.SetString("handTool." + key, dir.x.ToString("R", ci) + "," + dir.y.ToString("R", ci) + "," + dir.z.ToString("R", ci) + "," + grip.ToString("R", ci));
    PlayerPrefs.Save();
    Debug.Log("[manipulator] hand tool calibration saved for " + key + ": dir=" + dir.ToString("F3") + " grip=" + grip.ToString("F3"));
  }

  public static bool LoadToolCalibration(string key, ref Vector3 dir, ref float grip) {
    string v = PlayerPrefs.GetString("handTool." + key, "");
    string[] p = v.Split(',');
    if (p.Length != 4) return false;
    float x, y, z, g;
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out x) ||
        !float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out y) ||
        !float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out z) ||
        !float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out g)) return false;
    dir = new Vector3(x, y, z);
    grip = g;
    return true;
  }

  // gripOffset given: (hands) place the tool along the hand with that point of its +Z
  // axis at the grab point (drumsticks). Otherwise keep the controller layout, which in
  // hands mode still sits relative to the grab point at the pinch.
  // Returns true if the tool was placed for hands (callers then skip their controller-
  // specific Rotate/Translate). axisY: the tool's long axis is +Y (maraca, microphone)
  // instead of +Z (drumsticks).
  // pitch: default angle of the long axis (degrees, + = down) when not calibrated.
  // upHint (hand frame): which way the tool's roll should face (default up).
  public bool setVerticalPosition(Transform t, float gripOffset = float.NaN, string toolKey = null, float toolLength = 0f, bool axisY = false,
                                  float pitch = float.NaN, Vector3? upHint = null) {
    tipPackage.gameObject.SetActive(false);
    holdingTool = true;
    heldTool = t;
    heldToolKey = toolKey;
    heldToolLength = toolLength;
    heldToolAxisY = axisY;
    heldToolUpHint = upHint ?? Vector3.up;
    if (handsMode && grabCollider != null && !float.IsNaN(gripOffset)) {
      Vector3 dir = Quaternion.Euler(float.IsNaN(pitch) ? handToolPitch : pitch, 0, 0) * Vector3.forward;
      float grip = gripOffset;
      bool calibrated = toolKey != null && LoadToolCalibration(toolKey, ref dir, ref grip);
      ApplyToolPose(dir, grip);
      Transform r = transform.parent;
      Debug.Log("[manipulator] " + (IsLeftHand ? "left" : "right") + " hand holds " + (toolKey ?? "tool") +
                (calibrated ? " (calibrated)" : " (default)") + " dir=" + dir.ToString("F2") + " grip=" + grip.ToString("F3") +
                " handFwd=" + (r.rotation * Vector3.forward).ToString("F2") + " handUp=" + (r.rotation * Vector3.up).ToString("F2") +
                " rootScale=" + r.localScale.ToString("F0") + " toolFwd=" + heldTool.forward.ToString("F2"));
      return true;
    }
    if (usingOculus) {
      t.localPosition = oculusParentTarget.localPosition;
      t.localRotation = oculusParentTarget.localRotation;
    }
    return false;
  }

  bool touchpadActive = false;
  public void viveTouchpadUpdate() {


    bool tOn; // = SteamVR_Controller.Input(controllerIndex).GetTouchDown(SteamVR_Controller.ButtonMask.Touchpad);
    bool tOff; // = SteamVR_Controller.Input(controllerIndex).GetTouchUp(SteamVR_Controller.ButtonMask.Touchpad);

    bool pOn; // = SteamVR_Controller.Input(controllerIndex).GetPressDown(SteamVR_Controller.ButtonMask.Touchpad);
    bool pOff; // = SteamVR_Controller.Input(controllerIndex).GetPressUp(SteamVR_Controller.ButtonMask.Touchpad);

        if (controllerIndex == 0)
        {
            tOn = QuestInput.GetButtonDown("touchR");
            tOff = QuestInput.GetButtonUp("touchR");
            pOn = QuestInput.GetButtonDown("pressR");
            pOff = QuestInput.GetButtonUp("pressR");
        }
        else if (controllerIndex == 1)
        {
            tOn = QuestInput.GetButtonDown("touchL");
            tOff = QuestInput.GetButtonUp("touchL");
            pOn = QuestInput.GetButtonDown("pressL");
            pOff = QuestInput.GetButtonUp("pressL");
        }
        else
        {
            tOn = tOff = pOn = pOff = false;
        }
    bool activeManipObj = (grabbing && selectedObject != null);

    if (tOn) {
            touchpadActive = true;
      if (controllerVisible) _touchpad.setTouch(true);
      if (activeManipObj) selectedObject.setTouch(true);
    }
    if (tOff) {
            touchpadActive = false;
      if (controllerVisible) _touchpad.setTouch(false);
      if (activeManipObj) selectedObject.setTouch(false);
    }

    if (!touchpadActive) return;

    Vector2 pos; // = SteamVR_Controller.Input(controllerIndex).GetAxis();
        if (controllerIndex == 0)
        {
            pos = new Vector2(QuestInput.GetAxis("touchAxisXR"), QuestInput.GetAxis("touchAxisYR"));
        }
        else if (controllerIndex == 1)
        {
            pos = new Vector2(QuestInput.GetAxis("touchAxisXL"), QuestInput.GetAxis("touchAxisYL"));
        }
        else
        {
            pos = new Vector2(0.5f, 0.5f);
        }
    if (controllerVisible) _touchpad.updateTouchPos(pos);
    if (activeManipObj) selectedObject.updateTouchPos(pos);

    if (pOn) {

      if (controllerVisible) _touchpad.setPress(true);
      if (activeManipObj) selectedObject.setPress(true);
    }

    if (pOff) {
            if (controllerVisible) _touchpad.setPress(false);
      if (activeManipObj) selectedObject.setPress(false);
    }
  }

  void secondaryOculusButtonUpdate() {
    bool secondaryDown = false;
    bool secondaryUp = false;

    if (masterControl.instance.currentPlatform == masterControl.platform.Vive) {
//      secondaryDown = SteamVR_Controller.Input(controllerIndex).GetPressDown(SteamVR_Controller.ButtonMask.ApplicationMenu);
//      secondaryUp = SteamVR_Controller.Input(controllerIndex).GetPressUp(SteamVR_Controller.ButtonMask.ApplicationMenu);
                if (controllerIndex == 0)
                {
                    secondaryDown = QuestInput.GetButtonDown("secondaryButtonR");
                }
                else if (controllerIndex == 1)
                {
                    secondaryDown = QuestInput.GetButtonDown("secondaryButtonL");
                }
                else
                {
                    secondaryDown = false;
                }

                if (controllerIndex == 0)
                {
                    secondaryUp = QuestInput.GetButtonUp("secondaryButtonR");
                }
                else if (controllerIndex == 1)
                {
                    secondaryUp = QuestInput.GetButtonUp("secondaryButtonL");
                }
                else
                {
                    secondaryUp = false;
                }
    }
    if (controllerVisible) {
      if (secondaryDown) {
        if (copyEnabled) SetCopy(true);
        else if (deleteEnabled) DeleteSelection(true);
        else if (multiselectEnabled) MultiselectSelection(true);
        else toggleTips(true);
        oculusContextButtonGlow.SetActive(true);
      } else if (secondaryUp) {
        toggleTips(false);
        if (copying) SetCopy(false);
        else if (deleting) DeleteSelection(false);
        else if (multiselectEnabled) MultiselectSelection(false);
        oculusContextButtonGlow.SetActive(false);
      }
    } else if (grabbing && selectedObject != null) {
      if (secondaryDown) selectedObject.setPress(true);
      if (secondaryUp) selectedObject.setPress(false);
    }
  }

  public bool triggerDown = false;
    public bool pinchPinkyDown = false;
    // Per-manipulator (was static, so only the first manipulator ever saw a mode change).
    OVRPlugin.Controller lastControl = OVRPlugin.Controller.None;
    void Update() {
 
    updateProngs();
    bool triggerButtonDown, triggerButtonUp, menuButtonDown;

        OVRPlugin.Controller currentControl = OVRPlugin.GetActiveController(); //get current controller scheme
        bool currentControlHands = HandInput.HandsActive();
        bool currentControlChanged = false;
        float pinchIndexStrength = 0;
        float pinchPinkyStrength = 0;
        bool handLost = false;
        if (currentControlHands)
        {
            HandInput.Hand hand = HandInput.Get(controllerIndex);
            handLost = !hand.inputValid;
            pinchIndexStrength = suppressHandTrigger ? 0f : hand.pinchIndex;
            // Pinky pinch, or the system menu gesture on the left hand, opens the menu.
            pinchPinkyStrength = hand.menuPressed ? 1f : hand.pinchPinky;
        }
        if (currentControl != lastControl)
        {
            currentControlChanged = true;
            lastControl = currentControl;
        }
        if (masterControl.instance.currentPlatform == masterControl.platform.Vive) {
//      triggerButtonDown = SteamVR_Controller.Input(controllerIndex).GetPressDown(SteamVR_Controller.ButtonMask.Trigger);
//      triggerButtonUp = SteamVR_Controller.Input(controllerIndex).GetPressUp(SteamVR_Controller.ButtonMask.Trigger);
        if (!triggerDown)
        {
            if (currentControlHands)
            {
                triggerButtonDown = pinchIndexStrength > 0.85;
            }
            else
            {
                if (controllerIndex == 0)
                {
                    triggerButtonDown = QuestInput.GetAxis("triggerR") > 0.75;
                }
                else if (controllerIndex == 1)
                {
                    triggerButtonDown = QuestInput.GetAxis("triggerL") > 0.75;
                }
                else
                {
                    triggerButtonDown = false;
                }
            }
        }
        else
        {
            triggerButtonDown = false;
        }
        if (triggerDown)
        {
            if (currentControlHands)
            { 
                // release when the pinch opens or the hand drops out of tracking
                triggerButtonUp = pinchIndexStrength < 0.50 || handLost || currentControlChanged;
            }
            else if (!currentControlHands && currentControlChanged)
            {
                // force an trigger button up when we lose hands
                triggerButtonUp = true;
            }
            else
            {
                if (controllerIndex == 0)
                {
                    triggerButtonUp = QuestInput.GetAxis("triggerR") < 0.25;
                }
                else if (controllerIndex == 1)
                {
                    triggerButtonUp = QuestInput.GetAxis("triggerL") < 0.25;
                }
                else
                {
                    triggerButtonUp = false;
                }
            }
        }
        else
        {
            triggerButtonUp = false;
        }

            viveTouchpadUpdate();
            if (!usingOculus) {
                //        menuButtonDown = SteamVR_Controller.Input(controllerIndex).GetPressDown(SteamVR_Controller.ButtonMask.ApplicationMenu);
                if (currentControlHands)
                {
                    if (pinchPinkyStrength > 0.85)
                    {
                        if (pinchPinkyDown)
                        {
                            menuButtonDown = false;
                        }
                        else
                        {
                            menuButtonDown = true;
                            pinchPinkyDown = true;
                        }
                    }
                    else if (pinchPinkyStrength < 0.25)
                    {
                        pinchPinkyDown = false;
                        menuButtonDown = false;
                    }
                    else
                    {
                        menuButtonDown = false;
                    } 
                }
                else
                {
                    if (controllerIndex == 0)
                    {
                        menuButtonDown = QuestInput.GetButtonDown("menuButtonR");
                    }
                    else if (controllerIndex == 1)
                    {
                        menuButtonDown = QuestInput.GetButtonDown("menuButtonL");
                    }
                    else
                    {
                        menuButtonDown = false;
                    }
                }
      } else {
                Vector2 pos; // = SteamVR_Controller.Input(controllerIndex).GetAxis();
                if (controllerIndex == 0)
                {
                    pos = new Vector2(QuestInput.GetAxis("touchAxisXR"), QuestInput.GetAxis("touchAxisYR"));
                }
                else if (controllerIndex == 1)
                {
                    pos = new Vector2(QuestInput.GetAxis("touchAxisXL"), QuestInput.GetAxis("touchAxisYL"));
                }
                else
                {
                    pos = new Vector2(0.5f, 0.5f);
                }

                if (grabbing && selectedObject != null) selectedObject.updateTouchPos(pos);

        secondaryOculusButtonUpdate();
                //        menuButtonDown = SteamVR_Controller.Input(controllerIndex).GetPressDown(EVRButtonId.k_EButton_A);
                if (currentControlHands)
                {
                    if (pinchPinkyStrength > 0.85)
                    {
                        if (pinchPinkyDown)
                        {
                            menuButtonDown = false;
                        }
                        else
                        {
                            menuButtonDown = true;
                            pinchPinkyDown = true;
                        }
                    }
                    else if (pinchPinkyStrength < 0.25)
                    {
                        pinchPinkyDown = false;
                        menuButtonDown = false;
                    }
                    else
                    {
                        menuButtonDown = false;
                    }
                }
                else
                {
                    if (controllerIndex == 0)
                    {
                        menuButtonDown = QuestInput.GetButtonDown("menuButtonR");
                    }
                    else if (controllerIndex == 1)
                    {
                        menuButtonDown = QuestInput.GetButtonDown("menuButtonL");
                    }
                    else
                    {
                        menuButtonDown = false;
                    }
                }
      }
    } else {
      triggerButtonDown = triggerButtonUp = menuButtonDown = false;
    }

    if (triggerButtonDown) {
      if (!handsMode) {
        activeTip.SetActive(true);
        tipL.gameObject.SetActive(false);
        tipR.gameObject.SetActive(false);
      }
      SetTrigger(true);
    }
    if (triggerButtonUp) {
      if (!handsMode) {
        activeTip.SetActive(false);
        tipL.gameObject.SetActive(true);
        tipR.gameObject.SetActive(true);
      }
      SetTrigger(false);
    }

    if (menuButtonDown) _menuspawn.togglePad();

    if (grabbing && selectedObject != null) selectedObject.grabUpdate(transform);
    else if (selectedObject != null) selectedObject.selectUpdate(transform);
  }
}


