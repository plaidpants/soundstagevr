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

// GPU column display (added 2026-11) for the looper and recorder waveforms. They used to
// draw a 1024x512 RGBA texture on the CPU (2 MB) and upload it every few frames while
// playing; now each column is two bytes in a 1024x1 RG8 texture: R = bar height in rows,
// G = 1 if that column was written while recording (alternate line colour).
// _Mode 0: bars mirrored around the centre row (waveform), 1: bars up from the bottom.
// Output and blending match "Sonic Ether/Particles/Additive" (the displays' previous
// shader), including the alpha-folded additive used by the other glow shaders.

Shader "SoundStage/WaveColumns" {
Properties {
	_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
	_EmissionGain ("Emission Gain", Range(0, 1)) = 0.3
	_MaxBright ("Max line brightness (keeps hue)", Float) = 1.5
	_LineColor ("Line Color", Color) = (1,1,1,1)
	_LineColorAlt ("Line Color (recording)", Color) = (1,1,1,1)
	_BGColor ("Background Color", Color) = (0,0,0,1)
	_ColumnTex ("Columns (RG8)", 2D) = "black" {}
	_Columns ("Columns", Float) = 1024
	_Rows ("Rows", Float) = 512
	_Mode ("Mode", Float) = 0
	_EdgeMargin ("Bar margin from the edges (fraction)", Float) = 0.08
	_Polar ("Polar mapping (ring meshes)", Float) = 0
	_PolarU ("u = frac(x * angle/2pi + y)", Vector) = (1,0,0,0)
	_PolarV ("v = x * radius + y", Vector) = (1,0,0,0)
}

SubShader {
	Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
	Blend One One
	ColorMask RGB
	Cull Off Lighting Off ZWrite Off

	Pass {
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc"

		sampler2D _ColumnTex;
		float4 _ColumnTex_ST;
		half4 _TintColor;
		half4 _LineColor;
		half4 _LineColorAlt;
		half4 _BGColor;
		float _EmissionGain;
		float _MaxBright;
		float _SSEmissionOffset; // global, set by masterControl (log of brightness scale, 0 = unchanged)
		float _Columns;
		float _Rows;
		float _Mode;
		float _Polar;
		float _EdgeMargin;
		float4 _PolarU;
		float4 _PolarV;

		struct appdata_t {
			float4 vertex : POSITION;
			half4 color : COLOR;
			float2 texcoord : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct v2f {
			float4 vertex : SV_POSITION;
			half4 color : COLOR;
			float2 uv : TEXCOORD0;
			float2 ringPos : TEXCOORD1; // object-space position in the ring's plane (x, z)
			UNITY_VERTEX_OUTPUT_STEREO
		};

		v2f vert (appdata_t v) {
			v2f o;
			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_INITIALIZE_OUTPUT(v2f, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.color = v.color;
			o.uv = TRANSFORM_TEX(v.texcoord, _ColumnTex);
			o.ringPos = v.vertex.xz;
			return o;
		}

		half4 frag (v2f i) : SV_Target {
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
			// Anti-aliased: heights are filtered linearly between columns and the bar edge is
			// smoothed over one screen pixel (fwidth). The looper's ring mesh maps columns
			// around a circle, so hard per-texel edges showed as jagged hatching; the old CPU
			// texture got the same smoothing from bilinear filtering.
			// Ring meshes (the looper): the mesh is 20 trapezoid wedges, and UVs interpolated
			// across their triangles bend the columns into slanted, misaligned strokes. With
			// _Polar the UV is computed exactly from the fragment's angle and radius instead
			// (mapping fitted to the mesh's own UVs by ColumnDisplay.EnablePolar).
			if (_Polar > 0.5) {
				float ang = atan2(i.ringPos.y, i.ringPos.x) * 0.15915494; // / 2pi
				float2 puv = float2(frac(_PolarU.x * ang + _PolarU.y), _PolarV.x * length(i.ringPos) + _PolarV.y);
				i.uv = puv * _ColumnTex_ST.xy + _ColumnTex_ST.zw;
			}
			float2 cell = tex2Dlod(_ColumnTex, float4(frac(i.uv.x), 0.5, 0, 0)).rg;
			float h = cell.r * 255.0;
			float y = frac(i.uv.y) * _Rows;
			float aa = max(fwidth(y), 1e-3);
			// Keep the bars _EdgeMargin (fraction of the half height) away from the mesh's
			// edges, so loud peaks don't run into the ring's rim, and draw nothing for empty
			// columns (the smoothing otherwise left a faint dotted line along the centre).
			float halfH = _Rows * 0.5;
			h = min(h, halfH * (1.0 - _EdgeMargin));
			float edge = (_Mode < 0.5) ? (h - abs(y - halfH)) : (h + 1.0 - y);
			half cover = saturate(edge / aa + 0.5) * step(0.5, h);
			half4 lineCol = cell.g > 0.5 ? _LineColorAlt : _LineColor;
			half4 tex = lerp(_BGColor, lineCol, cover);
			half4 glowOut = 2.0f * i.color * _TintColor * tex * (exp(_EmissionGain * 5.0f + _SSEmissionOffset));
			glowOut.rgb *= glowOut.a;
			// Keep the line colour: these materials push the line ~10-20x past white, so every
			// channel clipped and all lines came out white (the looper's orange recording
			// colour was invisible). Cap the brightest channel at _MaxBright: hue survives and
			// the bloom still gets values above 1 to glow from.
			half peak = max(max(glowOut.r, glowOut.g), glowOut.b);
			glowOut.rgb *= min(1.0, _MaxBright / max(peak, 1e-4));
			return glowOut;
		}
		ENDCG
	}
}
}
