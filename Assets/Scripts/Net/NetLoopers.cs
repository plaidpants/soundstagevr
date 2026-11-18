// A looper's SAVE in multiplayer (milestone 4, 2026-11).
//
// Every headset records its own copy of a loop (each computes the same patch), but noise
// generators, the microphone and timing make the copies differ slightly. SAVE settles it:
// only the player who pressed it writes a file (looperDeviceInterface skips presses that
// came from another player), this message tells everyone its name and hash, and every
// other headset's looper shows the same tape and - once the file has come over through
// NetSamples - replaces its loop with that recording. Through the host like everything else.

using Mirror;
using UnityEngine;

public struct NetLooperSaveMsg : NetworkMessage { public int netId; public string label, file; }

public static class NetLoopers {
  // our SAVE finished (waveTranscribeLooper.updateTape)
  public static void Saved(waveTranscribeLooper looper, string label, string file) {
    NetDevice dev = looper != null ? looper.GetComponentInParent<NetDevice>() : null;
    if (dev == null || dev.netId == 0 || string.IsNullOrEmpty(file)) return;
    var m = new NetLooperSaveMsg { netId = dev.netId, label = label, file = file };
    Debug.Log("[NetLoopers] saved " + label + " on " + dev.name + ": sending it to the other players");
    if (NetworkServer.active) Relay(m, null);
    else NetworkClient.Send(m);
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetLooperSaveMsg>((conn, m) => { Apply(m); Relay(m, conn); });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetLooperSaveMsg>(Apply);
  }

  static void Relay(NetLooperSaveMsg m, NetworkConnectionToClient from) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m);
  }

  static void Apply(NetLooperSaveMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    waveTranscribeLooper looper = dev.GetComponentInChildren<waveTranscribeLooper>(true);
    if (looper == null) return;
    Debug.Log("[NetLoopers] another player saved " + m.label + " on " + dev.name);
    looper.ApplyRemoteSave(m.label, m.file);
  }
}
