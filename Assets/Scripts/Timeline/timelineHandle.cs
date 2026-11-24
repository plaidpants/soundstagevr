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

public class timelineHandle : manipObject {
  public Transform timelineTransform;
  timelineEvent _timelineEvent;
  GameObject highlight;

  Color glowColor = Color.HSVToRGB(0.1f, 0.7f, 0.1f);

  public bool stretchMode = false;

  public override void Awake() {
    base.Awake();
    canBeDeleted = true;
    _timelineEvent = GetComponentInParent<timelineEvent>();
    timelineTransform = _timelineEvent.transform.parent;
    createHandleFeedback();
  }

  public override void selfdelete() {
    _timelineEvent.removeSelf();
    Destroy(_timelineEvent.gameObject);
  }

  // Materials are shared between notes (2026-11-24): every note used to make its own copy
  // of its body's and its highlight's material (and of the highlight's mesh), so a roll
  // with 40 notes added 80 materials and the frame rate sank as you recorded. There are
  // only six note colours (by track) and three highlight brightnesses.
  static readonly Dictionary<long, Material> bodyMats = new Dictionary<long, Material>();
  static readonly Dictionary<long, Material> highlightMats = new Dictionary<long, Material>();
  static Material highlightBase;
  Material bodyBase;
  int hueIndex = -1;          // -1: not set yet (the highlight uses glowColor)
  float highlightGain = .75f;
  MeshRenderer highlightRenderer;

  static Material BodyMat(Material baseMat, int hue) {
    if (baseMat == null) return null;
    long k = ((long)baseMat.GetInstanceID() << 4) | (long)hue;
    if (!bodyMats.TryGetValue(k, out Material m) || m == null) {
      m = new Material(baseMat);
      m.SetColor("_EmissionColor", Color.HSVToRGB(hue / 6f, .95f, 1));
      bodyMats[k] = m;
    }
    return m;
  }

  void ApplyHighlight() {
    if (highlightRenderer == null || highlightBase == null) return;
    long k = ((long)(hueIndex + 1) << 16) | (long)Mathf.RoundToInt(highlightGain * 100);
    if (!highlightMats.TryGetValue(k, out Material m) || m == null) {
      m = new Material(highlightBase);
      m.SetColor("_TintColor", hueIndex < 0 ? glowColor : Color.HSVToRGB(hueIndex / 6f, 0.7f, 0.1f));
      m.SetFloat("_EmissionGain", highlightGain);
      highlightMats[k] = m;
    }
    highlightRenderer.sharedMaterial = m;
  }

  void SetHighlightGain(float g) { highlightGain = g; ApplyHighlight(); }

  public void setHue(float h) {
    int idx = ((int)Mathf.Repeat(Mathf.Round(h), 6)) % 6;
    Renderer r = GetComponent<Renderer>();
    if (bodyBase == null) bodyBase = r.sharedMaterial;
    Material m = BodyMat(bodyBase, idx);
    if (m != null) r.sharedMaterial = m;
    hueIndex = idx;
    ApplyHighlight();
  }

  void createHandleFeedback() {
    highlight = new GameObject("highlight");

    MeshFilter m = highlight.AddComponent<MeshFilter>();

    m.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
    MeshRenderer r = highlight.AddComponent<MeshRenderer>();
    if (highlightBase == null) highlightBase = Resources.Load("Materials/Highlight") as Material;
    highlightRenderer = r;

    highlight.transform.SetParent(transform, false);

    highlight.transform.localScale = new Vector3(1.15f, 1.05f, 1.1f);
    highlight.transform.localPosition = new Vector3(0, -.0025f, 0);
    highlightGain = .75f;
    ApplyHighlight();

    highlight.SetActive(false);
  }

  void Update() { long _pp = PerfProbe.Start(); try { UpdateProbed(); } finally { PerfProbe.Add("timelineHandle.Update", _pp); } } // (PerfProbe: temporary)
  void UpdateProbed() {
    if (recordDesired) {
      highlight.SetActive(recording);
      GetComponent<Renderer>().enabled = !recording;
      GetComponent<Collider>().enabled = !recording;
      recordDesired = false;
    }
  }

  bool recordDesired = false;
  bool recording = false;

  public void setRecord(bool on) {
    recordDesired = true;
    recording = on;
  }

