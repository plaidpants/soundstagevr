// Desktop version of SoundStage (2026-11).
//
// On a PC (editor or Windows build) two things come back from the original SteamVR
// version or are new:
//
// 1. The desktop companion UI (Resources/Desktop/DesktopUIfab: import samples / images,
//    open the recordings and saves folders, hide the environment, the desktop camera,
//    credits; the help videos button is removed - that playlist no longer exists). It slides in from the left when the mouse moves over the game window,
//    with or without a headset. The prefab had been taken out of the scene for Quest;
//    it's now created at start-up on Windows, and the links it had to scene objects
//    (masterControl, the ground plane) are made in code.
//
// 2. With no headset running: a mouse-and-keyboard player. The camera flies as in the
//    spectator (hold the right mouse button to look, W A S D move, Q / E down / up,
//    Shift faster) and the right controller follows the mouse, so everything that works
//    by hand works by mouse:
//      - point at something: it's highlighted (the controller tip is there)
//      - left button: the trigger - grab, press, plug cables; on keys it plays them
//        (hold and slide across keys for a run; Alt + click locks a xylophone key);
//        on a drum pad's centre it hits it (Shift: soft, Ctrl: hard; the rim moves the drum)
//      - while holding: move the mouse to drag it; mouse wheel = nearer / further
//      - holding a dial or knob: move the mouse left / right to turn it
//      - Tab: the menu (the A button), opened in front of the camera
//      - C: the B button (on a device's handle: copy it; on a note, event...: delete it)
//      - Delete: delete what the mouse points at or holds - a whole device, by its handle
//        (in VR that's dropping it in the menu's trash can)
//      - holding a cable end: point near a jack it fits and it plugs in (SnapJack)
//    The left controller is hidden. Other players see the mouse hand as a controller.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[DefaultExecutionOrder(-100)] // before the manipulators: Tab places the hand before they read it
public class DesktopMode : MonoBehaviour {
  public static DesktopMode instance;

  // PC with no headset: the mouse is the right hand
  public static bool MouseHand { get; private set; }

  // The left button as the right trigger. Pressing first pushes the hand into what the
  // mouse is over; the trigger only counts a moment later, once the hand has touched it
  // (things are picked by touching, and a trigger pulled before that grabs nothing).
  static float pressedAt = -1;
  public static bool Trigger {
    get { return pressedAt >= 0 && !keyPlaying && !drumClick && Time.unscaledTime - pressedAt > .06f; }
  }

  // Drum pads (2026-11-22): a click on a pad hits it - the sticks need a downward swing a
  // mouse can't give. Plain click: medium; Shift: soft; Ctrl: hard. Shared with the other
  // players like any drum hit. The click doesn't also grab what's under the pad.
  static bool drumClick;
  public static float drumClickVelocity = .75f, drumSoftVelocity = .4f, drumHardVelocity = 1f;

  // Keys (piano, xylophone) play by touch, not by the trigger: a click on a key
  // touches it for as long as the button is held, and sliding across keys with the
  // button held plays each in turn. Alt + click is the trigger instead, which locks a
  // xylophone key on (and off again).
  static bool keyPlaying;
  key touchedKey;

  public static bool IsWindows {
    get {
      return Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
    }
  }

  // controller buttons on the keyboard (names as QuestInput uses them; ...L = right controller)
  public static bool KeyFor(string name, out KeyCode k) {
    if (NetCodeDialog.Typing && name == "secondaryButtonL") { k = KeyCode.None; return true; } // typing a room code
    switch (name) {
      case "menuButtonL": k = KeyCode.Tab; return true;        // A: menu
      case "secondaryButtonL": k = KeyCode.C; return true; // B: copy (Delete deletes: DeleteUnderMouse)
    }
    k = KeyCode.None;
    return false;
  }

