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

// Environment level and wind, set from the settings panel (added 2026-11).
// Creates itself after the scene loads; nothing needs adding to main.unity.
//   0 OFF        black background
//   1 STARS      the starfield skybox (the OVR rig cameras cleared to solid colour
//                after the Unity upgrade, so the stars had gone missing)
//   2 MOUNTAINS  stars + the "Landscape" mountains (inactive in the scene) with their
//                four directional lights, which only light the mountains' layer
//   3 FULL       mountains + the stage spotlight, which lights everything under it.
//                The stage's own directional light is dimmed to fullStageLight of its
//                intensity then: the spotlight was tuned for a scene without it, and
//                together they washed the devices out.
// Sky: NEBULA (the original SoundStage sky, Resources/Skyboxes/Nebula) or MILKY WAY
// (SkyBoxStars, made later in SpaceEngine), PlayerPrefs "sky".
// Wind: the landscape's wind loop now plays from its own source, so it can be on
// with or without the mountains (PlayerPrefs "envSound", as before).
// While passthrough is on, passthroughControl owns the cameras and hides the landscape;
// it calls Apply() again when it turns off.

using UnityEngine;

public class EnvironmentControl : MonoBehaviour {
  public static EnvironmentControl instance;

  public static readonly string[] levelNames = { "OFF", "STARS", "MOUNTAINS", "FULL" };
  public const int defaultLevel = 2;
  public int level { get; private set; }

  GameObject landscape;
  Light[] spotLights = new Light[0];
  Light[] stageLights = new Light[0];   // directional lights outside the landscape
  float[] stageIntensity = new float[0];
  public static float fullStageLight = 0.3f;
  public static readonly string[] skyNames = { "NEBULA", "MILKY WAY" };
  public int sky { get; private set; }
  Material[] skyMats = new Material[2];
  Material skyMat { get { return skyMats[sky] != null ? skyMats[sky] : (skyMats[0] != null ? skyMats[0] : skyMats[1]); } }
  AudioSource wind;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (instance != null) return;
    GameObject go = new GameObject("EnvironmentControl");
    DontDestroyOnLoad(go);
    go.AddComponent<EnvironmentControl>();
  }

  void Awake() {
    instance = this;
    level = Mathf.Clamp(PlayerPrefs.GetInt("environment", defaultLevel), 0, levelNames.Length - 1);
    sky = Mathf.Clamp(PlayerPrefs.GetInt("sky", 0), 0, skyNames.Length - 1);
  }

  void Start() {
    // the star material is on the old (inactive) rigs' Skybox components
    skyMats[0] = Resources.Load<Material>("Skyboxes/Nebula");
    foreach (Skybox s in FindObjectsOfType<Skybox>(true)) {
      if (s.material != null) { skyMats[1] = s.material; break; }
    }

    masterControl mc = masterControl.instance;
    AudioSource oldWind = mc != null ? mc.backgroundAudio : null;
    if (oldWind != null) {
      landscape = oldWind.transform.root.gameObject;
      // wind gets its own source so it doesn't depend on the mountains being shown
      wind = gameObject.AddComponent<AudioSource>();
      wind.clip = oldWind.clip;
      wind.loop = true;
      wind.spatialBlend = 0;
      wind.priority = oldWind.priority;
      wind.outputAudioMixerGroup = oldWind.outputAudioMixerGroup;
      wind.volume = PlayerPrefs.GetInt("envSound", 1) == 1 ? .02f : 0;
      if (wind.clip != null) wind.Play();
      oldWind.enabled = false;
      mc.backgroundAudio = wind; // masterControl.MuteBackgroundSFX keeps working
    } else {
      landscape = GameObject.Find("Landscape");
    }
    if (landscape != null) {
      var spots = new System.Collections.Generic.List<Light>();
      foreach (Light l in landscape.GetComponentsInChildren<Light>(true)) {
        if (l.type != LightType.Spot) continue;
        // The spotlight sits under the mountains (scaled 1 x 0.3 x 1) with a scale of
        // 100 x 100 x 333 and a 90 degree tilt, so its world matrix is skewed. Unity's
        // light culling then misjudges the cone and switched it off when standing in
        // one spot. Give it an unscaled parent of its own at the same place.
        GameObject holder = new GameObject("StageSpotlight");
        holder.transform.SetPositionAndRotation(l.transform.position, l.transform.rotation);
        l.transform.SetParent(holder.transform, false);
        l.transform.localPosition = Vector3.zero;
        l.transform.localRotation = Quaternion.identity;
        l.transform.localScale = Vector3.one;
        spots.Add(l);
      }
      spotLights = spots.ToArray();
    }
    var stage = new System.Collections.Generic.List<Light>();
    foreach (Light l in FindObjectsOfType<Light>(true)) {
      if (l.type != LightType.Directional) continue;
      if (landscape != null && l.transform.IsChildOf(landscape.transform)) continue;
      stage.Add(l);
    }
    stageLights = stage.ToArray();
    stageIntensity = new float[stageLights.Length];
    for (int i = 0; i < stageLights.Length; i++) stageIntensity[i] = stageLights[i].intensity;
    Apply();
  }

  public void SetLevel(int n) {
    level = ((n % levelNames.Length) + levelNames.Length) % levelNames.Length;
    PlayerPrefs.SetInt("environment", level);
    Apply();
  }

  public void Cycle() { SetLevel(level + 1); }

  public void CycleSky() {
    sky = (sky + 1) % skyNames.Length;
    PlayerPrefs.SetInt("sky", sky);
    Apply();
  }

  public bool WindOn { get { return PlayerPrefs.GetInt("envSound", 1) == 1; } }

  public void SetWind(bool on) {
    if (masterControl.instance != null && masterControl.instance.backgroundAudio != null) masterControl.instance.MuteBackgroundSFX(!on);
    else PlayerPrefs.SetInt("envSound", on ? 1 : 0);
    if (masterControl.instance != null && masterControl.instance.muteEnvToggle != null) masterControl.instance.muteEnvToggle.isOn = !on;
  }

  public void Apply() {
    bool passthrough = passthroughControl.instance != null && passthroughControl.instance.isOn;
    if (passthrough) { // passthroughControl has the cameras and the landscape
      foreach (Light l in spotLights) if (l != null) l.enabled = false; // no longer under the landscape
      for (int i = 0; i < stageLights.Length; i++) if (stageLights[i] != null) stageLights[i].intensity = stageIntensity[i];
      return;
    }

    Material mat = skyMat;
    bool showSky = level >= 1 && mat != null;
    RenderSettings.skybox = showSky ? mat : null;
    if (mat != null) foreach (Skybox s in FindObjectsOfType<Skybox>(true)) s.material = mat; // camera device etc.
    OVRCameraRig rig = FindObjectOfType<OVRCameraRig>();
    if (rig != null) {
      foreach (Camera c in rig.GetComponentsInChildren<Camera>(true)) {
        c.clearFlags = showSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        Color bg = c.backgroundColor; bg.a = 1; c.backgroundColor = bg;
      }
    }

    if (landscape != null) landscape.SetActive(level >= 2);
    bool spot = level >= 3 && spotLights.Length > 0;
    foreach (Light l in spotLights) if (l != null) l.enabled = spot;
    for (int i = 0; i < stageLights.Length; i++) {
      if (stageLights[i] != null) stageLights[i].intensity = stageIntensity[i] * (spot ? fullStageLight : 1f);
    }

    if (masterControl.instance != null) masterControl.instance.showEnvironment = level >= 1;
  }
}
