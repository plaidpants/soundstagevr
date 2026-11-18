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
using System.IO;
using System.Runtime.InteropServices;

public class samplerLoad : MonoBehaviour {
  public GameObject tapePrefab;
  public GameObject loadingPrefab;
  public Transform deckOutline;
  public Transform deck;
  public clipPlayer[] players;
  public string CurTapeLabel;
  public string CurFile;

  public embeddedSpeaker miniSpeaker;

  Material deckMat;
  Color deckLight;

  public float[] clipSamples;

  GCHandle m_ClipHandle;

  void Awake() {
    if (players.Length == 0) players = GetComponents<clipPlayer>();
    deckMat = deckOutline.GetComponent<Renderer>().material;

    deckMat.SetFloat("_EmissionGain", .4f);
    deckLight = Color.white;
    deckMat.SetColor("_TintColor", Color.black);

    deckOutline.gameObject.SetActive(false);

    if (miniSpeaker != null) miniSpeaker.updateSecondary(false);
  }

  void Start() {
    deck.gameObject.layer = 14;
  }

  public bool hasTape() {
    return (currentTape != null);
  }

  tape currentTape;
  public void LoadTape(tape t) {
    if (currentTape != null && currentTape != t) {
      ForceEject();
    }
    currentTape = t;
    CurTapeLabel = t.label;
    CurFile = t.filename;
    if (miniSpeaker != null) miniSpeaker.updateSecondary(true);
    LoadClip(t.filename);
  }

  public void getTapeInfo(out string label, out string file) {
    if (currentTape != null) {
      label = CurTapeLabel;
      file = CurFile = SampleIndex.WithHash(CurFile); // saved / shared: with the sample's hash
    } else {
      label = file = "";
    }
  }

  public void ForceEject() {
    if (currentTape != null) {
      tape temp = currentTape;
      UnloadTape(currentTape);
      temp.Eject();
    }
  }

  public void UnloadTape(tape t) {

    if (currentTape == t) {
      StopWaiting(); // clears the tape's LOADING / MISSING line
      currentTape = null;
      if (_streamRoutine != null) {
        if (loaderObject != null) Destroy(loaderObject);
        StopCoroutine(_streamRoutine);
      }
      AbandonLoadingClip();

      if (miniSpeaker != null) miniSpeaker.updateSecondary(false);

      // unallocate memory
      if (m_ClipHandle.IsAllocated)
      {
      m_ClipHandle.Free();
      }
      for (int i = 0; i < players.Length; i++) players[i].UnloadClip();
    }
  }

  public void LoadClip(string path) {

    string fullpath = sampleManager.instance.parseFilename(path);
    string hash = SampleIndex.HashOf(path);

    if (!File.Exists(fullpath)) {
      // not on this machine: ask the other players for it (NetSamples), play it when it
      // arrives; the tape says what's happening meanwhile
      if (hash != "") WaitFor(hash);
      return;
    }
    // here, but with other content (a different file of that name): play it for now,
    // and fetch the scene's version if someone in the session has it
    if (hash != "" && NetSync.InSession && SampleIndex.Hash(fullpath) != hash && SampleCache.Find(hash) == null) WaitFor(hash);
    else StopWaiting();
    // a sample from another player: list it in the library's Sessions category
    if (hash != "" && fullpath.StartsWith(SampleCache.Dir, System.StringComparison.OrdinalIgnoreCase)) SessionSamples.Noticed(path);

    if (_streamRoutine != null) {
      if (loaderObject != null) Destroy(loaderObject);
      StopCoroutine(_streamRoutine);
    }
    AbandonLoadingClip();

    _streamRoutine = StartCoroutine(streamRoutine(fullpath));
  }

  GameObject loaderObject;
  Coroutine _streamRoutine;
  AudioClip loadingClip; // the clip being loaded (freed once its samples are copied: ClipRelease)

  // a load given up part-way: its clip is freed once the loader is done with it
  void AbandonLoadingClip() {
    if (loadingClip != null) ClipRelease.WhenDone(loadingClip);
    loadingClip = null;
  }

  IEnumerator streamRoutine(string fullpath) {
    AudioClip c = RuntimeAudioClipLoader.Manager.Load(fullpath, false, true, false);
    loadingClip = c;

    loaderObject = Instantiate(loadingPrefab, transform, false) as GameObject;
    loaderObject.transform.localPosition = new Vector3(-.05f, .013f, 0.061f);
    loaderObject.transform.localRotation = Quaternion.Euler(0, 180, 0);
    loaderObject.transform.localScale = Vector3.one * .1f;

    while (RuntimeAudioClipLoader.Manager.GetAudioClipLoadState(c) != AudioDataLoadState.Loaded) {
      yield return null;
    }
    if (loaderObject != null) Destroy(loaderObject);


    for (int i = 0; i < players.Length; i++) players[i].UnloadClip();

    while (c.loadState != AudioDataLoadState.Loaded) yield return null;

    clipSamples = new float[c.samples * c.channels];
    c.GetData(clipSamples, 0);
    loadingClip = null;
    ClipRelease.Now(c); // the samples are copied: the clip isn't needed any more

    //alocate the memory (the last clip's, if one is still pinned, is let go first)
    if (m_ClipHandle.IsAllocated) m_ClipHandle.Free();
    m_ClipHandle = GCHandle.Alloc(clipSamples, GCHandleType.Pinned);
    for (int i = 0; i < players.Length; i++) players[i].LoadSamples(clipSamples, m_ClipHandle, c.channels);
  }

