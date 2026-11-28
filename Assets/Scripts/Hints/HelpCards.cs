// Copyright 2026 contributors
// Licensed under the Apache License, Version 2.0
//
// Help cards (2026-11-27): devices with no help video (Gain, Glide, Scope) get the same
// "?" as the others, but touching it opens a short written card instead of a video. The
// "?" is a copy of the Gate's (Valve) tooltip, in the same place under the housing, so it
// shows and hides with the help setting and only the nearest device of each type shows
// one (tooltips.cs). The card looks like the hand help card (WristHelp) and sits where
// the Gate's video plays, fixed to the device like the videos.

using System.Collections.Generic;
using UnityEngine;

public static class HelpCards {
  public static readonly Dictionary<string, string> texts = new Dictionary<string, string> {
    { "gain",
      "GAIN\n" +
      "Turns a signal up, to about\n" +
      "    30 dB louder (x33)\n" +
      "Dial:  how much\n" +
      "Switch:  on / mute (fades)\n" +
      "Too loud clips at full scale\n" +
      "Touch ? again to close" },
    { "glide",
      "GLIDE\n" +
      "Smooths a control signal:\n" +
      "    notes slide into each other\n" +
      "Dial:  glide time\n" +
      "Nothing plugged in:  a steady\n" +
      "    value, set by the dial\n" +
      "Touch ? again to close" },
    { "cvseq",
      "CV SEQUENCER\n" +
      "A drum machine whose steps\n" +
      "    also send a value\n" +
      "Step:  on / off;  its dial:  value\n" +
      "Each row:  trigger out, CV out\n" +
      "    (CV into pitch, filter...)\n" +
      "PLAY, beat, swing, corner\n" +
      "    handle:  as the drum machine\n" +
      "Touch ? again to close" },
    { "scaleseq",
      "SCALE SEQUENCER\n" +
      "Each row is a note of a scale,\n" +
      "    highest at the top\n" +
      "Tape at the left:  the sound\n" +
      "Scale name:  pick one of 34\n" +
      "LINK:  share the scale with\n" +
      "    the other linked ones\n" +
      "JOIN:  cells side by side play\n" +
      "    as one long note\n" +
      "Handles:  more steps and rows\n" +
      "Touch ? again to close" },
    { "scope",
      "SCOPE\n" +
      "Shows what a signal looks like\n" +
      "IN:  the signal;  THRU:  passes it on\n" +
      "TRIG:  lock to another signal\n" +
      "TIME:  screen width (left: AUTO)\n" +
      "LEVEL:  where the picture locks on\n" +
      "GAIN:  zoom;  POS:  slide in time\n" +
      "AUTO / NORMAL:  NORMAL holds the\n" +
      "    last locked picture\n" +
      "RISE / FALL:  which edge locks\n" +
      "Touch ? again to close" },
  };

  const float pad = .006f, line = .0012f, charSize = .0013f;
  static readonly Color textColor = new Color(.85f, .92f, 1f);
  static readonly Color lineColor = Color.HSVToRGB(.6f, .9f, 1f);

  // add a "?" with a card to a device that has no help video
  // replaceVideo: use the card instead of a video the device borrows from another one
  public static void Attach(Component device, string key, bool replaceVideo = false) {
    if (device == null || !texts.ContainsKey(key)) return;
    tooltips own = device.GetComponentInChildren<tooltips>(true);
    if (own != null) {
      // a "?" of its own with no video (the Scope keeps the oscillator's), or one showing
      // another device's video: a card there, where its video would play
      if (!string.IsNullOrEmpty(own.vidFile) && !replaceVideo) return;
      own.vidFile = "card:" + key;
      own.helpText = texts[key];
      own.cardDevice = device.transform;
      return;
    }
    GameObject prefab = Resources.Load<GameObject>("Prefabs/Valve");
    tooltips src = prefab != null ? prefab.GetComponentInChildren<tooltips>(true) : null;
    if (src == null) return;

    Transform dev = device.transform;
    Bounds b = HousingBounds(dev);
    GameObject q = Object.Instantiate(src.gameObject, dev, false);
    q.name = "tooltip";
    tooltips t = q.GetComponent<tooltips>();
    t.vidFile = "card:" + key; // (one "?" per type: tooltips keys on this)
    t.vidContainer = null;
    t.helpText = texts[key];
    t.cardDevice = dev;
    q.transform.localPosition = TipPosition(b);
  }

