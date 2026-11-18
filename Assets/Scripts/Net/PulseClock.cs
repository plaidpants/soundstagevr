// Sequencers started from their control input (a button through a splitter, 2026-11-18).
//
// Such a sequencer used to keep a private timer from the moment its own pulse arrived:
// every headset got the pulse at a different time (network delay), a whole-patch reload
// restarted it, and nothing could line the copies up again. Now its private measure is
// the master clock (already kept in step on every headset) minus an OFFSET: the master
// clock's position (in measures) when the pulse came. The offset is saved with the
// device, so a reload or a joining headset rebuilds the sequencer exactly where it was;
// the host shares its offset in the clock message and the others take it (their own
// pulse arrived a little later), start playing if the host's is playing, and line the
// step up like a sequencer on the master clock (NetClock.ApplySequencers).
//
// When the master clock isn't running, the private measure just runs on its own, as
// before.

using UnityEngine;

public interface INetPulseSync {
  bool NetPulseDriven { get; }       // started from the control input (not the master clock)
  bool NetPlaying { get; }
  void NetStartPlaying();            // the host's copy plays: play here too
  double NetPulseOffset { get; set; }
}

public class PulseClock {
  double offset;                     // master measures at the last pulse
  volatile bool offsetPending;
  double pendingOffset;
  double frac;                       // where the private measure is (0-1)
  double bufferStartAbs;

  public double Offset {
    get { return offsetPending ? pendingOffset : offset; }
    set { pendingOffset = value; offsetPending = true; } // taken by the audio thread at the next buffer
  }

  public static double MasterAbs() {
    masterControl mc = masterControl.instance;
    return mc.measureCount + mc.MeasurePhaseSeconds / mc.measurePeriod;
  }

  static bool MasterRunning { get { return masterControl.instance != null && masterControl.instance.BeatRunning; } }

  public float Frac { get { return (float)frac; } }

  // audio thread, at the start of each buffer. True when the position jumped (the host's
  // offset was taken): the beat tracker should be put on the new position without
  // playing the ticks in between (beatTracker.Resync).
  public bool BeginBuffer() {
    bool jumped = false;
    if (offsetPending) { jumped = offset != pendingOffset; offset = pendingOffset; offsetPending = false; }
    if (!MasterRunning) return false;
    bufferStartAbs = MasterAbs();
    double p = bufferStartAbs - offset;
    frac = p - System.Math.Floor(p);
    return jumped;
  }

  // audio thread: the control input pulsed in this buffer (the private measure restarts)
  public void Pulse() {
    if (MasterRunning) offset = bufferStartAbs;
    offsetPending = false;
    frac = 0;
  }

  // audio thread: one sample on; the private measure's position for the beat tracker
  public float Advance(double sampleDuration) {
    frac += sampleDuration / masterControl.instance.measurePeriod;
    if (frac >= 1) frac -= 1;
    return (float)frac;
  }

  // the private clock's tick count (for NetBeat anchors): whole private measures since
  // the master clock started, plus the beat tracker's tick in this one
  public long Tick(beatTracker bt) {
    long measures = (long)System.Math.Floor(MasterAbs() - Offset);
    return measures * bt.TicksPerMeasure + bt.CurBeat;
  }
}
