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

public class cameraDeviceInterface : deviceInterface {

  public Camera rtCam;
  public GameObject realCam, previewCam;
  public Transform rtQuad, screenTrans;
  public GameObject recLight, recMessage;
  public button broadcastButton, previewButton;

  omniJack input;
  signalGenerator externalPulse;
  float[] lastPlaySig;

  bool activated = false;

  [DllImport("SoundStageNative")]
  public static extern int CountPulses(float[] buffer, int length, int channels, float[] lastSig);

  public override void Awake() {
    base.Awake();
    lastPlaySig = new float[] { 0, 0 };
    input = GetComponentInChildren<omniJack>();
    recLight.GetComponent<Renderer>().material.SetColor("_TintColor", Color.HSVToRGB(0, 152 / 255f, 69 / 255f));
    recLight.SetActive(false);
    recMessage.SetActive(false);
    camSetup();

    // The preview camera is rendered by hand in LateUpdate (throttled) instead of every frame.
    rtCam.enabled = false;
    rtQuadRenderer = rtQuad.GetComponent<Renderer>();
    frameOffset = (GetInstanceID() & 0x7fffffff) % 3;
#if UNITY_ANDROID && !UNITY_EDITOR
    // "Broadcast" renders a full-screen view for a desktop monitor. The headset has no
    // such output, so on Quest it was a second full scene render with nobody watching.
    Camera rc = realCam.GetComponent<Camera>();
    if (rc != null) rc.enabled = false;
#endif

  }

  void Start() {

    bool sky = masterControl.instance.showEnvironment;
    Camera[] cams = GetComponentsInChildren<Camera>();
    for (int i = 0; i < cams.Length; i++) {
      if (cams[i].GetComponent<Skybox>() != null) cams[i].GetComponent<Skybox>().enabled = sky;
    }
    realCam.GetComponent<Skybox>().enabled = sky;
    activated = true;
  }

  // Preview throttling (2026-11): the camera device drew the whole scene again every
  // frame; in a 34-device patch that alone took the headset from 72 to 33 fps.
  // Now the preview renders at previewFrameDivisor of the display rate (24 fps at
  // 72 Hz), and not at all while its screen is out of view or the preview is off.
  public static int previewFrameDivisor = 3;
  Renderer rtQuadRenderer;
  int frameOffset;
  Plane[] viewPlanes = new Plane[6];

  void LateUpdate() {
    if (!preview || rtCam == null || !rtCam.gameObject.activeInHierarchy) return;
    if (rtCam.targetTexture == null) return;
    int div = Mathf.Max(1, previewFrameDivisor);
    if ((Time.frameCount + frameOffset) % div != 0) return;
    if (!ScreenInView()) return;
    rtCam.Render();
  }

  bool ScreenInView() {
    if (rtQuadRenderer == null || !rtQuadRenderer.gameObject.activeInHierarchy) return false;
    Camera head = Camera.main;
    if (head == null) return true;
    GeometryUtility.CalculateFrustumPlanes(head, viewPlanes);
    Bounds b = rtQuadRenderer.bounds;
    b.Expand(0.3f); // margin: the head camera's mono frustum is a little narrower than both eyes
    return GeometryUtility.TestPlanesAABB(viewPlanes, b);
  }

  void OnDestroy() {
    if (screenTrans != null && activated) Destroy(screenTrans.gameObject);
  }

  void Update() {
    if (input.signal != externalPulse) externalPulse = input.signal;
    if (hits > 0) {
      if (hits % 2 != 0) toggleRealCam(!broadcasting);
      hits = 0;
    }
  }

  void OnDisable() {
    if (broadcasting) toggleRealCam(false);
  }

  bool preview = true;
  void togglePreview(bool on) {
    preview = on;
    previewCam.SetActive(on);
    screenTrans.gameObject.SetActive(on);
  }

  public override void hit(bool on, int ID = -1) {
    if (ID == 0) toggleRealCam(on);
    if (ID == 1) togglePreview(on);
  }

  bool broadcasting = false;
  public void toggleRealCam(bool on) {
    if (broadcasting == on) return;
    broadcasting = on;
    if (on) {
      realCam.GetComponent<Skybox>().enabled = masterControl.instance.showEnvironment;
      cameraDeviceInterface[] cams = FindObjectsOfType<cameraDeviceInterface>();
      for (int i = 0; i < cams.Length; i++) {
        if (cams[i] != this) {
          cams[i].toggleRealCam(false);
          cams[i].broadcastButton.phantomHit(false);
        }
      }
    }

    broadcastButton.phantomHit(on);
    recLight.SetActive(on);
    recMessage.SetActive(on);
    realCam.SetActive(on);
  }

  bool curHiRes = false;
  public void updateResolution(bool hires) {
    if (curHiRes == hires) return;
    camSetup(hires);
  }

  void camSetup(bool hires = false) {
    curHiRes = hires;

    if (rtCam.targetTexture != null) {
      rtCam.targetTexture.Release();
    }
    int mult = hires ? 32 : 16;
    rtCam.targetTexture = new RenderTexture(16 * mult, 9 * mult, 16);
    rtQuad.GetComponent<Renderer>().material.mainTexture = rtCam.targetTexture;
  }



  int hits = 0;
  float[] _scratch_playBuffer; // reused audio-thread scratch (see AudioBufferPool)
  private void OnAudioFilterRead(float[] buffer, int channels) {
    if (externalPulse == null) return;
    double dspTime = AudioSettings.dspTime;

    float[] playBuffer = AudioBufferPool.GetCleared(ref _scratch_playBuffer, buffer.Length);
    externalPulse.processBuffer(playBuffer, dspTime, channels);

    hits += CountPulses(playBuffer, buffer.Length, channels, lastPlaySig);
  }


  public override InstrumentData GetData() {
    CameraData data = new CameraData();
    data.deviceType = menuItem.deviceType.Camera;
    GetTransformData(data);

    data.inputID = input.transform.GetInstanceID();

    data.screenPosition = screenTrans.localPosition;
    data.screenRotation = screenTrans.localRotation;
    data.screenScale = screenTrans.localScale;
    data.screenDisabled = !preview;
    return data;
  }

  public override void Load(InstrumentData d) {
    CameraData data = d as CameraData;
    base.Load(data);

    previewButton.startToggled = !data.screenDisabled;

    screenTrans.localPosition = data.screenPosition;
    screenTrans.localRotation = data.screenRotation;
    screenTrans.localScale = data.screenScale;
    input.ID = data.inputID;
  }
}

public class CameraData : InstrumentData {
  public Vector3 screenPosition;
  public Vector3 screenScale;
  public Quaternion screenRotation;
  public int inputID;
  public bool screenDisabled;
}
