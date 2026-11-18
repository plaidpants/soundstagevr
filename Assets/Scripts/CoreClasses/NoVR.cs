// "-novr" on the command line: start the Windows app without VR, in desktop mode
// (mouse hand, keyboard camera), even with a headset runtime installed - for when the
// VR setup is broken or busy (2026-11).
//
//   SoundStage.exe -novr
//
// Unity only lets the editor change "Initialize XR on Startup", so this clears the
// setting in the loaded XR settings before Unity reads it (SubsystemRegistration runs
// before XR starts up). OVRSafe then also reports the Meta runtime as unavailable, so
// nothing reaches for controllers or hands. Without the option nothing changes.

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Management;

public static class NoVR {
  public static bool On { get; private set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void Check() {
    On = false;
    foreach (string a in Environment.GetCommandLineArgs())
      if (string.Equals(a, "-novr", StringComparison.OrdinalIgnoreCase)) On = true;
    if (!On) return;
    FieldInfo f = typeof(XRGeneralSettings).GetField("m_InitManagerOnStart", BindingFlags.Instance | BindingFlags.NonPublic);
    int n = 0;
    foreach (XRGeneralSettings s in Resources.FindObjectsOfTypeAll<XRGeneralSettings>()) {
      if (f != null) { f.SetValue(s, false); n++; }
    }
    Debug.Log("[NoVR] -novr: VR won't start" + (n == 0 ? " (couldn't find the XR settings - VR may start anyway)" : "") + "; desktop mode");
  }
}
