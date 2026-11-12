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

  void recalcTex() {

    for (int i = 0; i < wavewidth; i++) {
      if (columnMult * i < virtualBufferLength) {
        int curH = Mathf.FloorToInt((waveheight - 1) * .5f * Mathf.Clamp01(Mathf.Abs(sampleBuffer[columnMult * i])));

        display.SetColumn(i, curH);
      }
    }
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

    sampleBuffer = new float[(int)(maxDuration * _sampleRateOverride) * 2];
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
    if (_deviceInterface.recordTrigger.signal != null) _deviceInterface.recordTrigger.signal.processBuffer(recBuffer, dspTime, channels);
    if (_deviceInterface.playTrigger.signal != null) _deviceInterface.playTrigger.signal.processBuffer(playBuffer, dspTime, channels);
    if (_deviceInterface.backTrigger.signal != null) _deviceInterface.backTrigger.signal.processBuffer(backBuffer, dspTime, channels);

    if (incoming != null) incoming.processBuffer(buffer, dspTime, channels);
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
      }
      if (playing) {
        if (curBufferIndex % columnMult == 0) {
          curWaveW = curBufferIndex / columnMult;

          if (curWaveW < wavewidth) {
            int curH = Mathf.FloorToInt((waveheight - 1) * .5f * Mathf.Clamp01(Mathf.Abs(sampleBuffer[curBufferIndex])));

            display.SetColumn(curWaveW, curH, false);
          }

        }

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