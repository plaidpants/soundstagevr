// Samples received from other players (milestone 4: sample transfer, step 2).
//
// They're kept in a hidden cache in the app's own storage (persistentDataPath/NetSamples),
// one file per sample named by the hash the scene knows it by, never in the user's
// library. SampleIndex.Resolve looks here when no local file has a sample's content.
//
// Received files are untrusted. Accept() checks a downloaded file in a quarantine folder
// before anything else touches it:
//   - the exact announced size, and the SHA-256 the scene asked for (a different file is
//     never accepted under someone else's hash);
//   - the format from its first bytes, not its name: WAV (PCM or float only - never the
//     compressed WAV kinds, which Windows would decode with native code), OGG, MP3;
//   - decoded with managed C# decoders only (our own WAV reader, NVorbis, NLayer), with
//     limits on channels, sample rate and length;
//   - then written out again as a fresh 16-bit PCM WAV of our own, which is what gets
//     loaded and played. The verified original bytes are kept next to it (<hash>.src)
//     only so they can be passed on to another player (whose check needs the original
//     hash); they're never decoded or played here.
// Nothing received is ever run or handed to the system to open, and file names come from
// our own hash, never from the sender.
//
// Storage: never below a free-space floor (3 GB on the Quest, above Meta's ~2 GB warning);
// the cache is capped (500 MB Quest / 2 GB PC), least recently used first; samples the
// current scene uses are never removed automatically; anything unused for 60 days goes at
// start-up; files are written under a temporary name and renamed when complete, so a full
// disk or a failure never leaves half a sample. Clear() empties it (settings button).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class SampleCache {
  public const long MaxReceiveBytes = 50L * 1024 * 1024;     // one sample as sent
  public const long MaxDecodedBytes = 100L * 1024 * 1024;    // ~9 min stereo 44.1 kHz 16-bit
  public const int MaxSeconds = 10 * 60;
  public const int UnusedDays = 60;

  public static long CapBytes { get { return Application.platform == RuntimePlatform.Android ? 500L << 20 : 2048L << 20; } }
  public static long FloorBytes { get { return Application.platform == RuntimePlatform.Android ? 3072L << 20 : 2048L << 20; } }

  public static event Action Changed;

  static string dir, incoming;
  static bool ready;

  public static string Dir { get { Init(); return dir; } }
  public static string Incoming { get { Init(); return incoming; } }

  // main thread, once (persistentDataPath)
  public static void Init() {
    if (ready) return;
    ready = true;
    dir = Path.Combine(Application.persistentDataPath, "NetSamples");
    incoming = Path.Combine(dir, "incoming");
    try {
      Directory.CreateDirectory(dir);
      Directory.CreateDirectory(incoming);
      Cleanup();
    } catch (Exception e) {
      Debug.LogWarning("[SampleCache] can't set up " + dir + ": " + e.Message);
    }
  }

  // ---- lookup ----

  static bool IsHash(string h) {
    if (h == null || h.Length != 64) return false;
    foreach (char c in h) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
    return true;
  }

  public static string PathFor(string hash) { return IsHash(hash) ? Path.Combine(Dir, hash + ".wav") : null; }
  static string SrcFor(string hash) { return IsHash(hash) ? Path.Combine(Dir, hash + ".src") : null; }

  // the verified original bytes of a received sample (for passing on only), or null
  public static string SourcePath(string hash) {
    string p = SrcFor(hash);
    return p != null && File.Exists(p) && File.Exists(PathFor(hash)) ? p : null;
  }

  // A RIFF/WAVE file? (A kept original that's a WAV already passed our intake check: PCM
  // or float only.)
  public static bool IsWav(string path) {
    try {
      byte[] h = new byte[12];
      using (var s = File.OpenRead(path)) if (s.Read(h, 0, 12) < 12) return false;
      return h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'A' && h[10] == 'V' && h[11] == 'E';
    } catch { return false; }
  }

  // one cached sample: its WAV plus its original
  static long SizeOf(string hash) {
    long n = 0;
    try { var w = new FileInfo(PathFor(hash)); if (w.Exists) n += w.Length; } catch { }
    try { var o = new FileInfo(SrcFor(hash)); if (o.Exists) n += o.Length; } catch { }
    return n;
  }

  static void DeleteSample(string hash) { TryDelete(PathFor(hash)); TryDelete(SrcFor(hash)); }

  // the cached file for a hash (marked as just used), or null
  public static string Find(string hash) {
    string p = PathFor(hash);
    if (p == null || !File.Exists(p)) return null;
    try { File.SetLastWriteTimeUtc(p, DateTime.UtcNow); } catch { }
    return p;
  }

  public static long TotalBytes() {
    long n = 0;
    try {
      foreach (string f in Directory.GetFiles(Dir, "*.wav")) n += new FileInfo(f).Length;
      foreach (string f in Directory.GetFiles(Dir, "*.src")) n += new FileInfo(f).Length;
    } catch { }
    return n;
  }

  // ---- space ----

  // free bytes where the cache lives (main thread: uses Android's StatFs)
  public static long FreeBytes() {
    try {
      if (Application.platform == RuntimePlatform.Android) {
        using (var stat = new AndroidJavaObject("android.os.StatFs", Dir)) return stat.Call<long>("getAvailableBytes");
      }
      return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Dir))).AvailableFreeSpace;
    } catch (Exception e) {
      Debug.LogWarning("[SampleCache] can't read free space: " + e.Message);
      return 0; // unknown: treat as full
    }
  }

  // hashes of samples in the current scene (decks and loose tapes): never evicted
  static HashSet<string> InUse() {
    var used = new HashSet<string>();
    foreach (samplerLoad s in UnityEngine.Object.FindObjectsOfType<samplerLoad>(true)) {
      s.getTapeInfo(out _, out string f);
      string h = SampleIndex.HashOf(f);
      if (h != "") used.Add(h);
    }
    foreach (tape t in UnityEngine.Object.FindObjectsOfType<tape>(true)) {
      string h = SampleIndex.HashOf(t.filename);
      if (h != "") used.Add(h);
    }
    return used;
  }

  // Makes room for 'need' more bytes: under the cap and above the free-space floor,
  // removing least recently used samples not in the scene. False if that isn't enough.
  public static bool MakeRoom(long need) {
    HashSet<string> keep = InUse();
    var files = new List<FileInfo>();
    try { foreach (string f in Directory.GetFiles(Dir, "*.wav")) files.Add(new FileInfo(f)); } catch { }
    files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
    long total = TotalBytes();
    long free = FreeBytes();
    int removed = 0;
    foreach (FileInfo f in files) {
      if (total + need <= CapBytes && free - need >= FloorBytes) break;
      string h = Path.GetFileNameWithoutExtension(f.Name);
      if (keep.Contains(h)) continue;
      long n = SizeOf(h);
      DeleteSample(h);
      total -= n; free += n; removed++;
    }
    if (removed > 0) { Debug.Log("[SampleCache] removed " + removed + " old shared sample(s) to make room"); Changed?.Invoke(); }
    bool ok = total + need <= CapBytes && free - need >= FloorBytes;
    if (!ok) Debug.LogWarning("[SampleCache] no room for " + (need >> 20) + " MB: cache " + (total >> 20) + " MB of " + (CapBytes >> 20) +
                              ", free " + (free >> 20) + " MB (keeping " + (FloorBytes >> 20) + " MB free)");
    return ok;
  }

  // start-up: leftovers from interrupted work, and samples unused for 60 days
  static void Cleanup() {
    foreach (string f in Directory.GetFiles(incoming)) TryDelete(f);
    foreach (string f in Directory.GetFiles(dir, "*.tmp")) TryDelete(f);
    int old = 0;
    DateTime cutoff = DateTime.UtcNow.AddDays(-UnusedDays);
    foreach (string f in Directory.GetFiles(dir, "*.wav"))
      if (File.GetLastWriteTimeUtc(f) < cutoff) { DeleteSample(Path.GetFileNameWithoutExtension(f)); old++; }
    foreach (string f in Directory.GetFiles(dir, "*.src")) // an original without its WAV
      if (!File.Exists(Path.ChangeExtension(f, ".wav"))) TryDelete(f);
    if (old > 0) Debug.Log("[SampleCache] removed " + old + " shared sample(s) unused for " + UnusedDays + " days");
  }

  public static void Clear() {
    int n = 0;
    try {
      foreach (string f in Directory.GetFiles(Dir, "*.wav")) { TryDelete(f); n++; }
      foreach (string f in Directory.GetFiles(Dir, "*.src")) TryDelete(f);
      foreach (string f in Directory.GetFiles(Dir, "*.tmp")) TryDelete(f);
    } catch { }
    Debug.Log("[SampleCache] cleared " + n + " shared sample(s)");
    Changed?.Invoke();
  }

  static void TryDelete(string f) { try { File.Delete(f); } catch { } }

  // ---- intake ----

  // A new file in the quarantine folder to receive into (the caller writes it, then calls
  // Accept with it).
  public static string NewIncomingPath() {
    return Path.Combine(Incoming, Guid.NewGuid().ToString("N") + ".part");
  }

  // Checks a received file and, if it's good, stores our own WAV of it under 'hash'.
  // Returns null on success or a short reason. The received file is always deleted.
  public static async Task<string> Accept(string received, string hash, long size) {
    string final = PathFor(hash);
    bool keptOriginal = false;
    try {
      if (final == null) return "bad hash";
      if (File.Exists(final) && File.Exists(SrcFor(hash))) return null; // got it already
      if (size <= 0 || size > MaxReceiveBytes) return "too big";

      // checks and an estimate of the decoded size, off the main thread
      Probe p = await Task.Run(() => Check(received, hash, size));
      if (p.error != null) return p.error;

      if (!MakeRoom(p.estimate + size)) return "NO SPACE"; // our WAV + the original

      string tmp = final + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
      string err = await Task.Run(() => Decode(received, p.kind, tmp));
      if (err != null) { TryDelete(tmp); return err; }
      if (File.Exists(final)) TryDelete(tmp); // arrived twice
      else File.Move(tmp, final);
      // keep the verified original, for passing on only
      if (!File.Exists(SrcFor(hash))) { File.Move(received, SrcFor(hash)); keptOriginal = true; }
      Debug.Log("[SampleCache] stored " + hash.Substring(0, 12) + " (" + p.kind + ", " + (new FileInfo(final).Length >> 10) + " KB)");
      Changed?.Invoke();
      return null;
    } catch (Exception e) {
      Debug.LogWarning("[SampleCache] intake failed: " + e.Message);
      return e is IOException ? "NO SPACE" : "failed";
    } finally {
      if (!keptOriginal) TryDelete(received);
    }
  }

  struct Probe { public string error, kind; public long estimate; }

  static Probe Check(string path, string hash, long size) {
    var fi = new FileInfo(path);
    if (!fi.Exists || fi.Length != size) return new Probe { error = "wrong size" };
    using (var sha = SHA256.Create())
    using (var s = File.OpenRead(path)) {
      byte[] d = sha.ComputeHash(s);
      var sb = new StringBuilder(64);
      foreach (byte b in d) sb.Append(b.ToString("x2"));
      if (sb.ToString() != hash) return new Probe { error = "content doesn't match" };
    }
    byte[] head = new byte[12];
    using (var s = File.OpenRead(path)) s.Read(head, 0, head.Length);
    string kind =
      head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E' ? "wav" :
      head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S' ? "ogg" :
      (head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) ? "mp3" : null;
    if (kind == null) return new Probe { error = "not an audio file" };
    long est = kind == "wav" ? size * 2 : size * 12; // worst cases: 8-bit WAV doubles, compressed ~12x
    return new Probe { kind = kind, estimate = Math.Min(est, MaxDecodedBytes) + 64 };
  }

  // Decodes 'path' and writes a 16-bit PCM WAV to 'outPath'. Null or a reason.
  static string Decode(string path, string kind, string outPath) {
    try {
      using (var input = File.OpenRead(path)) {
        ISource src = kind == "wav" ? WavSource.Open(input) :
                      kind == "ogg" ? (ISource)new OggSource(input) : new Mp3Source(input);
        if (src == null) return "unsupported WAV";
        using (src) {
          if (src.Channels < 1 || src.Channels > 2) return "unsupported channel count";
          if (src.SampleRate < 8000 || src.SampleRate > 192000) return "unsupported sample rate";
          long maxFrames = Math.Min((long)src.SampleRate * MaxSeconds, MaxDecodedBytes / (2 * src.Channels));
          using (var output = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write))
            return WritePcm16(src, output, maxFrames);
        }
      }
    } catch (Exception e) {
      return "can't decode (" + e.GetType().Name + ")";
    }
  }

  static string WritePcm16(ISource src, Stream output, long maxFrames) {
    int ch = src.Channels;
    var w = new BinaryWriter(output);
    // header with sizes filled in at the end
    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(0); w.Write(Encoding.ASCII.GetBytes("WAVE"));
    w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)ch);
    w.Write(src.SampleRate); w.Write(src.SampleRate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(0);
    float[] buf = new float[4096 * ch];
    byte[] outBuf = new byte[buf.Length * 2];
    long frames = 0;
    while (true) {
      int n = src.Read(buf, 0, buf.Length);
      if (n <= 0) break;
      n -= n % ch;
      if (frames + n / ch > maxFrames) return "too long";
      for (int i = 0; i < n; i++) {
        float v = buf[i];
        if (float.IsNaN(v)) v = 0;
        int s = (int)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767f);
        outBuf[i * 2] = (byte)s;
        outBuf[i * 2 + 1] = (byte)(s >> 8);
      }
      w.Write(outBuf, 0, n * 2);
      frames += n / ch;
    }
    if (frames == 0) return "no audio";
    long data = frames * ch * 2;
    w.Flush();
    output.Seek(4, SeekOrigin.Begin); w.Write((int)(36 + data));
    output.Seek(40, SeekOrigin.Begin); w.Write((int)data);
    w.Flush();
    return null;
  }

  // A whole audio file decoded to interleaved floats - what the sampler and the other
  // tape players play (samplerLoad, 2026-11-21). WAV (PCM / float), OGG or MP3, told
  // apart by the first bytes; mono or stereo. Null if it isn't one of those. Any thread.
  public static float[] DecodeAll(string path, out int channels, out int rate) {
    channels = 0; rate = 0;
    try {
      byte[] head = new byte[12];
      using (var s = File.OpenRead(path)) if (s.Read(head, 0, head.Length) < 4) return null;
      string kind =
        head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E' ? "wav" :
        head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S' ? "ogg" :
        (head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0) ? "mp3" : null;
      if (kind == null) return null;
      using (var input = File.OpenRead(path)) {
        ISource src = kind == "wav" ? WavSource.Open(input) :
                      kind == "ogg" ? (ISource)new OggSource(input) : new Mp3Source(input);
        if (src == null) return null; // a WAV kind we don't read (compressed: ADPCM etc.)
        using (src) {
          if (src.Channels < 1 || src.Channels > 2 || src.SampleRate < 8000 || src.SampleRate > 192000) return null;
          channels = src.Channels;
          rate = src.SampleRate;
          long guess = kind == "wav" ? input.Length / 2 : input.Length * 12; // samples, roughly
          float[] all = new float[(int)Math.Max(65536, Math.Min(guess, 32L << 20))];
          int n = 0;
          while (true) {
            if (all.Length - n < 16384) {
              if (all.Length >= (1 << 29)) break; // ~45 minutes of stereo at 96 kHz: enough
              Array.Resize(ref all, Math.Min(all.Length * 2, 1 << 29));
            }
            int got = src.Read(all, n, Math.Min(65536, all.Length - n)); // in pieces: the WAV reader allocates a byte buffer of the size asked for
            if (got <= 0) break;
            n += got;
          }
          n -= n % channels;
          if (n == 0) return null;
          if (n != all.Length) Array.Resize(ref all, n);
          for (int i = 0; i < n; i++) if (float.IsNaN(all[i])) all[i] = 0;
          return all;
        }
      }
    } catch (Exception e) {
      Debug.LogWarning("[SampleCache] can't decode " + Path.GetFileName(path) + " (" + e.GetType().Name + ")");
      return null;
    }
  }

  // All the samples of a plain PCM / float WAV (ours, or a looper recording), interleaved;
  // null if it isn't one. (The looper copies another player's saved recording into its
  // loop with this.)
  public static float[] ReadWav(string path, out int channels) {
    channels = 0;
    try {
      using (var s = File.OpenRead(path)) {
        WavSource w = WavSource.Open(s);
        if (w == null) return null;
        channels = w.Channels;
        var all = new List<float>((int)Math.Min(s.Length / 2, 64L << 20));
        float[] buf = new float[8192];
        int n;
        while ((n = w.Read(buf, 0, buf.Length)) > 0)
          for (int i = 0; i < n; i++) all.Add(buf[i]);
        return all.ToArray();
      }
    } catch (Exception e) {
      Debug.LogWarning("[SampleCache] can't read " + path + ": " + e.Message);
      return null;
    }
  }

  // ---- decoders (managed only) ----

  interface ISource : IDisposable {
    int Channels { get; }
    int SampleRate { get; }
    int Read(float[] buf, int offset, int count); // interleaved samples, 0 at the end
  }

  // Our own RIFF/WAVE reader: PCM 8/16/24/32-bit and 32-bit float (also as
  // WAVE_FORMAT_EXTENSIBLE). Every size is checked against the file.
  class WavSource : ISource {
    Stream s;
    int bits;
    bool isFloat;
    long remaining;
    byte[] raw = new byte[0];
    public int Channels { get; private set; }
    public int SampleRate { get; private set; }

    public static WavSource Open(Stream s) {
      var r = new BinaryReader(s);
      long len = s.Length;
      s.Seek(12, SeekOrigin.Begin);
      int fmtTag = -1, ch = 0, rate = 0, bits = 0;
      while (s.Position + 8 <= len) {
        string id = Encoding.ASCII.GetString(r.ReadBytes(4));
        long size = r.ReadUInt32();
        long start = s.Position;
        if (size > len - start) return null; // chunk runs past the end
        if (id == "fmt ") {
          if (size < 16) return null;
          fmtTag = r.ReadUInt16(); ch = r.ReadUInt16(); rate = r.ReadInt32();
          r.ReadInt32(); r.ReadUInt16(); bits = r.ReadUInt16();
          if (fmtTag == 0xFFFE && size >= 40) { // extensible: the real format is in the subformat GUID
            r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32();
            fmtTag = r.ReadUInt16();
          }
        } else if (id == "data") {
          if (fmtTag != 1 && fmtTag != 3) return null; // PCM or float only
          if (fmtTag == 1 && bits != 8 && bits != 16 && bits != 24 && bits != 32) return null;
          if (fmtTag == 3 && bits != 32) return null;
          if (ch < 1 || ch > 2) return null;
          return new WavSource { s = s, bits = bits, isFloat = fmtTag == 3, remaining = size - size % (ch * (bits / 8)), Channels = ch, SampleRate = rate };
        }
        s.Seek(start + size + (size & 1), SeekOrigin.Begin);
      }
      return null;
    }

    public int Read(float[] buf, int offset, int count) {
      int bps = bits / 8;
      long want = Math.Min(remaining, (long)count * bps);
      if (want <= 0) return 0;
      if (raw.Length < want) raw = new byte[want];
      int got = 0;
      while (got < want) {
        int n = s.Read(raw, got, (int)want - got);
        if (n <= 0) break;
        got += n;
      }
      got -= got % bps;
      remaining -= got;
      int samples = got / bps;
      for (int i = 0; i < samples; i++) {
        int o = i * bps;
        float v;
        if (isFloat) v = BitConverter.ToSingle(raw, o);
        else if (bits == 8) v = (raw[o] - 128) / 128f;
        else if (bits == 16) v = (short)(raw[o] | raw[o + 1] << 8) / 32768f;
        else if (bits == 24) v = ((raw[o] << 8 | raw[o + 1] << 16 | raw[o + 2] << 24) >> 8) / 8388608f;
        else v = BitConverter.ToInt32(raw, o) / 2147483648f;
        buf[offset + i] = v;
      }
      return samples;
    }

    public void Dispose() { }
  }

  // NVorbis and NLayer: MIT-licensed sources in Assets/Plugins (2026-11-21; they used to
  // come with the Runtime AudioClip Loader Asset Store package, which isn't used any more)
  class OggSource : ISource {
    readonly NVorbis.VorbisReader r;
    public OggSource(Stream s) { r = new NVorbis.VorbisReader(s, false); }
    public int Channels { get { return r.Channels; } }
    public int SampleRate { get { return r.SampleRate; } }
    public int Read(float[] buf, int offset, int count) { return r.ReadSamples(buf, offset, count); }
    public void Dispose() { r.Dispose(); }
  }

  class Mp3Source : ISource {
    readonly NLayer.MpegFile r;
    public Mp3Source(Stream s) { r = new NLayer.MpegFile(s); }
    public int Channels { get { return r.Channels; } }
    public int SampleRate { get { return r.SampleRate; } }
    public int Read(float[] buf, int offset, int count) { return r.ReadSamples(buf, offset, count); }
    public void Dispose() { r.Dispose(); }
  }
}
