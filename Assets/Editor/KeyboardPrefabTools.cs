// One-off prefab edit (2026-11). Run from the menu:
//   SoundStage > Give keyboard an octave slider
// Copies the xyloroll's 5-notch octave slider (-2..+2) into the Keyboard prefab where
// the old on/off octave switch was, and turns the switch off. Safe to run again (it
// skips a prefab that already has the slider). Afterwards it's an ordinary prefab
// object you can move in the editor.

using UnityEditor;
using UnityEngine;

public static class KeyboardPrefabTools {
  const string keyboardPath = "Assets/Resources/Prefabs/Keyboard.prefab";
  const string xyloPath = "Assets/Resources/Prefabs/XyloRoll.prefab";

  [MenuItem("SoundStage/Give keyboard an octave slider")]
  static void AddOctaveSlider() {
    GameObject xylo = AssetDatabase.LoadAssetAtPath<GameObject>(xyloPath);
    Transform src = xylo != null ? xylo.transform.Find("OctaveSlider") : null;
    if (src == null) { Debug.LogError("XyloRoll OctaveSlider not found"); return; }

    GameObject root = PrefabUtility.LoadPrefabContents(keyboardPath);
    try {
      keyboardDeviceInterface kb = root.GetComponent<keyboardDeviceInterface>();
      if (kb == null) { Debug.LogError("Keyboard prefab has no keyboardDeviceInterface"); return; }
      if (root.transform.Find("OctaveSlider") != null) { Debug.Log("Keyboard already has OctaveSlider"); return; }

      Transform sw = root.transform.Find("switch");
      GameObject g = Object.Instantiate(src.gameObject, root.transform, false);
      g.name = "OctaveSlider";
      g.transform.localPosition = sw != null ? sw.localPosition : new Vector3(-0.514f, 0.0151f, -0.099f);
      g.transform.localRotation = src.localRotation;
      g.transform.localScale = src.localScale;

      sliderNotched slider = g.GetComponentInChildren<sliderNotched>(true);
      slider.switchVal = 2; // middle = no shift (the xyloroll's slider already sits there)
      kb.octaveSlider = slider;
      if (sw != null) sw.gameObject.SetActive(false);

      PrefabUtility.SaveAsPrefabAsset(root, keyboardPath);
      Debug.Log("Keyboard: added OctaveSlider, old octave switch disabled");
    } finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }
}
