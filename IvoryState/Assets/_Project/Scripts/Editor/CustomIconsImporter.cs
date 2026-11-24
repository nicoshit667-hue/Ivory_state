using System;
using System.Collections.Generic;
using System.IO;
using IvoryState.Client.UI;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor
{
    /// <summary>
    /// Configure automatiquement toutes les icônes UI importées sous
    /// <c>Assets/Art/UI/Icons/</c> (<c>Phone/</c>, <c>Consumables/</c>, <c>Profile/</c>) et
    /// <c>Assets/_Project/Resources/UI/Icons/</c> en <b>Sprite (2D and UI) / Single</b> avec
    /// réglages mobile adaptés (Read/Write désactivé, sans mipmaps, compression ASTC Android),
    /// puis synchronise <c>Assets/_Project/Resources/UI/UiIconDatabase.asset</c>.
    /// </summary>
    public sealed class CustomIconsImporter : AssetPostprocessor
    {
        public const string IconsRootFolder = "Assets/_Project/Resources/UI/Icons/";
        public const string ArtIconsRootFolder = "Assets/Art/UI/Icons/";
        public const string DatabaseAssetPath = "Assets/_Project/Resources/UI/UiIconDatabase.asset";

        private static bool s_syncQueued;

        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            bool isArtIcon = assetPath.StartsWith(ArtIconsRootFolder, StringComparison.OrdinalIgnoreCase);
            bool isLegacyIcon = assetPath.StartsWith(IconsRootFolder, StringComparison.OrdinalIgnoreCase);
            if (!isArtIcon && !isLegacyIcon)
                return;

            var importer = (TextureImporter)assetImporter;
            ApplySpriteSettings(importer, assetPath, isArtIcon);
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (s_syncQueued)
                return;

            if (ContainsArtIcon(importedAssets) || ContainsArtIcon(deletedAssets) ||
                ContainsArtIcon(movedAssets) || ContainsArtIcon(movedFromAssetPaths))
            {
                s_syncQueued = true;
                EditorApplication.delayCall += RunDelayedSync;
            }
        }

        private static bool ContainsArtIcon(string[] paths)
        {
            if (paths == null)
                return false;
            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]) &&
                    paths[i].StartsWith(ArtIconsRootFolder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void RunDelayedSync()
        {
            s_syncQueued = false;
            SyncIconDatabase();
        }

        [MenuItem("Ivory State/Sprites/Reimporter et synchroniser les icones UI")]
        public static void ReimportAllCustomIcons()
        {
            int count = 0;
            count += ReimportFolder(ArtIconsRootFolder.TrimEnd('/'), isArtIcon: true);
            count += ReimportFolder(IconsRootFolder.TrimEnd('/'), isArtIcon: false);
            SyncIconDatabase();
            AssetDatabase.SaveAssets();
            Debug.Log("[CustomIcons] " + count + " icone(s) configuree(s) en Sprite (2D and UI) / Single et base synchronisee.");
        }

        private static int ReimportFolder(string folder, bool isArtIcon)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return 0;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                if (ApplySpriteSettings(importer, path, isArtIcon))
                    importer.SaveAndReimport();
                count++;
            }
            return count;
        }

        public static void SyncIconDatabase()
        {
            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder("Assets/_Project/Resources/UI");

            UiIconDatabase db = AssetDatabase.LoadAssetAtPath<UiIconDatabase>(DatabaseAssetPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<UiIconDatabase>();
                AssetDatabase.CreateAsset(db, DatabaseAssetPath);
            }

            db.TaxiAppIcon = AssetDatabase.LoadAssetAtPath<Sprite>(ArtIconsRootFolder + "Phone/taxi.png");
            db.CommerceAppIcon = AssetDatabase.LoadAssetAtPath<Sprite>(ArtIconsRootFolder + "Profile/icon_commerce.png");
            db.Consumables = CollectSprites(ArtIconsRootFolder + "Consumables", excludeJpg: true);
            db.ProfileIcons = CollectSprites(ArtIconsRootFolder + "Profile", excludeJpg: true);
            db.ProfileBackgrounds = CollectSprites(ArtIconsRootFolder + "Profile", onlyJpg: true);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
        }

        private static UiIconDatabase.NamedSprite[] CollectSprites(string folder, bool excludeJpg = false, bool onlyJpg = false)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return Array.Empty<UiIconDatabase.NamedSprite>();

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            var list = new List<UiIconDatabase.NamedSprite>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string ext = Path.GetExtension(path);
                bool isJpg = string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
                if (excludeJpg && isJpg)
                    continue;
                if (onlyJpg && !isJpg)
                    continue;

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    continue;

                list.Add(new UiIconDatabase.NamedSprite
                {
                    Key = Path.GetFileNameWithoutExtension(path),
                    Sprite = sprite
                });
            }

            list.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return list.ToArray();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        private static bool ApplySpriteSettings(TextureImporter importer, string path, bool isArtIcon)
        {
            bool isJpg = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
            bool changed = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (Math.Abs(importer.spritePixelsPerUnit - 100f) > 0.01f)
            {
                importer.spritePixelsPerUnit = 100f;
                changed = true;
            }

            TextureImporterAlphaSource desiredAlpha = isJpg
                ? TextureImporterAlphaSource.None
                : TextureImporterAlphaSource.FromInput;
            if (importer.alphaSource != desiredAlpha)
            {
                importer.alphaSource = desiredAlpha;
                changed = true;
            }
            bool desiredAlphaTrans = !isJpg;
            if (importer.alphaIsTransparency != desiredAlphaTrans)
            {
                importer.alphaIsTransparency = desiredAlphaTrans;
                changed = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }
            if (importer.isReadable)
            {
                importer.isReadable = false;
                changed = true;
            }
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }
            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                changed = true;
            }
            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            int desiredMaxSize = isJpg ? 1024 : (isArtIcon ? 512 : 1024);
            if (importer.maxTextureSize != desiredMaxSize)
            {
                importer.maxTextureSize = desiredMaxSize;
                changed = true;
            }

            TextureImporterCompression desiredCompression = isArtIcon
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;
            if (importer.textureCompression != desiredCompression)
            {
                importer.textureCompression = desiredCompression;
                changed = true;
            }

            if (isArtIcon)
            {
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                TextureImporterFormat desiredFormat = isJpg ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
                int desiredQuality = isJpg ? 70 : 85;
                if (!android.overridden || android.maxTextureSize != desiredMaxSize ||
                    android.format != desiredFormat || android.compressionQuality != desiredQuality)
                {
                    android.name = "Android";
                    android.overridden = true;
                    android.maxTextureSize = desiredMaxSize;
                    android.format = desiredFormat;
                    android.compressionQuality = desiredQuality;
                    importer.SetPlatformTextureSettings(android);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
