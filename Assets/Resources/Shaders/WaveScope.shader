// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// GPU oscilloscope (added 2026-11). Replaces the CPU-drawn 512x64 RGBA texture that
// waveViz uploaded every frame (128 KB per oscillator) with a 512x1 R8 texture of
// column heights (512 bytes). Draws exactly what ProcessWaveTexture drew: in each
// column a vertical segment from the previous column's height to this column's height,
// on a 64-row grid, scrolled so the newest sample is on the right.
// Output and blending match "Sonic Ether/Particles/Additive" (the display's previous
// shader), so brightness and bloom glow are unchanged.

Shader "SoundStage/WaveScope" {
Properties {
	_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
	_EmissionGain ("Emission Gain", Range(0, 1)) = 0.3
	_LineColor ("Line Color", Color) = (1,1,1,1)
	_BGColor ("Background Color", Color) = (0,0,0,1)
	_WaveTex ("Wave Heights (R8)", 2D) = "black" {}
	_Head ("Write head (0-1)", Float) = 0
	_Columns ("Columns", Float) = 512
	_Rows ("Rows", Float) = 64
}

SubShader {
	Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
	Blend SrcAlpha One
	ColorMask RGB
	Cull Off Lighting Off ZWrite Off

	Pass {
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc"

		sampler2D _WaveTex;
		fixed4 _TintColor;
		fixed4 _LineColor;
		fixed4 _BGColor;
		float _EmissionGain;
		float _Head;
		float _Columns;
		float _Rows;

		struct appdata_t {
			float4 vertex : POSITION;
			fixed4 color : COLOR;
			float2 texcoord : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct v2f {
			float4 vertex : SV_POSITION;
			fixed4 color : COLOR;
			float2 uv : TEXCOORD0;
			UNITY_VERTEX_OUTPUT_STEREO
		};

		v2f vert (appdata_t v) {
			v2f o;
			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_INITIALIZE_OUTPUT(v2f, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.color = v.color;
			o.uv = v.texcoord;
			return o;
		}

		// height (0..rows-1) stored per column as a byte
		float heightAt(float column) {
			float u = (column + 0.5) / _Columns;          // wraps via Repeat
			return floor(tex2Dlod(_WaveTex, float4(u, 0.5, 0, 0)).r * 255.0 + 0.5);
		}

		fixed4 frag (v2f i) : SV_Target {
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
			float u = frac(i.uv.x + _Head);                // same scroll the old texture offset did
			float column = floor(u * _Columns);
			float h1 = heightAt(column);
			float h0 = heightAt(column - 1.0);
			float row = floor(i.uv.y * _Rows);
			bool lit = row >= min(h0, h1) && row <= max(h0, h1);
			fixed4 tex = lit ? _LineColor : _BGColor;
			tex.a = 1;
			return 2.0f * i.color * _TintColor * tex * (exp(_EmissionGain * 5.0f));
		}
		ENDCG
	}
}
}
