using System.Collections.Generic;
using System.IO;
using IvoryState.Client.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.Characters
{
    /// <summary>
    /// Prépare les personnages réalistes (Microsoft Rocketbox, MIT) téléchargés par
    /// <c>tools/fetch-characters.ps1</c> :
    /// <list type="bullet">
    /// <item>import Humanoid (squelette Bip01 de 3ds Max, reconnu automatiquement) ;</item>
    /// <item>matériaux URP : corps et tête (couleur + relief), cheveux et cils (alpha test) ;</item>
    /// <item>Animator : arbre de mélange attente / marche / course piloté par « Speed » ;</item>
    /// <item>un prefab par avatar et le catalogue <see cref="CharacterModels"/>.</item>
    /// </list>
    /// S'y ajoutent les personnages MPFB2 (MakeHuman, CC0) exportés par <c>tools/blender/mpfb</c>
    /// dans <c>ThirdParty/MPFB/Avatars</c> : placés en tête de liste, ce sont les apparences par défaut.
    /// Mobile : os limités à 2 influences par sommet, maillage compressé, ombres portées seulement
    /// pour les joueurs (pas pour les passants).
    /// </summary>
    public static class CharacterSetup
    {
        private const string Root = "Assets/_Project/ThirdParty/Rocketbox";
        private const string MpfbRoot = "Assets/_Project/ThirdParty/MPFB/Avatars";
        private const string AnimationFolder = Root + "/Animations";
        private const string OutputFolder = "Assets/_Project/Art/Characters";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Characters/Avatars";
        public const string ModelsPath = "Assets/_Project/Settings/CharacterModels.asset";

        // Vitesses au sol (m/s) auxquelles les clips sont joués sans glissement des pieds.
        private const float WalkSpeed = 1.45f;
        private const float RunSpeed = 4.8f;

        [MenuItem("Ivory State/Setup/Préparer les personnages", priority = 10)]
        public static CharacterModels Setup()
        {
            var models = AssetDatabase.LoadAssetAtPath<CharacterModels>(ModelsPath);
            if (models == null)
            {
                models = ScriptableObject.CreateInstance<CharacterModels>();
                EnsureFolder(Path.GetDirectoryName(ModelsPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(models, ModelsPath);
            }

            string avatarRoot = Root + "/Avatars";
            if (!AssetDatabase.IsValidFolder(avatarRoot))
            {
                Debug.LogWarning("[Characters] Aucun avatar : lancer tools/fetch-characters.ps1. Les capsules restent en place.");
                models.EditorSetModels(new GameObject[0]);
                EditorUtility.SetDirty(models);
                return models;
            }

            EnsureFolder(OutputFolder);
            EnsureFolder(PrefabFolder);
            AnimatorController male = BuildController("AC_Citizen_Male", "m_");
            AnimatorController female = BuildController("AC_Citizen_Female", "f_");

            // Ordre alterné homme / femme : les identifiants consécutifs donnent des apparences variées.
            var males = new List<GameObject>();
            var females = new List<GameObject>();
            foreach (string folder in AssetDatabase.GetSubFolders(avatarRoot))
            {
                string name = Path.GetFileName(folder);
                bool isFemale = name.StartsWith("Female");
                GameObject prefab = BuildAvatar(folder, name, isFemale ? female : male);
                if (prefab != null)
                    (isFemale ? females : males).Add(prefab);
            }

            // Personnages MPFB en tête : l'écran de création (visage 0 + coiffure 0) les propose
            // d'abord, ce sont l'homme et la femme par défaut.
            if (AssetDatabase.IsValidFolder(MpfbRoot))
            {
                string[] mpfbFolders = AssetDatabase.GetSubFolders(MpfbRoot);
                for (int i = mpfbFolders.Length - 1; i >= 0; i--)
                {
                    string name = Path.GetFileName(mpfbFolders[i]);
                    bool isFemale = name.StartsWith("Female");
                    GameObject prefab = BuildAvatar(mpfbFolders[i], name, isFemale ? female : male, true);
                    if (prefab != null)
                        (isFemale ? females : males).Insert(0, prefab);
                }
            }

            var ordered = new List<GameObject>();
            for (int i = 0; i < Mathf.Max(males.Count, females.Count); i++)
            {
                if (i < males.Count) ordered.Add(males[i]);
                if (i < females.Count) ordered.Add(females[i]);
            }

            models.EditorSetModels(ordered.ToArray());
            BuildOutfits(models, ordered);
            EditorUtility.SetDirty(models);
            AssetDatabase.SaveAssets();
            Debug.Log("[Characters] " + ordered.Count + " avatars prêts.");
            return models;
        }

        /// <summary>Entrée ligne de commande (Unity -batchmode -executeMethod).</summary>
        public static void SetupBatch()
        {
            Setup();
            AssetDatabase.SaveAssets();
        }

        #region Tenues

        // Tenues de rechange : teinte cible des vêtements (vert, bleu), ou -1 pour une tenue sombre.
        private static readonly float[] OutfitHues = { 0.38f, 0.62f, -1f };

        /// <summary>
        /// Variantes de couleur du matériau du corps de chaque avatar : seuls les vêtements changent.
        /// La peau (teintes chaudes, saturation moyenne, rouge &gt; vert &gt; bleu) est laissée intacte.
        /// </summary>
        private static void BuildOutfits(CharacterModels models, List<GameObject> ordered)
        {
            var bodies = new Material[ordered.Count];
            var outfits = new Material[ordered.Count * CharacterModels.OutfitVariants];
            for (int m = 0; m < ordered.Count; m++)
            {
                string avatar = ordered[m].name.Replace("PF_Avatar_", "");
                // MPFB : peau et vêtements sont des matériaux séparés, ce sont les vêtements qu'on teinte.
                Material body = AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/M_" + avatar + "_clothes.mat")
                                ?? AssetDatabase.LoadAssetAtPath<Material>(OutputFolder + "/M_" + avatar + "_body.mat");
                bodies[m] = body;
                var source = body != null ? body.GetTexture("_BaseMap") as Texture2D : null;
                string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                if (string.IsNullOrEmpty(sourcePath))
                    continue;

                var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                image.LoadImage(File.ReadAllBytes(sourcePath));
                Color32[] pixels = image.GetPixels32();

                for (int v = 0; v < CharacterModels.OutfitVariants; v++)
                {
                    var tinted = new Color32[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++)
                        tinted[i] = Recolor(pixels[i], OutfitHues[v]);

                    var result = new Texture2D(image.width, image.height, TextureFormat.RGB24, false);
                    result.SetPixels32(tinted);
                    string texturePath = OutputFolder + "/T_" + avatar + "_tenue" + (v + 1) + ".png";
                    File.WriteAllBytes(Path.GetFullPath(texturePath), result.EncodeToPNG());
                    Object.DestroyImmediate(result);
                    AssetDatabase.ImportAsset(texturePath);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    importer.maxTextureSize = 1024;
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.SaveAndReimport();

                    string materialPath = OutputFolder + "/M_" + avatar + "_body_tenue" + (v + 1) + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(body);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    material.CopyPropertiesFromMaterial(body);
                    material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                    EditorUtility.SetDirty(material);
                    outfits[m * CharacterModels.OutfitVariants + v] = material;
                }
                Object.DestroyImmediate(image);
            }
            models.EditorSetOutfits(bodies, outfits);
        }

        private static Color32 Recolor(Color32 pixel, float hue)
        {
            Color c = pixel;
            Color.RGBToHSV(c, out float h, out float s, out float val);
            bool skin = c.r > c.g && c.g > c.b && h < 0.14f && s > 0.15f && s < 0.75f && val > 0.12f;
            if (skin || val < 0.04f)
                return pixel;
            Color result = hue < 0f
                ? Color.HSVToRGB(h, s * 0.15f, val * 0.45f)                    // tenue sombre
                : Color.HSVToRGB(hue, Mathf.Clamp(s * 0.8f + 0.25f, 0f, 0.85f), val);
            return result;
        }

        #endregion

        #region Avatars

        private static GameObject BuildAvatar(string folder, string name, AnimatorController controller, bool mpfb = false)
        {
            string fbx = folder + "/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null)
                return null;

            bool changed = importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation ||
                           importer.importCameras || importer.importLights || importer.isReadable;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 2;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            // Matériaux : remplacés par des matériaux URP construits à partir des textures 1K.
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (asset is Material embedded)
                {
                    Material material = mpfb ? BuildMpfbMaterial(folder, name, embedded.name)
                                             : BuildMaterial(folder, name, embedded.name);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
                    changed = true;
                }
            }

            // Réimport : les matériaux sont déjà remappés et n'apparaissent plus comme intégrés ;
            // on remet quand même à jour les réglages des matériaux MPFB.
            if (mpfb)
            {
                foreach (var remap in importer.GetExternalObjectMap())
                {
                    if (remap.Key.type == typeof(Material))
                        BuildMpfbMaterial(folder, name, remap.Key.name);
                }
            }

            if (changed)
                importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null)
                return null;

            Avatar avatarAsset = AssetDatabase.LoadAssetAtPath<Avatar>(fbx);
            if (avatarAsset == null || !avatarAsset.isValid || !avatarAsset.isHuman)
                Debug.LogWarning("[Characters] Avatar Humanoid invalide pour " + name + " : l'animation ne s'appliquera pas.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "Avatar_" + name;
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer skinned)
                {
                    skinned.quality = SkinQuality.Bone2;
                    skinned.updateWhenOffscreen = false;
                    skinned.skinnedMotionVectors = false;
                }
            }

            // Disparaît quand il ne fait plus que quelques pixels : on ne dessine pas de foule lointaine.
            var lod = instance.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.03f, renderers) });

            string path = PrefabFolder + "/PF_Avatar_" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// Matériau URP d'une partie de l'avatar. Rocketbox nomme ses matériaux d'après ses textures
        /// (« m007_body », « m007_head », « m007_opacity »…).
        /// </summary>
        private static Material BuildMaterial(string folder, string avatar, string sourceName)
        {
            string lower = sourceName.ToLowerInvariant();
            string part = lower.Contains("opacity") || lower.Contains("hair") || lower.Contains("lash") ? "opacity"
                : lower.Contains("head") ? "head" : "body";

            string textures = folder + "/Textures";
            string prefix = FindPrefix(textures);
            string path = OutputFolder + "/M_" + avatar + "_" + part + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            if (part == "opacity")
            {
                material.SetTexture("_BaseMap", LoadTexture(textures + "/" + prefix + "_opacity_color.png", false, true));
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.4f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetFloat("_Smoothness", 0.3f);
            }
            else
            {
                material.SetTexture("_BaseMap", LoadTexture(textures + "/" + prefix + "_" + part + "_color.jpg", false, false));
                Texture2D normal = LoadTexture(textures + "/" + prefix + "_" + part + "_normal.png", true, false);
                material.SetTexture("_BumpMap", normal);
                if (normal != null)
                    material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Smoothness", part == "head" ? 0.38f : 0.22f);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Matériau URP d'une partie d'un personnage MPFB. Le script Blender nomme chaque matériau
        /// d'après sa partie (« body », « hair », « clothes », « shoes »…) et range ses textures dans
        /// Textures/&lt;partie&gt;_color.png et Textures/&lt;partie&gt;_normal.png.
        /// </summary>
        private static Material BuildMpfbMaterial(string folder, string avatar, string part)
        {
            string textures = folder + "/Textures";
            string path = OutputFolder + "/M_" + avatar + "_" + part + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            // Cheveux, sourcils et cils : cartes de mèches en alpha test, visibles des deux côtés.
            bool cutout = part.StartsWith("hair") || part.StartsWith("eyebrows") || part.StartsWith("eyelashes");
            // Cheveux MakeHuman : reflets trop clairs sous l'éclairage du jeu, assombris.
            material.SetColor("_BaseColor", part.StartsWith("hair") ? new Color(0.5f, 0.47f, 0.45f) : Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetTexture("_BaseMap", LoadTexture(textures + "/" + part + "_color.png", false, cutout));
            Texture2D normal = cutout ? null : LoadTexture(textures + "/" + part + "_normal.png", true, false);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");

            if (cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                // Mèches MakeHuman très translucides : seuil bas pour ne pas trouer la chevelure.
                material.SetFloat("_Cutoff", part.StartsWith("hair") ? 0.12f : 0.45f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
            material.SetFloat("_Smoothness", part == "eyes" ? 0.8f : part == "body" ? 0.35f : 0.2f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string FindPrefix(string textureFolder)
        {
            foreach (string guid in AssetDatabase.FindAssets("_body_color", new[] { textureFolder }))
            {
                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                return file.Substring(0, file.IndexOf("_body_color"));
            }
            return "";
        }

        private static Texture2D LoadTexture(string path, bool normalMap, bool alpha)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return null;

            // Reliefs en 512 : détail des plis encore visible à l'écran d'un téléphone, 4 fois moins lourd.
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            int maxSize = normalMap ? 512 : 1024;
            if (importer.textureType != type || importer.maxTextureSize != maxSize || importer.alphaIsTransparency != alpha)
            {
                importer.textureType = type;
                importer.maxTextureSize = maxSize;
                importer.alphaIsTransparency = alpha;
                importer.mipMapsPreserveCoverage = alpha;
                importer.alphaTestReferenceValue = 0.4f;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        #endregion

        #region Animations

        /// <summary>
        /// Contrôleur : un seul état, arbre de mélange 1D attente / marche / course sur « Speed ».
        /// Pas de transitions : aucun coût de machine à états.
        /// </summary>
        private static AnimatorController BuildController(string name, string clipPrefix)
        {
            AnimationClip idle = LoadClip(clipPrefix + "idle_neutral_01");
            AnimationClip walk = LoadClip(clipPrefix + "walk_neutral_01");
            AnimationClip run = LoadClip(clipPrefix + "run_neutral_01");

            string path = OutputFolder + "/" + name + ".controller";
            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("CycleOffset", AnimatorControllerParameterType.Float);

            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, WalkSpeed);
            if (run != null) tree.AddChild(run, RunSpeed);

            state.cycleOffsetParameter = "CycleOffset";
            state.cycleOffsetParameterActive = true;
            return controller;
        }

        private static AnimationClip LoadClip(string name)
        {
            string fbx = AnimationFolder + "/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[Characters] Animation absente : " + fbx);
                return null;
            }

            if (importer.animationType != ModelImporterAnimationType.Human || importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = true;
                importer.SaveAndReimport();
            }

            // Boucle, et déplacement « cuit » dans la pose : l'animation reste sur place,
            // c'est le CharacterController (ou l'agent) qui déplace le personnage.
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            // Rotation et position calées sur le corps (orientation du buste, centre de masse) et non
            // sur la racine d'origine du clip : les captures Rocketbox dérivent légèrement en cap et sur
            // le côté, ce qui faisait « zigzaguer » le personnage en courant.
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = false;
                clip.heightFromFeet = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    if (!clip.humanMotion)
                        Debug.LogWarning("[Characters] Clip non Humanoid : " + fbx);
                    Debug.Log("[Characters] " + name + " : durée " + clip.length.ToString("0.00") + " s, vitesse moyenne " +
                              clip.averageSpeed.ToString("F3") + ", rotation moyenne " + clip.averageAngularSpeed.ToString("0.0") + " °/s");
                    return clip;
                }
            }
            return null;
        }

        #endregion

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
