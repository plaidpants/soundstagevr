// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// The Scope's signal (2026-11-22): passes its input through unchanged (THRU) and shows
// it on the display. With nothing on THRU its little built-in speaker pulls the signal
// instead, at zero volume (scopeDeviceInterface), so the display runs either way.

using UnityEngine;

public class scopeSignalGenerator : signalGenerator {
  public signalGenerator incoming, trigger; // trigger: the TRIG jack (2026-11-27)
  public scopeViz viz;
  float[] _scratch_trig; // reused audio-thread scratch (see AudioBufferPool)

  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    signalGenerator inc = incoming, trig = trigger; // (read once: a cable pulled on the main thread can clear them)
    if (inc != null) inc.Pull(buffer, dspTime, channels);
    else System.Array.Clear(buffer, 0, buffer.Length);
    float[] t = null;
    if (trig != null) { t = AudioBufferPool.GetCleared(ref _scratch_trig, buffer.Length); trig.Pull(t, dspTime, channels); }
    if (viz != null) viz.Capture(buffer, channels, t);
  }
}
