// The internet room dialog (2026-11-18), shown on the settings panel in place of its
// buttons when MULTIPLAYER is turned ON in INTERNET mode.
//
// Terms page (first time, and whenever TermsVersion changes): what internet sessions
// mean for your samples, the limits, what Unity receives, the privacy policy's address.
// I AGREE / CANCEL, plus FORGET MY INTERNET ID (deletes this install's anonymous ID from
// Unity - privacy policy section 5) and the ID itself, to quote in an email.
//
// Code page: a 6-character ROOM CODE typed on a keypad (or the PC's keyboard), up to 4
// recent codes, NEW (makes a random code), DEL, CANCEL, JOIN. Whoever enters a code first
// hosts that room; anyone else with the code joins.
//
// The buttons are copies of the panel's MIDI OUT button (like the panel's other added
// buttons), so the MenuRoot prefab doesn't need editing; they report to this component.

using System.Collections.Generic;
using UnityEngine;

public class NetCodeDialog : componentInterface {
  public const int TermsVersion = 1;
  public const string PolicyUrl = "plaidpants.github.io/soundstagevr/SoundStagePrivacyPolicy.html";

  public static bool Agreed { get { return PlayerPrefs.GetInt("netTermsAgreed", 0) >= TermsVersion; } }
  public static bool IsOpen { get { return instance != null && instance.gameObject.activeInHierarchy; } }
  // the PC's keyboard types into the code (NetSpectator, DesktopMode leave the keys alone)
  public static bool Typing { get { return IsOpen && instance.page == Page.Code; } }

  const string TermsText =
    "Anyone with your room code can join while\n" +
    "the room is open. They hear, use and can keep\n" +
    "copies of the samples and recordings in your\n" +
    "scene. Share only what you have the right to\n" +
    "share, and only with people you trust: you're\n" +
    "responsible for what you share, and there is\n" +
    "no moderation.\n" +
    "Up to 4 players. Sessions end after 10 minutes\n" +
    "with nobody playing, or after 3 hours. Samples\n" +
    "over 5 MB aren't sent.\n" +
    "Rooms connect through Unity Gaming Services,\n" +
    "which receives your IP address and an anonymous\n" +
    "ID for this install (kept up to 30 days).\n" +
    "Not for children under 13. Provided as-is,\n" +
    "without warranty; it may change or stop at any\n" +
    "time. Privacy policy and terms:\n" +
    PolicyUrl;

  const string Reminder =
    "Anyone with the code can join and copy your\n" +
    "scene's samples. Sessions end after 10 min\n" +
    "idle or 3 hours. Samples over 5 MB aren't sent.";

  enum Page { Terms, Code }

  static NetCodeDialog instance;
  Page page;
  string code = "";
  bool forgetting;
  string forgetResult = "";

  Transform panel;                 // the settings panel (the buttons' parent)
  uiPanelSinglePress template;     // MIDI OUT
  TextMesh labelTemplate;          // the panel's WireLabel
  float leftX, rightX;             // column centres (button origins)
  float btnW, btnCX;               // a button's outline width and its centre's offset from the origin

  readonly List<GameObject> hidden = new List<GameObject>();
  readonly List<uiPanelSinglePress> keys = new List<uiPanelSinglePress>();
  readonly List<uiPanelSinglePress> recent = new List<uiPanelSinglePress>();
  uiPanelSinglePress del, make, cancel, join, terms, agree, refuse, forget;
  TextMesh title, codeText, body, idText;

  Color green = Color.HSVToRGB(.4f, 230f / 255, 118f / 255);
  Color red = Color.HSVToRGB(0f, 230f / 255, 118f / 255);
  Color blue = Color.HSVToRGB(.6f, .7f, .9f);

  // ---- opening ----

  // MULTIPLAYER ON in INTERNET mode (wirePanelComponentInterface)
  public static void Open(uiPanelSinglePress midiOut, TextMesh wireLabel, float rightColumnX) {
    if (midiOut == null) return;
    if (instance == null) {
      GameObject g = new GameObject("netCodeDialog");
      g.transform.SetParent(midiOut.transform.parent, false);
      instance = g.AddComponent<NetCodeDialog>();
      instance.Build(midiOut, wireLabel, rightColumnX);
      g.SetActive(false); // Show() hides the panel's own buttons when it opens
    }
    instance.Show(Agreed ? Page.Code : Page.Terms);
  }

  public static void Close() { if (instance != null) instance.Hide(); }

  void Show(Page p) {
    if (!gameObject.activeSelf) {
      // take over the panel: hide everything else on it until we close
      hidden.Clear();
      foreach (Transform t in panel) {
        if (t == transform || !t.gameObject.activeSelf) continue;
        hidden.Add(t.gameObject);
        t.gameObject.SetActive(false);
      }
      gameObject.SetActive(true);
      code = NetInternet.Code;
      forgetResult = "";
    }
    page = p;
    Refresh();
  }

