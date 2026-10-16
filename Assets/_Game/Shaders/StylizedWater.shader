// BuddyTD/StylizedWater – eigener, stilisierter Wassershader für das Walddorf (URP 17, ein Pass, 2026-10-16).
//
// Stil: Low-Poly/Painterly. Flach → tief nach echter Wassertiefe (Kamera-Tiefentextur, Rückfall: Tiefe aus der
// Vertexfarbe), Uferschaum und helle Kante an Ufern/Pfeilern (Tiefendifferenz), Wellen-Normalen, die im Bach in
// Fließrichtung strömen und auf Teich/Weiher langsam treiben, gestufter Sonnenglanz, Fresnel, Schlieren,
// optional Strudel (Mühlrad) und leichte Verzerrung über die Opaque-Textur.
//
// Mesh-Daten (VillageSetup.BuildWaterSurface):
//   uv0   Bach: U quer (m, + = links der Fließrichtung), V entlang (m, wächst in Fließrichtung); Teich: Welt-XZ
//   uv1   Fließrichtung in Welt-XZ (normiert)
//   color R = Strömung (1 = Bach, 0 = stehendes Wasser), G = Wassertiefe (m, 0..1) als Rückfall ohne Tiefentextur
//
// Überlappungen (Bach mündet in den Teich, enge Kurven) werden per Stencil-Bit 8 nur einmal gezeichnet:
// Teich-Material Queue 2999, Bach 3000 → im Teich gewinnt das stehende Wasser.
Shader "BuddyTD/StylizedWater"
{
    Properties
    {
        [Header(Farbe)]
        _ShallowColor ("Flaches Wasser (A = Deckkraft)", Color) = (0.24, 0.50, 0.56, 0.62)
        _DeepColor ("Tiefes Wasser (A = Deckkraft)", Color) = (0.08, 0.27, 0.42, 0.95)
        _DepthRange ("Tiefe bis voll tief (m)", Float) = 0.5
        _ColorVariation ("Farbflecken", Range(0, 0.5)) = 0.12

        [Header(Tiefe)]
        [Toggle] _UseSceneDepth ("Kamera-Tiefentextur nutzen (sonst Vertexfarbe G)", Float) = 1
        _EdgeFade ("Weiche Uferkante (m Tiefe)", Float) = 0.025

        [Header(Uferschaum)]
        _FoamColor ("Schaumfarbe", Color) = (0.93, 0.97, 0.95, 1)
        _FoamDepth ("Schaumbreite (m Tiefe)", Float) = 0.22
        _FoamSoftness ("Schaum weich", Range(0.01, 1)) = 0.22
        _FoamScale ("Schaum-Kachel je m", Float) = 0.9
        _FoamStrength ("Schaum-Stärke", Range(0, 1)) = 0.85
        _EdgeLine ("Helle Kante (m Tiefe)", Float) = 0.035

        [Header(Wellen)]
        [NoScaleOffset] _NormalTex ("Wellen-Normalen", 2D) = "bump" {}
        [NoScaleOffset] _NoiseTex ("Rauschen (R Schaum, G Schlieren, B Flecken, A Funkeln)", 2D) = "gray" {}
        _WaveScale ("Wellen-Kachel je m", Float) = 0.22
        _WaveStrength ("Wellen-Stärke", Range(0, 2)) = 0.55
        _FlowSpeed ("Fließgeschwindigkeit Bach (m/s)", Float) = 0.75
        _StillSpeed ("Treiben Teich (m/s)", Float) = 0.05

        [Header(Schlieren)]
        _StreakStrength ("Strömungslinien", Range(0, 1)) = 0.3
        _StreakScale ("Schlieren-Kachel je m", Float) = 0.16

        [Header(Licht)]
        _GlossColor ("Glanzfarbe", Color) = (1, 0.97, 0.88, 1)
        _Gloss ("Glanz-Schärfe", Range(8, 512)) = 140
        _GlossStrength ("Glanz-Stärke", Range(0, 2)) = 0.8
        _GlossSteps ("Glanz gestuft (0 = weich, 1 = hart)", Range(0, 1)) = 0.6
        _SparkleStrength ("Funkeln", Range(0, 2)) = 0.6
        _FresnelColor ("Himmelsreflex", Color) = (0.72, 0.84, 0.9, 1)
        _FresnelPower ("Fresnel-Exponent", Range(0.5, 10)) = 4
        _FresnelStrength ("Fresnel-Stärke", Range(0, 1)) = 0.35
        _Diffuse ("Licht auf der Oberfläche", Range(0, 1)) = 0.35

        [Header(Verzerrung)]
        _Refraction ("Verzerrung (Opaque-Textur, 0 = aus)", Range(0, 0.08)) = 0.025

        [Header(Strudel)]
        _SwirlPos ("Strudel: X, Z, Radius, Stärke (0 = aus)", Vector) = (0, 0, 2, 0)
        _SwirlSpeed ("Strudel-Drehung", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StylizedWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Stencil
            {
                Ref 8
                ReadMask 8
                WriteMask 8
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor, _GlossColor, _FresnelColor;
                float _DepthRange, _ColorVariation, _UseSceneDepth, _EdgeFade;
                float _FoamDepth, _FoamSoftness, _FoamScale, _FoamStrength, _EdgeLine;
                float _WaveScale, _WaveStrength, _FlowSpeed, _StillSpeed;
                float _StreakStrength, _StreakScale;
                float _Gloss, _GlossStrength, _GlossSteps, _SparkleStrength, _FresnelPower, _FresnelStrength, _Diffuse;
                float _Refraction;
                float4 _SwirlPos;
                float _SwirlSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 flowUV : TEXCOORD1;   // xy = Bach-UV (m), zw = Fließrichtung (Welt-XZ)
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.flowUV = float4(v.uv0, v.uv1);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Augentiefe aus dem Roh-Tiefenwert – perspektivisch und orthografisch (Minimap)
            float RawToEye(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    return _ProjectionParams.y + raw * (_ProjectionParams.z - _ProjectionParams.y);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // senkrechte Wassertiefe unter dem Fragment (m) aus der Kamera-Tiefentextur
            float SceneWaterDepth(float2 suv, float3 posWS, float fragEye)
            {
                float sceneEye = RawToEye(SampleSceneDepth(suv));
                float3 sceneWS;
                if (unity_OrthoParams.w > 0.5)
                {
                    float3 fwd = -UNITY_MATRIX_V[2].xyz;
                    sceneWS = posWS + fwd * (sceneEye - fragEye);
                }
                else
                {
                    sceneWS = _WorldSpaceCameraPos + (posWS - _WorldSpaceCameraPos) * (sceneEye / max(fragEye, 1e-4));
                }
                return posWS.y - sceneWS.y;
            }

            half2 SampleNormalXY(float2 uv)
            {
                half3 n = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv));
                return n.xy;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 posWS = i.positionWS;
                float flow = saturate(i.color.r);
                float2 dir = i.flowUV.zw;
                float dl = length(dir);
                dir = dl > 1e-3 ? dir / dl : float2(0, 1);
                float2 across = float2(-dir.y, dir.x); // Richtung, in der U wächst

                // ---- Wellen: Bach (UV-Raum, strömt in +V), Teich (Welt-XZ, treibt langsam)
                float2 uvS = i.flowUV.xy * _WaveScale;
                float sOff = t * _FlowSpeed * _WaveScale;
                half2 nS1 = SampleNormalXY(uvS + float2(0.0, -sOff));
                half2 nS2 = SampleNormalXY(uvS * 1.73 + float2(0.37, -sOff * 1.45));
                half2 nS = nS1 * 0.6 + nS2 * 0.4;
                float2 nSxz = nS.x * across + nS.y * dir;   // UV-Raum → Welt-XZ

                float2 uvW = posWS.xz * _WaveScale;
                float wOff = t * _StillSpeed * _WaveScale;
                half2 nW1 = SampleNormalXY(uvW + wOff * float2(0.6, 0.8));
                half2 nW2 = SampleNormalXY(uvW * 1.61 + wOff * float2(-0.9, 0.35) + 0.5);
                float2 nWxz = nW1 * 0.55 + nW2 * 0.45;

                float2 nxz = lerp(nWxz, nSxz, flow);

                // ---- Schaum-/Schlieren-Rauschen mitbewegt
                float2 fS = i.flowUV.xy * _FoamScale + float2(0.0, -t * _FlowSpeed * _FoamScale);
                float2 fW = posWS.xz * _FoamScale + t * _StillSpeed * _FoamScale * float2(0.7, -0.5);
                half4 noiseS = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, fS);
                half4 noiseW = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, fW);
                half4 nz = lerp(noiseW, noiseS, flow);
                half streak = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    i.flowUV.xy * _StreakScale * float2(1.0, 0.5) + float2(0.13, -t * _FlowSpeed * _StreakScale * 0.5)).g;
                half blot = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, posWS.xz * 0.035 + t * 0.004).b;

                // ---- Strudel (z. B. am Mühlrad): zwei Phasen, damit sich das Muster nicht endlos aufwickelt
                float swirl = 0.0;
                float swirlFoam = 0.0;
                if (_SwirlPos.w > 0.001)
                {
                    float2 rel = posWS.xz - _SwirlPos.xy;
                    float r = length(rel);
                    swirl = saturate(1.0 - r / max(_SwirlPos.z, 0.01)) * _SwirlPos.w;
                    if (swirl > 0.001)
                    {
                        float ph0 = frac(t * 0.18);
                        float ph1 = frac(t * 0.18 + 0.5);
                        float w0 = 1.0 - abs(1.0 - 2.0 * ph0);
                        float a0 = ph0 * _SwirlSpeed * 5.0 / (r + 0.6);
                        float a1 = ph1 * _SwirlSpeed * 5.0 / (r + 0.6);
                        float2 r0 = float2(rel.x * cos(a0) - rel.y * sin(a0), rel.x * sin(a0) + rel.y * cos(a0));
                        float2 r1 = float2(rel.x * cos(a1) - rel.y * sin(a1), rel.x * sin(a1) + rel.y * cos(a1));
                        half4 s0 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, r0 * _FoamScale * 1.4 + 0.21);
                        half4 s1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, r1 * _FoamScale * 1.4 + 0.67);
                        swirlFoam = lerp(s1.r, s0.r, w0);
                        half2 sn0 = SampleNormalXY(r0 * _WaveScale * 2.2);
                        half2 sn1 = SampleNormalXY(r1 * _WaveScale * 2.2 + 0.5);
                        nxz = lerp(nxz, lerp(sn1, sn0, w0) * 1.6, swirl * 0.7);
                    }
                }

                float3 N = normalize(float3(nxz.x * _WaveStrength, 1.0, nxz.y * _WaveStrength));
                float3 V = GetWorldSpaceNormalizeViewDir(posWS);

                // ---- Tiefe
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float fragEye = -TransformWorldToView(posWS).z;
                float depthM = i.color.g;
                if (_UseSceneDepth > 0.5) depthM = SceneWaterDepth(suv, posWS, fragEye);
                depthM = max(depthM, 0.0);
                float depth01 = saturate(depthM / max(_DepthRange, 0.01));

                // ---- Grundfarbe flach → tief
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                half alpha = lerp(_ShallowColor.a, _DeepColor.a, depth01);
                col *= 1.0 + (blot - 0.5) * 2.0 * _ColorVariation;

                // ---- Licht
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(posWS));
                half shadow = mainLight.shadowAttenuation;
                half3 lightCol = mainLight.color;
                half ndl = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(N);
                half3 lit = ambient + lightCol * shadow * lerp(1.0, ndl, _Diffuse);
                col *= lit;

                // Fresnel-Himmelsreflex
                half fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower) * _FresnelStrength;
                col = lerp(col, _FresnelColor.rgb * (ambient + lightCol * 0.5), fres);
                alpha = saturate(alpha + fres * 0.5);

                // Schlieren (nur im fließenden Wasser)
                half st = smoothstep(0.55, 0.9, streak) * _StreakStrength * flow * (1.0 - depth01 * 0.4);
                col += st * 0.35 * lit;

                // ---- Uferschaum + helle Kante (Tiefendifferenz) + Strudelschaum
                half foamMask = 1.0 - saturate(depthM / max(_FoamDepth, 0.001));
                half foam = saturate((foamMask - (1.0 - nz.r) * 0.9) / _FoamSoftness) * _FoamStrength;
                half edge = 1.0 - smoothstep(_EdgeLine * 0.5, _EdgeLine, depthM);
                foam = max(foam, edge * 0.8);
                foam = max(foam, smoothstep(0.62 - swirl * 0.35, 0.85, swirlFoam) * swirl);
                col = lerp(col, _FoamColor.rgb * lit, foam);
                alpha = max(alpha, foam);

                // ---- Glanz (gestuft) + Funkeln
                float3 H = normalize(mainLight.direction + V);
                half spec = pow(saturate(dot(N, H)), _Gloss);
                half stepped = smoothstep(0.35, 0.45, spec);
                spec = lerp(spec, stepped, _GlossSteps);
                half sparkle = nz.a * smoothstep(0.1, 0.6, pow(saturate(dot(N, H)), _Gloss * 0.12)) * _SparkleStrength;
                half3 gloss = _GlossColor.rgb * lightCol * shadow * (spec * _GlossStrength + sparkle) * (1.0 - foam);
                col += gloss;
                alpha = saturate(alpha + (spec * _GlossStrength + sparkle) * 0.5);

                // weiche Schnittkante mit dem Ufer
                alpha *= saturate(depthM / max(_EdgeFade, 0.001));

                col = MixFog(col, i.fogFactor);

                // ---- leichte Verzerrung des Bachbetts über die Opaque-Textur (billig, ein Sample)
                if (_Refraction > 0.0001)
                {
                    float2 ruv = suv + nxz * _Refraction * saturate(depthM * 4.0);
                    // nichts vor der Wasseroberfläche (Beine, Pfeiler) ins Wasser ziehen
                    if (RawToEye(SampleSceneDepth(ruv)) < fragEye) ruv = suv;
                    half3 bed = SampleSceneColor(ruv);
                    return half4(lerp(bed, col, alpha), saturate(depthM / max(_EdgeFade, 0.001)));
                }
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
