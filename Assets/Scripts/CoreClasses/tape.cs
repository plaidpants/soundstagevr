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
using System.Collections.Generic;
using System.Collections;
using System.IO;

public class tape : manipObject {
  public Material mat;
  public Transform masterObj;
  public GameObject loadingPrefab;

  Transform deck;
  Transform tapeTrans;
  public TextMesh labelMesh;
  public string label;
  public string filename;
  public bool fullpath = false;
  Vector3 origPos = Vector3.zero;
  Quaternion origRot = Quaternion.identity;
  Vector3 origScale = Vector3.one;
  Vector3 tapeTransOrigScale = new Vector3(2.15f, 2.15f, 2.15f);

  public bool previewWhenGrabbed = false;

  GameObject highlight;
  Renderer tapeRend, highlightRend;
  Material tapeBase, highlightBase;
  const int PaletteSize = 12;
  static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");

  void SetHighlight(float gain) {
    highlightRend.sharedMaterial = MaterialCache.Glow(highlightBase, Color.HSVToRGB(tapeHue, .9f, .3f), gain);
  }
  float highlightGain = .3f;
  Color tapeColor;
  float tapeHue;

  public override void Awake() {
    base.Awake();
    gameObject.layer = 13;
    origScale = transform.localScale;
    tapeTrans = transform.GetChild(0);
    tapeTransOrigScale = tapeTrans.localScale;
    // Shared materials (MaterialCache, 2026-11): tape colours come from a 12-hue
    // palette instead of a random hue, so tapes of the same colour share a material.
    tapeRend = tapeTrans.GetComponent<Renderer>();
    tapeBase = tapeRend.sharedMaterial;
    labelMesh = GetComponentInChildren<TextMesh>();
    Renderer labelRend = labelMesh.GetComponent<Renderer>();
    labelRend.sharedMaterial = MaterialCache.Colored(labelRend.sharedMaterial, Color.black);
    tapeHue = Random.Range(0, PaletteSize) / (float)PaletteSize;
    tapeColor = Color.HSVToRGB(tapeHue, .5f, 1);
    SetColor(tapeColor);
    createHandleFeedback();
    createFarLOD();
  }

  // Far LOD (2026-11): a 12-triangle box the size of the tape (Resources/Models/tape_lod1) when it is
  // small on screen; the full 600-triangle tape up close. A 20-tape group was ~12k triangles.
  public static float lodNearScreenHeight = 0.023f; // full tape above this fraction of screen height (~0.5 m further out than 0.04)
  public static float lodCullScreenHeight = 0.002f;
  static Mesh lodMesh;
  static bool lodMeshLoaded;
  Renderer lodRend;

  void createFarLOD() {
    if (!lodMeshLoaded) {
      lodMeshLoaded = true;
      lodMesh = Resources.Load<Mesh>("Models/tape_lod1");
      if (lodMesh == null) Debug.LogWarning("[tape] Resources/Models/tape_lod1 missing");
    }
    if (lodMesh == null || tapeRend == null) return;

    GameObject far = new GameObject("tapeLOD1");
    far.layer = tapeTrans.gameObject.layer;
    far.transform.SetParent(tapeTrans.parent, false); // sibling, added last (GetChild(0) stays the tape)
    far.transform.localPosition = tapeTrans.localPosition;
    far.transform.localRotation = tapeTrans.localRotation;
    far.transform.localScale = tapeTrans.localScale;
    far.AddComponent<MeshFilter>().sharedMesh = lodMesh;
    lodRend = far.AddComponent<MeshRenderer>();
    lodRend.sharedMaterial = tapeRend.sharedMaterial;
    lodRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

    LODGroup group = gameObject.AddComponent<LODGroup>();
    group.SetLODs(new LOD[] {
      new LOD(lodNearScreenHeight, new Renderer[] { tapeRend }),
      new LOD(lodCullScreenHeight, new Renderer[] { lodRend }),
    });
    group.RecalculateBounds();
  }

  public void setOrigTrans(Vector3 pos, Quaternion rot) {
    origPos = pos;
    origRot = rot;
  }

  public void getOrigTrans(out Vector3 pos, out Quaternion rot) {
    pos = origPos;
    rot = origRot;
  }

