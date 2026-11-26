// Phone version (2026-11-25): SoundStage on an Android phone, by touch.
//
// The phone build (SoundStage > Build Phone ..., Editor/QuestBuildMenu.cs) is the Quest
// project built without VR and with SOUNDSTAGE_PHONE defined. It runs the PC's desktop
// mode (Desktop/DesktopMode.cs: the right controller follows a pointer) with touch as
// the pointer:
//
//   tap or press a control      use it (buttons, keys, switches, sliders; hold a key
//                               and slide across others for a run; tap a drum pad).
//                               A touch just beside a small control still gets it.
//   double-tap a device         fly to it, face on to its front, filling the screen (on
//                               the menu or a panel: fly up to that spot)
//   MOTION                      look around by turning the phone (its gyroscope), like
//                               a 360 photo viewer; dragging empty space still turns
//                               you left / right. Press again to go back to dragging.
//   WATCH                       watch a camera device's broadcast full screen, as a PC
//                               shows it (someone presses BROADCAST on a camera); the
//                               controls hide. BACK returns to the interactive view.
//   MAP                         a map in the corner, north up: devices (tap one, or near
//                               it, to fly to it face on), the other players, and you
//                               with what the
//                               screen shows. Tap yourself (the middle) to level the
//                               view and bring the hand back. It hides while you drag.
//   drag a control or handle    move it (a device by its handle, a cable end into a jack,
//                               a dial: drag left / right to turn it). Dragged to the
//                               screen's edge, the view turns that way, to reach a jack
//                               or a spot that's off screen.
//   drag empty space            look around
//   two fingers                 drag: move left / right / up / down
//                               twist: turn left / right
//                               pinch: move forward / back (while holding something:
//                               push it further / bring it nearer)
//   MENU / COPY / DELETE        the menu (A), copy (B on a handle), delete
//   back button twice           quit
//   turn the phone              landscape or portrait (the menu fits portrait well)
//
// In the Editor: SoundStage > Phone Mode in Editor. The left mouse button is one finger,
// the right button dragged is two fingers moving together, the wheel is a pinch.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class Phone {
  public static bool Active {
    get {
#if SOUNDSTAGE_PHONE
      return true;
#else
      return Application.isEditor && PlayerPrefs.GetInt("phoneModeInEditor", 0) == 1;
#endif
    }
  }
}

[DefaultExecutionOrder(-200)] // before DesktopMode (-100), which reads PointerInput
public class PhoneMode : MonoBehaviour {
  public static PhoneMode instance;

