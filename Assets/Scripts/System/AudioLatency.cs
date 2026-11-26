using UnityEngine;

// Audio output latency on Android (2026-11-26). The project's DSP buffer is 512 samples
// ("Good latency"), which on the Quest queues about 43 ms of audio (512 x 4 at 48 kHz).
// The Quest's audio thread is only ~6-7% busy, so on Android it runs at 256 ("Best
// latency"): about 21 ms, and every instrument - and the microphone - answers sooner.
// The PC keeps the project setting. Set once at startup, before anything plays
// (AudioSettings.Reset restarts the audio system). PlayerPrefs "dspBufferSize" overrides
// it (0 = leave the project setting). Logged as "[Audio]"; masterLimiter's "[masterLimiter]"
// lines show the audio thread's load to compare.
public static class AudioLatency {
  public static int androidBuffer = 256;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Boot() {
#if UNITY_ANDROID && !UNITY_EDITOR
    int want = PlayerPrefs.GetInt("dspBufferSize", androidBuffer);
    AudioSettings.GetDSPBufferSize(out int len, out int num);
    if (want <= 0 || want == len) {
      Debug.Log("[Audio] DSP buffer " + len + " x " + num + " at " + AudioSettings.outputSampleRate + " Hz (unchanged)");
      return;
    }
    AudioConfiguration c = AudioSettings.GetConfiguration();
    c.dspBufferSize = want;
    bool ok = AudioSettings.Reset(c);
    AudioSettings.GetDSPBufferSize(out int len2, out int num2);
    Debug.Log("[Audio] DSP buffer " + len + " x " + num + " -> " + len2 + " x " + num2 + " at " + AudioSettings.outputSampleRate +
              " Hz (" + (ok ? "ok" : "Reset failed") + "), about " + Mathf.RoundToInt(len2 * num2 * 1000f / AudioSettings.outputSampleRate) + " ms queued");
#endif
  }
}
