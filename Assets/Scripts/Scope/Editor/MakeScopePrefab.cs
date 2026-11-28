// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// Makes Resources/Prefabs/Scope.prefab from the oscillator (2026-11-22): menu
// SoundStage > Make Scope Prefab. Keeps the oscillator's body, display, little speaker and
// two of its jacks (FREQ becomes IN, the output becomes THRU); removes the slider and the
// volume jack; swaps the oscillator's scripts for the Scope's.
// (2026-11-27) Keeps the dials and switches as controls: four dials in a row under the
// screen (TIME, LEVEL, GAIN, POS: the oscillator's two and a copy of each, smaller, their
// Hz / dB pictures hidden and a text label under each) and the two side switches (AUTO /
// NORMAL, RISE / FALL). Keeps the help "?" with no video (HelpCards gives it a card).
// Run it again after changing the oscillator prefab or this script; it overwrites Scope.

using UnityEditor;
using UnityEngine;

public static class MakeScopePrefab {
  // control layout (Scope local units; the body is the oscillator's)
  const float DialSpacing = .0265f, DialY = .0005f, DialScale = .85f;
  const float FrontZ = .0549f, LabelDY = -.0165f, LabelScale = .042f;

  static void SetSwitchLabels(basicSwitch s, string on, string off) {
    if (s.onLabel != null) { TextMesh m = s.onLabel.GetComponent<TextMesh>(); if (m != null) m.text = on; }
    if (s.offLabel != null) { TextMesh m = s.offLabel.GetComponent<TextMesh>(); if (m != null) m.text = off; }
  }

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

      foreach (string n in new[] { "SliderContainer" }) {
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

      // the oscillator's VOLUME jack (on top, beside IN) becomes TRIG: an external trigger
      Transform trigT = t.Find("OmniJackAmp");
      if (trigT != null) {
        trigT.name = "OmniJackTrig";
        omniJack trig = trigT.GetComponent<omniJack>();
        trig.homesignal = sig;
        TextMesh tl = trigT.GetComponentInChildren<TextMesh>(true);
        if (tl != null) tl.text = "TRIG";
        dev.trigger = trig;
      }

      // ---- controls ----
      TextMesh labelSrc = input.GetComponentInChildren<TextMesh>(true);
      Transform freq = t.Find("dialContainerFreq"), amp = t.Find("dialContainerAmp");
      Transform gainC = Object.Instantiate(freq.gameObject, t, false).transform;
      Transform posC = Object.Instantiate(amp.gameObject, t, false).transform;
      // left to right as you face it (+x is the viewer's left)
      Transform[] dials = { freq, amp, gainC, posC };
      string[] names = { "dialTime", "dialLevel", "dialGain", "dialPos" };
      string[] captions = { "TIME", "LEVEL", "GAIN", "POS" };
      float[] defaults = { 0f, .5f, 1f / 3f, .5f }; // AUTO, level 0, x1, centred
      float[] xs = { DialSpacing * 1.5f, DialSpacing * .5f, -DialSpacing * .5f, -DialSpacing * 1.5f };
      for (int i = 0; i < 4; i++) {
        Transform c = dials[i];
        c.name = names[i];
        Vector3 p = c.localPosition; p.x = xs[i]; p.y = DialY; c.localPosition = p;
        c.localScale = Vector3.one * DialScale;
        Transform pic = c.Find("Label");
        if (pic != null) { Renderer r = pic.GetComponent<Renderer>(); if (r != null) r.enabled = false; } // the Hz / dB picture
        c.GetComponentInChildren<dial>(true).percent = defaults[i];
        if (labelSrc != null) {
          GameObject lab = Object.Instantiate(labelSrc.gameObject, t, false);
          lab.name = "label" + captions[i];
          TextMesh tm = lab.GetComponent<TextMesh>();
          tm.text = captions[i];
          tm.anchor = TextAnchor.MiddleCenter; // centred under its dial
          tm.alignment = TextAlignment.Center;
          lab.transform.localRotation = Quaternion.Euler(0, 180, 0); // read from the front
          lab.transform.localScale = Vector3.one * LabelScale;
          lab.transform.localPosition = new Vector3(xs[i], DialY + LabelDY, FrontZ);
        }
      }
      dev.timeDial = freq.GetComponentInChildren<dial>(true);
      dev.levelDial = amp.GetComponentInChildren<dial>(true);
      dev.gainDial = gainC.GetComponentInChildren<dial>(true);
      dev.posDial = posC.GetComponentInChildren<dial>(true);

      // the side switches: top AUTO / NORMAL, bottom RISE / FALL (on = the first)
      basicSwitch mode = t.Find("lfoRangeSwitch").GetComponent<basicSwitch>();
      basicSwitch slope = t.Find("switch").GetComponent<basicSwitch>();
      mode.name = "switchMode"; slope.name = "switchSlope";
      SetSwitchLabels(mode, "AUTO", "NORMAL");
      SetSwitchLabels(slope, "RISE", "FALL");
      slope.redOption = false; // falling isn't "off"
      mode.switchVal = true; slope.switchVal = true;
      dev.modeSwitch = mode;
      dev.slopeSwitch = slope;

      // the help "?" stays, with no video: HelpCards shows a card there
      tooltips tip = g.GetComponentInChildren<tooltips>(true);
      if (tip != null) tip.vidFile = "";

      PrefabUtility.SaveAsPrefabAsset(g, Target, out bool ok);
      Debug.Log(ok ? "[MakeScopePrefab] saved " + Target : "[MakeScopePrefab] couldn't save " + Target);
    } finally {
      Object.DestroyImmediate(g);
    }
  }
}
