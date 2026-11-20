// Snap turn (left/right) and teleport (push forward, release to jump) on the right
// thumbstick (2026-11).
//
// Flick the right stick left or right to turn the view by snapAngle degrees around your
// head. Push and hold the right stick forward to aim a teleport arc onto the floor; let
// the stick return to centre to teleport there (or pull back / grab something to cancel).
// Only reacts when the right hand isn't holding or pointing at anything (devices use the
// stick while selected, e.g. the touchpad / timeline) and not during controller
// calibration. Off in hands mode (no stick). PlayerPrefs "snapTurn" (1 = on, default).

using UnityEngine;

public class SnapTurn : MonoBehaviour {
  public static float snapAngle = 30f;
  public static float flickThreshold = 0.75f; // stick x past this turns
  public static float rearmThreshold = 0.3f;  // stick must come back inside this before the next turn

  // Teleport settings
  public static float teleportAimThreshold = 0.65f;
  public static float teleportReleaseThreshold = 0.25f;
  public static float teleportCancelY = -0.35f;
  public static float teleportArcSpeed = 8.0f;
  public static float teleportGravity = 9.8f;
  public static float maxStageDistance = 12f;

  static SnapTurn instance;
  OVRCameraRig rig;
  ArtificialVRMovement walker;
  Transform moveRoot;
  manipulator rightHand;
  bool armed = true;

  bool teleportAiming = false;
  bool teleportValid = false;
  Vector3 teleportTarget;
  Vector3 stageHome;
  bool stageHomeSet = false;

  LineRenderer arcRenderer;
  GameObject targetMarker;
  const int ArcSegments = 28;
  readonly Vector3[] arcPoints = new Vector3[ArcSegments];

