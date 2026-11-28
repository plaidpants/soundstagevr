// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// Scope (2026-11-22): an oscilloscope to see what a signal looks like. One input, a THRU
// output (so it can sit in the middle of a chain). Built from the oscillator: its body,
// display, dials and switches (Editor/MakeScopePrefab.cs makes Resources/Prefabs/Scope).
// Controls (2026-11-27): TIME (far left AUTO, then 0.1 ms to 2 s across the screen),
// LEVEL (trigger level), GAIN (x0.25 to x16), POS (slides the picture in time, the middle
// = the trigger at the left edge as before); switches AUTO / NORMAL
// and RISE / FALL. Shared like every dial and switch (NetControls) and saved.

using UnityEngine;

public class scopeDeviceInterface : deviceInterface {
  public omniJack input, output, trigger; // trigger: TRIG, an external trigger (2026-11-27)
  public scopeViz viz;
  public dial timeDial, levelDial, gainDial, posDial;
  public basicSwitch modeSwitch, slopeSwitch;
  scopeSignalGenerator signal;

  public const float AutoBelow = .04f; // TIME dial: AUTO below this
  public static float Seconds(float p) { return 1e-4f * Mathf.Pow(2e4f, Mathf.Clamp01((p - AutoBelow) / (1f - AutoBelow))); }
  public static float Gain(float p) { return .25f * Mathf.Pow(64f, p); }
  static string TimeText(float p) {
    if (p < AutoBelow) return "AUTO";
    float s = Seconds(p);
    return s < .001f ? (s * 1000f).ToString("F2") + " ms" : s < 1f ? (s * 1000f).ToString("F0") + " ms" : s.ToString("F2") + " s";
  }

  public override void Awake() {
    base.Awake();
    signal = GetComponent<scopeSignalGenerator>();
    signal.viz = viz;
    // the little speaker keeps the signal flowing when nothing is on THRU; it stays silent
    foreach (speaker s in GetComponentsInChildren<speaker>(true)) s.volume = 0;
    if (timeDial != null) timeDial.valueText = TimeText;
    if (levelDial != null) levelDial.valueText = p => "LEVEL " + (p * 2f - 1f).ToString("+0.00;-0.00");
    if (gainDial != null) gainDial.valueText = p => "x" + Gain(p).ToString(Gain(p) < 1f ? "F2" : "F1");
    if (posDial != null) posDial.valueText = p => {
      float sh = p * 2f - 1f;
      return Mathf.Abs(sh) < .01f ? "TRIGGER AT LEFT" : sh > 0 ? "BEFORE TRIGGER " + Mathf.RoundToInt(sh * 100) + "%" : "AFTER TRIGGER " + Mathf.RoundToInt(-sh * 100) + "%";
    };
  }

  void Update() {
    if (signal.incoming != input.signal) signal.incoming = input.signal;
    if (trigger != null && signal.trigger != trigger.signal) signal.trigger = trigger.signal;
    if (viz == null) return;
    if (timeDial != null) { viz.timeAuto = timeDial.percent < AutoBelow; viz.seconds = Seconds(timeDial.percent); }
    if (levelDial != null) viz.triggerLevel = levelDial.percent * 2f - 1f;
    if (gainDial != null) viz.gain = Gain(gainDial.percent);
    if (posDial != null) viz.shift = posDial.percent * 2f - 1f;
    viz.position = 0f;
    if (modeSwitch != null) viz.autoMode = modeSwitch.switchVal;
    if (slopeSwitch != null) viz.rising = slopeSwitch.switchVal;
  }

  void Start() { HelpCards.Attach(this, "scope"); } // a "?" with a written card (no help video)

  public override InstrumentData GetData() {
    ScopeData data = new ScopeData();
    data.deviceType = menuItem.deviceType.Scope;
    GetTransformData(data);
    data.jackInID = input.transform.GetInstanceID();
    data.jackOutID = output.transform.GetInstanceID();
    if (trigger != null) data.jackTrigID = trigger.transform.GetInstanceID();
    if (timeDial != null) {
      data.hasControls = true;
      data.time = timeDial.percent; data.level = levelDial.percent; data.gain = gainDial.percent; data.pos = posDial.percent;
      data.auto = modeSwitch.switchVal; data.rising = slopeSwitch.switchVal;
    }
    return data;
  }

  public override void Load(InstrumentData d) {
    ScopeData data = d as ScopeData;
    base.Load(data);
    input.ID = data.jackInID;
    output.ID = data.jackOutID;
    if (trigger != null && data.jackTrigID != 0) trigger.ID = data.jackTrigID;
    if (data.hasControls && timeDial != null) { // (scopes saved before 2026-11-27 keep the defaults)
      timeDial.setPercent(data.time); levelDial.setPercent(data.level); gainDial.setPercent(data.gain); posDial.setPercent(data.pos);
      modeSwitch.setSwitch(data.auto); slopeSwitch.setSwitch(data.rising);
    }
  }
}

public class ScopeData : InstrumentData {
  public int jackInID;
  public int jackOutID;
  public bool hasControls;
  public int jackTrigID;
  public float time, level, gain, pos;
  public bool auto, rising;
}
