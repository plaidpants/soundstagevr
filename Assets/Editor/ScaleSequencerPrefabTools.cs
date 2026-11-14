// One-off prefab edit (2026-11). Run from the menu:
//   SoundStage > Add Scale Sequencer parts to prefab
// Puts the parts the Scale Sequencer adds on top of the drum machine into its prefab:
// the sampler panel with the voice engine, the ADSR, the JOIN and LINK switches, the
// top-row handle and the scale name button. Safe to run again (it only adds the ones
// still missing). Afterwards they're ordinary prefab objects you can move in the editor.
// The top-row handle keeps its x/z offset from the corner handle and its height, as
// placed, when the grid is resized.

using UnityEditor;
using UnityEngine;

public static class ScaleSequencerPrefabTools {
  const string path = "Assets/Resources/Prefabs/ScaleSequencer.prefab";

  [MenuItem("SoundStage/Add Scale Sequencer parts to prefab")]
  static void AddParts() {
    GameObject root = PrefabUtility.LoadPrefabContents(path);
    try {
      scaleSequencerDeviceInterface seq = root.GetComponent<scaleSequencerDeviceInterface>();
      if (seq == null) { Debug.LogError("ScaleSequencer prefab has no scaleSequencerDeviceInterface"); return; }
      bool had = seq.samplerPanel != null && seq.adsr != null && seq.joinSwitch != null && seq.linkSwitch != null && seq.topHandle != null && seq.scaleButton != null;
      if (had) { Debug.Log("ScaleSequencer prefab already has all its parts"); return; }
      seq.BuildMissingParts();
      PrefabUtility.SaveAsPrefabAsset(root, path);
      Debug.Log("ScaleSequencer: added its parts to the prefab");
    } finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }
}