  public static int ClosedFrame { get; private set; } = -1; // QuitKey: that Escape closed the dialog

  void Hide() {
    if (!gameObject.activeSelf) return;
    ClosedFrame = Time.frameCount;
    gameObject.SetActive(false);
    foreach (GameObject g in hidden) if (g != null) g.SetActive(true);
    hidden.Clear();
  }

  // (If the menu closes with the dialog open, it's still open when the menu comes back.)

  // ---- building ----

  void Build(uiPanelSinglePress midiOut, TextMesh wireLabel, float rightColumnX) {
    panel = midiOut.transform.parent;
    template = midiOut;
    labelTemplate = wireLabel;
    leftX = midiOut.transform.localPosition.x;
    rightX = rightColumnX;
    MeasureButton(midiOut, out btnW, out btnCX);
    // The panel's x axis may point to the viewer's left (it does on the settings panel):
    // lay buttons out left to right as seen, using the text's own right direction.
    Transform textT = wireLabel != null ? wireLabel.transform : midiOut.label != null ? midiOut.label.transform : null;
    mirrored = textT != null && Vector3.Dot(textT.right, panel.right) < 0;

    // rows as on the panel: 0 at the top of the panel, going up; 10 is the version label's
    // code page: title, code, keypad, buttons, recent codes, then the reminder under them
    title = MakeText("title", 10, .8f, TextAnchor.MiddleCenter);
    codeText = MakeText("code", 9, 1.3f, TextAnchor.MiddleCenter);
    for (int i = 0; i < NetInternet.CodeChars.Length; i++)
      keys.Add(MakeButton(NetInternet.CodeChars[i].ToString(), i, 8 - i / 8, i % 8, 8));
    del = MakeButton("DEL", 40, 4, 0, 5);
    make = MakeButton("NEW", 41, 4, 1, 5);
    terms = MakeButton("TERMS", 44, 4, 2, 5);
    cancel = MakeButton("CANCEL", 42, 4, 3, 5);
    join = MakeButton("JOIN", 43, 4, 4, 5);
    for (int i = 0; i < NetInternet.MaxRecent; i++) recent.Add(MakeButton("", 50 + i, 3, i, 4));
    body = MakeText("body", 2.45f, .5f, TextAnchor.UpperCenter);

    // terms page: title, the terms, the ID, then FORGET / CANCEL (BACK) / I AGREE
    idText = MakeText("id", 1.3f, .45f, TextAnchor.MiddleCenter);
    forget = MakeButton("FORGET MY INTERNET ID", 62, .3f, 0, 3);
    refuse = MakeButton("CANCEL", 61, .3f, 1, 3);
    agree = MakeButton("I AGREE", 60, .3f, 2, 3);
  }

  // the outline's width and centre in the panel's space
  public static void MeasureButton(uiPanelSinglePress b, out float width, out float centreX) {
    width = .15f; centreX = 0;
    if (b.outline == null) return;
    Bounds lb = b.outline.localBounds;
    Transform o = b.outline.transform, p = b.transform.parent;
    Vector3 a = p.InverseTransformPoint(o.TransformPoint(lb.min));
    Vector3 c = p.InverseTransformPoint(o.TransformPoint(lb.max));
    width = Mathf.Abs(c.x - a.x);
    centreX = (a.x + c.x) * .5f - b.transform.localPosition.x;
  }

  bool mirrored;
  float Left { get { return leftX + btnCX - btnW * .5f; } }
  float Centre { get { return Left + Width * .5f; } }
  // the panel x of a slot's centre, counting slots left to right as the viewer sees them
  float SlotX(float col, int cols) {
    float slot = Width / cols;
    return mirrored ? Left + Width - slot * (col + .5f) : Left + slot * (col + .5f);
  }
  float Width { get { return rightX - leftX + btnW; } }
  static float RowY(float row) { return .012f + .038f * row; }

  // a button in slot 'col' of 'cols' equal slots across the panel's width, in a row
  readonly Dictionary<uiPanelSinglePress, Vector3> labelScale = new Dictionary<uiPanelSinglePress, Vector3>();
  readonly Dictionary<uiPanelSinglePress, float> labelRoom = new Dictionary<uiPanelSinglePress, float>();

