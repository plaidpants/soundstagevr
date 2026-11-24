// Part of PerfProbe (System/PerfProbe.cs): marks the start / end of the frame's scripts.

using UnityEngine;

[DefaultExecutionOrder(-32000)]
public class PerfProbeFirst : MonoBehaviour {
  void Update() { PerfProbe.FirstUpdate(); }
  void FixedUpdate() { PerfProbe.FirstFixed(); }
}
