// Pose prediction for things other players move (2026-11).
//
// A pose from another headset is already some tens of milliseconds old when it gets
// here (the trip, plus up to one send interval). Instead of showing it late, the
// predictor carries it forward along its recent velocity (and spin) to where it should
// be *now* - like timewarp, but for positions. Each pose is stamped with the network
// time on the headset that sent it, so how far to carry it is known.
//
// To keep it from going wrong:
// - velocity only (acceleration from 20 poses a second is mostly noise);
// - at most maxLead ahead, and none once nothing new has come for a while;
// - a change of direction takes the new velocity at once (no overshooting a stop);
// - when a new pose shows the prediction was off, the difference is faded out
//   over ~70 ms instead of jumping (and without adding lag to steady movement).

using Mirror;
using UnityEngine;

public class PosePredictor {
  public static bool enabled = true;
  public static float maxLead = .1f;       // seconds, position
  // Spin is predicted much less: a fast hand wave spins the wrist hard and reverses
  // quickly, and carrying that forward 100 ms could turn a hand right round.
  public static float maxRotLead = .05f;   // seconds
  public static float maxRotAngle = 20f;   // degrees, however fast it's turning
  public static float stale = .35f;        // no pose for this long: hold still
  public static float velSmoothing = .6f;  // share of each new velocity sample taken
  public static float fade = 15f;          // how fast a correction fades (1/s)
  public static float snapDistance = .5f;  // a jump bigger than this (m) is shown at once

  Vector3 pos, vel, angVel; // angVel: axis * rad/s
  Quaternion rot = Quaternion.identity;
  double time;
  bool have, stopped;

  Vector3 posOffset;
  Quaternion rotOffset = Quaternion.identity;

  public bool Has { get { return have; } }
  public static double Now { get { return NetworkTime.time; } }

  // a new pose, sampled at sender network time t
  public void Feed(Vector3 p, Quaternion r, double t, bool moving = true) {
    if (have && t < time) return; // older than what we have (unreliable can reorder)
    double now = Now;
    Vector3 shownPos = Vector3.zero; Quaternion shownRot = Quaternion.identity;
    if (have) Shown(now, out shownPos, out shownRot);

    if (have && !stopped) {
      double dt = t - time;
      if (dt > 1e-3 && dt < .5) {
        Vector3 v = (p - pos) / (float)dt;
        // reversing (or stopping hard): take the new velocity as it is
        vel = Vector3.Dot(v, vel) < 0 ? v : Vector3.Lerp(vel, v, velSmoothing);
        Quaternion d = r * Quaternion.Inverse(rot);
        d.ToAngleAxis(out float ang, out Vector3 axis);
        if (ang > 180) ang -= 360;
        Vector3 w = float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero : axis * (ang * Mathf.Deg2Rad / (float)dt);
        angVel = Vector3.Dot(w, angVel) < 0 ? w : Vector3.Lerp(angVel, w, velSmoothing);
      } else if (dt >= .5) { vel = Vector3.zero; angVel = Vector3.zero; }
    } else { vel = Vector3.zero; angVel = Vector3.zero; }

    bool first = !have;
    pos = p; rot = r; time = t; have = true;
    stopped = !moving;
    if (stopped) { vel = Vector3.zero; angVel = Vector3.zero; }
    if (!enabled) { vel = Vector3.zero; angVel = Vector3.zero; }

    // keep what's shown where it was; the difference fades out
    Predict(now, out Vector3 np, out Quaternion nr);
    if (first || (shownPos - np).magnitude > snapDistance) { posOffset = Vector3.zero; rotOffset = Quaternion.identity; }
    else { posOffset = shownPos - np; rotOffset = shownRot * Quaternion.Inverse(nr); }
  }

  public void Predict(double now, out Vector3 p, out Quaternion r) {
    float age = (float)(now - time);
    float lead = age > stale ? 0 : Mathf.Clamp(age, 0, maxLead);
    p = pos + vel * lead;
    float w = angVel.magnitude;
    float rotLead = Mathf.Min(lead, maxRotLead);
    float angle = Mathf.Min(w * rotLead * Mathf.Rad2Deg, maxRotAngle);
    r = w > 1e-5f ? Quaternion.AngleAxis(angle, angVel / w) * rot : rot;
  }

  void Shown(double now, out Vector3 p, out Quaternion r) {
    Predict(now, out p, out r);
    p += posOffset;
    r = rotOffset * r;
  }

  // call once a frame: fades the correction and gives the pose to show
  public void Step(float dt, out Vector3 p, out Quaternion r) {
    float k = Mathf.Exp(-fade * dt);
    posOffset *= k;
    rotOffset = Quaternion.Slerp(Quaternion.identity, rotOffset, k);
    Shown(Now, out p, out r);
  }

  // at rest and nothing left to fade
  public bool Settled {
    get { return stopped && posOffset.sqrMagnitude < 1e-8f && Quaternion.Angle(rotOffset, Quaternion.identity) < .01f; }
  }

  public void Apply(Transform t, float dt) {
    Step(dt, out Vector3 p, out Quaternion r);
    t.SetPositionAndRotation(p, r);
  }

  public void Reset() { have = false; vel = angVel = posOffset = Vector3.zero; rotOffset = Quaternion.identity; }
}
