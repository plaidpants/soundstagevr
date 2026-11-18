// Things that play along with the master clock and keep their own position, so two
// headsets can end up apart even with the clocks in line: step sequencers (they count
// steps from when PLAY was pressed or the patch loaded), timelines / piano rolls (their
// playhead starts on the next eighth after PLAY). (2026-11)
//
// While playing, "position minus master-clock time played" stays the same, so it can be
// compared across headsets: the host sends it with the clock (NetClock) and a joined
// headset that's off moves its position into line.

public interface INetBeatSync {
  bool NetSyncable { get; }      // playing, following the master clock
  int NetConfig { get; }         // length / speed: only compared when the same here
  double NetAnchor();            // position minus clock time played, wrapped to NetPeriod
  double NetPeriod { get; }      // what the anchor wraps at
  double NetTolerance { get; }   // closer than this counts as in line
  void NetShift(double d);       // move the position by d (anchor units)
}

public static class NetBeat {
  // master-clock measures played (whole + this one so far)
  public static double Measures {
    get { masterControl mc = masterControl.instance; return mc.measureCount + mc.MeasurePhaseSeconds / mc.measurePeriod; }
  }

  // shared step-sequencer maths: step minus ticks played, mod length
  public static double StepAnchor(int step, beatTracker bt, int len) {
    long tick = masterControl.instance.measureCount * bt.TicksPerMeasure + bt.CurBeat;
    return StepAnchor(step, tick, len);
  }

  // the same with a tick count of its own (a sequencer started from its control input: PulseClock)
  public static double StepAnchor(int step, long tick, int len) {
    long a = (step - tick) % len;
    return a < 0 ? a + len : a;
  }
}
