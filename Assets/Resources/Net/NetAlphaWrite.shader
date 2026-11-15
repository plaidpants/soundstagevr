// Multiplayer (2026-11): drawn over other players' avatars, it only marks them as solid
// in the alpha channel. With passthrough on, the headset shows the camera feed wherever
// the picture's alpha is 0 - the avatars' glow / transparent materials left it at 0,
// so other players' hands and visors couldn't be seen in passthrough.
Shader "SoundStage/NetAlphaWrite" {
  SubShader {
    Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
    Pass {
      ZWrite Off
      ZTest LEqual
      Cull Back
      ColorMask A
      Blend One Zero
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile_instancing
      #include "UnityCG.cginc"
      struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
      struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
      v2f vert(appdata v) {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = UnityObjectToClipPos(v.vertex);
        return o;
      }
      fixed4 frag(v2f i) : SV_Target { return fixed4(0, 0, 0, 1); }
      ENDCG
    }
  }
}
