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

// Passthrough (mixed reality) mode, toggled from the settings panel (added 2026-11).
//
// Creates itself after the scene loads; nothing needs adding to main.unity.
// When on:
//   * starts Insight Passthrough on OVRManager and shows it through an
//     OVRPassthroughLayer underlay on the camera rig
//   * eye cameras clear to transparent black instead of drawing the starfield skybox
//   * hides the ground plane / landscape
//   * switches the SE bloom to keep the frame's alpha (so the glow spills over the
//     real world instead of making the frame opaque)
// Passthrough is started the first time it's turned on and then only shown/hidden via
// the layer for the rest of the session. Tearing the passthrough system down and
// re-creating it on every toggle (via OVRManager.isInsightPassthroughEnabled) hung
// the app after a few toggles.
// The choice is saved in PlayerPrefs("passthrough"). Passthrough needs a 64-bit build.

using UnityEngine;
using System.Collections.Generic;

public class passthroughControl : MonoBehaviour {
  public static passthroughControl instance;

  public bool isOn { get; private set; }

  OVRPassthroughLayer layer;

  class CamState { public Camera cam; public CameraClearFlags flags; public Color color; public Skybox sky; public bool skyEnabled; }
  List<CamState> cams;
  List<GameObject> hiddenObjects = new List<GameObject>();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (instance != null) return;
    GameObject go = new GameObject("passthroughControl");
    DontDestroyOnLoad(go);
    go.AddComponent<passthroughControl>();
  }

  void Awake() {
    instance = this;
  }

  void Start() {
    bool want = PlayerPrefs.GetInt("passthrough", 0) == 1;
    // The scene's OVRManager has passthrough enabled. If the player hasn't turned it
    // on, don't start it at all (no layer exists yet, so this is safe).
    if (!want && OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = false;
    SetPassthrough(want, false);
  }

  public bool Toggle() {
    SetPassthrough(!isOn);
    return isOn;
  }

  public void SetPassthrough(bool on, bool save = true) {
    isOn = on;
    if (save) PlayerPrefs.SetInt("passthrough", on ? 1 : 0);

    OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
    if (on) {
      // Start passthrough once; it stays initialised for the rest of the session.
      if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;
      if (layer == null && rig != null) {
        layer = rig.gameObject.AddComponent<OVRPassthroughLayer>();
        layer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
        layer.overlayType = OVROverlay.OverlayType.Underlay;
        layer.compositionDepth = 0;
      }
    }
    // Only show/hide the layer; never disable it or shut the system down mid-session.
    if (layer != null) layer.hidden = !on;

    UpdateCameras(rig, on);
    UpdateEnvironment(on);

    if (masterControl.instance != null) {
      masterControl.instance.passthroughActive = on;
      if (masterControl.instance.mainGlowShader != null) masterControl.instance.mainGlowShader.preserveAlpha = on;
    }

    Debug.Log("[passthroughControl] passthrough " + (on ? "ON" : "OFF"));

    // The runtime caps CPU/GPU levels while passthrough runs (seen 4/4 -> 3/2) and does
    // not raise them again when it stops, so re-request ours once passthrough is off.
    if (!on) {
      CancelInvoke("ReapplyPerformance");
      Invoke("ReapplyPerformance", 0.5f);
      Invoke("ReapplyPerformance", 2f);
    }
  }

  void ReapplyPerformance() {
    if (masterControl.instance != null && masterControl.instance.passthroughActive) return;
    QuestPerformanceSetup.Apply();
  }

  void UpdateCameras(OVRCameraRig rig, bool on) {
    if (cams == null) {
      cams = new List<CamState>();
      Camera[] found = rig != null ? rig.GetComponentsInChildren<Camera>(true) : new Camera[] { Camera.main };
      foreach (Camera c in found) {
        if (c == null) continue;
        Skybox s = c.GetComponent<Skybox>();
        cams.Add(new CamState { cam = c, flags = c.clearFlags, color = c.backgroundColor, sky = s, skyEnabled = s != null && s.enabled });
      }
    }
    foreach (CamState st in cams) {
      if (st.cam == null) continue;
      if (on) {
        st.cam.clearFlags = CameraClearFlags.SolidColor;
        st.cam.backgroundColor = new Color(0, 0, 0, 0);
        if (st.sky != null) st.sky.enabled = false;
      } else {
        st.cam.clearFlags = st.flags;
        // Opaque in VR mode: the scene's original background alpha is 0 (Unity default).
        // With the glow (bloom) off nothing forced alpha to 1, so the eye buffer was
        // see-through and the headset showed the real room as if passthrough were on.
        Color c = st.color;
        c.a = 1f;
        st.cam.backgroundColor = c;
        if (st.sky != null) st.sky.enabled = st.skyEnabled;
      }
    }
  }

  void UpdateEnvironment(bool on) {
    if (masterControl.instance != null) masterControl.instance.showEnvironment = !on;

    if (on) {
      hiddenObjects.Clear();
      if (imageLoad.instance != null && imageLoad.instance.groundPlane != null) Hide(imageLoad.instance.groundPlane);
      Hide(GameObject.Find("Landscape"));
    } else {
      foreach (GameObject g in hiddenObjects) if (g != null) g.SetActive(true);
      hiddenObjects.Clear();
    }
  }

  void Hide(GameObject g) {
    if (g == null || !g.activeSelf || hiddenObjects.Contains(g)) return;
    g.SetActive(false);
    hiddenObjects.Add(g);
  }
}
