// One-off prefab edit (2026-11). Run from the menu:
//   SoundStage > Add LFO range switch to oscillator
// Adds the LINEAR / SLOW LFO range switch (OscillatorRangeSwitch) to the Oscillator
// prefab. Safe to run again (skips it if it's already there). Afterwards it's an
// ordinary prefab object you can move in the editor.

using UnityEditor;
using UnityEngine;

public static class OscillatorPrefabTools {
  const string path = "Assets/Resources/Prefabs/Oscillator.prefab";

  [MenuItem("SoundStage/Add LFO range switch to oscillator")]
  static void AddRangeSwitch() {
    GameObject gain = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Gain.prefab");
    Transform src = gain != null ? gain.transform.Find("switch") : null;
    if (src == null) { Debug.LogError("Gain prefab switch not found"); return; }

    GameObject root = PrefabUtility.LoadPrefabContents(path);
    try {
      oscillatorDeviceInterface osc = root.GetComponent<oscillatorDeviceInterface>();
      if (osc == null) { Debug.LogError("Oscillator prefab has no oscillatorDeviceInterface"); return; }
      if (root.transform.Find("lfoRangeSwitch") != null) { Debug.Log("Oscillator already has lfoRangeSwitch"); return; }

      GameObject g = Object.Instantiate(src.gameObject, root.transform, false);
      g.name = "lfoRangeSwitch";
      g.transform.localPosition = OscillatorRangeSwitch.localPos;
      g.transform.localRotation = OscillatorRangeSwitch.localRot;
      g.transform.localScale = Vector3.one * OscillatorRangeSwitch.scale;
      basicSwitch sw = g.GetComponent<basicSwitch>();
      OscillatorRangeSwitch.Label(sw);
      osc.rangeSwitch = sw;

      PrefabUtility.SaveAsPrefabAsset(root, path);
      Debug.Log("Oscillator: added lfoRangeSwitch");
    } finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }
}
