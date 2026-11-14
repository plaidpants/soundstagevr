// Draw-call experiment (2026-11). A big patch issues ~2700 draw calls a frame on Quest,
// one per renderer, and Quest's tiled GPU replays every draw for each screen tile (more
// tiles with MSAA on). Hiding all 665 labels saved ~0.5 ms and all 553 glow parts ~2.5 ms,
// so the rest of the cost is spread over the device bodies, knobs, jacks and buttons -
// many copies of the same mesh with the same (shared, see MaterialCache) material.
//
// GPU instancing lets Unity draw all copies of one mesh + material in one draw call.
// It only works for shaders that support it (Standard does; custom shaders need
// #pragma multi_compile_instancing). Menu items below switch it on / off for every
// material asset in the project; the [Stats] drawCalls figure shows the effect.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BatchingTestTools {
  const string Menu = "SoundStage/Batching test/";

  [MenuItem(Menu + "Enable GPU instancing on all materials")]
  static void On() { SetAll(true); }

  [MenuItem(Menu + "Disable GPU instancing on all materials")]
  static void Off() { SetAll(false); }

  static void SetAll(bool on) {
    int changed = 0, supported = 0, total = 0;
    var unsupported = new Dictionary<string, int>();
    foreach (string guid in AssetDatabase.FindAssets("t:Material")) {
      string path = AssetDatabase.GUIDToAssetPath(guid);
      if (!path.StartsWith("Assets/")) continue;
      Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (m == null || m.shader == null) continue;
      total++;
      bool canInstance = ShaderSupportsInstancing(m.shader);
      if (canInstance) supported++;
      else {
        unsupported.TryGetValue(m.shader.name, out int n);
        unsupported[m.shader.name] = n + 1;
      }
      if (m.enableInstancing != on) {
        m.enableInstancing = on;
        EditorUtility.SetDirty(m);
        changed++;
      }
    }
    AssetDatabase.SaveAssets();
    var sb = new System.Text.StringBuilder();
    foreach (var kv in unsupported) sb.Append("\n  " + kv.Key + " x" + kv.Value);
    Debug.Log("[BatchingTestTools] GPU instancing " + (on ? "ON" : "OFF") + ": " + changed + " of " + total +
              " materials changed; " + supported + " use a shader that supports instancing. Shaders without it:" + sb);
  }

  // Built-in shaders (Standard etc.) support instancing; for the project's own shaders,
  // look for the pragma in the source.
  static bool ShaderSupportsInstancing(Shader s) {
    string path = AssetDatabase.GetAssetPath(s);
    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return true;
    string src = System.IO.File.ReadAllText(path);
    return src.Contains("multi_compile_instancing") || src.Contains("surface "); // surface shaders get it automatically
  }
}
