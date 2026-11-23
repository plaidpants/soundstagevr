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
using System;

public class waveTranscribeRecorder : signalGenerator {
  public int duration = 300;
  public bool recording = false;
  public bool playing = false;

  public TextMesh saveText;

  int maxDuration = 300;

  float[] sampleBuffer;
  int curBufferIndex = 0;

  int virtualBufferLength = 0;

  public Transform scrubTransform;
  Vector2 scrubRange = new Vector2(.4f, -.4f);

  public signalGenerator incoming;
  recorderDeviceInterface _deviceInterface;

  // WAVE DRAW TOOLS
  ColumnDisplay display; // GPU-drawn waveform (Resources/Shaders/WaveColumns)
  public Renderer waverend;
  int wavewidth = 1024;
  int waveheight = 512;
  public int period = 512; 
  int curWaveW = 0;
  public Color32 waveBG = Color.black;
  public Color32 waveLine = Color.white;
  int columnMult = 1;
  double _sampleRateOverride;

  float[] lastRecSig, lastPlaySig, lastBackSig;

  public override void Awake() {
    base.Awake();
    _deviceInterface = GetComponent<recorderDeviceInterface>();
    sampleBuffer = new float[(int)(maxDuration * AudioSettings.outputSampleRate) * 2];
    virtualBufferLength = sampleBuffer.Length;
    _sampleRateOverride = AudioSettings.outputSampleRate;

    display = new ColumnDisplay(waverend, wavewidth, waveheight, waveLine, waveLine, waveBG, 0);

    lastRecSig = new float[] { 0, 0 };
    lastPlaySig = new float[] { 0, 0 };
    lastBackSig = new float[] { 0, 0 };
  }

