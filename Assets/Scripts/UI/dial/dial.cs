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
    WarmPopup();
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

  float curRot = 0;
  float realRot = 0f;
  float prevShakeRot = 0f;
  // Turning (2026-11-22): the hand's turn since the last frame moves the dial (twice as
  // far, as before), and the value stops at 0% and 100%. It used to map the hand's angle
  // straight onto the dial, so turning on past either end jumped to the other end and
  // carried on round.
  float lastHandAngle = 0f;
  float HandAngle(Transform t) {
    Vector2 temp = dialCoordinates(t.right);
    return Vector2.Angle(temp, Vector2.up) * Mathf.Sign(temp.x);
  }

  public override void grabUpdate(Transform t) {
    float hand = HandAngle(t);
    float d = Mathf.DeltaAngle(lastHandAngle, hand);
    lastHandAngle = hand;
    float p = Mathf.Clamp01(percent + d * 2f / 300f); // 300 degrees of dial = 0..100%
    if (p != percent) setPercent(p);

    if (Mathf.Abs(percent * 300f - prevShakeRot) > 10f) {
      if (manipulatorObjScript != null) manipulatorObjScript.hapticPulse(500);
      prevShakeRot = percent * 300f;
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Instantiate(Resources.Load("Hints/TurnVignette", typeof(GameObject)), transform.parent, false);
        Debug.Log("[dial] first-dial turn hint shown (" + sw.ElapsedMilliseconds + " ms to load)"); // (2026-11: chasing a hitch on the first dial grab)
      }

      yield return null;
    }
    yield return null;
  }

  // ---- value popup (2026-11) ----
  // While a player holds the dial, its value floats above it, facing them. A device can
  // say what the value means (the oscillator shows Hz and the note); otherwise it's 0-100.
  public System.Func<float, string> valueText;
  TextMesh popup;

  void ShowPopup(bool on) {
    if (!on) {
      if (popup != null) Destroy(popup.gameObject);
      popup = null;
      return;
    }
    if (popup != null) return;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // look like the device's own text labels (the dial's "Label" is a picture, not text)
    deviceInterface dev = GetComponentInParent<deviceInterface>();
    TextMesh src = dev != null ? dev.GetComponentInChildren<TextMesh>(true) : null;
    if (src == null) return;
    long tFind = sw.ElapsedMilliseconds;
    // A copy of the device's own label, not a new TextMesh: on the PC AddComponent<TextMesh>
    // took 0.7-11 s the first time (a new TextMesh starts with Unity's default font, which
    // Windows has to look up among the system fonts).
    // (Only a plain label is copied - one with scripts on it would run them - otherwise a
    // new TextMesh as before.)
    bool plain = true;
    foreach (Component c in src.GetComponents<Component>())
      if (!(c is Transform || c is TextMesh || c is MeshRenderer || c is MeshFilter)) plain = false;
    GameObject g;
    if (plain) {
      g = Instantiate(src.gameObject);
      g.transform.SetParent(null, false);
      for (int i = g.transform.childCount - 1; i >= 0; i--) DestroyImmediate(g.transform.GetChild(i).gameObject);
      g.SetActive(true);
      popup = g.GetComponent<TextMesh>();
    } else {
      g = new GameObject();
      popup = g.AddComponent<TextMesh>();
    }
    g.name = "dialValuePopup";
    long tMake = sw.ElapsedMilliseconds;
    popup.font = src.font;
    long tFont = sw.ElapsedMilliseconds;
    popup.fontSize = src.fontSize;
    popup.characterSize = src.characterSize;
    popup.anchor = TextAnchor.LowerCenter;
    popup.alignment = TextAlignment.Center;
    popup.richText = false;
    g.GetComponent<MeshRenderer>().sharedMaterial = src.GetComponent<Renderer>().sharedMaterial;
    Vector3 s = src.transform.lossyScale;
    g.transform.localScale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) * 1.3f;
    long tStyle = sw.ElapsedMilliseconds;
    UpdatePopup();
    if (sw.ElapsedMilliseconds > 30) Debug.Log("[dial] value popup took " + sw.ElapsedMilliseconds + " ms to make (find " + tFind + ", object " + (tMake - tFind) + ", font " + (tFont - tMake) + ", style " + (tStyle - tFont) + ", text " + (sw.ElapsedMilliseconds - tStyle) + "; font " + (src.font != null ? src.font.name + (src.font.dynamic ? " dynamic" : "") : "none") + ")");
  }

  void UpdatePopup() {
    if (popup == null) return;
    string t = null;
    if (valueText != null) { try { t = valueText(percent); } catch (System.Exception) { } }
    popup.text = t ?? Mathf.RoundToInt(percent * 100).ToString();
    Transform lt = transform.parent.Find("Label");
    float size = lt != null ? Mathf.Abs(lt.lossyScale.y) : .01f;
    Vector3 pos = transform.position + transform.parent.up * (size * 4f + .02f);
    popup.transform.position = pos;
    Camera cam = Camera.main;
    if (cam != null) popup.transform.rotation = Quaternion.LookRotation(pos - cam.transform.position, cam.transform.up);
  }

  // The first popup of a session took ~0.7 s to make on the PC (a hitch on the first dial
  // grab). The first dial that starts makes one and throws it away (LateUpdate removes it,
  // as nobody holds the dial), so that cost lands while the app loads.
  static bool popupWarmed;
  void WarmPopup() {
    if (popupWarmed || GetComponentInParent<deviceInterface>() == null) return;
    popupWarmed = true;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    ShowPopup(true);
    if (popup != null) popup.text = "0123456789.,-+# HZ ABCDEFG"; // every character a popup shows
    Debug.Log("[dial] value popup warmed up at start (" + sw.ElapsedMilliseconds + " ms)");
  }

  void LateUpdate() {
    if (popup == null) return;
    if (curState != manipState.grabbed) { ShowPopup(false); return; }
    UpdatePopup();
  }

  void OnDisable() { ShowPopup(false); }
  void OnDestroy() { ShowPopup(false); }

  public override void setState(manipState state) {
    curState = state;
    setGlowState(state);
    ShowPopup(state == manipState.grabbed);

    if (curState == manipState.grabbed) {
      turnCount = 0;
      if (!masterControl.instance.dialUsed) {
        if (_dialCheckRoutine != null) StopCoroutine(_dialCheckRoutine);
        _dialCheckRoutine = StartCoroutine(dialCheckRoutine());
      }
      lastHandAngle = HandAngle(manipulatorObj);
      prevShakeRot = percent * 300f;
    }
  }
}