  public void Setup(string s, string f) {
    if (masterObj == null) masterObj = transform.parent;
    label = s;
    // the file string carries the sample's content hash (SampleIndex), so the scene knows
    // the sound itself, not just its name on this machine. Only an already known hash here:
    // a Tapes shelf makes dozens of tapes at once (an unknown one is worked out in the
    // background; saving / sharing the tape adds it then).
    filename = f = SampleIndex.WithHash(f, false);
    // the colour comes from the sample's path (2026-11), so a tape keeps its colour when
    // it's loaded again from a patch, and is the same colour on every headset (the path
    // travels with the hash)
    if (!string.IsNullOrEmpty(f)) {
      string key = SampleIndex.PathOf(f);
      uint h = 2166136261;
      foreach (char ch in key) h = (h ^ ch) * 16777619;
      setHue((h % PaletteSize) / (float)PaletteSize);
    }
    labelMesh.text = s;
    origPos = transform.localPosition;
    origRot = transform.localRotation;
    origScale = transform.localScale;
  }

  // A line under the label while the sample isn't playable here yet (NetSamples):
  // LOADING / MISSING / NO SPACE, "" to clear.
  public void SetStatus(string status) {
    if (labelMesh == null) return;
    labelMesh.text = string.IsNullOrEmpty(status) ? label : label + "\n" + status;
  }

  public bool inDeck() {
    return (deck != null);
  }

  public Color setHue(float h) {
    tapeHue = h;
    tapeColor = Color.HSVToRGB(tapeHue, .5f, 1);
    SetColor(tapeColor);
    SetHighlight(highlightGain);
    return tapeColor;
  }

  void createHandleFeedback() {
    highlight = new GameObject("highlight");

    MeshFilter m = highlight.AddComponent<MeshFilter>();

    m.sharedMesh = transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh; // was .mesh: a mesh copy per tape
    highlightRend = highlight.AddComponent<MeshRenderer>();
    highlightBase = Resources.Load("Materials/Highlight") as Material;

    highlight.transform.SetParent(transform, false);
    highlight.transform.localScale = Vector3.one * 2.3f;
    SetHighlight(.3f);

    highlight.SetActive(false);
  }

  void SetColor(Color c) {
    mat = MaterialCache.Get(MaterialCache.Colored(tapeBase, c), SpecColorId, c);
    tapeRend.sharedMaterial = mat;
    if (lodRend != null) lodRend.sharedMaterial = mat;
  }

  manipulator tempM;

  // (2026-11-25) Which deck a held tape goes into. The tape touches the slot above or
  // below as well as the one it's over (slots are 4 cm tall, 5 cm apart), and it used to
  // take whichever slot it touched last - and lose it again on leaving a neighbour, so
  // the tape showed in the wrong deck or none. Now every slot it touches is kept, and
  // it goes into the one nearest the tape (a slight lead is needed to switch, so it
  // doesn't flicker between two).
  readonly Dictionary<Transform, float> touching = new Dictionary<Transform, float>();
  const float touchHold = .1f;    // a slot not reported touching for this long is let go
  const float switchLead = .006f; // m: how much nearer another slot must be

  void OnCollisionEnter(Collision coll) {
    if (curState != manipState.grabbed) return;
    if (coll.transform.name != "tapeInsert") return;
    touching[coll.transform] = Time.time;
    PickDeck();
  }

  void OnCollisionStay(Collision coll) {
    if (curState != manipState.grabbed) return;
    if (coll.transform.name != "tapeInsert") return;
    touching[coll.transform] = Time.time;
  }

  void OnCollisionExit(Collision coll) {
    if (curState != manipState.grabbed) return;
    touching.Remove(coll.transform);
    PickDeck();
  }

  void Update() {
    if (curState == manipState.grabbed && (touching.Count > 0 || deck != null)) PickDeck();
  }

  static readonly List<Transform> stale = new List<Transform>();
  void PickDeck() {
    if (tapeTrans == null) { // its body went with a deck that was removed (a patch reload) while held
      touching.Clear();
      deck = null;
      Destroy(gameObject);
      return;
    }
    stale.Clear();
    foreach (KeyValuePair<Transform, float> kv in touching)
      if (kv.Key == null || !kv.Key.gameObject.activeInHierarchy || Time.time - kv.Value > touchHold) stale.Add(kv.Key);
    foreach (Transform t in stale) touching.Remove(t);

    Transform best = null;
    float bestD = float.MaxValue;
    foreach (Transform t in touching.Keys) {
      float d = Vector3.Distance(transform.position, t.position);
      if (d < bestD) { bestD = d; best = t; }
    }
    if (best != null && deck != null && best != deck && touching.ContainsKey(deck) &&
        Vector3.Distance(transform.position, deck.position) < bestD + switchLead) best = deck;
    if (best == deck) return;

    if (deck != null) { // out of the slot it was in
      samplerLoad sl = deck.parent.GetComponent<samplerLoad>();
      // (2026-11-25) only a deck this tape was really loaded in is emptied on the other
      // headsets: a tape just passing over a deck that holds another tape used to eject
      // that tape there
      bool wasLoaded = sl != null && sl.Holds(this);
      if (sl != null) sl.UnloadTape(this);
      if (wasLoaded) NetTapes.Ejected(sl); // multiplayer
      if (best == null && manipulatorObjScript != null) manipulatorObjScript.hapticPulse(500);
      deck = null;
      tapeTrans.parent = transform;
      tapeTrans.localPosition = Vector3.zero;
      tapeTrans.localRotation = Quaternion.identity;
      tapeTrans.localScale = tapeTransOrigScale;
    }
    if (best != null) {
      if (manipulatorObjScript != null) {
        manipulatorObjScript.hapticPulse(1000);
        tempM = manipulatorObjScript;
      }
      deck = best;
      tapeTrans.parent = deck;
      tapeTrans.localPosition = Vector3.zero;
      tapeTrans.localRotation = Quaternion.Euler(-90, 0, 0);
      tapeTrans.localScale = tapeTransOrigScale;
    }
  }

