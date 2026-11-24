// Eau de la lagune Ébrié, pensée pour le mobile : aucune texture.
// Le relief des vagues est une somme de 5 ondes (dérivées analytiques) ; le reflet du ciel vient de
// la sonde de réflexion d'URP (Fresnel : l'eau reflète davantage le ciel quand on la regarde de loin).
Shader "IvoryState/Water"
{
    Properties
    {
        _DeepColor ("Eau profonde", Color) = (0.04, 0.13, 0.13, 1)
        _ShallowColor ("Eau claire (vagues)", Color) = (0.10, 0.26, 0.24, 1)
        _WaveScale ("Échelle des vagues (1/m)", Float) = 0.35
        _WaveSpeed ("Vitesse des vagues", Float) = 0.9
        _NormalStrength ("Force du relief", Range(0, 2)) = 0.6
        _Smoothness ("Brillance", Range(0, 1)) = 0.93
        [HDR] _NightGlow ("Reflets des lumières de la ville (nuit)", Color) = (0.9, 0.55, 0.25, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "IvoryCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor;
            float _WaveScale, _WaveSpeed, _NormalStrength, _Smoothness;
            half4 _NightGlow;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Une onde : ajoute sa pente (dérivée de a·sin(k·d·p + w·t)) à grad.
            void Wave(float2 p, float2 dir, float k, float a, float w, float t, inout float2 grad, inout float height)
            {
                float phase = dot(dir, p) * k + w * t;
                grad += dir * (a * k * cos(phase));
                height += a * sin(phase);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = input.positionWS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;

                float2 grad = 0;
                float height = 0;
                Wave(p, float2(0.80, 0.60), 1.00, 0.30, 1.0, t, grad, height);
                Wave(p, float2(-0.45, 0.89), 1.70, 0.18, 1.4, t, grad, height);
                Wave(p, float2(0.96, -0.28), 2.90, 0.10, 2.1, t, grad, height);
                Wave(p, float2(-0.71, -0.70), 5.30, 0.05, 2.9, t, grad, height);
                Wave(p, float2(0.20, -0.98), 9.70, 0.025, 4.3, t, grad, height);

                half3 normalWS = normalize(half3(-grad.x * _NormalStrength, 1.0, -grad.y * _NormalStrength));
                InputData inputData = IvoryInputData(input.positionWS, normalWS, input.positionCS, input.fogFactor);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(height * 0.8 + 0.4));
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);
                // La nuit, des reflets chauds dansent sur les crêtes (lumières des quais).
                surface.emission = _NightGlow.rgb * (_IvoryNight * saturate(height - 0.25) * 0.6);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex IvoryDepthVertex
            #pragma fragment IvoryNullFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }

    FallBack Off
}
