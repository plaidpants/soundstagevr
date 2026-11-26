// The phone build (2026-11-25): see QuestBuildMenu (Build Phone ...) and
// Scripts/Desktop/PhoneMode.cs.
//
// Settings: for the length of the build, the Oculus XR loader is taken off Android (no VR
// start-up, no Meta manifest entries from the XR plug-in), the app gets its own package
// name (so it sits beside the Quest app and is never mistaken for it) and turns with
// the phone (landscape both ways, and portrait). Everything is put back afterwards, so
// ProjectSettings and the XR settings stay as the Quest build needs them.
//
// Manifest: Plugins/Android/AndroidManifest.xml is the Quest's. In a phone build the
// generated Gradle project's manifests lose the VR parts: the VR launcher category, the
// head-tracking requirement (a phone has none), the Quest-only meta-data and hand
// tracking, and "exclude from recents".
//
// SoundStage > Phone Mode in Editor: runs the phone's touch controls in Play mode
// (mouse as the finger), for trying them without a build.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

public static class PhoneBuild {
  public static bool active; // a phone build is running
  const string OculusLoader = "Unity.XR.Oculus.OculusLoader";
  const string PhoneSuffix = "Phone";

  public class Settings : IDisposable {
    readonly string oldId;
    readonly UIOrientation oldOrientation;
    readonly bool oldPL, oldPR, oldLL, oldLR;
    readonly bool loaderRemoved;
    readonly UnityEngine.Object[] oldPreloaded;
    readonly bool oldOverrideId;

    public Settings() {
      var target = NamedBuildTarget.Android;
      oldId = PlayerSettings.GetApplicationIdentifier(target);
      oldOrientation = PlayerSettings.defaultInterfaceOrientation;
      oldPL = PlayerSettings.allowedAutorotateToPortrait;
      oldPR = PlayerSettings.allowedAutorotateToPortraitUpsideDown;
      oldLL = PlayerSettings.allowedAutorotateToLandscapeLeft;
      oldLR = PlayerSettings.allowedAutorotateToLandscapeRight;
      oldPreloaded = PlayerSettings.GetPreloadedAssets();  // taking the loader off drops its settings from these
      oldOverrideId = OverrideId();                         // setting the package name turns this on

      if (!oldId.EndsWith(PhoneSuffix)) PlayerSettings.SetApplicationIdentifier(target, oldId + PhoneSuffix);
      PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
      PlayerSettings.allowedAutorotateToPortrait = true;
      PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
      PlayerSettings.allowedAutorotateToLandscapeLeft = true;
      PlayerSettings.allowedAutorotateToLandscapeRight = true;
      loaderRemoved = SetOculusLoader(false);
      active = true;
      Debug.Log("[PhoneBuild] " + PlayerSettings.GetApplicationIdentifier(target) + ", landscape and portrait, " +
                (loaderRemoved ? "Oculus loader off for this build" : "no Oculus loader was on"));
    }

    public void Dispose() {
      active = false;
      var target = NamedBuildTarget.Android;
      PlayerSettings.SetApplicationIdentifier(target, oldId);
      PlayerSettings.defaultInterfaceOrientation = oldOrientation;
      PlayerSettings.allowedAutorotateToPortrait = oldPL;
      PlayerSettings.allowedAutorotateToPortraitUpsideDown = oldPR;
      PlayerSettings.allowedAutorotateToLandscapeLeft = oldLL;
      PlayerSettings.allowedAutorotateToLandscapeRight = oldLR;
      if (loaderRemoved) SetOculusLoader(true);
      PlayerSettings.SetPreloadedAssets(oldPreloaded);
      OverrideId(oldOverrideId);
      AssetDatabase.SaveAssets();
      Debug.Log("[PhoneBuild] Quest settings back: " + oldId + (loaderRemoved ? ", Oculus loader on" : ""));
    }
  }

