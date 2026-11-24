// Façades d'immeubles d'Ivory State, pensées pour le mobile.
//
// Un seul matériau par quartier et un seul draw call par bloc : la couleur de chaque immeuble vient de
// la couleur de sommet (RVB = teinte, A = graine aléatoire de l'immeuble), et les fenêtres sont
// dessinées en calcul (aucune texture de fenêtre, aucune géométrie) à partir de la grille de travées
// stockée dans UV1 (x = travée, y = étage ; x < 0 = mur plein, sans ouverture).
//
// UV0 est en mètres : l'enduit se répète à l'échelle réelle quelle que soit la taille de l'immeuble.
//
// Ce que le shader dessine :
//   - étages : fenêtres vitrées (reflet du ciel), persiennes fermées, encadrements, bandeaux d'étage ;
//   - rez-de-chaussée : rideaux métalliques peints, boutiques ouvertes, ou vitrines (Plateau) ;
//   - nuit (_IvoryNight, globale réglée par DayNightCycle) : une partie des fenêtres s'allume ;
//   - crasse en pied de façade.
// Coût : 3 lectures de texture par pixel (enduit, relief, rideau), le reste est arithmétique.
Shader "IvoryState/Facade"
{
    Properties
    {
        [Header(Enduit)]
        _WallMap ("Enduit (couleur)", 2D) = "white" {}
        [Normal] _WallNormal ("Enduit (relief)", 2D) = "bump" {}
        _WallScale ("Mètres par répétition de l'enduit", Float) = 2.5
        _TintBoost ("Intensité de la teinte", Float) = 1.6
        _NormalStrength ("Force du relief", Range(0, 2)) = 1
        _WallSmoothness ("Brillance de l'enduit", Range(0, 1)) = 0.12
        _Grime ("Crasse en pied de façade", Range(0, 1)) = 0.35
        _BandShade ("Teinte des bandeaux d'étage", Range(0.5, 1.5)) = 0.82

        [Header(Fenetres)]
        _WindowRect ("Fenêtre dans la travée (x0, x1, y0, y1)", Vector) = (0.28, 0.72, 0.30, 0.80)
        _FrameWidth ("Largeur de l'encadrement (en travée)", Float) = 0.035
        _FrameColor ("Encadrement", Color) = (0.86, 0.84, 0.80, 1)
        _GlassColor ("Vitre", Color) = (0.07, 0.09, 0.11, 1)
        _GlassSmoothness ("Brillance des vitres", Range(0, 1)) = 0.92
        _GlassMetallic ("Reflet des vitres", Range(0, 1)) = 0.45
        _ShutterRatio ("Part de persiennes fermées", Range(0, 1)) = 0.3
        _LouverColorA ("Persienne A", Color) = (0.45, 0.33, 0.22, 1)
        _LouverColorB ("Persienne B", Color) = (0.30, 0.42, 0.36, 1)

        [Header(Rez de chaussee)]
        _StoreRect ("Ouverture du rez-de-chaussée (x0, x1, y0, y1)", Vector) = (0.08, 0.92, 0.0, 0.78)
        _GroundFloorGlass ("Vitrines (1) ou rideaux métalliques (0)", Range(0, 1)) = 0
        [NoScaleOffset] _ShutterMap ("Rideau métallique", 2D) = "white" {}
        _ShutterScale ("Mètres par répétition du rideau", Float) = 2
        _OpenShopRatio ("Part de boutiques ouvertes", Range(0, 1)) = 0.35
        _ShopInterior ("Intérieur de boutique", Color) = (0.16, 0.12, 0.09, 1)
        _ShopColor0 ("Rideau 0", Color) = (0.20, 0.38, 0.62, 1)
        _ShopColor1 ("Rideau 1", Color) = (0.62, 0.64, 0.66, 1)
        _ShopColor2 ("Rideau 2", Color) = (0.25, 0.50, 0.30, 1)
        _ShopColor3 ("Rideau 3", Color) = (0.70, 0.22, 0.18, 1)

        [Header(Nuit)]
        [HDR] _LitColor ("Fenêtre allumée", Color) = (1.0, 0.62, 0.32, 1)
        _LitIntensity ("Intensité des fenêtres", Float) = 1.4
        _LitRatio ("Part de fenêtres allumées", Range(0, 1)) = 0.45
        [HDR] _ShopLight ("Lumière des boutiques", Color) = (1.0, 0.70, 0.40, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "IvoryCommon.hlsl"

        TEXTURE2D(_WallMap);    SAMPLER(sampler_WallMap);
        TEXTURE2D(_WallNormal); SAMPLER(sampler_WallNormal);
        TEXTURE2D(_ShutterMap); SAMPLER(sampler_ShutterMap);

        // Même CBUFFER dans toutes les passes : compatible SRP Batcher.
        CBUFFER_START(UnityPerMaterial)
            float4 _WallMap_ST;
            float _WallScale, _TintBoost, _NormalStrength, _WallSmoothness, _Grime, _BandShade;
            float4 _WindowRect;
            float _FrameWidth;
            half4 _FrameColor, _GlassColor;
            float _GlassSmoothness, _GlassMetallic, _ShutterRatio;
            half4 _LouverColorA, _LouverColorB;
            float4 _StoreRect;
            float _GroundFloorGlass, _ShutterScale, _OpenShopRatio;
            half4 _ShopInterior, _ShopColor0, _ShopColor1, _ShopColor2, _ShopColor3;
            half4 _LitColor;
            float _LitIntensity, _LitRatio;
            half4 _ShopLight;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                half4  color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  tangentWS  : TEXCOORD2;
                half4  color      : TEXCOORD3;
                float4 uv         : TEXCOORD4; // xy = mètres, zw = travée / étage
                half   fogFactor  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.color = input.color;
                o.uv = float4(input.uv0, input.uv1);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Couleur de rideau parmi 4, selon un tirage h dans [0, 1).
            half3 ShopPalette(float h)
            {
                half3 c = lerp(_ShopColor0.rgb, _ShopColor1.rgb, step(0.25, h));
                c = lerp(c, _ShopColor2.rgb, step(0.5, h));
                return lerp(c, _ShopColor3.rgb, step(0.75, h));
            }

            // Masque d'un rectangle (x0, x1, y0, y1) dans la cellule, antialiasé.
            float RectMask(float2 cell, float4 rect, float2 aa)
            {
                float x = saturate(min(cell.x - rect.x, rect.y - cell.x) / aa.x + 0.5);
                float y = saturate(min(cell.y - rect.z, rect.w - cell.y) / aa.y + 0.5);
                return x * y;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 meters = input.uv.xy;
                float2 bay = input.uv.zw;

                // --- Enduit teinté ---
                float2 wallUV = meters / _WallScale;
                half3 wallTex = SAMPLE_TEXTURE2D(_WallMap, sampler_WallMap, wallUV).rgb;
                half lum = dot(wallTex, half3(0.299, 0.587, 0.114));
                half3 albedo = min(input.color.rgb * lum * _TintBoost, 0.85);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_WallNormal, sampler_WallNormal, wallUV), _NormalStrength);
                half smoothness = _WallSmoothness;
                half metallic = 0;
                half3 emission = 0;

                // Crasse : la poussière et les éclaboussures noircissent le bas des murs.
                albedo *= lerp(1.0 - _Grime, 1.0, saturate(meters.y / 1.2));

                // Dérivées calculées hors du branchement ci-dessous (indéfinies dans un branchement
                // qui varie d'un pixel à l'autre, en bord de face).
                float2 aa = max(fwidth(bay), 1e-4);
                float2 shutterUV = meters / _ShutterScale;
                float2 shutterDx = ddx(shutterUV), shutterDy = ddy(shutterUV);

                // --- Grille des ouvertures ---
                // Murs pleins, sols, terrain, feuillage (bay.x < 0) : tout le calcul des fenêtres est
                // sauté. Sur téléphone, ces grandes surfaces dominaient le coût par pixel. Branchement
                // dynamique (pas de mot-clé, donc aucune variante de shader en plus).
                UNITY_BRANCH
                if (bay.x >= 0.0)
                {
                float windowed = 1.0;
                float2 cell = frac(bay);
                float2 id = floor(bay);
                float seed = input.color.a * 255.0;
                float ground = windowed * step(id.y, 0.5);

                float h1 = IvoryHash(id + seed * 17.0);
                float h2 = IvoryHash(id.yx * 1.7 + seed * 5.3 + 11.0);
                float h3 = IvoryHash(id * 3.1 + seed + 47.0);

                float4 rect = lerp(_WindowRect, _StoreRect, ground);
                float opening = RectMask(cell, rect, aa) * windowed;
                float4 frameRect = rect + float4(-_FrameWidth, _FrameWidth, -_FrameWidth * (1.0 - ground), _FrameWidth);
                float frame = saturate(RectMask(cell, frameRect, aa) * windowed - opening);

                // Bandeau de dalle à chaque étage (pas au rez-de-chaussée).
                float band = windowed * (1.0 - ground) * saturate((0.07 - cell.y) / aa.y + 0.5);
                albedo *= lerp(1.0, _BandShade, band);

                // --- Fenêtres des étages ---
                float closed = step(h2, _ShutterRatio);
                half3 glass = _GlassColor.rgb * lerp(0.7, 1.4, h3);
                half slat = 0.72 + 0.28 * step(0.5, frac(cell.y * 16.0));
                half3 louver = lerp(_LouverColorA.rgb, _LouverColorB.rgb, step(0.5, h3)) * slat;
                half3 winAlbedo = lerp(glass, louver, closed);
                half winSmooth = lerp(_GlassSmoothness, 0.3, closed);
                half winMetal = lerp(_GlassMetallic, 0.0, closed);
                float lit = step(h1, _LitRatio) * lerp(1.0, 0.25, closed);
                // Intensité variable d'une fenêtre à l'autre, plus forte en haut (plafonnier).
                half lamp = lerp(0.35, 1.0, frac(h1 * 7.0 + h3)) * lerp(0.75, 1.1, saturate((cell.y - rect.z) / max(rect.w - rect.z, 0.01)));
                half3 winEmission = _LitColor.rgb * (_LitIntensity * lamp * lit * _IvoryNight);

                // --- Rez-de-chaussée : rideaux peints, boutiques ouvertes ou vitrines ---
                half shutterLum = dot(SAMPLE_TEXTURE2D_GRAD(_ShutterMap, sampler_ShutterMap, shutterUV, shutterDx, shutterDy).rgb, half3(0.333, 0.333, 0.333));
                half3 shutter = ShopPalette(h3) * shutterLum * 1.7;
                float open = step(h1, _OpenShopRatio);
                half3 storeAlbedo = lerp(shutter, _ShopInterior.rgb, open);
                half storeSmooth = lerp(0.35, 0.2, open);
                half storeMetal = lerp(0.3, 0.0, open);
                float storeLit = open;

                // Vitrines (Plateau) : grandes baies vitrées, éclairées le soir.
                storeAlbedo = lerp(storeAlbedo, glass, _GroundFloorGlass);
                storeSmooth = lerp(storeSmooth, _GlassSmoothness, _GroundFloorGlass);
                storeMetal = lerp(storeMetal, _GlassMetallic, _GroundFloorGlass);
                storeLit = lerp(storeLit, step(h2, 0.7), _GroundFloorGlass);
                half3 storeEmission = _ShopLight.rgb * (storeLit * lerp(0.6, 1.0, h3) * (0.03 * (1.0 - _GroundFloorGlass) + 1.1 * _IvoryNight));

                half3 openAlbedo = lerp(winAlbedo, storeAlbedo, ground);
                half openSmooth = lerp(winSmooth, storeSmooth, ground);
                half openMetal = lerp(winMetal, storeMetal, ground);
                half3 openEmission = lerp(winEmission, storeEmission, ground);

                // --- Composition ---
                albedo = lerp(albedo, _FrameColor.rgb, frame);
                smoothness = lerp(smoothness, 0.35, frame);
                albedo = lerp(albedo, openAlbedo, opening);
                smoothness = lerp(smoothness, openSmooth, opening);
                metallic = openMetal * opening;
                emission = openEmission * opening;
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), saturate(opening + frame)));
                }

                // --- Éclairage URP ---
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
                InputData inputData = IvoryInputData(input.positionWS, normalWS, input.positionCS, input.fogFactor);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.normalTS = normalTS;
                surface.emission = emission;
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex IvoryShadowVertex
            #pragma fragment IvoryNullFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
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
