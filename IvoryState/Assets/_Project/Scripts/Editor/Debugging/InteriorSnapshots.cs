using System.IO;
using IvoryState.Gameplay.Homes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IvoryState.Editor.Debugging
{
    /// <summary>
    /// Photos de contrôle des intérieurs (studio, appartements, maisons) : une caméra dans un coin de la
    /// pièce, rendu dans Builds/Snapshots. Utilisable en mode batch (sans -nographics).
    /// </summary>
    internal static class InteriorSnapshots
    {
        private const string ScenePath = "Assets/_Project/Scenes/Bootstrap.unity";

        [MenuItem("Ivory State/Debug/Photos des intérieurs")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Snapshots");
            Directory.CreateDirectory(folder);

            var cameraObject = new GameObject("SnapshotCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;
            var texture = new RenderTexture(1280, 720, 24);
            camera.targetTexture = texture;

            Shoot(camera, texture, folder, "studio_colocation", "Studio_1", StudioLayout.Center(1), new Vector2(StudioLayout.Width, StudioLayout.Depth));
            int[] samples = { 1, 3, 4, 13, 17, 18 };
            foreach (int id in samples)
            {
                PropertyCatalog.Property property = PropertyCatalog.Get(id);
                Vector3 center = PropertyCatalog.InteriorCenter(id);
                if (!PropertyCatalog.HasGarden(property.Kind))
                {
                    Shoot(camera, texture, folder, "bien_" + id + "_" + property.Kind, "Bien_" + id, center, PropertyCatalog.InteriorSize(property.Kind));
                    continue;
                }
                // Villas duplex : la cour depuis le portail, le salon, l'étage.
                const float s = PropertyCatalog.PlanScale;
                ShootAt(camera, texture, folder, "villa_" + id + "_cour", "Bien_" + id, center + new Vector3(-2f * s, 5f, -10.5f * s), center + new Vector3(1f * s, 1f, 2f * s));
                ShootAt(camera, texture, folder, "villa_" + id + "_salon", "Bien_" + id, center + new Vector3(-5.6f * s, 2.3f, 0.4f * s), center + new Vector3(2f * s, 0.8f, 6f * s));
                ShootAt(camera, texture, folder, "villa_" + id + "_etage", "Bien_" + id, center + new Vector3(1.6f * s, 6.3f, 0.3f * s), center + new Vector3(4f * s, 4.3f, 7f * s));
            }

            Vector3 hospital = Gameplay.Combat.HospitalCatalog.InteriorCenter(0);
            ShootAt(camera, texture, folder, "hopital_0", "SalleDeSoins_0", hospital + new Vector3(0f, 3.2f, -15.0f), hospital + new Vector3(-2f, 1.5f, -4.5f));
            ShootAt(camera, texture, folder, "hopital_accueil", "SalleDeSoins_0", hospital + new Vector3(-5.5f, 3.0f, -15.0f), hospital + new Vector3(4.5f, 1.3f, -5.2f));
            ShootAt(camera, texture, folder, "hopital_couloir", "SalleDeSoins_0", hospital + new Vector3(-16.0f, 2.9f, -1.0f), hospital + new Vector3(14.0f, 1.5f, -1.0f));
            ShootAt(camera, texture, folder, "hopital_hospitalisation", "SalleDeSoins_0", hospital + new Vector3(9.4f, 3.0f, 4.2f), hospital + new Vector3(16.8f, 1.1f, 14.5f));
            ShootAt(camera, texture, folder, "hopital_urgences", "SalleDeSoins_0", hospital + new Vector3(-10.5f, 3.0f, -6.2f), hospital + new Vector3(-18.5f, 1.1f, -14.5f));

            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(texture);
            Debug.Log("[Snapshots] Photos enregistrées dans " + folder);
        }

        private static void Shoot(Camera camera, RenderTexture texture, string folder, string file, string rootName, Vector3 center, Vector2 size)
        {
            if (!Show(rootName, true))
                return;
            // Coin sud-ouest, à hauteur d'homme, regard vers le coin nord-est.
            Vector3 eye = center + new Vector3(-size.x * 0.5f + 0.4f, 2.4f, -size.y * 0.5f + 0.4f);
            Vector3 target = center + new Vector3(size.x * 0.3f, 0.6f, size.y * 0.3f);
            Render(camera, texture, folder, file, eye, target);
            Show(rootName, false);
        }

        private static void ShootAt(Camera camera, RenderTexture texture, string folder, string file, string rootName, Vector3 eye, Vector3 target)
        {
            if (!Show(rootName, true))
                return;
            Render(camera, texture, folder, file, eye, target);
            Show(rootName, false);
        }

        private static void ShootGarden(Camera camera, RenderTexture texture, string folder, string file, string rootName, Vector3 center, Vector2 size, float garden)
        {
            if (!Show(rootName, true))
                return;
            // Bord est du jardin, en hauteur, regard vers la maison et la piscine (au sud).
            Vector3 eye = center + new Vector3(size.x * 0.5f + garden - 0.5f, 5f, 0f);
            Vector3 target = center + new Vector3(size.x * 0.5f + garden * 0.3f, 0f, -size.y * 0.3f);
            Render(camera, texture, folder, file, eye, target);
            Show(rootName, false);
        }

        private static bool Show(string rootName, bool visible)
        {
            GameObject root = GameObject.Find(rootName);
            Transform visual = root != null ? root.transform.Find("Visuel") : null;
            if (visual == null)
            {
                Debug.LogWarning("[Snapshots] Introuvable : " + rootName);
                return false;
            }
            visual.gameObject.SetActive(visible);
            return true;
        }

        private static void Render(Camera camera, RenderTexture texture, string folder, string file, Vector3 eye, Vector3 target)
        {
            camera.transform.position = eye;
            camera.transform.LookAt(target);
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, file + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