  // where the Gate has its "?" and its video, from its housing (roundedCube): centre
  // 0.0012 up, 0.0403 half-height; "?" at (0.0154, -0.0495, 0.0136), video container at
  // (-0.1176, -0.1117, 0.0136) turned 180 degrees
  static Vector3 TipPosition(Bounds b) { return new Vector3(b.center.x + .0154f, b.min.y - .0104f, b.center.z + .0136f); }
  static Vector3 CardPosition(Bounds b) { return TipPosition(b) + new Vector3(-.133f, -.0622f, 0); }

  // the housing (the "roundedCube" the handles are on), else every renderer
  public static Bounds HousingBounds(Transform dev) {
    Transform h = dev.Find("roundedCube");
    Renderer r = h != null ? h.GetComponent<Renderer>() : null;
    if (r == null) return LocalBounds(dev);
    return ToLocal(dev, r.bounds);
  }

  static Bounds ToLocal(Transform dev, Bounds w) {
    Bounds lb = new Bounds(dev.InverseTransformPoint(w.min), Vector3.zero);
    for (int i = 0; i < 8; i++)
      lb.Encapsulate(dev.InverseTransformPoint(new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z)));
    return lb;
  }

  // the device's size in its own space, from its renderers
  public static Bounds LocalBounds(Transform dev) {
    Bounds lb = new Bounds(Vector3.zero, Vector3.zero);
    bool have = false;
    foreach (Renderer r in dev.GetComponentsInChildren<Renderer>()) {
      if (r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
      Bounds w = r.bounds;
      for (int i = 0; i < 8; i++) {
        Vector3 c = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
        Vector3 p = dev.InverseTransformPoint(c);
        if (have) lb.Encapsulate(p); else { lb = new Bounds(p, Vector3.zero); have = true; }
      }
    }
    return lb;
  }

  // the card: text on a dark panel in a glowing frame like the video player's, where the
  // device's video would play. Touch it: the frame brightens; press it: a bright flash
  // and it closes (like pausing a video).
  public static GameObject MakeCard(Transform dev, string text, Transform videoSpot = null, tooltips tip = null) {
    Bounds b = HousingBounds(dev);
    GameObject card = new GameObject("helpCard");
    card.transform.SetParent(dev, false);
    card.SetActive(false); // (finished below, then shown)

    GameObject tg = new GameObject("text");
    tg.transform.SetParent(card.transform, false);
    tg.transform.localPosition = new Vector3(0, 0, -.002f);
    TextMesh tm = tg.AddComponent<TextMesh>();
    Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    tm.font = f;
    MeshRenderer tr = tg.GetComponent<MeshRenderer>();
    tr.sharedMaterial = f.material;
    tm.fontSize = 64;
    tm.characterSize = charSize;
    tm.anchor = TextAnchor.MiddleCenter;
    tm.alignment = TextAlignment.Left;
    tm.text = text;
    tm.color = textColor;

    // the text's own size (in the card's space, whatever the device's rotation; it used to
    // be measured in world space, so a turned device got a box the wrong size)
    Bounds lb = tr.localBounds;
    Vector2 size = new Vector2(lb.size.x, lb.size.y);
    if (size.x < .02f || size.y < .02f) { // not laid out yet: estimate from the lines
      string[] lines = text.Split('\n');
      int longest = 0; foreach (string l in lines) longest = Mathf.Max(longest, l.Length);
      size = new Vector2(longest * charSize * 3.6f, lines.Length * charSize * 7.2f);
    }
    tg.transform.localPosition = new Vector3(-lb.center.x, -lb.center.y, -.002f);
    float w = size.x + 2 * pad, h = size.y + 2 * pad;

    Shader sh = Shader.Find("Sprites/Default");
    if (sh == null) sh = Shader.Find("Unlit/Transparent");
    Material back = new Material(sh) { color = new Color(0, 0, 0, .75f) };
    Quad("back", card.transform, Vector3.zero, new Vector3(w, h, 1), back);

    // the frame: the video player's own glowing line material, else plain quads
    Material frameSrc = null;
    if (tip != null && tip.vidPlayerPrefab != null) {
      VideoPlayerUI ui = tip.vidPlayerPrefab.GetComponentInChildren<VideoPlayerUI>(true);
      if (ui != null && ui.frameQuad != null) { Renderer fr = ui.frameQuad.GetComponent<Renderer>(); if (fr != null) frameSrc = fr.sharedMaterial; }
    }
    Material frame;
    if (frameSrc != null) {
      frame = new Material(frameSrc);
      GameObject fg = new GameObject("frame");
      fg.transform.SetParent(card.transform, false);
      fg.transform.localPosition = new Vector3(0, 0, -.001f);
      Mesh m = new Mesh();
      m.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) };
      m.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
      fg.AddComponent<MeshFilter>().sharedMesh = m;
      MeshRenderer mr = fg.AddComponent<MeshRenderer>();
      mr.sharedMaterial = frame;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    } else {
      frame = new Material(sh) { color = lineColor };
      Vector3 front = new Vector3(0, 0, -.001f);
      Quad("t", card.transform, front + new Vector3(0, (h - line) / 2, 0), new Vector3(w, line, 1), frame);
      Quad("b", card.transform, front + new Vector3(0, -(h - line) / 2, 0), new Vector3(w, line, 1), frame);
      Quad("l", card.transform, front + new Vector3(-(w - line) / 2, 0, 0), new Vector3(line, h, 1), frame);
      Quad("r", card.transform, front + new Vector3((w - line) / 2, 0, 0), new Vector3(line, h, 1), frame);
    }

    // touchable, like the video
    BoxCollider bc = card.AddComponent<BoxCollider>();
    bc.size = new Vector3(w, h, .01f);
    Panel panel = card.AddComponent<Panel>();
    panel.tip = tip;
    panel.frame = frame;

    // (sizes above are in metres: undo the device's own scale)
    Vector3 ls = dev.lossyScale;
    float s = Mathf.Max(1e-4f, Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
    card.transform.localScale = Vector3.one / s;
    card.transform.localRotation = Quaternion.Euler(0, 180, 0); // read from the front, like the videos
    card.transform.localPosition = CardPosition(b);
    if (videoSpot != null) { // the device's own video spot
      card.transform.position = videoSpot.position;
      card.transform.rotation = videoSpot.rotation;
    }
    card.SetActive(true);
    return card;
  }

  // the card's touch behaviour (VideoPlayerUI's colours and glow levels)
  public class Panel : manipObject {
    public tooltips tip;
    public Material frame;
    static readonly Color tint = new Color32(0x3A, 0x61, 0xD0, 0xFF);
    bool closing;

    void OnEnable() { closing = false; Glow(.3f); }

    void Glow(float gain) {
      if (frame == null) return;
      if (frame.HasProperty("_TintColor")) frame.SetColor("_TintColor", tint);
      if (frame.HasProperty("_EmissionGain")) frame.SetFloat("_EmissionGain", gain);
      else frame.color = Color.Lerp(lineColor, Color.white, Mathf.Clamp01((gain - .3f) * 2f));
    }

    public override void setState(manipState state) {
      if (curState == state) return;
      curState = state;
      if (closing) return;
      if (state == manipState.none) Glow(.3f);
      else if (state == manipState.selected) Glow(.45f);
      else if (state == manipState.grabbed) { closing = true; Glow(.9f); StartCoroutine(Close()); }
    }

    System.Collections.IEnumerator Close() {
      float t0 = Time.unscaledTime;
      while (Time.unscaledTime - t0 < .15f) yield return null;
      if (tip != null) tip.ToggleVideo(false); else gameObject.SetActive(false);
    }
  }

  static void Quad(string name, Transform parent, Vector3 pos, Vector3 scale, Material m) {
    GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
    Object.Destroy(q.GetComponent<Collider>());
    q.name = name;
    q.transform.SetParent(parent, false);
    q.transform.localPosition = pos;
    q.transform.localScale = scale;
    MeshRenderer r = q.GetComponent<MeshRenderer>();
    r.sharedMaterial = m;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  }

}
