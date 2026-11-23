// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// The Scope's signal (2026-11-22): passes its input through unchanged (THRU) and shows
// it on the display. With nothing on THRU its little built-in speaker pulls the signal
// instead, at zero volume (scopeDeviceInterface), so the display runs either way.

using UnityEngine;

public class scopeSignalGenerator : signalGenerator {
  public signalGenerator incoming;
  public scopeViz viz;

  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    signalGenerator inc = incoming; // (read once: a cable pulled on the main thread can clear it)
    if (inc != null) inc.Pull(buffer, dspTime, channels);
    else System.Array.Clear(buffer, 0, buffer.Length);
    if (viz != null) viz.Capture(buffer, channels);
  }
}