  uiPanelSinglePress MakeButton(string text, int id, float row, int col, int cols) {
    GameObject g = Instantiate(template.gameObject, transform, false);
    foreach (TextMesh t in g.GetComponentsInChildren<TextMesh>(true))
      if (t.text.Contains("REQUIRES")) t.gameObject.SetActive(false); // MIDI OUT's note
    g.name = "netCode_" + (text != "" ? text : id.ToString());
    uiPanelSinglePress u = g.GetComponent<uiPanelSinglePress>();
    u.buttonID = id;
    float slot = Width / cols;
    float sx = Mathf.Min(1f, slot * .92f / btnW);
    Vector3 s = template.transform.localScale;
    g.transform.localScale = new Vector3(s.x * sx, s.y, s.z);
    g.transform.localRotation = template.transform.localRotation;
    Vector3 p = template.transform.localPosition;
    p.x = SlotX(col, cols) - btnCX * sx;
    p.y = RowY(row);
    g.transform.localPosition = p;
    // the label: undo the squeeze and centre it on the outline
    if (u.label != null) {
      Transform lt = u.label.transform;
      Vector3 ls = lt.localScale;
      lt.localScale = new Vector3(ls.x / sx, ls.y, ls.z);
      u.label.anchor = TextAnchor.MiddleCenter;
      u.label.alignment = TextAlignment.Center;
      if (u.outline != null) {
        Bounds lb = u.outline.localBounds;
        Vector3 centre = u.outline.transform.TransformPoint(lb.center);
        Vector3 local = lt.parent.InverseTransformPoint(centre);
        lt.localPosition = new Vector3(local.x, local.y, lt.localPosition.z);
      }
      labelScale[u] = lt.localScale;
      labelRoom[u] = slot * .85f;
      SetLabel(u, text);
    }
    return u;
  }

  // A button's text, shrunk if it's too long for the button. By character count: the
  // panel's MIDI OUT button fits "ENABLE MIDI OUT" (15 characters) across its width.
  void SetLabel(uiPanelSinglePress u, string text) {
    if (u.label == null) return;
    u.label.text = text;
    if (!labelScale.TryGetValue(u, out Vector3 baseScale)) return;
    float fits = 15f * labelRoom[u] / Mathf.Max(1e-4f, btnW * .85f);
    float k = text.Length > fits ? fits / text.Length : 1f;
    u.label.transform.localScale = baseScale * k;
  }

  float WidthInPanel(TextMesh t) {
    Renderer r = t.GetComponent<Renderer>();
    if (r == null) return 0;
    Bounds lb = r.localBounds;
    Vector3 a = panel.InverseTransformPoint(t.transform.TransformPoint(lb.min));
    Vector3 c = panel.InverseTransformPoint(t.transform.TransformPoint(lb.max));
    return Mathf.Abs(c.x - a.x);
  }

  float HeightInPanel(TextMesh t) {
    Renderer r = t.GetComponent<Renderer>();
    if (r == null) return 0;
    Bounds lb = r.localBounds;
    Vector3 a = panel.InverseTransformPoint(t.transform.TransformPoint(lb.min));
    Vector3 c = panel.InverseTransformPoint(t.transform.TransformPoint(lb.max));
    return Mathf.Abs(c.y - a.y);
  }

  Vector3 baseTextScale;
  TextMesh MakeText(string name, float row, float size, TextAnchor anchor) {
    GameObject g;
    if (labelTemplate != null) {
      g = Instantiate(labelTemplate.gameObject, transform, false);
      baseTextScale = labelTemplate.transform.localScale;
    } else {
      g = new GameObject();
      g.transform.SetParent(transform, false);
      g.AddComponent<TextMesh>();
      baseTextScale = Vector3.one * .01f;
    }
    g.name = "netCode_" + name;
    TextMesh t = g.GetComponent<TextMesh>();
    t.anchor = anchor;
    t.alignment = anchor == TextAnchor.UpperCenter ? TextAlignment.Left : TextAlignment.Center; // a block of lines: centred, lines left-aligned
    t.richText = false;
    g.transform.localScale = baseTextScale * size;
    Vector3 p = labelTemplate != null ? labelTemplate.transform.localPosition : Vector3.zero;
    p.x = Centre;
    p.y = RowY(row);
    g.transform.localPosition = p;
    return t;
  }

  // set a text and shrink it to fit (width, and height for the terms)
  void SetText(TextMesh t, string text, float size, float maxHeight = 0) {
    t.text = text;
    t.transform.localScale = baseTextScale * size;
    float w = WidthInPanel(t);
    float k = w > Width && w > 1e-5f ? Width / w : 1f;
    if (maxHeight > 0) {
      float h = HeightInPanel(t) * k;
      if (h > maxHeight && h > 1e-5f) k *= maxHeight / h;
    }
    t.transform.localScale = baseTextScale * size * k;
  }

  // ---- showing a page ----

