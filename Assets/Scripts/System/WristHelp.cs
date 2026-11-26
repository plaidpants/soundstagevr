// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Hand tracking help on the wrist (2026-11-24). Turn the back of your LEFT hand towards
// your face, like looking at a watch, and the same "?" as the devices' help-video buttons
// appears on the back of the hand. Touch it with your right index finger to open a card
// listing what the fingers do; touch it again (or turn the hand away) to close it.
// Controllers have tooltips on their buttons, but a hand has nothing fixed to hang labels
// on. (The back of the hand, not the palm: palm towards the face is the Quest's own menu
// gesture.)
//
// Nothing shows while that hand is holding something, so it never gets in the way of playing.

using UnityEngine;

public class WristHelp : MonoBehaviour {
  public static bool enabledByUser = true;

  // what the card says (keep in step with manipulator / HandInput when gestures change)
  public static string text =
    "HANDS\n" +
    "Index pinch:  grab, turn, plug,\n" +
    "    press buttons, move sliders\n" +
    "Pinky pinch, palm down:  menu\n" +
    "Fingertips:  play keys\n" +
    "Slap:  hit drums\n" +
    "Other hand:  reposition\n" +
    "    held items\n" +
    "Copy, delete:  use controllers\n" +
    "Touch ? again to close";

  // how squarely the back of the hand must face the head (cosine), with some hysteresis,
  // and how near the head the hand must be
  public static float showFacing = .75f, hideFacing = .55f, maxDistance = .65f;
  const float fadeTime = .15f, holdBeforeShow = .25f;
  const float buttonSize = .024f;           // the "?", metres across
  const float aboveSkin = .022f;            // back-of-hand bones to just above the skin
  const float touchRadius = .018f;          // fingertip this close to the "?" = touch
  const float releaseRadius = .04f;         // and must move this far away before the next touch
  const float pad = .006f, line = .0012f;   // card margin and outline width

