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

public class spectrumDisplay : MonoBehaviour {
  public AudioSource source;
  int texW = 256;
  int texH = 32;
  public Renderer texrend;

  bool active = false;

  float[] spectrum;

  // Bars are drawn on the GPU (Resources/Shaders/EmissiveColumns) from one byte per
  // column, instead of filling and uploading a 256x32 RGBA texture (32 KB) every other
  // frame (2026-11).
  Texture2D tex;
  byte[] columns;

  void Start() {
    spectrum = new float[texW];
    columns = new byte[texW * 2];

    tex = new Texture2D(texW, 1, TextureFormat.RG16, false, true);
    tex.filterMode = FilterMode.Point;
    tex.wrapMode = TextureWrapMode.Clamp;
    Upload();

    Material m = texrend.material;
    Shader sh = Resources.Load<Shader>("Shaders/EmissiveColumns");
    if (sh != null) m.shader = sh;
    else Debug.LogWarning("[spectrumDisplay] missing Resources/Shaders/EmissiveColumns");
    m.mainTexture = tex;
    m.SetFloat("_Columns", texW);
    m.SetFloat("_Rows", texH);
    m.SetColor("_EmissionColor", Color.HSVToRGB(10 / 400f, 98 / 255f, 1f));
    m.SetFloat("_EmissionGain", .4f);
    SetBlack();
  }

  const float spectrumMult = 5;
  void GenerateColumns() {
    for (int i = 0; i < texW; i++) {
      // old texture: row i2 lit when spectrum * mult * texH >= i2
      float v = spectrum[i] * spectrumMult * texH;
      columns[i * 2] = (byte)Mathf.Clamp(Mathf.FloorToInt(v), 0, 255);
    }
  }

  // The old "off" state was all black (no bars at all, not even row 0).
  void SetBlack() {
    texrend.material.SetFloat("_Active", active ? 1f : 0f);
  }

  void Upload() {
    tex.SetPixelData(columns, 0);
    tex.Apply(false);
  }

  public void toggleActive(bool on) {
    active = on;
    if (tex == null) return;
    if (!active) {
      System.Array.Clear(columns, 0, columns.Length);
      Upload();
    }
    SetBlack();
  }

  int lastUpload = -100;

  void Update() {
    if (!active) return;
    if (!DisplayUpdate.Due(texrend, ref lastUpload, DisplayUpdate.ScopeInterval)) return;

    source.GetSpectrumData(spectrum, 0, FFTWindow.Rectangular);
    GenerateColumns();
    Upload();
  }
}