  public static float lookDegreesPerScreen = 120f;  // a drag across the screen's height turns this far
  public static float moveMetresPerScreen = 2.5f;   // two-finger drag across the screen's height
  public static float pinchMetresPerScreen = 3f;    // fingers spread by the screen's height
  public static float minPress = .15f;              // a quick tap still holds the trigger this long (s)
  public static float reachMM = 4f;                 // a control this close to the fingertip (mm) still counts
  public static float edgeZone = .1f;               // of the screen's shorter side: the edge band that turns the view
  public static float edgeTurnDegPerSec = 70f;      // at the very edge
  public static float doubleTapTime = .35f, tapTime = .3f;
  public static float landscapeFov = 60f, portraitFov = 80f; // vertical, degrees

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!Phone.Active) return;
    PointerInput.touch = true;
    GameObject g = new GameObject("PhoneMode");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<PhoneMode>();
    if (!Application.isEditor) {
      Input.simulateMouseWithTouches = false;          // touches are read here, not as a mouse
      Screen.sleepTimeout = SleepTimeout.NeverSleep;   // music playing with nobody touching the screen
      Application.targetFrameRate = 60;                // Android's default is 30
    }
    Debug.Log("[PhoneMode] phone: touch controls (tap / drag controls, drag empty space to look, two fingers to move, pinch forward / back)");
  }

  // ---- on-screen buttons ----

  public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler {
    public bool held, down, up;     // down / up: true for one frame (promoted in PhoneMode.Update)
    bool pendingDown, pendingUp;
    Image img;
    void Awake() { img = GetComponent<Image>(); }
    public void OnPointerDown(PointerEventData e) { held = true; pendingDown = true; Tint(); }
    public void OnPointerUp(PointerEventData e) { held = false; pendingUp = true; Tint(); }
    public void Promote() { down = pendingDown; up = pendingUp; pendingDown = pendingUp = false; }
    void Tint() { if (img != null) img.color = held ? new Color(.2f, .9f, 1f, .55f) : new Color(0f, .15f, .2f, .55f); }
  }

  readonly HoldButton[] buttons = new HoldButton[6]; // MENU (Tab), COPY (C), DELETE (Delete), MAP, MOTION, WATCH

  public static bool Key(KeyCode k, int kind) {
    if (instance == null) return false;
    int i = k == KeyCode.Tab ? 0 : k == KeyCode.C ? 1 : k == KeyCode.Delete ? 2 : -1;
    if (i < 0 || instance.buttons[i] == null) return false;
    HoldButton b = instance.buttons[i];
    return kind == 0 ? b.held : kind == 1 ? b.down : b.up;
  }

  void Start() { MakeUI(); }

  CanvasScaler scaler;
  bool? wasPortrait;
  // portrait: the buttons keep their size, and the view is taller (vertical field of view)
  void FitScreen() {
    bool portrait = Screen.height > Screen.width;
    if (wasPortrait == portrait && (Camera.main == null || Mathf.Approximately(Camera.main.fieldOfView, portrait ? portraitFov : landscapeFov))) return;
    wasPortrait = portrait;
    if (scaler != null) {
      scaler.referenceResolution = portrait ? new Vector2(1080, 1920) : new Vector2(1920, 1080);
      scaler.matchWidthOrHeight = portrait ? 0f : 1f; // scale by the shorter side either way
    }
    if (Camera.main != null && !UnityEngine.XR.XRSettings.isDeviceActive) Camera.main.fieldOfView = portrait ? portraitFov : landscapeFov;
  }

  float PixelsPerMM { get { return (Screen.dpi > 0 ? Screen.dpi : 160f * Screen.height / 1080f) / 25.4f; } }

  // A touch beside a small control: the nearest control within reachMM, as an offset
  // from the fingertip (kept for the whole touch, so a slider follows the finger).
  int Reach(Vector2 p, DesktopMode dm, out Vector2 offset) {
    offset = Vector2.zero;
    int k = dm.PickKind(p);
    if (k == 2) return 2;
    float r = reachMM * PixelsPerMM;
    for (int ring = 1; ring <= 2; ring++) {
      float rr = r * ring / 2f;
      for (int i = 0; i < 8; i++) {
        float a = i * Mathf.PI / 4f + (ring == 2 ? Mathf.PI / 8f : 0f);
        Vector2 o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        if (dm.PickKind(p + o) == 2) { offset = o; return 2; }
      }
    }
    return k;
  }

  void MakeUI() {
    // an event system for the buttons; the project's Input Manager has no Submit /
    // Cancel / Horizontal / Vertical axes, so every input module gets MouseOnlyInput
    if (FindObjectOfType<EventSystem>() == null) {
      GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
      DontDestroyOnLoad(es);
    }
    foreach (StandaloneInputModule im in FindObjectsOfType<StandaloneInputModule>(true))
      if (im.inputOverride == null || !(im.inputOverride is MouseOnlyInput)) im.inputOverride = im.gameObject.AddComponent<MouseOnlyInput>();

    GameObject cg = new GameObject("PhoneUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
    DontDestroyOnLoad(cg);
    Canvas canvas = cg.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 100;
    CanvasScaler cs = cg.GetComponent<CanvasScaler>();
    cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    cs.referenceResolution = new Vector2(1920, 1080);
    cs.matchWidthOrHeight = 1f;
    scaler = cs;

    Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    string[] names = { "MENU", "COPY", "DELETE", "MAP", "MOTION", "WATCH" };
    for (int i = 0; i < names.Length; i++) {
      GameObject bg = new GameObject(names[i], typeof(RectTransform), typeof(Image));
      bg.transform.SetParent(cg.transform, false);
      RectTransform rt = bg.GetComponent<RectTransform>();
      rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
      rt.pivot = new Vector2(1, 0);
      rt.sizeDelta = new Vector2(240, 120);
      rt.anchoredPosition = new Vector2(-40, 40 + (names.Length - 1 - i) * 140);
      if (i == 5) { rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1); rt.anchoredPosition = new Vector2(-40, -40); }
      else if (i >= 3) { rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0); rt.anchoredPosition = new Vector2(40, 40 + (i - 3) * 140); }
      Image img = bg.GetComponent<Image>();
      img.color = new Color(0f, .15f, .2f, .55f);
      buttons[i] = bg.AddComponent<HoldButton>();

      GameObject tg = new GameObject("Label", typeof(RectTransform), typeof(Text));
      tg.transform.SetParent(bg.transform, false);
      RectTransform tr = tg.GetComponent<RectTransform>();
      tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
      Text t = tg.GetComponent<Text>();
      t.font = font;
      t.text = names[i];
      t.fontSize = 44;
      t.alignment = TextAnchor.MiddleCenter;
      t.color = new Color(.6f, 1f, 1f, .95f);
      t.raycastTarget = false;
    }
  }

  // ---- fingers ----

  struct Finger { public int id; public Vector2 pos; }
  readonly List<Finger> cur = new List<Finger>();
  readonly HashSet<int> onUI = new HashSet<int>(); // fingers that started on a button

  enum Mode { None, Interact, Look, Two }
  Mode mode;
  int pointerId = -1;
  Vector2 lastPos;
  float prevDist;
  Vector2 prevMid;
  float prevAngle;
  bool pinching;
  float pressedAt, releaseAt = -1;
  Vector2 reachOffset;          // fingertip -> the control it reached
  float downAt; Vector2 downPos; bool moved; int downKind;
  float lastTapAt = -10; Vector2 lastTapPos;

  void Update() {
    foreach (HoldButton b in buttons) if (b != null) b.Promote();
    if (buttons[3] != null && buttons[3].down) ToggleMap();
    if (buttons[4] != null && buttons[4].down) ToggleMotion();
    if (buttons[5] != null && buttons[5].down) ToggleWatch();
    Watch();
    Motion();
    UpdateMap();
    PointerInput.tDown = false;
    PointerInput.tScroll = 0;
    PointerInput.tDelta = Vector2.zero;

    FitScreen();
    if (watching) { PointerInput.tHeld = false; mode = Mode.None; coasting = false; return; } // just watching
    ReadFingers();
    if (cur.Count > 0) coasting = false; else Coast();

    // a quick tap: keep the trigger held a moment so the press lands (DesktopMode only
    // counts the trigger once the hand has touched what it points at)
    if (releaseAt >= 0) {
      if (cur.Count > 0 || Time.unscaledTime >= releaseAt) { PointerInput.tHeld = false; releaseAt = -1; }
    }

    if (cur.Count == 0) {
      if (mode == Mode.Interact || mode == Mode.Look) EndTouch();
      if (mode == Mode.Look && lookVel.magnitude > 20f) coasting = true;
      if (mode == Mode.Interact && PointerInput.tHeld) {
        if (Time.unscaledTime - pressedAt < minPress) releaseAt = pressedAt + minPress;
        else PointerInput.tHeld = false;
      }
      mode = Mode.None; pointerId = -1; pinching = false;
      return;
    }

    if (cur.Count == 1 && mode != Mode.Two) {
      Finger f = cur[0];
      if (mode == Mode.None) {
        DesktopMode dm = DesktopMode.instance;
        if (NetSpectator.instance != null) NetSpectator.instance.StopFlying();
        int kind = dm != null ? Reach(f.pos, dm, out reachOffset) : 0;
        bool control = kind > 0;
        mode = control ? Mode.Interact : Mode.Look;
        pointerId = f.id;
        lastPos = f.pos;
        lookVel = Vector2.zero;
        downAt = Time.unscaledTime; downPos = f.pos; moved = false; downKind = kind;
        if (!control) reachOffset = Vector2.zero;
        if (control) {
          PointerInput.tPos = f.pos + reachOffset;
          PointerInput.tDown = true;
          PointerInput.tHeld = true;
          pressedAt = Time.unscaledTime;
        }
      }
      Vector2 d = f.pos - lastPos;
      lastPos = f.pos;
      if ((f.pos - downPos).magnitude > 3f * PixelsPerMM) moved = true;
      if (mode == Mode.Interact) {
        PointerInput.tDelta = d;
        PointerInput.tPos = f.pos + reachOffset;
        if (DesktopMode.Holding) EdgeTurn(f.pos);
      } else if (mode == Mode.Look && NetSpectator.instance != null) {
        float k = lookDegreesPerScreen / Screen.height;
        if (motion) motionYawOffset -= d.x * k;          // MOTION: the phone sets up / down
        else NetSpectator.instance.Look(-d.x * k, d.y * k); // the world follows the finger
        float dt = Mathf.Max(Time.unscaledDeltaTime, .001f);
        Vector2 v = Vector2.ClampMagnitude(new Vector2(-d.x * k, motion ? 0 : d.y * k) / dt, coastMaxDegPerSec);
        lookVel = Vector2.Lerp(lookVel, v, .4f); // a finger held still brings it back to zero
      }
      pinching = false;
      return;
    }

    if (cur.Count >= 2) {
      Finger a = cur[0], b = cur[1];
      if (mode == Mode.Interact) {
        // keep pointing with the first finger
        foreach (Finger f in cur) if (f.id == pointerId) a = f;
        foreach (Finger f in cur) if (f.id != pointerId) { b = f; break; }
      }
      float dist = Vector2.Distance(a.pos, b.pos);
      Vector2 mid = (a.pos + b.pos) * .5f;

      if (mode == Mode.Interact && DesktopMode.Holding) {
        // holding something: the pinch pushes it away / brings it nearer
        if (!pinching) { prevDist = dist; pinching = true; }
        if (prevDist > 1f) PointerInput.tScroll = (dist / prevDist - 1f) * 10f;
        prevDist = dist;
        PointerInput.tDelta = a.pos - lastPos;
        PointerInput.tPos = a.pos + reachOffset;
        lastPos = a.pos;
        moved = true;
        return;
      }

      if (mode != Mode.Two) {
        if (mode == Mode.Interact) PointerInput.tHeld = false;
        lastTapAt = -10;
        mode = Mode.Two;
        prevDist = dist; prevMid = mid; prevAngle = TwistAngle(a, b);
        return;
      }
      Vector2 dm2 = mid - prevMid;
      float pinch = dist - prevDist;
      float angle = TwistAngle(a, b);
      float twist = Mathf.DeltaAngle(prevAngle, angle);
      prevMid = mid; prevDist = dist; prevAngle = angle;
      // twist: the world turns with the fingers (anticlockwise -> turn right)
      if (Mathf.Abs(twist) < 45f && NetSpectator.instance != null) {
        if (motion) motionYawOffset += twist * twistGain;
        else NetSpectator.instance.Look(twist * twistGain, 0);
      }
      if (NetSpectator.instance != null) {
        float m = moveMetresPerScreen / Screen.height, p = pinchMetresPerScreen / Screen.height;
        NetSpectator.instance.Fly(new Vector3(-dm2.x * m, -dm2.y * m, pinch * p));
      }
    }
  }

  // holding something with the finger near an edge: turn the view that way, so the
  // held thing (which stays under the finger) goes where the screen didn't reach
  void EdgeTurn(Vector2 p) {
    if (NetSpectator.instance == null) return;
    float zone = edgeZone * Mathf.Min(Screen.width, Screen.height);
    float x = 0, y = 0;
    if (p.x < zone) x = -(zone - p.x) / zone; else if (p.x > Screen.width - zone) x = (p.x - (Screen.width - zone)) / zone;
    if (p.y < zone) y = (zone - p.y) / zone; else if (p.y > Screen.height - zone) y = -(p.y - (Screen.height - zone)) / zone;
    if (x == 0 && y == 0) return;
    float s = edgeTurnDegPerSec * Time.unscaledDeltaTime;
    if (motion) { motionYawOffset += Mathf.Clamp(x, -1, 1) * s; return; }
    NetSpectator.instance.Look(Mathf.Clamp(x, -1, 1) * s, Mathf.Clamp(y, -1, 1) * s); // bottom edge: look down
  }

  // a finger lifted: was it a tap, and the second of two on a device or panel?
  void EndTouch() {
    bool tap = !moved && Time.unscaledTime - downAt < tapTime && downKind != 2;
    if (!tap) { lastTapAt = -10; return; }
    if (Time.unscaledTime - lastTapAt < doubleTapTime && (downPos - lastTapPos).magnitude < 8f * PixelsPerMM) {
      lastTapAt = -10;
      Focus(downPos);
      return;
    }
    lastTapAt = Time.unscaledTime; lastTapPos = downPos;
  }

  void Focus(Vector2 p) {
    DesktopMode dm = DesktopMode.instance;
    if (dm == null) return;
    if (!dm.FocusTarget(p, out Vector3 centre, out float radius, out Vector3 viewDir)) return;
    FlyToView(centre, radius, viewDir);
  }

  public void FlyToView(Vector3 centre, float radius, Vector3 viewDir) {
    Camera cam = Camera.main;
    if (cam == null || NetSpectator.instance == null) return;
    // just far enough back that it fills the screen (its shorter side)
    float half = cam.fieldOfView * .5f * Mathf.Deg2Rad;
    if (Screen.height > Screen.width) half = Mathf.Atan(Mathf.Tan(half) * Screen.width / Screen.height);
    float dist = Mathf.Clamp(radius / Mathf.Tan(half) * 1.1f, .15f, 3f);
    NetSpectator.instance.FlyTo(centre - viewDir * dist, centre);
    Debug.Log("[PhoneMode] double-tap: flying to " + centre.ToString("F2") + " (" + dist.ToString("F2") + " m away)");
  }

  // ---- MOTION: look by turning the phone ----
  //
  // Only the direction out of the back of the phone is used (not its roll), so it works
  // the same whichever way the screen is turned, and the view stays level. Its compass
  // heading is offset so switching on doesn't jump: you keep facing where you were, and
  // dragging empty space adds to the offset.

  bool motion, motionSync;
  float motionYawOffset, smoothYaw, smoothPitch;
  public static float motionSmoothing = 18f; // per second; higher follows the phone more tightly

  void ToggleMotion() {
    if (!SystemInfo.supportsGyroscope) { SetLabel(4, "NO GYRO"); return; }
    motion = !motion;
    Input.gyro.enabled = motion;
    motionSync = true;
    SetLabel(4, motion ? "MOTION: ON" : "MOTION");
    Debug.Log("[PhoneMode] motion look " + (motion ? "on (turn the phone to look around)" : "off"));
  }

  void SetLabel(int i, string s) {
    if (buttons[i] == null) return;
    Text t = buttons[i].GetComponentInChildren<Text>();
    if (t != null) t.text = s;
    Image img = buttons[i].GetComponent<Image>();
    if (img != null && i == 4) img.color = motion ? new Color(.1f, .45f, .55f, .7f) : new Color(0f, .15f, .2f, .55f);
    if (img != null && i == 5) img.color = watching ? new Color(.1f, .45f, .55f, .7f) : new Color(0f, .15f, .2f, .55f);
  }

  // where the back of the phone points, as yaw / pitch (degrees, pitch + = down)
  static bool PhoneLook(out float yaw, out float pitch) {
    yaw = pitch = 0;
    Quaternion q = Input.gyro.attitude; // device -> world, Android axes (x east, y north, z up)
    if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) return false; // no reading yet
    Vector3 back = q * new Vector3(0, 0, -1);  // out of the back of the phone
    Vector3 d = new Vector3(back.x, back.z, back.y); // -> Unity (x east, y up, z north)
    if (d.sqrMagnitude < 1e-6f) return false;
    d.Normalize();
    yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    pitch = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
    return true;
  }

  void Motion() {
    NetSpectator ns = NetSpectator.instance;
    if (!motion || ns == null) return;
    if (ns.Flying) { motionSync = true; return; } // a double-tap fly: pick up from where it ends
    if (!PhoneLook(out float gy, out float gp)) return;
    if (motionSync) {
      motionYawOffset = Mathf.DeltaAngle(gy, ns.Yaw);
      smoothYaw = gy; smoothPitch = gp;
      motionSync = false;
    }
    float k = 1f - Mathf.Exp(-motionSmoothing * Time.unscaledDeltaTime);
    smoothYaw += Mathf.DeltaAngle(smoothYaw, gy) * k;
    smoothPitch += (gp - smoothPitch) * k;
    ns.SetLook(smoothYaw + motionYawOffset, smoothPitch);
  }

  void OnApplicationPause(bool paused) { if (!paused && motion) motionSync = true; }

  // Tap yourself on the map (its middle): level the view, stop any fly or coast and let
  // go of anything held - for when you've lost your bearings or your hand.
  void Recover() {
    NetSpectator ns = NetSpectator.instance;
    if (ns != null) { ns.StopFlying(); ns.SetLook(ns.Yaw, 0); }
    coasting = false; lookVel = Vector2.zero;
    PointerInput.tHeld = false; releaseAt = -1; mode = Mode.None;
    PointerInput.tPos = new Vector2(Screen.width * .5f, Screen.height * .5f); // the hand back to the middle
    motionSync = true;
    NetNotice.Show("VIEW LEVELLED", "", 0, 1.5f);
    Debug.Log("[PhoneMode] map: tapped yourself - view levelled, hand back in the middle");
  }

  // ---- WATCH: a camera device's broadcast, full screen ----
  //
  // A camera device's BROADCAST turns on its full-screen camera (realCam), which a PC
  // shows over its own view. The Quest switches that camera off (nobody would see it),
  // and so does the phone until WATCH is on: then whichever camera is broadcasting fills
  // the screen and the on-screen controls hide, except BACK.

  bool watching;
  cameraDeviceInterface[] cams = new cameraDeviceInterface[0];
  float camsAge = 99;

  Camera BroadcastCamera() {
    camsAge += Time.unscaledDeltaTime;
    if (camsAge > .5f) { camsAge = 0; cams = FindObjectsOfType<cameraDeviceInterface>(); }
    foreach (cameraDeviceInterface c in cams) {
      if (c == null || c.realCam == null || !c.realCam.activeInHierarchy) continue;
      Camera cam = c.realCam.GetComponent<Camera>();
      if (cam != null) return cam;
    }
    return null;
  }

  void ToggleWatch() {
    if (!watching && BroadcastCamera() == null) {
      NetNotice.Show("NOTHING IS BROADCASTING", "PRESS BROADCAST ON A CAMERA DEVICE", 0, 3f);
      return;
    }
    SetWatching(!watching);
  }

  void SetWatching(bool on) {
    watching = on;
    for (int i = 0; i < buttons.Length; i++) if (buttons[i] != null && i != 5) buttons[i].gameObject.SetActive(!on);
    SetLabel(5, on ? "BACK" : "WATCH");
    if (!on) foreach (cameraDeviceInterface c in cams) {
      Camera cam = c != null && c.realCam != null ? c.realCam.GetComponent<Camera>() : null;
      if (cam != null) cam.enabled = false;
    }
    Debug.Log("[PhoneMode] " + (on ? "watching the broadcast" : "back to the interactive view"));
  }

  void Watch() {
    if (!watching) return; // the broadcast camera stays off on the phone unless watching (as on the Quest)
    Camera bc = BroadcastCamera();
    if (bc == null) {
      SetWatching(false);
      NetNotice.Show("THE BROADCAST ENDED", "", 0, 3f);
      return;
    }
    foreach (cameraDeviceInterface c in cams) {
      Camera cam = c != null && c.realCam != null ? c.realCam.GetComponent<Camera>() : null;
      if (cam != null) cam.enabled = cam == bc;
    }
  }

  // ---- MAP: north up (world +Z), centred on you ----

  public static float mapSize = 420f;     // canvas units (the shorter screen side is 1080)
  public static float mapMinRange = 1.5f, mapMaxRange = 12f; // metres from you to the map's edge

  RectTransform map, mapContent, me, fovL, fovR;
  readonly List<RectTransform> dots = new List<RectTransform>();
  readonly List<NetDevice> dotDevices = new List<NetDevice>();
  readonly List<RectTransform> playerDots = new List<RectTransform>();
  NetDevice[] devices = new NetDevice[0];
  float devicesAge = 99, range = 4f;

  // a tap anywhere on the map: the device whose square is nearest (within reach)
  class MapTap : MonoBehaviour, IPointerClickHandler {
    public void OnPointerClick(PointerEventData e) { if (instance != null) instance.MapTapAt(e.position); }
  }

  public static float mapReach = 70f; // canvas units from a square
  void MapTapAt(Vector2 screen) {
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapContent, screen, null, out Vector2 local)) return;
    NetDevice best = null; float bestD = mapReach;
    for (int i = 0; i < dots.Count; i++) {
      if (!dots[i].gameObject.activeSelf || dotDevices[i] == null) continue;
      float d = Vector2.Distance(local, dots[i].anchoredPosition);
      if (d < bestD) { bestD = d; best = dotDevices[i]; }
    }
    if (best == null) {
      if (local.magnitude < mapReach) Recover(); // your own dot, in the middle: a way back
      return;
    }
    if (DesktopMode.FocusDevice(best, out Vector3 c, out float r, out Vector3 v)) {
      FlyToView(c, r, v);
      Debug.Log("[PhoneMode] map: flying to " + best.name);
    }
  }

  // The map shows when wanted, but never while a finger is using or dragging something:
  // it sits over the top-left corner, and the hand (or a held cable end) dragged under
  // it would be hidden, with every touch there going to the map instead.
  bool mapWanted;
  void ToggleMap() {
    if (map == null) MakeMap();
    mapWanted = !mapWanted;
  }
  bool MapShown { get { return mapWanted && !watching && mode != Mode.Interact && !DesktopMode.Holding; } }

  RectTransform Box(Transform parent, string name, Vector2 size, Color c, bool raycast = false) {
    GameObject g = new GameObject(name, typeof(RectTransform), typeof(Image));
    g.transform.SetParent(parent, false);
    RectTransform rt = g.GetComponent<RectTransform>();
    rt.sizeDelta = size;
    Image img = g.GetComponent<Image>();
    img.color = c;
    img.raycastTarget = raycast;
    return rt;
  }

  void MakeMap() {
    Transform canvas = buttons[0].transform.parent;
    map = Box(canvas, "Map", new Vector2(mapSize, mapSize), new Color(0f, .1f, .14f, .6f), true);
    map.gameObject.AddComponent<MapTap>();
    map.anchorMin = map.anchorMax = new Vector2(0, 1);
    map.pivot = new Vector2(0, 1);
    map.anchoredPosition = new Vector2(40, -40);
    map.gameObject.AddComponent<RectMask2D>();
    mapContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
    mapContent.SetParent(map, false);
    mapContent.anchorMin = mapContent.anchorMax = new Vector2(.5f, .5f);
    mapContent.sizeDelta = Vector2.zero;
    Text n = new GameObject("N", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
    n.transform.SetParent(map, false);
    RectTransform nr = n.GetComponent<RectTransform>();
    nr.anchorMin = nr.anchorMax = new Vector2(.5f, 1); nr.pivot = new Vector2(.5f, 1);
    nr.sizeDelta = new Vector2(60, 50); nr.anchoredPosition = new Vector2(0, -4);
    n.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    n.text = "N"; n.fontSize = 36; n.alignment = TextAnchor.UpperCenter; n.color = new Color(.6f, 1f, 1f, .9f); n.raycastTarget = false;
    // you: a dot, and two lines for the edges of what the screen shows
    fovL = Box(mapContent, "ViewLeft", new Vector2(3, mapSize * .5f), new Color(.6f, 1f, 1f, .7f));
    fovR = Box(mapContent, "ViewRight", new Vector2(3, mapSize * .5f), new Color(.6f, 1f, 1f, .7f));
    fovL.pivot = fovR.pivot = new Vector2(.5f, 0);
    me = Box(mapContent, "You", new Vector2(22, 22), new Color(.6f, 1f, 1f, 1f));
    map.gameObject.SetActive(false);
  }

  void UpdateMap() {
    if (map == null) return;
    bool show = MapShown;
    if (map.gameObject.activeSelf != show) map.gameObject.SetActive(show);
    if (!show) return;
    Camera cam = Camera.main;
    if (cam == null) return;
    devicesAge += Time.unscaledDeltaTime;
    if (devicesAge > .5f) { devicesAge = 0; devices = FindObjectsOfType<NetDevice>(); }

    Vector3 c = cam.transform.position;
    // range: just enough to show every device (smoothed)
    float far = mapMinRange;
    foreach (NetDevice d in devices) if (d != null) far = Mathf.Max(far, Flat(d.transform.position - c).magnitude * 1.15f);
    range = Mathf.Lerp(range, Mathf.Min(far, mapMaxRange), Time.unscaledDeltaTime * 3f);
    float scale = mapSize * .5f / range;

    // you and the view
    float yaw = cam.transform.eulerAngles.y;
    float halfH = Mathf.Atan(Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
    me.anchoredPosition = Vector2.zero;
    fovL.localEulerAngles = new Vector3(0, 0, -(yaw - halfH));
    fovR.localEulerAngles = new Vector3(0, 0, -(yaw + halfH));

    // devices (tap one to fly to it)
    int n = 0;
    foreach (NetDevice d in devices) {
      if (d == null) continue;
      if (n == dots.Count) {
        RectTransform dot = Box(mapContent, "Device", new Vector2(30, 30), new Color(1f, .75f, .3f, .9f));
        dots.Add(dot); dotDevices.Add(null);
      }
      RectTransform r = dots[n];
      r.gameObject.SetActive(true);
      dotDevices[n] = d;
      r.anchoredPosition = Flat(d.transform.position - c) * scale;
      r.localEulerAngles = new Vector3(0, 0, -d.transform.eulerAngles.y);
      n++;
    }
    for (int i = n; i < dots.Count; i++) dots[i].gameObject.SetActive(false);

    // the other players
    int m = 0;
    foreach (NetPlayer p in NetPlayer.all) {
      if (p == null || p.isLocalPlayer || p.head == null) continue;
      if (m == playerDots.Count) playerDots.Add(Box(mapContent, "Player", new Vector2(24, 24), Color.white));
      RectTransform r = playerDots[m];
      r.gameObject.SetActive(true);
      r.GetComponent<Image>().color = p.color;
      r.anchoredPosition = Flat(p.head.position - c) * scale;
      r.localEulerAngles = new Vector3(0, 0, 45f - p.head.eulerAngles.y);
      m++;
    }
    for (int i = m; i < playerDots.Count; i++) playerDots[i].gameObject.SetActive(false);
  }

  static Vector2 Flat(Vector3 v) { return new Vector2(v.x, v.z); }

  public static float twistGain = 1f; // degrees of turn per degree of twist

  // A look drag coasts on a little after the finger lifts, slowing to a stop.
  public static float coastDamping = 6f;     // per second: higher stops sooner
  public static float coastMaxDegPerSec = 240f;
  Vector2 lookVel;   // degrees per second (yaw, pitch) of the drag, smoothed
  bool coasting;

  void Coast() {
    if (!coasting || NetSpectator.instance == null) return;
    float dt = Time.unscaledDeltaTime;
    Vector2 step = lookVel * dt;
    if (motion) motionYawOffset += step.x; else NetSpectator.instance.Look(step.x, step.y);
    lookVel *= Mathf.Exp(-coastDamping * dt);
    if (lookVel.magnitude < 3f) { coasting = false; lookVel = Vector2.zero; }
  }

  // the angle of the line between two fingers, the same whichever finger is first
  // (the touch order can change between frames)
  static float TwistAngle(Finger a, Finger b) {
    if (a.id > b.id) { Finger t = a; a = b; b = t; }
    Vector2 v = b.pos - a.pos;
    return Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
  }

  void ReadFingers() {
    cur.Clear();
    EventSystem es = EventSystem.current;
    if (Application.isEditor && Input.touchCount == 0) { EditorFingers(es); return; }
    for (int i = 0; i < Input.touchCount; i++) {
      Touch t = Input.GetTouch(i);
      if (t.phase == TouchPhase.Began && es != null && es.IsPointerOverGameObject(t.fingerId)) onUI.Add(t.fingerId);
      bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
      if (onUI.Contains(t.fingerId)) { if (ended) onUI.Remove(t.fingerId); continue; }
      if (ended) continue;
      cur.Add(new Finger { id = t.fingerId, pos = t.position });
    }
  }

  // Editor: left button = one finger, right button = two fingers moving together,
  // wheel = pinch (forward / back, or the held thing nearer / further)
  bool mouseOnUI;
  void EditorFingers(EventSystem es) {
    Vector2 m = Input.mousePosition;
    if (Input.GetMouseButtonDown(0)) mouseOnUI = es != null && es.IsPointerOverGameObject();
    if (Input.GetMouseButton(0) && !mouseOnUI) cur.Add(new Finger { id = 0, pos = m });
    else if (Input.GetMouseButton(1)) {
      cur.Add(new Finger { id = 0, pos = m });
      cur.Add(new Finger { id = 1, pos = m + new Vector2(Screen.height * .2f, 0) });
    }
    float w = Input.mouseScrollDelta.y;
    if (w != 0) {
      if (DesktopMode.Holding) PointerInput.tScroll = w;
      else if (NetSpectator.instance != null) NetSpectator.instance.Fly(new Vector3(0, 0, w * .1f));
    }
  }
}
