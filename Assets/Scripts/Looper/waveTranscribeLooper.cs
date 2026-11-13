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

public class waveTranscribeLooper : signalGenerator {
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
  public override void updateTape(string s) {
    s = System.IO.Path.GetFileNameWithoutExtension(s);
    curfilename = s;
    if (curTape != null) Destroy(curTape.gameObject);
    curTape = (Instantiate(tapePrefab, tapeHolder, false) as GameObject).GetComponent<tape>();
    curTape.Setup(s, sampleManager.instance.sampleDictionary["Recordings"][s]);
  }

  void createNewTape() {
    Vector3 p;
    Quaternion q;
    curTape.getOrigTrans(out p, out q);
    curTape.masterObj = null;
    curTape = (Instantiate(tapePrefab, tapeHolder, false) as GameObject).GetComponent<tape>();

    curTape.transform.localPosition = p;
    curTape.Setup(curfilename, sampleManager.instance.sampleDictionary["Recordings"][curfilename]);
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

    AudioBufferPool.GetCleared(ref oldBuffer, buffer.Length);
    float[] recBuffer = AudioBufferPool.GetCleared(ref _scratch_recBuffer, buffer.Length);
    float[] playBuffer = AudioBufferPool.GetCleared(ref _scratch_playBuffer, buffer.Length);

    if (_deviceInterface.recordTrigger.signal != null) _deviceInterface.recordTrigger.signal.processBuffer(recBuffer, dspTime, channels);
    if (_deviceInterface.playTrigger.signal != null) _deviceInterface.playTrigger.signal.processBuffer(playBuffer, dspTime, channels);

    if (incoming != null) incoming.processBuffer(buffer, dspTime, channels);

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

  bool recordRequested = false;
  public void requestRecord(bool on) {
    recordRequested = on;
    lastremaining = Mathf.CeilToInt((virtualBufferLength - curBufferIndex) / (2 * (float)_sampleRateOverride));
  }

  int lastremaining = 0;
}