  Coroutine insertCoroutine;
  IEnumerator insertRoutine() {
    float timer = 0;
    Vector3 dest = new Vector3(0, 0, -.0225f);
    while (timer < 1) {
      if (tempM != null && timer < 0.5f) tempM.hapticPulse(500);
      timer = Mathf.Clamp01(timer + Time.deltaTime * 2);
      transform.localPosition = Vector3.Lerp(Vector3.zero, dest, timer);
      yield return null;
    }
  }

  void flashEmptySamplers(bool on) {
    if (!on) {
      tape[] tempTapes = FindObjectsOfType<tape>();
      for (int i = 0; i < tempTapes.Length; i++) {
        if (tempTapes[i] != this && tempTapes[i].curState == manipState.grabbed) return;
      }
    }

    samplerLoad[] samps = FindObjectsOfType<samplerLoad>();
    for (int i = 0; i < samps.Length; i++) {
      samps[i].flashDecklight(on);
    }
  }

  Coroutine returnRoutineID;
  IEnumerator returnRoutine() {
    Vector3 curPos = transform.localPosition;
    Quaternion curRot = transform.localRotation;

    float t = 0;
    float modT = 0;
    while (t < 1) {
      t = Mathf.Clamp01(t + Time.deltaTime * 2);
      modT = Mathf.Sin(t * Mathf.PI * 0.5f);
      transform.localPosition = Vector3.Lerp(curPos, origPos, modT);
      transform.localRotation = Quaternion.Lerp(curRot, origRot, modT);
      yield return null;
    }
  }

  public void Eject() {
    deck = null;
    tapeTrans.parent = transform;
    tapeTrans.localPosition = Vector3.zero;
    tapeTrans.localRotation = Quaternion.identity;
    tapeTrans.localScale = tapeTransOrigScale;
    if (masterObj) {
      transform.parent = masterObj;
      transform.localScale = origScale;
      if (gameObject.activeInHierarchy) {
        if (returnRoutineID != null) StopCoroutine(returnRoutineID);
        returnRoutineID = StartCoroutine(returnRoutine());
      } else {
        transform.localPosition = origPos;
        transform.localRotation = origRot;
      }
    } else Destroy(gameObject);
  }

  void OnEnable() {
    if (deck == null) {
      transform.localPosition = origPos;
      transform.localRotation = origRot;
    }
  }

  public void Release() {
    if (deck == null) {
      if (masterObj == null) {
        Destroy(gameObject);
        return;
      }

      transform.parent = masterObj;
      transform.localScale = origScale;
      tapeTrans.parent = transform;
      tapeTrans.localPosition = Vector3.zero;
      tapeTrans.localRotation = Quaternion.identity;
      tapeTrans.localScale = tapeTransOrigScale;

      if (gameObject.activeInHierarchy) {

        if (returnRoutineID != null) StopCoroutine(returnRoutineID);
        returnRoutineID = StartCoroutine(returnRoutine());
      } else {
        transform.localPosition = origPos;
        transform.localRotation = origRot;
      }
    } else {
      transform.parent = deck;
      transform.localPosition = Vector3.zero;
      transform.localRotation = Quaternion.Euler(-90, 0, 0);
      transform.localScale = origScale;

      tapeTrans.parent = transform;
      tapeTrans.localPosition = Vector3.zero;
      tapeTrans.localRotation = Quaternion.identity;
      tapeTrans.localScale = tapeTransOrigScale;
      deck.parent.GetComponent<samplerLoad>().LoadTape(this);
      NetTapes.Loaded(deck.parent.GetComponent<samplerLoad>(), this); // multiplayer
      if (insertCoroutine != null) StopCoroutine(insertCoroutine);
      insertCoroutine = StartCoroutine(insertRoutine());
    }
  }

