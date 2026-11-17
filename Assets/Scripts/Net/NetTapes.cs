// Tapes put into / taken out of a sampler deck, for multiplayer (2026-11).
//
// A tape dropped into a deck (a drum pad, sampler, looper ...) by a player here is
// sent as the device ID, which of the device's decks, and the tape's label and file;
// every other headset loads the same sample into the same deck (samplerLoad.SetSample).
// Pulling a tape out sends the same with no file. Only what players do is sent: the
// hooks are in tape's own drop / pull-out paths, not in samplerLoad, which loading a
// patch or applying one of these also goes through.
// Samples have to exist on every headset (built-in ones do; your own recordings and
// custom samples don't yet - sample transfer is a later milestone).

using Mirror;
using UnityEngine;

public struct NetTapeMsg : NetworkMessage { public int netId; public int deck; public string label, file; }

public static class NetTapes {
  static bool Find(samplerLoad s, out NetDevice dev, out int index) {
    dev = s != null ? s.GetComponentInParent<NetDevice>() : null;
    index = -1;
    if (dev == null || dev.netId == 0) return false;
    samplerLoad[] all = dev.GetComponentsInChildren<samplerLoad>(true);
    index = System.Array.IndexOf(all, s);
    return index >= 0;
  }

  public static void Loaded(samplerLoad s, tape t) {
    if (t != null) Loaded(s, t.label, t.filename);
  }

  // (also the test harness, which puts tapes in with samplerLoad.SetSample)
  public static void Loaded(samplerLoad s, string label, string file) {
    if (!NetSync.Active || !Find(s, out NetDevice dev, out int i)) return;
    Debug.Log("[NetTapes] tape " + label + " into " + dev.name + " deck " + i);
    NetSync.SendTape(new NetTapeMsg { netId = dev.netId, deck = i, label = label, file = file });
  }

  public static void Ejected(samplerLoad s) {
    if (!NetSync.Active || !Find(s, out NetDevice dev, out int i)) return;
    NetSync.SendTape(new NetTapeMsg { netId = dev.netId, deck = i, label = "", file = "" });
  }

  public static void Apply(NetTapeMsg m) {
    if (!NetDevice.byId.TryGetValue(m.netId, out NetDevice dev) || dev == null) return;
    samplerLoad[] all = dev.GetComponentsInChildren<samplerLoad>(true);
    if (m.deck < 0 || m.deck >= all.Length) return;
    samplerLoad s = all[m.deck];
    if (string.IsNullOrEmpty(m.label)) { s.ForceEject(); return; }
    s.getTapeInfo(out string label, out string file);
    if (file == m.file) return; // already in
    s.ForceEject();
    s.SetSample(m.label, m.file);
    dev.Rebaseline();
  }
}
