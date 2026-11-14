// LOOP / ONCE switch for the drum-machine and CV sequencers (2026-11).
//
// Started from the control input (a trigger), a sequencer used to play one pass and
// stop; started with its PLAY button it loops. The switch decides for both: LOOP keeps
// looping, ONCE plays a single pass per trigger or PLAY press. New sequencers and older
// saves start on ONCE (the old external-trigger behaviour), so cabled patches still work.
//
// The switch is normally part of the prefab (added with the editor menu
// SoundStage > Add LOOP switch to sequencers, then positioned by hand). Prefabs that
// don't have one get a copy of the Gain device's switch at runtime.

using UnityEngine;

public static class SequencerLoopSwitch {
  public static Vector3 localPos = new Vector3(0.074f, 0.053f, 0.028f);
  // Gain / Valve switch orientation turned 90 degrees so LOOP is at the top.
  public static readonly Quaternion localRot = Quaternion.AngleAxis(90f, Vector3.forward) * new Quaternion(0.5f, -0.5f, -0.5f, 0.5f);
  public const float scale = 0.6f;

  public static basicSwitch Create(Transform device) {
    GameObject gain = Resources.Load("Prefabs/Gain") as GameObject;
    Transform src = gain != null ? gain.transform.Find("switch") : null;
    if (src == null) {
      Debug.LogWarning("[SequencerLoopSwitch] Gain prefab switch not found");
      return null;
    }
    GameObject g = Object.Instantiate(src.gameObject, device, false);
    g.name = "loopSwitch";
    g.transform.localPosition = localPos;
    g.transform.localRotation = localRot;
    g.transform.localScale = Vector3.one * scale;

    basicSwitch sw = g.GetComponent<basicSwitch>();
    Label(sw);
    return sw;
  }

  public static void Label(basicSwitch sw) {
    SetLabel(sw.onLabel, "LOOP");
    SetLabel(sw.offLabel, "ONCE");
  }

  static void SetLabel(Transform t, string s) {
    if (t == null) return;
    TextMesh tm = t.GetComponent<TextMesh>();
    if (tm != null) tm.text = s;
  }
}
