using System.Collections.Generic;
using UnityEngine;

namespace IvoryState.Editor.City
{
    internal static partial class CityGenerator
    {
        /// <summary>
        /// Monuments d'Abidjan, en volumes simples (interprétations stylisées, pas des maquettes exactes),
        /// chacun dans sa commune. Coordonnées locales au bloc (centre du bloc, dessus du trottoir à
        /// <c>y</c>). Murs et volumes : maillage « peint » teinté par sommet ; colliders en pavés simples.
        /// </summary>
        private static class Landmarks
        {
            private static readonly Color White = new Color(0.94f, 0.93f, 0.90f);
            private static readonly Color Stone = new Color(0.82f, 0.78f, 0.70f);
            private static readonly Color Glass = new Color(0.30f, 0.42f, 0.52f);
            private static readonly Color Dark = new Color(0.12f, 0.12f, 0.13f);

            private static void Forecourt(BoxMeshBuilder ground, float y, Color color, float size = 56f)
            {
                ground.SetColor(color, 1);
                ground.AddFlatQuad(new Vector3(0f, y + 0.01f, 0f), Vector3.right, size, size);
            }

            /// <summary>La Pyramide (Plateau) : immeuble de bureaux en gradins, terrasses à chaque niveau.</summary>
            public static void Pyramide(GameObject block, BoxMeshBuilder facade, BoxMeshBuilder painted, BoxMeshBuilder ground, float y)
            {
                Forecourt(ground, y, new Color(0.80f, 0.77f, 0.70f));
                const int tiers = 10;
                const float tierHeight = 3.4f;
                var center = new Vector3(0f, y, 8f);
                for (int i = 0; i < tiers; i++)
                {
                    float size = Mathf.Lerp(34f, 10f, i / (float)(tiers - 1));
                    var basePosition = center + Vector3.up * (i * tierHeight);
                    facade.SetColor(new Color(0.62f, 0.68f, 0.74f), i * 17);
                    facade.AddBuilding(basePosition, new Vector3(size, tierHeight - 0.3f, size));
                    painted.SetColor(Stone, 2);
                    painted.AddBox(basePosition + Vector3.up * (tierHeight - 0.3f), new Vector3(size + 0.6f, 0.3f, size + 0.6f));
                    AddBoxCollider(block, basePosition + Vector3.up * tierHeight * 0.5f, new Vector3(size, tierHeight, size));
                }
                painted.SetColor(new Color(0.75f, 0.20f, 0.15f), 3);
                painted.AddBox(center + Vector3.up * (tiers * tierHeight), new Vector3(0.3f, 9f, 0.3f));
            }

