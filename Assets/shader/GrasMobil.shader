// Sparsame Mobil-Variante von gras.shadergraph (Branch feature/mobile, 2026-11-02). Gleicher Wind (Sinus über Zeit +
// Weltposition, nach oben stärker), gleiche Textur/Farbe, aber Licht pro Vertex (Hauptlicht halb-Lambert + Umgebung)
// statt PBR pro Pixel, keine Schatten, kein Zusatzlicht. Nur von MobileGrass auf der Low-Stufe genutzt; PC unverändert.
Shader "BuddyTD/GrasMobil"
{
    Properties
    {
        _MainTex ("Textur", 2D) = "white" {}
        _Color ("Farbe", Color) = (0.14, 0.4, 0.12, 1)
        _Scale ("Wind-Stärke", Float) = 0.1
        _Speed ("Wind-Tempo", Float) = 1
        _Cutoff ("Alpha-Schwelle", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float _Scale;
            float _Speed;
            half _Cutoff;
        CBUFFER_END

        // Wie im Shader Graph: Versatz = sin((Zeit + x + z) * Tempo) * Stärke * uv.y, auf alle Achsen addiert
        float3 WindWorld(float3 posOS, float2 uv)
        {
            float3 posWS = TransformObjectToWorld(posOS);
            float w = sin((_Time.y + posWS.x + posWS.z) * _Speed) * _Scale * uv.y;
            return posWS + w;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 lightFog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 posWS = WindWorld(v.positionOS.xyz, v.uv);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Halme leuchten von beiden Seiten → halb-Lambert mit nach oben geneigter Normale
                half3 n = normalize(TransformObjectToWorldNormal(v.normalOS) + half3(0, 1, 0));
                Light main = GetMainLight();
                half ndl = saturate(dot(n, main.direction) * 0.5h + 0.5h);
                half3 light = main.color * ndl + SampleSH(n);
                o.lightFog = half4(light, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                half3 col = tex.rgb * _Color.rgb * i.lightFog.rgb;
                col = MixFog(col, i.lightFog.a);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(WindWorld(v.positionOS.xyz, v.uv));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