  public static bool Enabled {
    get { return PlayerPrefs.GetInt("snapTurn", 1) == 1; }
    set { PlayerPrefs.SetInt("snapTurn", value ? 1 : 0); }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Bootstrap() {
    if (instance != null) return;
    GameObject go = new GameObject("SnapTurn");
    DontDestroyOnLoad(go);
    instance = go.AddComponent<SnapTurn>();
  }

  void EnsureTeleportVisuals() {
    if (arcRenderer != null) return;

    Shader sh = Shader.Find("Sonic Ether/Particles/Additive");
    if (sh == null) sh = Shader.Find("Sprites/Default");
    Color glowColor = new Color(0.15f, 0.55f, 0.95f, 0.8f);

    GameObject arcObj = new GameObject("TeleportArc");
    arcObj.transform.SetParent(transform, false);
    arcRenderer = arcObj.AddComponent<LineRenderer>();
    arcRenderer.useWorldSpace = true;
    arcRenderer.startWidth = 0.012f;
    arcRenderer.endWidth = 0.018f;
    arcRenderer.positionCount = 0;
    arcRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    arcRenderer.receiveShadows = false;
    Material arcMat = new Material(sh);
    arcMat.SetColor("_TintColor", glowColor);
    arcMat.SetFloat("_EmissionGain", 0.45f);
    arcRenderer.sharedMaterial = arcMat;
    arcObj.SetActive(false);

    targetMarker = new GameObject("TeleportTarget");
    targetMarker.transform.SetParent(transform, false);
    LineRenderer ring = targetMarker.AddComponent<LineRenderer>();
    ring.useWorldSpace = false;
    ring.loop = true;
    ring.startWidth = 0.025f;
    ring.endWidth = 0.025f;
    ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    ring.receiveShadows = false;
    ring.sharedMaterial = arcMat;
    const int ringPts = 32;
    const float radius = 0.25f;
    ring.positionCount = ringPts;
    for (int i = 0; i < ringPts; i++) {
      float a = (i / (float)ringPts) * Mathf.PI * 2f;
      ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.01f, Mathf.Sin(a) * radius));
    }
    targetMarker.SetActive(false);
  }

  void CancelTeleport() {
    teleportAiming = false;
    teleportValid = false;
    if (arcRenderer != null) arcRenderer.gameObject.SetActive(false);
    if (targetMarker != null) targetMarker.SetActive(false);
  }

  void UpdateTeleportArc() {
    EnsureTeleportVisuals();
    float floorY = moveRoot.position.y;
    Vector3 start = rightHand.GrabPointWorld;
    Vector3 dir = rightHand.transform.forward;
    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
    dir.Normalize();

    Vector3 v0 = dir * teleportArcSpeed;
    const float dt = 0.08f;
    const float maxTime = (ArcSegments - 1) * dt;

    // Solve start.y + v0.y * t - 0.5 * g * t^2 = floorY for the positive landing time tHit
    float a = 0.5f * teleportGravity;
    float b = -v0.y;
    float c = floorY - start.y;
    float disc = b * b - 4f * a * c;
    if (disc < 0f) {
      teleportValid = false;
      arcRenderer.gameObject.SetActive(false);
      targetMarker.SetActive(false);
      return;
    }
    float tHit = (-b + Mathf.Sqrt(disc)) / (2f * a);
    if (tHit <= 0.02f || tHit > maxTime) {
      teleportValid = false;
      arcRenderer.gameObject.SetActive(false);
      targetMarker.SetActive(false);
      return;
    }

    Vector3 hitPos = new Vector3(start.x + v0.x * tHit, floorY, start.z + v0.z * tHit);

    // Match ArtificialVRMovement's walking bounds: ArtificialVRMovement moves moveRoot
    // (the parent of OVRCameraRig) and clamps (moveRoot.position - home) horizontally to
    // maxDistance. Since teleporting moves moveRoot by (hitPos - head.position), the new
    // moveRoot position is within maxLimit of home iff hitPos is within maxLimit of boundCenter.
    Vector3 home = walker != null ? walker.Home : stageHome;
    float maxLimit = walker != null ? walker.maxDistance : maxStageDistance;
    Transform head = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main.transform;
    Vector2 boundCenter = new Vector2(
      home.x + (head.position.x - moveRoot.position.x),
      home.z + (head.position.z - moveRoot.position.z)
    );
    Vector2 start2D = new Vector2(start.x, start.z);
    Vector2 hit2D = new Vector2(hitPos.x, hitPos.z);
    Vector2 toHit = hit2D - boundCenter;

    if (toHit.sqrMagnitude > maxLimit * maxLimit) {
      Vector2 rayVec = hit2D - start2D;
      float rayLen = rayVec.magnitude;
      Vector2 oc = start2D - boundCenter;
      if (rayLen > 1e-4f && oc.sqrMagnitude <= maxLimit * maxLimit) {
        // Intersect the forward aiming ray with the boundary circle so the target stays
        // in the exact direction the controller is pointing and never folds back.
        Vector2 rayDir = rayVec / rayLen;
        float qb = Vector2.Dot(oc, rayDir);
        float qc = oc.sqrMagnitude - maxLimit * maxLimit;
        float s = Mathf.Clamp(-qb + Mathf.Sqrt(Mathf.Max(0f, qb * qb - qc)), 0f, rayLen);
        hit2D = start2D + rayDir * s;
      } else {
        hit2D = boundCenter + toHit.normalized * maxLimit;
      }
      hitPos = new Vector3(hit2D.x, floorY, hit2D.y);
    }

    // Build monotonically advancing arc points from start to hitPos
    for (int i = 0; i < ArcSegments; i++) {
      float u = i / (float)(ArcSegments - 1);
      float t = u * tHit;
      float y = start.y + v0.y * t - 0.5f * teleportGravity * (t * t);
      if (i == ArcSegments - 1) y = floorY;
      arcPoints[i] = new Vector3(
        Mathf.Lerp(start.x, hitPos.x, u),
        y,
        Mathf.Lerp(start.z, hitPos.z, u)
      );
    }

    teleportValid = true;
    teleportTarget = hitPos;

    arcRenderer.gameObject.SetActive(true);
    arcRenderer.positionCount = ArcSegments;
    arcRenderer.SetPositions(arcPoints);

    targetMarker.SetActive(true);
    targetMarker.transform.position = hitPos;
  }

  void Update() {
    if (!Enabled || platformSetup.calibrating || NetRoom.StickBlocked) {
      if (teleportAiming) CancelTeleport();
      return;
    }
    if (rig == null) {
      rig = FindObjectOfType<OVRCameraRig>();
      if (rig == null) return;
      walker = rig.GetComponentInParent<ArtificialVRMovement>();
      if (walker == null) walker = FindObjectOfType<ArtificialVRMovement>();
      moveRoot = walker != null ? walker.transform : rig.transform;
    }
    if (!stageHomeSet) {
      stageHome = walker != null ? walker.Home : moveRoot.position;
      stageHomeSet = true;
    }
    if (rightHand == null) {
      foreach (manipulator m in FindObjectsOfType<manipulator>()) if (!m.IsLeftHand) rightHand = m;
    }

    if (!OVRSafe.Available || rightHand == null || rightHand.IsHandsMode || rightHand.IsBusy) {
      if (teleportAiming) CancelTeleport();
      return;
    }

    Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

    // Teleport on right stick forward
    if (teleportAiming) {
      if (stick.y < teleportCancelY) {
        CancelTeleport();
        return;
      }
      if (stick.y < teleportReleaseThreshold && Mathf.Abs(stick.x) < rearmThreshold) {
        if (teleportValid) {
          Transform head = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main.transform;
          Vector3 delta = teleportTarget - head.position;
          Vector3 p = moveRoot.position + new Vector3(delta.x, 0f, delta.z);
          Vector3 home = walker != null ? walker.Home : stageHome;
          float maxLimit = walker != null ? walker.maxDistance : maxStageDistance;
          Vector3 off = p - home;
          Vector3 flat = new Vector3(off.x, 0f, off.z);
          if (flat.magnitude > maxLimit) {
            flat = flat.normalized * maxLimit;
            p = new Vector3(home.x + flat.x, p.y, home.z + flat.z);
          }
          moveRoot.position = p;
          rightHand.hapticPulse(1000);
        }
        CancelTeleport();
        return;
      }
      UpdateTeleportArc();
      return;
    } else if (stick.y >= teleportAimThreshold && stick.y > Mathf.Abs(stick.x) * 1.2f) {
      teleportAiming = true;
      UpdateTeleportArc();
      return;
    }

    // Snap turn on right stick left / right
    float x = stick.x;
    if (Mathf.Abs(x) < rearmThreshold) { armed = true; return; }
    if (!armed || Mathf.Abs(x) < flickThreshold) return;

    armed = false;
    Transform headAnchor = rig.centerEyeAnchor != null ? rig.centerEyeAnchor : Camera.main.transform;
    moveRoot.RotateAround(headAnchor.position, Vector3.up, Mathf.Sign(x) * snapAngle);
  }
}
