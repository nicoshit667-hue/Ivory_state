using System.IO;
using IvoryState.Client.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Captures d'écran de la scène Bootstrap (vue aérienne, rue, quartiers), utilisables sans ouvrir
    /// l'éditeur : <c>-executeMethod IvoryState.Editor.City.ScenePreviewCapture.CaptureAll</c>
    /// (en batchmode <b>sans</b> -nographics, pour que le GPU fasse le rendu).
    /// Images écrites dans <c>docs/screenshots/</c> à la racine du dépôt.
    /// </summary>
    public static class ScenePreviewCapture
    {
        private const string ScenePath = "Assets/_Project/Scenes/Bootstrap.unity";
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("Ivory State/Captures d'écran de la ville", priority = 50)]
        public static void CaptureAll()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "screenshots"));
            Directory.CreateDirectory(folder);

            var go = new GameObject("PreviewCamera");
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 55f;
            camera.farClipPlane = 1500f;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            Light sun = RenderSettings.sun;
            Material sky = RenderSettings.skybox;
            float skyExposure = sky != null && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f;

            // Fin d'après-midi (heure dorée), l'ambiance visée par la direction artistique.
            DayNightCycle.Apply(sun, 17.1f, sky);

            // Positions tirées de la disposition (CityLayout.json) : quartiers, monuments, pont, aéroport, forêt.
            static Vector3 G(float x, float z, float up) => new Vector3(x, Gameplay.World.CityLayout.GroundHeight(x, z) + up, z);
            Gameplay.World.CityLayout.Data layout = Gameplay.World.CityLayout.Layout;

            // Vue aérienne : toute la ville, de Port-Bouët à Abobo, depuis le sud-ouest (brume réduite).
            float fogEnd = RenderSettings.fogEndDistance, fogStart = RenderSettings.fogStartDistance;
            RenderSettings.fogStartDistance = 1500f;
            RenderSettings.fogEndDistance = 6000f;
            camera.farClipPlane = 7000f;
            Shot(camera, folder, "01_vue_aerienne", new Vector3(-1500f, 1500f, -2300f), new Vector3(150f, 0f, 200f));
            Vector3 plaza = Gameplay.World.CityLayout.PlazaCenter;
            Shot(camera, folder, "11_plateau_et_lagune", plaza + new Vector3(-220f, 150f, -240f), plaza);
            // Le plus long pont de la carte, de côté.
            float bestSpan = 0f;
            Vector3 bridgeA = Vector3.zero, bridgeB = Vector3.zero;
            foreach (Gameplay.World.CityLayout.RoadData road in layout.roads)
            {
                for (int i = 0; road.b != null && i + 3 < road.b.Length; i += 4)
                {
                    var a = new Vector3(road.b[i], 0f, road.b[i + 1]);
                    var b = new Vector3(road.b[i + 2], 0f, road.b[i + 3]);
                    if (Vector3.Distance(a, b) > bestSpan)
                    {
                        bestSpan = Vector3.Distance(a, b);
                        bridgeA = a;
                        bridgeB = b;
                    }
                }
            }
            if (bestSpan > 0f)
            {
                Vector3 mid = (bridgeA + bridgeB) * 0.5f;
                Vector3 side = Vector3.Cross(Vector3.up, (bridgeB - bridgeA).normalized);
                Shot(camera, folder, "12_pont", mid + side * (bestSpan * 0.8f) + Vector3.up * 18f, mid + Vector3.up * 2f);
            }
            // Aéroport (piste 03/21) et forêt du Banco.
            if (layout.airport?.runways != null && layout.airport.runways.Length > 0)
            {
                float[] p = layout.airport.runways[0].p;
                Vector3 mid = G((p[0] + p[p.Length - 2]) * 0.5f, (p[1] + p[p.Length - 1]) * 0.5f, 0f);
                Shot(camera, folder, "14_aeroport", mid + new Vector3(-220f, 90f, -160f), mid);
            }
            if (layout.trees != null && layout.trees.Length > 1)
            {
                float sx = 0f, sz = 0f;
                int n = layout.trees.Length / 2;
                for (int i = 0; i + 1 < layout.trees.Length; i += 2)
                {
                    sx += layout.trees[i];
                    sz += layout.trees[i + 1];
                }
                Vector3 c = G(sx / n, sz / n, 0f);
                Shot(camera, folder, "15_foret_du_banco", c + new Vector3(-160f, 90f, -200f), c);
            }
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            camera.farClipPlane = 1500f;

            // Au sol, comme le joueur : la place du Plateau.
            Shot(camera, folder, "02_place_du_plateau", plaza + new Vector3(0f, 2.5f, -24f), plaza + new Vector3(0f, 7f, 40f));
            // Monuments à leur vraie place, vus depuis la rue.
            foreach (Gameplay.World.CityLayout.LandmarkData landmark in layout.landmarks)
            {
                var front = new Vector3(landmark.fx, 0f, landmark.fz);
                Vector3 center = G(landmark.x, landmark.z, 0f);
                float back = Mathf.Max(landmark.w, landmark.d) * 0.9f + 20f;
                Shot(camera, folder, "13_" + landmark.key.Replace("PF_", "").ToLowerInvariant(),
                    center + front * back + Vector3.up * (back * 0.35f), center + Vector3.up * 4f);
            }
            // Quartiers vus d'un peu haut.
            int index = 0;
            foreach (Gameplay.World.CityLayout.DistrictData district in layout.districts)
            {
                Vector3 c = G(district.x, district.z, 0f);
                Shot(camera, folder, "0" + (4 + index / 10) + (index % 10) + "_" + district.name.ToLowerInvariant(),
                    c + new Vector3(-80f, 35f, -80f), c + new Vector3(0f, 0f, 40f));
                index++;
            }

            // Nuit : fenêtres, boutiques et lampadaires allumés.
            DayNightCycle.Apply(sun, 21f, sky);
            Shot(camera, folder, "20_nuit_plateau", plaza + new Vector3(0f, 2.5f, -24f), plaza + new Vector3(0f, 7f, 40f));
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();

            // Remise en état : l'éclairage de l'éditeur reste celui de fin d'après-midi.
            DayNightCycle.Apply(sun, CityLighting.EditorHour, sky);
            if (sky != null && sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", skyExposure);

            Object.DestroyImmediate(go);
            Debug.Log("[Preview] Captures écrites dans " + folder);
        }

        /// <summary>
        /// Rues vues de près (feux tricolores d'un carrefour, rond-point, aire de retournement), écrites dans
        /// IvoryState/Builds/preview, puis compilation du client Windows (une seule session Unity).
        /// </summary>
        public static void CaptureStreetsAndBuildClient()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "preview"));
            Directory.CreateDirectory(folder);
            var go = new GameObject("PreviewCamera");
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 55f;
            camera.farClipPlane = 900f;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            DayNightCycle.Apply(RenderSettings.sun, 16.5f, RenderSettings.skybox);
            static Vector3 G(float x, float z, float up) => new Vector3(x, Gameplay.World.CityLayout.GroundHeight(x, z) + up, z);
            Gameplay.World.CityLayout.Data layout = Gameplay.World.CityLayout.Layout;

            int lights = 0;
            foreach (Gameplay.World.CityLayout.JunctionData j in layout.junctions)
            {
                if (j.s == 0 || j.ring != 0 || j.a.Length < 16 || lights >= 2)
                    continue;
                var arm = new Vector3(j.a[0], 0f, j.a[1]).normalized;
                Vector3 c = G(j.x, j.z, 0f);
                Shot(camera, folder, "feux_" + lights + "_haut", c + arm * 30f + Vector3.up * 14f, c);
                Shot(camera, folder, "feux_" + lights + "_conducteur", c + arm * 26f - Vector3.Cross(Vector3.up, arm) * 2f + Vector3.up * 1.4f, c + Vector3.up * 2.5f);
                lights++;
            }
            if (layout.roundabouts.Length > 0)
            {
                Gameplay.World.CityLayout.RoundaboutData rb = layout.roundabouts[layout.roundabouts.Length / 2];
                Vector3 c = G(rb.x, rb.z, 0f);
                Shot(camera, folder, "rond_point", c + new Vector3(-28f, 20f, -28f), c);
            }
            foreach (Gameplay.World.CityLayout.RoadData road in layout.roads)
            {
                if (road.de == null || road.de.Length < 2 || road.de[1] == 0)
                    continue;
                Vector3 end = G(road.p[road.p.Length - 2], road.p[road.p.Length - 1], 0f);
                Shot(camera, folder, "impasse", end + new Vector3(-18f, 12f, -18f), end);
                break;
            }
            Object.DestroyImmediate(go);
            Debug.Log("[Preview] Rues capturées dans " + folder);
            IvoryBuildMenu.BuildWindowsTestClient();
        }

        public static void SetupAndCaptureAll()
        {
            IvoryProjectSetup.SetupAll();
            CaptureAll();
        }

        /// <summary>
        /// Taxi garé à la station est de la place (x = 41 m) et passants sur le trottoir voisin, posés
        /// dans une image de leur animation (marche ou attente) : l'éditeur n'est pas en mode Play.
        /// </summary>
        private static void StageStreet(Transform parent)
        {
            var taxi = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/PF_Taxi.prefab");
            if (taxi != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(taxi, parent);
                instance.transform.SetPositionAndRotation(new Vector3(41f, 0f, -6f), Quaternion.Euler(0f, 0f, 0f));
            }

            // Passants : seulement si l'éditeur sait les poser. Les clips Humanoid ne s'échantillonnent
            // pas hors mode Play en batchmode ; on évite des silhouettes en T (la capture en jeu,
            // 06_en_jeu, montre les personnages animés).
            var models = AssetDatabase.LoadAssetAtPath<IvoryState.Client.Characters.CharacterModels>(
                IvoryState.Editor.Characters.CharacterSetup.ModelsPath);
            if (models == null || models.Count == 0 || Application.isBatchMode)
                return;

            // Sur le trottoir de la place (bord est à x = 32 m), face à la rue ou en marche.
            Vector3[] spots =
            {
                new Vector3(30.6f, 0.15f, 3.0f), new Vector3(29.8f, 0.15f, 5.2f), new Vector3(31.2f, 0.15f, 7.5f),
                new Vector3(29.5f, 0.15f, 9.8f), new Vector3(31.0f, 0.15f, -4.5f), new Vector3(28.4f, 0.15f, 1.8f),
                new Vector3(30.4f, 0.15f, 12.5f),
            };
            float[] yaws = { 200f, 160f, 90f, 0f, 270f, 120f, 180f };
            for (int i = 0; i < spots.Length; i++)
            {
                GameObject model = models.Get(i);
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                avatar.transform.SetPositionAndRotation(spots[i], Quaternion.Euler(0f, yaws[i], 0f));
                var animator = avatar.GetComponent<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;
                bool walking = i % 3 == 2;
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip.name.Contains(walking ? "walk" : "idle"))
                    {
                        // Clip Humanoid : échantillonné par le mode Animation de l'éditeur (comme la
                        // fenêtre Animation), qui gère le reciblage. Arrêté après les captures.
                        if (!AnimationMode.InAnimationMode())
                            AnimationMode.StartAnimationMode();
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(avatar, clip, clip.length * (0.15f + 0.1f * i));
                        AnimationMode.EndSampling();
                        break;
                    }
                }
            }
        }

        private static void Shot(UnityEngine.Camera camera, string folder, string name, Vector3 position, Vector3 lookAt)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
