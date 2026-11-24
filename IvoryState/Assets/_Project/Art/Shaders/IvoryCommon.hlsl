// Fonctions partagées par les shaders Ivory State (façades, eau…).
// Éclairage URP standard (UniversalFragmentPBR) : soleil, ombres, lumières additionnelles, brume.
#ifndef IVORY_COMMON_INCLUDED
#define IVORY_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Variable globale, réglée par DayNightCycle : 0 en plein jour, 1 en pleine nuit.
// Allume les fenêtres, les vitrines et les lampadaires.
float _IvoryNight;

// Hachage 2D -> [0, 1) stable sur mobile (pas de sin() en demi-précision).
float IvoryHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

InputData IvoryInputData(float3 positionWS, half3 normalWS, float4 positionCS, half fogFactor)
{
    InputData d = (InputData)0;
    d.positionWS = positionWS;
    d.positionCS = positionCS;
    d.normalWS = NormalizeNormalPerPixel(normalWS);
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(positionWS);
#else
    d.shadowCoord = float4(0, 0, 0, 0);
#endif
    d.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), fogFactor);
    d.bakedGI = SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    return d;
}

// --- Passes d'ombre et de profondeur (communes) ---

float3 _LightDirection;
float3 _LightPosition;

struct IvoryShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float4 IvoryShadowVertex(IvoryShadowAttributes input) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    return ApplyShadowClamping(positionCS);
}

float4 IvoryDepthVertex(IvoryShadowAttributes input) : SV_POSITION
{
    UNITY_SETUP_INSTANCE_ID(input);
    return TransformObjectToHClip(input.positionOS.xyz);
}

half4 IvoryNullFragment() : SV_Target
{
    return 0;
}

#endif
