// Part of PerfProbe (System/PerfProbe.cs): marks the start / end of the frame's scripts.

using UnityEngine;

[DefaultExecutionOrder(32000)]
public class PerfProbeLast : MonoBehaviour {
  void LateUpdate() { PerfProbe.LastLateUpdate(); }
  void FixedUpdate() { PerfProbe.LastFixed(); }
}
