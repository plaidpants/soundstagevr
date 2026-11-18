// Samples from multiplayer sessions in the tape library (milestone 4, step 5; 2026-11-18).
//
// The library's "Sessions" category lists:
//   - samples received from other players in this run (they live in the hidden cache,
//     SampleCache), under their original names, so they can be put on tapes again;
//   - samples kept for good: saving a scene copies every received sample it uses out of
//     the cache into Samples/Sessions ("<name>_<first 6 of hash>.wav") and the saved
//     scene points there. The cache can then be cleared or evicted without breaking it.
//
// A kept sample is the verified original when that was a WAV (same bytes, same hash, so
// it's found like any library sample and can be passed on). A received OGG or MP3 is kept
// as our own decoded PCM WAV - the untrusted original is never handed to the normal sample
// loader - and an alias (samplealiases.txt) maps the scene's hash to that file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class SessionSamples {
  public const string Category = "Sessions";

  public static string Folder {
    get { return masterControl.instance.SaveDir + Path.DirectorySeparatorChar + "Samples" + Path.DirectorySeparatorChar + Category; }
  }

  // ---- library ----

  // A tape's sample was found in the cache (samplerLoad): list it under its own name.
  public static void Noticed(string sample) {
    if (sampleManager.instance == null || string.IsNullOrEmpty(sample)) return;
    string hash = SampleIndex.HashOf(sample);
    if (hash == "") return;
    Add(NameOf(sample), SampleIndex.PathOf(sample) + SampleIndex.Tag + hash);
  }

  static void Add(string label, string sample) {
    Dictionary<string, string> list = Ensure();
    string hash = SampleIndex.HashOf(sample);
    foreach (var kv in list) if (SampleIndex.HashOf(kv.Value) == hash || kv.Value == sample) return; // listed already
    string l = label;
    for (int n = 2; list.ContainsKey(l); n++) l = label + " " + n;
    list[l] = sample;
    Refresh();
  }

  static Dictionary<string, string> Ensure() {
    var d = sampleManager.instance.sampleDictionary;
    if (!d.TryGetValue(Category, out Dictionary<string, string> list)) d[Category] = list = new Dictionary<string, string>();
    return list;
  }

  static void Refresh() {
    foreach (libraryDeviceInterface lib in UnityEngine.Object.FindObjectsOfType<libraryDeviceInterface>())
      if (lib.curPrimary == Category) lib.updateSecondaryPanels(Category);
  }

  // "DOC\Custom\My Loop.wav|sha=..." -> "My Loop" (either separator: the path may come
  // from another machine)
  static string NameOf(string sample) {
    string p = SampleIndex.PathOf(sample).Replace('\\', '/');
    string n = Path.GetFileNameWithoutExtension(p.Substring(p.LastIndexOf('/') + 1));
    return n == "" ? "sample" : n;
  }

  // ---- saving a scene ----

  static readonly Regex tagged = new Regex(@"[^<>""|]*\|sha=([0-9a-f]{64})");

  // After a scene is written: received samples it uses are copied out of the cache, and
  // the file is rewritten to point at the copies. Leaves the file as it is if nothing
  // needs keeping, or if a copy can't be made (it still works while the cache has them).
  public static void KeepForScene(string sceneFile) {
    try {
      string xml = File.ReadAllText(sceneFile, Encoding.UTF8);
      if (xml.IndexOf(SampleIndex.Tag, StringComparison.Ordinal) < 0) return;
      var replaced = new Dictionary<string, string>();
      int kept = 0;
      string cacheDir = SampleCache.Dir;
      string result = tagged.Replace(xml, m => {
        string sample = m.Value;
        if (replaced.TryGetValue(sample, out string done)) return done;
        string hash = m.Groups[1].Value;
        string full = sampleManager.instance.parseFilename(sample);
        string now = sample;
        if (full.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) {
          string copy = Keep(NameOf(sample), hash, full);
          if (copy != null) { now = copy; kept++; }
        }
        replaced[sample] = now;
        return now;
      });
      if (kept == 0) return;
      string tmp = sceneFile + ".tmp";
      File.WriteAllText(tmp, result, new UTF8Encoding(false));
      File.Delete(sceneFile);
      File.Move(tmp, sceneFile);
      Debug.Log("[SessionSamples] kept " + kept + " received sample(s) in Samples/" + Category + " for " + Path.GetFileName(sceneFile));
    } catch (Exception e) {
      Debug.LogWarning("[SessionSamples] couldn't keep the scene's received samples: " + e.Message);
    }
  }

  // Copies one cached sample into Samples/Sessions; the new sample string, or null.
  static string Keep(string name, string hash, string cached) {
    string safe = Sanitize(name);
    string fileName = safe + "_" + hash.Substring(0, 6) + ".wav";
    string dest = Path.Combine(Folder, fileName);
    string sample = "DOC" + Path.DirectorySeparatorChar + Category + Path.DirectorySeparatorChar + fileName + SampleIndex.Tag + hash;
    Directory.CreateDirectory(Folder);

    string src = SampleCache.SourcePath(hash);
    bool original = src != null && SampleCache.IsWav(src); // same bytes as the sender's: same hash
    string from = original ? src : cached;

    if (File.Exists(dest)) {
      // kept before (or a file of that name): fine if it's this sample
      if (original ? SampleIndex.Hash(dest) == hash : Alias(hash) == dest) { Add(safe + "_" + hash.Substring(0, 6), sample); return sample; }
      Debug.LogWarning("[SessionSamples] " + fileName + " is already there with other content: not replaced");
      return null;
    }
    long size = new FileInfo(from).Length;
    if (SampleCache.FreeBytes() - size < SampleCache.FloorBytes) {
      Debug.LogWarning("[SessionSamples] not enough free space to keep " + fileName + " (" + (size >> 20) + " MB)");
      return null;
    }
    string tmp = dest + ".tmp";
    File.Copy(from, tmp, true);
    File.Move(tmp, dest);
    if (original) SampleIndex.Hash(dest);
    else SetAlias(hash, dest);
    Add(Path.GetFileNameWithoutExtension(fileName), sample);
    return sample;
  }

  static string Sanitize(string name) {
    var sb = new StringBuilder();
    foreach (char c in name) {
      if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')') sb.Append(c);
      else sb.Append('_');
    }
    string s = sb.ToString().Trim().Trim('.');
    if (s.Length > 60) s = s.Substring(0, 60);
    return s == "" ? "sample" : s;
  }

  // ---- aliases: a scene's hash -> our decoded copy of a received OGG/MP3 ----

  static Dictionary<string, string> aliases;
  static string AliasFile { get { return Path.Combine(Application.persistentDataPath, "samplealiases.txt"); } }

  static void LoadAliases() {
    if (aliases != null) return;
    aliases = new Dictionary<string, string>();
    try {
      if (!File.Exists(AliasFile)) return;
      foreach (string line in File.ReadAllLines(AliasFile)) {
        string[] p = line.Split('\t');
        if (p.Length == 2 && p[0].Length == 64) aliases[p[0]] = p[1];
      }
    } catch (Exception e) { Debug.LogWarning("[SessionSamples] couldn't read the aliases: " + e.Message); }
  }

  // the kept copy for a hash, if there is one and it's still there; else null
  public static string Alias(string hash) {
    if (string.IsNullOrEmpty(hash)) return null;
    lock (typeof(SessionSamples)) {
      LoadAliases();
      return aliases.TryGetValue(hash, out string p) && File.Exists(p) ? p : null;
    }
  }

  static void SetAlias(string hash, string path) {
    lock (typeof(SessionSamples)) {
      LoadAliases();
      aliases[hash] = path;
      try {
        var sb = new StringBuilder();
        foreach (var kv in aliases) sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        string tmp = AliasFile + ".tmp";
        File.WriteAllText(tmp, sb.ToString());
        if (File.Exists(AliasFile)) File.Delete(AliasFile);
        File.Move(tmp, AliasFile);
      } catch (Exception e) { Debug.LogWarning("[SessionSamples] couldn't save the aliases: " + e.Message); }
    }
  }
}
