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

// Far-away grid for the drum machine and Scale Sequencer (2026-11). DeviceLOD used to
// drop the grid cells that existed when the device appeared (they're small parts) but
// kept ones added later by stretching, so a far sequencer showed a ragged half grid.
// Now the cells are left out of DeviceLOD, and when the device switches to its far
// version this hides every cell and draws the whole grid as ONE mesh of plain boxes in
// two draw calls: white boxes for empty cells, glowing boxes in each row's colour for
// lit ones. The mesh is rebuilt only when cells or their on/off states change.
// The play head is a separate glowing bar over the current column (a third draw call),
// moved each frame from the step handle's position - no mesh rebuild per step.

using UnityEngine;
using System.Collections.Generic;

public class GridFarProxy : MonoBehaviour {
  public Material offMat;     // the cell body (coreMatWhite)
  public Material glowBase;   // the cell glow (coreColorGlow); row colour comes from vertex colours
  public Transform playhead;  // the step handle; its local x is the current column's x
  public static Color playheadColor = new Color(.35f, .35f, .45f);
  public static float playheadGain = .45f;
  public static float glowGain = .6f;
  public static Vector3 boxHalf = new Vector3(.0185f, .0185f, .02f);
  const float checkInterval = .25f;

  float nextCheck;
  bool far = false;
  readonly List<Renderer> hidden = new List<Renderer>();
  readonly List<button> cells = new List<button>();
  GameObject proxy;
  MeshRenderer proxyRend;
  Transform bar;
  Renderer barRend;
  float gridTop, gridBottom;
  Mesh mesh;
  int lastHash = 0;
  LODGroup lod;

  public static bool IsCellRoot(Transform t) {
    return t.name.StartsWith("touchCube") || t.name.StartsWith("touchDial");
  }

  // true for anything belonging to a grid cell (or the proxy) of this device
  public static bool IsGridPart(Transform t, Transform device) {
    while (t != null && t != device) {
      if (t.parent == device && (IsCellRoot(t) || t.name == "gridFarProxy")) return true;
      t = t.parent;
    }
    return false;
  }

  void Start() {
    nextCheck = Time.unscaledTime + Random.value * checkInterval; // spread the checks out
  }

