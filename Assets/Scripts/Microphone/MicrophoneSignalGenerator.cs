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
using System.Runtime.InteropServices;

public class MicrophoneSignalGenerator : signalGenerator {

  [DllImport("SoundStageNative")]
  public static extern void SetArrayToSingleValue(float[] a, int length, float val);
  [DllImport("SoundStageNative")]
  public static extern void CopyArray(float[] a, float[] b, int length);
  [DllImport("SoundStageNative")]
  public static extern void MicFunction(float[] a, float[] b, int length, float val);

  // Which headset's microphone feeds this device (2026-11-22). In multiplayer every
  // headset has its own copy of each Microphone device, and each copy used to open that
  // headset's microphone - every player's mic was live, all playing into the patch (and,
  // in one room, into each other's speakers: feedback). Now, in a session, a copy listens
  // only while a player on this headset is holding the mic; everyone else's copy is
  // silent. Alone, it's live as before. Several mics can be in the scene: they share the
  // headset's one microphone (MicCapture).
  AudioSource source;
  float[] sharedBuffer;
  volatile bool activated = false; // mic audio is flowing into this device
  bool capturing;                  // holding a MicCapture reference
  microphoneGrip grip;

  public Dictionary<float, float[]> freqBuffers = new Dictionary<float, float[]>();

  public float amp = 1;
  public bool active = true;

  public override void Awake() {
    base.Awake();
    sharedBuffer = new float[MAX_BUFFER_LENGTH];
  }

  void Start() {
    source = GetComponent<AudioSource>();
    grip = GetComponentInChildren<microphoneGrip>(true);
  }

  bool WantLive() {
    if (!NetSync.Active) return true;
    return grip != null && grip.curState == manipObject.manipState.grabbed;
  }

  void Update() {
    if (source == null) return;
    bool want = WantLive();
    if (want && !capturing) StartCapture();
    else if (!want && capturing) StopCapture();
  }

  Coroutine _MicActivateRoutine;
  void StartCapture() {
    AudioClip clip = MicCapture.Acquire();
    if (clip == null) return; // no microphone (or no permission): stay silent, try again later
    capturing = true;
    if (_MicActivateRoutine != null) StopCoroutine(_MicActivateRoutine);
    _MicActivateRoutine = StartCoroutine(MicActivateRoutine(clip));
  }

  void StopCapture() {
    if (_MicActivateRoutine != null) { StopCoroutine(_MicActivateRoutine); _MicActivateRoutine = null; }
    activated = false;
    if (source != null) { source.Stop(); source.clip = null; }
    if (capturing) { capturing = false; MicCapture.Release(); }
  }

  IEnumerator MicActivateRoutine(AudioClip clip) {
    source.Stop();
    source.clip = clip;
    source.loop = true;
    // wait (at most 2 s) for the first audio from the microphone - this used to wait
    // without end, which froze the app when no audio came (no microphone permission)
    float waited = 0;
    while (MicCapture.Position() <= 0 && waited < 2f) { waited += Time.unscaledDeltaTime; yield return null; }
    if (MicCapture.Position() <= 0) { Debug.LogWarning("[Microphone] no audio from the microphone"); yield break; }
    source.Play();
    _MicActivateRoutine = null;
  }

  void OnDisable() { StopCapture(); }

  private void OnAudioFilterRead(float[] buffer, int channels) {
    if (!capturing) return;
    activated = true;
    if (sharedBuffer.Length != buffer.Length)
      System.Array.Resize(ref sharedBuffer, buffer.Length);

    CopyArray(buffer, sharedBuffer, buffer.Length);
    SetArrayToSingleValue(buffer, buffer.Length, 0.0f);
  }

  public override void processBuffer(float[] buffer, double dspTime, int channels) {
    if (!active || !activated || !capturing) {
      return;
    }

    MicFunction(buffer, sharedBuffer, buffer.Length, amp);
  }
}

// The headset's microphone, shared by every Microphone device that is listening: opened
// when the first one starts, closed when the last one stops.
public static class MicCapture {
  static AudioClip clip;
  static string device;
  static int users;

  public static AudioClip Acquire() {
    if (users == 0 || clip == null) {
      if (Microphone.devices.Length == 0) return null;
      device = Microphone.devices[0];
      clip = Microphone.Start(device, true, 1, 44100);
      if (clip == null) return null;
      Debug.Log("[Microphone] " + device + " on");
      users = 0;
    }
    users++;
    return clip;
  }

  public static void Release() {
    if (users <= 0) return;
    if (--users > 0) return;
    if (device != null) Microphone.End(device);
    Debug.Log("[Microphone] " + device + " off");
    clip = null;
  }

  public static int Position() { return device != null ? Microphone.GetPosition(device) : 0; }
}
