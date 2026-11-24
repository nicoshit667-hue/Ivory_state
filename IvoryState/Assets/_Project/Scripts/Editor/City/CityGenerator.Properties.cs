using System.Collections.Generic;
using IvoryState.Gameplay.Homes;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Biens immobiliers : entrées dans les rues (cercle doré et portail) et intérieurs construits d'après
    /// les plans d'aménagement (voir CityGenerator.Plans). Un modèle par type (maillages partagés), posé
    /// une fois par bien.
    /// </summary>
    internal static partial class CityGenerator
    {
        private static void CreatePropertyEntrances(Transform city, List<Mesh> meshes)
        {
            Material gateMaterial = GetOrCreateMaterial("M_PropertyGate", new Color(0.18f, 0.30f, 0.45f));
            Material villaGate = GetOrCreateMaterial("M_VillaGate", new Color(0.92f, 0.88f, 0.78f));
            var entrances = PropertyCatalog.Entrances;
            for (int index = 0; index < entrances.Length; index++)
            {
                PropertyCatalog.Entrance entrance = entrances[index];
                var root = new GameObject("Entree_" + index);
                root.transform.SetParent(city, false);
                root.transform.position = entrance.Position;

                // Repère local : +x vers le bâtiment (le portail), z le long du trottoir.
                root.transform.rotation = Quaternion.FromToRotation(Vector3.right, entrance.Inward);
                var ring = new BoxMeshBuilder();
                AddCircle(ring, new Vector3(0f, 0.02f, 0f), 1.3f, 0.16f, 32);
                AddCircle(ring, new Vector3(0f, 0.02f, 0f), 0.95f, 0.06f, 32);
                AddMeshObject(root.transform, "Cercle", ring, HomeRingMaterial(), meshes);

                bool house = false;
                foreach (PropertyCatalog.Property property in PropertyCatalog.Properties)
                {
                    if (property.EntranceIndex == index && PropertyCatalog.IsForSale(property.Kind))
                        house = true;
                }
                if (BlenderCityKit.HasModel("SM_PropertyGate"))
                {
                    var builders = new Dictionary<Material, BoxMeshBuilder>();
                    Matrix4x4 xf = Matrix4x4.TRS(new Vector3(1.3f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                    BlenderCityKit.Stamp("SM_PropertyGate", builders, xf,
                        house ? villaGate : gateMaterial, Color.white, index);
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
                    AddMeshObject(root.transform, "Portail", gate, house ? villaGate : gateMaterial, meshes);
                    var sign = new BoxMeshBuilder();
                    sign.AddBox(new Vector3(1.06f, 3.55f, 0f), new Vector3(0.10f, 0.72f, 2.8f));
                    AddMeshObject(root.transform, "Enseigne", sign, HomeRingMaterial(), meshes);
                }

                root.AddComponent<HomeEntrance>().EditorSetup(entrance.Name, 1.3f, Quaternion.Inverse(root.transform.rotation) * entrance.ExitOffset, index);
            }
        }

        /// <summary>Intérieurs de tous les biens, d'après les plans ; renvoie leurs visuels (index = n° du bien - 1).</summary>
        private static GameObject[] CreatePropertyInteriors(Transform root, List<Mesh> meshes)
        {
            var templates = new Dictionary<PropertyKind, PlanTemplate>();
            var visuals = new GameObject[PropertyCatalog.Count];
            foreach (PropertyCatalog.Property property in PropertyCatalog.Properties)
            {
                if (!templates.TryGetValue(property.Kind, out PlanTemplate template))
                {
                    template = BakePlan(BuildPlan(property.Kind), "Bien_" + property.Kind, meshes);
                    templates[property.Kind] = template;
                }
                visuals[property.Id - 1] = PlaceTemplate(root, "Bien_" + property.Id, PropertyCatalog.InteriorCenter(property.Id), template);
            }
            Debug.Log("[City] " + PropertyCatalog.Count + " intérieurs de biens immobiliers générés (d'après les plans).");
            return visuals;
        }
    }
}
