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

// Quest runtime performance settings (added 2026-11).
//
// Creates itself automatically after the first scene loads, so nothing needs to be
// added to main.unity. Applies:
//   * Fixed foveated rendering (dynamic, up to High)
//   * An explicit display refresh rate
//   * Sustained CPU/GPU performance-level hints
// and re-applies them when the app regains focus, since the runtime can reset them.
//
// Note: while a full-screen OnRenderImage effect (the SE bloom) is active, the scene
// is rendered to an intermediate texture, so foveation has little or no effect until
// the bloom is replaced.

using UnityEngine;

public class QuestPerformanceSetup : MonoBehaviour {

  // 72 Hz gives the most frame-time headroom (13.9 ms). Raise to 90 once the
  // app holds frame rate comfortably; the request is ignored if unsupported.
  public static float targetRefreshRate = 72f;

  public static OVRManager.FoveatedRenderingLevel foveationLevel = OVRManager.FoveatedRenderingLevel.High;
  public static bool dynamicFoveation = true;

  public static OVRManager.ProcessorPerformanceLevel cpuLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
  public static OVRManager.ProcessorPerformanceLevel gpuLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;

#if UNITY_ANDROID && !UNITY_EDITOR
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    GameObject go = new GameObject("QuestPerformanceSetup");
    DontDestroyOnLoad(go);
    go.AddComponent<QuestPerformanceSetup>();
  }
#endif

  void Start() {
    Apply();
  }

  void OnApplicationPause(bool paused) {
    if (!paused) Apply();
  }

  void OnApplicationFocus(bool focused) {
    if (focused) Apply();
  }

  public static void Apply() {
    if (!OVRManager.isHmdPresent) return;

    if (OVRManager.fixedFoveatedRenderingSupported) {
      OVRManager.foveatedRenderingLevel = foveationLevel;
      OVRManager.useDynamicFoveatedRendering = dynamicFoveation;
    }

    float[] rates = OVRPlugin.systemDisplayFrequenciesAvailable;
    if (rates != null) {
      for (int i = 0; i < rates.Length; i++) {
        if (Mathf.Approximately(rates[i], targetRefreshRate)) {
          OVRPlugin.systemDisplayFrequency = targetRefreshRate;
          break;
        }
      }
    }

    OVRManager.suggestedCpuPerfLevel = cpuLevel;
    OVRManager.suggestedGpuPerfLevel = gpuLevel;

    Debug.Log("[QuestPerformanceSetup] FFR=" + OVRManager.foveatedRenderingLevel + " dynamic=" + OVRManager.useDynamicFoveatedRendering +
              " refresh=" + OVRPlugin.systemDisplayFrequency + " cpu=" + cpuLevel + " gpu=" + gpuLevel);
  }
}