            /// <summary>
            /// Cathédrale Saint-Paul (Plateau) : nef blanche au toit montant vers l'entrée, grand pylône
            /// incliné au-dessus de la nef, haubans tendus jusqu'au toit, croix au sommet.
            /// </summary>
            public static void Cathedrale(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y)
            {
                Forecourt(ground, y, new Color(0.86f, 0.83f, 0.76f));
                const float halfWidth = 15f, back = 18f, front = -8f, lowRoof = 10f, highRoof = 22f;
                Vector3 bl = new Vector3(-halfWidth, y, back), br = new Vector3(halfWidth, y, back);
                Vector3 fl = new Vector3(-halfWidth, y, front), fr = new Vector3(halfWidth, y, front);
                Vector3 upLow = Vector3.up * lowRoof, upHigh = Vector3.up * highRoof;

                painted.SetColor(White, 4);
                painted.AddFace(br, br + upLow, bl + upLow, bl);                 // mur du fond
                painted.AddFace(bl, bl + upLow, fl + upHigh, fl);                // côté gauche (toit en pente)
                painted.AddFace(fr, fr + upHigh, br + upLow, br);                // côté droit
                painted.AddFace(bl + upLow, br + upLow, fr + upHigh, fl + upHigh); // toit
                painted.AddFace(fl, fl + upHigh, fr + upHigh, fr);               // façade blanche
                // Grandes verrières verticales et portail.
                painted.SetColor(Glass, 5);
                foreach (float x in new[] { -10f, -5f, 5f, 10f })
                    painted.AddBox(new Vector3(x, y + 3f, front - 0.06f), new Vector3(1.6f, 15f, 0.1f));
                painted.SetColor(Dark, 6);
                painted.AddBox(new Vector3(0f, y, front - 0.06f), new Vector3(5f, 5f, 0.1f));
                AddBoxCollider(block, new Vector3(0f, y + 8f, (back + front) * 0.5f), new Vector3(halfWidth * 2f, 16f, back - front));

                // Pylône incliné : du parvis jusqu'au-dessus de la nef.
                Vector3 foot = new Vector3(0f, y, front - 6f);
                Vector3 top = new Vector3(0f, y + 58f, 4f);
                Vector3 axis = top - foot;
                painted.SetColor(White, 7);
                painted.AddRotatedBox((foot + top) * 0.5f, new Vector3(3.2f, axis.magnitude, 4.2f), Quaternion.FromToRotation(Vector3.up, axis));
                AddBoxCollider(block, foot + Vector3.up * 3f, new Vector3(3.5f, 6f, 4.5f));
                // Croix au sommet.
                painted.SetColor(new Color(0.85f, 0.75f, 0.35f), 8);
                painted.AddBox(top, new Vector3(0.6f, 7f, 0.6f));
                painted.AddBox(top + Vector3.up * 4.5f, new Vector3(4f, 0.6f, 0.6f));
                // Haubans jusqu'au toit.
                painted.SetColor(new Color(0.55f, 0.56f, 0.58f), 9);
                Vector3 anchor = top - axis.normalized * 6f;
                foreach (float x in new[] { -13f, -7f, 7f, 13f })
                {
                    foreach (float z in new[] { 14f, 4f })
                    {
                        float roof = Mathf.Lerp(highRoof, lowRoof, (z - front) / (back - front));
                        painted.AddBeam(anchor, new Vector3(x, y + roof + 0.2f, z), 0.12f);
                    }
                }

                AddVendor(block.transform, new Vector3(-6f, y, front - 9f), 0f, s_vendorIndex++, false);
                AddVendor(block.transform, new Vector3(5f, y, front - 11f), 20f, s_vendorIndex++, false);
            }

            /// <summary>Mosquée : salle de prière, grand dôme sur tambour, minaret(s) à balcon, cour avec fontaine.</summary>
            public static void Mosquee(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, bool grand)
            {
                Forecourt(ground, y, new Color(0.88f, 0.86f, 0.80f));
                float hall = grand ? 30f : 18f, wall = grand ? 10f : 6.5f, dome = grand ? 8.5f : 5f;
                var center = new Vector3(0f, y, grand ? 6f : 8f);
                var green = new Color(0.20f, 0.52f, 0.32f);

                painted.SetColor(White, 10);
                painted.AddBox(center, new Vector3(hall, wall, hall));
                AddBoxCollider(block, center + Vector3.up * wall * 0.5f, new Vector3(hall, wall, hall));
                // Frise verte et arcades de la façade (piliers).
                painted.SetColor(green, 11);
                painted.AddBox(center + Vector3.up * (wall - 0.8f), new Vector3(hall + 0.2f, 0.5f, hall + 0.2f));
                painted.SetColor(Dark, 12);
                for (int i = 0; i < (grand ? 7 : 4); i++)
                {
                    float x = -hall * 0.4f + i * (hall * 0.8f / ((grand ? 7 : 4) - 1));
                    painted.AddBox(center + new Vector3(x, 0f, -hall * 0.5f - 0.05f), new Vector3(hall * 0.07f, wall * 0.55f, 0.1f));
                }
                // Tambour et dôme, petits dômes d'angle.
                Vector3 roof = center + Vector3.up * wall;
                painted.SetColor(White, 13);
                painted.AddCylinder(roof, dome, wall * 0.3f, 16);
                painted.SetColor(green, 14);
                painted.AddDome(roof + Vector3.up * wall * 0.3f, dome, dome * 0.9f, 16, 6);
                painted.SetColor(new Color(0.90f, 0.75f, 0.30f), 15);
                painted.AddBox(roof + Vector3.up * (wall * 0.3f + dome * 0.9f), new Vector3(0.3f, 2f, 0.3f));
                if (grand)
                {
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            painted.SetColor(green, 16);
                            painted.AddDome(roof + new Vector3(sx * hall * 0.36f, 0f, sz * hall * 0.36f), 2.5f, 2.2f, 10, 4);
                        }
                    }
                }