  public override void grabUpdate(Transform t) {
    if (!stretchMode) {

      Vector2 a = _timelineEvent._componentInterface.worldPosToGridPos(t.position);
      Vector2 dif = a - manipOffset;
      Vector2 newPos = startPos + dif;

      if (!_timelineEvent._componentInterface.notelock) {
        _timelineEvent.track = Mathf.FloorToInt(_timelineEvent._componentInterface._gridParams.YtoUnit(a.y));
        setHue(_timelineEvent.track);
      }

      float newIn = startIn + dif.x;
      if (_timelineEvent._componentInterface.snapping) {
        newIn = _timelineEvent._componentInterface._gridParams.XtoSnap(newIn, false);
      }
      _timelineEvent.in_out.x = _timelineEvent._componentInterface._gridParams.XtoUnit(newIn);
      _timelineEvent.in_out.y = _timelineEvent.in_out.x + unitLength;

      _timelineEvent.gridUpdate();

    } else {
      Vector3 a = _timelineEvent._componentInterface.worldPosToGridPos(t.position, true);
      float dif = a.x - manipOffset.x;
      if (manipOffset.x - a.x > 0) {
        a.x = dif + manipOffset.x - _timelineEvent._componentInterface._gridParams.unitSize / (2 * _timelineEvent._componentInterface._gridParams.snapFraction);
        _timelineEvent.edgeOutHandle.transform.position = _timelineEvent.edgeOut.position = timelineTransform.TransformPoint(a);

        a.x = manipOffset.x + _timelineEvent._componentInterface._gridParams.unitSize / (2 * _timelineEvent._componentInterface._gridParams.snapFraction);
        _timelineEvent.edgeInHandle.transform.position = _timelineEvent.edgeIn.position = timelineTransform.TransformPoint(a);
      } else {
        a.x = dif + manipOffset.x + _timelineEvent._componentInterface._gridParams.unitSize / (2 * _timelineEvent._componentInterface._gridParams.snapFraction);
        _timelineEvent.edgeInHandle.transform.position = _timelineEvent.edgeIn.position = timelineTransform.TransformPoint(a);

        a.x = manipOffset.x - _timelineEvent._componentInterface._gridParams.unitSize / (2 * _timelineEvent._componentInterface._gridParams.snapFraction);
        _timelineEvent.edgeOutHandle.transform.position = _timelineEvent.edgeOut.position = timelineTransform.TransformPoint(a);
      }

      _timelineEvent.recalcTrackPosition();
    }
  }

  float startIn, unitLength;
  Vector2 startPos;
  Vector2 manipOffset;
  public override void setState(manipState state) {
    if (curState == state) return;

    if (curState == manipState.grabbed && state != manipState.grabbed) {
      _timelineEvent.grabbed = false;
      if (stretchMode) stretchMode = false;

      _timelineEvent.transform.position = highlight.transform.position;
      _timelineEvent.transform.rotation = highlight.transform.rotation;

      highlight.transform.localPosition = Vector3.zero;
      highlight.transform.localRotation = Quaternion.identity;

      _timelineEvent.transform.parent = timelineTransform;
      _timelineEvent.recalcTrackPosition();

      _timelineEvent.toggleEdges(true);

      _timelineEvent.overlapCheck(true); // merge with notes it was dropped on (2026-11-24)
    }

    curState = state;

    if (curState == manipState.none) {

      if (multiselected) {
        highlight.SetActive(true);
        SetHighlightGain(.9f);
      } else highlight.SetActive(false);

    }
    if (curState == manipState.selected) {
      highlight.SetActive(true);
      SetHighlightGain(.55f);
    }
    if (curState == manipState.grabbed) {
      _timelineEvent._componentInterface.killMultiselect();
      _timelineEvent.grabbed = true;
      _timelineEvent.toggleEdges(false);
      highlight.SetActive(true);

      if (stretchMode) manipOffset = _timelineEvent._componentInterface.worldPosToGridPos(manipulatorObj.position, true);
      else {
        manipOffset = _timelineEvent._componentInterface.worldPosToGridPos(manipulatorObj.position);
        startPos = _timelineEvent.transform.localPosition;
        startIn = _timelineEvent._componentInterface._gridParams.UnittoX(_timelineEvent.in_out.x);
        unitLength = _timelineEvent.in_out.y - _timelineEvent.in_out.x;
      }
      SetHighlightGain(.75f);
    }
  }

  bool multiselected = false;
  public void OnMultiselect(bool on) {
    multiselected = on;
    highlight.SetActive(on);
    if (on) SetHighlightGain(.9f);
  }
}

