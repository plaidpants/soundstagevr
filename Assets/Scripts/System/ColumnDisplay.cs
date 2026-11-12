// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// CPU side of the "SoundStage/WaveColumns" shader: one RG8 texel per display column
// (R = bar height in rows, G = alternate colour flag). Replaces drawing every pixel of
// a big RGBA texture on the CPU and uploading it (the looper/recorder waveforms were
// 1024x512 = 2 MB per upload). Columns can be written from the audio thread; Upload()
// runs on the main thread.

using UnityEngine;

public class ColumnDisplay {
  public readonly int columns, rows;
  public static float edgeMargin = 0.08f; // WaveColumns: keep bars this far (fraction of half height) from the edges
  public static float maxBright = 1.5f; // see WaveColumns.shader: caps the line so its colour survives
  readonly byte[] data;
  readonly Texture2D tex;
  public bool dirty = true;

  public ColumnDisplay(Renderer rend, int columns, int rows, Color line, Color lineAlt, Color background, int mode) {
    this.columns = columns;
    this.rows = rows;
    data = new byte[columns * 2];
    tex = new Texture2D(columns, 1, TextureFormat.RG16, false, true);
    tex.filterMode = FilterMode.Bilinear; // shader interpolates heights between columns
    tex.wrapMode = TextureWrapMode.Clamp;

    Material m = rend.material;
    Shader sh = Resources.Load<Shader>("Shaders/WaveColumns");
    if (sh != null) {
      Vector2 scale = m.mainTextureScale, offset = m.mainTextureOffset;
      m.shader = sh; // keeps _TintColor / _EmissionGain from the original material
      m.SetTextureScale("_ColumnTex", scale);
      m.SetTextureOffset("_ColumnTex", offset);
    } else {
      Debug.LogWarning("[ColumnDisplay] missing Resources/Shaders/WaveColumns");
    }
    m.SetTexture("_ColumnTex", tex);
    m.SetFloat("_Columns", columns);
    m.SetFloat("_Rows", rows);
    m.SetFloat("_Mode", mode);
    m.SetFloat("_MaxBright", maxBright);
    m.SetFloat("_EdgeMargin", edgeMargin);
    SetColors(m, line, lineAlt, background);
    material = m;
    Upload();
  }

  public readonly Material material;

  public static void SetColors(Material m, Color line, Color lineAlt, Color background) {
    m.SetColor("_LineColor", line);
    m.SetColor("_LineColorAlt", lineAlt);
    m.SetColor("_BGColor", background);
  }

  // height in rows (clamped to 0..255), alt = use the alternate line colour
  public void SetColumn(int column, int height, bool alt = false) {
    if (column < 0 || column >= columns) return;
    data[column * 2] = (byte)Mathf.Clamp(height, 0, 255);
    data[column * 2 + 1] = (byte)(alt ? 255 : 0);
    dirty = true;
  }

  public void Clear() {
    System.Array.Clear(data, 0, data.Length);
    dirty = true;
  }

  // For ring-shaped meshes lying in their local XZ plane (the looper's loopRing): fit
  // u = s * angle/2pi + c and v = a * radius + b to the mesh's own vertex UVs, and let
  // the shader compute exact polar UVs per pixel. Needs the mesh to be readable
  // (loopRing.fbx import setting "Read/Write"). Returns false and leaves UVs alone if the
  // mesh isn't a flat XZ ring or the fit is poor.
  public bool EnablePolar(Renderer rend) {
    MeshFilter mf = rend.GetComponent<MeshFilter>();
    Mesh mesh = mf != null ? mf.sharedMesh : null;
    if (mesh == null || !mesh.isReadable) { Debug.LogWarning("[ColumnDisplay] polar: mesh not readable"); return false; }
    Vector3[] vs = mesh.vertices;
    Vector2[] uvs = mesh.uv;
    if (vs.Length == 0 || uvs.Length != vs.Length) return false;
    Bounds bb = mesh.bounds;
    if (bb.size.y > 0.05f * Mathf.Max(bb.size.x, bb.size.z)) { Debug.LogWarning("[ColumnDisplay] polar: mesh is not flat in XZ"); return false; }

    // u: try both directions, keep the one whose offsets agree (circular mean/variance)
    float bestS = 1, bestC = 0, bestR = -1;
    for (int si = 0; si < 2; si++) {
      float sgn = si == 0 ? 1f : -1f;
      double cx = 0, cy = 0; int n = 0;
      for (int i = 0; i < vs.Length; i++) {
        if (uvs[i].x < 0.02f || uvs[i].x > 0.98f) continue; // seam vertices are ambiguous
        float ang = Mathf.Atan2(vs[i].z, vs[i].x) / (2f * Mathf.PI);
        float c = uvs[i].x - sgn * ang;
        cx += Mathf.Cos(c * 2f * Mathf.PI); cy += Mathf.Sin(c * 2f * Mathf.PI); n++;
      }
      if (n == 0) return false;
      float r = (float)(System.Math.Sqrt(cx * cx + cy * cy) / n);
      if (r > bestR) {
        bestR = r; bestS = sgn;
        float c0 = Mathf.Atan2((float)cy, (float)cx) / (2f * Mathf.PI);
        bestC = c0 - Mathf.Floor(c0);
      }
    }
    // v: least squares on radius
    double sr = 0, sv = 0, srr = 0, srv = 0; int m = vs.Length;
    for (int i = 0; i < m; i++) {
      double rr = new Vector2(vs[i].x, vs[i].z).magnitude, vv = uvs[i].y;
      sr += rr; sv += vv; srr += rr * rr; srv += rr * vv;
    }
    double den = m * srr - sr * sr;
    if (bestR < 0.99f || System.Math.Abs(den) < 1e-9) { Debug.LogWarning("[ColumnDisplay] polar: poor fit (" + bestR + ")"); return false; }
    float a = (float)((m * srv - sr * sv) / den);
    float b = (float)((sv - a * sr) / m);

    material.SetFloat("_Polar", 1f);
    material.SetVector("_PolarU", new Vector4(bestS, bestC, 0, 0));
    material.SetVector("_PolarV", new Vector4(a, b, 0, 0));
    Debug.Log("[ColumnDisplay] polar mapping on " + rend.name + ": u = " + bestS + " * angle + " + bestC.ToString("F3") +
              ", v = " + a.ToString("F3") + " * r + " + b.ToString("F3") + " (fit " + bestR.ToString("F4") + ")");
    return true;
  }

  public void Upload() {
    tex.SetPixelData(data, 0);
    tex.Apply(false);
    dirty = false;
  }
}
