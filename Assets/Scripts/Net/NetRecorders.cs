// A recorder's SAVE in multiplayer (2026-11-22), like the looper's (NetLoopers).
//
// Every headset records its own copy, but a recorder fed by a microphone only hears it on
// the headset holding the mic (MicrophoneSignalGenerator), so the other copies recorded
// silence and nothing showed up for the other players. Now only the player who pressed
// SAVE writes a file (recorderDeviceInterface skips presses that came from another
// player), this message tells everyone its name and hash, and every other headset's
// recorder - once the file has come over through NetSamples - takes that recording.
// Through the host like everything else.

using Mirror;
using UnityEngine;

public struct NetRecorderSaveMsg : NetworkMessage { public int netId; public string label, file; }

public static class NetRecorders {
  // our SAVE finished (waveTranscribeRecorder.updateTape)
  public static void Saved(waveTranscribeRecorder rec, string label, string file) {
    NetDevice dev = rec != null ? rec.GetComponentInParent<NetDevice>() : null;
    if (dev == null || dev.netId == 0 || string.IsNullOrEmpty(file)) return;
    var m = new NetRecorderSaveMsg { netId = dev.netId, label = label, file = file };
    Debug.Log("[NetRecorders] saved " + label + " on " + dev.name + ": sending it to the other players");
    if (NetworkServer.active) Relay(m, null);
    else NetworkClient.Send(m);
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetRecorderSaveMsg>((conn, m) => { Apply(m); Relay(m, conn); });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetRecorderSaveMsg>(Apply);
  }

  static void Relay(NetRecorderSaveMsg m, NetworkConnectionToClient from) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m);
  }

  static void Apply(NetRecorderSaveMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    waveTranscribeRecorder rec = dev.GetComponentInChildren<waveTranscribeRecorder>(true);
    if (rec == null) return;
    Debug.Log("[NetRecorders] another player saved " + m.label + " on " + dev.name);
    rec.ApplyRemoteSave(m.label, m.file);
  }
}
