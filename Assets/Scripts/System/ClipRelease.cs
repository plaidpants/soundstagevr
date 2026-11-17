// Frees the audio clips that RuntimeAudioClipLoader makes (2026-11).
//
// Every tape loaded into a deck, and every tape preview, made a new AudioClip with
// RuntimeAudioClipLoader.Manager.Load(..., useCache: false). Nothing ever destroyed it,
// and the loader's own tables (Manager, private) kept a reference to each one, so memory grew
// with every tape put in - and every patch load or multiplayer patch share reloads all
// the tapes (the test harness saw 100 MB -> 500 MB in half an hour on the PC).
// A deck copies the samples out of the clip, so the clip can go as soon as it has
// loaded; a preview's clip goes once it has finished playing. A clip given up while still
// loading (tape pulled out, device deleted) goes when the loader is done with it.

using System.Collections.Generic;
using UnityEngine;

public class ClipRelease : MonoBehaviour {
  class Pending { public AudioClip clip; public bool afterPlaying; public float loadedAt = -1; }
  static readonly List<Pending> pending = new List<Pending>();
  static ClipRelease instance;

  // the clip has loaded and its samples have been copied: free it now
  public static void Now(AudioClip c) {
    if (c == null) return;
    Forget(c);
    Destroy(c);
  }

  // Drop the clip from RuntimeAudioClipLoader.Manager's private tables. The loader is an
  // Asset Store package that isn't in the repository, so it isn't edited: its fields are
  // reached by name, and if a version doesn't have them, nothing happens.
  static System.Reflection.FieldInfo[] tables;
  static void Forget(AudioClip c) {
    if (tables == null) {
      var list = new List<System.Reflection.FieldInfo>();
      foreach (string n in new[] { "audioClipToLoadType", "audioClipToLoadState", "cache" }) {
        var f = typeof(RuntimeAudioClipLoader.Manager).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (f != null) list.Add(f);
      }
      tables = list.ToArray();
    }
    foreach (var f in tables) {
      try {
        if (!(f.GetValue(null) is System.Collections.IDictionary d)) continue;
        if (d.Contains(c)) { d.Remove(c); continue; } // keyed by clip
        object key = null;                            // the cache: keyed by file name
        foreach (System.Collections.DictionaryEntry e in d) if ((object)e.Value == c) { key = e.Key; break; }
        if (key != null) d.Remove(key);
      } catch (System.Exception) { }
    }
  }

  // free it once it has loaded (and, for a preview, played through)
  public static void WhenDone(AudioClip c, bool afterPlaying = false) {
    if (c == null) return;
    pending.Add(new Pending { clip = c, afterPlaying = afterPlaying });
    if (instance == null) {
      GameObject g = new GameObject("ClipRelease");
      DontDestroyOnLoad(g);
      instance = g.AddComponent<ClipRelease>();
    }
  }

  void Update() {
    for (int i = pending.Count - 1; i >= 0; i--) {
      Pending p = pending[i];
      if (p.clip == null) { pending.RemoveAt(i); continue; }
      AudioDataLoadState s = RuntimeAudioClipLoader.Manager.GetAudioClipLoadState(p.clip);
      if (s != AudioDataLoadState.Loaded && s != AudioDataLoadState.Failed) continue; // the loader is still writing into it
      if (p.loadedAt < 0) p.loadedAt = Time.unscaledTime;
      float wait = p.afterPlaying && s == AudioDataLoadState.Loaded ? p.clip.length + 1f : 0f;
      if (Time.unscaledTime - p.loadedAt < wait) continue;
      pending.RemoveAt(i);
      Now(p.clip);
    }
  }
}
