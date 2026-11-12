// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class drumpad : MonoBehaviour {
  public Material onMat;
  Renderer rend;
  Material offMat;
  Material glowMat;
  deviceInterface _deviceInterface;
  public Transform stickTip;

  Color glowColor = Color.HSVToRGB(.4f, .5f, .1f);
  void Awake() {
    _deviceInterface = transform.parent.GetComponent<deviceInterface>();
    rend = GetComponent<Renderer>();
    offMat = rend.material;
    glowMat = new Material(onMat);
    glowMat.SetColor("_TintColor", glowColor);
  }

  void Update() {
    if (Input.GetKeyDown(KeyCode.Space)) {
      keyHit(true);
    }
    HandHitUpdate();
  }

  // Hit speed (m/s) for full velocity; slower hits are quieter, down to minVelocity.
  public static float fullVelocitySpeed = 4.0f; // was 2.5: hard hits saturated too early
  public static float minVelocity = 0.15f;
  public static float VelocityFromSpeed(float metersPerSecond) {
    return Mathf.Clamp(metersPerSecond / fullVelocitySpeed, minVelocity, 1f);
  }

  // Hands: slap the pad with fingertips or palm. Same test as the drumsticks: a point
  // inside the pad's radius crossing its surface downwards. One hit per hand per 0.12 s
  // so a whole-hand slap counts once; a hand holding a stick lets the stick do it.
  readonly Dictionary<Transform, Vector3> lastHandPos = new Dictionary<Transform, Vector3>();
  readonly float[] handCooldownUntil = new float[2];
  void HandHitUpdate() {
    if (HandFingertips.active.Count == 0 || !HandInput.HandsActive()) {
      if (lastHandPos.Count > 0) lastHandPos.Clear();
      return;
    }
    Transform space = transform.parent;
    float scaleY = space.lossyScale.y;
    float dt = Mathf.Max(Time.deltaTime, 0.001f);
    for (int h = 0; h < HandFingertips.active.Count; h++) {
      HandFingertips hand = HandFingertips.active[h];
      int side = hand.isLeft ? 0 : 1;
      bool skip = (hand.owner != null && hand.owner.holdingTool) || Time.time < handCooldownUntil[side];
      IList<Transform> points = hand.HitPoints;
      for (int p = 0; p < points.Count; p++) {
        Transform t = points[p];
        if (t == null) continue;
        Vector3 pos = space.InverseTransformPoint(t.position);
        Vector3 last;
        bool hadLast = lastHandPos.TryGetValue(t, out last);
        lastHandPos[t] = pos;
        if (skip || !hadLast) continue;
        if (new Vector2(pos.x, pos.z).magnitude >= .175f) continue;
        if (last.y > -.004f && pos.y <= -.004f) {
          float speed = (last.y - pos.y) * scaleY / dt;
          keyHit(true, VelocityFromSpeed(speed));
          handCooldownUntil[side] = Time.time + 0.12f;
          skip = true; // keep updating the other points' positions
        }
      }
    }
  }

  IEnumerator offRoutine() {
    yield return new WaitForSeconds(0.1f);
    _deviceInterface.hit(false);
    rend.material = offMat;
  }

  Coroutine offCoroutine;
  public bool isHit = false;
  public void keyHit(bool on, float velocity = 1f) {
    isHit = on;
    if (on) {
      drumDeviceInterface drum = _deviceInterface as drumDeviceInterface;
      if (drum != null) drum.hitWithVelocity(on, velocity);
      else _deviceInterface.hit(on);
      rend.material = glowMat;
      if (offCoroutine != null) StopCoroutine(offRoutine());
      offCoroutine = StartCoroutine(offRoutine());
    }
  }
}
