using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Cocotiers générés en code (les arbres Poly Haven pèsent des centaines de milliers de triangles).
    /// <para>
    /// Un cocotier : tronc légèrement penché (cylindre à 6 pans), couronne de 10 à 13 palmes en
    /// rubans pliés en V. La découpe des folioles vient d'une texture générée (alpha test), d'où
    /// environ 250 triangles par arbre. Trois variantes, enregistrées dans un seul asset.
    /// </para>
    /// </summary>
    internal static class PalmBuilder
    {
        public const string MeshAssetPath = "Assets/_Project/Art/Generated/Palms.asset";
        public const string FrondTexturePath = "Assets/_Project/Art/Generated/T_PalmFrond.png";
        public const int VariantCount = 3;

        private static Mesh[] s_meshes;
        private static Material[] s_materials;

        /// <summary>Crée (ou recrée) les maillages, la texture et les matériaux des cocotiers.</summary>
        public static void Prepare()
        {
            Texture2D frondTexture = BuildFrondTexture();

            Material frond = CityMaterials.GetOrCreate("M_PalmFrond", Shader.Find("Universal Render Pipeline/Lit"));
            frond.SetTexture("_BaseMap", frondTexture);
            frond.SetColor("_BaseColor", Color.white);
            frond.SetFloat("_Smoothness", 0.3f);
            frond.SetFloat("_AlphaClip", 1f);
            frond.SetFloat("_Cutoff", 0.5f);
            frond.SetFloat("_Cull", (float)CullMode.Off);
            frond.EnableKeyword("_ALPHATEST_ON");
            frond.SetOverrideTag("RenderType", "TransparentCutout");
            frond.renderQueue = (int)RenderQueue.AlphaTest;
            frond.doubleSidedGI = true;
            frond.enableInstancing = true;
            EditorUtility.SetDirty(frond);

            s_materials = new[] { CityMaterials.Bark(), frond };

            var random = new System.Random(4242);
            s_meshes = new Mesh[VariantCount];
            for (int i = 0; i < VariantCount; i++)
                s_meshes[i] = BuildPalmMesh(i, random);

            AssetDatabase.DeleteAsset(MeshAssetPath);
            AssetDatabase.CreateAsset(s_meshes[0], MeshAssetPath);
            for (int i = 1; i < VariantCount; i++)
                AssetDatabase.AddObjectToAsset(s_meshes[i], s_meshes[0]);
        }

        /// <summary>Place un cocotier (statique, ombre portée, masqué de loin).</summary>
        public static GameObject Place(Transform parent, Vector3 localPosition, int variant, float yaw)
        {
            var go = new GameObject("Cocotier", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<MeshFilter>().sharedMesh = s_meshes[variant % VariantCount];
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = s_materials;
            go.isStatic = true;

            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { renderer }) });

            // Tronc solide (le bas du tronc, quasi vertical : la courbure n'apparaît qu'en hauteur).
            var trunk = go.AddComponent<CapsuleCollider>();
            trunk.radius = 0.25f;
            trunk.height = 4f;
            trunk.center = new Vector3(0f, 2f, 0f);
            return go;
        }

        #region Maillage

        private static Mesh BuildPalmMesh(int variant, System.Random random)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var colors = new List<Color32>();
            var trunkTriangles = new List<int>();
            var frondTriangles = new List<int>();

            float height = 6.5f + variant * 1.4f + (float)random.NextDouble();
            float lean = 0.5f + variant * 0.35f;
            const int rings = 6, sides = 6;
            var barkColor = new Color32(196, 172, 146, (byte)random.Next(256));

            // Tronc : centre qui s'écarte en arc (penché vers +X), rayon qui s'affine vers le haut.
            Vector3 Center(float t) => new Vector3(lean * t * t, height * t, 0f);
            for (int ring = 0; ring <= rings; ring++)
            {
                float t = ring / (float)rings;
                float radius = Mathf.Lerp(0.22f, 0.14f, t) + (t < 0.12f ? 0.1f * (1f - t / 0.12f) : 0f);
                Vector3 c = Center(t);
                for (int side = 0; side <= sides; side++)
                {
                    float a = side * Mathf.PI * 2f / sides;
                    var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vertices.Add(c + n * radius);
                    normals.Add(n);
                    tangents.Add(new Vector4(-n.z, 0f, n.x, 1f));
                    uv0.Add(new Vector2(side / (float)sides * 1.2f, t * height));
                    uv1.Add(new Vector2(-1f, -1f));
                    colors.Add(barkColor);
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * (sides + 1) + side, b = a + sides + 1;
                    trunkTriangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            }

            // Couronne de palmes.
            Vector3 crown = Center(1f) + Vector3.up * 0.1f;
            int fronds = 10 + variant + random.Next(2);
            for (int f = 0; f < fronds; f++)
            {
                float azimuth = (f + (float)random.NextDouble() * 0.5f) / fronds * Mathf.PI * 2f;
                float elevation = Mathf.Lerp(-10f, 50f, (float)random.NextDouble());
                float length = Mathf.Lerp(3.0f, 4.2f, (float)random.NextDouble());
                AddFrond(crown, azimuth, elevation, length, vertices, normals, tangents, uv0, uv1, colors, frondTriangles);
            }

            var mesh = new Mesh { name = "Cocotier_" + variant };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trunkTriangles, 0);
            mesh.SetTriangles(frondTriangles, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFrond(Vector3 origin, float azimuth, float elevationDeg, float length,
            List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv0,
            List<Vector2> uv1, List<Color32> colors, List<int> triangles)
        {
            const int segments = 7;
            const float width = 1.7f;
            var dir = new Vector3(Mathf.Cos(azimuth), 0f, Mathf.Sin(azimuth));
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            int start = vertices.Count;
            Vector3 p = origin;

            for (int i = 0; i <= segments; i++)
            {
                float s = i / (float)segments;
                // La palme monte puis retombe sous son propre poids.
                float angle = (elevationDeg - 85f * Mathf.Pow(s, 1.4f)) * Mathf.Deg2Rad;
                Vector3 forward = (dir * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
                Vector3 normal = Vector3.Cross(forward, side).normalized;
                float halfWidth = width * 0.5f * (i == 0 ? 0.2f : 1f);
                Vector3 fold = normal * (0.14f * halfWidth); // pli en V le long de la nervure

                AddFrondVertex(p - side * halfWidth - fold, normal, side, new Vector2(0f, s), vertices, normals, tangents, uv0, uv1, colors);
                AddFrondVertex(p, normal, side, new Vector2(0.5f, s), vertices, normals, tangents, uv0, uv1, colors);
                AddFrondVertex(p + side * halfWidth - fold, normal, side, new Vector2(1f, s), vertices, normals, tangents, uv0, uv1, colors);

                p += forward * (length / segments);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 3, b = a + 3;
                triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                triangles.AddRange(new[] { a + 1, b + 1, b + 2, a + 1, b + 2, a + 2 });
            }
        }

        private static void AddFrondVertex(Vector3 position, Vector3 normal, Vector3 side, Vector2 uv,
            List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv0,
            List<Vector2> uv1, List<Color32> colors)
        {
            vertices.Add(position);
            // Normales tirées vers le haut : éclairage doux et homogène des feuillages.
            normals.Add((normal + Vector3.up * 0.6f).normalized);
            tangents.Add(new Vector4(side.x, side.y, side.z, 1f));
            uv0.Add(uv);
            uv1.Add(new Vector2(-1f, -1f));
            colors.Add(new Color32(255, 255, 255, 255));
        }

        #endregion

        #region Texture des palmes

        /// <summary>
        /// Texture d'une palme (256 × 512) : nervure centrale et folioles obliques, couleur et alpha.
        /// u = travers de la palme, v = de la base à la pointe.
        /// </summary>
        private static Texture2D BuildFrondTexture()
        {
            const int w = 256, h = 512, leaflets = 34;
            var random = new System.Random(7);
            var lengths = new float[leaflets + 2];
            var tints = new float[leaflets + 2];
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = 0.75f + 0.25f * (float)random.NextDouble();
                tints[i] = (float)random.NextDouble();
            }

            var pixels = new Color32[w * h];
            var dark = new Color(0.14f, 0.28f, 0.07f);
            var light = new Color(0.36f, 0.52f, 0.14f);
            var dry = new Color(0.55f, 0.48f, 0.22f);
            var rib = new Color(0.45f, 0.42f, 0.24f);

            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                // Enveloppe : folioles courtes à la base et à la pointe, longues au milieu.
                float envelope = v < 0.06f ? 0f : Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01((v - 0.06f) / 0.94f)), 0.55f);

                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1);
                    float d = Mathf.Abs(u - 0.5f) * 2f; // 0 sur la nervure, 1 au bord
                    Color color = rib;
                    float alpha = 0f;

                    if (d < 0.035f && v > 0.02f)
                    {
                        alpha = 1f;
                    }
                    else
                    {
                        // Les folioles partent de la nervure en biais vers la pointe.
                        float phase = (v - d * 0.22f) * leaflets;
                        int index = Mathf.Clamp(Mathf.FloorToInt(phase) + 1, 0, leaflets + 1);
                        float along = phase - Mathf.Floor(phase);
                        float thickness = 0.62f * (1f - d * 0.6f);
                        if (along < thickness && d < envelope * lengths[index])
                        {
                            alpha = 1f;
                            float shade = Mathf.Abs(along / thickness - 0.5f) * 2f; // bords plus sombres
                            color = Color.Lerp(light, dark, shade * 0.6f + tints[index] * 0.4f);
                            // Pointes jaunies, palmes du bas plus sèches.
                            color = Color.Lerp(color, dry, Mathf.Clamp01((d / Mathf.Max(envelope, 0.01f) - 0.8f) * 3f) * 0.5f);
                        }
                    }

                    pixels[y * w + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            string fullPath = Path.GetFullPath(FrondTexturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FrondTexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(FrondTexturePath);
            importer.alphaIsTransparency = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
            importer.maxTextureSize = 512;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FrondTexturePath);
        }

        #endregion
    }
}
