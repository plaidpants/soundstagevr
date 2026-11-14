// Glow buffer format for the Quest build (2026-11).
//
// While the glow (SE bloom) is on, the scene renders into an HDR texture that the bloom
// reads. Unity's default HDR format is FP16 (64 bits per pixel). R11G11B10 is also HDR
// (the bright emissive parts keep their extra brightness) but 32 bits per pixel, so the
// scene target, its MSAA resolve and the bloom chain move half the data.
// Tested 2026-11-14: R11G11B10 gained ~1 ms with the glow on, but it has no alpha
// channel, so passthrough showed nothing (and rendering LDR there instead lost the glow).
// The project stays on FP16; this menu is kept for future experiments.
//
// The setting is stored in ProjectSettings/GraphicsSettings.asset (Android tier settings).

using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class GlowBufferTools {
  const string Menu = "SoundStage/Glow buffer (Android)/";

  [MenuItem(Menu + "R11G11B10 (32-bit HDR)")]
  static void SetR11() { Set(CameraHDRMode.R11G11B10); }

  [MenuItem(Menu + "FP16 (64-bit HDR, Unity default)")]
  static void SetFP16() { Set(CameraHDRMode.FP16); }

  static void Set(CameraHDRMode mode) {
    foreach (GraphicsTier tier in new[] { GraphicsTier.Tier1, GraphicsTier.Tier2, GraphicsTier.Tier3 }) {
      TierSettings ts = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Android, tier);
      ts.hdr = true;
      ts.hdrMode = mode;
      EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.Android, tier, ts);
    }
    // Tier settings live on the GraphicsSettings object; mark it dirty so SaveAssets
    // actually writes ProjectSettings/GraphicsSettings.asset.
    EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
    AssetDatabase.SaveAssets();
    TierSettings check = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Android, GraphicsTier.Tier1);
    Debug.Log("[GlowBufferTools] Android HDR mode set to " + mode + " (reads back hdr=" + check.hdr +
              " mode=" + check.hdrMode + "). Check Project Settings > Graphics > Tier Settings > Android, then rebuild.");
  }
}
