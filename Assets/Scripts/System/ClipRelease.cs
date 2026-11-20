// Frees the audio clips made for tape previews (2026-11).
//
// Tape previews used to make a clip with the Runtime AudioClip Loader that nothing ever
// destroyed, so memory grew with every preview. Now samples are decoded by our own code
// (SampleCache.DecodeAll, 2026-11-21): a deck keeps the samples, not a clip, and a
// preview's clip is freed once it has finished playing.

using System.Collections.Generic;
using UnityEngine;

public class ClipRelease : MonoBehaviour {
  class Pending { public AudioClip clip; public bool afterPlaying; public float since = -1; }
  static readonly List<Pending> pending = new List<Pending>();
  static ClipRelease instance;

  // not needed any more: free it now
  public static void Now(AudioClip c) {
    if (c != null) Destroy(c);
  }

  // free it soon (for a preview: once it has played through)
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
      if (p.since < 0) p.since = Time.unscaledTime;
      float wait = p.afterPlaying ? p.clip.length + 1f : 0f;
      if (Time.unscaledTime - p.since < wait) continue;
      pending.RemoveAt(i);
      Now(p.clip);
    }
  }
}
