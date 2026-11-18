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
//        (hold and slide across keys for a run; Alt + click locks a xylophone key)
//      - while holding: move the mouse to drag it; mouse wheel = nearer / further
//      - holding a dial or knob: move the mouse left / right to turn it
//      - Tab: the menu (the A button), opened in front of the camera
//      - Delete: the B button
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
    get { return pressedAt >= 0 && !keyPlaying && Time.unscaledTime - pressedAt > .06f; }
  }

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
      case "secondaryButtonL": k = KeyCode.Delete; return true; // B
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
    if (!IsWindows) return;
    GameObject g = new GameObject("DesktopMode");
    DontDestroyOnLoad(g);
    instance = g.AddComponent<DesktopMode>();
  }

  void Start() {
    CreateCompanionUI();
    MouseHand = NetSpectator.Active;
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
    if (MouseHand && hand != null && Input.GetKeyDown(KeyCode.Tab) && Camera.main != null) {
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
    if (Time.unscaledTime - menuOpenedAt < .4f) return; // the menu is opening from where the hand is

    // the desktop UI takes clicks over it
    bool overUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
    bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    if (Input.GetMouseButtonDown(0) && !overUI) {
      pressedAt = Time.unscaledTime;
      keyPlaying = !alt && hand.getSelection() == null && PickTarget(ray, out _, out _) is Transform t0 && t0.GetComponent<key>() != null;
    }
    if (!Input.GetMouseButton(0)) { pressedAt = -1; keyPlaying = false; }
    bool pressing = pressedAt >= 0;
    bool holding = hand.getSelection() != null;
    Holding = holding;

    if (holding) {
      if (!wasHolding) { heldRotation = handRoot.rotation; twist = 0; depth = Vector3.Distance(ray.origin, hand.transform.position); }
      depth = Mathf.Clamp(depth * (1f + Input.mouseScrollDelta.y * .1f), .15f, 20f);
      manipObject held = hand.getSelection();
      if (held is dial) {
        // turn: mouse left / right twists the hand about the dial's axis, and the hand stays put
        twist += NetSpectator.MouseDelta.x * .8f;
        Vector3 axis = held.transform.parent != null ? held.transform.parent.up : held.transform.up;
        handRoot.rotation = Quaternion.AngleAxis(-twist, axis) * heldRotation;
        wasHolding = true;
        return;
      }
      MoveTipTo(ray.GetPoint(depth), heldRotation);
    } else {
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
      return h.collider.transform; // a control
    }
    if (handleT != null) { point = handleP; dist = handleD; }
    return handleT;
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