  public void ForceLoad(Transform targetDeck) {
    deck = targetDeck;
    transform.parent = deck;
    transform.localPosition = new Vector3(0, 0, -.0225f);
    transform.localRotation = Quaternion.Euler(-90, 0, 0);
    transform.localScale = origScale;

    tapeTrans.parent = transform;
    tapeTrans.localPosition = Vector3.zero;
    tapeTrans.localRotation = Quaternion.identity;
    tapeTrans.localScale = tapeTransOrigScale;
    deck.parent.GetComponent<samplerLoad>().LoadTape(this);
  }

  // a preview: decoded off the main thread (SampleCache.DecodeAll, 2026-11-21), played as
  // a clip that is freed once it has played (ClipRelease)
  IEnumerator streamRoutine(string f) {
    loaderObject = Instantiate(loadingPrefab, transform, false) as GameObject;
    loaderObject.transform.localPosition = new Vector3(-.03f, -.037f, .01f);
    loaderObject.transform.localRotation = Quaternion.Euler(-90, 180, 0);
    loaderObject.transform.localScale = Vector3.one * .1f;

    int ch = 0, rate = 0;
    var task = System.Threading.Tasks.Task.Run(() => SampleCache.DecodeAll(f, out ch, out rate));
    while (!task.IsCompleted) yield return null;
    if (loaderObject != null) Destroy(loaderObject);
    float[] d = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? task.Result : null;
    if (d == null || ch < 1 || masterObj == null || masterObj.parent == null) yield break;

    AudioClip c = AudioClip.Create(Path.GetFileNameWithoutExtension(f), d.Length / ch, ch, rate, false);
    c.SetData(d, 0);
    ClipRelease.WhenDone(c, true);
    UISound.Play(masterObj.parent.GetComponent<AudioSource>(), c, .25f);
  }

  Coroutine _StreamRoutine;
  bool previewing = false;
  void preview(bool on) {
    previewing = on;
    if (on) {
      if (masterObj == null) return;
      if (masterObj.parent == null) return;

      string f = sampleManager.instance.parseFilename(filename);
      if (!File.Exists(f)) return;
      if (_StreamRoutine != null) {
        if (loaderObject != null) Destroy(loaderObject);
        StopCoroutine(_StreamRoutine);
      }
      _StreamRoutine = StartCoroutine(streamRoutine(f));
    } else {
      if (loaderObject != null) Destroy(loaderObject);
      if (_StreamRoutine != null) StopCoroutine(_StreamRoutine);
      if (masterObj != null) {
        if (masterObj.parent != null) masterObj.parent.GetComponent<AudioSource>().Stop();
      }
    }
  }

  GameObject loaderObject;
  public override void setState(manipState state) {
    if (curState == state) return;

    if (previewing) {
      if (previewWhenGrabbed) {
        if (state == manipState.none) preview(false);
      } else if (curState == manipState.selected) preview(false);
    }

    if (curState == manipState.grabbed && state != manipState.grabbed) {
      flashEmptySamplers(false);
      Release();
    }

    curState = state;

    if (curState == manipState.none) {
      highlight.SetActive(false);
    }
    if (curState == manipState.selected) {
      highlight.SetActive(true);
      highlightGain = .3f;
      SetHighlight(highlightGain);
      if (deck == null) preview(true);
    }

    if (curState == manipState.grabbed) {
      if (returnRoutineID != null) StopCoroutine(returnRoutineID);
      highlight.SetActive(true);
      flashEmptySamplers(true);
      highlightGain = .5f;
      SetHighlight(highlightGain);
      if (insertCoroutine != null) StopCoroutine(insertCoroutine);
      touching.Clear();
      if (deck != null) {
        touching[deck] = Time.time; // still in its slot (its touch is reported from the next physics step)
        tapeTrans.parent = deck;
        tapeTrans.localPosition = Vector3.zero;
        tapeTrans.localRotation = Quaternion.Euler(-90, 0, 0);
        tapeTrans.localScale = tapeTransOrigScale;
      }
      transform.parent = manipulatorObj.parent;
      Vector3 parentScale = manipulatorObj.parent.lossyScale;
      transform.localScale = new Vector3(
        parentScale.x != 0 ? origScale.x / parentScale.x : origScale.x,
        parentScale.y != 0 ? origScale.y / parentScale.y : origScale.y,
        parentScale.z != 0 ? origScale.z / parentScale.z : origScale.z);
    }
  }
}