  void Refresh() {
    bool c = page == Page.Code;
    foreach (uiPanelSinglePress k in keys) k.gameObject.SetActive(c);
    del.gameObject.SetActive(c);
    make.gameObject.SetActive(c);
    cancel.gameObject.SetActive(c);
    join.gameObject.SetActive(c);
    terms.gameObject.SetActive(c);
    codeText.gameObject.SetActive(c);
    idText.gameObject.SetActive(!c);
    forget.gameObject.SetActive(!c);
    agree.gameObject.SetActive(!c);
    refuse.gameObject.SetActive(!c);

    if (c) {
      SetText(title, "INTERNET ROOM CODE", .8f);
      var sb = new System.Text.StringBuilder();
      for (int i = 0; i < NetInternet.CodeLength; i++) {
        if (i > 0) sb.Append(' ');
        sb.Append(i < code.Length ? code[i] : '_');
      }
      SetText(codeText, sb.ToString(), 1.3f);
      List<string> r = NetInternet.Recent();
      for (int i = 0; i < recent.Count; i++) {
        bool on = i < r.Count;
        recent[i].gameObject.SetActive(on);
        if (on) { SetLabel(recent[i], r[i]); recent[i].newColor(r[i] == code ? green : blue); }
      }
      bool ok = NetInternet.Valid(code);
      join.newColor(ok ? green : red);
      SetText(body, Reminder, .5f, RowY(2.45f) - RowY(.4f));
      body.transform.localPosition = new Vector3(Centre, RowY(2.45f), body.transform.localPosition.z);
    } else {
      foreach (uiPanelSinglePress b in recent) b.gameObject.SetActive(false);
      SetText(title, "INTERNET MULTIPLAYER", .8f);
      SetText(body, TermsText, .6f, RowY(9.45f) - RowY(1.9f));
      body.transform.localPosition = new Vector3(Centre, RowY(9.45f), body.transform.localPosition.z);
      string id = NetInternet.PlayerId;
      SetText(idText, forgetResult != "" ? forgetResult : "YOUR INTERNET ID: " + (id != "" ? id : "NONE YET"), .45f);
      SetLabel(forget, forgetting ? "FORGETTING..." : "FORGET MY INTERNET ID");
      forget.newColor(red);
      SetLabel(refuse, Agreed ? "BACK" : "CANCEL");
      agree.gameObject.SetActive(!Agreed);
      agree.newColor(green);
      refuse.newColor(Agreed ? blue : red);
    }
  }

  void FitText(TextMesh t, float maxWidth) {
    float w = WidthInPanel(t);
    if (w > maxWidth && w > 1e-5f) t.transform.localScale *= maxWidth / w;
  }

  // ---- presses ----

  public override void hit(bool on, int ID = -1) {
    if (!on) return;
    if (ID >= 0 && ID < NetInternet.CodeChars.Length) Type(NetInternet.CodeChars[ID]);
    else if (ID == 40) Backspace();
    else if (ID == 41) { code = NetInternet.NewCode(); Refresh(); }
    else if (ID == 42) Hide();
    else if (ID == 43) Join();
    else if (ID == 44) Show(Page.Terms);
    else if (ID >= 50 && ID < 50 + NetInternet.MaxRecent) {
      List<string> r = NetInternet.Recent();
      int i = ID - 50;
      if (i < r.Count) { code = r[i]; Refresh(); }
    } else if (ID == 60) {
      PlayerPrefs.SetInt("netTermsAgreed", TermsVersion);
      Show(Page.Code);
    } else if (ID == 61) {
      if (Agreed) Show(Page.Code); else Hide();
    } else if (ID == 62) Forget();
  }

  void Type(char ch) {
    if (code.Length >= NetInternet.CodeLength) return;
    code = NetInternet.Normalize(code + ch);
    Refresh();
  }

  void Backspace() {
    if (code.Length > 0) code = code.Substring(0, code.Length - 1);
    Refresh();
  }

  void Join() {
    if (!NetInternet.Valid(code)) return;
    Hide();
    NetSession.JoinRoom(code);
  }

  async void Forget() {
    if (forgetting) return;
    forgetting = true;
    forgetResult = "";
    if (NetSession.On && NetSession.Kind == NetSession.Mode.Internet) NetSession.SetOn(false);
    Refresh();
    bool ok = await NetInternet.ForgetId();
    forgetting = false;
    forgetResult = ok ? "INTERNET ID DELETED" : "COULDN'T DELETE IT (OFFLINE?)";
    if (this != null && gameObject.activeInHierarchy) Refresh();
  }

  // the PC's keyboard
  void Update() {
    if (page != Page.Code) return;
    foreach (char ch in Input.inputString) {
      if (ch == '\b') Backspace();
      else if (ch == '\n' || ch == '\r') Join();
      else {
        string n = NetInternet.Normalize(ch.ToString());
        if (n.Length == 1) Type(n[0]);
      }
    }
    if (Input.GetKeyDown(KeyCode.Escape)) Hide();
  }
}