                // Minarets.
                float[] xs = grand ? new[] { -hall * 0.5f - 3f, hall * 0.5f + 3f } : new[] { hall * 0.5f + 2.5f };
                float height = grand ? 38f : 22f;
                foreach (float x in xs)
                {
                    var foot = new Vector3(x, y, center.z - hall * 0.5f + 1f);
                    painted.SetColor(White, 17);
                    painted.AddCylinder(foot, 1.5f, height, 8);
                    AddBoxCollider(block, foot + Vector3.up * height * 0.5f, new Vector3(3f, height, 3f));
                    painted.SetColor(green, 18);
                    painted.AddCylinder(foot + Vector3.up * (height - 6f), 2.3f, 0.6f, 10);
                    painted.SetColor(White, 19);
                    painted.AddCylinder(foot + Vector3.up * height, 1.1f, 3.5f, 8);
                    painted.SetColor(green, 20);
                    painted.AddDome(foot + Vector3.up * (height + 3.5f), 1.25f, 2f, 8, 3);
                }

                // Cour : fontaine aux ablutions.
                var fountain = new Vector3(0f, y, center.z - hall * 0.5f - 8f);
                painted.SetColor(Stone, 21);
                painted.AddCylinder(fountain, 2.6f, 0.7f, 12);
                painted.SetColor(new Color(0.30f, 0.55f, 0.75f), 22);
                painted.AddCylinder(fountain + Vector3.up * 0.05f, 2.2f, 0.67f, 12);
                AddBoxCollider(block, fountain + Vector3.up * 0.35f, new Vector3(5f, 0.7f, 5f));
                AddVendor(block.transform, fountain + new Vector3(3.5f, 0f, -1f), 270f, s_vendorIndex++, false);
            }

            /// <summary>Église de quartier : nef à toit à deux pentes, clocher, croix, parvis.</summary>
            public static void Eglise(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y)
            {
                Forecourt(ground, y, new Color(0.84f, 0.80f, 0.72f));
                const float width = 11f, length = 22f, wall = 7f, ridge = 4.5f;
                var center = new Vector3(0f, y, 6f);
                var cream = new Color(0.95f, 0.90f, 0.78f);
                var roofColor = new Color(0.55f, 0.22f, 0.15f);
                painted.SetColor(cream, 23);
                painted.AddBox(center, new Vector3(width, wall, length));
                AddBoxCollider(block, center + Vector3.up * (wall + ridge) * 0.5f, new Vector3(width, wall + ridge, length));

                // Toit à deux pentes et pignons.
                float h = wall, z0 = center.z - length * 0.5f, z1 = center.z + length * 0.5f, hw = width * 0.5f + 0.4f;
                Vector3 ridgeFront = new Vector3(0f, y + h + ridge, z0 - 0.4f), ridgeBack = new Vector3(0f, y + h + ridge, z1 + 0.4f);
                painted.SetColor(roofColor, 24);
                painted.AddFace(new Vector3(-hw, y + h, z0 - 0.4f), ridgeFront, ridgeBack, new Vector3(-hw, y + h, z1 + 0.4f));
                painted.AddFace(new Vector3(hw, y + h, z1 + 0.4f), ridgeBack, ridgeFront, new Vector3(hw, y + h, z0 - 0.4f));
                painted.SetColor(cream, 25);
                painted.AddTriangle(new Vector3(hw - 0.4f, y + h, z0), ridgeFront + Vector3.forward * 0.4f, new Vector3(-hw + 0.4f, y + h, z0));
                painted.AddTriangle(new Vector3(-hw + 0.4f, y + h, z1), ridgeBack - Vector3.forward * 0.4f, new Vector3(hw - 0.4f, y + h, z1));
                painted.SetColor(Dark, 26);
                painted.AddBox(new Vector3(0f, y, z0 - 0.05f), new Vector3(2.4f, 3.6f, 0.1f));

                // Clocher.
                var tower = new Vector3(0f, y, z0 - 2.5f);
                painted.SetColor(cream, 27);
                painted.AddBox(tower, new Vector3(4f, 16f, 4f));
                AddBoxCollider(block, tower + Vector3.up * 8f, new Vector3(4f, 16f, 4f));
                painted.SetColor(Dark, 28);
                painted.AddBox(tower + Vector3.up * 12f + Vector3.back * 2.01f, new Vector3(1.4f, 2f, 0.05f));
                painted.SetColor(roofColor, 29);
                painted.AddCone(tower + Vector3.up * 16f, 3f, 0f, 5f, 4);
                painted.SetColor(new Color(0.85f, 0.75f, 0.35f), 30);
                painted.AddBox(tower + Vector3.up * 21f, new Vector3(0.25f, 2.5f, 0.25f));
                painted.AddBox(tower + Vector3.up * 22.6f, new Vector3(1.4f, 0.25f, 0.25f));

                AddVendor(block.transform, new Vector3(-4f, y, z0 - 8f), 10f, s_vendorIndex++, false);
                AddVendor(block.transform, new Vector3(3f, y, z0 - 10f), 340f, s_vendorIndex++, false);
            }