  // ---- samples coming from other players ----

  string waitingHash = "";

  void WaitFor(string hash) {
    if (waitingHash != hash) {
      StopWaiting();
      waitingHash = hash;
      NetSamples.Arrived += OnSampleArrived;
      NetSamples.StateChanged += OnSampleState;
    }
    NetSamples.Need(hash);
    ShowState(NetSamples.StateOf(hash));
  }

  void StopWaiting() {
    if (waitingHash == "") return;
    waitingHash = "";
    NetSamples.Arrived -= OnSampleArrived;
    NetSamples.StateChanged -= OnSampleState;
    if (currentTape != null) currentTape.SetStatus("");
  }

  void OnSampleArrived(string hash) {
    if (hash != waitingHash) return;
    StopWaiting();
    if (currentTape != null) LoadClip(CurFile);
  }

  void OnSampleState(string hash, NetSamples.State s) {
    if (hash == waitingHash) ShowState(s);
  }

  void ShowState(NetSamples.State s) {
    if (currentTape == null) return;
    currentTape.SetStatus(s == NetSamples.State.Loading ? "LOADING" : s == NetSamples.State.Missing ? "MISSING" :
                          s == NetSamples.State.NoSpace ? "NO SPACE" : s == NetSamples.State.TooBig ? "TOO BIG" : "");
  }

  void OnDestroy() {
        StopWaiting();
        AbandonLoadingClip();
        if (m_ClipHandle.IsAllocated)
        {
            m_ClipHandle.Free();
        }
  }

  public string[] queuedSample = new string[] { "", "" };
  void OnEnable() {
    if (queuedSample[0] != "") {
      SetSample(queuedSample[0], queuedSample[1]);
      queuedSample[0] = "";
      queuedSample[1] = "";
    }

  }

  public void QueueSample(string s, string f) {
    if (gameObject.activeInHierarchy) SetSample(s, f);
    else {
      queuedSample[0] = s;
      queuedSample[1] = f;
    }
  }

  public void SetSample(string s, string f) {
    if (s == "") return;

    // a sample that isn't here still gets its tape when the scene knows its content
    // (hash): it's fetched from the other players, and the tape says so meanwhile
    if (!File.Exists(sampleManager.instance.parseFilename(f)) && SampleIndex.HashOf(f) == "") {
      Debug.LogWarning("[samplerLoad] no tape for " + s + ": " + f + " isn't here, and the scene doesn't say which sample it is (no hash), so it can't be fetched");
      return;
    }

    GameObject g = Instantiate(tapePrefab, Vector3.zero, Quaternion.identity) as GameObject;
    g.transform.localRotation = Quaternion.Euler(-90, 0, 0);
    tape t = g.GetComponent<tape>();
    t.Setup(s, f);
    t.ForceLoad(deck);
  }

  bool flashing = false;
  public void flashDecklight(bool on) {
    if (flashing == on) return;

    flashing = on;
    if (flashing) {
      if (_flashRoutine != null) StopCoroutine(_flashRoutine);
      _flashRoutine = StartCoroutine(flashRoutine());
    }
  }

  Coroutine _flashRoutine;

  IEnumerator flashRoutine() {
    deckOutline.gameObject.SetActive(true);
    while (flashing) {
      float cyc = Mathf.Repeat(masterControl.instance.curCycle * 4, 1);

      if (cyc < .1f) {
        deckMat.SetColor("_TintColor", Color.Lerp(Color.black, deckLight, cyc * 10));
        yield return null;
      } else if (cyc < .5f) {
        deckMat.SetColor("_TintColor", Color.Lerp(deckLight, Color.black, (cyc - .1f) / .4f));
        yield return null;
      } else if (cyc < .6f) {
        deckMat.SetColor("_TintColor", Color.Lerp(Color.black, deckLight, (cyc - .5f) * 10));
        yield return null;
      } else {
        deckMat.SetColor("_TintColor", Color.Lerp(deckLight, Color.black, (cyc - .6f) / .4f));
        yield return null;
      }
    }
    deckOutline.gameObject.SetActive(false);
  }

}