  void Update() {
    if (far && bar != null && playhead != null) {
      Vector3 p = bar.localPosition;
      p.x = playhead.localPosition.x;
      bar.localPosition = p;
    }
    if (Time.unscaledTime < nextCheck) return;
    nextCheck = Time.unscaledTime + checkInterval;

    Camera cam = Camera.main;
    if (cam == null) return;
    if (lod == null) lod = GetComponent<LODGroup>();

    // same measure Unity's LODGroup uses, so the grid switches with the rest of the device
    float size; Vector3 centre;
    if (lod != null) {
      Vector3 s = transform.lossyScale;
      size = lod.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
      centre = transform.TransformPoint(lod.localReferencePoint);
    } else {
      size = .3f * transform.lossyScale.x;
      centre = transform.position;
    }
    float dist = Mathf.Max(.01f, Vector3.Distance(cam.transform.position, centre));
    float frac = size / (2f * dist * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
    float thr = DeviceLOD.farDetailScreenHeight;
    bool wantFar = DeviceLOD.enabledGlobally && (far ? frac < thr * 1.05f : frac < thr * .95f);

    if (wantFar) GoFar();
    else if (far) GoNear();
  }

  void CollectCells() {
    cells.Clear();
    foreach (Transform c in transform) {
      if (!IsCellRoot(c)) continue;
      button b = c.GetComponent<button>();
      if (b != null) cells.Add(b);
    }
  }

  void GoFar() {
    CollectCells();
    // hide every cell part still showing (cells added since the last check too)
    foreach (button b in cells) {
      foreach (Renderer r in b.GetComponentsInChildren<Renderer>()) {
        if (r.enabled) { r.enabled = false; hidden.Add(r); }
      }
    }
    int h = cells.Count;
    foreach (button b in cells) {
      Vector3 p = b.transform.localPosition;
      h = h * 31 + Mathf.RoundToInt(p.x * 1000) * 7 + Mathf.RoundToInt(p.y * 1000) * 13 + (b.isHit ? 1 : 0);
    }
    if (proxy == null) MakeProxy();
    if (h != lastHash || !far) { lastHash = h; Rebuild(); }
    proxyRend.enabled = true;
    if (barRend != null) barRend.enabled = playhead != null && cells.Count > 0;
    far = true;
  }

  void GoNear() {
    foreach (Renderer r in hidden) if (r != null) r.enabled = true;
    hidden.Clear();
    if (proxyRend != null) proxyRend.enabled = false;
    if (barRend != null) barRend.enabled = false;
    far = false;
  }

  void MakeProxy() {
    proxy = new GameObject("gridFarProxy");
    proxy.transform.SetParent(transform, false);
    mesh = new Mesh();
    mesh.name = "gridFarProxy";
    mesh.MarkDynamic();
    proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
    proxyRend = proxy.AddComponent<MeshRenderer>();
    proxyRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    proxyRend.receiveShadows = false;
    proxyRend.sharedMaterials = new Material[] { offMat, MaterialCache.Glow(glowBase, Color.white, glowGain) };

    GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
    Destroy(b.GetComponent<Collider>());
    b.name = "playhead";
    b.transform.SetParent(proxy.transform, false);
    bar = b.transform;
    barRend = b.GetComponent<Renderer>();
    barRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    barRend.receiveShadows = false;
    barRend.sharedMaterial = MaterialCache.Glow(glowBase, playheadColor, playheadGain);
    barRend.enabled = false;
  }

  static readonly Vector3[] faceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
  readonly List<Vector3> verts = new List<Vector3>();
  readonly List<Vector3> norms = new List<Vector3>();
  readonly List<Color> cols = new List<Color>();
  readonly List<int> offTris = new List<int>();
  readonly List<int> onTris = new List<int>();

  void Rebuild() {
    verts.Clear(); norms.Clear(); cols.Clear(); offTris.Clear(); onTris.Clear();
    gridTop = float.MinValue; gridBottom = float.MaxValue;
    foreach (button b in cells) {
      Vector3 c = b.transform.localPosition;
      gridTop = Mathf.Max(gridTop, c.y); gridBottom = Mathf.Min(gridBottom, c.y);
      Color col = b.isHit ? b.GlowColor : Color.white;
      List<int> tris = b.isHit ? onTris : offTris;
      for (int f = 0; f < 6; f++) {
        Vector3 n = faceN[f];
        Vector3 u = Mathf.Abs(n.y) > .5f ? Vector3.right : Vector3.up;
        Vector3 v = Vector3.Cross(n, u);
        int i0 = verts.Count;
        for (int k = 0; k < 4; k++) {
          float su = (k == 1 || k == 2) ? 1 : -1, sv = (k >= 2) ? 1 : -1;
          Vector3 p = n + u * su + v * sv;
          verts.Add(c + Vector3.Scale(p, boxHalf));
          norms.Add(n);
          cols.Add(col);
        }
        tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2); // clockwise seen from outside
        tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
      }
    }
    mesh.Clear();
    mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
    mesh.SetVertices(verts);
    mesh.SetNormals(norms);
    mesh.SetColors(cols);
    mesh.subMeshCount = 2;
    mesh.SetTriangles(offTris, 0);
    mesh.SetTriangles(onTris, 1);
    mesh.RecalculateBounds();

    // play head bar: the grid's height, a little bigger than a cell, just in front
    if (bar != null && cells.Count > 0) {
      bar.localScale = new Vector3(boxHalf.x * 2.2f, gridTop - gridBottom + boxHalf.y * 2.2f, boxHalf.z * 2.2f);
      bar.localPosition = new Vector3(playhead != null ? playhead.localPosition.x : 0, (gridTop + gridBottom) * .5f, 0);
    }
  }

  void OnDestroy() {
    if (mesh != null) Destroy(mesh);
  }
}
