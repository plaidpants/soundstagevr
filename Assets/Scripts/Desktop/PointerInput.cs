// Pointer and keys for the mouse hand (2026-11-25).
//
// On a PC this is the mouse and keyboard. On a phone it's touch: PhoneMode fills it in
// every frame from the fingers and the on-screen buttons. DesktopMode, NetSpectator and
// QuestInput read this instead of Input, so the same mouse-hand code drives both.

using UnityEngine;
using UnityEngine.EventSystems;

public static class PointerInput {
  public static bool touch; // the phone: PhoneMode sets the fields below

  // set by PhoneMode
  public static Vector2 tPos, tDelta;
  public static bool tDown, tHeld;
  public static float tScroll;

  public static Vector2 Position { get { return touch ? tPos : (Vector2)Input.mousePosition; } }
  public static bool Down { get { return touch ? tDown : Input.GetMouseButtonDown(0); } }
  public static bool Held { get { return touch ? tHeld : Input.GetMouseButton(0); } }
  public static float Scroll { get { return touch ? tScroll : Input.mouseScrollDelta.y; } }
  public static Vector2 Delta { get { return touch ? tDelta : NetSpectator.MouseDelta; } }

  // a finger that starts on an on-screen button never becomes the pointer, so on the
  // phone the pointer is never over the UI
  public static bool OverUI {
    get { return !touch && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }
  }

  public static bool Alt { get { return !touch && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)); } }
  public static bool Shift { get { return !touch && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)); } }
  public static bool Ctrl { get { return !touch && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)); } }

  // Tab (menu), C (copy / B button), Delete: on the phone, the on-screen buttons
  public static bool GetKey(KeyCode k) { return touch ? PhoneMode.Key(k, 0) : Input.GetKey(k); }
  public static bool GetKeyDown(KeyCode k) { return touch ? PhoneMode.Key(k, 1) : Input.GetKeyDown(k); }
  public static bool GetKeyUp(KeyCode k) { return touch ? PhoneMode.Key(k, 2) : Input.GetKeyUp(k); }
}