  public void updateDuration(int newduration) {
    duration = newduration;
    virtualBufferLength = duration * AudioSettings.outputSampleRate * 2;
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
      for (int n = start - (start & 1); n < end; n += 2) {
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
    Vector3 pos = scrubTransform.localPosition;
    pos.x = Mathf.Lerp(scrubRange.x, scrubRange.y, 0);
    scrubTransform.localPosition = pos;
    columnMult = Mathf.CeilToInt((float)sampleBuffer.Length / (wavewidth - 1));
  }

  void scrubReset() {
    resetScrub = false;
    Vector3 pos = scrubTransform.localPosition;
    pos.x = scrubRange.x;
    scrubTransform.localPosition = pos;
  }

  bool resetScrub = false;
  float samplePos = 0;
  int lastUpload = -100;

  void Update() {
    if (resetScrub) scrubReset();
    if (!playing) return;

    if (DisplayUpdate.Due(waverend, ref lastUpload, DisplayUpdate.TransportInterval)) {
      if (display.dirty) display.Upload();
    }

    Vector3 pos = scrubTransform.localPosition;
    pos.x = Mathf.Lerp(scrubRange.x, scrubRange.y, samplePos);
    scrubTransform.localPosition = pos;
  }

  public void Flush() {
    display.Clear();
    recordedEnd = 0;

    sampleBuffer = new float[(int)(maxDuration * _sampleRateOverride) * 2];
    display.Upload();
  }

  // how far into the buffer has been recorded (audio thread); SAVE writes only that much
  // (2026-11-22: it wrote the whole length set on the slider - a 5 s take saved as 60 s)
  volatile int recordedEnd = 0;

  public void Save() {
    if (bufferToWav.instance.savingInProgress) return;
    // "rec_" (2026-11-22): tells the recorder's takes from the looper's in the tape library
    string audioFilename = masterControl.instance.SaveDir + System.IO.Path.DirectorySeparatorChar + "Samples" + System.IO.Path.DirectorySeparatorChar +
        "Recordings" + System.IO.Path.DirectorySeparatorChar +
      string.Format("rec_{0:MM-dd_hh-mm-ss-tt}.wav",
       DateTime.Now);

    int len = recordedEnd > 0 ? Mathf.Min(recordedEnd, virtualBufferLength) : virtualBufferLength;
    bufferToWav.instance.Save(audioFilename, sampleBuffer, 2, len, saveText, this);
  }

  // ---- multiplayer (Net/NetRecorders.cs, 2026-11-22) ----
  // Our SAVE finished: say where it went, and send the recording to the other players.
  // (A recorder fed by a microphone only hears it on the headset holding the mic, so
  // the others' copies recorded silence.)
  public override void updateTape(string s) {
    string label = System.IO.Path.GetFileNameWithoutExtension(s);
    ShowSaved("SAVED: " + label + "\n(tape library: RECORDINGS)");
    string file;
    if (!sampleManager.instance.sampleDictionary["Recordings"].TryGetValue(label, out file)) return;
    if (NetSync.Active) NetRecorders.Saved(this, label, SampleIndex.WithHash(file));
  }

  Coroutine _savedRoutine;
  void ShowSaved(string text) {
    if (saveText == null) return;
    if (_savedRoutine != null) StopCoroutine(_savedRoutine);
    _savedRoutine = StartCoroutine(SavedRoutine(text));
  }

  IEnumerator SavedRoutine(string text) {
    saveText.gameObject.SetActive(true);
    saveText.text = text;
    yield return new WaitForSeconds(4f);
    saveText.gameObject.SetActive(false);
    _savedRoutine = null;
  }

  // Another player saved this recorder's take: once their file is here (NetSamples), it
  // replaces what this copy recorded.
  string remoteHash = "", remoteFile = "", remoteLabel = "";
  public void ApplyRemoteSave(string label, string file) {
    StopWaitingRemote();
    remoteLabel = label;
    remoteFile = file;
    remoteHash = SampleIndex.HashOf(file);
    if (TakeRecording(file)) { remoteHash = ""; return; }
    if (remoteHash == "") return;
    ShowSaved("GETTING " + label + "...");
    NetSamples.Arrived += OnRemoteArrived;
    NetSamples.StateChanged += OnRemoteState;
    NetSamples.Need(remoteHash);
    OnRemoteState(remoteHash, NetSamples.StateOf(remoteHash));
  }

  void OnRemoteArrived(string hash) {
    if (hash != remoteHash) return;
    StopWaitingRemote();
    TakeRecording(remoteFile);
  }

  void OnRemoteState(string hash, NetSamples.State s) {
    if (hash != remoteHash) return;
    if (s == NetSamples.State.Missing || s == NetSamples.State.NoSpace || s == NetSamples.State.TooBig)
      ShowSaved(remoteLabel + ": " + (s == NetSamples.State.Missing ? "MISSING" : s == NetSamples.State.NoSpace ? "NO SPACE" : "TOO BIG"));
  }

  void StopWaitingRemote() {
    NetSamples.Arrived -= OnRemoteArrived;
    NetSamples.StateChanged -= OnRemoteState;
  }

  void OnDestroy() { StopWaitingRemote(); }

  bool TakeRecording(string file) {
    string path = sampleManager.instance.parseFilename(file);
    if (!System.IO.File.Exists(path)) return false;
    float[] rec;
    int ch, rate;
    try { rec = SampleCache.DecodeAll(path, out ch, out rate); }
    catch (Exception e) { Debug.LogWarning("[NetRecorders] couldn't read " + path + ": " + e.Message); return false; }
    if (rec == null || ch != 2) { Debug.LogWarning("[NetRecorders] not a stereo WAV: " + path); return false; }
    int outRate = AudioSettings.outputSampleRate;
    int theirs = rec.Length / 2;
    int ours = rate == outRate || rate <= 0 ? theirs : (int)((long)theirs * outRate / rate);
    ours = Mathf.Min(ours, sampleBuffer.Length / 2);
    playing = false; recording = false;
    if (ours == theirs) Array.Copy(rec, sampleBuffer, ours * 2);
    else {
      // another sample rate (the Quest runs at 48 kHz, the PC at 44.1): resample, linearly
      double step = (double)rate / outRate;
      for (int i = 0; i < ours; i++) {
        double pos = i * step;
        int a = Mathf.Min((int)pos, theirs - 1);
        int b = Mathf.Min(a + 1, theirs - 1);
        float t = (float)(pos - a);
        sampleBuffer[i * 2] = rec[a * 2] + (rec[b * 2] - rec[a * 2]) * t;
        sampleBuffer[i * 2 + 1] = rec[a * 2 + 1] + (rec[b * 2 + 1] - rec[a * 2 + 1]) * t;
      }
    }
    if (ours * 2 < sampleBuffer.Length) Array.Clear(sampleBuffer, ours * 2, sampleBuffer.Length - ours * 2);
    recordedEnd = ours * 2;
    curBufferIndex = 0;
    resetScrub = true;
    _deviceInterface.buttons[0].phantomHit(false);
    _deviceInterface.buttons[1].phantomHit(false);
    recalcTex();
    display.Upload();
    ShowSaved("FROM ANOTHER PLAYER: " + remoteLabel);
    Debug.Log("[NetRecorders] " + name + ": took another player's recording " + remoteLabel + " (" + ours + " frames" + (rate != outRate ? ", resampled from " + rate + " Hz" : "") + ")");
    return true;
  }


  public void Back() {
    curBufferIndex = 0;
    resetScrub = true;
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
  float[] _scratch_backBuffer; // reused audio-thread scratch (see AudioBufferPool)
  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (lastIncomingDspTime == dspTime) {

      for (int i = 0; i < buffer.Length; i++) {
        buffer[i] = oldBuffer[i];
      }
      return;
    }
    lastIncomingDspTime = dspTime;

    AudioBufferPool.GetCleared(ref oldBuffer, buffer.Length);
    float[] recBuffer = AudioBufferPool.GetCleared(ref _scratch_recBuffer, buffer.Length);
    float[] playBuffer = AudioBufferPool.GetCleared(ref _scratch_playBuffer, buffer.Length);
    float[] backBuffer = AudioBufferPool.GetCleared(ref _scratch_backBuffer, buffer.Length);
    if (_deviceInterface.recordTrigger.signal != null) _deviceInterface.recordTrigger.signal.Pull(recBuffer, dspTime, channels);
    if (_deviceInterface.playTrigger.signal != null) _deviceInterface.playTrigger.signal.Pull(playBuffer, dspTime, channels);
    if (_deviceInterface.backTrigger.signal != null) _deviceInterface.backTrigger.signal.Pull(backBuffer, dspTime, channels);

    if (incoming != null) incoming.Pull(buffer, dspTime, channels);
    for (int i = 0; i < buffer.Length; i += channels) {
      if (_deviceInterface.playTrigger.signal != null) {
        if (playBuffer[i] > lastPlaySig[1] && lastPlaySig[1] <= lastPlaySig[0]) {
          playing = !playing;
          _deviceInterface.buttons[1].phantomHit(playing);

          if (!playing && recording) {
            recording = false;
            _deviceInterface.buttons[0].phantomHit(recording);
          }

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
            _deviceInterface.buttons[1].phantomHit(playing);
          }
        }
        lastRecSig[0] = lastRecSig[1];
        lastRecSig[1] = recBuffer[i];
      }

      if (_deviceInterface.backTrigger.signal != null) {
        if (backBuffer[i] > lastBackSig[1] && lastBackSig[1] <= lastBackSig[0]) {
          curBufferIndex = 0;

        }

        lastBackSig[0] = lastBackSig[1];
        lastBackSig[1] = backBuffer[i];
      }

      if (playing) {
        oldBuffer[i] = buffer[i] += sampleBuffer[curBufferIndex];
        oldBuffer[i + 1] = buffer[i + 1] += sampleBuffer[curBufferIndex + 1];
      }
      if (recording) {
        sampleBuffer[curBufferIndex] = buffer[i];
        sampleBuffer[curBufferIndex + 1] = buffer[i + 1];
        if (curBufferIndex + 2 > recordedEnd) recordedEnd = curBufferIndex + 2;
      }
      if (playing) {
        curWaveW = curBufferIndex / columnMult;
        TrackPeak(curBufferIndex, columnMult, false);

        curBufferIndex = (curBufferIndex + 2);
        if (curBufferIndex >= virtualBufferLength) {
          curBufferIndex = 0; //NOT RIGHT
          Stop();
        }

      }
    }
    samplePos = (float)curBufferIndex / virtualBufferLength;
  }
}