  // ProjectSettings' overrideDefaultApplicationIdentifier (no PlayerSettings API for it)
  static SerializedObject PlayerSettingsObject() {
    UnityEngine.Object ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
    return ps != null ? new SerializedObject(ps) : null;
  }
  static bool OverrideId() {
    SerializedProperty p = PlayerSettingsObject()?.FindProperty("overrideDefaultApplicationIdentifier");
    return p != null && p.boolValue;
  }
  static void OverrideId(bool on) {
    SerializedObject so = PlayerSettingsObject();
    SerializedProperty p = so?.FindProperty("overrideDefaultApplicationIdentifier");
    if (p == null || p.boolValue == on) return;
    p.boolValue = on;
    so.ApplyModifiedPropertiesWithoutUndo();
  }

  // on: put the Oculus loader (back) on Android; off: take it off. Returns whether it changed.
  static bool SetOculusLoader(bool on) {
    XRGeneralSettings_Get(out var manager);
    if (manager == null) { Debug.LogWarning("[PhoneBuild] no XR settings for Android"); return false; }
    bool has = manager.activeLoaders.Any(l => l != null && l.GetType().FullName == OculusLoader);
    if (on == has) return false;
    bool ok = on ? XRPackageMetadataStore.AssignLoader(manager, OculusLoader, BuildTargetGroup.Android)
                 : XRPackageMetadataStore.RemoveLoader(manager, OculusLoader, BuildTargetGroup.Android);
    if (!ok) Debug.LogWarning("[PhoneBuild] couldn't " + (on ? "put back" : "take off") + " the Oculus loader");
    return ok;
  }

  static void XRGeneralSettings_Get(out UnityEngine.XR.Management.XRManagerSettings manager) {
    var s = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
    manager = s != null ? s.Manager : null;
  }

  // ---- the generated manifests ----

  public class Manifest : IPostGenerateGradleAndroidProject {
    public int callbackOrder { get { return 10000; } } // after the Meta / Oculus processors
    public void OnPostGenerateGradleAndroidProject(string path) {
      if (!active) return;
      foreach (string m in new[] {
        Path.Combine(path, "src", "main", "AndroidManifest.xml"),
        Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml") }) {
        if (File.Exists(m)) Strip(m);
      }
    }

    static readonly string[] removeLinesWith = {
      "com.oculus.intent.category.VR",
      "android.hardware.vr.headtracking",
      "com.samsung.android.vr.application.mode",
      "com.oculus.supportedDevices",
      "com.oculus.vr.focusaware",
      "com.oculus.handtracking",
      "oculus.software.handtracking",
      "com.oculus.permission.HAND_TRACKING",
    };

    static void Strip(string file) {
      string text = File.ReadAllText(file);
      int before = text.Length;
      // whole elements that mention a VR-only name (they're one tag each)
      foreach (string key in removeLinesWith)
        text = Regex.Replace(text, @"[ \t]*<[^<>]*" + Regex.Escape(key) + @"[^<>]*/>[ \t]*\r?\n?", "");
      text = text.Replace("android:excludeFromRecents=\"true\"", "android:excludeFromRecents=\"false\"");
      text = Regex.Replace(text, "android:screenOrientation=\"[^\"]*\"", "android:screenOrientation=\"fullUser\""); // turns with the phone, unless rotation is locked
      File.WriteAllText(file, text);
      Debug.Log("[PhoneBuild] " + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file)))) +
                " manifest: VR entries removed (" + (before - text.Length) + " characters)");
    }
  }

  // ---- trying it in the Editor ----

  const string EditorPref = "phoneModeInEditor"; // read by Phone.Active (Scripts/Desktop/PhoneMode.cs)

  [MenuItem("SoundStage/Phone Mode in Editor", false, 160)]
  static void ToggleEditor() {
    PlayerPrefs.SetInt(EditorPref, PlayerPrefs.GetInt(EditorPref, 0) == 1 ? 0 : 1);
    PlayerPrefs.Save();
    Debug.Log("[PhoneBuild] Phone Mode in Editor " + (PlayerPrefs.GetInt(EditorPref) == 1 ? "on" : "off") + " (from the next Play)");
  }

  [MenuItem("SoundStage/Phone Mode in Editor", true)]
  static bool ToggleEditorCheck() {
    Menu.SetChecked("SoundStage/Phone Mode in Editor", PlayerPrefs.GetInt(EditorPref, 0) == 1);
    return true;
  }
}
