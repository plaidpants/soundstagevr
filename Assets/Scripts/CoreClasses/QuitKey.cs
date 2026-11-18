// Escape quits the PC app (2026-11-18): press it twice within 2 seconds (the first press
// shows "PRESS ESC AGAIN TO QUIT"), so a stray press doesn't end a session. Not while the
// room code dialog is open - there Escape closes the dialog. Windows build only (in the
// editor it stops Play mode).

using UnityEngine;

public class QuitKey : MonoBehaviour {
  float armedUntil;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
    GameObject g = new GameObject("QuitKey");
    g.hideFlags = HideFlags.HideInHierarchy;
    DontDestroyOnLoad(g);
    g.AddComponent<QuitKey>();
  }

  void Update() {
    if (!Input.GetKeyDown(KeyCode.Escape)) return;
    if (NetCodeDialog.IsOpen || NetCodeDialog.ClosedFrame == Time.frameCount) return;
    if (Time.unscaledTime < armedUntil) {
      Debug.Log("[QuitKey] Escape pressed twice: quitting");
      NetNotice.Hide();
#if UNITY_EDITOR
      UnityEditor.EditorApplication.isPlaying = false;
#else
      Application.Quit();
#endif
      return;
    }
    armedUntil = Time.unscaledTime + 2f;
    NetNotice.Show("PRESS ESC AGAIN TO QUIT", "", 0, 2f);
  }
}
