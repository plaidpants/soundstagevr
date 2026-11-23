// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// Makes Resources/Prefabs/Scope.prefab from the oscillator (2026-11-22): menu
// SoundStage > Make Scope Prefab. Keeps the oscillator's body, display, little speaker and
// two of its jacks (FREQ becomes IN, the output becomes THRU); removes the dials,
// switches, slider and help tooltip; swaps the oscillator's scripts for the Scope's.
// Run it again after changing the oscillator prefab or this script; it overwrites Scope.

using UnityEditor;
using UnityEngine;

public static class MakeScopePrefab {
  const string Source = "Assets/Resources/Prefabs/Oscillator.prefab";
  const string Target = "Assets/Resources/Prefabs/Scope.prefab";

  [MenuItem("SoundStage/Make Scope Prefab")]
  static void Make() {
    GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
    if (src == null) { Debug.LogError("[MakeScopePrefab] " + Source + " not found"); return; }
    GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(src);
    PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    try {
      g.name = "Scope";
      Transform t = g.transform;

      foreach (string n in new[] { "switch", "lfoRangeSwitch", "dialContainerFreq", "dialContainerAmp", "SliderContainer", "OmniJackAmp", "tooltip", "tooltipVideo" }) {
        Transform c = t.Find(n);
        if (c != null) Object.DestroyImmediate(c.gameObject);
        else Debug.LogWarning("[MakeScopePrefab] no " + n + " in the oscillator");
      }

      Transform display = t.Find("waveDisplay");
      omniJack input = t.Find("OmniJackFreq").GetComponent<omniJack>();
      omniJack output = t.Find("OmniJackOut").GetComponent<omniJack>();
      input.name = "OmniJackIn";
      output.name = "OmniJackThru";

      // the oscillator's scripts out, the Scope's in
      for (int pass = 0; pass < 3; pass++) // (a script another one requires goes on a later pass)
        foreach (MonoBehaviour m in g.GetComponents<MonoBehaviour>()) if (m != null && !(m is scopeSignalGenerator)) Object.DestroyImmediate(m);
      scopeSignalGenerator sig = g.AddComponent<scopeSignalGenerator>();
      scopeViz viz = g.AddComponent<scopeViz>();
      viz.waverend = display.GetComponent<Renderer>();
      scopeDeviceInterface dev = g.AddComponent<scopeDeviceInterface>();
      dev.input = input;
      dev.output = output;
      dev.viz = viz;
      sig.viz = viz;
      input.homesignal = sig;
      output.homesignal = sig;

      // jack labels: IN, and a copy of it on THRU
      TextMesh inLabel = input.GetComponentInChildren<TextMesh>(true);
      if (inLabel != null) {
        inLabel.text = "IN";
        GameObject thru = Object.Instantiate(inLabel.gameObject, output.transform, false);
        thru.name = "label";
        thru.GetComponent<TextMesh>().text = "THRU";
      }

      // the little speaker stays (it keeps the signal flowing when nothing is on THRU,
      // at zero volume) but isn't shown
      Transform baby = t.Find("babySpeaker");
      if (baby != null) foreach (Renderer r in baby.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

      PrefabUtility.SaveAsPrefabAsset(g, Target, out bool ok);
      Debug.Log(ok ? "[MakeScopePrefab] saved " + Target : "[MakeScopePrefab] couldn't save " + Target);
    } finally {
      Object.DestroyImmediate(g);
    }
  }
}
