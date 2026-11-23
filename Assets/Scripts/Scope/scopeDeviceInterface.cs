// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// Scope (2026-11-22): an oscilloscope to see what a signal looks like. One input, a THRU
// output (so it can sit in the middle of a chain), no controls: the display picks its own
// timebase (scopeViz). Built from the oscillator: its body and display, without the
// dials, switches and slider (Editor/MakeScopePrefab.cs makes Resources/Prefabs/Scope).

using UnityEngine;

public class scopeDeviceInterface : deviceInterface {
  public omniJack input, output;
  public scopeViz viz;
  scopeSignalGenerator signal;

  public override void Awake() {
    base.Awake();
    signal = GetComponent<scopeSignalGenerator>();
    signal.viz = viz;
    // the little speaker keeps the signal flowing when nothing is on THRU; it stays silent
    foreach (speaker s in GetComponentsInChildren<speaker>(true)) s.volume = 0;
  }

  void Update() {
    if (signal.incoming != input.signal) signal.incoming = input.signal;
  }

  public override InstrumentData GetData() {
    ScopeData data = new ScopeData();
    data.deviceType = menuItem.deviceType.Scope;
    GetTransformData(data);
    data.jackInID = input.transform.GetInstanceID();
    data.jackOutID = output.transform.GetInstanceID();
    return data;
  }

  public override void Load(InstrumentData d) {
    ScopeData data = d as ScopeData;
    base.Load(data);
    input.ID = data.jackInID;
    output.ID = data.jackOutID;
  }
}

public class ScopeData : InstrumentData {
  public int jackInID;
  public int jackOutID;
}
