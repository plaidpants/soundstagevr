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

// GPU bar display (added 2026-11) for the filter's spectrum display: same lighting and
// emission as "Sonic Ether/Emissive/Textured" (the display's previous shader), but the
// diffuse/emission mask is computed from a Nx1 RG8 column texture (R = bar height in
// rows) instead of a CPU-drawn 256x32 RGBA texture uploaded every other frame.
// A row is lit when row <= height (row 0 always lit), as the old texture did.

Shader "SoundStage/EmissiveColumns" {
Properties {
	_EmissionColor ("Emission Color", Color) = (1,1,1,1)
	_DiffuseColor ("Diffuse Color", Color) = (1, 1, 1, 1)
	_MainTex ("Columns (RG8)", 2D) = "black" {}
	_EmissionGain ("Emission Gain", Range(0, 1)) = 0.5
	_Columns ("Columns", Float) = 256
	_Rows ("Rows", Float) = 32
	_Active ("Active", Float) = 1
}

SubShader {
	Tags { "RenderType"="Opaque" }
	LOD 200

CGPROGRAM
#pragma surface surf Lambert
#pragma target 3.0

sampler2D _MainTex;
half4 _DiffuseColor;
half4 _EmissionColor;
float _EmissionGain;
float _SSEmissionOffset; // global, set by masterControl (log of brightness scale, 0 = unchanged)
float _Columns;
float _Rows;
float _Active; // 0 = display off (all black, like the old cleared texture)

struct Input {
	float2 uv_MainTex;
};

void surf (Input IN, inout SurfaceOutput o) {
	float column = floor(frac(IN.uv_MainTex.x) * _Columns);
	float h = floor(tex2Dlod(_MainTex, float4((column + 0.5) / _Columns, 0.5, 0, 0)).r * 255.0 + 0.5);
	float row = floor(frac(IN.uv_MainTex.y) * _Rows);
	half mask = (row <= h ? 1.0 : 0.0) * _Active;
	o.Albedo = mask * _DiffuseColor.rgb;
	o.Emission = _EmissionColor.rgb * mask * (exp(_EmissionGain * 10.0f + _SSEmissionOffset));
	o.Alpha = _DiffuseColor.a;
}
ENDCG
}
FallBack "Self-Illumin/VertexLit"
}
