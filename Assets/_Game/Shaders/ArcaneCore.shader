// Kern des Arkanballs (Spieltest 2026-10-18, Paket V): additive Kugel mit hellem, fast weißem Kern und
// violetter Fresnel-Hülle, die langsam pulsiert. Unlit, kein Schatten, kein Tiefenschreiben – aus der Draufsicht
// liest sich die Kugel als leuchtender Ball mit kräftigem Rand. Material: M_ArcaneCore (ArcaneVfxBuilder).
Shader "BuddyTD/ArcaneCore"
{
    Properties
    {
        [HDR] _CoreColor ("Kern", Color) = (1.6, 1.35, 2.2, 1)
        [HDR] _RimColor ("Hülle (Fresnel)", Color) = (0.75, 0.25, 2.4, 1)
        _RimPower ("Fresnel-Exponent", Range(0.5, 8)) = 2.2
        _CoreSize ("Kerngröße", Range(0, 1)) = 0.55
        _PulseSpeed ("Puls-Tempo", Float) = 7
        _PulseAmount ("Puls-Stärke", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ArcaneCore"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _RimColor;
                half _RimPower;
                half _CoreSize;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float phase : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                // Phase aus der Weltposition des Objekts: mehrere Kugeln pulsieren nicht im Gleichschritt
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                o.phase = frac(dot(originWS, float3(0.37, 0.11, 0.53))) * 6.2831;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 vdir = normalize(i.viewWS);
                half ndv = saturate(dot(n, vdir));
                half rim = pow(1.0h - ndv, _RimPower);
                half pulse = 1.0h + _PulseAmount * sin(_Time.y * _PulseSpeed + i.phase);
                half core = smoothstep(1.0h - _CoreSize, 1.0h, ndv);
                half3 col = _CoreColor.rgb * core + _RimColor.rgb * rim * pulse + _RimColor.rgb * 0.15h;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
