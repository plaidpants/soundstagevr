// Hand-held parts of devices in multiplayer (2026-11): drumsticks, maracas, the
// microphone grip. While a player holds one, the others see it in that player's hand;
// when it's let go it glides back to its resting place on the device, as it does for
// the holder. Visual only for now: the sound these make on other headsets comes with
// the control sync (milestone 2).
//
// A held part is named by its device's network ID plus its index in the device's tool
// list (NetDevice.tools: same prefab, same order on every headset).

using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetToolMsg : NetworkMessage { public int netId; public int index; public bool held; public Vector3 pos; public Quaternion rot; }

public class NetTools : MonoBehaviour {
  static NetTools instance;

  public static bool IsTool(manipObject m) {
    return m is drumstick || m is maracaUI || m is microphoneGrip;
  }

  // ---------- local: the NetPlayer reports what each hand holds ----------

  static readonly manipObject[] lastHeld = new manipObject[2];

  public static void LocalHands(manipulator left, manipulator right) {
    Report(0, left);
    Report(1, right);
  }

  static void Report(int hand, manipulator m) {
    manipObject held = m != null ? m.getSelection() : null;
    if (held != null && !IsTool(held)) held = null;

    if (lastHeld[hand] != null && lastHeld[hand] != held) Send(lastHeld[hand], false); // let go
    if (held != null) Send(held, true);
    lastHeld[hand] = held;
  }

  static void Send(manipObject tool, bool held) {
    if (tool == null) return;
    if (!NetDevice.toolOwner.TryGetValue(tool, out NetDevice dev) || dev == null || dev.netId == 0 || dev.tools == null) return;
    int index = dev.tools.IndexOf(tool);
    if (index < 0) return;
    NetToolMsg msg = new NetToolMsg { netId = dev.netId, index = index, held = held, pos = tool.transform.position, rot = tool.transform.rotation };
    NetSync.SendTool(msg);
  }

  // ---------- remote: show it ----------

  class Puppet {
    public Transform t;
    public Vector3 restPos; public Quaternion restRot; // local, on the device
    public Vector3 pos; public Quaternion rot;          // world, while held
    public bool held;
    public manipObject tool;
  }

  readonly Dictionary<long, Puppet> puppets = new Dictionary<long, Puppet>();

  public static void Apply(NetToolMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    if (instance == null) {
      GameObject g = new GameObject("NetTools");
      DontDestroyOnLoad(g);
      instance = g.AddComponent<NetTools>();
    }
    long key = ((long)m.netId << 16) | (uint)m.index;
    if (!instance.puppets.TryGetValue(key, out Puppet p) || p.t == null) {
      if (dev.tools == null || m.index >= dev.tools.Count || dev.tools[m.index] == null) return;
      manipObject tool = dev.tools[m.index];
      if (tool.curState == manipObject.manipState.grabbed) return; // we hold it ourselves
      Transform t = tool.transform;
      p = new Puppet { t = t, restPos = t.localPosition, restRot = t.localRotation, tool = tool };
      NetSync.SetGrabBlocked(new[] { tool }, true); // can't be taken out of their hand
      instance.puppets[key] = p;
    }
    p.held = m.held;
    p.pos = m.pos;
    p.rot = m.rot;
    if (m.held && !p.t.gameObject.activeInHierarchy) p.t.gameObject.SetActive(true);
  }

  void LateUpdate() {
    float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
    List<long> done = null;
    foreach (KeyValuePair<long, Puppet> kv in puppets) {
      Puppet p = kv.Value;
      if (p.t == null) { (done ??= new List<long>()).Add(kv.Key); continue; }
      if (!NetSync.Active) p.held = false; // session ended: put it back
      if (p.held) {
        p.t.position = Vector3.Lerp(p.t.position, p.pos, k);
        p.t.rotation = Quaternion.Slerp(p.t.rotation, p.rot, k);
      } else {
        p.t.localPosition = Vector3.Lerp(p.t.localPosition, p.restPos, k);
        p.t.localRotation = Quaternion.Slerp(p.t.localRotation, p.restRot, k);
        if ((p.t.localPosition - p.restPos).sqrMagnitude < 1e-8f) {
          p.t.localPosition = p.restPos;
          p.t.localRotation = p.restRot;
          NetSync.SetGrabBlocked(new[] { p.tool }, false);
          (done ??= new List<long>()).Add(kv.Key);
        }
      }
    }
    if (done != null) foreach (long key in done) puppets.Remove(key);
  }
}
