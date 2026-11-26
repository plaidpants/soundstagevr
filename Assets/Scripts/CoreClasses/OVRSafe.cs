// Is the Meta runtime plugin (OVRPlugin's native library) there? (2026-11)
// A Windows build made without the Oculus XR plug-in for desktop has no OVRPlugin.dll:
// every OVRInput / OVRPlugin call then throws DllNotFoundException - every frame, in
// the manipulators too, which stopped the mouse hand's clicks half-way. Code that reads
// controllers or hands checks this first and acts as if nothing is pressed.

using UnityEngine;

public static class OVRSafe {
  static int state; // 0 unknown, 1 there, 2 missing
  public static bool Available {
    get {
      if (state == 0) {
        if (NoVR.On) state = 2; // started with -novr: leave the headset runtime alone
        else if (Phone.Active) state = 2; // a phone: no headset runtime to ask
        else try { var v = OVRPlugin.version; state = v != null ? 1 : 2; }
        catch (System.Exception) { state = 2; }
        if (state == 2) Debug.Log("[OVRSafe] no OVRPlugin library: controllers and hands are off (desktop mode)");
      }
      return state == 1;
    }
  }
}
