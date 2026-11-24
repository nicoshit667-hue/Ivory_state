using System.Collections.Generic;
using IvoryState.Gameplay.Homes;
using UnityEngine;
using F = IvoryState.Editor.City.PlanBuilder.Facing;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Intérieurs construits d'après les plans d'aménagement fournis : studio (6 × 4,5), F2 (8 × 6),
    /// F3 (12 × 8) et villas duplex 3 et 4 chambres (terrain 16 × 21, maison 12 × 9 sur deux niveaux,
    /// escalier, cour et portail ; piscine, terrasse et jardin pour la villa de luxe). Repère local : x vers
    /// l'est, z vers le nord, sol à y = 0, centre de l'emprise à l'origine. Les portes d'entrée correspondent
    /// à PropertyCatalog.InteriorDoor (le serveur y fait apparaître et sortir les joueurs).
    /// </summary>
    internal static partial class CityGenerator
    {
        private sealed class PlanTemplate
        {
            public readonly List<(string name, Mesh mesh, Material material)> Parts = new List<(string, Mesh, Material)>();
            public List<(Vector3 center, Vector3 size, Quaternion rotation)> Colliders;
            public List<Vector3> Lights;
            public List<(string id, Vector3 position, float yaw, float scale, bool solid, float cull)> Props;
        }

        private static PlanBuilder NewPlan(bool luxe, bool hospital = false)
        {
            var b = new PlanBuilder(PropertyCatalog.PlanScale);
            b.Register("Murs", IntWall(luxe ? new Color(1f, 0.98f, 0.94f) : new Color(0.98f, 0.90f, 0.74f)));
            b.Register("Sol", luxe ? IntMarble() : IntFloorTiles());
            b.Register("Parquet", IntParquet());
            b.Register("Plafond", GetOrCreateMaterial("M_StudioCeiling", new Color(0.96f, 0.96f, 0.93f)));
            b.Register("Bois", IntWood());
            b.Register("Draps", IntSheets());
            b.Register("Pagne", IntPagne());
            b.Register("Metal", GetOrCreateMaterial("M_StudioMetal", new Color(0.85f, 0.86f, 0.88f)));
            b.Register("Sombre", GetOrCreateMaterial("M_StudioDark", new Color(0.07f, 0.07f, 0.08f)));
            b.Register("CadreFenetre", CityMaterials.Plain("M_WindowFrame", new Color(0.22f, 0.16f, 0.12f), 0.35f));
            b.Register("Faience", IntShowerTiles());
            b.Register("Sanitaire", GetOrCreateMaterial("M_Sanitary", new Color(0.97f, 0.97f, 0.98f)));
            b.Register("Lumiere", Emissive("M_StudioWindow", new Color(0.75f, 0.88f, 1f), new Color(0.55f, 0.70f, 0.85f)));
            b.Register("Sortie", HomeRingMaterial());
            b.Register("Gazon", IntGrass());
            b.Register("Terrasse", IntTerraceTiles());
            b.Register("Tuiles", IntRoofTiles());
            b.Register("Brique", IntBrick());
            b.Register("Beton", CityMaterials.Lit("M_Int_Yard", "concrete_pavement_02", 0.4f, 0.2f, new Color(0.85f, 0.83f, 0.80f), true, 2));
            b.Register("Feuillage", GetOrCreateMaterial("M_GardenLeaves", new Color(0.18f, 0.42f, 0.16f)));
            b.Register("Eau", Emissive("M_PoolWater", new Color(0.20f, 0.65f, 0.85f), new Color(0.05f, 0.25f, 0.35f)));
            return b;
        }

        /// <summary>Porte d'entrée : battant plein avec relief, poignée levier, collider aligné sur le nouveau gabarit (1,35 × 2,55 m), cercle de sortie.</summary>
        private static void EntranceDoor(PlanBuilder b, Vector3 door, Vector3 inward)
        {
            bool alongZ = Mathf.Abs(inward.x) > 0.5f;
            float dw = PlanBuilder.DoorWidth - 0.04f;
            float dh = PlanBuilder.DoorHeight;
            Vector3 panel = alongZ ? new Vector3(0.06f, dh - 0.02f, dw) : new Vector3(dw, dh - 0.02f, 0.06f);
            Vector3 doorPos = door - inward * 0.02f;
            b.WorldBox("Bois", doorPos, panel, false);
            // Montant décoratif central + barre de tirage métallique.
            Vector3 trim = alongZ ? new Vector3(0.08f, dh - 0.35f, dw * 0.65f) : new Vector3(dw * 0.65f, dh - 0.35f, 0.08f);
            b.WorldBox("CadreFenetre", doorPos + new Vector3(0f, 0.16f, 0f), trim, false);
            Vector3 handleOff = alongZ ? new Vector3(0.05f, 1.02f, dw * 0.34f) : new Vector3(dw * 0.34f, 1.02f, 0.05f);
            b.WorldBox("Metal", doorPos + handleOff, new Vector3(0.04f, 0.34f, 0.04f), false);
            b.WorldSolid(door + new Vector3(0f, dh * 0.5f, 0f),
                alongZ ? new Vector3(0.22f, dh, PlanBuilder.DoorWidth + 0.06f) : new Vector3(PlanBuilder.DoorWidth + 0.06f, dh, 0.22f));
            AddCircle(b.P("Sortie"), door + inward * 0.80f + new Vector3(0f, 0.012f, 0f), StudioLayout.ExitRadius, 0.08f, 28);
        }

        private static void Shell(PlanBuilder b, float hw, float hd, float h, Vector3 door)
        {
            b.Slab("Sol", -hw, -hd, hw, hd, 0f);
            b.Slab("Plafond", -hw - 0.1f, -hd - 0.1f, hw + 0.1f, hd + 0.1f, h + 0.2f);
            b.Wall("Murs", -hw, hd, hw, hd, 0f, h);
            b.Wall("Murs", -hw, -hd, hw, -hd, 0f, h);
            b.Wall("Murs", hw, -hd, hw, hd, 0f, h);
            b.Wall("Murs", -hw, -hd, -hw, hd, 0f, h, door.z / b.Scale + hd);
        }

        // --- Studio (F1) : 6,00 × 4,50 ---
        private static PlanBuilder BuildStudioPlan()
        {
            const float h = 3.4f;
            PlanBuilder b = NewPlan(false);
            (Vector3 door, Vector3 inward) = PropertyCatalog.InteriorDoor(PropertyKind.Studio);
            Shell(b, 3f, 2.25f, h, door);
            EntranceDoor(b, door, inward);

            // Salle d'eau fermée au nord-est (WC, lavabo + miroir, douche).
            b.Wall("Murs", 1f, 0.45f, 3f, 0.45f, 0f, h, 0.55f);
            b.Wall("Murs", 1f, 0.45f, 1f, 2.25f, 0f, h);
            b.Toilet(1.45f, 1.85f, 0f);
            b.Washbasin(2.05f, 2.0f, 0f, F.North);
            b.Shower(2.5f, 1.0f, 0f);

            // Coin cuisine le long du mur nord : évier, cuisinière 3D, micro-ondes 3D, frigo, bouteille de gaz.
            b.Counter(-1.5f, 1.93f, 0f, 2.4f, F.South, true, true);
            b.Fridge(0.45f, 1.9f, 0f, F.South);
            b.PlanProp("small_lpg_tank", -2.7f, 1.95f, 0f, 0f, 0.85f, true, 0.004f);

            // Lit deux places 3D + chevet (tête à l'est), armoire au sud-est.
            b.Bed(2.0f, -0.45f, 0f, F.East, false);
            b.Wardrobe(1.85f, -1.92f, 0f, 1.6f, F.North);

            // Coin repas 3D, console & TV 3D à l'ouest, canapé 3D face à la télé, table basse 3D, tapis.
            b.Table(0.4f, -0.4f, 0f, 0.85f, 0.85f, 2);
            b.Tv(-2.72f, -1.1f, 0f, F.East, false);
            b.Sofa(-1.2f, -1.1f, 0f, 1.7f, F.West);
            b.CoffeeTable(-2.0f, -1.1f, 0f, true);
            b.Rug(-1.9f, -1.1f, 0f, 1.7f, 1.7f);

            // Détails : ventilateur au plafond, plante verte, tableau, horloge murale.
            b.AreaProp("ceiling_fan", -0.8f, -0.4f, h, 0f, 1.0f, false, 0.004f);
            b.PlanProp("potted_plant_04", -2.75f, -2.0f, 0f, 0f, 2.3f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", 2.91f, -0.45f, 1.85f, 270f, 1.45f, false, 0.004f);
            b.PlanProp("wall_clock", -1.5f, 2.16f, 2.25f, 180f, 1.25f, false, 0.004f);
            b.PlanProp("boombox", -2.65f, -1.75f, 0.78f, 90f, 0.6f, false, 0.004f);

            b.Window(new Vector3(-0.6f, 1.4f, -2.17f), F.South, 1.4f);
            b.Window(new Vector3(2.1f, 1.4f, -2.17f), F.South, 0.9f);
            b.Light(new Vector3(-1.0f, h, 0f));
            b.Light(new Vector3(2.0f, h, 1.35f));
            return b;
        }

        // --- Appartement 2 pièces (F2) : 8,00 × 6,00 ---
        private static PlanBuilder BuildF2Plan()
        {
            const float h = 3.4f;
            PlanBuilder b = NewPlan(false);
            (Vector3 door, Vector3 inward) = PropertyCatalog.InteriorDoor(PropertyKind.OneBedroom);
            Shell(b, 4f, 3f, h, door);
            EntranceDoor(b, door, inward);

            // Toilettes visiteurs (nord-ouest).
            b.Wall("Murs", -4f, 1.4f, -2.6f, 1.4f, 0f, h, 0.7f);
            b.Wall("Murs", -2.6f, 1.4f, -2.6f, 3f, 0f, h);
            b.Toilet(-3.3f, 2.45f, 0f);
            // Cuisine ouverte (nord), magasin avec étagères 3D.
            b.Counter(-1.3f, 2.68f, 0f, 2.2f, F.South, true, true);
            b.Fridge(0.05f, 2.62f, 0f, F.South);
            b.PlanProp("small_lpg_tank", -2.35f, 2.72f, 0f, 0f, 0.85f, true, 0.004f);
            b.Wall("Murs", 0.4f, 1.4f, 0.4f, 3f, 0f, h);
            b.Wall("Murs", 0.4f, 1.4f, 1.6f, 1.4f, 0f, h, 0.6f);
            b.PlanProp("steel_frame_shelves_01", 1.0f, 2.74f, 0f, 0f, 0.92f, true, 0.004f);

            // Dégagement et chambre (est).
            b.Wall("Murs", 0.4f, -0.6f, 0.4f, 1.4f, 0f, h, 1.0f);
            b.Wall("Murs", 1.6f, -0.6f, 1.6f, 3f, 0f, h, 1.1f);
            b.Bed(3.05f, 1.9f, 0f, F.North);
            b.Wardrobe(1.95f, 2.3f, 0f, 1.2f, F.East);
            b.AreaProp("ceiling_fan", 2.8f, 1.2f, h, 0f, 0.9f, false, 0.004f);

            // Salle d'eau (sud du dégagement).
            b.Wall("Murs", 0.4f, -0.6f, 2.4f, -0.6f, 0f, h, 0.6f);
            b.Wall("Murs", 0.4f, -3f, 0.4f, -0.6f, 0f, h);
            b.Wall("Murs", 2.4f, -3f, 2.4f, -0.6f, 0f, h);
            b.Shower(0.95f, -2.45f, 0f);
            b.Toilet(1.9f, -2.5f, 0f);
            b.Washbasin(1.9f, -0.95f, 0f, F.North);

            // Salon et salle à manger (sud-ouest) avec canapé + fauteuil 3D + bibliothèque.
            b.Tv(-3.72f, -1.0f, 0f, F.East, true);
            b.Sofa(-2.0f, -1.0f, 0f, 2.0f, F.West, modern: true);
            b.PlanProp("ArmChair_01", -2.9f, -2.2f, 0f, 160f, 0.95f, true, 0.004f);
            b.CoffeeTable(-2.9f, -1.0f, 0f, true);
            b.Rug(-2.9f, -1.0f, 0f, 1.9f, 2.0f);
            b.Table(-0.85f, -2.1f, 0f, 1.35f, 0.85f, 4);
            b.PlanProp("wooden_bookshelf_worn", -0.2f, -0.8f, 0f, 270f, 0.90f, true, 0.004f);

            b.AreaProp("ceiling_fan", -2.4f, -1.2f, h, 0f, 1.0f, false, 0.004f);
            b.PlanProp("potted_plant_04", -3.72f, -2.72f, 0f, 0f, 2.6f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", -2.9f, -2.91f, 1.8f, 0f, 1.5f, false, 0.004f);
            b.PlanProp("wall_clock", -1.3f, 2.91f, 2.25f, 180f, 1.25f, false, 0.004f);

            b.Window(new Vector3(-1.8f, 1.4f, -2.92f), F.South, 1.8f);
            b.Window(new Vector3(3.92f, 1.4f, 1.2f), F.East, 1.0f);
            b.Window(new Vector3(-1.2f, 1.6f, 2.92f), F.North, 0.9f);
            b.Light(new Vector3(-2.0f, h, -0.8f));
            b.Light(new Vector3(-1.2f, h, 2.2f));
            b.Light(new Vector3(2.8f, h, 1.2f));
            b.Light(new Vector3(1.4f, h, -1.8f));
            return b;
        }

        // --- Appartement 3 pièces (F3) : 12,00 × 8,00 ---
        private static PlanBuilder BuildF3Plan()
        {
            const float h = 3.5f;
            PlanBuilder b = NewPlan(false);
            (Vector3 door, Vector3 inward) = PropertyCatalog.InteriorDoor(PropertyKind.TwoBedroom);
            Shell(b, 6f, 4f, h, door);
            EntranceDoor(b, door, inward);

            // Rangée nord : toilettes visiteurs, cuisine équipée avec îlot central, cellier, salle d'eau.
            b.Wall("Murs", -6f, 1.6f, -4.8f, 1.6f, 0f, h, 0.6f);
            b.Wall("Murs", -4.8f, 1.6f, -4.8f, 4f, 0f, h);
            b.Toilet(-5.4f, 3.1f, 0f);
            b.Counter(-2.7f, 3.68f, 0f, 3.4f, F.South, true, true);
            b.Fridge(-0.95f, 3.62f, 0f, F.South);
            b.PlanProp("small_lpg_tank", -4.45f, 3.72f, 0f, 0f, 0.85f, true, 0.004f);
            // Îlot central avec plateau métal et plante.
            b.Box("Bois", new Vector3(-2.7f, 0f, 2.1f), new Vector3(1.7f, 0.88f, 0.75f), true);
            b.Box("Metal", new Vector3(-2.7f, 0.88f, 2.1f), new Vector3(1.78f, 0.05f, 0.82f), false);
            b.PlanProp("potted_plant_04", -2.2f, 2.1f, 0.93f, 0f, 1.3f, false, 0.004f);
            b.Wall("Murs", -0.6f, 1.6f, -0.6f, 4f, 0f, h);
            b.Wall("Murs", 0.6f, 1.6f, 0.6f, 4f, 0f, h);
            b.Wall("Murs", -0.6f, 1.6f, 3f, 1.6f, 0f, h, 0.6f, 2.1f);
            b.PlanProp("steel_frame_shelves_01", 0f, 3.72f, 0f, 0f, 0.95f, true, 0.004f);
            b.Shower(2.4f, 3.45f, 0f);
            b.Toilet(1.2f, 3.5f, 0f);
            b.Washbasin(1.9f, 2.0f, 0f, F.South);

            // Chambre parentale (nord-est).
            b.Wall("Murs", 3f, -0.4f, 3f, 4f, 0f, h, 1.0f);
            b.Bed(4.95f, 2.4f, 0f, F.East);
            b.Wardrobe(3.45f, 3.4f, 0f, 1.0f, F.East);
            b.Wardrobe(4.6f, 0.0f, 0f, 1.6f, F.North);
            b.AreaProp("ceiling_fan", 4.5f, 1.8f, h, 0f, 1.0f, false, 0.004f);

            // Chambre 1 (sud-est).
            b.Wall("Murs", 0f, -0.4f, 6f, -0.4f, 0f, h, 0.7f);
            b.Wall("Murs", 0f, -4f, 0f, -0.4f, 0f, h);
            b.Bed(2.0f, -2.95f, 0f, F.South);
            b.Wardrobe(4.6f, -0.75f, 0f, 1.4f, F.South);
            b.PlanProp("painted_wooden_cabinet", 4.6f, -3.55f, 0f, 180f, 0.95f, true, 0.004f);
            b.AreaProp("ceiling_fan", 2.5f, -2.2f, h, 0f, 1.0f, false, 0.004f);

            // Salon et salle à manger (sud-ouest) richement meublés.
            b.Tv(-5.70f, -1.2f, 0f, F.East, true);
            b.Sofa(-3.7f, -1.2f, 0f, 2.2f, F.West, modern: true);
            b.PlanProp("ArmChair_01", -4.7f, -3.2f, 0f, 0f, 1.0f, true, 0.004f);
            b.CoffeeTable(-4.7f, -1.2f, 0f, true);
            b.Rug(-4.7f, -1.5f, 0f, 2.2f, 2.6f);
            b.Table(-1.55f, -2.5f, 0f, 1.8f, 1.0f, 6);
            b.PlanProp("wooden_bookshelf_worn", -0.35f, -2.4f, 0f, 270f, 0.95f, true, 0.004f);
            b.PlanProp("planter_box_01", -3.0f, -3.65f, 0f, 0f, 1.0f, false, 0.004f);
            b.PlanProp("calathea_orbifolia_01", -3.0f, -3.65f, 0.40f, 0f, 0.25f, false, 0.004f);
            b.PlanProp("potted_plant_04", -5.65f, -3.65f, 0f, 0f, 2.6f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", -1.6f, -3.91f, 1.85f, 0f, 1.6f, false, 0.004f);
            b.PlanProp("wall_clock", -2.7f, 3.91f, 2.25f, 180f, 1.3f, false, 0.004f);
            b.AreaProp("ceiling_fan", -3.2f, -1.4f, h, 0f, 1.1f, false, 0.004f);

            b.Window(new Vector3(-3f, 1.4f, -3.92f), F.South, 2.4f);
            b.Window(new Vector3(2f, 1.4f, -3.92f), F.South, 1.2f);
            b.Window(new Vector3(5.92f, 1.4f, 1.6f), F.East, 1.0f);
            b.Light(new Vector3(-3f, h, -1.2f));
            b.Light(new Vector3(-2.7f, h, 2.6f));
            b.Light(new Vector3(4.5f, h, 1.8f));
            b.Light(new Vector3(2.5f, h, -2.2f));
            b.Light(new Vector3(1.5f, h, 0.6f));
            return b;
        }

        // --- Villas duplex : terrain 16 × 21, maison 12 × 9 (z 0…9), étage à 3,8 m ---
        private static PlanBuilder BuildVillaPlan(PropertyKind kind)
        {
            bool luxe = kind == PropertyKind.House4Luxe;
            bool four = kind != PropertyKind.House3;
            const float h = 3.6f, up = 3.8f;
            PlanBuilder b = NewPlan(luxe);
            (Vector3 door, Vector3 inward) = PropertyCatalog.InteriorDoor(kind);

            // Terrain : cour pavée, clôture maçonnée à piliers de briques, portail en fer forgé au sud.
            b.Slab("Beton", -8f, -11f, 8f, 10f, 0f);
            b.Wall("Murs", -8f, 10f, 8f, 10f, 0f, 2.6f);
            b.Wall("Murs", -8f, -11f, -8f, 10f, 0f, 2.6f);
            b.Wall("Murs", 8f, -11f, 8f, 10f, 0f, 2.6f);
            b.Wall("Murs", -8f, -11f, -1.6f, -11f, 0f, 2.6f);
            b.Wall("Murs", 1.6f, -11f, 8f, -11f, 0f, 2.6f);
            // Piliers de brique au portail et aux angles de la cour.
            foreach (float px in new[] { -1.65f, 1.65f, -7.85f, 7.85f })
                b.Area("Brique", new Vector3(px, 0f, -10.95f), new Vector3(0.45f, 2.9f, 0.45f), false);
            // Portail ajouré haut (2,55 m) : cadre et barreaux métalliques + collider.
            float gateZ = -11.05f * b.Scale;
            b.WorldBox("Sombre", new Vector3(0f, 0.05f, gateZ), new Vector3(3.2f * b.Scale, 0.25f, 0.08f), false);
            b.WorldBox("Sombre", new Vector3(0f, 2.52f, gateZ), new Vector3(3.2f * b.Scale, 0.12f, 0.08f), false);
            for (int bar = -8; bar <= 8; bar++)
                b.WorldBox("Metal", new Vector3(bar * 0.26f, 0.05f, gateZ), new Vector3(0.05f, 2.55f, 0.05f), false);
            b.AreaSolid(new Vector3(0f, 1.3f, -11f), new Vector3(3.3f, 2.6f, 0.3f));
            AddCircle(b.P("Sortie"), door + inward * 0.75f + new Vector3(0f, 0.012f, 0f), StudioLayout.ExitRadius, 0.08f, 28);

            // Parking (ouest) + groupe électrogène.
            b.Area("Sanitaire", new Vector3(-7.5f, 0f, -5.5f), new Vector3(0.1f, 0.01f, 5f));
            b.Area("Sanitaire", new Vector3(-4.5f, 0f, -5.5f), new Vector3(0.1f, 0.01f, 5f));
            b.AreaProp("portable_generator", -7.1f, 2.0f, 0f, 90f, 1.0f, true, 0.005f);

            // Véranda / porche d'entrée couvert devant la haute porte (sud de la maison, x = 0,4…3,0).
            b.Area("Terrasse", new Vector3(1.8f, 0.01f, -1.0f), new Vector3(2.8f, 0.04f, 2.0f), false);
            foreach (float cx in new[] { 0.6f, 3.0f })
            {
                b.Area("Brique", new Vector3(cx, 0f, -1.85f), new Vector3(0.28f, 0.85f, 0.28f), true);
                b.Area("Murs", new Vector3(cx, 0.85f, -1.85f), new Vector3(0.22f, 2.4f, 0.22f), false);
            }
            b.P("Tuiles").AddRotatedBox(b.Sc(new Vector3(1.8f, 3.38f, -1.0f)),
                new Vector3(3.2f * b.Scale, 0.14f, 2.4f * b.Scale), Quaternion.Euler(14f, 0f, 0f));

            if (luxe)
            {
                b.Area("Sanitaire", new Vector3(-1.5f, 0f, -5.5f), new Vector3(0.1f, 0.01f, 5f));
                // Jardin, piscine carrelée, terrasse extérieure avec salon de jardin et vrais transats.
                b.Area("Gazon", new Vector3(4.5f, 0f, -5.5f), new Vector3(6.8f, 0.02f, 10.8f));
                b.Area("Terrasse", new Vector3(4.5f, 0.01f, -0.2f), new Vector3(6.8f, 0.04f, 2.6f));
                b.Area("Eau", new Vector3(4.6f, 0f, -4.7f), new Vector3(4.2f, 0.04f, 6.8f));
                foreach ((float x, float z, float w, float d) in new[]
                         { (4.6f, -1.15f, 4.8f, 0.3f), (4.6f, -8.25f, 4.8f, 0.3f), (2.35f, -4.7f, 0.3f, 7.4f), (6.85f, -4.7f, 0.3f, 7.4f) })
                    b.Area("Faience", new Vector3(x, 0f, z), new Vector3(w, 0.12f, d));

                // Transats en bois + matelas + dossier incliné au bord de la piscine.
                for (int i = 0; i < 3; i++)
                {
                    Vector3 lPos = b.Sc(new Vector3(1.4f, 0f, -2.7f - i * 1.7f));
                    b.WorldBox("Bois", lPos, new Vector3(0.75f, 0.22f, 1.85f), true);
                    b.WorldBox("Draps", lPos + new Vector3(0f, 0.22f, 0.15f), new Vector3(0.68f, 0.08f, 1.35f), false);
                    b.P("Draps").AddRotatedBox(lPos + new Vector3(0f, 0.38f, -0.68f), new Vector3(0.68f, 0.08f, 0.55f), Quaternion.Euler(-28f, 0f, 0f));
                }
                // Salon de jardin 3D sur la terrasse au nord de la piscine + parasol.
                b.AreaProp("outdoor_table_chair_set_01", 4.6f, 0.2f, 0.03f, 90f, 1.0f, true, 0.005f);
                Vector3 parasol = b.Sc(new Vector3(4.6f, 0.03f, 0.2f));
                b.WorldBox("Bois", parasol, new Vector3(0.06f, 2.3f, 0.06f), false);
                b.P("Draps").AddCone(parasol + Vector3.up * 2.1f, 1.65f, 0.05f, 0.45f, 10);
                b.AreaProp("football", 4.8f, -7.2f, 0.02f, 20f, 1.15f, false, 0.005f);
            }

            // Jardinières, arbustes et banc de jardin dans la cour.
            foreach ((float x, float z) in luxe
                ? new[] { (-0.3f, -0.45f), (3.5f, -0.45f), (6.8f, -9.8f), (-6.8f, -9.8f) }
                : new[] { (-0.3f, -0.45f), (3.5f, -0.45f), (6.8f, -9.8f) })
            {
                b.AreaProp("planter_box_01", x, z, 0f, 0f, 1.05f, true, 0.005f);
                b.AreaProp("calathea_orbifolia_01", x, z, 0.40f, 0f, 0.26f, false, 0.005f);
            }
            b.AreaProp("painted_wooden_bench", -3.4f, -0.6f, 0f, 0f, 1.15f, true, 0.005f);
            foreach (Vector2 bush in luxe ? new[] { new Vector2(7.0f, -9.2f), new Vector2(7.0f, -1.8f), new Vector2(-6.9f, 8.8f) } : new[] { new Vector2(7.0f, -9.2f) })
                b.AreaProp("shrub_02", bush.x, bush.y, 0.17f, 35f, 0.36f, true, 0.005f);

            // --- Rez-de-chaussée (x -6…6, z 0…9) ---
            b.Slab("Sol", -5.92f, 0.08f, 5.92f, 8.92f, 0.02f, 0.04f);
            b.Wall("Murs", -6f, 9f, 6f, 9f, 0f, h);
            b.Wall("Murs", -6f, 0f, 6f, 0f, 0f, h, 7.8f); // entrée (x = 1,8)
            b.Wall("Murs", -6f, 0f, -6f, 9f, 0f, h);
            b.Wall("Murs", 6f, 0f, 6f, 9f, 0f, h);
            b.Wall("Murs", 2.6f, 0f, 2.6f, 9f, 0f, h, 1.6f, 4.5f, 7.2f);
            b.Wall("Murs", 2.6f, 3.5f, 6f, 3.5f, 0f, h, 2.5f);
            b.Wall("Murs", 2.6f, 5.5f, 6f, 5.5f, 0f, h);
            b.Wall("Murs", 4.2f, 3.5f, 4.2f, 5.5f, 0f, h);

            // Grand salon et salle à manger avec mobilier 3D Poly Haven.
            b.Tv(-5.70f, 2.2f, 0f, F.East, true);
            b.WorldProp("sofa_03", b.Place(-3.7f, 2.2f, 0f), 270f, 0.94f, false, 0.004f);
            b.WorldSolid(b.Place(-3.7f, 2.2f, 0f) + new Vector3(0f, 0.45f, 0f), new Vector3(0.95f, 0.9f, 2.55f));
            b.PlanProp("ArmChair_01", -4.7f, 0.7f, 0f, 0f, 1.0f, true, 0.004f);
            b.PlanProp("ArmChair_01", -4.7f, 3.7f, 0f, 180f, 1.0f, true, 0.004f);
            b.CoffeeTable(-4.8f, 2.2f, 0f, true);
            b.Rug(-4.7f, 2.2f, 0f, 2.4f, 2.8f);

            b.Table(-2.7f, 6.6f, 0f, 2.2f, 1.15f, 6);
            b.PlanProp("classic_laptop", -2.5f, 6.6f, 0.75f, 210f, 0.6f, false, 0.004f);
            b.Tv(-5.70f, 6.6f, 0f, F.East, true);
            b.PlanProp("wooden_bookshelf_worn", -2.8f, 8.65f, 0f, 180f, 0.95f, true, 0.004f);
            b.PlanProp("potted_plant_04", -5.65f, 0.45f, 0f, 0f, 2.7f, false, 0.004f);
            b.PlanProp("potted_plant_04", -5.65f, 8.55f, 0f, 0f, 2.7f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", -5.90f, 4.4f, 1.85f, 90f, 1.8f, false, 0.004f);
            b.PlanProp("fancy_picture_frame_01", 2.50f, 6.6f, 1.85f, 270f, 1.6f, false, 0.004f);
            b.PlanProp("wall_clock", 0f, 8.90f, 2.35f, 180f, 1.4f, false, 0.004f);
            if (luxe)
            {
                b.AreaProp("Chandelier_01", -3.4f, 2.2f, h, 0f, 1.0f, false, 0.004f);
                b.AreaProp("Chandelier_01", -2.7f, 6.6f, h, 0f, 1.0f, false, 0.004f);
                b.PlanProp("ceramic_vase_01", 2.2f, 0.55f, 0f, 0f, 1.25f, false, 0.004f);
            }
            else
            {
                b.AreaProp("ceiling_fan", -3.4f, 2.2f, h, 0f, 1.15f, false, 0.004f);
                b.AreaProp("ceiling_fan", -2.7f, 6.6f, h, 0f, 1.05f, false, 0.004f);
            }

            // Escalier ajouré (couloir central), cuisine, toilettes, chambre 1.
            b.Stairs(-0.9f, 0.7f, 2.2f, 7.8f, 0f, up);
            b.Counter(4.3f, 8.68f, 0f, 3.0f, F.South, true, true);
            b.Fridge(5.58f, 6.1f, 0f, F.West);
            b.PlanProp("small_lpg_tank", 5.65f, 7.2f, 0f, 0f, 0.85f, true, 0.004f);
            b.Box("Bois", new Vector3(4.3f, 0f, 7.0f), new Vector3(1.5f, 0.88f, 0.72f), true);
            b.Box("Metal", new Vector3(4.3f, 0.88f, 7.0f), new Vector3(1.58f, 0.05f, 0.78f), false);
            b.Toilet(3.4f, 4.8f, 0f);
            b.Shower(5.4f, 4.9f, 0f);
            b.Washbasin(4.8f, 3.9f, 0f, F.South);
            b.Bed(5.0f, 1.7f, 0f, F.East);
            b.Wardrobe(3.0f, 2.9f, 0f, 1.0f, F.East);
            b.Window(new Vector3(-3f, 1.4f, 0.09f), F.South, 2.4f);
            b.Window(new Vector3(-5.91f, 1.4f, 4.5f), F.West, 1.6f);
            // Fenêtres côté façade sud extérieure (visibles depuis la cour).
            b.Window(new Vector3(-3f, 1.45f, -0.09f), F.South, 2.4f);
            b.Window(new Vector3(4.4f, 1.45f, -0.09f), F.South, 1.5f);
            b.Light(new Vector3(-2.5f, h, 2.5f));
            b.Light(new Vector3(-2.5f, h, 6.5f));
            b.Light(new Vector3(4.3f, h, 7.2f));
            b.Light(new Vector3(4.3f, h, 1.6f));

            // --- Plancher de l'étage (rentré de 8 cm dans l'épaisseur du mur pour ne pas baver en façade !) ---
            b.Slab("Parquet", -5.92f, 0.08f, -0.9f, 8.92f, up);
            b.Slab("Parquet", 0.7f, 0.08f, 5.92f, 8.92f, up);
            b.Slab("Sol", -0.9f, 0.08f, 0.7f, 2.2f, up);
            b.Slab("Sol", -0.9f, 7.8f, 0.7f, 8.92f, up);
            b.Slab("Plafond", -5.92f, 0.08f, -0.9f, 8.92f, up - 0.2f, 0.02f);
            b.Slab("Plafond", 0.7f, 0.08f, 5.92f, 8.92f, up - 0.2f, 0.02f);
            b.Slab("Plafond", -0.9f, 0.08f, 0.7f, 2.2f, up - 0.2f, 0.02f);
            b.Slab("Plafond", -0.9f, 7.8f, 0.7f, 8.92f, up - 0.2f, 0.02f);
            // Bandeau de façade extérieure débordant (sans Z-fighting) et plafond de l'étage + grand toit à 4 pans !
            b.Slab("Murs", -6.08f, -0.08f, 6.08f, 9.08f, up - 0.02f, 0.16f);
            b.Slab("Plafond", -6.02f, -0.02f, 6.02f, 9.02f, up + h + 0.18f);
            b.P("Tuiles").AddHipRoof(b.Sc(new Vector3(0f, up + h, 4.5f)),
                new Vector2(12.2f * b.Scale, 9.2f * b.Scale), 2.6f, 0.75f);

            // --- Étage ---
            b.Wall("Murs", -6f, 9f, 6f, 9f, up, h);
            b.Wall("Murs", -6f, 0f, 6f, 0f, up, h);
            b.Wall("Murs", -6f, 0f, -6f, 9f, up, h);
            b.Wall("Murs", 6f, 0f, 6f, 9f, up, h);
            b.Wall("Murs", -0.9f, 0f, -0.9f, 9f, up, h, 1.1f, 8.4f);
            // Garde-corps ajouré le long de la trémie (poteaux et main courante au lieu d'un muret plein).
            b.AreaSolid(new Vector3(0.7f, up + 0.5f, 5.0f), new Vector3(0.12f, 1.0f, 5.6f));
            b.WorldBox("Metal", b.Sc(new Vector3(0.7f, up + 0.95f, 5.0f)), new Vector3(0.06f, 0.06f, 5.6f * b.Scale), false);
            b.WorldBox("Bois", b.Sc(new Vector3(0.7f, up + 0.50f, 5.0f)), new Vector3(0.04f, 0.04f, 5.6f * b.Scale), false);
            for (float rz = 2.2f; rz <= 7.81f; rz += 0.7f)
                b.WorldBox("Metal", b.Sc(new Vector3(0.7f, up, rz)), new Vector3(0.05f, 1.0f, 0.05f), false);

            b.Wall("Murs", 2.5f, 0f, 2.5f, 9f, up, h, 1.1f, 8.4f);
            b.Wall("Murs", -6f, 4f, -0.9f, 4f, up, h);
            b.Wall("Murs", 2.5f, 4.6f, 6f, 4.6f, up, h, 2.1f);
            b.Wall("Murs", 2.5f, 2.6f, 6f, 2.6f, up, h);

            if (four)
            {
                b.Bed(-3.0f, 7.9f, up, F.North);
                b.Wardrobe(-5.6f, 6.5f, up, 2.0f, F.East);
                b.PlanProp("painted_wooden_cabinet", -5.5f, 4.6f, up, 90f, 0.95f, true, 0.004f);
                b.AreaProp("ceiling_fan", -3.4f, 6.5f, up + h, 0f, 1.0f, false, 0.004f);
            }
            else
            {
                b.Tv(-5.70f, 6.5f, up, F.East, true);
                b.Sofa(-3.8f, 6.5f, up, 2.2f, F.West, modern: true);
                b.PlanProp("ArmChair_01", -4.7f, 4.7f, up, 0f, 0.95f, true, 0.004f);
                b.Rug(-4.7f, 6.5f, up, 2.2f, 2.4f);
                b.AreaProp("ceiling_fan", -3.5f, 6.5f, up + h, 0f, 1.0f, false, 0.004f);
            }

            b.Bed(-3.2f, 1.1f, up, F.South);
            b.Wardrobe(-5.6f, 2.6f, up, 1.4f, F.East);
            b.Shower(-1.45f, 3.4f, up);
            b.Washbasin(-1.35f, 2.3f, up, F.East);

            b.Bed(4.9f, 7.6f, up, F.North);
            b.Wardrobe(2.95f, 6.6f, up, 1.6f, F.East);
            b.PlanProp("painted_wooden_cabinet", 5.5f, 5.2f, up, 270f, 0.95f, true, 0.004f);
            b.AreaProp("ceiling_fan", 4.3f, 6.8f, up + h, 0f, 1.0f, false, 0.004f);
            b.Bathtub(4.9f, 3.6f, up, F.East);
            b.Toilet(3.0f, 3.4f, up);
            b.Washbasin(5.6f, 2.95f, up, F.East);

            if (four)
                b.Bed(4.9f, 1.2f, up, F.East);
            b.Wardrobe(3.0f, 1.3f, up, 1.6f, F.East);

            b.Window(new Vector3(-3f, up + 1.4f, 0.09f), F.South, 2.4f);
            b.Window(new Vector3(-3f, up + 1.45f, -0.09f), F.South, 2.4f);
            b.Window(new Vector3(4.3f, up + 1.45f, -0.09f), F.South, 1.6f);
            b.Window(new Vector3(4.2f, up + 1.4f, 8.91f), F.North, 1.6f);
            b.Window(new Vector3(-3f, up + 1.4f, 8.91f), F.North, 1.6f);
            // Blocs climatiseurs sur la façade extérieure.
            b.AreaProp("exterior_aircon_unit", -0.8f, -0.22f, up + 0.65f, 180f, 0.55f, false, 0.005f);
            b.AreaProp("exterior_aircon_unit", 6.18f, 3.0f, up + 0.65f, 90f, 0.55f, false, 0.005f);

            b.Light(new Vector3(-2.5f, up + h, 6.5f));
            b.Light(new Vector3(-2.5f, up + h, 2.0f));
            b.Light(new Vector3(4.3f, up + h, 6.8f));
            b.Light(new Vector3(4.3f, up + h, 3.6f));
            b.Light(new Vector3(4.3f, up + h, 1.3f));
            b.Light(new Vector3(-0.1f, up + h, 8.4f));
            return b;
        }

        private static PlanBuilder BuildPlan(PropertyKind kind) => kind switch
        {
            PropertyKind.Studio => BuildStudioPlan(),
            PropertyKind.OneBedroom => BuildF2Plan(),
            PropertyKind.TwoBedroom => BuildF3Plan(),
            _ => BuildVillaPlan(kind),
        };

        private static PlanTemplate BakePlan(PlanBuilder b, string prefix, List<Mesh> meshes)
        {
            var template = new PlanTemplate { Colliders = b.Colliders, Lights = b.Lights, Props = b.Props };
            foreach (KeyValuePair<string, (BoxMeshBuilder builder, Material material)> part in b.Parts)
            {
                if (part.Value.builder.IsEmpty)
                    continue;
                Mesh mesh = part.Value.builder.Build(prefix + "_" + part.Key);
                meshes.Add(mesh);
                template.Parts.Add((part.Key, mesh, part.Value.material));
            }
            return template;
        }

        /// <summary>Pose un gabarit : visuel (masqué, affiché par StudioVisibility) et colliders actifs.</summary>
        private static GameObject PlaceTemplate(Transform parent, string name, Vector3 position, PlanTemplate template)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var visual = new GameObject("Visuel");
            visual.transform.SetParent(root.transform, false);
            foreach ((string partName, Mesh mesh, Material material) in template.Parts)
            {
                var go = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(visual.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Vector3 lightPosition in template.Lights)
            {
                var lightObject = new GameObject("Plafonnier");
                lightObject.transform.SetParent(visual.transform, false);
                lightObject.transform.localPosition = lightPosition;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8.5f;
                light.intensity = 2.4f;
                light.color = new Color(1f, 0.92f, 0.80f);
                light.shadows = LightShadows.None;
            }
            if (template.Props != null)
            {
                foreach (var prop in template.Props)
                {
                    PropLibrary.Place(prop.id, visual.transform, prop.position, prop.yaw, prop.scale,
                        castShadows: false, cullScreenSize: prop.cull, solid: prop.solid);
                }
            }
            visual.SetActive(false);

            var collision = new GameObject("Collisions");
            collision.transform.SetParent(root.transform, false);
            foreach ((Vector3 center, Vector3 size, Quaternion rotation) in template.Colliders)
            {
                if (rotation == Quaternion.identity)
                {
                    AddBoxCollider(collision, center, size);
                    continue;
                }
                var ramp = new GameObject("Rampe");
                ramp.transform.SetParent(collision.transform, false);
                ramp.transform.localPosition = center;
                ramp.transform.localRotation = rotation;
                ramp.AddComponent<BoxCollider>().size = size;
            }
            return visual;
        }
    }
}
