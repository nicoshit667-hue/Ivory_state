using System.IO;
using IvoryState.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor
{
    /// <summary>
    /// Préfabs des armes à feu tenues en main (<c>Resources/Weapons/&lt;modèle&gt;.prefab</c>), chargés par
    /// <see cref="PlayerCombat"/> sur tous les clients.
    /// <list type="bullet">
    /// <item>Source : <c>ThirdParty/Weapons/Models/&lt;id&gt;/&lt;id&gt;_1k.fbx</c> (axe long = X après conversion Blender).</item>
    /// <item>Le canon est tourné vers +Z (l'avant du joueur) : le bout le moins haut de l'arme est le canon
    /// (la crosse ou la poignée descendent plus bas).</item>
    /// <item>Pivot vers l'arrière (la main), taille réelle (<see cref="FirearmDef.Length"/>), un matériau URP Lit.</item>
    /// <item>Matériaux des traçantes (balle) et du fil du taser.</item>
    /// </list>
    /// </summary>
    internal static class WeaponPrefabBuilder
    {
        private const string SourceRoot = "Assets/_Project/ThirdParty/Weapons/Models/";
        private const string OutputRoot = "Assets/_Project/Resources/Weapons/";

        [MenuItem("Ivory State/Armes/Construire les préfabs d'armes")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(OutputRoot);
            foreach (FirearmDef def in Firearms.All)
                Build(def);
            BuildLineMaterial("M_Tracer", new Color(1f, 0.85f, 0.45f, 1f));
            BuildLineMaterial("M_TaserWire", new Color(0.55f, 0.85f, 1f, 1f));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Armes] Préfabs construits dans " + OutputRoot);
        }

        private static void Build(FirearmDef def)
        {
            string id = def.Model;
            string fbxPath = SourceRoot + id + "/" + id + "_1k.fbx";
            if (AssetImporter.GetAtPath(fbxPath) is ModelImporter importer)
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                // Matériaux : laissés au réglage de PropLibrary (vitrine de l'armurerie) ; le préfab a les siens.
                importer.SaveAndReimport();
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
            {
                Debug.LogWarning("[Armes] Modèle introuvable : " + fbxPath);
                return;
            }

            Material material = BuildMaterial(id, def);

            var root = new GameObject(id);
            var model = (GameObject)Object.Instantiate(source, root.transform);
            model.name = "Modele";
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            // Analyse de la forme, dans le repère de la racine.
            Bounds bounds = MeasureBounds(model.transform, root.transform, out float extentMinX, out float extentMaxX);
            bool muzzleAtPlusX = extentMaxX <= extentMinX;
            float length = Mathf.Max(bounds.size.x, 0.001f);
            float scale = def.Length / length;

            // Pivot : à 30 % de la longueur depuis l'arrière, au milieu de la hauteur (≈ la main).
            float rearX = muzzleAtPlusX ? bounds.min.x : bounds.max.x;
            float gripX = rearX + (muzzleAtPlusX ? 1f : -1f) * length * 0.3f;
            var grip = new Vector3(gripX, bounds.center.y, bounds.center.z);

            Quaternion turn = Quaternion.Euler(0f, muzzleAtPlusX ? -90f : 90f, 0f);
            model.transform.localScale = model.transform.localScale * scale;
            model.transform.localRotation = turn * model.transform.localRotation;
            model.transform.localPosition = turn * (model.transform.localPosition - grip) * scale;

            string prefabPath = OutputRoot + id + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Armes] {id} : longueur source {length:F3} → {def.Length:F3} m, canon côté {(muzzleAtPlusX ? "+X" : "-X")} " +
                      $"(hauteur aux bouts : -X {extentMinX:F3}, +X {extentMaxX:F3})");
        }

        /// <summary>Boîte englobante des sommets et hauteur de l'arme à chaque bout (12 % de la longueur).</summary>
        private static Bounds MeasureBounds(Transform model, Transform root, out float extentMinX, out float extentMaxX)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            bool first = true;
            var bounds = new Bounds();
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(v);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                        bounds.Encapsulate(p);
                }
            }

            float band = bounds.size.x * 0.12f;
            float minLowY = float.MaxValue, minHighY = float.MinValue, maxLowY = float.MaxValue, maxHighY = float.MinValue;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(v);
                    if (p.x <= bounds.min.x + band)
                    {
                        minLowY = Mathf.Min(minLowY, p.y);
                        minHighY = Mathf.Max(minHighY, p.y);
                    }
                    if (p.x >= bounds.max.x - band)
                    {
                        maxLowY = Mathf.Min(maxLowY, p.y);
                        maxHighY = Mathf.Max(maxHighY, p.y);
                    }
                }
            }
            extentMinX = minHighY - minLowY;
            extentMaxX = maxHighY - maxLowY;
            return bounds;
        }

        private static Material BuildMaterial(string id, FirearmDef def)
        {
            string path = OutputRoot + "M_Weapon_" + id + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            string texDir = SourceRoot + id + "/textures/";
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + id + "_diff_1k.jpg"));
            material.SetColor("_BaseColor", Color.white);

            string normalPath = texDir + id + "_nor_gl_1k.jpg";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter normalImporter && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");

            material.SetFloat("_Metallic", def.Stun ? 0.15f : 0.75f);
            material.SetFloat("_Smoothness", def.Stun ? 0.35f : 0.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildLineMaterial(string name, Color color)
        {
            string path = OutputRoot + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
        }
    }
}
