// Copyright 2017-2026 Google LLC / contributors
// Licensed under the Apache License, Version 2.0
//
// Reference-counted manipObject.onTouch. With hand tracking several colliders
// (the pinch point plus every fingertip) can touch the same key at once; without
// counting, one finger leaving would turn the key off while another still holds it.

using System.Collections.Generic;

public static class TouchTracker {
  static readonly Dictionary<manipObject, int> counts = new Dictionary<manipObject, int>();

  public static void Touch(manipObject o, bool on, manipulator m) {
    if (o == null) return;
    int c;
    counts.TryGetValue(o, out c);
    int next = on ? c + 1 : c - 1;
    if (next <= 0) counts.Remove(o); else counts[o] = next;
    if (on && c == 0) o.onTouch(true, m);
    else if (!on && c > 0 && next <= 0) o.onTouch(false, m);
  }
}
