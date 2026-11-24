using System.Collections.Generic;
using IvoryState.Gameplay.Homes;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Logements : l'entrée de la Résidence Les Flamboyants (Treichville, au débouché du pont ouest) et
    /// les studios en colocation, des pièces fermées rangées loin sous la ville (voir StudioLayout).
    /// Un seul jeu de maillages pour tous les studios (partagés) ; le client n'affiche que le studio
    /// occupé par le joueur local (StudioVisibility).
    /// </summary>
    internal static partial class CityGenerator
    {
        private const string ResidenceName = "Résidence Les Flamboyants";

        // Trottoir ouest du bloc (0,-2), face à la rue qui descend du pont ouest.
        private static readonly Vector3 ResidenceEntrance = new Vector3(-30.5f, SidewalkHeight, -140f);

        private static void CreateResidenceEntrance(Transform city, List<Mesh> meshes)
        {
            var root = new GameObject("Residence_Flamboyants");
            root.transform.SetParent(city, false);
            // Repère réservé sur un trottoir de Treichville ; +x local vers l'immeuble (le portail).
            Gameplay.World.CityLayout.Place place = Gameplay.World.CityPlaces.Residence;
            root.transform.position = place.Position;
            root.transform.rotation = Quaternion.FromToRotation(Vector3.right, -place.ToRoad);
            Vector3 exitLocal = Quaternion.Inverse(root.transform.rotation) * (place.Along * 3.6f);

            var ring = new BoxMeshBuilder();
            AddCircle(ring, new Vector3(0f, 0.02f, 0f), 1.3f, 0.16f, 32);
            AddCircle(ring, new Vector3(0f, 0.02f, 0f), 0.95f, 0.06f, 32);
            AddMeshObject(root.transform, "Cercle", ring, HomeRingMaterial(), meshes);

            // Portail Blender (piliers moulurés, vantaux fer forgé, corniche et enseigne) au débouché du trottoir.
            if (BlenderCityKit.HasModel("SM_PropertyGate"))
            {
                var builders = new Dictionary<Material, BoxMeshBuilder>();
                Matrix4x4 xf = Matrix4x4.TRS(new Vector3(1.3f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                BlenderCityKit.Stamp("SM_PropertyGate", builders, xf,
                    GetOrCreateMaterial("M_ResidenceGate", new Color(0.55f, 0.18f, 0.12f)), Color.white, 0);
                BlenderCityKit.BuildObjects(root.transform, "Portail", builders, meshes);
            }
            else
            {
                var gate = new BoxMeshBuilder();
                gate.AddBox(new Vector3(1.3f, 0f, -1.65f), new Vector3(0.55f, 0.35f, 0.55f));
                gate.AddBox(new Vector3(1.3f, 0f, 1.65f), new Vector3(0.55f, 0.35f, 0.55f));
                gate.AddBox(new Vector3(1.3f, 0.35f, -1.65f), new Vector3(0.38f, 2.8f, 0.38f));
                gate.AddBox(new Vector3(1.3f, 0.35f, 1.65f), new Vector3(0.38f, 2.8f, 0.38f));
                gate.AddBox(new Vector3(1.3f, 3.15f, 0f), new Vector3(0.55f, 0.4f, 3.85f));
                AddMeshObject(root.transform, "Portail", gate, GetOrCreateMaterial("M_ResidenceGate", new Color(0.55f, 0.18f, 0.12f)), meshes);
                var sign = new BoxMeshBuilder();
                sign.AddBox(new Vector3(1.06f, 3.55f, 0f), new Vector3(0.10f, 0.72f, 2.8f));
                AddMeshObject(root.transform, "Enseigne", sign, HomeRingMaterial(), meshes);
            }

            var entrance = root.AddComponent<HomeEntrance>();
            entrance.EditorSetup(ResidenceName, 1.3f, exitLocal);
        }

        private static Material HomeRingMaterial()
        {
            Material material = GetOrCreateMaterial("M_HomeRing", new Color(1f, 0.78f, 0.25f));
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.9f, 0.6f, 0.1f) * 0.8f);
            return material;
        }

        private static Material Emissive(string name, Color color, Color emission)
        {
            Material material = GetOrCreateMaterial(name, color);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            return material;
        }

        /// <summary>
        /// Studios de colocation (d'après le plan du studio 6 × 4,5), puis intérieurs des biens et des
        /// hôpitaux ; un seul composant d'affichage pour tous (StudioVisibility).
        /// </summary>
        private static void CreateStudios(Transform parent, List<Mesh> meshes)
        {
            var root = new GameObject("Interieurs");
            root.transform.SetParent(parent, false);

            PlanTemplate template = BakePlan(BuildStudioPlan(), "Studio", meshes);
            var visuals = new GameObject[StudioLayout.Count];
            for (int n = 1; n <= StudioLayout.Count; n++)
                visuals[n - 1] = PlaceTemplate(root.transform, "Studio_" + n, StudioLayout.Center(n), template);

            // Biens immobiliers (appartements, villas) et hôpitaux : mêmes règles d'affichage.
            GameObject[] properties = CreatePropertyInteriors(root.transform, meshes);
            GameObject[] hospitals = CreateHospitalInteriors(root.transform, meshes);
            var all = new GameObject[visuals.Length + properties.Length + hospitals.Length];
            visuals.CopyTo(all, 0);
            properties.CopyTo(all, visuals.Length);
            hospitals.CopyTo(all, visuals.Length + properties.Length);
            root.AddComponent<Client.World.StudioVisibility>().EditorSetVisuals(all);
            Debug.Log("[City] " + StudioLayout.Count + " studios de colocation générés.");
        }
    }
}
