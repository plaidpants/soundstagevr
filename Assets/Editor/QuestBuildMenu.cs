// Quest build menu (2026-11-23): SoundStage > Build Quest ...
//
// Development builds with the fast IL2CPP settings (Debug C++, smaller/faster-to-build
// code), and the release build with the project's own settings - without changing
// ProjectSettings: the dev settings are set for the build and put back afterwards, so
// they can't end up in a commit or in a store build.
//
//   Build Quest (dev, fast) + Run    a full development APK, installed and started
//   Patch Quest (dev, fast) + Run    only what changed since the last dev build (the dev
//                                    APK from "Build Quest (dev)" must be on the headset)
//   Build Quest (release)            the store APK, project settings untouched
//
//   Build Phone (dev) + Run          the phone version (Desktop/PhoneMode.cs) on an Android
//   Build Phone (release)            phone over USB: the same project built without VR, with
//                                    SOUNDSTAGE_PHONE defined, as its own app
//                                    (com.HardLightLabsLLC.SoundStagePhone), landscape. The
//                                    Oculus loader, package name and orientation are set for
//                                    the build and put back afterwards (PhoneBuild.cs).
//
// Output: BuildQuest/soundstage-dev.apk and BuildQuest/soundstage.apk;
//         BuildPhone/soundstage-phone-dev.apk and BuildPhone/soundstage-phone.apk.
// The Editor must already be on Android (File > Build Settings): switching platform
// reimports the whole project, so it's never done from here.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class QuestBuildMenu {
  const string DevApk = "BuildQuest/soundstage-dev.apk";
  const string ReleaseApk = "BuildQuest/soundstage.apk";

  [MenuItem("SoundStage/Build Quest (dev, fast) + Run", false, 100)]
  static void BuildDev() { Dev(false); }

  [MenuItem("SoundStage/Patch Quest (dev, fast) + Run", false, 101)]
  static void PatchDev() { Dev(true); }

  [MenuItem("SoundStage/Build Quest (release)", false, 120)]
  static void BuildRelease() {
    if (!OnAndroid()) return;
    if (!EditorUtility.DisplayDialog("Build Quest (release)",
          "Build the store APK with the project's settings?\n\n" +
          "Signed with: " + KeyDescription() + "\n" +
          "IL2CPP: " + PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.Android) + ", " +
          PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Android) + "\n" +
          "Output: " + ReleaseApk, "Build", "Cancel")) return;
    Build(ReleaseApk, BuildOptions.None, "release");
  }

  static void Dev(bool patch) {
    if (!OnAndroid()) return;
    if (patch && !File.Exists(DevApk)) {
      EditorUtility.DisplayDialog("Patch Quest", "There's no dev build yet: use Build Quest (dev, fast) first, " +
        "and have that APK installed on the headset.", "OK");
      return;
    }
    var target = NamedBuildTarget.Android;
    Il2CppCompilerConfiguration oldConfig = PlayerSettings.GetIl2CppCompilerConfiguration(target);
    Il2CppCodeGeneration oldCodeGen = PlayerSettings.GetIl2CppCodeGeneration(target);
    try {
      PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Debug);
      PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize); // "Faster (smaller) builds"
      BuildOptions o = BuildOptions.Development | BuildOptions.AutoRunPlayer;
      if (patch) o |= BuildOptions.PatchPackage;
      Build(DevApk, o, patch ? "dev patch" : "dev");
    } finally {
      // put the project's own settings back, whatever happened
      PlayerSettings.SetIl2CppCompilerConfiguration(target, oldConfig);
      PlayerSettings.SetIl2CppCodeGeneration(target, oldCodeGen);
    }
  }

  static void Build(string path, BuildOptions options, string kind, string[] defines = null) {
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    bool oldBundle = EditorUserBuildSettings.buildAppBundle;
    EditorUserBuildSettings.buildAppBundle = false; // an APK, for the headset and for Meta
    try {
      var bpo = new BuildPlayerOptions {
        scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
        locationPathName = path,
        target = BuildTarget.Android,
        targetGroup = BuildTargetGroup.Android,
        options = options,
        extraScriptingDefines = defines,
      };
      float start = (float)EditorApplication.timeSinceStartup;
      BuildReport r = BuildPipeline.BuildPlayer(bpo);
      float secs = (float)EditorApplication.timeSinceStartup - start;
      BuildSummary s = r.summary;
      string msg = "[QuestBuild] " + kind + " build " + s.result + " in " + secs.ToString("F0") + " s: " + path +
                   (s.result == BuildResult.Succeeded ? " (" + (s.totalSize / (1024 * 1024)) + " MB)" : "") +
                   ", " + s.totalErrors + " error(s), " + s.totalWarnings + " warning(s); signed with " + KeyDescription();
      if (s.result == BuildResult.Succeeded) Debug.Log(msg); else Debug.LogError(msg);
    } finally {
      EditorUserBuildSettings.buildAppBundle = oldBundle;
    }
  }

  // ---- phone ----

  const string PhoneDevApk = "BuildPhone/soundstage-phone-dev.apk";
  const string PhoneApk = "BuildPhone/soundstage-phone.apk";

  [MenuItem("SoundStage/Build Phone (dev) + Run", false, 140)]
  static void BuildPhoneDev() { BuildPhone(true); }

  [MenuItem("SoundStage/Build Phone (release)", false, 141)]
  static void BuildPhoneRelease() {
    if (!OnAndroid()) return;
    if (!EditorUtility.DisplayDialog("Build Phone (release)",
          "Build the phone APK (no VR, touch controls)?\n\nSigned with: " + KeyDescription() + "\nOutput: " + PhoneApk, "Build", "Cancel")) return;
    BuildPhone(false);
  }

  static void BuildPhone(bool dev) {
    if (!OnAndroid()) return;
    var target = NamedBuildTarget.Android;
    Il2CppCompilerConfiguration oldConfig = PlayerSettings.GetIl2CppCompilerConfiguration(target);
    Il2CppCodeGeneration oldCodeGen = PlayerSettings.GetIl2CppCodeGeneration(target);
    using (new PhoneBuild.Settings()) {
      try {
        BuildOptions o = BuildOptions.None;
        if (dev) {
          PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Debug);
          PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
          o = BuildOptions.Development | BuildOptions.AutoRunPlayer;
        }
        Build(dev ? PhoneDevApk : PhoneApk, o, dev ? "phone dev" : "phone release", new[] { "SOUNDSTAGE_PHONE" });
      } finally {
        PlayerSettings.SetIl2CppCompilerConfiguration(target, oldConfig);
        PlayerSettings.SetIl2CppCodeGeneration(target, oldCodeGen);
      }
    }
  }

  static bool OnAndroid() {
    if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return true;
    EditorUtility.DisplayDialog("Build Quest", "The Editor is on " + EditorUserBuildSettings.activeBuildTarget +
      ". Switch to Android in File > Build Settings first (it reimports the project, so this menu won't do it).", "OK");
    return false;
  }

  static string KeyDescription() {
    return PlayerSettings.Android.useCustomKeystore
      ? "keystore " + Path.GetFileName(PlayerSettings.Android.keystoreName) + " (alias " + PlayerSettings.Android.keyaliasName + ")"
      : "the Android debug key (.android/debug.keystore)";
  }
}