  // Development builds pop up Unity's red Development Console over the picture on any
  // error - on a PC without the Oculus runtime OVRManager logs one at start-up
  // (DllNotFoundException: OVRPlugin, harmless) - and the PC's window is what gets
  // broadcast. Switched off before the scene loads; everything still goes to Player.log.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void HideDevConsole() {
    if (Application.platform == RuntimePlatform.WindowsPlayer) Debug.developerConsoleEnabled = false;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!IsWindows && !Phone.Active) return; // the phone runs desktop mode by touch (PhoneMode)
    GameObject g = new GameObject("DesktopMode");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<DesktopMode>();
  }

  void Start() {
    if (IsWindows && !PointerInput.touch) CreateCompanionUI(); // folders, import...: PC only
    MouseHand = NetSpectator.Active;
    if (PointerInput.touch) { Debug.Log("[DesktopMode] phone: the touch pointer is the right hand"); return; }
    Debug.Log("[DesktopMode] desktop UI on" + (MouseHand ? "; no headset: mouse and keyboard (left button = trigger, right button = look, WASD/QE move, wheel = nearer/further, Tab = menu)" : ""));
  }

  // ---------- 1. the companion UI ----------

  void CreateCompanionUI() {
    GameObject prefab = Resources.Load<GameObject>("Desktop/DesktopUIfab");
    if (prefab == null) { Debug.LogWarning("[DesktopMode] Resources/Desktop/DesktopUIfab not found"); return; }
    GameObject ui = Instantiate(prefab);
    ui.name = "DesktopUI";
    DontDestroyOnLoad(ui);

    // events whose target was a scene object (lost when the UI became a prefab)
    foreach (Button b in ui.GetComponentsInChildren<Button>(true)) {
      // the help videos playlist is gone from YouTube (and opening it hung the app): no button
      bool videos = false;
      for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++) if (b.onClick.GetPersistentMethodName(i) == "openVideoTutorials") videos = true;
      if (videos) { b.gameObject.SetActive(false); continue; }
      Rewire(b.onClick);
    }
    foreach (Toggle t in ui.GetComponentsInChildren<Toggle>(true)) {
      for (int i = 0; i < t.onValueChanged.GetPersistentEventCount(); i++) {
        string m = t.onValueChanged.GetPersistentMethodName(i);
        if (m == "ToggleEnvironment") {
          // HIDE ENVIRONMENT -> the in-game ENVIRONMENT button: each click goes to the next
          // level (OFF / STARS / MOUNTAINS / FULL), the label says which
          t.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.Off);
          envToggle = t;
          envLabel = t.GetComponentInChildren<Text>(true);
          t.onValueChanged.AddListener(_ => { if (EnvironmentControl.instance != null) EnvironmentControl.instance.Cycle(); syncTimer = 1; });
        } else if (m == "MuteBackgroundSFX") {
          // MUTE ENVIRONMENT SFX is the in-game WIND switch (same setting): keep them showing the same
          t.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.Off);
          windToggle = t;
          if (masterControl.instance != null) masterControl.instance.muteEnvToggle = t;
          t.onValueChanged.AddListener(on => { if (EnvironmentControl.instance != null) EnvironmentControl.instance.SetWind(!on); syncTimer = 1; });
        }
      }
    }
    SyncSidebar();

    // The project's Input Manager has no Submit / Cancel / Horizontal / Vertical, which
    // the UI's input module asks for every frame (an exception each time): give it an
    // input that answers "nothing" for those - mouse clicks on the UI still work.
    foreach (UnityEngine.EventSystems.StandaloneInputModule im in ui.GetComponentsInChildren<UnityEngine.EventSystems.StandaloneInputModule>(true))
      im.inputOverride = im.gameObject.AddComponent<MouseOnlyInput>();

    DesktopCameraControl dc = ui.GetComponentInChildren<DesktopCameraControl>(true);
    if (dc != null && dc.groundPlane == null && imageLoad.instance != null) dc.groundPlane = imageLoad.instance.groundPlane;
  }

  static void Rewire(UnityEvent e) {
    for (int i = 0; i < e.GetPersistentEventCount(); i++) {
      if (e.GetPersistentTarget(i) != null) continue;
      string m = e.GetPersistentMethodName(i);
      masterControl mc = masterControl.instance;
      switch (m) {
        case "openRecordings": e.AddListener(() => mc.openRecordings()); break;
        case "openSavedScenes": e.AddListener(() => mc.openSavedScenes()); break;
      }
    }
  }

  // the sidebar shows what the in-game menu set (and the other way round)
  Toggle envToggle, windToggle;
  Text envLabel;
  float syncTimer;
  void SyncSidebar() {
    HideControllerHelp();
    EnvironmentControl env = EnvironmentControl.instance;
    if (env == null) return;
    if (envToggle != null) envToggle.SetIsOnWithoutNotify(env.level == 0);
    if (envLabel != null) envLabel.text = "ENVIRONMENT: " + EnvironmentControl.levelNames[env.level];
    if (windToggle != null) windToggle.SetIsOnWithoutNotify(!env.WindOn);
  }

  // The controller's help labels are unreadable on a mouse-driven hand, so in mouse
  // mode they stay hidden whatever the tips setting: the touchpad's button names, and
  // the "TRIGGER / pull to grab" and "MENU" tooltips (TriggerTooltipOculus,
  // MenuTooltipOculus... in Prefabs/player/ControllerNode).
  void HideControllerHelp() {
    if (!MouseHand) return;
    if (hand != null) {
      Transform node = hand.transform;
      while (node.parent != null && node.name != "ControllerNode") node = node.parent;
      foreach (Transform t in node.GetComponentsInChildren<Transform>(true)) {
        if (t.name.Contains("Tooltip") && t.gameObject.activeSelf) t.gameObject.SetActive(false);
      }
    }
    foreach (touchpad pad in FindObjectsOfType<touchpad>()) {
      if (pad.buttonContainers == null || pad.buttonContainers.Length == 0) continue;
      GameObject help = pad.buttonContainers[0];
      if (help != null && help.activeSelf) help.SetActive(false);
    }
  }

  void Update() {
    syncTimer += Time.unscaledDeltaTime;
    if (syncTimer > .5f) { syncTimer = 0; SyncSidebar(); }

    // Tab (the menu button): open the menu in front of the camera, not wherever the
    // mouse hand happens to be - the hand is moved there before the manipulator reads Tab
    if (MouseHand && hand != null && PointerInput.GetKeyDown(KeyCode.Delete) && !NetCodeDialog.Typing && !NetCodeDialog.IsOpen) DeleteUnderMouse();

    if (MouseHand && hand != null && PointerInput.GetKeyDown(KeyCode.Tab) && Camera.main != null) {
      Transform c = Camera.main.transform;
      Vector3 flatFwd = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized;
      if (flatFwd.sqrMagnitude < .01f) flatFwd = c.forward;
      MoveTipTo(c.position + flatFwd * .45f - Vector3.up * .2f, Quaternion.LookRotation(flatFwd, Vector3.up));
      menuOpenedAt = Time.unscaledTime;
    }
  }
  float menuOpenedAt = -10;

  // ---------- 2. the mouse hand ----------

  manipulator hand, otherHand;
  Transform handRoot;
  float depth = 1f;          // how far along the mouse ray the hand is while holding
  float twist;               // extra turn for dials, degrees
  Quaternion heldRotation;
  bool wasHolding;

  void LateUpdate() {
    if (!MouseHand) return;
    Camera cam = Camera.main;
    if (cam == null) return;
    if (hand == null) {
      foreach (manipulator m in FindObjectsOfType<manipulator>(true)) {
        if (m.ControllerIndex == 1) hand = m;
        else if (m.ControllerIndex == 0) otherHand = m;
      }
      if (hand == null) return;
      handRoot = hand.transform.parent;
      hand.pointerMode = true; // picked by the mouse ray, not by bumping into things
      if (otherHand != null && otherHand.transform.parent != null) otherHand.transform.parent.gameObject.SetActive(false);
    }
    if (!Application.isFocused) { TouchKey(null); keyPlaying = false; return; }
    PhoneBody(cam);
    if (Time.unscaledTime - menuOpenedAt < .4f) return; // the menu is opening from where the hand is

    // the desktop UI takes clicks over it
    bool overUI = PointerInput.OverUI;
    Ray ray = cam.ScreenPointToRay(PointerInput.Position);
    bool alt = PointerInput.Alt;
    if (PointerInput.Down && !overUI) {
      pressedAt = Time.unscaledTime;
      keyPlaying = !alt && hand.getSelection() == null && PickTarget(ray, out _, out _) is Transform t0 && t0.GetComponent<key>() != null;
      drumClick = false;
      if (!alt && !keyPlaying && hand.getSelection() == null && RaycastScene(ray, out RaycastHit dh)) {
        drumpad pad = DrumPadAt(dh);
        if (pad != null) {
          drumClick = true;
          bool soft = PointerInput.Shift;
          bool hard = PointerInput.Ctrl;
          pad.keyHit(true, soft ? drumSoftVelocity : hard ? drumHardVelocity : drumClickVelocity);
        }
      }
    }
    if (!PointerInput.Held) { pressedAt = -1; keyPlaying = false; drumClick = false; }
    bool pressing = pressedAt >= 0;
    bool holding = hand.getSelection() != null;
    Holding = holding;

    if (holding) {
      if (!wasHolding) { heldRotation = handRoot.rotation; twist = 0; depth = Vector3.Distance(ray.origin, hand.transform.position); }
      depth = Mathf.Clamp(depth * (1f + PointerInput.Scroll * .1f), .15f, 20f);
      manipObject held = hand.getSelection();
      if (held is dial) {
        // turn: mouse left / right twists the hand about the dial's axis, and the hand stays put
        twist += PointerInput.Delta.x * (PointerInput.touch ? 1600f / Screen.height : .8f); // phone: about a screen-height drag for a full turn
        Vector3 axis = held.transform.parent != null ? held.transform.parent.up : held.transform.up;
        handRoot.rotation = Quaternion.AngleAxis(-twist, axis) * heldRotation;
        wasHolding = true;
        return;
      }
      if (held is omniPlug plug) {
        // a cable end: near a jack it can go into (on screen), it jumps into it
        omniJack j = SnapJack(plug, cam);
        if (j != null) {
          Vector3 offset = plug.transform.position - hand.transform.position; // where the plug sits on the hand
          MoveTipTo(j.transform.position - offset, heldRotation);
          depth = Vector3.Distance(ray.origin, j.transform.position);
          wasHolding = true;
          return;
        }
      } else snapped = null;
      MoveTipTo(ray.GetPoint(depth), heldRotation);
    } else {
      snapped = null;
      // pointing: the control under the mouse is picked by the ray (small buttons win
      // over the case or handle they sit on); the hand just shows where
      Vector3 target;
      Transform picked = PickTarget(ray, out Vector3 point, out float dist);
      if (!pressing || keyPlaying) hand.pointerTarget = picked; // (keep what was clicked while the click lands)
      TouchKey(keyPlaying && picked != null ? picked.GetComponent<key>() : null);
      if (dist < float.MaxValue) {
        target = point - ray.direction * .01f;
        depth = dist;
      } else target = ray.GetPoint(Mathf.Clamp(depth, .3f, 3f));
      MoveTipTo(target, Quaternion.LookRotation(ray.direction, cam.transform.up));
    }
    wasHolding = holding;
  }

  public static bool Holding { get; private set; }

  // The phone (2026-11-25): the controller is only a pointer there, and close up its
  // body fills the screen and hides what it's doing. Within phoneBodyHideDistance of the
  // camera the body (controllerSimple) isn't drawn; the tip and its two prongs, which
  // are what actually grab and press, always are. Other players' view of this hand is
  // their own model, so it isn't affected.
  public static float phoneBodyHideDistance = .8f;
  Renderer[] bodyRenderers;
  bool[] bodyWasOn;
  bool bodyHidden;

  void PhoneBody(Camera cam) {
    if (!PointerInput.touch || hand == null) return;
    if (bodyRenderers == null) {
      Transform body = FindChild(hand.transform, "controllerSimple");
      bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
      bodyWasOn = new bool[bodyRenderers.Length];
    }
    bool hide = Vector3.Distance(cam.transform.position, hand.transform.position) < phoneBodyHideDistance;
    if (hide == bodyHidden) return;
    bodyHidden = hide;
    for (int i = 0; i < bodyRenderers.Length; i++) {
      Renderer r = bodyRenderers[i];
      if (r == null) continue;
      if (hide) { bodyWasOn[i] = r.enabled; r.enabled = false; }
      else r.enabled = bodyWasOn[i];
    }
  }

  static Transform FindChild(Transform t, string name) {
    foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
    return null;
  }

  // the phone (PhoneMode): would a finger put down here use a control (or grab a
  // handle, or hit a drum pad)? If not, its drag looks around instead.
  public bool WouldInteract(Vector2 screen) {
    Camera cam = Camera.main;
    if (cam == null || hand == null || handRoot == null) return false;
    Ray ray = cam.ScreenPointToRay(screen);
    if (PickTarget(ray, out _, out _) != null) return true;
    return RaycastScene(ray, out RaycastHit h) && DrumPadAt(h) != null;
  }

  // 0: nothing to use, 1: a handle (moves a whole device), 2: a control or a drum pad
  public int PickKind(Vector2 screen) {
    Camera cam = Camera.main;
    if (cam == null || hand == null || handRoot == null) return 0;
    Ray ray = cam.ScreenPointToRay(screen);
    Transform t = PickTarget(ray, out _, out _);
    if (t != null && !(t.GetComponent<manipObject>() is handle)) return 2;
    if (RaycastScene(ray, out RaycastHit h) && DrumPadAt(h) != null) return 2;
    return t != null ? 1 : 0;
  }

  // The phone's double-tap: what to fly to. A device (its NetDevice root): face on to
  // whichever of its sides you're looking at most - usually its front panel - framed
  // by its body (the handles you move it by; renderers can reach far out: cables,
  // tooltips). Anything else hit (the menu, a panel): that spot, seen face on.
  // radius: half the size across the view.
  public bool FocusTarget(Vector2 screen, out Vector3 centre, out float radius, out Vector3 viewDir) {
    centre = Vector3.zero; radius = .15f; viewDir = Vector3.forward;
    Camera cam = Camera.main;
    if (cam == null || handRoot == null) return false;
    Ray ray = cam.ScreenPointToRay(screen);
    if (!RaycastScene(ray, out RaycastHit h)) return false;
    NetDevice dev = h.collider.GetComponentInParent<NetDevice>();
    if (dev != null) return FocusDevice(dev, out centre, out radius, out viewDir);
    if (h.collider.GetComponentInParent<manipObject>() == null) return false; // the floor, the sky...
    centre = h.point;
    viewDir = Vector3.Dot(h.normal, ray.direction) < 0 ? -h.normal : ray.direction;
    return true;
  }

  public static bool FocusDevice(NetDevice dev, out Vector3 centre, out float radius, out Vector3 viewDir) {
    centre = dev.transform.position; radius = .15f; viewDir = Vector3.forward;
    Camera cam = Camera.main;
    if (cam == null) return false;
    // the body: the handles' colliders (what you grab it by)
    bool any = false;
    Bounds b = new Bounds(dev.transform.position, Vector3.zero);
    foreach (handle hd in dev.GetComponentsInChildren<handle>()) {
      foreach (Collider c in hd.GetComponents<Collider>()) {
        if (!c.enabled) continue;
        if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
      }
    }
    if (!any) {
      foreach (Collider c in dev.GetComponentsInChildren<Collider>()) {
        if (!c.enabled || c.GetComponentInParent<omniPlug>() != null) continue;
        if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
      }
    }
    centre = b.center;
    // face on: the device's own axis (+/- forward, up, right) closest to the way back to the camera
    Transform t = dev.transform;
    Vector3 toCam = (cam.transform.position - centre).normalized;
    Vector3[] axes = { t.forward, -t.forward, t.up, -t.up, t.right, -t.right };
    Vector3 face = axes[0]; float best = -2;
    foreach (Vector3 a in axes) { float d = Vector3.Dot(a, toCam); if (d > best) { best = d; face = a; } }
    viewDir = -face;
    // half its size across the view (the corners, flattened onto the view plane)
    float r = 0;
    Vector3 e = b.extents;
    for (int i = 0; i < 8; i++) {
      Vector3 corner = b.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
      r = Mathf.Max(r, Vector3.ProjectOnPlane(corner - b.center, viewDir).magnitude);
    }
    radius = Mathf.Clamp(r, .05f, .8f);
    return true;
  }

  // Delete (2026-11-22): it used to be the B button, which on a device's handle copies it.
  // Now it deletes: a device (pointed at or held by its handle) goes, as if dropped in the
  // trash can; anything else that B deletes (a timeline event, a note...) is deleted.
  void DeleteUnderMouse() {
    manipObject target = hand.getSelection();
    if (target == null && hand.pointerTarget != null) target = hand.pointerTarget.GetComponent<manipObject>();
    if (target == null) return;
    if (target is handle h && (h.masterObj != null || h.transform.parent != null)) {
      GameObject device = (h.masterObj != null ? h.masterObj : h.transform.parent).gameObject;
      if (hand.getSelection() != null) hand.ForceRelease();
      hand.pointerTarget = null;
      Debug.Log("[DesktopMode] Delete: " + device.name);
      Destroy(device);
    } else if (target.canBeDeleted) {
      target.selfdelete();
    }
  }

  // ---- plugging cables with the mouse (2026-11-22) ----
  // A plug connects by touching a jack, which with the mouse meant getting the hand to
  // exactly the jack's depth. Now, while a cable end is held, the jack it can go into
  // nearest the mouse pointer on screen (within snapPixels) pulls the plug into it; move
  // the mouse away to pull it out again, let go to leave it plugged in.
  public static float snapPixels = 40f;  // at 1080 lines; scaled with the window
  omniJack snapped;
  omniJack[] jacks = new omniJack[0];
  float jacksAge = 99;

  omniJack SnapJack(omniPlug plug, Camera cam) {
    jacksAge += Time.unscaledDeltaTime;
    if (jacksAge > .5f) { jacksAge = 0; jacks = FindObjectsOfType<omniJack>(); }
    float scale = Screen.height / 1080f * (PointerInput.touch ? 2f : 1f); // a fingertip is less exact
    Vector2 mouse = PointerInput.Position;
    omniJack best = null;
    float bestD = snapPixels * scale;
    foreach (omniJack j in jacks) {
      if (!CanTake(plug, j)) continue;
      Vector3 sp = cam.WorldToScreenPoint(j.transform.position);
      if (sp.z <= .05f) continue; // behind the camera
      float d = Vector2.Distance(mouse, sp);
      if (j == snapped) d *= .67f; // stay with the jack it's in until the mouse is clearly elsewhere
      if (d < bestD) { bestD = d; best = j; }
    }
    snapped = best;
    return best;
  }

  // the same rule as omniPlug.updateJackList, plus free (or this plug's) and showing
  static bool CanTake(omniPlug plug, omniJack j) {
    if (j == null || !j.isActiveAndEnabled) return false;
    if (j.outgoing == plug.outputPlug) return false;
    if (j.near != null && j.near != plug) return false;
    if (plug.otherPlug != null && plug.otherPlug.connected != null && plug.otherPlug.connected.transform.parent == j.transform.parent) return false;
    Collider c = j.GetComponent<Collider>();
    return c == null || c.enabled;
  }

  // the key the mouse is playing (null: none); touches go through TouchTracker like a
  // fingertip's, so a key held by a headset player at the same time isn't cut off
  void TouchKey(key k) {
    if (k == touchedKey) return;
    if (touchedKey != null) TouchTracker.Touch(touchedKey, false, null);
    touchedKey = k;
    if (k != null) TouchTracker.Touch(k, true, null);
  }

  void OnDisable() { TouchKey(null); }

  // What the mouse points at. Along the ray, controls (buttons, keys, dials, the MIDI
  // device list...) are preferred over handles (the parts that move a whole device):
  // a control up to 4 cm behind a handle's surface still wins, so flush buttons and
  // list entries floating over other things can be picked. Anything more than 4 cm
  // behind the first surface hit is out of reach.
  readonly System.Collections.Generic.List<RaycastHit> hits = new System.Collections.Generic.List<RaycastHit>();
  Transform PickTarget(Ray ray, out Vector3 point, out float dist) {
    hits.Clear();
    hits.AddRange(Physics.RaycastAll(ray, 30f, ~0, QueryTriggerInteraction.Collide));
    hits.Sort((a, b) => a.distance.CompareTo(b.distance));
    Transform handleT = null; Vector3 handleP = Vector3.zero; float handleD = 0;
    float first = float.MaxValue;
    point = Vector3.zero; dist = float.MaxValue;
    foreach (RaycastHit h in hits) {
      if (IsMine(h.collider)) continue;
      if (first == float.MaxValue) { first = h.distance; point = h.point; dist = h.distance; }
      if (h.distance > first + .04f) break;
      manipObject mo = h.collider.GetComponent<manipObject>();
      if (mo == null) continue; // a case, panel backing...: only blocks what's more than 4 cm behind it
      if (mo is handle) {
        if (handleT == null) { handleT = h.collider.transform; handleP = h.point; handleD = h.distance; }
        continue;
      }
      point = h.point; dist = h.distance;
      if (PointerInput.touch && mo is omniJack jk && jk.near != null && jk.near.connected == jk)
        return jk.near.transform; // phone: a jack with a plug in it - take the plug (a finger covers both)
      return h.collider.transform; // a control
    }
    if (handleT != null) { point = handleP; dist = handleD; }
    return handleT;
  }

  // The drum's pad has no collider of its own (sticks are tested by position): the drum's
  // body (its move handles) is what the mouse ray hits. A click on the top, inside the
  // pad's centre, is a hit; the outer ring and the sides still grab the drum to move it
  // (Alt + click grabs it anywhere).
  public static float drumClickRadius = .14f; // the pad itself is .175 (drumpad)
  static drumpad DrumPadAt(RaycastHit h) {
    drumpad direct = h.collider.GetComponentInParent<drumpad>();
    if (direct != null) return direct;
    drumDeviceInterface drum = h.collider.GetComponentInParent<drumDeviceInterface>();
    if (drum == null) return null;
    drumpad pad = drum.GetComponentInChildren<drumpad>();
    if (pad == null || pad.transform.parent == null) return null;
    Vector3 local = pad.transform.parent.InverseTransformPoint(h.point);
    if (new Vector2(local.x, local.z).magnitude > drumClickRadius) return null;
    if (local.y < -.015f) return null; // the side or underneath
    return pad;
  }

  // nearest hit that isn't one of our own hands
  bool RaycastScene(Ray ray, out RaycastHit best) {
    best = default(RaycastHit);
    float bestD = float.MaxValue;
    foreach (RaycastHit h in Physics.RaycastAll(ray, 30f, ~0, QueryTriggerInteraction.Collide)) {
      if (h.distance < bestD && !IsMine(h.collider)) { best = h; bestD = h.distance; }
    }
    return bestD < float.MaxValue;
  }

  bool IsMine(Collider c) {
    return c.transform.IsChildOf(handRoot) || (otherHand != null && c.transform.IsChildOf(otherHand.transform.parent));
  }

  // place the controller so its grab point (the manipulator) is at p
  void MoveTipTo(Vector3 p, Quaternion rot) {
    handRoot.rotation = rot;
    handRoot.position += p - hand.transform.position;
  }
}

// UI input without the Input Manager's named buttons and axes (see CreateCompanionUI)
public class MouseOnlyInput : UnityEngine.EventSystems.BaseInput {
  public override bool GetButtonDown(string buttonName) { return false; }
  public override float GetAxisRaw(string axisName) { return 0f; }
}
