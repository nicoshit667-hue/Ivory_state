using System;
using System.Collections.Generic;
using System.IO;
using IvoryState.Client.World;
using IvoryState.Gameplay.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Pipeline d'import FBX, d'attribution de matériaux URP/NightLight, de génération de Prefabs
    /// avec BoxColliders exacts, et de placement géographique par commune pour l'architecture d'Abidjan.
    /// </summary>
    public static class ArchitectureSetup
    {
        public const string ModelsRoot = "Assets/_Project/Art/Models/Architecture";
        public const string PrefabsRoot = "Assets/_Project/Prefabs/Architecture";
        public const string MaterialsRoot = "Assets/_Project/Art/Materials/Architecture";

        private const string ManifestRelativeDir = "tools/blender/architecture";

        private struct ArchPlacementSpec
        {
            public string PrefabAssetName;
            public string PrefabsSubfolder;
            public string ManifestFile;
            public string Commune;
            /// <summary>Repli si CityLayout.json n'a pas ce monument (sinon : sa vraie place, voir tools/citylayout).</summary>
            public Vector3 Position;
            public Vector3 ToStreet;

            public string PrefabPath => $"{PrefabsRoot}/{PrefabsSubfolder}/{PrefabAssetName}.prefab";
        }

        private static readonly ArchPlacementSpec[] Placements =
        {
            // Emplacement vérifié à Cocody (30 x 36 m, remplace buildings[386, 1897] sur le Boulevard Aimé Henri Konan Bedie).
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Duplex_Cocody_01",
                PrefabsSubfolder = "Maisons",
                ManifestFile = "duplex_cocody_01_manifest.json",
                Commune = "Cocody",
                Position = new Vector3(455.69f, 0f, 373.02f),
                ToStreet = new Vector3(0.993f, 0f, -0.117f),
            },
            // Monuments d'Abidjan et édifices civiques (Plateau & CHU de Cocody), sans collision route/lagune.
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Tour_F",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "tour_f_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(-388.0f, 0f, 108.0f),
                ToStreet = new Vector3(-0.0851f, 0f, 0.9964f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Stade_FHB",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "stade_fhb_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(340.0f, 0f, 20.0f),
                ToStreet = new Vector3(0f, 0f, 1f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Palais_Presidentiel",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "palais_presidentiel_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(-302.0f, 0f, -30.0f),
                ToStreet = new Vector3(1f, 0f, 0f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Mairie_Plateau",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "mairie_plateau_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(-76.0f, 0f, 20.0f),
                ToStreet = new Vector3(0f, 0f, 1f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Commissariat_Central",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "commissariat_central_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(88.0f, 0f, 280.0f),
                ToStreet = new Vector3(0f, 0f, -1f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Cathedrale_Saint_Paul",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "cathedrale_saint_paul_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(-317.0f, 0f, 108.5f),
                ToStreet = new Vector3(-0.0851f, 0f, 0.9964f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Grande_Mosquee",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "grande_mosquee_manifest.json",
                Commune = "Plateau",
                Position = new Vector3(206.0f, 0f, 66.0f),
                ToStreet = new Vector3(-1f, 0f, 0f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Hopital_CHU",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "hopital_chu_manifest.json",
                Commune = "Cocody",
                Position = new Vector3(393.25f, 0f, 315.35f),
                ToStreet = new Vector3(0.119f, 0f, 0.993f),
            },
            new ArchPlacementSpec
            {
                PrefabAssetName = "PF_Base_Militaire_Yopougon",
                PrefabsSubfolder = "Monuments",
                ManifestFile = "base_militaire_yopougon_manifest.json",
                Commune = "Yopougon",
                Position = new Vector3(-660.0f, 0f, 745.0f),
                ToStreet = new Vector3(-1f, 0f, 0f),
            },
        };

        private static readonly string[] Communes =
        {
            "Plateau",
            "Cocody",
            "Marcory",
            "Treichville",
            "Adjame",
            "Yopougon",
            "Attecoube",
            "Koumassi",
        };

        [Serializable]
        private class ColliderBoxData
        {
            public float[] center;
            public float[] size;
            public float[] euler;
        }

        [Serializable]
        private class DoorSpecData
        {
            public string name;
            public string type;
            public ColliderBoxData[] colliders;
            public float[] default_rot_deg;
            public float closed_offset_x;
            public float closed_offset_y;
            public float[] pivot_blender;
        }

        /// <summary>Identifiants des portes (WorldDoor) : au-delà de ceux des points du générateur de ville.</summary>
        private const int DoorIdBase = 900000;

        /// <summary>Bâtiments ouverts au public : leurs portails sont ouverts au lancement du serveur.</summary>
        private static bool IsPublicBuilding(string prefabName) =>
            prefabName == "PF_Hopital_CHU" || prefabName == "PF_Mairie_Plateau" || prefabName == "PF_Cathedrale_Saint_Paul" ||
            prefabName == "PF_Grande_Mosquee" || prefabName == "PF_Tour_F" || prefabName == "PF_Stade_FHB";

        [Serializable]
        private class BuildingManifestData
        {
            public string name;
            public float[] lot_meters;
            public string exterior_fbx;
            public string interior_fbx;
            public ColliderBoxData[] exterior_colliders;
            public ColliderBoxData[] interior_colliders;
            public DoorSpecData[] exterior_doors;
            public DoorSpecData[] interior_doors;
        }

        /// <summary>Indique si un bloc spécial de CityLayout.json est remplacé par un monument Blender dédié.</summary>
        public static bool ReplacesSpecialBlock(string specialKind)
        {
            return string.Equals(specialKind, "Cathedrale", StringComparison.Ordinal) ||
                   string.Equals(specialKind, "GrandeMosquee", StringComparison.Ordinal);
        }

        /// <summary>Indique si le portail simple d'un hôpital (ex. CHU de Cocody) est remplacé par le bâtiment complet.</summary>
        public static bool ReplacesHospitalEntrance(int hospitalIndex)
        {
            return hospitalIndex == 1;
        }

        [MenuItem("Ivory State/Architecture/Reconstruire Duplex Cocody 01")]
        public static void RebuildDuplexCocody01Menu()
        {
            EnsureArchitectureFolders();
            Dictionary<string, Material> materials = EnsureMaterials();
            BuildArchitecturePrefab(Placements[0], materials);
            AssetDatabase.SaveAssets();
            Debug.Log("[Architecture] Prefab PF_Duplex_Cocody_01 reconstruit.");
        }

        [MenuItem("Ivory State/Architecture/Reconstruire Base Militaire Yopougon")]
        public static void RebuildBaseMilitaireYopougonMenu()
        {
            EnsureArchitectureFolders();
            Dictionary<string, Material> materials = EnsureMaterials();
            foreach (ArchPlacementSpec spec in Placements)
            {
                if (spec.PrefabAssetName == "PF_Base_Militaire_Yopougon")
                {
                    BuildArchitecturePrefab(spec, materials);
                    break;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Architecture] Prefab PF_Base_Militaire_Yopougon reconstruit.");
        }

        [MenuItem("Ivory State/Architecture/Reconstruire tous les édifices et monuments")]
        public static void RebuildAllArchitectureMenu()
        {
            EnsureArchitectureFolders();
            Dictionary<string, Material> materials = EnsureMaterials();
            foreach (ArchPlacementSpec spec in Placements)
                BuildArchitecturePrefab(spec, materials);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Architecture] {Placements.Length} prefabs architecturaux reconstruits.");
        }

        /// <summary>
        /// Construit les prefabs manquants si nécessaire et place tous les bâtiments d'architecture
        /// dans leur commune respective sous City/Architecture.
        /// </summary>
        public static void PlaceAllInScene(Transform cityRoot)
        {
            if (cityRoot == null)
                return;

            EnsureArchitectureFolders();
            Dictionary<string, Material> materials = EnsureMaterials();

            Transform existing = cityRoot.Find("Architecture");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var archRoot = new GameObject("Architecture").transform;
            archRoot.SetParent(cityRoot, false);

            var communeRoots = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (string commune in Communes)
            {
                var c = new GameObject(commune).transform;
                c.SetParent(archRoot, false);
                communeRoots[commune] = c;
            }

            for (int placementIndex = 0; placementIndex < Placements.Length; placementIndex++)
            {
                ArchPlacementSpec spec = Placements[placementIndex];
                GameObject prefab = BuildArchitecturePrefab(spec, materials);
                if (prefab == null)
                    continue;

                if (!communeRoots.TryGetValue(spec.Commune, out Transform parent))
                    parent = archRoot;

                var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
                if (instance != null)
                {
                    instance.name = spec.PrefabAssetName;
                    // Emplacement réel (OpenStreetMap) réservé par tools/citylayout/build_layout.py : aucune rue ni
                    // aucun bâtiment n'y passe, et le relief y est aplani. Posé à plat : CityGenerator relève tout.
                    Gameplay.World.CityLayout.LandmarkData landmark = Gameplay.World.CityLayout.Landmark(spec.PrefabAssetName);
                    Vector3 position = landmark != null ? new Vector3(landmark.x, 0f, landmark.z) : spec.Position;
                    Vector3 toStreet = landmark != null ? new Vector3(landmark.fx, 0f, landmark.fz) : spec.ToStreet;
                    instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(toStreet.normalized, Vector3.up));
                    instance.transform.localScale = Vector3.one;
                    if (landmark == null)
                        Debug.LogWarning("[Architecture] " + spec.PrefabAssetName + " absent de CityLayout.json : position de repli.");

                    // Portes ouvrables : identifiants stables (même scène côté client et serveur).
                    int doorIndex = 0;
                    string label = landmark != null ? landmark.name : spec.PrefabAssetName;
                    foreach (WorldDoor door in instance.GetComponentsInChildren<WorldDoor>(true))
                    {
                        door.EditorSetup(DoorIdBase + placementIndex * 200 + doorIndex++, label + " (" + door.name.Replace("Poignee_", "") + ")");
                        PrefabUtility.RecordPrefabInstancePropertyModifications(door);
                    }
                }
            }
        }

        /// <summary>Point d'entrée batchmode : régénère toute la ville, vérifie les colliders/dimensions et capture des vues.</summary>
        public static void SetupVerifyAndCapture()
        {
            IvoryProjectSetup.SetupAll();

            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            foreach (ArchPlacementSpec spec in Placements)
            {
                string hierarchyPath = $"City/Architecture/{spec.Commune}/{spec.PrefabAssetName}";
                var instance = GameObject.Find(hierarchyPath);
                if (instance == null)
                    throw new InvalidOperationException($"{spec.PrefabAssetName} introuvable dans Bootstrap.unity ({hierarchyPath}) !");

                Bounds totalBounds = CalculateCombinedRendererBounds(instance);
                int colliderCount = instance.GetComponentsInChildren<BoxCollider>(true).Length;
                int doorColCount = 0;
                foreach (var col in instance.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (IsDoorOrGateName(col.gameObject.name))
                        doorColCount++;
                }
                Debug.Log($"[ArchitectureVerify] {spec.PrefabAssetName} ({spec.Commune}) pos={instance.transform.position} boundsCenter={totalBounds.center} boundsSize={totalBounds.size} boxColliders={colliderCount} doorColliders={doorColCount}");
            }

            Physics.SyncTransforms();
            VerifyArchitecturePhysicsWalkability();

            string outDir = Environment.GetEnvironmentVariable("IVORY_ARCH_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(outDir))
                CaptureArchitectureViews(outDir);
        }

        private static void VerifyArchitecturePhysicsWalkability()
        {
            GameObject duplexGo = GameObject.Find("City/Architecture/Cocody/PF_Duplex_Cocody_01");
            if (duplexGo == null)
                throw new InvalidOperationException("PF_Duplex_Cocody_01 introuvable pour test physique.");

            Transform dt = duplexGo.transform;
            VerifyStairClimb(dt, "Duplex_Flight1", new Vector3(-1.125f, 0.35f, -0.20f), new Vector3(-1.125f, 2.375f, -3.70f));
            VerifyStairClimb(dt, "Duplex_Flight2", new Vector3(-3.475f, 2.375f, -3.70f), new Vector3(-3.475f, 4.40f, -0.20f));
            VerifyWalkableSurface(dt, "Duplex_MidLanding", new Vector3(-2.30f, 2.375f, -4.55f));

            GameObject tourFGo = GameObject.Find("City/Architecture/Plateau/PF_Tour_F");
            if (tourFGo != null)
            {
                Transform tt = tourFGo.transform;
                VerifyStairClimb(tt, "TourF_Stairs_West", new Vector3(16.0f, 0.52f, -1.50f), new Vector3(16.0f, 5.40f, -11.50f));
                VerifyStairClimb(tt, "TourF_Stairs_East", new Vector3(-16.0f, 0.52f, -1.50f), new Vector3(-16.0f, 5.40f, -11.50f));
            }

            GameObject stadeGo = GameObject.Find("City/Architecture/Plateau/PF_Stade_FHB");
            if (stadeGo != null)
            {
                Transform st = stadeGo.transform;
                VerifyWalkableSurface(st, "Stade_NW_Causeway", new Vector3(-95.0f, 0.15f, 94.0f));
                VerifyWalkableSurface(st, "Stade_CenterPitch", new Vector3(0.0f, 0.40f, 0.0f));
            }
        }

        private static void VerifyStairClimb(Transform buildingRoot, string label, Vector3 localStart, Vector3 localEnd, int samples = 7)
        {
            Vector3? prevWorld = null;
            float prevY = float.MinValue;
            for (int i = 1; i < samples; i++)
            {
                float t = (float)i / samples;
                Vector3 expectedLocal = Vector3.Lerp(localStart, localEnd, t);
                Vector3 rayOrigin = buildingRoot.TransformPoint(expectedLocal + Vector3.up * 0.85f);
                if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1.50f, ~0, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: aucun collider détecté sous {rayOrigin}");

                if (!hit.collider.transform.IsChildOf(buildingRoot))
                    throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: collision étrangère '{hit.collider.name}'");

                float slopeDeg = Vector3.Angle(hit.normal, Vector3.up);
                if (slopeDeg > 42f)
                    throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: pente {slopeDeg:F1}° > 42° sur '{hit.collider.name}'");

                if (Mathf.Abs(hit.point.y - expectedLocal.y) > 0.30f)
                    throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: Y={hit.point.y:F2} attendu ~{expectedLocal.y:F2}");

                if (hit.point.y + 0.05f < prevY)
                    throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: régression de hauteur ({hit.point.y:F2} < {prevY:F2})");
                prevY = hit.point.y;

                Vector3 chestPos = hit.point + Vector3.up * 0.90f;
                if (prevWorld.HasValue)
                {
                    Vector3 seg = chestPos - prevWorld.Value;
                    float segLen = seg.magnitude;
                    if (segLen > 0.05f && Physics.Raycast(prevWorld.Value, seg.normalized, out RaycastHit wallHit, segLen, ~0, QueryTriggerInteraction.Ignore))
                        throw new InvalidOperationException($"[StairVerify] {label} t={t:F2}: obstacle bloquant '{wallHit.collider.name}' à hauteur du torse !");
                }
                prevWorld = chestPos;
            }
            Debug.Log($"[StairVerify] {label} OK ({samples - 1} points vérifiés, montée fluide jusqu'à Y={prevY:F2}m)");
        }

        private static void VerifyWalkableSurface(Transform buildingRoot, string label, Vector3 expectedLocal)
        {
            Vector3 rayOrigin = buildingRoot.TransformPoint(expectedLocal + Vector3.up * 0.85f);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1.50f, ~0, QueryTriggerInteraction.Ignore) ||
                !hit.collider.transform.IsChildOf(buildingRoot))
                throw new InvalidOperationException($"[SurfaceVerify] {label}: surface praticable introuvable à {expectedLocal}");

            Debug.Log($"[SurfaceVerify] {label} OK (Y={hit.point.y:F2}m, collider={hit.collider.name})");
        }

        public static GameObject BuildDuplexCocody01Prefab(Dictionary<string, Material> materials)
        {
            return BuildArchitecturePrefab(Placements[0], materials);
        }

        private static GameObject BuildArchitecturePrefab(ArchPlacementSpec spec, Dictionary<string, Material> materials)
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string manifestPath = Path.Combine(repoRoot, ManifestRelativeDir, spec.ManifestFile);
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[Architecture] Manifest introuvable : " + manifestPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            }

            var manifest = JsonUtility.FromJson<BuildingManifestData>(File.ReadAllText(manifestPath));
            ConfigureFbxImporter(manifest.exterior_fbx);
            if (!string.IsNullOrEmpty(manifest.interior_fbx))
                ConfigureFbxImporter(manifest.interior_fbx);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var extAsset = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.exterior_fbx);
            if (extAsset == null)
                throw new InvalidOperationException($"Impossible de charger le FBX extérieur de {spec.PrefabAssetName} ({manifest.exterior_fbx}).");
            GameObject intAsset = !string.IsNullOrEmpty(manifest.interior_fbx)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(manifest.interior_fbx)
                : null;

            var root = new GameObject(spec.PrefabAssetName);
            try
            {
                var extNode = UnityEngine.Object.Instantiate(extAsset, root.transform);
                extNode.name = "Exterieur";
                extNode.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                extNode.transform.localScale = Vector3.one;
                ApplyMaterialsAndStaticFlags(extNode);

                GameObject intNode = null;
                if (intAsset != null)
                {
                    intNode = UnityEngine.Object.Instantiate(intAsset, root.transform);
                    intNode.name = "Interieur";
                    intNode.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    intNode.transform.localScale = Vector3.one;
                    ApplyMaterialsAndStaticFlags(intNode);
                }

                var colRoot = new GameObject("Colliders");
                colRoot.transform.SetParent(root.transform, false);
                colRoot.isStatic = true;

                SpawnStaticColliderBoxes(colRoot.transform, "Exterieur", manifest.exterior_colliders);
                SpawnStaticColliderBoxes(colRoot.transform, "Interieur", manifest.interior_colliders);
                FitStairRamps(colRoot.transform, spec.PrefabAssetName);

                bool publicBuilding = IsPublicBuilding(spec.PrefabAssetName);
                AttachDoorColliders(extNode.transform, manifest.exterior_doors, false, publicBuilding);
                if (intNode != null)
                    AttachDoorColliders(intNode.transform, manifest.interior_doors, true, publicBuilding);

                void ApplyMaterialsAndStaticFlags(GameObject branch)
                {
                    foreach (var animator in branch.GetComponentsInChildren<Animator>(true))
                        UnityEngine.Object.DestroyImmediate(animator);

                    foreach (var mr in branch.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        bool isMovable = IsDoorOrGateName(mr.gameObject.name);
                        if (!isMovable)
                        {
                            GameObjectUtility.SetStaticEditorFlags(
                                mr.gameObject,
                                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        }
                        else
                        {
                            mr.gameObject.isStatic = false;
                            GameObjectUtility.SetStaticEditorFlags(mr.gameObject, 0);
                        }

                        Material[] src = mr.sharedMaterials;
                        var dst = new Material[src.Length];
                        for (int i = 0; i < src.Length; i++)
                        {
                            string slotName = src[i] != null ? CleanBlenderMaterialName(src[i].name) : string.Empty;
                            if (!materials.TryGetValue(slotName, out Material mapped))
                            {
                                Debug.LogWarning($"[Architecture] Matériau non mappé '{slotName}' sur {mr.gameObject.name}");
                                mapped = materials["Mat_Arch_MurExt_Blanc"];
                            }
                            dst[i] = mapped;
                        }
                        mr.sharedMaterials = dst;
                    }
                }

                EnsureFolder(Path.GetDirectoryName(spec.PrefabPath)?.Replace('\\', '/'));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SpawnStaticColliderBoxes(Transform parent, string groupName, ColliderBoxData[] boxes)
        {
            if (boxes == null || boxes.Length == 0)
                return;

            var group = new GameObject(groupName);
            group.transform.SetParent(parent, false);
            group.isStatic = true;

            for (int i = 0; i < boxes.Length; i++)
            {
                ColliderBoxData box = boxes[i];
                if (box.center == null || box.size == null || box.center.Length < 3 || box.size.Length < 3)
                    continue;

                var go = new GameObject($"Col_{groupName}_{i}");
                go.transform.SetParent(group.transform, false);
                go.isStatic = true;

                bool rotated = box.euler != null && box.euler.Length == 3 &&
                    (Mathf.Abs(box.euler[0]) > 0.001f || Mathf.Abs(box.euler[1]) > 0.001f || Mathf.Abs(box.euler[2]) > 0.001f);

                if (rotated)
                {
                    go.transform.localPosition = new Vector3(box.center[0], box.center[1], box.center[2]);
                    go.transform.localEulerAngles = new Vector3(box.euler[0], box.euler[1], box.euler[2]);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = Vector3.zero;
                    bc.size = new Vector3(box.size[0], box.size[1], box.size[2]);
                }
                else
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = new Vector3(box.center[0], box.center[1], box.center[2]);
                    bc.size = new Vector3(box.size[0], box.size[1], box.size[2]);
                }
            }
        }

        /// <summary>
        /// Rampes des escaliers (pavés inclinés de 18 cm du manifeste) recalées sur les sols : dans les modèles,
        /// leur dessus passe par le nez des marches, environ 30 cm au-dessus du palier du bas et du palier du
        /// haut. Le personnage (pas de 35 cm) butait sur ce rebord incliné en montant comme en descendant.
        /// Chaque rampe est reconstruite du sol du bas (trouvé par lancer de rayon) au sol du haut, prolongée
        /// de 30 cm sur chaque palier : on monte et on descend sans accroc.
        /// </summary>
        private static void FitStairRamps(Transform colRoot, string label)
        {
            const int probeLayer = 31;
            var boxes = colRoot.GetComponentsInChildren<BoxCollider>(true);
            var layers = new Dictionary<GameObject, int>();
            foreach (BoxCollider b in boxes)
            {
                layers[b.gameObject] = b.gameObject.layer;
                b.gameObject.layer = probeLayer;
            }
            Physics.SyncTransforms();
            int fitted = 0;
            try
            {
                foreach (BoxCollider ramp in boxes)
                {
                    Transform t = ramp.transform;
                    Vector3 e = t.localEulerAngles;
                    float ax = Mathf.DeltaAngle(0f, e.x), az = Mathf.DeltaAngle(0f, e.z);
                    bool aboutX = Mathf.Abs(ax) >= 12f && Mathf.Abs(ax) <= 45f && Mathf.Abs(az) < 1f;
                    bool aboutZ = Mathf.Abs(az) >= 12f && Mathf.Abs(az) <= 45f && Mathf.Abs(ax) < 1f;
                    if ((!aboutX && !aboutZ) || ramp.size.y > 0.3f)
                        continue;

                    // Ligne du dessus de la rampe (monde : la racine du prefab est à l'origine pendant la construction).
                    Vector3 along = t.TransformDirection(aboutX ? Vector3.forward : Vector3.right);
                    float length = aboutX ? ramp.size.z : ramp.size.x;
                    float width = aboutX ? ramp.size.x : ramp.size.z;
                    Vector3 top = t.TransformPoint(ramp.center) + t.up * (ramp.size.y * 0.5f);
                    Vector3 a = top - along * (length * 0.5f), b = top + along * (length * 0.5f);
                    Vector3 low = a.y < b.y ? a : b, high = a.y < b.y ? b : a;
                    Vector3 flat = high - low;
                    flat.y = 0f;
                    if (flat.sqrMagnitude < 0.25f)
                        continue;
                    flat.Normalize();

                    ramp.enabled = false;
                    Physics.SyncTransforms();
                    bool lowHit = Physics.Raycast(low - flat * 0.3f + Vector3.up * 0.6f, Vector3.down, out RaycastHit floor, 1.6f, 1 << probeLayer);
                    bool highHit = Physics.Raycast(high + flat * 0.3f + Vector3.up * 0.6f, Vector3.down, out RaycastHit landing, 1.6f, 1 << probeLayer);
                    ramp.enabled = true;
                    // Sans dalle trouvée : rez-de-chaussée posé sur le sol de la ville (y = 0) en bas ; en haut,
                    // le palier est sous le nez de la dernière marche (environ 15 cm).
                    float floorY = lowHit ? floor.point.y : (low.y < 0.8f ? 0f : low.y - 0.15f);
                    float landingY = highHit ? landing.point.y : high.y - 0.15f;
                    if (landingY - floorY < 0.5f)
                        continue;

                    Vector3 p0 = new Vector3(low.x, floorY, low.z) - flat * 0.3f;
                    Vector3 p1 = new Vector3(high.x, landingY, high.z) + flat * 0.3f;
                    Vector3 dir = (p1 - p0).normalized;
                    Vector3 normal = Vector3.Cross(dir, Vector3.Cross(Vector3.up, dir)).normalized;
                    if (normal.y < 0f)
                        normal = -normal;
                    float slope = Mathf.Asin(Mathf.Clamp01(dir.y)) * Mathf.Rad2Deg;
                    if (slope > 40f)
                        continue;

                    const float thickness = 0.18f;
                    t.SetPositionAndRotation((p0 + p1) * 0.5f - normal * (thickness * 0.5f), Quaternion.LookRotation(dir, normal));
                    ramp.center = Vector3.zero;
                    ramp.size = new Vector3(width, thickness, Vector3.Distance(p0, p1));
                    fitted++;
                }
            }
            finally
            {
                foreach (var pair in layers)
                    pair.Key.layer = pair.Value;
                Physics.SyncTransforms();
            }
            if (fitted > 0)
                Debug.Log("[Architecture] " + label + " : " + fitted + " rampe(s) d'escalier recalée(s) sur les sols.");
        }

        private static void AttachDoorColliders(Transform branchRoot, DoorSpecData[] doors, bool interior, bool publicBuilding)
        {
            if (doors == null)
                return;

            foreach (DoorSpecData door in doors)
            {
                Transform target = FindChildRecursive(branchRoot, door.name);
                if (target == null)
                {
                    Debug.LogWarning("[Architecture] Porte/portail introuvable dans FBX : " + door.name);
                    continue;
                }

                target.gameObject.isStatic = false;
                GameObjectUtility.SetStaticEditorFlags(target.gameObject, 0);

                if (door.colliders == null)
                    continue;

                foreach (ColliderBoxData box in door.colliders)
                {
                    if (box.center == null || box.size == null || box.center.Length < 3 || box.size.Length < 3)
                        continue;

                    var bc = target.gameObject.AddComponent<BoxCollider>();
                    bc.center = new Vector3(box.center[0], box.center[1], box.center[2]);
                    bc.size = new Vector3(box.size[0], box.size[1], box.size[2]);
                }
                SetupDoorMotion(target, door, interior, publicBuilding);
            }
        }

        /// <summary>
        /// Rend une porte du manifeste ouvrable (<see cref="WorldDoor"/>) : poses fermée et ouverte calculées
        /// d'après son type (battant, coulissant, barrière levante) et sa pose exportée de Blender (ouverte si
        /// <c>default_rot_deg</c> ou <c>closed_offset_*</c> sont renseignés, fermée sinon).
        /// <list type="bullet">
        /// <item>portes intérieures : ouvertes au départ (aucune porte ne bloque un couloir) ;</item>
        /// <item>portails de bâtiments publics (CHU, mairie, lieux de culte...) : ouverts au départ ;</item>
        /// <item>cellules, prison, caisses et armureries : verrouillées, elles restent fermées.</item>
        /// </list>
        /// </summary>
        private static void SetupDoorMotion(Transform leaf, DoorSpecData door, bool interior, bool publicBuilding)
        {
            string lower = door.name.ToLowerInvariant();
            if (lower.Contains("cell") || lower.Contains("prison") || lower.Contains("caisse") || lower.Contains("armurerie"))
                return;

            Transform parent = leaf.parent;
            Vector3 up = parent.InverseTransformDirection(Vector3.up).normalized;
            Vector3 restPos = leaf.localPosition;
            Quaternion restRot = leaf.localRotation;
            Vector3 scale = leaf.localScale;
            ColliderBoxData box = door.colliders != null && door.colliders.Length > 0 ? door.colliders[0] : null;
            Vector3 center = box?.center != null && box.center.Length == 3 ? new Vector3(box.center[0], box.center[1], box.center[2]) : Vector3.zero;
            Vector3 size = box?.size != null && box.size.Length == 3 ? new Vector3(box.size[0], box.size[1], box.size[2]) : new Vector3(1.2f, 2.2f, 0.1f);
            Vector3 PointToParent(Vector3 local) => restPos + restRot * Vector3.Scale(scale, local);
            Vector3 DirToParent(Vector3 local) => restRot * Vector3.Scale(scale, local);

            Vector3 closedPos = restPos, openPos = restPos;
            Quaternion closedRot = restRot, openRot = restRot;
            bool restIsOpen = false;
            DoorMotion motion;
            switch (door.type)
            {
                case "sliding":
                {
                    motion = DoorMotion.Sliding;
                    bool alongZ = Mathf.Abs(door.closed_offset_y) > 0.001f || (Mathf.Abs(door.closed_offset_x) < 0.001f && size.z > size.x);
                    Vector3 axis = alongZ ? Vector3.forward : Vector3.right;
                    float offset = alongZ ? door.closed_offset_y : door.closed_offset_x;
                    if (Mathf.Abs(offset) > 0.001f)
                    {
                        // Exporté ouvert : la pose fermée est décalée de closed_offset le long du battant.
                        restIsOpen = true;
                        closedPos = restPos + DirToParent(axis * offset);
                    }
                    else
                    {
                        // Exporté fermé : il glisse de sa largeur, du côté où il dépasse de son pivot.
                        float c = alongZ ? center.z : center.x;
                        float w = alongZ ? size.z : size.x;
                        openPos = restPos + DirToParent(axis * ((c >= 0f ? 1f : -1f) * w * 0.95f));
                    }
                    break;
                }
                case "barrier_hinge":
                {
                    motion = DoorMotion.Barrier;
                    // Pivot donné en coordonnées Blender (x, y, z) -> axes locaux de l'objet importé (x, -z, y).
                    Vector3 pivotLocal = door.pivot_blender != null && door.pivot_blender.Length == 3
                        ? new Vector3(door.pivot_blender[0], -door.pivot_blender[2], door.pivot_blender[1])
                        : center - Vector3.right * size.x * 0.5f;
                    bool alongZ = size.z > size.x;
                    float side = alongZ ? center.z - pivotLocal.z : center.x - pivotLocal.x;
                    Vector3 armP = DirToParent((alongZ ? Vector3.forward : Vector3.right) * (side >= 0f ? 1f : -1f)).normalized;
                    Vector3 axisP = Vector3.Cross(armP, up);
                    if (axisP.sqrMagnitude < 1e-4f)
                        return;
                    Vector3 pivotP = PointToParent(pivotLocal);
                    Quaternion lift = Quaternion.AngleAxis(80f, axisP.normalized);
                    openRot = lift * restRot;
                    openPos = pivotP + lift * (restPos - pivotP);
                    break;
                }
                default:
                {
                    motion = DoorMotion.Hinged;
                    float yaw = door.default_rot_deg != null && door.default_rot_deg.Length == 3 ? door.default_rot_deg[2] : 0f;
                    if (Mathf.Abs(yaw) > 1f)
                    {
                        // Exporté entrouvert : rotation Blender autour de Z (sens direct) = -yaw autour de Y dans Unity.
                        restIsOpen = true;
                        closedRot = Quaternion.AngleAxis(yaw, up) * restRot;
                        openRot = Quaternion.AngleAxis(-Mathf.Sign(yaw) * 90f, up) * closedRot;
                    }
                    else
                    {
                        openRot = Quaternion.AngleAxis(-90f, up) * restRot;
                    }
                    break;
                }
            }

            bool vehicleGate = door.name.StartsWith("Gate_", StringComparison.Ordinal) && Mathf.Max(size.x, size.z) >= 3.4f;
            bool openByDefault = interior || (vehicleGate ? publicBuilding : restIsOpen);

            // Poignée (point d'interaction) : au milieu du battant fermé, au niveau du sol.
            Vector3 closedCenter = closedPos + closedRot * Vector3.Scale(scale, center);
            closedCenter -= up * Vector3.Dot(closedCenter - closedPos, up);
            var handle = new GameObject("Poignee_" + door.name);
            handle.transform.SetParent(parent, false);
            handle.transform.localPosition = closedCenter;
            var worldDoor = handle.AddComponent<WorldDoor>();
            worldDoor.EditorSetupDoor(leaf, motion, closedPos, closedRot, openPos, openRot, vehicleGate, openByDefault);
            var so = new SerializedObject(worldDoor);
            so.FindProperty("_range").floatValue = vehicleGate ? 5.5f : 2.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            leaf.localPosition = openByDefault ? openPos : closedPos;
            leaf.localRotation = openByDefault ? openRot : closedRot;
        }

        private static bool IsDoorOrGateName(string objectName)
        {
            return objectName.StartsWith("Gate_", StringComparison.Ordinal) ||
                   objectName.StartsWith("Door_", StringComparison.Ordinal);
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root.name == targetName)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static string CleanBlenderMaterialName(string rawName)
        {
            string cleaned = rawName.Replace(" (Instance)", string.Empty);
            int dot = cleaned.LastIndexOf('.');
            if (dot > 0 && int.TryParse(cleaned.Substring(dot + 1), out _))
                cleaned = cleaned.Substring(0, dot);
            return cleaned;
        }

        private static void ConfigureFbxImporter(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            }
            if (importer == null)
                return;

            bool needsReimport =
                !Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.useFileUnits ||
                !importer.useFileScale ||
                importer.bakeAxisConversion ||
                importer.isReadable ||
                importer.addCollider ||
                importer.importCameras ||
                importer.importLights ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importTangents != ModelImporterTangents.CalculateMikk ||
                importer.meshCompression != ModelImporterMeshCompression.Medium ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard;

            if (!needsReimport)
                return;

            importer.globalScale = 1f;
            importer.useFileUnits = true;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, Material> EnsureMaterials()
        {
            ArchitectureTextures.EnsureGenerated();

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Shader nightShader = Shader.Find("IvoryState/NightLight");

            var map = new Dictionary<string, Material>(StringComparer.Ordinal);

            void ResetOpaqueLitState(Material mat)
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", null);
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = -1;
                mat.SetShaderPassEnabled("ShadowCaster", true);
            }

            Material CreateLitTextured(string name, string textureId, float tiling, float smoothness, Color tint, bool normal = true)
            {
                Material mat = GetOrCreateMaterial(name, litShader);
                ResetOpaqueLitState(mat);
                Texture2D albedo = CityMaterials.Albedo(textureId, 2);
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                mat.SetColor("_BaseColor", tint);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                Texture2D nor = normal ? CityMaterials.Normal(textureId, 2) : null;
                mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", 1f);
                if (nor != null)
                    mat.EnableKeyword("_NORMALMAP");
                else
                    mat.DisableKeyword("_NORMALMAP");
                return SaveMaterial(mat);
            }

            Material CreateCustomLitTextured(string name, string texPrefix, float tiling, float metallic, float smoothness, Color tint, float bumpScale = 1f)
            {
                Material mat = GetOrCreateMaterial(name, litShader);
                ResetOpaqueLitState(mat);
                Texture2D albedo = ArchitectureTextures.Albedo(texPrefix, 4);
                Texture2D nor = ArchitectureTextures.Normal(texPrefix, 4);
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                mat.SetColor("_BaseColor", tint);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", bumpScale);
                if (nor != null)
                    mat.EnableKeyword("_NORMALMAP");
                else
                    mat.DisableKeyword("_NORMALMAP");
                return SaveMaterial(mat);
            }

            Material CreateLitPlain(string name, Color color, float metallic, float smoothness, string normalPrefix = null, float normalTiling = 1f, float bumpScale = 0.35f)
            {
                Material mat = GetOrCreateMaterial(name, litShader);
                ResetOpaqueLitState(mat);
                mat.SetTexture("_BaseMap", null);
                mat.SetTextureScale("_BaseMap", new Vector2(normalTiling, normalTiling));
                Texture2D nor = !string.IsNullOrEmpty(normalPrefix) ? ArchitectureTextures.Normal(normalPrefix, 2) : null;
                mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", bumpScale);
                if (nor != null)
                    mat.EnableKeyword("_NORMALMAP");
                else
                    mat.DisableKeyword("_NORMALMAP");
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                return SaveMaterial(mat);
            }

            Material CreateGlass(string name, Color color, float smoothness)
            {
                Material mat = GetOrCreateMaterial(name, litShader);
                mat.SetTexture("_BaseMap", null);
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
                mat.DisableKeyword("_EMISSION");
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", 0.12f);
                mat.SetFloat("_Smoothness", smoothness);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                // Laisser la lumière du jour pénétrer à travers les baies vitrées dans le salon.
                mat.SetShaderPassEnabled("ShadowCaster", false);
                return SaveMaterial(mat);
            }

            Material CreateTranslucentTextured(string name, string albedoFile, string normalFile, float tiling, Color color, float metallic, float smoothness, Color? emission = null)
            {
                Material mat = GetOrCreateMaterial(name, litShader);
                Texture2D albedo = ArchitectureTextures.Albedo(albedoFile, 4);
                Texture2D nor = !string.IsNullOrEmpty(normalFile) ? ArchitectureTextures.Normal(normalFile, 4) : null;
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", 0.9f);
                if (nor != null)
                    mat.EnableKeyword("_NORMALMAP");
                else
                    mat.DisableKeyword("_NORMALMAP");
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission.HasValue)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", albedo);
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                }
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetShaderPassEnabled("ShadowCaster", false);
                return SaveMaterial(mat);
            }

            Material CreateNight(string name, Color color, float dayIntensity, float nightIntensity, string texFile = null, float tiling = 1f)
            {
                Material mat = GetOrCreateMaterial(name, nightShader);
                Texture tex = !string.IsNullOrEmpty(texFile)
                    ? (Texture)ArchitectureTextures.Albedo(texFile, 2)
                    : Texture2D.whiteTexture;
                mat.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
                mat.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
                mat.SetColor("_Color", color);
                mat.SetFloat("_DayIntensity", dayIntensity);
                mat.SetFloat("_NightIntensity", nightIntensity);
                mat.SetFloat("_Additive", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                return SaveMaterial(mat);
            }

            // 23 matériaux architecturaux de base
            map["Mat_Arch_MurExt_Blanc"] = CreateCustomLitTextured("Mat_Arch_MurExt_Blanc", "T_Arch_MurBlanc", 0.55f, 0f, 0.18f, Color.white, 0.45f);
            map["Mat_Arch_MurExt_Anthracite"] = CreateLitTextured("Mat_Arch_MurExt_Anthracite", "painted_plaster_wall", 0.40f, 0.18f, new Color(0.24f, 0.26f, 0.29f));
            map["Mat_Arch_Parement_Pierre"] = CreateLitTextured("Mat_Arch_Parement_Pierre", "brick_wall_005", 0.45f, 0.18f, new Color(0.94f, 0.90f, 0.84f));
            map["Mat_Arch_Beton_Brut"] = CreateLitTextured("Mat_Arch_Beton_Brut", "concrete", 0.35f, 0.18f, new Color(0.86f, 0.85f, 0.83f));
            map["Mat_Arch_Paves_Cour"] = CreateCustomLitTextured("Mat_Arch_Paves_Cour", "T_Arch_PavesClairs", 0.50f, 0f, 0.16f, Color.white, 0.90f);
            map["Mat_Arch_Gazon"] = CreateLitTextured("Mat_Arch_Gazon", "leafy_grass", 0.45f, 0.10f, new Color(0.74f, 0.90f, 0.58f));
            map["Mat_Arch_Toiture_Terrasse"] = CreateLitTextured("Mat_Arch_Toiture_Terrasse", "concrete_pavement_02", 0.35f, 0.15f, new Color(0.72f, 0.70f, 0.67f));
            map["Mat_Arch_Metal_Anthracite"] = CreateLitPlain("Mat_Arch_Metal_Anthracite", new Color(0.16f, 0.18f, 0.21f), 0.78f, 0.54f, "T_Arch_BrushedMetal", 1.5f, 0.35f);
            map["Mat_Arch_Metal_Inox"] = CreateLitPlain("Mat_Arch_Metal_Inox", new Color(0.82f, 0.84f, 0.86f), 0.88f, 0.78f, "T_Arch_BrushedMetal", 1.8f, 0.25f);
            map["Mat_Arch_Bois_Exterieur"] = CreateLitTextured("Mat_Arch_Bois_Exterieur", "dark_wood", 0.70f, 0.36f, new Color(0.98f, 0.74f, 0.52f));
            map["Mat_Arch_Vitre_Ext"] = CreateGlass("Mat_Arch_Vitre_Ext", new Color(0.52f, 0.72f, 0.82f, 0.30f), 0.92f);
            map["Mat_Arch_Lumiere_Nuit"] = CreateNight("Mat_Arch_Lumiere_Nuit", new Color(1.00f, 0.86f, 0.62f), 0.08f, 2.50f);
            map["Mat_Arch_Lumiere_Int_Nuit"] = CreateNight("Mat_Arch_Lumiere_Int_Nuit", new Color(1.00f, 0.91f, 0.74f), 0.25f, 1.65f);
            map["Mat_Arch_MurInt_Creme"] = CreateCustomLitTextured("Mat_Arch_MurInt_Creme", "T_Arch_MurCreme", 0.55f, 0f, 0.15f, Color.white, 0.35f);
            map["Mat_Arch_Sol_Marbre"] = CreateCustomLitTextured("Mat_Arch_Sol_Marbre", "T_Arch_MarbreBlanc", 0.52f, 0f, 0.68f, Color.white, 0.55f);
            map["Mat_Arch_Sol_Carrelage"] = CreateCustomLitTextured("Mat_Arch_Sol_Carrelage", "T_Arch_CarrelageClair", 0.60f, 0f, 0.45f, Color.white, 0.75f);
            map["Mat_Arch_Sol_Terrasse"] = CreateCustomLitTextured("Mat_Arch_Sol_Terrasse", "T_Arch_TerrasseClair", 0.52f, 0f, 0.28f, Color.white, 0.85f);
            map["Mat_Arch_Sol_Parquet"] = CreateCustomLitTextured("Mat_Arch_Sol_Parquet", "T_Arch_ParquetChaud", 0.62f, 0f, 0.48f, Color.white, 0.80f);
            map["Mat_Arch_Bois_Interieur"] = CreateLitTextured("Mat_Arch_Bois_Interieur", "dark_wood", 0.80f, 0.38f, new Color(0.92f, 0.68f, 0.48f));
            map["Mat_Arch_Tissu_Mobilier"] = CreateLitTextured("Mat_Arch_Tissu_Mobilier", "fabric_pattern_07", 0.80f, 0.10f, new Color(0.55f, 0.61f, 0.68f));
            map["Mat_Arch_Plan_Travail"] = CreateLitTextured("Mat_Arch_Plan_Travail", "marble_mosaic_tiles", 0.60f, 0.68f, new Color(0.45f, 0.48f, 0.53f));
            map["Mat_Arch_Ceramique_SDB"] = CreateLitPlain("Mat_Arch_Ceramique_SDB", new Color(0.96f, 0.96f, 0.97f), 0f, 0.78f);
            map["Mat_Arch_Cuve_Bleue"] = CreateLitPlain("Mat_Arch_Cuve_Bleue", new Color(0.11f, 0.36f, 0.70f), 0f, 0.52f);

            // 10 matériaux villa piscine, Tour F & Stade Félix Houphouët-Boigny
            map["Mat_Arch_Eau_Piscine"] = CreateTranslucentTextured("Mat_Arch_Eau_Piscine", "T_Arch_WaterRipple_diff.png", "T_Arch_WaterRipple_nor.png", 0.35f, new Color(0.55f, 0.90f, 1.00f, 0.72f), 0.08f, 0.95f);
            map["Mat_Arch_Piscine_Fond"] = CreateCustomLitTextured("Mat_Arch_Piscine_Fond", "T_Arch_PiscineMosaic", 0.65f, 0.02f, 0.72f, Color.white, 0.70f);
            map["Mat_Arch_MurInt_Accent"] = CreateCustomLitTextured("Mat_Arch_MurInt_Accent", "T_Arch_MurBlanc", 0.55f, 0f, 0.20f, new Color(0.76f, 0.81f, 0.78f), 0.45f);
            map["Mat_Arch_Vitre_GratteCiel"] = CreateLitPlain("Mat_Arch_Vitre_GratteCiel", new Color(0.18f, 0.40f, 0.58f), 0.68f, 0.94f, "T_Arch_BrushedMetal", 0.8f, 0.15f);
            map["Mat_Arch_Fenetre_Nuit"] = CreateNight("Mat_Arch_Fenetre_Nuit", new Color(1.00f, 0.90f, 0.68f), 0.18f, 2.20f);
            map["Mat_Arch_Pelouse_Stade"] = CreateCustomLitTextured("Mat_Arch_Pelouse_Stade", "T_Arch_PelouseStade", 0.25f, 0f, 0.14f, Color.white, 0.55f);
            map["Mat_Arch_Piste_Athletisme"] = CreateCustomLitTextured("Mat_Arch_Piste_Athletisme", "T_Arch_PisteAthletisme", 0.45f, 0f, 0.22f, Color.white, 0.75f);
            map["Mat_Arch_Siege_Orange"] = CreateLitPlain("Mat_Arch_Siege_Orange", new Color(0.95f, 0.46f, 0.06f), 0.04f, 0.48f);
            map["Mat_Arch_Siege_Vert"] = CreateLitPlain("Mat_Arch_Siege_Vert", new Color(0.06f, 0.58f, 0.26f), 0.04f, 0.48f);
            map["Mat_Arch_Siege_Blanc"] = CreateLitPlain("Mat_Arch_Siege_Blanc", new Color(0.94f, 0.94f, 0.92f), 0.04f, 0.48f);

            // 19 matériaux des monuments, hôpitaux, police, lieux de culte et drapeaux
            map["Mat_Arch_Or_Ceremonie"] = CreateCustomLitTextured("Mat_Arch_Or_Ceremonie", "T_Arch_BrushedGold", 0.85f, 0.90f, 0.78f, Color.white, 0.8f);
            map["Mat_Arch_Tapis_Rouge"] = CreateCustomLitTextured("Mat_Arch_Tapis_Rouge", "T_Arch_TapisRouge", 0.35f, 0f, 0.16f, Color.white, 0.9f);
            map["Mat_Arch_Police_Bleu"] = CreateLitTextured("Mat_Arch_Police_Bleu", "painted_plaster_wall", 0.42f, 0.44f, new Color(0.10f, 0.28f, 0.64f));
            map["Mat_Arch_Drapeau_Orange"] = CreateLitTextured("Mat_Arch_Drapeau_Orange", "fabric_pattern_07", 1.20f, 0.15f, new Color(0.98f, 0.48f, 0.05f));
            map["Mat_Arch_Drapeau_Vert"] = CreateLitTextured("Mat_Arch_Drapeau_Vert", "fabric_pattern_07", 1.20f, 0.15f, new Color(0.06f, 0.62f, 0.28f));
            map["Mat_Arch_Eau_Bassin"] = CreateTranslucentTextured("Mat_Arch_Eau_Bassin", "T_Arch_WaterRipple_diff.png", "T_Arch_WaterRipple_nor.png", 0.32f, new Color(0.85f, 0.96f, 1.00f, 0.78f), 0.06f, 0.94f);
            map["Mat_Arch_Lumiere_Ecran"] = CreateNight("Mat_Arch_Lumiere_Ecran", new Color(0.32f, 0.82f, 1.00f), 0.70f, 1.65f, "T_Arch_ScreenUI_diff.png", 0.9f);
            map["Mat_Arch_Lumiere_Gyro_Bleu"] = CreateNight("Mat_Arch_Lumiere_Gyro_Bleu", new Color(0.14f, 0.50f, 1.00f), 0.75f, 3.20f, "T_Arch_BeaconLens_diff.png", 1.0f);
            map["Mat_Arch_Lumiere_Gyro_Rouge"] = CreateNight("Mat_Arch_Lumiere_Gyro_Rouge", new Color(1.00f, 0.16f, 0.14f), 0.75f, 3.20f, "T_Arch_BeaconLens_diff.png", 1.0f);
            map["Mat_Arch_Hopital_Teal"] = CreateLitTextured("Mat_Arch_Hopital_Teal", "anti_skid_tiles", 0.55f, 0.48f, new Color(0.18f, 0.66f, 0.64f));
            map["Mat_Arch_Hopital_Menthe"] = CreateLitTextured("Mat_Arch_Hopital_Menthe", "painted_plaster_wall", 0.40f, 0.24f, new Color(0.90f, 0.98f, 0.95f));
            map["Mat_Arch_Croix_Rouge"] = CreateLitPlain("Mat_Arch_Croix_Rouge", new Color(0.92f, 0.09f, 0.09f), 0.05f, 0.62f, "T_Arch_BrushedMetal", 1.2f, 0.20f);
            map["Mat_Arch_Lumiere_Croix"] = CreateNight("Mat_Arch_Lumiere_Croix", new Color(1.00f, 0.04f, 0.04f), 0.90f, 2.40f);
            map["Mat_Arch_Vitrail_Bleu"] = CreateTranslucentTextured("Mat_Arch_Vitrail_Bleu", "T_Arch_Vitrail_Bleu_diff.png", "T_Arch_Vitrail_Bleu_nor.png", 0.45f, new Color(0.95f, 0.98f, 1.00f, 0.68f), 0.05f, 0.90f, new Color(0.14f, 0.28f, 0.65f));
            map["Mat_Arch_Vitrail_Or"] = CreateTranslucentTextured("Mat_Arch_Vitrail_Or", "T_Arch_Vitrail_Or_diff.png", "T_Arch_Vitrail_Bleu_nor.png", 0.45f, new Color(1.00f, 0.96f, 0.88f, 0.68f), 0.05f, 0.90f, new Color(0.55f, 0.40f, 0.08f));
            map["Mat_Arch_Vitrail_Rubis"] = CreateTranslucentTextured("Mat_Arch_Vitrail_Rubis", "T_Arch_Vitrail_Rubis_diff.png", "T_Arch_Vitrail_Bleu_nor.png", 0.45f, new Color(1.00f, 0.92f, 0.94f, 0.68f), 0.05f, 0.90f, new Color(0.52f, 0.08f, 0.14f));
            map["Mat_Arch_Mosquee_Bleu"] = CreateCustomLitTextured("Mat_Arch_Mosquee_Bleu", "T_Arch_ZelligeBlue", 0.65f, 0.05f, 0.74f, Color.white, 1.0f);
            map["Mat_Arch_Mosquee_Vert"] = CreateCustomLitTextured("Mat_Arch_Mosquee_Vert", "T_Arch_PrayerCarpet", 0.40f, 0f, 0.15f, Color.white, 0.9f);
            map["Mat_Arch_Pierre_Ocre"] = CreateCustomLitTextured("Mat_Arch_Pierre_Ocre", "T_Arch_PierreOcre", 0.35f, 0f, 0.24f, Color.white, 1.0f);

            // 6 matériaux de la Base Militaire de Yopougon (kaki, camo, sable, asphalte, marquage, tôle verte)
            map["Mat_Arch_Militaire_Kaki"] = CreateLitTextured("Mat_Arch_Militaire_Kaki", "painted_plaster_wall", 0.42f, 0.26f, new Color(0.30f, 0.35f, 0.24f));
            map["Mat_Arch_Militaire_CamoVert"] = CreateLitTextured("Mat_Arch_Militaire_CamoVert", "painted_plaster_wall", 0.45f, 0.22f, new Color(0.20f, 0.30f, 0.19f));
            map["Mat_Arch_Militaire_Sable"] = CreateLitTextured("Mat_Arch_Militaire_Sable", "painted_plaster_wall", 0.45f, 0.20f, new Color(0.80f, 0.73f, 0.59f));
            map["Mat_Arch_Asphalte_Base"] = CreateLitTextured("Mat_Arch_Asphalte_Base", "concrete_pavement_02", 0.38f, 0.12f, new Color(0.24f, 0.25f, 0.27f));
            map["Mat_Arch_Marquage_Jaune"] = CreateLitPlain("Mat_Arch_Marquage_Jaune", new Color(0.95f, 0.78f, 0.12f), 0.05f, 0.45f);
            map["Mat_Arch_Toiture_Tole_Verte"] = CreateLitPlain("Mat_Arch_Toiture_Tole_Verte", new Color(0.16f, 0.33f, 0.22f), 0.45f, 0.48f, "T_Arch_BrushedMetal", 1.6f, 0.30f);

            return map;
        }

        private static Material GetOrCreateMaterial(string name, Shader shader)
        {
            string path = MaterialsRoot + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static Material SaveMaterial(Material mat)
        {
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureArchitectureFolders()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(ArchitectureTextures.Folder);
            EnsureFolder(ModelsRoot + "/Monuments");
            EnsureFolder(ModelsRoot + "/Maisons");
            EnsureFolder(ModelsRoot + "/Interieurs");
            EnsureFolder(PrefabsRoot + "/Monuments");
            EnsureFolder(PrefabsRoot + "/Maisons");
            EnsureFolder(PrefabsRoot + "/Interieurs");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static Bounds CalculateCombinedRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void CaptureArchitectureViews(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var camGo = new GameObject("ArchVerifyCam");
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 1100f;
            var urpCam = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = true;
            urpCam.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            Light sun = null;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
            Material sky = RenderSettings.skybox;

            try
            {
                void CaptureMonument(string hierarchyPath, string prefix, Vector3 dayEyeLocal, Vector3 lookLocal, Vector3 intEyeLocal, Vector3 intLookLocal)
                {
                    GameObject go = GameObject.Find(hierarchyPath);
                    if (go == null)
                        return;
                    Transform t = go.transform;
                    Vector3 p(Vector3 v) => t.TransformPoint(v);

                    DayNightCycle.Apply(sun, 16.5f, sky);
                    RenderShot(cam, outDir, prefix + "_day.png", p(dayEyeLocal), p(lookLocal));
                    RenderShot(cam, outDir, prefix + "_int.png", p(intEyeLocal), p(intLookLocal));

                    DayNightCycle.Apply(sun, 21.0f, sky);
                    RenderShot(cam, outDir, prefix + "_night.png", p(dayEyeLocal), p(lookLocal));
                }

                // Duplex Cocody v2 (30 x 36 m avec piscine, jardin et grand escalier en U)
                Transform duplex = GameObject.Find("City/Architecture/Cocody/PF_Duplex_Cocody_01")?.transform;
                if (duplex != null)
                {
                    Vector3 dp(float lx, float ly, float lz) => duplex.TransformPoint(new Vector3(lx, ly, lz));
                    DayNightCycle.Apply(sun, 16.5f, sky);
                    RenderShot(cam, outDir, "unity_duplex_day_street.png", dp(-17f, 8.5f, 30f), dp(0f, 3.6f, -1f));
                    RenderShot(cam, outDir, "unity_duplex_day_pool.png", dp(11f, 4.5f, -22f), dp(-3f, 1.8f, -10f));
                    RenderShot(cam, outDir, "unity_duplex_day_interior.png", dp(6.0f, 2.3f, 2.5f), dp(-2.0f, 2.2f, -4.5f));
                    RenderShot(cam, outDir, "unity_duplex_day_stairs.png", dp(-1.12f, 2.1f, 2.4f), dp(-2.4f, 2.9f, -4.2f));
                    DayNightCycle.Apply(sun, 21.0f, sky);
                    RenderShot(cam, outDir, "unity_duplex_night_street.png", dp(-17f, 8.5f, 30f), dp(0f, 3.6f, -1f));
                }

                // Tour F (Plateau, 412 m) & Stade Félix Houphouët-Boigny (160 x 200 m)
                Transform tourF = GameObject.Find("City/Architecture/Plateau/PF_Tour_F")?.transform;
                if (tourF != null)
                {
                    Vector3 tp(float lx, float ly, float lz) => tourF.TransformPoint(new Vector3(lx, ly, lz));
                    DayNightCycle.Apply(sun, 16.5f, sky);
                    RenderShot(cam, outDir, "unity_tour_f_skyline.png", tp(-145f, 115f, 265f), tp(0f, 175f, 0f));
                    RenderShot(cam, outDir, "unity_tour_f_day.png", tp(-36f, 22f, 64f), tp(0f, 25f, 0f));
                    RenderShot(cam, outDir, "unity_tour_f_int.png", tp(4f, 3.2f, 16f), tp(-12f, 3.5f, -10f));
                    DayNightCycle.Apply(sun, 21.0f, sky);
                    RenderShot(cam, outDir, "unity_tour_f_night.png", tp(-145f, 115f, 265f), tp(0f, 175f, 0f));
                }

                Transform stade = GameObject.Find("City/Architecture/Plateau/PF_Stade_FHB")?.transform;
                if (stade != null)
                {
                    Vector3 sp(float lx, float ly, float lz) => stade.TransformPoint(new Vector3(lx, ly, lz));
                    DayNightCycle.Apply(sun, 16.5f, sky);
                    RenderShot(cam, outDir, "unity_stade_fhb_aerial.png", sp(-130f, 82f, 145f), sp(0f, 8f, 0f));
                    RenderShot(cam, outDir, "unity_stade_fhb_pitch.png", sp(-18f, 4.5f, 28f), sp(14f, 8f, -38f));
                    DayNightCycle.Apply(sun, 21.0f, sky);
                    RenderShot(cam, outDir, "unity_stade_fhb_night.png", sp(-130f, 82f, 145f), sp(0f, 8f, 0f));
                }

                // Les 6 nouveaux édifices & monuments
                CaptureMonument("City/Architecture/Plateau/PF_Palais_Presidentiel", "unity_palais",
                    new Vector3(-34f, 20f, 68f), new Vector3(0f, 6f, -4f),
                    new Vector3(0f, 4.2f, 16f), new Vector3(0f, 3.5f, -12f));
                CaptureMonument("City/Architecture/Plateau/PF_Mairie_Plateau", "unity_mairie",
                    new Vector3(-28f, 16f, 46f), new Vector3(0f, 5f, -4f),
                    new Vector3(0f, 3.2f, 9f), new Vector3(0f, 2.5f, -10f));
                CaptureMonument("City/Architecture/Plateau/PF_Commissariat_Central", "unity_commissariat",
                    new Vector3(-24f, 14f, 44f), new Vector3(0f, 4f, -2f),
                    new Vector3(-6f, 2.5f, 9f), new Vector3(4f, 2.2f, -12f));
                CaptureMonument("City/Architecture/Plateau/PF_Cathedrale_Saint_Paul", "unity_cathedrale",
                    new Vector3(-32f, 18f, 56f), new Vector3(0f, 8f, -6f),
                    new Vector3(0f, 4.0f, 12f), new Vector3(0f, 3.5f, -20f));
                CaptureMonument("City/Architecture/Plateau/PF_Grande_Mosquee", "unity_mosquee",
                    new Vector3(-34f, 20f, 58f), new Vector3(0f, 10f, -8f),
                    new Vector3(0f, 4.0f, 6f), new Vector3(0f, 3.0f, -24f));
                CaptureMonument("City/Architecture/Cocody/PF_Hopital_CHU", "unity_hopital",
                    new Vector3(-30f, 16f, 50f), new Vector3(0f, 5f, -4f),
                    new Vector3(0f, 2.6f, 11f), new Vector3(0f, 2.0f, -14f));
            }
            finally
            {
                DayNightCycle.Apply(sun, CityLighting.EditorHour, sky);
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static void RenderShot(UnityEngine.Camera cam, string dir, string file, Vector3 pos, Vector3 lookAt)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
