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

public class dial : manipObject {

  public float percent = 0f;

  glowDisk dialFeedback;
  // Shared glow variants from MaterialCache (2026-11) instead of per-dial copies.
  Renderer[] glowRends;
  Material[] glowBases;

  public Color customColor;

  public bool externalHue = false;
  public float hue = .5f;

  GameObject littleDisk;

  public override void Awake() {
    base.Awake();
    littleDisk = transform.Find("littleDisk").gameObject;

    glowRends = new Renderer[3];
    glowRends[0] = littleDisk.GetComponent<Renderer>();
    glowRends[1] = transform.parent.Find("glowDisk").GetComponent<Renderer>();
    glowRends[2] = transform.parent.Find("Label").GetComponent<Renderer>();
    glowBases = new Material[3];
    for (int i = 0; i < 3; i++) glowBases[i] = glowRends[i].sharedMaterial;

    customColor = Color.HSVToRGB(hue, 227 / 255f, 206 / 255f);

    setGlowState(manipState.none);

    dialFeedback = transform.parent.Find("glowDisk").GetComponent<glowDisk>();
  }

  void setGlowState(manipState s) {
    Color c = customColor;

    switch (s) {
      case manipState.none:
        littleDisk.SetActive(false);

        setGlow(c, .1f);
        break;
      case manipState.selected:
        littleDisk.SetActive(true);
        setGlow(c, .3f);
        break;
      case manipState.grabbed:
        littleDisk.SetActive(true);

        setGlow(masterControl.instance.tipColor, .25f);
        break;
      default:
        break;
    }
  }


  void setGlow(Color c, float gain) {
    for (int i = 0; i < glowRends.Length; i++) {
      glowRends[i].sharedMaterial = MaterialCache.Glow(glowBases[i], c, gain);
    }
  }

  void Start() {
    setPercent(percent);
  }

  void Update() {
    updatePercent();
  }

  public void setPercent(float p) {
    percent = Mathf.Clamp01(p);
    if (p >= 0.5f) realRot = (percent - .5f) * 300;
    else realRot = percent * 300 + 210;

    curRot = realRot / 2f;
    transform.localRotation = Quaternion.Euler(0, realRot, 0);
  }

  void updatePercent() {
    if (realRot < 180) percent = .5f + realRot / 300;
    else percent = (realRot - 210) / 300;

    dialFeedback.percent = percent * 0.85f;
    dialFeedback.PercentUpdate();
  }

  Vector2 dialCoordinates(Vector3 vec) {
    Vector3 flat = transform.parent.InverseTransformDirection(Vector3.ProjectOnPlane(vec, transform.parent.up));
    return new Vector2(flat.x, flat.z);
  }

  float deltaRot = 0;
  float curRot = 0;
  float realRot = 0f;
  float prevShakeRot = 0f;
  public override void grabUpdate(Transform t) {
    Vector2 temp = dialCoordinates(t.right);
    curRot = Vector2.Angle(temp, Vector2.up) * Mathf.Sign(temp.x) - deltaRot;

    curRot = Mathf.Repeat(curRot, 360f);
    realRot = Mathf.Repeat(curRot * 2, 360f);

    if (realRot > 150 && realRot < 210) {
      if (realRot < 180) realRot = 150;
      else realRot = 210;
    }
    transform.localRotation = Quaternion.Euler(0, realRot, 0);

    if (Mathf.Abs(realRot - prevShakeRot) > 10f) {
      if (manipulatorObjScript != null) manipulatorObjScript.hapticPulse(500);
      prevShakeRot = realRot;
      turnCount++;
    }
  }

  int turnCount = 0;

  Coroutine _dialCheckRoutine;
  IEnumerator dialCheckRoutine() {
    Vector3 lastPos = Vector3.zero;
    float cumulative = 0;
    if (manipulatorObj != null) {
      lastPos = manipulatorObj.position;
    }

    while (curState == manipState.grabbed && manipulatorObj != null && !masterControl.instance.dialUsed) {
      cumulative += Vector3.Magnitude(manipulatorObj.position - lastPos);
      lastPos = manipulatorObj.position;

      if (turnCount > 3) masterControl.instance.dialUsed = true;
      else if (cumulative > .2f) {
        masterControl.instance.dialUsed = true;
        Instantiate(Resources.Load("Hints/TurnVignette", typeof(GameObject)), transform.parent, false);
      }

      yield return null;
    }
    yield return null;
  }

  public override void setState(manipState state) {
    curState = state;
    setGlowState(state);

    if (curState == manipState.grabbed) {
      turnCount = 0;
      if (!masterControl.instance.dialUsed) {
        if (_dialCheckRoutine != null) StopCoroutine(_dialCheckRoutine);
        _dialCheckRoutine = StartCoroutine(dialCheckRoutine());
      }
      Vector2 temp = dialCoordinates(manipulatorObj.right);
      deltaRot = Vector2.Angle(temp, Vector2.up) * Mathf.Sign(temp.x) - curRot;
    }
  }
}