            /// <summary>Stade Félix Houphouët-Boigny : pelouse, tribunes en gradins autour, pylônes d'éclairage.</summary>
            public static void Stade(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y)
            {
                Forecourt(ground, y, new Color(0.75f, 0.72f, 0.66f));
                ground.SetColor(new Color(0.25f, 0.52f, 0.22f), 2);
                ground.AddFlatQuad(new Vector3(0f, y + 0.02f, 0f), Vector3.right, 32f, 22f);
                ground.SetColor(Color.white, 3);
                AddLine(ground, new Vector3(0f, y + 0.03f, -11f), new Vector3(0f, y + 0.03f, 11f), 0.12f);
                AddCircle(ground, new Vector3(0f, y + 0.03f, 0f), 3.5f, 0.12f, 20);

                const int segments = 24;
                const float rx = 21f, rz = 16f;
                Color[] seats = { new Color(0.95f, 0.55f, 0.10f), Color.white, new Color(0.15f, 0.55f, 0.30f) }; // orange, blanc, vert
                for (int i = 0; i < segments; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                    // Entrée au sud : deux segments laissés ouverts.
                    if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 270f)) < 16f)
                        continue;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float yaw = -a * Mathf.Rad2Deg + 90f;
                    float segment = 2f * Mathf.PI * Mathf.Sqrt((rx * rx + rz * rz) * 0.5f) / segments + 0.6f;
                    for (int tier = 0; tier < 3; tier++)
                    {
                        float r = 1f + tier * 2.4f;
                        var p = new Vector3(dir.x * (rx + r), y, dir.z * (rz + r));
                        float h = 1.6f + tier * 2.2f;
                        painted.SetColor(new Color(0.70f, 0.70f, 0.68f), 4);
                        painted.AddOrientedBox(p, new Vector3(segment, h, 2.5f), yaw);
                        painted.SetColor(seats[tier], 5);
                        painted.AddOrientedBox(p + Vector3.up * h, new Vector3(segment - 0.2f, 0.12f, 2.3f), yaw);
                    }
                    var outer = new Vector3(dir.x * (rx + 5.8f), y, dir.z * (rz + 5.8f));
                    AddBoxCollider(block, outer + Vector3.up * 3f, new Vector3(4f, 6f, 4f));
                }

                // Pylônes d'éclairage aux quatre coins.
                foreach (float sx in new[] { -1f, 1f })
                {
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        var foot = new Vector3(sx * 26f, y, sz * 24f);
                        painted.SetColor(new Color(0.55f, 0.56f, 0.58f), 6);
                        painted.AddBox(foot, new Vector3(0.7f, 26f, 0.7f));
                        AddBoxCollider(block, foot + Vector3.up * 13f, new Vector3(0.7f, 26f, 0.7f));
                        painted.SetColor(new Color(0.95f, 0.95f, 0.85f), 7);
                        painted.AddBox(foot + Vector3.up * 26f, new Vector3(3.2f, 2f, 0.5f));
                    }
                }
            }

            /// <summary>
            /// Hôtel Ivoire (Cocody, au bord de la lagune) : haute tour blanche, aile basse, piscine,
            /// cocotiers, enseigne rouge au sommet.
            /// </summary>
            public static void HotelIvoire(GameObject block, BoxMeshBuilder facade, BoxMeshBuilder painted, BoxMeshBuilder ground, float y, List<Mesh> meshes)
            {
                Forecourt(ground, y, new Color(0.88f, 0.86f, 0.80f));
                var tower = new Vector3(0f, y, 14f);
                facade.SetColor(new Color(0.96f, 0.95f, 0.92f), 31);
                facade.AddBuilding(tower, new Vector3(34f, 72f, 11f));
                AddBoxCollider(block, tower + Vector3.up * 36f, new Vector3(34f, 72f, 11f));
                painted.SetColor(White, 32);
                painted.AddBox(tower + Vector3.up * 72f, new Vector3(30f, 4f, 9f));
                painted.SetColor(new Color(0.80f, 0.12f, 0.12f), 33);
                painted.AddBox(tower + new Vector3(0f, 73f, -4.6f), new Vector3(16f, 2.2f, 0.3f));

                var wing = new Vector3(0f, y, -2f);
                facade.SetColor(new Color(0.93f, 0.92f, 0.88f), 34);
                facade.AddBuilding(wing, new Vector3(52f, 9f, 8f));
                AddBoxCollider(block, wing + Vector3.up * 4.5f, new Vector3(52f, 9f, 8f));

                // Piscine côté lagune et terrasse.
                ground.SetColor(new Color(0.92f, 0.90f, 0.84f), 35);
                ground.AddFlatQuad(new Vector3(0f, y + 0.02f, -17f), Vector3.right, 30f, 12f);
                var pool = new BoxMeshBuilder();
                pool.AddFlatQuad(new Vector3(0f, y + 0.06f, -17f), Vector3.right, 22f, 7f);
                AddMeshObject(block.transform, "Piscine", pool, CityMaterials.Water(), meshes);
                AddBoxCollider(block, new Vector3(0f, y + 0.5f, -17f), new Vector3(22f, 1f, 7f));
                foreach (float x in new[] { -22f, -14f, 14f, 22f })
                    PalmBuilder.Place(block.transform, new Vector3(x, y, -22f), s_decor.Next(PalmBuilder.VariantCount), s_decor.Next(360));
                AddVendor(block.transform, new Vector3(-12f, y, -11.5f), 180f, s_vendorIndex++, false);
            }

            /// <summary>
            /// Palais de la Culture (Treichville, face à la lagune) : long volume vitré coiffé de grandes
            /// toitures inclinées en vagues, esplanade et drapeaux ivoiriens.
            /// </summary>
            public static void PalaisCulture(GameObject block, BoxMeshBuilder painted, BoxMeshBuilder ground, float y)
            {
                Forecourt(ground, y, new Color(0.84f, 0.80f, 0.72f));
                var body = new Vector3(0f, y, -2f);
                painted.SetColor(new Color(0.86f, 0.82f, 0.74f), 36);
                painted.AddBox(body, new Vector3(46f, 9f, 26f));
                AddBoxCollider(block, body + Vector3.up * 6f, new Vector3(46f, 12f, 26f));
                painted.SetColor(Glass, 37);
                painted.AddBox(body + new Vector3(0f, 1f, 13.05f), new Vector3(40f, 7f, 0.1f));   // façade vitrée côté lagune

                // Toitures en vagues : cinq coques inclinées, alternées.
                for (int i = 0; i < 5; i++)
                {
                    float x = -18.4f + i * 9.2f;
                    float tilt = i % 2 == 0 ? 14f : -14f;
                    painted.SetColor(i % 2 == 0 ? new Color(0.92f, 0.90f, 0.86f) : new Color(0.80f, 0.60f, 0.40f), 38 + i);
                    painted.AddRotatedBox(body + new Vector3(x, 11f, 0f), new Vector3(9.6f, 0.5f, 28f), Quaternion.Euler(0f, 0f, tilt));
                }

                // Drapeaux de la Côte d'Ivoire sur l'esplanade (orange, blanc, vert).
                Color[] flag = { new Color(0.97f, 0.50f, 0.05f), Color.white, new Color(0.0f, 0.62f, 0.38f) };
                foreach (float x in new[] { -10f, 0f, 10f })
                {
                    var pole = new Vector3(x, y, 20f);
                    painted.SetColor(new Color(0.80f, 0.80f, 0.80f), 45);
                    painted.AddBox(pole, new Vector3(0.15f, 10f, 0.15f));
                    AddBoxCollider(block, pole + Vector3.up * 5f, new Vector3(0.2f, 10f, 0.2f));
                    for (int k = 0; k < 3; k++)
                    {
                        painted.SetColor(flag[k], 46 + k);
                        painted.AddBox(pole + new Vector3(0.1f + 0.5f + k * 1f, 8.2f, 0f), new Vector3(1f, 1.6f, 0.04f));
                    }
                }
                AddVendor(block.transform, new Vector3(6f, y, 16f), 180f, s_vendorIndex++, false);
                AddVendor(block.transform, new Vector3(-7f, y, 17f), 160f, s_vendorIndex++, false);
            }
        }
    }
}
