// Shared material variants (2026-11, Quest draw-call work).
//
// Many controls used to make their own copy of a material (renderer.material /
// new Material) just to set a colour or glow strength, so a 34-device patch had
// ~1 distinct material per renderer and Unity couldn't batch anything (SetPass
// calls ~= draw calls). MaterialCache hands out one shared material per
// (base material, colour, float) combination instead, so e.g. all the dark
// sequencer cells, or every lit cell in the same row colour, use one material.
//
// Never modify a material returned from here: other renderers share it.

using UnityEngine;
using System.Collections.Generic;

public static class MaterialCache {
  struct Key : System.IEquatable<Key> {
    public int baseId, colorProp, floatProp;
    public Color color;
    public float value;

    public bool Equals(Key o) {
      return baseId == o.baseId && colorProp == o.colorProp && floatProp == o.floatProp &&
             color == o.color && value == o.value;
    }
    public override bool Equals(object o) { return o is Key && Equals((Key)o); }
    public override int GetHashCode() {
      unchecked {
        int h = baseId;
        h = h * 31 + colorProp;
        h = h * 31 + floatProp;
        h = h * 31 + color.GetHashCode();
        h = h * 31 + value.GetHashCode();
        return h;
      }
    }
  }

  static readonly Dictionary<Key, Material> cache = new Dictionary<Key, Material>();

  public static readonly int TintColor = Shader.PropertyToID("_TintColor");
  public static readonly int ColorProp = Shader.PropertyToID("_Color");
  public static readonly int EmissionGain = Shader.PropertyToID("_EmissionGain");

  // Colour and/or float variant of baseMat. Pass 0 for a property you don't set.
  public static Material Get(Material baseMat, int colorProp, Color color, int floatProp = 0, float value = 0f) {
    if (baseMat == null) return null;
    Key k = new Key { baseId = baseMat.GetInstanceID(), colorProp = colorProp, color = color, floatProp = floatProp, value = value };
    Material m;
    if (cache.TryGetValue(k, out m) && m != null) return m;
    m = new Material(baseMat);
    m.name = baseMat.name + " (shared)";
    if (colorProp != 0) m.SetColor(colorProp, color);
    if (floatProp != 0) m.SetFloat(floatProp, value);
    cache[k] = m;
    return m;
  }

  // Glow variant: _TintColor + _EmissionGain (the SE additive / emissive shaders).
  public static Material Glow(Material baseMat, Color tint, float gain) {
    return Get(baseMat, TintColor, tint, EmissionGain, gain);
  }

  // _EmissionGain-only variant, keeping the base material's tint.
  public static Material Gain(Material baseMat, float gain) {
    return Get(baseMat, 0, Color.clear, EmissionGain, gain);
  }

  // _Color variant (lit Standard materials such as coreMat).
  public static Material Colored(Material baseMat, Color c) {
    return Get(baseMat, ColorProp, c);
  }

  public static int Count { get { return cache.Count; } }
}
