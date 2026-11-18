// Content hashes for samples (milestone 4: sample transfer, step 1).
//
// A sample is identified by the SHA-256 of its file, not by its name or folder: the same
// sound can have different names on different machines (renamed, imported twice), and two
// different sounds can share a name. Tapes carry the hash inside their file string,
//     DOC\Samples\Recordings\loop.wav|sha=<64 hex>
// so every device's save format (label + file) keeps it without changes, saved scenes
// remember it, and scenes shared between headsets stay identical. Each machine turns a
// hash back into whatever local file has that content (Resolve), so the path in the
// string is only a hint.
//
// Hashes of local files are cached by path, size and modification time
// (persistentDataPath/sampleindex.txt), so each file is read once; the library is hashed
// once in the background at start-up, so picking a tape never waits for it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

public class SampleIndex : MonoBehaviour {
  public const string Tag = "|sha=";

  struct Entry { public long size, mtime; public string hash; }

  static readonly object gate = new object();
  static readonly Dictionary<string, Entry> byPath = new Dictionary<string, Entry>();
  static readonly Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
  static bool loaded, dirty;
  static string indexFile;
  static SampleIndex instance;
  float saveTimer;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("SampleIndex");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<SampleIndex>();
    lock (gate) {
      indexFile = Path.Combine(Application.persistentDataPath, "sampleindex.txt"); // main thread only
      Load();
    }
    SampleCache.Init();
  }

  // ---- strings ----

  // "path|sha=hash" -> path, hash ("" when there's none)
  public static void Split(string f, out string path, out string hash) {
    hash = "";
    path = f ?? "";
    int i = path.IndexOf(Tag, StringComparison.Ordinal);
    if (i < 0) return;
    hash = path.Substring(i + Tag.Length);
    path = path.Substring(0, i);
  }

  public static string PathOf(string f) { Split(f, out string p, out _); return p; }
  public static string HashOf(string f) { Split(f, out _, out string h); return h; }

  // Adds the hash to a sample string if it has none and the file is here. With wait =
  // false (tapes being made - a Tapes shelf makes dozens at once) only an already known
  // hash is used; an unknown one is worked out in the background and the string stays as
  // it is for now. With wait = true (a tape's sample being saved or shared) it's worked
  // out on the spot if need be - one file, and usually already known.
  public static string WithHash(string f, bool wait = true) {
    if (string.IsNullOrEmpty(f)) return f;
    Split(f, out string path, out string hash);
    if (hash != "") return f;
    string full = sampleManager.instance != null ? sampleManager.instance.ParsePath(path) : path;
    string h = wait ? Hash(full) : Known(full);
    if (h == "" && !File.Exists(full)) h = BuiltinSamples.HashForPath(path); // a built-in this library lacks
    else if (h == "" && !wait) HashLater(full);
    return h == "" ? f : path + Tag + h;
  }

  // the hash if it's already known and the file hasn't changed, otherwise ""
  public static string Known(string full) {
    try {
      var fi = new FileInfo(full);
      if (!fi.Exists) return "";
      lock (gate) {
        Load();
        if (byPath.TryGetValue(full, out Entry e) && e.size == fi.Length && e.mtime == fi.LastWriteTimeUtc.Ticks) return e.hash;
      }
    } catch { }
    return "";
  }

  // one background worker for hashes nobody is waiting for
  static readonly Queue<string> later = new Queue<string>();
  static bool laterRunning;
  static void HashLater(string full) {
    lock (gate) {
      if (later.Contains(full)) return;
      later.Enqueue(full);
      if (laterRunning) return;
      laterRunning = true;
    }
    var t = new Thread(() => {
      while (true) {
        string next;
        lock (gate) {
          if (later.Count == 0) { laterRunning = false; return; }
          next = later.Dequeue();
        }
        Hash(next);
      }
    });
    t.IsBackground = true;
    t.Priority = System.Threading.ThreadPriority.BelowNormal;
    t.Start();
  }

  // Same sound? By hash when both have one, otherwise by path.
  public static bool SameSample(string a, string b) {
    Split(a, out string pa, out string ha);
    Split(b, out string pb, out string hb);
    if (ha != "" && hb != "") return ha == hb;
    return pa == pb;
  }

  // Network (2026-11-18): every sample path in a patch going out carries its hash. A
  // device can still hold the plain path it was loaded with (a drum machine row not built
  // yet when an example was shared): another machine without that file got no tape at all
  // - it can only fetch a sample it knows the hash of.
  static readonly Regex plainSample = new Regex(@">((?:APP[\\/]|DOC[\\/]|[A-Za-z]:[\\/]|/)[^<>|]+?\.(?:wav|ogg|mp3))<", RegexOptions.IgnoreCase);
  public static string AddHashes(string xml) {
    return plainSample.Replace(xml, m => ">" + WithHash(m.Groups[1].Value) + "<");
  }

  // For comparing scenes between machines: a sample string compares by its hash alone.
  static readonly Regex tagged = new Regex(@"[^<>""|]*\|sha=([0-9a-f]{64})");
  public static string Normalize(string xml) {
    return xml.IndexOf(Tag, StringComparison.Ordinal) < 0 ? xml : tagged.Replace(xml, "|sha=$1");
  }

  // The local file for a sample string: the path if it's there and holds that content,
  // otherwise any local file with the same hash (renamed, moved, another folder), otherwise
  // a copy received from another player (SampleCache). If none is found the path itself
  // comes back (and doesn't exist, or has other content); samplerLoad then asks the
  // other players for it (NetSamples).
  public static string Resolve(string full, string hash) {
    if (hash == "") return full;
    if (Hash(full) == hash) return full;
    lock (gate) {
      if (byHash.TryGetValue(hash, out List<string> paths))
        foreach (string p in paths.ToArray())
          if (p != full && Hash(p) == hash) return p;
    }
    string kept = SessionSamples.Alias(hash); // a received OGG/MP3 kept by a scene save
    if (kept != null) return kept;
    string cached = SampleCache.Find(hash);
    return cached ?? full;
  }

  // A file here whose bytes are exactly this sample, to send to another player: a local
  // file with that hash, or the original of one received earlier (SampleCache). Null if
  // this machine doesn't have it. Our cached WAVs don't count (different bytes).
  public static string LocalSource(string hash) {
    if (string.IsNullOrEmpty(hash)) return null;
    string[] paths = null;
    lock (gate) { if (byHash.TryGetValue(hash, out List<string> l)) paths = l.ToArray(); }
    string cacheDir = SampleCache.Dir;
    if (paths != null)
      foreach (string p in paths)
        if (!p.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase) && Hash(p) == hash) return p;
    return SampleCache.SourcePath(hash);
  }

  // ---- hashing ----

  // SHA-256 of a local file (lower-case hex), "" if it isn't there. Cached by path, size
  // and modification time.
  public static string Hash(string full) {
    if (string.IsNullOrEmpty(full)) return "";
    FileInfo fi;
    try {
      fi = new FileInfo(full);
      if (!fi.Exists) return "";
    } catch { return ""; }
    long size = fi.Length, mtime = fi.LastWriteTimeUtc.Ticks;
    lock (gate) {
      Load();
      if (byPath.TryGetValue(full, out Entry e) && e.size == size && e.mtime == mtime) return e.hash;
    }
    string h;
    try {
      using (var sha = SHA256.Create())
      using (var s = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 65536)) {
        byte[] d = sha.ComputeHash(s);
        var sb = new StringBuilder(64);
        foreach (byte b in d) sb.Append(b.ToString("x2"));
        h = sb.ToString();
      }
    } catch (Exception ex) {
      Debug.LogWarning("[SampleIndex] can't read " + full + ": " + ex.Message);
      return "";
    }
    lock (gate) {
      if (byPath.TryGetValue(full, out Entry old) && old.hash != h) Unlink(old.hash, full);
      byPath[full] = new Entry { size = size, mtime = mtime, hash = h };
      Link(h, full);
      dirty = true;
    }
    return h;
  }

  static void Link(string h, string p) {
    if (!byHash.TryGetValue(h, out List<string> l)) byHash[h] = l = new List<string>();
    if (!l.Contains(p)) l.Add(p);
  }

  static void Unlink(string h, string p) {
    if (byHash.TryGetValue(h, out List<string> l)) l.Remove(p);
  }

  // ---- index file ----

  static void Load() {
    if (loaded || indexFile == null) return;
    loaded = true;
    try {
      // a crash between removing the old index and renaming the new one leaves only the
      // .tmp (complete, it's renamed only after being written): use it
      string file = indexFile;
      if (!File.Exists(file) && File.Exists(indexFile + ".tmp")) file = indexFile + ".tmp";
      if (!File.Exists(file)) return;
      foreach (string line in File.ReadAllLines(file)) {
        string[] p = line.Split('\t');
        if (p.Length != 4 || p[0].Length != 64) continue;
        if (!long.TryParse(p[1], out long size) || !long.TryParse(p[2], out long mtime)) continue;
        byPath[p[3]] = new Entry { hash = p[0], size = size, mtime = mtime };
        Link(p[0], p[3]);
      }
      Debug.Log("[SampleIndex] " + byPath.Count + " sample hashes known");
    } catch (Exception e) {
      Debug.LogWarning("[SampleIndex] couldn't read the index: " + e.Message);
    }
  }

  static void Save() {
    string text;
    lock (gate) {
      if (!dirty || indexFile == null) return;
      dirty = false;
      var sb = new StringBuilder();
      foreach (KeyValuePair<string, Entry> kv in byPath)
        sb.Append(kv.Value.hash).Append('\t').Append(kv.Value.size).Append('\t').Append(kv.Value.mtime).Append('\t').Append(kv.Key).Append('\n');
      text = sb.ToString();
    }
    try {
      string tmp = indexFile + ".tmp";
      File.WriteAllText(tmp, text);
      if (File.Exists(indexFile)) File.Delete(indexFile);
      File.Move(tmp, indexFile);
    } catch (Exception e) {
      Debug.LogWarning("[SampleIndex] couldn't save the index: " + e.Message);
    }
  }

  // ---- start-up: hash the library in the background ----

  System.Collections.IEnumerator Start() {
    while (sampleManager.instance == null || sampleManager.instance.sampleDictionary == null) yield return null;
    var files = new List<string>();
    foreach (var lib in sampleManager.instance.sampleDictionary.Values)
      foreach (string f in lib.Values) files.Add(sampleManager.instance.ParsePath(f));
    Debug.Log("[SampleIndex] checking " + files.Count + " library samples in the background");
    var t = new Thread(() => {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      int n = 0;
      foreach (string f in files) { if (Hash(f) != "") n++; }
      Debug.Log("[SampleIndex] library checked: " + n + " samples in " + sw.ElapsedMilliseconds + " ms");
    });
    t.IsBackground = true;
    t.Priority = System.Threading.ThreadPriority.BelowNormal; // never compete with the audio
    t.Start();
  }

  void Update() {
    saveTimer += Time.unscaledDeltaTime;
    if (saveTimer < 5f) return;
    saveTimer = 0;
    if (dirty) Save();
  }

  void OnApplicationPause(bool paused) { if (paused) Save(); }
  void OnApplicationQuit() { Save(); }
}
