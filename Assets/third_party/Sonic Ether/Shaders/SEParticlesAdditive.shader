// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Sonic Ether/Particles/Additive" {
Properties {
	_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
	_MainTex ("Particle Texture", 2D) = "white" {}
	_InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0
	_EmissionGain ("Emission Gain", Range(0, 1)) = 0.3
}

Category {
	Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
	Blend One One // was SrcAlpha One; alpha folded into rgb in frag (see below)
	AlphaTest Greater .01
	ColorMask RGB
	Offset -2, -32 // glow shells sit on or just outside opaque bodies (sequencer cubes, speaker rim); pull them forward to avoid z-fighting
	Cull Off Lighting Off ZWrite Off Fog { Color (0,0,0,0) }
	
	SubShader {
		Pass {
		
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_particles
			#pragma multi_compile_instancing // GPU instancing of shared glow materials (2026-11)

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			half4 _TintColor;
			
			struct appdata_t {
				float4 vertex : POSITION;
				half4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 vertex : SV_POSITION;
				half4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				#ifdef SOFTPARTICLES_ON
				float4 projPos : TEXCOORD1;
				#endif
				UNITY_VERTEX_OUTPUT_STEREO
			};
			
			float4 _MainTex_ST;

			v2f vert (appdata_t v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.vertex = UnityObjectToClipPos(v.vertex);
				#ifdef SOFTPARTICLES_ON
				o.projPos = ComputeScreenPos (o.vertex);
				COMPUTE_EYEDEPTH(o.projPos.z);
				#endif
				o.color = v.color;
				o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
				return o;
			}

			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
			float _InvFade;
			float _EmissionGain;
			float _SSEmissionOffset; // global, set by masterControl (log of brightness scale, 0 = unchanged)
			
			half4 frag (v2f i) : COLOR
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				#ifdef SOFTPARTICLES_ON
				float sceneZ = LinearEyeDepth (SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
				float partZ = i.projPos.z;
				float fade = saturate (_InvFade * (sceneZ-partZ));
				i.color.a *= fade;
				#endif
				
				// The output alpha is often > 1 (2 * tint.a * exp(gain*5)). With Blend SrcAlpha One
				// a float (HDR) target uses it unclamped as the blend factor, which is how the PC
				// build gets its bright, saturated glow; Quest renders to an 8-bit target where the
				// factor clamps to 1 (2-3x dimmer, washed out). Multiply it in here instead.
				half4 glowOut = 2.0f * i.color * _TintColor * tex2D(_MainTex, i.texcoord) * (exp(_EmissionGain * 5.0f + _SSEmissionOffset));
				glowOut.rgb *= glowOut.a;
				return glowOut;
			}
			ENDCG 
		}
	}	
}
}
