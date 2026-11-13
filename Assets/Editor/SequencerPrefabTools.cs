// One-off prefab edits for the sequencers (2026-11). Run from the menu:
//   SoundStage > Add LOOP switch to sequencers
//   SoundStage > Add dark backing to CV Sequencer dials
// Both are safe to run again (they skip prefabs that already have the part).
// Afterwards the parts are ordinary prefab objects you can move in the editor.

using UnityEditor;
using UnityEngine;

public static class SequencerPrefabTools {
  static readonly string[] sequencerPrefabs = {
    "Assets/Resources/Prefabs/Sequencer.prefab",
    "Assets/Resources/Prefabs/CVSequencer.prefab",
  };

  [MenuItem("SoundStage/Add LOOP switch to sequencers")]
  static void AddLoopSwitch() {
    GameObject gain = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/Gain.prefab");
    Transform src = gain != null ? gain.transform.Find("switch") : null;
    if (src == null) { Debug.LogError("Gain prefab switch not found"); return; }

    foreach (string path in sequencerPrefabs) {
      GameObject root = PrefabUtility.LoadPrefabContents(path);
      try {
        if (root.transform.Find("loopSwitch") != null) { Debug.Log(path + ": already has loopSwitch"); continue; }
        GameObject g = Object.Instantiate(src.gameObject, root.transform, false);
        g.name = "loopSwitch";
        g.transform.localPosition = SequencerLoopSwitch.localPos;
        g.transform.localRotation = SequencerLoopSwitch.localRot;
        g.transform.localScale = Vector3.one * SequencerLoopSwitch.scale;
        basicSwitch sw = g.GetComponent<basicSwitch>();
        SequencerLoopSwitch.Label(sw);

        sequencerDeviceInterface seq = root.GetComponent<sequencerDeviceInterface>();
        if (seq != null) seq.loopSwitch = sw;
        cvSequencerDeviceInterface cv = root.GetComponent<cvSequencerDeviceInterface>();
        if (cv != null) cv.loopSwitch = sw;

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Debug.Log(path + ": added loopSwitch");
      } finally {
        PrefabUtility.UnloadPrefabContents(root);
      }
    }
  }

  [MenuItem("SoundStage/Add dark backing to CV Sequencer dials")]
  static void AddDialBacking() {
    const string path = "Assets/Prefabs/sequencer/touchDial.prefab";
    Material dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/coreMaterials/coreMatDark.mat");
    GameObject root = PrefabUtility.LoadPrefabContents(path);
    try {
      Transform container = root.transform.Find("dialContainer");
      Transform disk = container != null ? container.Find("glowDisk") : null;
      if (disk == null) { Debug.LogError("touchDial glowDisk not found"); return; }
      if (container.Find("dialBacking") != null) { Debug.Log(path + ": already has dialBacking"); return; }

      // Opaque copy of the glow disk just behind it, so the dial stays readable when
      // the cube behind it is lit (the additive arc vanished on a white cube).
      GameObject b = new GameObject("dialBacking");
      b.transform.SetParent(container, false);
      b.transform.localPosition = disk.localPosition + new Vector3(0, -0.001f, 0); // -y is away from the viewer here (into the cube)
      b.transform.localRotation = disk.localRotation;
      b.transform.localScale = disk.localScale;
      b.AddComponent<MeshFilter>().sharedMesh = disk.GetComponent<MeshFilter>().sharedMesh;
      MeshRenderer r = b.AddComponent<MeshRenderer>();
      r.sharedMaterial = dark;
      r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

      PrefabUtility.SaveAsPrefabAsset(root, path);
      Debug.Log(path + ": added dialBacking");
    } finally {
      PrefabUtility.UnloadPrefabContents(root);
    }
  }
}
