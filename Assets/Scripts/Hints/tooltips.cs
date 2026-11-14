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

public class tooltips : manipObject {
  public GameObject vidPlayerPrefab, vidContainer;
  public string vidFile;

  Material mat;
  Color normalColor = new Color(.5f, .5f, 1);
  Color normalOnColor = new Color(.5f, .5f, 1);
  Color selectColor = new Color(.85f, .85f, 1);
  Color grabColor = new Color(.85f, .85f, 1);

  public bool tipOn = false;
  bool tipLoaded = false;

  public override void Awake() {
    base.Awake();
    normalColor = Color.HSVToRGB(.6f, 1f, .06f);
    normalOnColor = Color.HSVToRGB(.6f, .95f, .2f);
    selectColor = Color.HSVToRGB(.6f, .95f, .5f);
    grabColor = Color.HSVToRGB(.6f, .9f, 1f);

    mat = GetComponent<Renderer>().material;
    mat.SetColor("_TintColor", normalColor);

    registry.Add(this);
    shown = true; // so the first SetShown(false) below really hides it
    SetShown(false);
    nextCheck = 0; // let the next check decide which "?" of this type to show
  }

  // Only one "?" per device type is shown: the one on the device of that type nearest
  // to you (2026-11; it used to be whichever was spawned first). Rechecked twice a
  // second by whichever tooltip updates first; a "?" whose video is playing keeps it,
  // and a nearer one has to be 0.25 m closer to take over, so it doesn't flicker.
  static readonly System.Collections.Generic.List<tooltips> registry = new System.Collections.Generic.List<tooltips>();
  static readonly System.Collections.Generic.Dictionary<string, tooltips> current = new System.Collections.Generic.Dictionary<string, tooltips>();
  static float nextCheck = 0;
  const float checkInterval = 0.5f;
  const float switchMargin = 0.25f;
  bool shown = false;

  void Update() {
    if (Time.unscaledTime < nextCheck) return;
    nextCheck = Time.unscaledTime + checkInterval;
    PickNearest();
  }

  static void PickNearest() {
    masterControl mc = masterControl.instance;
    bool on = mc != null && mc.tooltipsOn;
    Camera cam = Camera.main;
    Vector3 head = cam != null ? cam.transform.position : Vector3.zero;

    var best = new System.Collections.Generic.Dictionary<string, tooltips>();
    var bestDist = new System.Collections.Generic.Dictionary<string, float>();
    for (int i = registry.Count - 1; i >= 0; i--) {
      tooltips t = registry[i];
      if (t == null) { registry.RemoveAt(i); continue; }
      if (!on || !t.gameObject.activeInHierarchy) continue;
      string key = t.vidFile ?? "";
      float d = Vector3.Distance(head, t.transform.position);
      if (t.tipOn) d = -1; // playing: keep it
      else if (current.TryGetValue(key, out tooltips cur) && cur == t) d -= switchMargin;
      if (!bestDist.ContainsKey(key) || d < bestDist[key]) { bestDist[key] = d; best[key] = t; }
    }
    current.Clear();
    foreach (var kv in best) current[kv.Key] = kv.Value;
    foreach (tooltips t in registry) {
      bool show = on && t.gameObject.activeInHierarchy && best.TryGetValue(t.vidFile ?? "", out tooltips b) && b == t;
      t.SetShown(show);
    }
  }

  void SetShown(bool on) {
    if (shown == on) return;
    shown = on;
    GetComponent<Renderer>().enabled = on;
    GetComponent<Collider>().enabled = on;
    if (!on && tipOn) ToggleVideo(false);
  }

  void OnDestroy() {
    registry.Remove(this);
    nextCheck = 0; // another device of this type picks up the "?"
  }

  public void ShowTooltips(bool on) {
    // tooltips switched on/off for everything (masterControl): recheck which to show
    if (!on) SetShown(false);
    nextCheck = 0;
  }

  public void ToggleVideo(bool on) {
    tipOn = on;

    vidContainer.SetActive(on);
    if (tipOn && !tipLoaded) {
      tipLoaded = true;
      GameObject g = Instantiate(vidPlayerPrefab, vidContainer.transform, false);
      g.transform.localPosition = Vector3.zero;
      g.transform.localRotation = Quaternion.identity;
      g.transform.localScale = Vector3.one;
    }

    if (tipOn) vidContainer.GetComponentInChildren<VideoPlayerDeviceInterface>().Autoplay(vidFile, this);
  }

  public override void setState(manipState state) {
    if (curState == state) return;

    curState = state;

    if (curState == manipState.none) {
      mat.SetColor("_TintColor", normalColor);
    } else if (curState == manipState.selected) {
      mat.SetColor("_TintColor", selectColor);
    } else if (curState == manipState.grabbed) {
      mat.SetColor("_TintColor", grabColor);
      ToggleVideo(!tipOn);
    }
  }
}
