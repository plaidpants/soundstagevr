// The camera device's screen in multiplayer (2026-11-25).
//
// Moving or resizing the camera's preview screen used to share the whole patch when it
// was let go: every other headset reloaded everything (sequencers and timelines restarted).
// Now only the screen's place, turn and size go out (the patch saves them too).

using Mirror;
using UnityEngine;

public struct NetCameraScreenMsg : NetworkMessage { public int netId; public Vector3 p; public Quaternion r; public Vector3 s; }

public static class NetCameraScreen {
  // let go here (NetControls)
  public static void Moved(manipObject obj) {
    if (!NetSync.Active) return;
    NetDevice dev = obj != null ? obj.GetComponentInParent<NetDevice>() : null;
    cameraDeviceInterface cam = dev != null ? dev.GetComponent<cameraDeviceInterface>() : null;
    if (dev == null || dev.netId == 0 || cam == null || cam.screenTrans == null) return;
    Transform t = cam.screenTrans;
    NetCameraScreenMsg m = new NetCameraScreenMsg { netId = dev.netId, p = t.localPosition, r = t.localRotation, s = t.localScale };
    if (NetworkServer.active) Relay(m, null);
    else if (NetworkClient.isConnected) NetworkClient.Send(m);
  }

  public static void RegisterServer() {
    NetworkServer.RegisterHandler<NetCameraScreenMsg>((conn, m) => { Apply(m); Relay(m, conn); });
  }

  public static void RegisterClient() {
    NetworkClient.RegisterHandler<NetCameraScreenMsg>(Apply);
  }

  static void Relay(NetCameraScreenMsg m, NetworkConnectionToClient from) {
    foreach (NetworkConnectionToClient c in NetworkServer.connections.Values)
      if (c != null && c != from && c != NetworkServer.localConnection) c.Send(m);
  }

  static void Apply(NetCameraScreenMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    cameraDeviceInterface cam = dev.GetComponent<cameraDeviceInterface>();
    if (cam == null || cam.screenTrans == null) return;
    cameraPanel panel = cam.screenTrans.GetComponent<cameraPanel>();
    if (panel != null && panel.curState == manipObject.manipState.grabbed) return; // being moved here right now
    cam.screenTrans.localPosition = m.p;
    cam.screenTrans.localRotation = m.r;
    cam.screenTrans.localScale = m.s;
    cam.updateResolution(m.s.x > 2);
    if (panel != null) panel.RefreshEdges();
  }
}
