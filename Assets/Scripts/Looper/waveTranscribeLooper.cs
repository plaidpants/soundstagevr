// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class waveTranscribeLooper : signalGenerator, INetBeatSync {
  public GameObject tapePrefab;
  public Transform tapeHolder;
  public tape curTape;
  public bool recording = false;
  public bool playing = false;

  public TextMesh saveText;


  float[] sampleBuffer;
  int curBufferIndex = 0;

  int virtualBufferLength = 0;

  public Transform scrubTransform;
  Vector2 scrubRange = new Vector2(.4f, -.4f);

  public signalGenerator incoming;
  looperDeviceInterface _deviceInterface;

  //WAVE DRAW TOOLS
  ColumnDisplay display; // GPU-drawn waveform (Resources/Shaders/WaveColumns)
  public Renderer waverend;
  int wavewidth = 1024;
  int waveheight = 512;
  public int period = 512; 
  int curWaveW = 0;
  public Color32 waveBG = Color.black;
  public Color32 waveBGRec = Color.black;
  public Color32 waveLine = Color.white;
  public Color32 waveLineRec = Color.white;
  int columnMult = 1;
  double _sampleRateOverride;

  float[] lastRecSig, lastPlaySig, lastBackSig;

  float biggestBeats = 1;
  double biggestPeriod = 0.0625;


  public override void Awake() {
    base.Awake();
    _deviceInterface = GetComponent<looperDeviceInterface>();
    // assuming stereo

    lastbeatperiod = _deviceInterface.period;
    virtualBufferLength = StereoLength(_deviceInterface.period * .25f * AudioSettings.outputSampleRate);
    sampleBuffer = new float[virtualBufferLength];

    _sampleRateOverride = AudioSettings.outputSampleRate;
    display = new ColumnDisplay(waverend, wavewidth, waveheight, waveLine, waveLineRec, waveBG, 0);
    display.EnablePolar(waverend); // ring mesh: exact angle/radius mapping

    lastRecSig = new float[] { 0, 0 };
    lastPlaySig = new float[] { 0, 0 };
  }

  double lastbeatperiod = 0;

  // Interleaved stereo length: must be even. An odd length let the read/write head
  // (steps of 2, touching index + 1) run one past the end of sampleBuffer, which threw
  // IndexOutOfRangeException on every audio callback from then on (stuck at the end).
  static int StereoLength(double samples) {
    return Mathf.RoundToInt((float)samples) & ~1;
  }

  public void updateDuration(float beats, double beatperiod) {
    virtualBufferLength = StereoLength(beats * beatperiod * .25f * AudioSettings.outputSampleRate);
    if (beatperiod > biggestPeriod || beats > biggestBeats) {
      if (biggestBeats < beats) biggestBeats = beats;
      if (biggestPeriod < beatperiod) biggestPeriod = beatperiod;
      Array.Resize<float>(ref sampleBuffer, StereoLength(biggestBeats * biggestPeriod * .25f * AudioSettings.outputSampleRate));
    }

    columnMult = Mathf.CeilToInt((float)virtualBufferLength / (wavewidth - 1));
    recalcTex();
    if (!playing) {
      display.Upload();
    }
  }

  // Each column shows the peak of all the samples it covers (2026-11). It used to show
  // one sample per column, so a steady tone aliased into a beating "envelope".
  void recalcTex() {
    for (int i = 0; i < wavewidth; i++) {
      int start = columnMult * i;
      if (start >= virtualBufferLength) break;
      int end = Mathf.Min(start + columnMult, virtualBufferLength, sampleBuffer.Length - 1);
      float peak = 0;
      // (2026-11-24) ~256 samples spread across each column, not all of them: a 32-beat
      // loop is 1.5-3 million samples, and this runs on every length change - and on every
      // tempo change while the tempo is being dragged, for every looper
      int stride = Mathf.Max(2, ((end - start) / 256) & ~1);
      for (int n = start - (start & 1); n < end; n += stride) {
        float a = Mathf.Abs(sampleBuffer[n]), b = Mathf.Abs(sampleBuffer[n + 1]);
        if (a > peak) peak = a;
        if (b > peak) peak = b;
      }
      display.SetColumn(i, ColumnHeight(peak));
    }
  }

  int ColumnHeight(float peak) {
    return Mathf.FloorToInt((waveheight - 1) * .5f * Mathf.Clamp01(peak));
  }

  // running peak of the column being played (audio thread)
  int peakColumn = -1;
  float peakValue = 0;
  bool peakAlt = false;

  void TrackPeak(int index, int mult, bool alt) {
    int col = index / mult;
    if (col != peakColumn) {
      if (peakColumn >= 0 && peakColumn < wavewidth) display.SetColumn(peakColumn, ColumnHeight(peakValue), peakAlt);
      peakColumn = col;
      peakValue = 0;
    }
    float a = Mathf.Abs(sampleBuffer[index]), b = Mathf.Abs(sampleBuffer[index + 1]);
    if (a > peakValue) peakValue = a;
    if (b > peakValue) peakValue = b;
    peakAlt = alt;
  }

  void Start() {
    Flush();
    display.Upload();
    scrubTransform.localRotation = Quaternion.identity;
    columnMult = Mathf.CeilToInt((float)virtualBufferLength / (wavewidth - 1));
  }

  void scrubReset() {
    resetScrub = false;
    scrubTransform.localRotation = Quaternion.identity;
  }

  bool resetScrub = false;
  float samplePos = 0;
  int lastUpload = -100;

  void Update() {
    if (resetScrub) scrubReset();

    scrubTransform.gameObject.SetActive(playing || _deviceInterface.recordCountdown || _deviceInterface.playCountdown);
    if (!playing) {
      return;
    }

    if (DisplayUpdate.Due(waverend, ref lastUpload, DisplayUpdate.TransportInterval)) {
      if (display.dirty) display.Upload();
    }

    if (curTape != null) {
      if (curTape.inDeck()) {
        createNewTape();
      }
    }

    scrubTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0, 360, samplePos));
  }

  public void Flush() {
    display.Clear();
    if (curTape != null) Destroy(curTape.gameObject);
    sampleBuffer = new float[StereoLength(biggestBeats * biggestPeriod * .25f * AudioSettings.outputSampleRate)];
    display.Upload();
  }


  public void Save() {
    if (bufferToWav.instance.savingInProgress) return;
    string audioFilename = masterControl.instance.SaveDir + System.IO.Path.DirectorySeparatorChar + "Samples" + System.IO.Path.DirectorySeparatorChar +
         "Recordings" + System.IO.Path.DirectorySeparatorChar +
        string.Format("{0:MM-dd_hh-mm-ss-tt}.wav",
        DateTime.Now);
    bufferToWav.instance.Save(audioFilename, sampleBuffer, 2, virtualBufferLength, saveText, this);
  }

  string curfilename = "";
  string curfile = ""; // the tape's sample string (from Recordings here, or another player's)

  // our own SAVE finished: a tape of it, and the recording goes to the other players
  public override void updateTape(string s) {
    s = System.IO.Path.GetFileNameWithoutExtension(s);
    ShowTape(s, sampleManager.instance.sampleDictionary["Recordings"][s]);
    if (NetSync.Active) NetLoopers.Saved(this, curfilename, SampleIndex.WithHash(curfile));
  }

  void ShowTape(string label, string file) {
    curfilename = label;
    curfile = file;
    if (curTape != null) Destroy(curTape.gameObject);
    curTape = (Instantiate(tapePrefab, tapeHolder, false) as GameObject).GetComponent<tape>();
    curTape.Setup(label, file);
  }

  // Another player saved this looper's loop: show their tape and, once their recording is
  // here (NetSamples), play it as this loop - so every headset has the same one.
  string remoteHash = "";
  public void ApplyRemoteSave(string label, string file) {
    ShowTape(label, file);
    StopWaitingRemote();
    remoteHash = SampleIndex.HashOf(file);
    if (TakeRecording(file)) { remoteHash = ""; return; }
    if (remoteHash == "") return;
    NetSamples.Arrived += OnRemoteArrived;
    NetSamples.StateChanged += OnRemoteState;
    NetSamples.Need(remoteHash);
    OnRemoteState(remoteHash, NetSamples.StateOf(remoteHash));
  }

  void OnRemoteArrived(string hash) {
    if (hash != remoteHash) return;
    StopWaitingRemote();
    TakeRecording(curfile);
    if (joinWaiting) { joinWaiting = false; recalcTex(); display.Upload(); } // (still playing in time from StartJoinPlay)
  }

  void OnRemoteState(string hash, NetSamples.State s) {
    if (hash != remoteHash || curTape == null) return;
    curTape.SetStatus(s == NetSamples.State.Loading ? "LOADING" : s == NetSamples.State.Missing ? "MISSING" :
                      s == NetSamples.State.NoSpace ? "NO SPACE" : s == NetSamples.State.TooBig ? "TOO BIG" : "");
  }

  void StopWaitingRemote() {
    NetSamples.Arrived -= OnRemoteArrived;
    NetSamples.StateChanged -= OnRemoteState;
    if (curTape != null) curTape.SetStatus("");
  }

  void OnDestroy() { StopWaitingRemote(); }

  // ---- kept across a multiplayer patch reload (2026-11-18) ----
  // The loop itself isn't part of the patch, so a reload (another player loading, moving
  // a turntable, ...) used to leave this headset with an empty looper and no tape, while
  // the player who caused it still had theirs. NetSync.ReplacePatch takes a snapshot of
  // each looper first and gives it to the new looper in the same place.
  public class Snapshot { public float[] buf; public int len, index; public bool playing, hasTape; public string label, file; public double dsp; }

  public Snapshot TakeSnapshot() {
    if (recording) return null;
    return new Snapshot {
      buf = (float[])sampleBuffer.Clone(), len = virtualBufferLength, index = curBufferIndex,
      playing = playing, hasTape = curTape != null, label = curfilename, file = curfile, dsp = AudioSettings.dspTime
    };
  }

  public void RestoreSnapshot(Snapshot s) {
    if (s == null) return;
    if (s.len != virtualBufferLength) { Debug.Log("[NetLoopers] " + name + ": loop length changed in the reload, not restored"); return; }
    int n = Mathf.Min(s.buf.Length, sampleBuffer.Length);
    Array.Copy(s.buf, sampleBuffer, n);
    recalcTex();
    display.Upload();
    if (s.hasTape && !string.IsNullOrEmpty(s.file)) ShowTape(s.label, s.file);
    if (s.playing && virtualBufferLength > 1) {
      // carry on where it would be now (the reload took a few frames)
      long adv = (long)((AudioSettings.dspTime - s.dsp) * AudioSettings.outputSampleRate) * 2;
      curBufferIndex = (int)((s.index + adv) % virtualBufferLength) & ~1;
      playing = true;
      _deviceInterface.buttons[1].phantomHit(true);
    }
    Debug.Log("[NetLoopers] " + name + ": kept its loop through the reload" + (s.playing ? " (still playing)" : ""));
  }

  // copies a saved recording into the loop (sample for sample; the loop length comes from
  // the looper's shared settings, so it matches the player who saved it)
  bool TakeRecording(string file) {
    string path = sampleManager.instance.parseFilename(file);
    if (!System.IO.File.Exists(path)) return false;
    float[] rec = SampleCache.ReadWav(path, out int ch);
    if (rec == null || ch != 2) { Debug.LogWarning("[NetLoopers] not a stereo WAV: " + path); return false; }
    float[] buf = sampleBuffer;
    int ours = Mathf.Min(virtualBufferLength, buf.Length) / 2, theirs = rec.Length / 2;
    if (theirs > 0 && ours > 0 && Mathf.Abs(theirs - ours) > ours / 100) {
      // the same loop at another sample rate (2026-11-18: the Quest runs at 48 kHz, the PC
      // at 44.1 kHz - copied sample for sample it played slower and lower): stretch it over
      // our loop length, linearly
      double step = (double)theirs / ours;
      for (int i = 0; i < ours; i++) {
        double pos = i * step;
        int a = (int)pos;
        int b = Mathf.Min(a + 1, theirs - 1);
        float t = (float)(pos - a);
        buf[i * 2] = rec[a * 2] + (rec[b * 2] - rec[a * 2]) * t;
        buf[i * 2 + 1] = rec[a * 2 + 1] + (rec[b * 2 + 1] - rec[a * 2 + 1]) * t;
      }
      if (ours * 2 < buf.Length) Array.Clear(buf, ours * 2, buf.Length - ours * 2);
    } else {
      int n = Mathf.Min(rec.Length, buf.Length);
      Array.Copy(rec, buf, n);
      if (n < buf.Length) Array.Clear(buf, n, buf.Length - n);
    }
    Debug.Log("[NetLoopers] " + name + ": took another player's saved loop (" + ours + " frames" +
              (theirs != ours ? ", resampled from " + theirs : "") + ")");
    resetScrub = true;
    return true;
  }

  void createNewTape() {
    Vector3 p;
    Quaternion q;
    curTape.getOrigTrans(out p, out q);
    curTape.masterObj = null;
    curTape = (Instantiate(tapePrefab, tapeHolder, false) as GameObject).GetComponent<tape>();

    curTape.transform.localPosition = p;
    curTape.Setup(curfilename, curfile);
  }

  public void Back() {
    curBufferIndex = 0;
    resetScrub = true;
  }

  public void Record() {
    playing = true;
    recording = true;
  }

  public void Stop() {
    playing = false;
    resetScrub = true;
    _deviceInterface.buttons[1].phantomHit(playing);

    if (!playing && recording) {
      recording = false;
      _deviceInterface.buttons[0].phantomHit(recording);
    }
  }

  double lastIncomingDspTime = -1;
  float[] oldBuffer;
  float[] _scratch_recBuffer; // reused audio-thread scratch (see AudioBufferPool)
  float[] _scratch_playBuffer; // reused audio-thread scratch (see AudioBufferPool)
  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    int tempColumnMult = columnMult;

    if (lastIncomingDspTime == dspTime) {
      for (int i = 0; i < buffer.Length; i++) {
        buffer[i] = oldBuffer[i];
      }
      return;
    }
    lastIncomingDspTime = dspTime;
    ApplyNetShift(); // the beat sync moved this loop into line with the host's

    AudioBufferPool.GetCleared(ref oldBuffer, buffer.Length);
    float[] recBuffer = AudioBufferPool.GetCleared(ref _scratch_recBuffer, buffer.Length);
    float[] playBuffer = AudioBufferPool.GetCleared(ref _scratch_playBuffer, buffer.Length);

    if (_deviceInterface.recordTrigger.signal != null) _deviceInterface.recordTrigger.signal.Pull(recBuffer, dspTime, channels);
    if (_deviceInterface.playTrigger.signal != null) _deviceInterface.playTrigger.signal.Pull(playBuffer, dspTime, channels);

    if (incoming != null) incoming.Pull(buffer, dspTime, channels);

    Color curWaveLine = waveLine;
    Color curWaveBG = waveBG;
    for (int i = 0; i < buffer.Length; i += channels) {
      if (_deviceInterface.playTrigger.signal != null) {
        if (playBuffer[i] > lastPlaySig[1] && lastPlaySig[1] <= lastPlaySig[0]) {
          playing = true;
          curBufferIndex = 0;
          _deviceInterface.buttons[1].phantomHit(playing);
        }

        lastPlaySig[0] = lastPlaySig[1];
        lastPlaySig[1] = playBuffer[i];
      }

      if (_deviceInterface.recordTrigger.signal != null) {
        if (recBuffer[i] > lastRecSig[1] && lastRecSig[1] <= lastRecSig[0]) {
          recording = !recording;
          _deviceInterface.buttons[0].phantomHit(recording);

          if (recording && !playing) {
            playing = true;
            curBufferIndex = 0;
            _deviceInterface.buttons[1].phantomHit(playing);
          }
        }

        lastRecSig[0] = lastRecSig[1];
        lastRecSig[1] = recBuffer[i];
      }

      if (curBufferIndex + 1 >= sampleBuffer.Length || curBufferIndex >= virtualBufferLength) curBufferIndex = 0; // length changed under us

      if (playing) {
        oldBuffer[i] = buffer[i] += sampleBuffer[curBufferIndex];
        oldBuffer[i + 1] = buffer[i + 1] += sampleBuffer[curBufferIndex + 1];
      }

      if (recording) {
        sampleBuffer[curBufferIndex] = buffer[i];
        sampleBuffer[curBufferIndex + 1] = buffer[i + 1];
        curWaveLine = waveLineRec;
        curWaveBG = waveBGRec;
      } else {
        curWaveLine = waveLine;
        curWaveBG = waveBG;
      }

      if (playing) {
        curWaveW = curBufferIndex / tempColumnMult;
        TrackPeak(curBufferIndex, tempColumnMult, recording);

        curBufferIndex = (curBufferIndex + 2);
        if (curBufferIndex >= virtualBufferLength) {
          if (recording) {
            recording = !recording;
            _deviceInterface.buttons[0].phantomHit(recording);
          }
          curBufferIndex = 0;

          if (_deviceInterface.playTrigger.signal != null) Stop();

          if (recordRequested) {
            recording = true;
            _deviceInterface.playClick = true;
            _deviceInterface.buttons[0].phantomHit(recording);
            recordRequested = false;
            _deviceInterface.recordCountdown = false;
          }
        }

        if (recordRequested) {
          _deviceInterface.recCountdownRemaining = Mathf.CeilToInt((virtualBufferLength - curBufferIndex) / (2 * (float)_sampleRateOverride));
          if (lastremaining != _deviceInterface.recCountdownRemaining) {
            _deviceInterface.playClick = true;
            lastremaining = _deviceInterface.recCountdownRemaining;
          }
        }
      }
    }

    samplePos = (float)curBufferIndex / virtualBufferLength;
  }

  // ---- multiplayer: late joiners and the beat sync (Net/NetLoopShare.cs, 2026-11-24) ----
  // (_sampleRateOverride: set in Awake - AudioSettings can't be asked from the audio thread,
  // and asking it there threw out of every audio callback once a shift was due)
  double SR { get { return _sampleRateOverride; } }
  double MeasureSamples { get { return masterControl.instance.measurePeriod * SR; } }   // frames per bar
  double LoopMeasures { get { double m = MeasureSamples; return m > 0 ? (virtualBufferLength / 2) / m : 0; } }

  public bool HasContent() {
    int n = Mathf.Min(virtualBufferLength, sampleBuffer.Length);
    for (int i = 0; i < n; i += 7) if (sampleBuffer[i] > 1e-4f || sampleBuffer[i] < -1e-4f) return true;
    return false;
  }

  public float[] ShareData(out int frames) {
    frames = HasContent() ? Mathf.Min(virtualBufferLength, sampleBuffer.Length) / 2 : 0;
    return sampleBuffer;
  }

  // INetBeatSync: a playing loop's position in bars, minus the bars the master clock has
  // played, stays the same while it plays - compared across headsets (NetClock)
  public bool NetSyncable { get { return playing && !recording && LoopMeasures > 0 && _deviceInterface.playTrigger.signal == null && !_deviceInterface.recordCountdown && !_deviceInterface.playCountdown; } }
  public int NetConfig { get { return Mathf.RoundToInt((float)(LoopMeasures * 64)); } }
  public double NetPeriod { get { return LoopMeasures; } }
  public double NetTolerance { get { return 1.0 / 64; } }
  public double NetAnchor() {
    double len = LoopMeasures;
    if (len <= 0) return 0;
    double a = ((curBufferIndex / 2) / MeasureSamples - NetBeat.Measures) % len;
    return a < 0 ? a + len : a;
  }
  volatile bool netShiftPending;
  long netShiftMove; // interleaved samples, worked out on the main thread
  public void NetShift(double d) { netShiftMove = (long)(d * MeasureSamples) * 2; netShiftPending = true; }

  void ApplyNetShift() { // audio thread: no Unity calls
    if (!netShiftPending) return;
    netShiftPending = false;
    if (virtualBufferLength <= 2) return;
    long move = netShiftMove;
    long idx = (curBufferIndex + move) % virtualBufferLength;
    if (idx < 0) idx += virtualBufferLength;
    curBufferIndex = (int)idx & ~1;
  }

  // the session's loop for a player who just joined
  bool joinPlay;
  double joinAnchor;
  public void ApplyJoinState(string file, bool play, double anchor) {
    joinPlay = play; joinAnchor = anchor;
    StopWaitingRemote();
    curfile = file;
    remoteHash = string.IsNullOrEmpty(file) ? "" : SampleIndex.HashOf(file);
    if (!string.IsNullOrEmpty(file) && TakeRecording(file)) { remoteHash = ""; recalcTex(); display.Upload(); StartJoinPlay(); return; }
    if (remoteHash == "") { StartJoinPlay(); return; }
    joinWaiting = true;
    NetSamples.Arrived += OnRemoteArrived;
    NetSamples.Need(remoteHash);
    StartJoinPlay(); // plays (silently) in time until the audio is here
  }
  bool joinWaiting;

  void StartJoinPlay() {
    if (!joinPlay || virtualBufferLength <= 2) return;
    double len = LoopMeasures;
    double pos = len > 0 ? (joinAnchor + NetBeat.Measures) % len : 0;
    if (pos < 0) pos += len;
    curBufferIndex = ((int)(pos * MeasureSamples) * 2 % virtualBufferLength) & ~1;
    playing = true;
    _deviceInterface.buttons[1].phantomHit(true);
  }

  bool recordRequested = false;
  public void requestRecord(bool on) {
    recordRequested = on;
    lastremaining = Mathf.CeilToInt((virtualBufferLength - curBufferIndex) / (2 * (float)_sampleRateOverride));
  }

  int lastremaining = 0;
}