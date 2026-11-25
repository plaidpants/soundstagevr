// Loopers and recorders for a player who joins late (2026-11-24).
//
// A loop (and a recorder's take) isn't part of the patch: each headset records its own
// copy, and only SAVE sent one to the others (NetLoopers / NetRecorders). So a player who
// joined after a loop was recorded got an empty looper - and its PLAY state, pressed or
// set by the looper itself before they joined, never reached them either.
//
// Now, once a joining headset has loaded the session's patch, it asks the host, and the
// host answers for each looper / recorder that holds something: its current audio (written
// to a hidden folder, LoopShare, and fetched like any sample through NetSamples - nothing
// shows up in the tape library), whether it's playing, and for a looper where in the loop
// it is (against the shared bar count, so it comes in on the right bar). The looper then
// stays lined up through the beat sync (INetBeatSync, like the step sequencers).

using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

public struct NetLoopRequestMsg : NetworkMessage { }
public struct NetLoopStateMsg : NetworkMessage { public int netId; public bool recorder, playing; public string file; public double anchor, pos, time; }

public static class NetLoopShare {
  static string Dir { get { return Path.Combine(Application.persistentDataPath, "LoopShare"); } }
  static bool cleaned;

  // joining headset, its patch just loaded (NetSync)
  public static void JoinedSession() {
    if (NetworkServer.active || !NetworkClient.isConnected) return;
    NetworkClient.Send(new NetLoopRequestMsg());
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetLoopRequestMsg>((conn, m) => SendStates(conn));
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetLoopStateMsg>(Apply);
  }

  // The audio is copied here, but written and hashed on a worker thread: on the Quest that
  // took over a second, and the host froze for it each time someone joined.
  struct Job { public NetLoopStateMsg msg; public float[] data; public int frames; }

  static void SendStates(NetworkConnectionToClient conn) {
    var jobs = new List<Job>();
    foreach (NetDevice d in NetDevice.all) {
      if (d == null || d.netId == 0) continue;
      waveTranscribeLooper lp = d.GetComponentInChildren<waveTranscribeLooper>(true);
      if (lp != null && (lp.playing || lp.HasContent())) {
        float[] buf = lp.ShareData(out int frames);
        jobs.Add(new Job { msg = new NetLoopStateMsg { netId = d.netId, recorder = false, playing = lp.playing && !lp.recording, anchor = lp.NetAnchor() },
                           data = Copy(buf, frames), frames = frames });
      }
      waveTranscribeRecorder rc = d.GetComponentInChildren<waveTranscribeRecorder>(true);
      if (rc != null && rc.HasContent()) {
        float[] buf = rc.ShareData(out int frames);
        // a recorder doesn't follow the bars: where it is (seconds) at what session time
        jobs.Add(new Job { msg = new NetLoopStateMsg { netId = d.netId, recorder = true, playing = rc.playing && !rc.recording, pos = rc.PlaySeconds, time = NetworkTime.time },
                           data = Copy(buf, frames), frames = frames });
      }
    }
    if (jobs.Count == 0) return;
    string dir = Dir;
    int rate = AudioSettings.outputSampleRate;
    var work = System.Threading.Tasks.Task.Run(() => {
      for (int i = 0; i < jobs.Count; i++) {
        Job j = jobs[i];
        j.msg.file = j.frames > 0 ? Write(dir, rate, j.msg.netId, j.data, j.frames) : "";
        jobs[i] = j;
      }
    });
    Runner.Get().StartCoroutine(SendWhenDone(conn, jobs, work));
  }

  static float[] Copy(float[] buf, int frames) {
    if (frames <= 0) return null;
    float[] c = new float[frames * 2];
    System.Array.Copy(buf, c, Mathf.Min(c.Length, buf.Length));
    return c;
  }

  static System.Collections.IEnumerator SendWhenDone(NetworkConnectionToClient conn, List<Job> jobs, System.Threading.Tasks.Task work) {
    while (!work.IsCompleted) yield return null;
    if (work.IsFaulted) Debug.LogWarning("[NetLoopShare] couldn't write the loops: " + work.Exception?.GetBaseException().Message);
    if (conn == null || !NetworkServer.active || !NetworkServer.connections.ContainsKey(conn.connectionId)) yield break;
    foreach (Job j in jobs) conn.Send(j.msg);
    Debug.Log("[NetLoopShare] a player joined: sent " + jobs.Count + " looper / recorder state(s)");
  }

  class Runner : MonoBehaviour {
    static Runner instance;
    public static Runner Get() {
      if (instance == null) {
        GameObject g = new GameObject("NetLoopShare");
        Object.DontDestroyOnLoad(g);
        instance = g.AddComponent<Runner>();
      }
      return instance;
    }
  }

  static void Apply(NetLoopStateMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice d) || d == null) return;
    if (m.recorder) {
      waveTranscribeRecorder rc = d.GetComponentInChildren<waveTranscribeRecorder>(true);
      if (rc != null) rc.ApplyJoinState(m.file, m.playing, m.pos, m.time);
    } else {
      waveTranscribeLooper lp = d.GetComponentInChildren<waveTranscribeLooper>(true);
      if (lp != null) lp.ApplyJoinState(m.file, m.playing, m.anchor);
    }
    Debug.Log("[NetLoopShare] " + d.name + ": took the session's " + (m.recorder ? "recording" : "loop") + (m.playing ? " (playing)" : "") + (string.IsNullOrEmpty(m.file) ? " (no audio)" : ""));
  }

  // 16-bit stereo WAV at this machine's rate, in the hidden folder; returns "path|sha=..."
  static string Write(string dir, int rate, int netId, float[] data, int frames) { // (worker thread: no Unity calls)
    try {
      Directory.CreateDirectory(dir);
      if (!cleaned) {
        cleaned = true; // old ones from earlier sessions
        foreach (string f in Directory.GetFiles(dir, "*.wav"))
          try { if (File.GetLastWriteTimeUtc(f) < System.DateTime.UtcNow.AddDays(-1)) File.Delete(f); } catch { }
      }
      string path = Path.Combine(dir, "loop_" + netId + "_" + System.DateTime.UtcNow.Ticks + ".wav");
      using (var fs = new FileStream(path, FileMode.Create))
      using (var w = new BinaryWriter(fs)) {
        int bytes = frames * 2 * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        for (int i = 0; i < frames * 2; i++) { float s = data[i] < -1f ? -1f : data[i] > 1f ? 1f : data[i]; w.Write((short)(s * 32760)); }
      }
      string h = SampleIndex.Hash(path); // indexed: NetSamples can send it by hash
      return h == "" ? "" : path + SampleIndex.Tag + h;
    } catch (System.Exception e) {
      UnityEngine.Debug.LogWarning("[NetLoopShare] couldn't write the loop: " + e.Message); // (Debug.Log is safe from any thread)
      return "";
    }
  }
}
