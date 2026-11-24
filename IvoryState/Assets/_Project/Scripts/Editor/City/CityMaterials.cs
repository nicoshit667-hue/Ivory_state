using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.City
{
    /// <summary>Style de façade d'un quartier (shader <c>IvoryState/Facade</c>).</summary>
    internal enum FacadeStyle
    {
        Popular,     // Yopougon, Adjamé, Treichville : enduit écaillé, petites fenêtres, rideaux métalliques
        Residential, // Marcory : immeubles d'habitation, balcons
        Villa,       // Cocody : enduit propre, grandes fenêtres, pas de boutiques
        Tower,       // Plateau : murs-rideaux vitrés
    }

    /// <summary>
    /// Matériaux réalistes de la ville, construits à partir des textures Poly Haven (CC0) de
    /// <c>ThirdParty/PolyHaven</c> (voir <c>tools/fetch-assets.ps1</c>). Recréés à chaque génération :
    /// les réglages vivent dans le code, pas dans des fichiers modifiés à la main.
    /// </summary>
    internal static class CityMaterials
    {
        public const string Folder = "Assets/_Project/Art/Materials";
        private const string TextureRoot = "Assets/_Project/ThirdParty/PolyHaven/Textures/";

        private static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

        #region Textures

        /// <summary>Texture de couleur Poly Haven (1K), importée pour le mobile.</summary>
        public static Texture2D Albedo(string id, int aniso = 1)
        {
            return LoadTexture(TextureRoot + id + "/" + id + "_diff_1k.jpg", false, aniso);
        }

        /// <summary>Carte de relief Poly Haven (convention OpenGL, comme Unity).</summary>
        public static Texture2D Normal(string id, int aniso = 1)
        {
            return LoadTexture(TextureRoot + id + "/" + id + "_nor_gl_1k.jpg", true, aniso);
        }

        public static Texture2D LoadTexture(string path, bool normalMap, int aniso, int maxSize = 1024)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[City] Texture introuvable : " + path + " (lancer tools/fetch-assets.ps1)");
                return null;
            }

            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            bool androidNeedsUpdate = !android.overridden || android.maxTextureSize != maxSize ||
                                      android.format != TextureImporterFormat.ASTC_6x6;
            if (importer.textureType != type || importer.maxTextureSize != maxSize || importer.anisoLevel != aniso ||
                importer.streamingMipmaps != true || androidNeedsUpdate)
            {
                importer.textureType = type;
                importer.maxTextureSize = maxSize;
                importer.anisoLevel = aniso;
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Compressed;
                android.overridden = true;
                android.maxTextureSize = maxSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.compressionQuality = 50;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        #endregion

        #region Matériaux

        /// <summary>Matériau URP/Lit texturé ; <paramref name="tiling"/> = répétitions par unité d'UV.</summary>
        public static Material Lit(string name, string textureId, float tiling, float smoothness, Color tint, bool normal = true, int aniso = 1)
        {
            Material material = GetOrCreate(name, LitShader);
            Texture2D albedo = Albedo(textureId, aniso);
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);

            Texture2D normalMap = normal ? Normal(textureId, aniso) : null;
            material.SetTexture("_BumpMap", normalMap);
            if (normalMap != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");

            return Save(material);
        }

        /// <summary>Matériau URP/Lit uni (marquages, métal peint…).</summary>
        public static Material Plain(string name, Color color, float smoothness)
        {
            Material material = GetOrCreate(name, LitShader);
            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            return Save(material);
        }

        /// <summary>Façades d'un quartier : un matériau partagé par tous ses immeubles (teinte par sommet).</summary>
        public static Material Facade(string name, FacadeStyle style)
        {
            Material m = GetOrCreate(name, Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            bool peeling = style == FacadeStyle.Popular;
            string wall = peeling ? "peeling_painted_wall" : "painted_plaster_wall";
            m.SetTexture("_WallMap", Albedo(wall));
            m.SetTexture("_WallNormal", Normal(wall));
            m.SetTexture("_ShutterMap", Albedo("painted_metal_shutter"));
            m.SetFloat("_WallScale", peeling ? 3f : 2.5f);
            m.SetFloat("_TintBoost", peeling ? 1.9f : 1.45f);

            switch (style)
            {
                case FacadeStyle.Popular:
                    m.SetVector("_WindowRect", new Vector4(0.30f, 0.70f, 0.33f, 0.78f));
                    m.SetFloat("_FrameWidth", 0.025f);
                    m.SetFloat("_ShutterRatio", 0.45f);
                    m.SetFloat("_GroundFloorGlass", 0f);
                    m.SetFloat("_OpenShopRatio", 0.45f);
                    m.SetFloat("_LitRatio", 0.35f);
                    m.SetFloat("_Grime", 0.5f);
                    m.SetFloat("_BandShade", 0.85f);
                    break;

                case FacadeStyle.Residential:
                    m.SetVector("_WindowRect", new Vector4(0.24f, 0.76f, 0.30f, 0.80f));
                    m.SetFloat("_FrameWidth", 0.03f);
                    m.SetFloat("_ShutterRatio", 0.3f);
                    m.SetFloat("_GroundFloorGlass", 0f);
                    m.SetFloat("_OpenShopRatio", 0.35f);
                    m.SetFloat("_LitRatio", 0.45f);
                    m.SetFloat("_Grime", 0.35f);
                    m.SetFloat("_BandShade", 0.8f);
                    break;

                case FacadeStyle.Villa:
                    m.SetVector("_WindowRect", new Vector4(0.22f, 0.78f, 0.30f, 0.82f));
                    m.SetVector("_StoreRect", new Vector4(0.22f, 0.78f, 0.12f, 0.80f));
                    m.SetFloat("_FrameWidth", 0.035f);
                    m.SetFloat("_ShutterRatio", 0.35f);
                    m.SetFloat("_GroundFloorGlass", 1f);
                    m.SetFloat("_LitRatio", 0.5f);
                    m.SetFloat("_Grime", 0.2f);
                    m.SetFloat("_BandShade", 0.9f);
                    break;

                case FacadeStyle.Tower:
                    // Mur-rideau : la vitre occupe presque toute la travée, montants sombres.
                    m.SetVector("_WindowRect", new Vector4(0.035f, 0.965f, 0.10f, 0.97f));
                    m.SetVector("_StoreRect", new Vector4(0.03f, 0.97f, 0.0f, 0.92f));
                    m.SetFloat("_FrameWidth", 0.022f);
                    m.SetColor("_FrameColor", new Color(0.62f, 0.64f, 0.66f)); // montants en aluminium
                    // Verre réfléchissant : en PBR, le reflet d'un métal prend la couleur de l'albédo,
                    // d'où un albédo clair (bleu-gris) pour refléter le ciel au lieu d'un noir mat.
                    m.SetColor("_GlassColor", new Color(0.36f, 0.46f, 0.52f));
                    m.SetFloat("_GlassMetallic", 0.65f);
                    m.SetFloat("_GlassSmoothness", 0.95f);
                    m.SetFloat("_ShutterRatio", 0f);
                    m.SetFloat("_GroundFloorGlass", 1f);
                    // Bureaux le soir : une partie seulement des plateaux reste allumée, lumière froide.
                    m.SetFloat("_LitRatio", 0.28f);
                    m.SetFloat("_LitIntensity", 0.75f);
                    m.SetColor("_LitColor", new Color(0.82f, 0.88f, 1.0f));
                    m.SetFloat("_Grime", 0.15f);
                    m.SetFloat("_BandShade", 0.55f);
                    break;
            }

            return Save(m);
        }

        /// <summary>Matériau de façade sans fenêtres, pour l'écorce des palmiers (texture de béton strié).</summary>
        public static Material Bark()
        {
            Material m = GetOrCreate("M_PalmTrunk", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("concrete"));
            m.SetTexture("_WallNormal", Normal("concrete"));
            m.SetFloat("_WallScale", 1.2f);
            m.SetFloat("_TintBoost", 1.5f);
            m.SetFloat("_Grime", 0.3f);
            return Save(m);
        }

        /// <summary>
        /// Sol naturel des abords (herbe, latérite, sable, sous-bois) : texture de béton très atténuée,
        /// teinte portée par la couleur des sommets. Même shader que les façades (sans fenêtres).
        /// </summary>
        public static Material Ground()
        {
            Material m = GetOrCreate("M_Terrain", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            // Enduit lisse (le béton a des marques de coffrage qui dessinaient des rayures sur les collines).
            m.SetTexture("_WallMap", Albedo("painted_plaster_wall"));
            m.SetTexture("_WallNormal", Normal("painted_plaster_wall"));
            m.SetFloat("_WallScale", 7f);
            m.SetFloat("_NormalStrength", 0.35f);
            m.SetFloat("_TintBoost", 1.35f);
            m.SetFloat("_Grime", 0f); // pour le sol, « le bas du mur » serait la moitié sud de la carte
            return Save(m);
        }

        /// <summary>Feuillage des arbres : facettes teintées par sommet (plusieurs verts dans un seul matériau).</summary>
        public static Material Foliage()
        {
            Material m = GetOrCreate("M_Foliage", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("concrete"));
            m.SetTexture("_WallNormal", Normal("concrete"));
            m.SetFloat("_WallScale", 0.8f);
            m.SetFloat("_TintBoost", 1.7f);
            m.SetFloat("_Grime", 0f);
            return Save(m);
        }

        /// <summary>Monuments, gbakas, mobilier : façade sans fenêtres teintée par sommet (enduit lisse).</summary>
        public static Material Painted()
        {
            Material m = GetOrCreate("M_Painted", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("painted_plaster_wall"));
            m.SetTexture("_WallNormal", Normal("painted_plaster_wall"));
            m.SetFloat("_WallScale", 2.5f);
            m.SetFloat("_TintBoost", 1.45f);
            m.SetFloat("_Grime", 0.15f);
            return Save(m);
        }

        /// <summary>Toitures en tuiles ou tôle ondulée (sans fenêtres, teinte par sommet).</summary>
        public static Material Roof(string name, string textureId, float scale = 2.0f, float smoothness = 0.2f)
        {
            Material m = GetOrCreate(name, Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo(textureId));
            m.SetTexture("_WallNormal", Normal(textureId));
            m.SetFloat("_WallScale", scale);
            m.SetFloat("_NormalStrength", 1.2f);
            m.SetFloat("_WallSmoothness", smoothness);
            m.SetFloat("_TintBoost", 2.55f);
            m.SetFloat("_Grime", 0.06f);
            return Save(m);
        }

        /// <summary>Béton architectural (bordures, piles de pont, socles, caniveaux).</summary>
        public static Material Concrete()
        {
            Material m = GetOrCreate("M_CityConcrete", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("concrete"));
            m.SetTexture("_WallNormal", Normal("concrete"));
            m.SetFloat("_WallScale", 2.2f);
            m.SetFloat("_NormalStrength", 0.85f);
            m.SetFloat("_WallSmoothness", 0.14f);
            m.SetFloat("_TintBoost", 1.85f);
            m.SetFloat("_Grime", 0.08f);
            return Save(m);
        }

        /// <summary>Boiseries et persiennes (teinte portée par la couleur de sommet).</summary>
        public static Material Wood()
        {
            Material m = GetOrCreate("M_CityWood", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("laminate_floor_02"));
            m.SetTexture("_WallNormal", Normal("laminate_floor_02"));
            m.SetFloat("_WallScale", 1.5f);
            m.SetFloat("_NormalStrength", 0.7f);
            m.SetFloat("_WallSmoothness", 0.28f);
            m.SetFloat("_TintBoost", 2.15f);
            m.SetFloat("_Grime", 0.04f);
            return Save(m);
        }

        /// <summary>Vitrage architectural 3D : reflets le jour, persiennes et éclairage aléatoire la nuit.</summary>
        public static Material BuildingGlass()
        {
            Material m = GetOrCreate("M_BuildingGlass", Shader.Find("IvoryState/Facade"));
            ResetToDefaults(m);
            m.SetTexture("_WallMap", Albedo("painted_plaster_wall"));
            m.SetTexture("_WallNormal", Normal("painted_plaster_wall"));
            m.SetTexture("_ShutterMap", Albedo("painted_metal_shutter"));
            m.SetVector("_WindowRect", new Vector4(0f, 1f, 0f, 1f));
            m.SetVector("_StoreRect", new Vector4(0f, 1f, 0f, 1f));
            m.SetFloat("_FrameWidth", 0f);
            m.SetColor("_GlassColor", new Color(0.24f, 0.34f, 0.42f));
            m.SetFloat("_GlassMetallic", 0.62f);
            m.SetFloat("_GlassSmoothness", 0.94f);
            m.SetFloat("_ShutterRatio", 0.20f);
            m.SetFloat("_GroundFloorGlass", 1f);
            m.SetFloat("_LitRatio", 0.40f);
            m.SetFloat("_LitIntensity", 1.15f);
            m.SetFloat("_BandShade", 1f);
            m.SetFloat("_Grime", 0f);
            return Save(m);
        }

        /// <summary>Métal URP PBR partagé (lampadaires, ferronnerie, huisseries).</summary>
        public static Material Metal(string name, Color color, float metallic, float smoothness)
        {
            Material material = GetOrCreate(name, LitShader);
            material.SetTexture("_BaseMap", null);
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return Save(material);
        }

        public static Material Water()
        {
            return Save(GetOrCreate("M_Lagoon", Shader.Find("IvoryState/Water")));
        }

        /// <summary>
        /// Lumière de nuit (ampoules, halos au sol). <paramref name="additive"/> : halo transparent
        /// additif, sinon ampoule opaque.
        /// </summary>
        public static Material NightLight(string name, Color color, float dayIntensity, float nightIntensity, Texture2D mask, bool additive)
        {
            Material m = GetOrCreate(name, Shader.Find("IvoryState/NightLight"));
            m.SetColor("_Color", color);
            m.SetFloat("_DayIntensity", dayIntensity);
            m.SetFloat("_NightIntensity", nightIntensity);
            m.SetTexture("_MainTex", mask);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.Zero));
            m.SetFloat("_ZWrite", additive ? 0f : 1f);
            m.renderQueue = additive ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            return Save(m);
        }

        public static Material GetOrCreate(string name, Shader shader)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets/_Project/Art", "Materials");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        /// <summary>Remet toutes les propriétés aux valeurs par défaut du shader (réglages = code uniquement).</summary>
        private static void ResetToDefaults(Material material)
        {
            var defaults = new Material(material.shader);
            material.CopyPropertiesFromMaterial(defaults);
            Object.DestroyImmediate(defaults);
        }

        private static Material Save(Material material)
        {
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        #endregion
    }
}
