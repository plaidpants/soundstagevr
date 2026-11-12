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

// Oscilloscope display (reworked 2026-11).
// The audio thread records one height (0..63) per displayed column into a 512-entry
// ring buffer; each frame the 512 bytes are uploaded as a 512x1 R8 texture and the
// SoundStage/WaveScope shader draws the trace. Previously the CPU drew a 512x64 RGBA
// image (native ProcessWaveTexture) and uploaded all 128 KB every frame per oscillator.
public class waveViz : MonoBehaviour {
  public Renderer waverend;
  public int period = 512;
  public Color32 waveBG = Color.black;
  public Color32 waveLine = Color.white;

  const int wavewidth = 512;
  const int waveheight = 64;

  // written on the audio thread, read on the main thread (tearing is harmless here)
  readonly byte[] heights = new byte[wavewidth];
  int curWaveW = 0;

  Texture2D heightTex;
  Material waveMat;
  int lastUpload = -100;

  static Shader scopeShader;
  static readonly int idWaveTex = Shader.PropertyToID("_WaveTex");
  static readonly int idHead = Shader.PropertyToID("_Head");
  static readonly int idLine = Shader.PropertyToID("_LineColor");
  static readonly int idBG = Shader.PropertyToID("_BGColor");

  void Awake() {
    heightTex = new Texture2D(wavewidth, 1, TextureFormat.R8, false);
    heightTex.filterMode = FilterMode.Point;
    heightTex.wrapMode = TextureWrapMode.Repeat;
    byte mid = (byte)((waveheight - 1) / 2);   // flat line at rest
    for (int i = 0; i < wavewidth; i++) heights[i] = mid;
    heightTex.SetPixelData(heights, 0);
    heightTex.Apply(false);
  }

  void Start() {
    if (scopeShader == null) scopeShader = Resources.Load<Shader>("Shaders/WaveScope");
    waveMat = waverend.material;
    if (scopeShader == null) {
      Debug.LogError("[waveViz] Resources/Shaders/WaveScope shader not found");
      return;
    }
    // Keeps the material's _TintColor / _EmissionGain, so the glow is unchanged.
    waveMat.shader = scopeShader;
    waveMat.SetTexture(idWaveTex, heightTex);
    ApplyColors();
  }

  void OnDestroy() {
    if (heightTex != null) Destroy(heightTex);
  }

  void ApplyColors() {
    waveMat.SetColor(idLine, waveLine);
    waveMat.SetColor(idBG, waveBG);
  }

  // Audio thread: same sampling as the old native ProcessWaveTexture.
  public void UpdateViz(float[] buffer) {
    int n = buffer.Length / period;
    int w = curWaveW;
    for (int i = 0; i < n; i++) {
      float t = (buffer[i * period] + 1.0f) * .5f;
      t = t > 1f ? 1f : (t < 0f ? 0f : t);
      heights[w] = (byte)((waveheight - 1) * t);
      w = (w + 1) % wavewidth;
    }
    curWaveW = w;
  }

  void Update() {
    if (waveMat == null || scopeShader == null) return;
    if (!DisplayUpdate.Due(waverend, ref lastUpload, 1)) return;   // 512 bytes: every visible frame is fine
    heightTex.SetPixelData(heights, 0);
    heightTex.Apply(false);
    waveMat.SetFloat(idHead, (float)curWaveW / wavewidth);
    ApplyColors();   // waveLine changes when the oscillator switches between LFO and audio rate
  }
}
