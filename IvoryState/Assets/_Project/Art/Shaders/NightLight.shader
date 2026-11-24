// Lumières qui s'allument la nuit, sans vraie source de lumière (trop coûteuses sur mobile) :
// ampoules des lampadaires, guirlandes des maquis, et halos lumineux au sol (quad additif).
// L'intensité suit _IvoryNight, réglée par DayNightCycle ; le bloom fait le reste.
Shader "IvoryState/NightLight"
{
    Properties
    {
        [HDR] _Color ("Couleur", Color) = (1.0, 0.78, 0.5, 1)
        _MainTex ("Masque (halo)", 2D) = "white" {}
        _DayIntensity ("Intensité de jour", Float) = 0.05
        _NightIntensity ("Intensité de nuit", Float) = 4
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _IvoryNight;
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _DayIntensity, _NightIntensity;
                float _SrcBlend, _DstBlend, _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                half intensity = lerp(_DayIntensity, _NightIntensity, _IvoryNight);
                half3 color = _Color.rgb * (mask * intensity);
                // Halo additif : la brume l'efface (vers le noir) ; ampoule opaque : brume normale.
                color = _DstBlend > 0.5 ? color * ComputeFogIntensity(input.fogFactor) : MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
