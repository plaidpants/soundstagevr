// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Performance logging for development builds (added 2026-11). Self-bootstraps; logs
// "[Stats]" lines to logcat:
//  - every 5 s: frame time, main/render thread time, draw calls, batches, SetPass
//    calls, triangles and vertices drawn (ProfilerRecorder; development builds only),
//    plus total GameObjects / active renderers / MonoBehaviours.
//  - whenever the set of devices changes (and every 30 s): a breakdown per device
//    type: count, renderers, materials, mesh triangles, MonoBehaviours.
// Grab with:  adb logcat -s Unity | findstr "[Stats]"

using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

public class SceneStats : MonoBehaviour {
  public static bool enabledInBuild = true;
  public static float interval = 5f;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (!Debug.isDebugBuild || !enabledInBuild) return;
    GameObject go = new GameObject("SceneStats");
    DontDestroyOnLoad(go);
    go.AddComponent<SceneStats>();
  }

  ProfilerRecorder drawCalls, batches, setPass, tris, verts, mainThread, renderThread;
  float timer, breakdownTimer;
  int frames;
  float frameTimeSum, frameTimeMax;
  int lastDeviceCount = -1;
  float deviceCheckTimer;

  void OnEnable() {
    drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
    batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
    setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
    tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
    mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
    renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
  }

  void OnDisable() {
    drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); verts.Dispose();
    mainThread.Dispose(); renderThread.Dispose();
  }

  static double AvgMs(ProfilerRecorder r) {
    if (!r.Valid || r.Count == 0) return -1;
    double sum = 0;
    int n = r.Count;
    for (int i = 0; i < n; i++) sum += r.GetSample(i).Value;
    return sum / n * 1e-6; // ns -> ms
  }

  static string V(ProfilerRecorder r) { return r.Valid ? r.LastValue.ToString() : "n/a"; }

  void Update() {
    float dt = Time.unscaledDeltaTime;
    frames++;
    frameTimeSum += dt;
    if (dt > frameTimeMax) frameTimeMax = dt;
    timer += Time.unscaledDeltaTime;
    breakdownTimer += Time.unscaledDeltaTime;

    if (timer >= interval) {
      int renderers = 0;
      foreach (Renderer r in FindObjectsOfType<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) renderers++;
      Debug.Log("[Stats] fps=" + (frames / frameTimeSum).ToString("F1") +
                " frameMax=" + (frameTimeMax * 1000f).ToString("F1") + "ms" +
                " main=" + AvgMs(mainThread).ToString("F2") + "ms render=" + AvgMs(renderThread).ToString("F2") + "ms" +
                " drawCalls=" + V(drawCalls) + " batches=" + V(batches) + " setPass=" + V(setPass) +
                " tris=" + V(tris) + " verts=" + V(verts) +
                " gameObjects=" + FindObjectsOfType<Transform>().Length + " renderers=" + renderers +
                " behaviours=" + FindObjectsOfType<MonoBehaviour>().Length +
                " devices=" + GameObject.FindGameObjectsWithTag("instrument").Length +
                " glow=" + (masterControl.instance != null && masterControl.instance.mainGlowShader != null && masterControl.instance.mainGlowShader.enabled ?
                            (masterControl.instance.glowQuality == 2 ? "high" : "low") : "off") +
                " passthrough=" + (passthroughControl.instance != null && passthroughControl.instance.isOn) + " sharedMats=" + MaterialCache.Count);
      timer = 0; frames = 0; frameTimeSum = 0; frameTimeMax = 0;
    }

    deviceCheckTimer += Time.unscaledDeltaTime;
    if (deviceCheckTimer < 1f) return; // FindGameObjectsWithTag allocates: once a second
    deviceCheckTimer = 0;
    GameObject[] devices = GameObject.FindGameObjectsWithTag("instrument");
    if (devices.Length != lastDeviceCount || breakdownTimer >= 30f) {
      lastDeviceCount = devices.Length;
      breakdownTimer = 0;
      LogBreakdown(devices);
    }
  }

  class TypeStats { public int count, renderers, tris, behaviours, objects; public HashSet<Material> mats = new HashSet<Material>(); }

  void LogBreakdown(GameObject[] devices) {
    Dictionary<string, TypeStats> byType = new Dictionary<string, TypeStats>();
    foreach (GameObject d in devices) {
      deviceInterface di = d.GetComponent<deviceInterface>();
      string type = di != null ? di.GetType().Name.Replace("DeviceInterface", "").Replace("deviceInterface", "") : d.name;
      TypeStats ts;
      if (!byType.TryGetValue(type, out ts)) { ts = new TypeStats(); byType[type] = ts; }
      ts.count++;
      ts.objects += d.GetComponentsInChildren<Transform>(true).Length;
      ts.behaviours += d.GetComponentsInChildren<MonoBehaviour>(true).Length;
      foreach (Renderer r in d.GetComponentsInChildren<Renderer>()) {
        if (!r.enabled) continue;
        ts.renderers++;
        foreach (Material m in r.sharedMaterials) if (m != null) ts.mats.Add(m);
        MeshFilter mf = r.GetComponent<MeshFilter>();
        Mesh mesh = mf != null ? mf.sharedMesh : (r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : null);
        if (mesh != null) for (int s = 0; s < mesh.subMeshCount; s++) ts.tris += (int)(mesh.GetIndexCount(s) / 3);
      }
    }
    StringBuilder sb = new StringBuilder("[Stats] devices by type (count: renderers / distinct materials / mesh tris / objects / behaviours):");
    foreach (KeyValuePair<string, TypeStats> kv in byType) {
      sb.Append(" | ").Append(kv.Key).Append(" x").Append(kv.Value.count).Append(": ")
        .Append(kv.Value.renderers).Append(" / ").Append(kv.Value.mats.Count).Append(" / ")
        .Append(kv.Value.tris).Append(" / ").Append(kv.Value.objects).Append(" / ").Append(kv.Value.behaviours);
    }
    Debug.Log(sb.ToString());
  }
}
