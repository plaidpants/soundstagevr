// Cables in multiplayer (2026-11, milestone 2).
//
// After a player here plugs, re-plugs or pulls a cable (omniPlug.Release), the cables
// in the scene are compared with the last known set and the differences are sent:
// "connect jack A to jack B" (with colour and cord shape) or "disconnect". Other
// headsets make or remove the same cable between the same jacks.
// A jack is named by its device's network ID and its index among the device's jacks.
// Cables that vanish because their device was deleted aren't sent (the delete does it).

using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct NetCableMsg : NetworkMessage {
  public bool connect;
  public int outDev, outJack, inDev, inJack; // the cord's output plug's jack, the other plug's jack
  public Color color;
  public Vector3[] path;
}

public static class NetCables {
  static readonly HashSet<string> known = new HashSet<string>();

  static void Rebaseline(int dev) {
    if (NetDevice.byId.TryGetValue(dev, out NetDevice d) && d != null) d.Rebaseline();
  }

  static string Key(int a, int ja, int b, int jb) { return a + ":" + ja + ">" + b + ":" + jb; }

  static bool JackId(omniJack j, out int dev, out int index) {
    dev = 0; index = -1;
    if (j == null) return false;
    NetDevice d = j.GetComponentInParent<NetDevice>();
    if (d == null || d.netId == 0) return false;
    dev = d.netId;
    index = System.Array.IndexOf(d.GetComponentsInChildren<omniJack>(true), j);
    return index >= 0;
  }

  static omniJack JackAt(int dev, int index) {
    if (!NetDevice.byId.TryGetValue(dev, out NetDevice d) || d == null) return null;
    omniJack[] jacks = d.GetComponentsInChildren<omniJack>(true);
    return index >= 0 && index < jacks.Length ? jacks[index] : null;
  }

  // every complete cable in the scene, by key
  static Dictionary<string, NetCableMsg> Scan() {
    var result = new Dictionary<string, NetCableMsg>();
    foreach (omniPlug p in Object.FindObjectsOfType<omniPlug>()) {
      if (!p.outputPlug || p.connected == null || p.otherPlug == null || p.otherPlug.connected == null) continue;
      if (p.curState == manipObject.manipState.grabbed || p.otherPlug.curState == manipObject.manipState.grabbed) continue;
      if (!JackId(p.connected, out int a, out int ja) || !JackId(p.otherPlug.connected, out int b, out int jb)) continue;
      PlugData data = p.GetData();
      result[Key(a, ja, b, jb)] = new NetCableMsg {
        connect = true, outDev = a, outJack = ja, inDev = b, inJack = jb, color = data.cordColor, path = data.plugPath
      };
    }
    return result;
  }

  // after a whole patch load: start over from what's in the scene
  public static void ForgetKnown() {
    known.Clear();
    foreach (string k in Scan().Keys) known.Add(k);
  }

  // ---------- local changes ----------

  public static void LocalCablesChanged() {
    if (!NetSync.Active || NetSession.instance == null) return;
    NetSession.instance.StartCoroutine(DiffNextFrame());
  }

  static IEnumerator DiffNextFrame() {
    yield return null; // a pulled cable's plugs are destroyed at the end of the frame
    if (!NetSync.Active) yield break;
    Dictionary<string, NetCableMsg> now = Scan();
    foreach (KeyValuePair<string, NetCableMsg> kv in now) {
      if (known.Contains(kv.Key)) continue;
      known.Add(kv.Key);
      Rebaseline(kv.Value.outDev); Rebaseline(kv.Value.inDev);
      NetSync.SendCable(kv.Value);
      Debug.Log("[NetCables] connected here: " + kv.Key);
    }
    List<string> gone = new List<string>();
    foreach (string k in known) if (!now.ContainsKey(k)) gone.Add(k);
    foreach (string k in gone) {
      known.Remove(k);
      string[] p = k.Split(':', '>');
      int a = int.Parse(p[0]), ja = int.Parse(p[1]), b = int.Parse(p[2]), jb = int.Parse(p[3]);
      if (!NetDevice.byId.ContainsKey(a) || !NetDevice.byId.ContainsKey(b)) continue; // device deleted
      Rebaseline(a); Rebaseline(b);
      NetSync.SendCable(new NetCableMsg { connect = false, outDev = a, outJack = ja, inDev = b, inJack = jb });
      Debug.Log("[NetCables] disconnected here: " + k);
    }
  }

  // ---------- from other players ----------

  public static void Apply(NetCableMsg m) {
    string k = Key(m.outDev, m.outJack, m.inDev, m.inJack);
    omniJack ja = JackAt(m.outDev, m.outJack), jb = JackAt(m.inDev, m.inJack);
    Rebaseline(m.outDev); Rebaseline(m.inDev);
    if (ja == null || jb == null) { Debug.Log("[NetCables] jacks not found for " + k); return; }

    if (m.connect) {
      if (ja.near != null || jb.near != null) { Debug.Log("[NetCables] jack busy for " + k); return; }
      Connect(ja, jb, m.color, m.path);
      known.Add(k);
    } else {
      foreach (omniPlug p in Object.FindObjectsOfType<omniPlug>()) {
        if (!p.outputPlug || p.connected != ja || p.otherPlug == null || p.otherPlug.connected != jb) continue;
        omniPlug other = p.otherPlug;
        p.connected.endConnection();
        other.connected.endConnection();
        Object.Destroy(other.gameObject);
        Object.Destroy(p.gameObject);
        break;
      }
      known.Remove(k);
    }
  }

  // like SaveLoadInterface.LoadPlugs for one cable
  static void Connect(omniJack outJack, omniJack inJack, Color color, Vector3[] path) {
    GameObject prefab = SaveLoadInterface.instance.plugPrefab;
    omniPlug outPlug = (Object.Instantiate(prefab, outJack.transform.position, outJack.transform.rotation) as GameObject).GetComponent<omniPlug>();
    omniPlug inPlug = (Object.Instantiate(prefab, inJack.transform.position, inJack.transform.rotation) as GameObject).GetComponent<omniPlug>();
    outPlug.outputPlug = true;
    inPlug.outputPlug = false;
    if (path == null || path.Length < 2) path = new[] { outJack.transform.position, inJack.transform.position };
    inPlug.Activate(outPlug, inJack, path, color);
    outPlug.Activate(inPlug, outJack, path, color);
  }
}