  // the help-video "?" colours (tooltips.cs): idle like a hovered "?", lit like a pressed one
  static readonly Color idleTint = Color.HSVToRGB(.6f, .95f, .5f);
  static readonly Color litTint = Color.HSVToRGB(.6f, .9f, 1f);
  static readonly Color textColor = new Color(.85f, .92f, 1f);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (FindObjectOfType<WristHelp>() != null) return;
    new GameObject("WristHelp").AddComponent<WristHelp>();
  }

  GameObject button, card;
  TextMesh label;
  Material qMat, backMat, lineMat;
  bool qTinted; // qMat is the tooltip's additive material (_TintColor), not the fallback
  float buttonAlpha, cardAlpha, facingSince = -1, pulse;
  bool showing, open, fingerArmed = true;

  static Shader UnlitTransparent() {
    Shader sh = Shader.Find("Sprites/Default"); // unlit, transparent, both sides (always in builds)
    if (sh == null) sh = Shader.Find("Unlit/Transparent");
    return sh;
  }

  static GameObject Quad(string name, Transform parent, Vector3 pos, Vector3 scale, Material m) {
    GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
    Destroy(q.GetComponent<Collider>()); // nothing to grab or bump into
    q.name = name;
    q.transform.SetParent(parent, false);
    q.transform.localPosition = pos;
    q.transform.localScale = scale;
    MeshRenderer r = q.GetComponent<MeshRenderer>();
    r.sharedMaterial = m;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    return q;
  }

  void BuildButton() {
    button = new GameObject("WristHelpButton");
    button.transform.SetParent(transform, false);

    // the devices' own "?" (question.fbx with TooltipQuestionMark), taken from a device prefab
    Mesh mesh = null; Material mat = null;
    GameObject prefab = Resources.Load<GameObject>("Prefabs/ADSR");
    tooltips tip = prefab != null ? prefab.GetComponentInChildren<tooltips>(true) : null;
    if (tip != null) {
      MeshFilter mf = tip.GetComponent<MeshFilter>();
      Renderer r = tip.GetComponent<Renderer>();
      if (mf != null) mesh = mf.sharedMesh;
      if (r != null) mat = r.sharedMaterial;
    }

    if (mesh != null && mat != null) {
      GameObject q = new GameObject("question");
      q.transform.SetParent(button.transform, false);
      q.transform.localRotation = Quaternion.Euler(0, 180, 0); // as on the devices
      Vector3 b = mesh.bounds.size;
      float s = buttonSize / Mathf.Max(.0001f, Mathf.Max(b.x, b.y));
      q.transform.localScale = Vector3.one * s;
      q.AddComponent<MeshFilter>().sharedMesh = mesh;
      MeshRenderer mr = q.AddComponent<MeshRenderer>();
      qMat = new Material(mat);
      mr.sharedMaterial = qMat;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      qTinted = qMat.HasProperty("_TintColor");
    } else {
      // fallback: a "?" in an outlined box
      qMat = new Material(UnlitTransparent());
      float w = buttonSize, l = .0015f;
      Quad("t", button.transform, new Vector3(0, (w - l) / 2, 0), new Vector3(w, l, 1), qMat);
      Quad("b", button.transform, new Vector3(0, -(w - l) / 2, 0), new Vector3(w, l, 1), qMat);
      Quad("l", button.transform, new Vector3(-(w - l) / 2, 0, 0), new Vector3(l, w, 1), qMat);
      Quad("r", button.transform, new Vector3((w - l) / 2, 0, 0), new Vector3(l, w, 1), qMat);
      TextMesh t = NewText("q", button.transform, "?", .003f, TextAlignment.Center);
      t.GetComponent<MeshRenderer>().sharedMaterial = qMat; // same colour as the box
    }
    button.SetActive(false);
  }

  static TextMesh NewText(string name, Transform parent, string s, float charSize, TextAlignment align) {
    GameObject t = new GameObject(name);
    t.transform.SetParent(parent, false);
    t.transform.localPosition = new Vector3(0, 0, -.002f); // just in front of the backing
    TextMesh tm = t.AddComponent<TextMesh>();
    Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    tm.font = f;
    t.GetComponent<MeshRenderer>().sharedMaterial = f.material;
    tm.fontSize = 64;
    tm.characterSize = charSize;
    tm.anchor = TextAnchor.MiddleCenter;
    tm.alignment = align;
    tm.text = s;
    return tm;
  }

  void BuildCard() {
    card = new GameObject("WristHelpCard");
    card.transform.SetParent(transform, false);
    label = NewText("text", card.transform, text, .0013f, TextAlignment.Left);

    // fit the card to the text (the card is still at the origin, unrotated)
    Vector2 size = label.GetComponent<MeshRenderer>().bounds.size;
    if (size.x < .02f || size.y < .02f) size = new Vector2(.17f, .09f);
    float w = size.x + 2 * pad, h = size.y + 2 * pad;

    backMat = new Material(UnlitTransparent());
    lineMat = new Material(UnlitTransparent());
    Quad("back", card.transform, Vector3.zero, new Vector3(w, h, 1), backMat);
    Vector3 front = new Vector3(0, 0, -.001f);
    Quad("t", card.transform, front + new Vector3(0, (h - line) / 2, 0), new Vector3(w, line, 1), lineMat);
    Quad("b", card.transform, front + new Vector3(0, -(h - line) / 2, 0), new Vector3(w, line, 1), lineMat);
    Quad("l", card.transform, front + new Vector3(-(w - line) / 2, 0, 0), new Vector3(line, h, 1), lineMat);
    Quad("r", card.transform, front + new Vector3((w - line) / 2, 0, 0), new Vector3(line, h, 1), lineMat);
    cardHeight = h;
    card.SetActive(false);
  }
  float cardHeight = .1f;

  void LateUpdate() {
    HandFingertips hand = Hand(true);
    Camera head = Camera.main;
    bool want = false;
    Vector3 buttonPos = Vector3.zero, cardPos = Vector3.zero;
    Quaternion buttonRot = Quaternion.identity, cardRot = Quaternion.identity;

    if (enabledByUser && hand != null && head != null && platformSetup.HandsModeStable) {
      hand.GetHoldPose(out Vector3 grip, out Quaternion handRot);
      Vector3 backOfHand = handRot * Vector3.up;
      Vector3 centre = hand.BackOfHandCenter;
      Vector3 toHead = head.transform.position - centre;
      float dist = toHead.magnitude;
      float facing = dist > 1e-4f ? Vector3.Dot(backOfHand, toHead / dist) : 0;
      bool holding = hand.owner != null && hand.owner.getSelection() != null;
      bool facingNow = !holding && dist < maxDistance && facing > (showing ? hideFacing : showFacing);
      if (facingNow) {
        if (facingSince < 0) facingSince = Time.time;
        want = showing || Time.time - facingSince >= holdBeforeShow; // a glance, not a pass
      } else facingSince = -1;

      // the "?" in the middle of the back of the hand; the card above it; both facing the eyes
      Vector3 up = head.transform.up;
      buttonPos = centre + backOfHand * aboveSkin;
      buttonRot = Quaternion.LookRotation(buttonPos - head.transform.position, up);
      cardPos = buttonPos + backOfHand * .01f + up * (buttonSize * .5f + .012f + cardHeight * .5f);
      cardRot = Quaternion.LookRotation(cardPos - head.transform.position, up);
    } else facingSince = -1;

    showing = want;
    if (!want) { open = false; fingerArmed = true; }

    // the right index fingertip toggles the card
    if (want && buttonAlpha > .5f) {
      HandFingertips other = Hand(false);
      if (other != null && other.indexTip != null) {
        float d = Vector3.Distance(other.indexTip.position, buttonPos);
        if (fingerArmed && d < touchRadius) {
          open = !open;
          fingerArmed = false;
          pulse = 1;
        } else if (!fingerArmed && d > releaseRadius) fingerArmed = true;
      }
    }

    buttonAlpha = Mathf.MoveTowards(buttonAlpha, want ? 1 : 0, Time.deltaTime / fadeTime);
    cardAlpha = Mathf.MoveTowards(cardAlpha, want && open ? 1 : 0, Time.deltaTime / fadeTime);
    pulse = Mathf.MoveTowards(pulse, 0, Time.deltaTime * 6);

    if (buttonAlpha <= 0 && cardAlpha <= 0) {
      if (button != null && button.activeSelf) button.SetActive(false);
      if (card != null && card.activeSelf) card.SetActive(false);
      return;
    }

    if (button == null) BuildButton();
    if (card == null) BuildCard();

    if (!button.activeSelf) button.SetActive(true);
    if (want) button.transform.SetPositionAndRotation(buttonPos, buttonRot); // (fading out: stay where it was)
    button.transform.localScale = Vector3.one * (1 + .25f * pulse);
    Color tint = Color.Lerp(idleTint, litTint, open ? 1 : pulse);
    if (qTinted) qMat.SetColor("_TintColor", tint * buttonAlpha); // additive: darker = fainter
    else qMat.color = new Color(tint.r, tint.g, tint.b, buttonAlpha);

    bool cardOn = cardAlpha > 0;
    if (card.activeSelf != cardOn) card.SetActive(cardOn);
    if (cardOn) {
      if (want) card.transform.SetPositionAndRotation(cardPos, cardRot);
      label.color = new Color(textColor.r, textColor.g, textColor.b, cardAlpha);
      backMat.color = new Color(0, 0, 0, .75f * cardAlpha);
      lineMat.color = new Color(litTint.r, litTint.g, litTint.b, cardAlpha);
    }
  }

  // this headset's own tracked hand (not another player's)
  static HandFingertips Hand(bool left) {
    foreach (HandFingertips h in HandFingertips.active)
      if (h != null && h.isLeft == left && h.owner != null && h.HasHoldPose) return h;
    return null;
  }
}
