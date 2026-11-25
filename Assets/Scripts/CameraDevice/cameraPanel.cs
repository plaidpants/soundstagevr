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

// The camera's preview screen (2026-11-22). Grab it with one hand to move it; grab it
// with both hands (anywhere on it, or on the bars that light up at its left and right
// edges) and pull them apart or together to resize it, like a device's handles. It used
// to take its size from how far it was held from the camera, so moving it changed its size.
public class cameraPanel : manipObject {
  GameObject highlight;
  Material highlightMat;
  Transform masterObj;
  public cameraDeviceInterface _deviceInterface;

  public static float minScale = 2f, maxScale = 16f;

  cameraPanelEdge[] edges;

  public override void Awake() {
    base.Awake();
    masterObj = transform.parent;
    createHandleFeedback();
    makeEdges();
  }

  void createHandleFeedback() {
    highlight = new GameObject("highlight");

    MeshFilter m = highlight.AddComponent<MeshFilter>();
    m.mesh = GetComponent<MeshFilter>().mesh;

    MeshRenderer r = highlight.AddComponent<MeshRenderer>();
    r.material = Resources.Load("Materials/Highlight") as Material;
    highlightMat = r.material;

    highlight.transform.SetParent(transform, false);
    highlight.transform.localScale = new Vector3(1.05f, 1.1f, 1.1f);//Vector3.one * 1.1f;

    Color c = Color.HSVToRGB(0f, 0f, .5f);
    highlightMat.SetColor("_TintColor", c);
    highlightMat.SetFloat("_EmissionGain", .35f);

    highlight.SetActive(false);
  }

  // a grab bar along each side edge (a second hand can't take the screen itself while
  // the first holds it)
  void makeEdges() {
    BoxCollider box = GetComponent<BoxCollider>();
    Vector3 size = box != null ? box.size : new Vector3(.096f, .054f, .01f);
    Vector3 center = box != null ? box.center : Vector3.zero;
    // (2026-11-25) not the Highlight material: its shader draws only the inside faces (it is
    // meant as a glow shell around an object), so a solid bar looked inside out
    Material baseMat = Resources.Load("Materials/RecycleSymbol") as Material;
    float w = size.x * .15f;
    edges = new cameraPanelEdge[2];
    for (int i = 0; i < 2; i++) {
      float side = i == 0 ? -1 : 1;
      GameObject g = new GameObject(i == 0 ? "screenEdgeL" : "screenEdgeR");
      g.transform.SetParent(transform, false);
      g.transform.localPosition = center + new Vector3(side * (size.x * .5f - w * .5f), 0, 0);
      BoxCollider bc = g.AddComponent<BoxCollider>();
      bc.size = new Vector3(w, size.y, size.z * 1.5f);

      GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
      Destroy(vis.GetComponent<Collider>());
      vis.name = "bar";
      vis.transform.SetParent(g.transform, false);
      vis.transform.localPosition = new Vector3(side * w * .3f, 0, 0);
      vis.transform.localScale = new Vector3(w * .25f, size.y * .9f, size.z * 1.2f);
      Renderer r = vis.GetComponent<Renderer>();
      if (baseMat != null) { r.material = new Material(baseMat); r.material.SetTexture("_MainTex", Texture2D.whiteTexture); }
      r.material.SetColor("_TintColor", Color.HSVToRGB(0f, 0f, .35f));
      vis.SetActive(false);

      cameraPanelEdge e = g.AddComponent<cameraPanelEdge>();
      e.panel = this;
      e.bar = vis;
      e.barMat = r.material;
      edges[i] = e;
    }
  }

  // the edge bars show while the screen or either bar is highlighted or held
  public void RefreshEdges() {
    if (edges == null) return;
    bool on = curState != manipState.none;
    foreach (cameraPanelEdge e in edges) if (e != null && e.curState != manipState.none) on = true;
    foreach (cameraPanelEdge e in edges) {
      if (e == null || e.bar == null) continue;
      e.bar.SetActive(on);
      if (e.barMat != null) e.barMat.SetFloat("_EmissionGain", e.curState == manipState.grabbed ? .6f : e.curState == manipState.selected ? .45f : .25f);
    }
  }

  public override void setState(manipState state) {
    if (curState == state) return;
    curState = state;

    if (curState == manipState.none) {
      highlight.SetActive(false);
    }
    if (curState == manipState.selected) {
      highlight.SetActive(true);
      highlightMat.SetFloat("_EmissionGain", .35f);
    }
    if (curState == manipState.grabbed) {
      highlight.SetActive(true);
      highlightMat.SetFloat("_EmissionGain", .45f);
    }
    RefreshEdges();
  }

  // ---- moving (one hand) and resizing (two hands) ----
  readonly Transform[] hands = new Transform[2];
  int lastCount;
  Vector3 relPos;        // one hand: the screen in the hand's space
  Quaternion relRot;
  float startDist, startScale; // two hands
  Vector3 midOffset;

  int CollectHands() {
    int n = 0;
    if (curState == manipState.grabbed && manipulatorObj != null) hands[n++] = manipulatorObj;
    if (edges != null)
      foreach (cameraPanelEdge e in edges)
        if (n < 2 && e != null && e.curState == manipState.grabbed && e.manipulatorObj != null) hands[n++] = e.manipulatorObj;
    return n;
  }

  Vector3 Mid() { return Vector3.Lerp(hands[0].position, hands[1].position, .5f); }

  void LateUpdate() {
    int n = CollectHands();
    if (n != lastCount) {
      if (n == 1) {
        relPos = hands[0].InverseTransformPoint(transform.position);
        relRot = Quaternion.Inverse(hands[0].rotation) * transform.rotation;
      } else if (n == 2) {
        startDist = Mathf.Max(.01f, Vector3.Distance(hands[0].position, hands[1].position));
        startScale = Mathf.Max(.01f, transform.localScale.x);
        midOffset = transform.position - Mid();
      } else if (lastCount > 0 && _deviceInterface != null) {
        _deviceInterface.updateResolution(transform.localScale.x > 2);
      }
      lastCount = n;
    }
    if (n == 1) {
      transform.position = hands[0].TransformPoint(relPos);
      transform.rotation = hands[0].rotation * relRot;
    } else if (n == 2) {
      float s = Mathf.Clamp(startScale * Vector3.Distance(hands[0].position, hands[1].position) / startDist, minScale, maxScale);
      transform.localScale = Vector3.one * s;
      transform.position = Mid() + midOffset * (s / startScale);
    }
  }
}

// a grab bar on one side edge of the camera's screen (cameraPanel)
public class cameraPanelEdge : manipObject {
  public cameraPanel panel;
  public GameObject bar;
  public Material barMat;

  public override void setState(manipState state) {
    curState = state;
    if (panel != null) panel.RefreshEdges();
  }
}
