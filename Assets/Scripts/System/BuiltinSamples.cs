// SoundStage's built-in samples (Samples.tgz in StreamingAssets) - milestone 4, step 4
// (2026-11-18).
//
// - App updates: the library is unpacked once (sampleManager.Init); after that, a new
//   build only adds built-ins it hasn't installed before (Samples/.installed lists them).
//   Files the user deleted stay deleted, files they replaced aren't overwritten.
// - A scene (or another player's patch) can use a built-in this machine doesn't have
//   (deleted, or an older library): it's restored from the bundled archive into the hidden
//   received-samples cache (SampleCache) - offline, no traffic - before anyone is asked.
// - A plain built-in path ("APP/909 Drums/909 Kick 2.wav", as examples and older scenes
//   store them) whose file is missing gets its hash from the archive, so the tape still
//   appears and the sample can be restored or fetched.
// The archive's index (path, hash, size of every sample) is worked out in the background
// once per build and kept in builtin_index.txt.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using UnityEngine;
using UnityEngine.Networking;

public class BuiltinSamples : MonoBehaviour {
  struct Entry { public string path, hash; public long size; }

  static readonly Dictionary<string, Entry> byPath = new Dictionary<string, Entry>(); // lower-case relative path
  static readonly Dictionary<string, Entry> byHash = new Dictionary<string, Entry>();
  static volatile bool ready;
  static BuiltinSamples instance;

