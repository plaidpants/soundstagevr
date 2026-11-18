// A Scale Sequencer's scale picked in multiplayer (2026-11-18).
//
// Picking a scale used to share the whole patch, which reloads every device on the other
// headsets: sequencers started from a shared button (control input) restarted out of
// step with the host, and nothing could line them up again (only sequencers following
// the master clock are lined up). Now just the scale goes out, through the host like
// everything else; LINKed sequencers follow on every headset as they do locally.

using Mirror;
using UnityEngine;

public struct NetScaleMsg : NetworkMessage { public int netId, scale; }

public static class NetScales {
  // a scale picked here (scaleSequencerDeviceInterface.PickScale)
  public static void Picked(scaleSequencerDeviceInterface seq, int scale) {
    if (!NetSync.Active) return;
    NetDevice dev = seq != null ? seq.GetComponentInParent<NetDevice>() : null;
    if (dev == null || dev.netId == 0) return;
    var m = new NetScaleMsg { netId = dev.netId, scale = scale };
    if (NetworkServer.active) Relay(m, null);
    else NetworkClient.Send(m);
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetScaleMsg>((conn, m) => { Apply(m); Relay(m, conn); });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetScaleMsg>(Apply);
  }

  static void Relay(NetScaleMsg m, NetworkConnectionToClient from) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m);
  }

  static void Apply(NetScaleMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    scaleSequencerDeviceInterface seq = dev.GetComponent<scaleSequencerDeviceInterface>();
    if (seq == null) return;
    seq.ApplyRemoteScale(m.scale);
  }
}
