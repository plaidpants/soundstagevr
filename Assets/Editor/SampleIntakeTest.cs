// SoundStage > Multiplayer > Test sample intake... (milestone 4 testing)
//
// Runs a file of your choice through SampleCache.Accept exactly as a sample received from
// another player would be: copied into the quarantine folder, checked, decoded and
// written out as our own WAV in the cache. The Console shows what happened; good files
// end up in persistentDataPath/NetSamples named by their hash. Also offers a bad case:
// the same file announced under a wrong hash, which must be refused.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SampleIntakeTest {
  [MenuItem("SoundStage/Multiplayer/Test sample intake...")]
  static async void Run() {
    string src = EditorUtility.OpenFilePanel("Sample to receive", "", "wav,ogg,mp3,*");
    if (string.IsNullOrEmpty(src)) return;
    string hash;
    using (var sha = SHA256.Create())
    using (var s = File.OpenRead(src)) {
      var sb = new StringBuilder();
      foreach (byte b in sha.ComputeHash(s)) sb.Append(b.ToString("x2"));
      hash = sb.ToString();
    }
    long size = new FileInfo(src).Length;

    // a wrong hash must be refused
    string bad = SampleCache.NewIncomingPath();
    File.Copy(src, bad);
    string badResult = await SampleCache.Accept(bad, new string('0', 64), size);
    Debug.Log("[SampleIntakeTest] wrong hash: " + (badResult ?? "ACCEPTED (bug!)"));

    string tmp = SampleCache.NewIncomingPath();
    File.Copy(src, tmp);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string result = await SampleCache.Accept(tmp, hash, size);
    Debug.Log("[SampleIntakeTest] " + Path.GetFileName(src) + " (" + (size >> 10) + " KB): " + (result ?? "accepted") +
              " in " + sw.ElapsedMilliseconds + " ms -> " + (SampleCache.Find(hash) ?? "not stored") +
              "; quarantine left empty: " + (Directory.GetFiles(SampleCache.Incoming).Length == 0));
  }
}