  static string ArchivePath { get { return Path.Combine(Application.streamingAssetsPath, "Samples.tgz"); } }
  static string IndexFile { get { return Path.Combine(Application.persistentDataPath, "builtin_index.txt"); } }
  public static string BuildStamp { get { return Application.version + " " + Application.buildGUID; } }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (instance != null) return;
    GameObject g = new GameObject("BuiltinSamples");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<BuiltinSamples>();
  }

  // ---- index ----

  // the app's own library folder ("APP"), where the archive is unpacked (Quest and the
  // Windows build; the editor uses the project's)
  public static string LibraryDir {
    get {
#if (UNITY_ANDROID || UNITY_STANDALONE_WIN) && !UNITY_EDITOR // (the editor set to Android has UNITY_ANDROID too: it used to unpack into AppData)
      return Path.Combine(Directory.GetParent(Application.persistentDataPath).FullName, "Samples");
#else
      return null;
#endif
    }
  }

  // A new build: index the archive and add new built-ins to the library, in the
  // background (one read of the archive for both).
  IEnumerator Start() {
    string indexFile = IndexFile, stamp = BuildStamp, lib = LibraryDir;
    Task<bool> load = Task.Run(() => LoadIndex(indexFile, stamp));
    while (!load.IsCompleted) yield return null;
    bool indexOk = load.Exception == null && load.Result;
    if (indexOk) ready = true;
    bool libStale = lib != null && Directory.Exists(lib) && !LibraryCurrent(lib);
    if (indexOk && !libStale) yield break;

    byte[] tgz = null;
    yield return ReadArchive(b => tgz = b);
    if (tgz == null) yield break;
    float t0 = Time.realtimeSinceStartup;
    Task<int> work = Task.Run(() => {
      int n = indexOk ? -1 : BuildIndex(tgz, indexFile, stamp);
      if (libStale) UpdateLibrary(lib, false, tgz, stamp);
      return n;
    });
    while (!work.IsCompleted) yield return null;
    tgz = null;
    if (work.Exception != null) { Debug.LogWarning("[BuiltinSamples] couldn't read the built-in samples: " + work.Exception.GetBaseException().Message); yield break; }
    ready = true;
    if (work.Result >= 0) Debug.Log("[BuiltinSamples] indexed " + work.Result + " built-in samples in " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s");
  }

  static bool LibraryCurrent(string lib) {
    try {
      string stampFile = Path.Combine(lib, ".build");
      return File.Exists(stampFile) && File.ReadAllText(stampFile) == BuildStamp;
    } catch { return false; }
  }

  // the archive's bytes (inside the APK on the Quest), without blocking the frame
  static IEnumerator ReadArchive(Action<byte[]> done) {
    if (Application.platform != RuntimePlatform.Android) {
      Task<byte[]> r = Task.Run(() => File.Exists(ArchivePath) ? File.ReadAllBytes(ArchivePath) : null);
      while (!r.IsCompleted) yield return null;
      done(r.Exception == null ? r.Result : null);
      yield break;
    }
    using (UnityWebRequest req = UnityWebRequest.Get(ArchivePath)) {
      yield return req.SendWebRequest();
      if (req.result != UnityWebRequest.Result.Success) {
        Debug.LogWarning("[BuiltinSamples] couldn't read Samples.tgz: " + req.error);
        done(null);
      } else done(req.downloadHandler.data);
    }
  }

  static string Clean(string name) {
    name = name.Replace('\\', '/');
    while (name.StartsWith("./")) name = name.Substring(2);
    return name;
  }

  static bool IsSample(string name) {
    string e = Path.GetExtension(name).ToLowerInvariant();
    return e == ".wav" || e == ".ogg" || e == ".mp3";
  }

  static bool LoadIndex(string file, string stamp) {
    try {
      if (!File.Exists(file)) return false;
      string[] lines = File.ReadAllLines(file);
      if (lines.Length == 0 || lines[0] != stamp) return false;
      lock (byPath) {
        for (int i = 1; i < lines.Length; i++) {
          string[] p = lines[i].Split('\t');
          if (p.Length != 3 || p[0].Length != 64 || !long.TryParse(p[1], out long size)) continue;
          Add(new Entry { hash = p[0], size = size, path = p[2] });
        }
      }
      return true;
    } catch { return false; }
  }

  static void Add(Entry e) {
    byPath[e.path.ToLowerInvariant()] = e;
    byHash[e.hash] = e;
  }

  static int BuildIndex(byte[] tgz, string file, string stamp) {
    var sb = new StringBuilder(stamp).Append('\n');
    int n = 0;
    using (var ms = new MemoryStream(tgz))
    using (var gz = new GZipInputStream(ms))
    using (var tar = new TarInputStream(gz)) {
      TarEntry te;
      byte[] buf = new byte[65536];
      while ((te = tar.GetNextEntry()) != null) {
        if (te.IsDirectory || !IsSample(te.Name)) continue;
        using (var sha = SHA256.Create()) {
          long left = te.Size;
          while (left > 0) {
            int r = tar.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (r <= 0) break;
            sha.TransformBlock(buf, 0, r, null, 0);
            left -= r;
          }
          sha.TransformFinalBlock(buf, 0, 0);
          var h = new StringBuilder(64);
          foreach (byte b in sha.Hash) h.Append(b.ToString("x2"));
          var e = new Entry { hash = h.ToString(), size = te.Size, path = Clean(te.Name) };
          lock (byPath) Add(e);
          sb.Append(e.hash).Append('\t').Append(e.size).Append('\t').Append(e.path).Append('\n');
          n++;
        }
      }
    }
    try {
      File.WriteAllText(file + ".tmp", sb.ToString());
      if (File.Exists(file)) File.Delete(file);
      File.Move(file + ".tmp", file);
    } catch (Exception ex) { Debug.LogWarning("[BuiltinSamples] couldn't save the index: " + ex.Message); }
    return n;
  }

  // ---- lookups ----

  public static bool Has(string hash) {
    if (!ready || string.IsNullOrEmpty(hash)) return false;
    lock (byPath) return byHash.ContainsKey(hash);
  }

  // "APP/909 Drums/909 Kick 2.wav" (either separator) -> its hash in the archive, or ""
  public static string HashForPath(string samplePath) {
    if (!ready || string.IsNullOrEmpty(samplePath)) return "";
    string p = samplePath.Replace('\\', '/');
    if (!p.StartsWith("APP/", StringComparison.OrdinalIgnoreCase)) return "";
    p = p.Substring(4).ToLowerInvariant();
    lock (byPath) return byPath.TryGetValue(p, out Entry e) ? e.hash : "";
  }

  // ---- restoring into the cache ----

  static readonly Dictionary<string, TaskCompletionSource<bool>> wanted = new Dictionary<string, TaskCompletionSource<bool>>();
  static bool passRunning;

  // Puts a built-in sample into the received-samples cache from the bundled archive.
  // True when it's there. Several asked for together are restored in one pass.
  public static Task<bool> Restore(string hash) {
    if (!Has(hash) || instance == null) return Task.FromResult(false);
    if (!wanted.TryGetValue(hash, out TaskCompletionSource<bool> tcs)) wanted[hash] = tcs = new TaskCompletionSource<bool>();
    if (!passRunning) { passRunning = true; instance.StartCoroutine(instance.RestorePass()); }
    return tcs.Task;
  }

  IEnumerator RestorePass() {
    yield return null; // gather everything asked for this frame
    byte[] tgz = null;
    yield return ReadArchive(b => tgz = b);
    var batch = new Dictionary<string, TaskCompletionSource<bool>>(wanted);
    wanted.Clear();
    var targets = new Dictionary<string, Entry>();
    lock (byPath) foreach (string h in batch.Keys) if (byHash.TryGetValue(h, out Entry e)) targets[e.path.ToLowerInvariant()] = e;
    Task<Dictionary<string, string>> extract = tgz == null ? Task.FromResult(new Dictionary<string, string>()) : Task.Run(() => Extract(tgz, targets));
    while (!extract.IsCompleted) yield return null;
    tgz = null;
    Dictionary<string, string> files = extract.Exception == null ? extract.Result : new Dictionary<string, string>();
    foreach (var kv in batch) {
      bool ok = false;
      if (files.TryGetValue(kv.Key, out string received)) {
        Entry e;
        lock (byPath) e = byHash[kv.Key];
        Task<string> accept = SampleCache.Accept(received, kv.Key, e.size); // checked and stored like any received sample
        while (!accept.IsCompleted) yield return null;
        ok = accept.Exception == null && accept.Result == null;
        Debug.Log("[BuiltinSamples] " + (ok ? "restored " : "couldn't restore ") + e.path + " from the app's built-in samples" + (ok ? "" : ": " + (accept.Exception != null ? accept.Exception.GetBaseException().Message : accept.Result)));
      }
      kv.Value.TrySetResult(ok);
    }
    passRunning = false;
    if (wanted.Count > 0) { passRunning = true; StartCoroutine(RestorePass()); }
  }

  // hash -> a file in the cache's quarantine folder with that entry's bytes
  static Dictionary<string, string> Extract(byte[] tgz, Dictionary<string, Entry> targets) {
    var result = new Dictionary<string, string>();
    using (var ms = new MemoryStream(tgz))
    using (var gz = new GZipInputStream(ms))
    using (var tar = new TarInputStream(gz)) {
      TarEntry te;
      while ((te = tar.GetNextEntry()) != null && result.Count < targets.Count) {
        if (te.IsDirectory || !targets.TryGetValue(Clean(te.Name).ToLowerInvariant(), out Entry e)) continue;
        string path = SampleCache.NewIncomingPath();
        using (var f = File.Create(path)) tar.CopyEntryContents(f);
        result[e.hash] = path;
      }
    }
    return result;
  }

  // ---- app updates (sampleManager.Init) ----

  // Unpacks the built-ins this install hasn't had yet into the library folder. A fresh
  // library (sampleManager.Init, on the main thread) gets everything; after that, when the
  // build changes (Samples/.build, from Start in the background), only entries new in this
  // build (Samples/.installed lists what was ever installed), never over a file that's
  // there or one the user deleted. New ones show in the library from the next start.
  // (stamp: BuildStamp, worked out on the main thread - Application.version can't be read
  // from the background, which left the stamp unwritten and the library "stale" forever)
  public static void UpdateLibrary(string samplesDir, bool fresh, byte[] tgz = null, string stamp = null) {
    if (stamp == null) stamp = BuildStamp;
    string stampFile = Path.Combine(samplesDir, ".build");
    string listFile = Path.Combine(samplesDir, ".installed");
    if (tgz == null) tgz = StreamingAssetsLoader.ReadAllBytes(ArchivePath);
    if (tgz == null) return;
    var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    bool haveList = File.Exists(listFile);
    if (haveList) foreach (string l in File.ReadAllLines(listFile)) if (l.Length > 0) installed.Add(l);
    int added = 0;
    try {
      using (var ms = new MemoryStream(tgz))
      using (var gz = new GZipInputStream(ms))
      using (var tar = new TarInputStream(gz)) {
        TarEntry te;
        while ((te = tar.GetNextEntry()) != null) {
          string name = Clean(te.Name);
          if (te.IsDirectory || name.Length == 0 || name.Contains("..")) continue;
          string dest = Path.Combine(samplesDir, name.Replace('/', Path.DirectorySeparatorChar));
          bool exists = File.Exists(dest);
          // an install from before this list existed: what's missing now was deleted
          bool isNew = fresh || (haveList && !installed.Contains(name));
          if (isNew && !exists) {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (var f = File.Create(dest)) tar.CopyEntryContents(f);
            added++;
          }
          installed.Add(name);
        }
      }
      File.WriteAllLines(listFile, new List<string>(installed));
      File.WriteAllText(stampFile, stamp);
      Debug.Log("[BuiltinSamples] library " + (fresh ? "unpacked: " : "updated: ") + added + " built-in sample file(s) added");
    } catch (Exception e) {
      Debug.LogWarning("[BuiltinSamples] couldn't update the library: " + e.Message);
    }
  }
}
