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

// Lengthens a housing (mesh + box colliders) toward -x without distorting its ends:
// every vertex / collider edge on the -x side of the cut moves by the extra length,
// the rest stays put. Used by the keyboard and xyloroll range handles.
public class StretchableHousing {
  Transform t;
  MeshFilter mf;
  Mesh mesh;       // per-instance copy, made the first time the housing is lengthened
  float lastExtra = 0;
  Vector3[] baseVerts;
  BoxCollider[] boxes;
  Vector3[] boxCenter, boxSize;
  float cut;

  public void Destroy() {
    if (mesh != null) Object.Destroy(mesh);
  }

  public StretchableHousing(Transform target) {
    t = target;
    mf = t.GetComponent<MeshFilter>();
    Bounds b = new Bounds();
    bool have = false;
    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) {
      b = mf.sharedMesh.bounds; have = true;
    } else mf = null;
    boxes = t.GetComponents<BoxCollider>();
    boxCenter = new Vector3[boxes.Length];
    boxSize = new Vector3[boxes.Length];
    for (int i = 0; i < boxes.Length; i++) {
      boxCenter[i] = boxes[i].center;
      boxSize[i] = boxes[i].size;
      Bounds bb = new Bounds(boxes[i].center, boxes[i].size);
      if (have) b.Encapsulate(bb); else { b = bb; have = true; }
    }
    cut = b.center.x;
  }

  // extra: added length in the PARENT's local units (positive = longer toward -x)
  public void SetExtra(float extra) {
    float sx = Mathf.Abs(t.localScale.x) > 1e-6f ? t.localScale.x : 1f;
    if (Mathf.Approximately(extra, lastExtra)) return;
    lastExtra = extra;
    float d = -extra / sx;
    if (mesh == null && mf != null) {
      Mesh orig = mf.sharedMesh;
      mesh = Object.Instantiate(orig);
      mesh.name = orig.name + " (stretch)";
      baseVerts = mesh.vertices;
      // everything drawing this mesh (the housing and its grab highlight) uses the copy
      foreach (MeshFilter f in t.GetComponentsInChildren<MeshFilter>(true)) {
        if (f.sharedMesh == orig) f.sharedMesh = mesh;
      }
    }
    if (mesh != null) {
      Vector3[] v = new Vector3[baseVerts.Length];
      for (int i = 0; i < v.Length; i++) {
        v[i] = baseVerts[i];
        if (v[i].x < cut) v[i].x += d;
      }
      mesh.vertices = v;
      mesh.RecalculateBounds();
    }
    for (int i = 0; i < boxes.Length; i++) {
      float min = boxCenter[i].x - boxSize[i].x * .5f, max = boxCenter[i].x + boxSize[i].x * .5f;
      if (min < cut) min += d;
      if (max < cut) max += d;
      Vector3 c = boxCenter[i]; c.x = (min + max) * .5f;
      Vector3 s = boxSize[i]; s.x = max - min;
      boxes[i].center = c; boxes[i].size = s;
    }
  }
}
