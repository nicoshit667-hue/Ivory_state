using System.Collections.Generic;
using IvoryState.Gameplay.Combat;
using UnityEngine;
using F = IvoryState.Editor.City.PlanBuilder.Facing;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Hôpitaux : entrée dans la rue (cercle, portail blanc et croix rouge lumineuse) et CHU intérieur
    /// agrandi (28 × 22 unités de plan = 42 × 33 m, plafond relevé à 4,8 m, doubles portes 2,4–3,0 × 2,9 m) :
    /// grand hall d'accueil, couloir central large de 6,75 m, urgences, consultations, pharmacie, laboratoire,
    /// grande salle d'hospitalisation à 4 lits, bureau du médecin et sanitaires. Voir HospitalCatalog.
    /// </summary>
    internal static partial class CityGenerator
    {
        private static void CreateHospitalEntrances(Transform city, List<Mesh> meshes)
        {
            Material white = GetOrCreateMaterial("M_HospitalWhite", new Color(0.95f, 0.96f, 0.96f));
            Material slate = GetOrCreateMaterial("M_HospitalSlate", new Color(0.24f, 0.36f, 0.42f));
            Material red = Emissive("M_HospitalRed", new Color(0.85f, 0.08f, 0.08f), new Color(0.6f, 0.02f, 0.02f));
            for (int index = 0; index < HospitalCatalog.Count; index++)
            {
                HospitalCatalog.Hospital hospital = HospitalCatalog.Hospitals[index];
                var root = new GameObject("Hopital_" + index);
                root.transform.SetParent(city, false);
                root.transform.position = hospital.Entrance;
                root.transform.rotation = Quaternion.FromToRotation(Vector3.right, hospital.Inward);

                var ring = new BoxMeshBuilder();
                AddCircle(ring, new Vector3(0f, 0.02f, 0f), 1.3f, 0.16f, 32);
                AddMeshObject(root.transform, "Cercle", ring, red, meshes);

                if (ArchitectureSetup.ReplacesHospitalEntrance(index))
                    continue;

                if (BlenderCityKit.HasModel("SM_HospitalGate"))
                {
                    var builders = new Dictionary<Material, BoxMeshBuilder>();
                    Matrix4x4 xf = Matrix4x4.TRS(new Vector3(1.3f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
                    BlenderCityKit.Stamp("SM_HospitalGate", builders, xf, white, Color.white, index);
                    BlenderCityKit.BuildObjects(root.transform, "Portail", builders, meshes);
                }
                else
                {
                    var plinths = new BoxMeshBuilder();
                    plinths.AddBox(new Vector3(1.3f, 0f, -1.85f), new Vector3(0.65f, 0.45f, 0.65f));
                    plinths.AddBox(new Vector3(1.3f, 0f, 1.85f), new Vector3(0.65f, 0.45f, 0.65f));
                    plinths.AddBox(new Vector3(0.95f, 3.35f, 0f), new Vector3(1.1f, 0.12f, 4.4f));
                    AddMeshObject(root.transform, "SoclePortail", plinths, slate, meshes);

                    var gate = new BoxMeshBuilder();
                    gate.AddBox(new Vector3(1.3f, 0.45f, -1.85f), new Vector3(0.45f, 3.15f, 0.45f));
                    gate.AddBox(new Vector3(1.3f, 0.45f, 1.85f), new Vector3(0.45f, 3.15f, 0.45f));
                    gate.AddBox(new Vector3(1.3f, 3.60f, 0f), new Vector3(0.6f, 0.55f, 4.4f));
                    gate.AddBox(new Vector3(1.15f, 4.15f, 0f), new Vector3(0.14f, 1.45f, 1.45f));
                    AddMeshObject(root.transform, "Portail", gate, white, meshes);

                    var cross = new BoxMeshBuilder();
                    cross.AddBox(new Vector3(1.06f, 4.38f, 0f), new Vector3(0.06f, 1.05f, 0.32f));
                    cross.AddBox(new Vector3(1.06f, 4.74f, 0f), new Vector3(0.06f, 0.32f, 1.05f));
                    AddMeshObject(root.transform, "Croix", cross, red, meshes);
                }
            }
        }

        private static PlanBuilder BuildHospitalPlan()
        {
            const float h = 4.8f;
            PlanBuilder b = NewPlan(false, true);
            b.Register("Murs", IntWall(new Color(0.91f, 0.96f, 0.94f)));
            b.Register("Sol", IntHospitalFloor());
            b.Register("Draps", GetOrCreateMaterial("M_HospitalWhite", new Color(0.95f, 0.96f, 0.96f)));
            b.Register("Couverture", GetOrCreateMaterial("M_HospitalBlanket", new Color(0.22f, 0.58f, 0.56f)));
            b.Register("Paravent", GetOrCreateMaterial("M_HospitalCurtain", new Color(0.62f, 0.80f, 0.92f)));
            b.Register("Croix", Emissive("M_HospitalRed", new Color(0.85f, 0.08f, 0.08f), new Color(0.6f, 0.02f, 0.02f)));
            b.Register("Ecran", Emissive("M_VitalMonitor", new Color(0.10f, 0.72f, 0.38f), new Color(0.15f, 0.9f, 0.45f)));

            // Grande emprise complète (42 × 33 m) avec plafond haut à 4,8 m.
            b.Slab("Sol", -14f, -11f, 14f, 11f, 0f);
            b.Slab("Plafond", -14.1f, -11.1f, 14.1f, 11.1f, h + 0.25f, 0.25f);

            // Enveloppe extérieure : grande double porte d'entrée au sud (3,0 × 2,9 m) et accès ambulance à l'ouest.
            b.Wall("Murs", -14f, 11f, 14f, 11f, 0f, h);
            b.Wall("Murs", 14f, -11f, 14f, 11f, 0f, h);
            b.WideWall("Murs", -14f, -11f, -14f, 11f, 0f, h, 2.6f, 4f);
            b.WideWall("Murs", -14f, -11f, 14f, -11f, 0f, h, 3.0f, 14f);

            // Vantaux vitrés/alu de la porte d'entrée principale (hauteurs 2,95 m ajustées au linteau, zéro fente).
            Vector3 entrance = HospitalCatalog.ExitCircle(0) - HospitalCatalog.InteriorCenter(0);
            float southZ = b.S(-11f);
            foreach (float x in new[] { -0.74f, 0.74f })
            {
                b.WorldBox("Metal", new Vector3(x, 0f, southZ), new Vector3(1.42f, 2.95f, 0.07f), false);
                b.WorldBox("Lumiere", new Vector3(x, 0.45f, southZ), new Vector3(1.06f, 2.15f, 0.09f), false);
                b.WorldBox("Sombre", new Vector3(x, 1.15f, southZ + 0.055f), new Vector3(0.95f, 0.05f, 0.04f), false);
            }
            b.WorldSolid(new Vector3(0f, 1.48f, southZ), new Vector3(3.15f, 2.95f, 0.28f));
            AddCircle(b.P("Sortie"), entrance + new Vector3(0f, 0.012f, 0f), 0.75f, 0.08f, 28);

            // Porte double des ambulances (ouest, Urgences, z = -7 × 1,5 = -10,5 m).
            float westX = b.S(-14f) + 0.09f;
            foreach (float z in new[] { -0.64f, 0.64f })
            {
                b.WorldBox("Metal", new Vector3(westX, 0f, b.S(-7f) + z), new Vector3(0.06f, 2.95f, 1.24f), false);
                b.WorldBox("Lumiere", new Vector3(westX, 1.35f, b.S(-7f) + z), new Vector3(0.08f, 1.15f, 0.85f), false);
            }

            // Cloisons ailes Nord (z = 1,5..11) : Consultation 1, Consultation 2, Pharmacie, Laboratoire, Hospitalisation.
            b.Wall("Murs", -14f, 1.5f, -8.75f, 1.5f, 0f, h, 2.6f);
            b.Wall("Murs", -8.75f, 1.5f, -3.5f, 1.5f, 0f, h, 2.6f);
            b.Wall("Murs", -8.75f, 1.5f, -8.75f, 11f, 0f, h);

            b.WideWall("Murs", -3.5f, 1.5f, 1.0f, 1.5f, 0f, h, 2.4f, 2.25f);
            b.Wall("Murs", 1.0f, 1.5f, 5.5f, 1.5f, 0f, h, 2.25f);
            b.Wall("Murs", -3.5f, 1.5f, -3.5f, 11f, 0f, h);
            b.Wall("Murs", 1.0f, 1.5f, 1.0f, 11f, 0f, h, 4.5f);

            b.Wall("Murs", 5.5f, 1.5f, 5.5f, 11f, 0f, h);
            b.WideWall("Murs", 5.5f, 1.5f, 14f, 1.5f, 0f, h, 2.8f, 4.25f);

            // Cloisons ailes Sud (z = -11..-3) : Urgences, Grand Hall central, Toilettes & Bureau du médecin.
            b.Wall("Murs", -6f, -11f, -6f, -3f, 0f, h);
            b.WideWall("Murs", -14f, -3f, -6f, -3f, 0f, h, 2.8f, 4f);
            b.WideWall("Murs", -6f, -3f, 7.5f, -3f, 0f, h, 3.0f, 6.75f);

            b.Wall("Murs", 7.5f, -11f, 7.5f, -3f, 0f, h, 2.25f);
            b.Wall("Murs", 7.5f, -6.5f, 14f, -6.5f, 0f, h);
            b.Wall("Murs", 7.5f, -3f, 14f, -3f, 0f, h, 3.25f);
            b.Wall("Murs", 10.8f, -6.5f, 10.8f, -4.5f, 0f, 2.4f);

            // Bandeaux muraux turquoise uniquement sur les tronçons de murs pleins (aucune traversée de porte !).
            b.WorldBox("Couverture", b.Sc(new Vector3(-3.3f, 1.05f, -3.11f)), new Vector3(b.S(4.6f), 0.14f, 0.04f), false);
            b.WorldBox("Couverture", b.Sc(new Vector3(4.8f, 1.05f, -3.11f)), new Vector3(b.S(4.6f), 0.14f, 0.04f), false);
            b.WorldBox("Couverture", b.Sc(new Vector3(7.38f, 1.05f, -5.0f)), new Vector3(0.04f, 0.14f, b.S(3.2f)), false);
            b.WorldBox("Couverture", b.Sc(new Vector3(6.8f, 1.05f, 1.40f)), new Vector3(b.S(1.8f), 0.14f, 0.04f), false);
            b.WorldBox("Couverture", b.Sc(new Vector3(12.6f, 1.05f, 1.40f)), new Vector3(b.S(1.8f), 0.14f, 0.04f), false);

            // Lits (catalogue : 4 hospitalisation, 2 consultations, 2 urgences), paravents et tables de chevet.
            for (int bed = 0; bed < HospitalCatalog.Beds; bed++)
            {
                Vector3 p = HospitalCatalog.BedPosition(0, bed) - HospitalCatalog.InteriorCenter(0);
                int head = HospitalCatalog.BedHead(bed);
                b.HospitalBed(p, head == 0 ? F.North : F.South, withIvPole: bed < 4 || bed >= 6);
                if (bed < 4)
                {
                    if (bed < 3)
                        b.WorldBox("Paravent", p + new Vector3(1.5f, 0f, 0f), new Vector3(0.05f, 2.0f, 2.2f), false);
                    b.WorldProp("painted_wooden_nightstand", p + new Vector3(-0.88f, 0f, 0.65f), 180f, 0.90f, true, 0.004f);
                }
            }

            // Salle d'hospitalisation : poste infirmier complet (bureau plat), armoire médicale, lavabo loin des fenêtres.
            b.Table(12.0f, 3.8f, 0f, 1.6f, 0.8f, 1);
            b.PlanProp("classic_laptop", 12.2f, 3.8f, 0.74f, 180f, 0.55f, false, 0.004f);
            b.PlanProp("medical_box", 11.45f, 3.8f, 0.74f, 15f, 0.85f, false, 0.004f);
            b.PlanProp("office_notepads", 11.8f, 3.9f, 0.74f, 25f, 0.45f, false, 0.004f);
            b.PlanProp("steel_frame_shelves_01", 6.0f, 6.4f, 0f, 90f, 1.0f, true, 0.004f);
            b.PlanProp("potted_plant_04", 6.0f, 9.8f, 0f, 0f, 2.5f, false, 0.004f);
            b.Washbasin(13.6f, 2.8f, 0f, F.East);

            // Grand Hall d'Accueil : 6 îlots de chaises bien répartis, comptoir d'accueil en L, jardinières, tableaux.
            b.ChairRow(-4.6f, -5.0f, 0f, 6, false, F.East);
            b.ChairRow(-2.4f, -5.0f, 0f, 5, false, F.West);
            b.ChairRow(-1.4f, -5.0f, 0f, 5, false, F.East);
            b.ChairRow(0.4f, -5.0f, 0f, 5, false, F.West);
            b.ChairRow(1.4f, -5.0f, 0f, 5, false, F.East);
            b.ChairRow(-3.0f, -9.8f, 0f, 5, true, F.North);
            b.PlanProp("painted_wooden_bench", -4.8f, -10.3f, 0f, 0f, 1.1f, true, 0.004f);
            b.PlanProp("painted_wooden_bench", 5.8f, -10.3f, 0f, 0f, 1.1f, true, 0.004f);

            b.Counter(5.0f, -6.1f, 0f, 3.0f, F.West, false, false);
            b.Counter(3.8f, -7.4f, 0f, 2.2f, F.North, false, false);
            b.Counter(5.0f, -3.8f, 0f, 1.4f, F.West, false, false);
            b.PlanProp("CashRegister_01", 5.0f, -3.8f, 0.90f, 270f, 0.65f, false, 0.004f);
            b.PlanProp("classic_laptop", 5.0f, -5.4f, 0.90f, 270f, 0.55f, false, 0.004f);
            b.PlanProp("office_notepads", 4.1f, -7.4f, 0.90f, 30f, 0.45f, false, 0.004f);
            b.PlanProp("medical_tape", 5.0f, -6.5f, 0.90f, 0f, 1.0f, false, 0.004f);

            b.PlanProp("wheelchair_01", -5.2f, -8.8f, 0f, 45f, 0.92f, true, 0.004f);
            b.PlanProp("WetFloorSign_01", -1.1f, -3.8f, 0f, 15f, 1.05f, false, 0.004f);
            b.PlanProp("ceramic_vase_01", 7.0f, -10.3f, 0f, 0f, 1.35f, true, 0.004f);
            b.PlanProp("potted_plant_04", 2.2f, -10.3f, 0f, 0f, 2.8f, false, 0.004f);
            b.PlanProp("potted_plant_04", -5.5f, -3.5f, 0f, 0f, 2.6f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", -4.6f, -3.12f, 2.25f, 180f, 1.7f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", 3.8f, -3.12f, 2.25f, 180f, 1.7f, false, 0.004f);
            b.PlanProp("wall_clock", 5.0f, -3.12f, 3.05f, 180f, 1.5f, false, 0.004f);
            b.PlanProp("fire_alarm", -5.6f, -3.12f, 1.65f, 180f, 1.05f, false, 0.004f);

            // Couloir central : bancs le long des murs pleins, alarme, panneau sol glissant et plantes.
            b.PlanProp("painted_wooden_bench", -12.6f, -2.5f, 0f, 0f, 1.15f, true, 0.004f);
            b.PlanProp("painted_wooden_bench", 7.4f, 1.0f, 0f, 180f, 1.15f, true, 0.004f);
            b.PlanProp("potted_plant_04", -4.6f, 1.0f, 0f, 0f, 2.6f, false, 0.004f);
            b.PlanProp("WetFloorSign_01", 2.8f, -1.8f, 0f, 30f, 1.05f, false, 0.004f);
            b.PlanProp("fire_alarm", -8.0f, 1.38f, 1.65f, 0f, 1.05f, false, 0.004f);

            // Grande croix rouge au-dessus de l'arche du hall vers le couloir.
            b.Box("Croix", new Vector3(-2.6f, 2.4f, -3.12f), new Vector3(0.55f, 1.6f, 0.05f), false);
            b.Box("Croix", new Vector3(-2.6f, 2.92f, -3.12f), new Vector3(1.6f, 0.55f, 0.05f), false);

            // Urgences (Sud-Ouest) : brancard de réanimation, moniteur vital, étagères, armoire médicale, fauteuil roulant.
            b.Box("Metal", new Vector3(-10.0f, 0f, -5.8f), new Vector3(0.85f, 0.85f, 2.15f), true);
            b.Box("Draps", new Vector3(-10.0f, 0.85f, -5.8f), new Vector3(0.75f, 0.12f, 2.0f), false);
            b.Box("Metal", new Vector3(-6.7f, 0f, -5.0f), new Vector3(0.6f, 1.15f, 0.6f), true);
            b.Box("Ecran", new Vector3(-6.7f, 1.25f, -5.0f), new Vector3(0.42f, 0.30f, 0.18f), false);
            b.Wardrobe(-7.2f, -10.4f, 0f, 1.4f, F.North);
            b.PlanProp("steel_frame_shelves_01", -6.5f, -8.5f, 0f, 270f, 1.0f, true, 0.004f);
            b.Washbasin(-13.6f, -4.0f, 0f, F.West);
            b.PlanProp("wheelchair_01", -13.0f, -4.4f, 0f, 145f, 0.92f, true, 0.004f);
            b.PlanProp("medical_box", -10.0f, -5.8f, 0.97f, 10f, 1.05f, false, 0.004f);
            b.PlanProp("medical_tape", -6.7f, -5.0f, 1.15f, 20f, 1.0f, false, 0.004f);

            // Salles de Consultation 1 & 2 (Nord-Ouest) : bureau plat adapté aux objets de soin.
            foreach ((float cx, float sx) in new[] { (-10.2f, -13.6f), (-4.9f, -8.4f) })
            {
                b.Table(cx, 4.2f, 0f, 1.4f, 0.7f, 1);
                b.PlanProp("classic_laptop", cx + 0.25f, 4.2f, 0.74f, 180f, 0.55f, false, 0.004f);
                b.PlanProp("office_notepads", cx - 0.35f, 4.2f, 0.74f, 25f, 0.45f, false, 0.004f);
                b.PlanProp("medical_box", cx, 4.2f, 0.74f, 5f, 0.8f, false, 0.004f);
                b.Washbasin(sx, 6.2f, 0f, F.West);
            }

            // Pharmacie & Laboratoire (Nord-Centre).
            b.PlanProp("steel_frame_shelves_01", -2.8f, 10.4f, 0f, 180f, 1.0f, true, 0.004f);
            b.PlanProp("steel_frame_shelves_01", -1.4f, 10.4f, 0f, 180f, 1.0f, true, 0.004f);
            b.PlanProp("wooden_display_shelves_01", 0.0f, 10.4f, 0f, 180f, 1.0f, true, 0.004f);
            b.Counter(-1.25f, 4.0f, 0f, 2.8f, F.South, false, false);
            b.PlanProp("CashRegister_01", -1.25f, 4.0f, 0.90f, 180f, 0.65f, false, 0.004f);
            b.PlanProp("medical_box", -2.1f, 4.0f, 0.90f, 15f, 1.0f, false, 0.004f);
            b.PlanProp("medical_tape", -0.4f, 4.0f, 0.90f, 0f, 1.0f, false, 0.004f);

            b.Counter(5.1f, 6.2f, 0f, 4.2f, F.West, false, true);
            b.Counter(3.2f, 10.4f, 0f, 2.6f, F.South, false, false);
            b.Fridge(1.6f, 10.3f, 0f, F.South);
            b.PlanProp("classic_laptop", 3.2f, 10.4f, 0.90f, 180f, 0.55f, false, 0.004f);

            // Bureau du médecin-chef & Sanitaires (Sud-Est).
            b.Table(10.8f, -8.8f, 0f, 1.8f, 0.9f, 2);
            b.PlanProp("classic_laptop", 10.8f, -8.8f, 0.74f, 270f, 0.55f, false, 0.004f);
            b.PlanProp("office_notepads", 10.4f, -8.6f, 0.74f, 20f, 0.45f, false, 0.004f);
            b.PlanProp("wooden_bookshelf_worn", 13.6f, -7.2f, 0f, 270f, 0.95f, true, 0.004f);
            b.Box("Metal", new Vector3(12.2f, 0f, -10.4f), new Vector3(1.9f, 0.72f, 0.75f), true);

            b.Toilet(9.2f, -5.8f, 0f);
            b.Toilet(12.4f, -5.8f, 0f);
            b.Washbasin(9.6f, -3.5f, 0f, F.North);
            b.Washbasin(12.2f, -3.5f, 0f, F.North);

            // Plafonniers LED mobiles légers (~28 tris fusionnés au lieu de 17 820 tris chacun !) sous h = 4,8 m.
            foreach ((float lx, float lz, bool alongX) in new[]
            {
                (-1.0f, -7.0f, false), (3.0f, -7.0f, false), (-1.0f, -4.2f, true),
                (-10.0f, -0.7f, true), (-2.0f, -0.7f, true), (6.0f, -0.7f, true), (11.5f, -0.7f, true),
                (-10.0f, -7.0f, true), (-11.4f, 6.2f, false), (-6.1f, 6.2f, false),
                (-1.2f, 6.2f, false), (3.2f, 6.2f, false), (9.8f, 6.2f, true), (10.8f, -8.8f, true),
            })
                b.CeilingLuminaire(lx, lz, h, alongX);

            // Grand couloir & pièces : fenêtres hautes adaptées à la nouvelle hauteur (h = 4,8 m).
            b.Window(new Vector3(-11.4f, 2.0f, 10.92f), F.North, 2.0f, 1.4f);
            b.Window(new Vector3(-6.1f, 2.0f, 10.92f), F.North, 2.0f, 1.4f);
            b.Window(new Vector3(9.8f, 2.0f, 10.92f), F.North, 3.2f, 1.5f);
            b.Window(new Vector3(13.92f, 2.0f, 6.2f), F.East, 2.4f, 1.5f);
            b.Window(new Vector3(13.92f, 2.0f, -8.8f), F.East, 2.0f, 1.4f);
            b.Window(new Vector3(-13.92f, 2.0f, 6.2f), F.West, 2.0f, 1.4f);
            b.Window(new Vector3(-10.0f, 2.0f, -10.92f), F.South, 1.8f, 1.4f);
            b.Window(new Vector3(-3.2f, 2.0f, -10.92f), F.South, 2.2f, 1.5f);
            b.Window(new Vector3(3.6f, 2.0f, -10.92f), F.South, 2.2f, 1.5f);

            foreach (Vector2 light in new[]
            {
                new Vector2(-1f, -7f), new Vector2(3f, -7f), new Vector2(-10f, -0.7f), new Vector2(-2f, -0.7f),
                new Vector2(6f, -0.7f), new Vector2(11.5f, -0.7f), new Vector2(-11.4f, 6.2f), new Vector2(-6.1f, 6.2f),
                new Vector2(-1.2f, 6.2f), new Vector2(3.2f, 6.2f), new Vector2(9.8f, 6.2f), new Vector2(-10f, -7f),
                new Vector2(10.8f, -8.8f), new Vector2(10.8f, -4.8f),
            })
                b.Light(new Vector3(light.x, h, light.y));
            return b;
        }

        /// <summary>Intérieurs des hôpitaux ; renvoie leurs visuels (index = n° de l'hôpital).</summary>
        private static GameObject[] CreateHospitalInteriors(Transform root, List<Mesh> meshes)
        {
            PlanTemplate template = BakePlan(BuildHospitalPlan(), "Hopital", meshes);
            var visuals = new GameObject[HospitalCatalog.Count];
            for (int index = 0; index < HospitalCatalog.Count; index++)
                visuals[index] = PlaceTemplate(root, "SalleDeSoins_" + index, HospitalCatalog.InteriorCenter(index), template);
            Debug.Log("[City] " + HospitalCatalog.Count + " hôpitaux agrandis générés (42 × 33 m, plafond 4,8 m).");
            return visuals;
        }
    }
}
