// Merges each menu preview into a single mesh (2026-11, Quest performance).
//
// Menu previews are stripped copies of the real device prefabs, and every renderer
// in them already uses the same menu material. Opening the menu added ~600
// renderers / draw calls and dropped the frame rate. After a preview is set up
// (one frame later, once its Destroy() calls have gone through) its meshes are
// combined into one renderer and the originals are disabled. The menu previews are
// also the templates for the example palette and file-preview copies
// (menuManager.refObjects), so those copies get the single mesh too.
//
// Meshes that aren't Read/Write enabled can't be combined at runtime and are left
// as they are (the model importers were switched to Read/Write for this).

using UnityEngine;
using System.Collections.Generic;

public class MenuPreviewCombiner : MonoBehaviour {
  static MenuPreviewCombiner runner;
  static readonly List<GameObject> pending = new List<GameObject>();
  public static bool enabledCombine = true;

  public static void Queue(GameObject preview) {
    if (!enabledCombine || preview == null) return;
    if (runner == null) {
      GameObject go = new GameObject("MenuPreviewCombiner");
      DontDestroyOnLoad(go);
      runner = go.AddComponent<MenuPreviewCombiner>();
    }
    pending.Add(preview);
    runner.queuedFrame = Time.frameCount;
  }

  int queuedFrame;

  void LateUpdate() {
    if (pending.Count == 0 || Time.frameCount <= queuedFrame) return;
    int before = 0, after = 0;
    foreach (GameObject g in pending) {
      if (g == null) continue;
      int b, a;
      Combine(g, out b, out a);
      before += b; after += a;
    }
    Debug.Log("[MenuPreviewCombiner] " + pending.Count + " previews: " + before + " renderers -> " + after);
    pending.Clear();
  }

  static void Combine(GameObject root, out int before, out int after) {
    before = 0; after = 0;
    // Far-LOD renderers (e.g. the tape's tapeLOD1) would double the geometry: hide them
    // and drop the LODGroups, the preview is always seen up close.
    foreach (LODGroup lg in root.GetComponentsInChildren<LODGroup>(true)) {
      LOD[] lods = lg.GetLODs();
      for (int l = 1; l < lods.Length; l++)
        foreach (Renderer lr in lods[l].renderers) if (lr != null) lr.enabled = false;
      Object.Destroy(lg);
    }

    MeshRenderer[] rends = root.GetComponentsInChildren<MeshRenderer>(true);
    Material mat = null;
    List<CombineInstance> parts = new List<CombineInstance>();
    List<MeshRenderer> merged = new List<MeshRenderer>();
    Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
    int verts = 0;

    foreach (MeshRenderer r in rends) {
      if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
      before++;
      if (r.GetComponent<TextMesh>() != null) continue;
      MeshFilter mf = r.GetComponent<MeshFilter>();
      if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
      if (mat == null) mat = r.sharedMaterial;
      if (r.sharedMaterial != mat) continue;
      Mesh m = mf.sharedMesh;
      for (int s = 0; s < m.subMeshCount; s++) {
        parts.Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = toRoot * r.transform.localToWorldMatrix });
      }
      verts += m.vertexCount;
      merged.Add(r);
    }

    if (merged.Count < 2) { after = before; return; }

    Mesh combined = new Mesh();
    combined.name = root.name + " (menu combined)";
    if (verts > 65000) combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    combined.CombineMeshes(parts.ToArray(), true, true);
    combined.UploadMeshData(true); // no CPU copy needed after this

    GameObject c = new GameObject("combinedPreview");
    c.layer = root.layer;
    c.transform.SetParent(root.transform, false);
    c.AddComponent<MeshFilter>().sharedMesh = combined;
    MeshRenderer cr = c.AddComponent<MeshRenderer>();
    cr.sharedMaterial = mat;
    cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    cr.receiveShadows = false;

    foreach (MeshRenderer r in merged) r.enabled = false;
    after = before - merged.Count + 1;
  }
}
