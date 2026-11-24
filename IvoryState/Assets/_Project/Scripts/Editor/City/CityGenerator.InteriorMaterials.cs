using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Matériaux texturés des intérieurs (textures Poly Haven CC0, 1K, voir tools/fetch-assets.ps1).
    /// Les UV des boîtes sont en mètres : « tiling » = répétitions de la texture par mètre.
    /// </summary>
    internal static partial class CityGenerator
    {
        private static Material IntWall(Color tint) =>
            CityMaterials.Lit("M_Int_Wall_" + ColorUtility.ToHtmlStringRGB(tint), "painted_plaster_wall", 0.4f, 0.1f, tint, true, 1);

        private static Material IntFloorTiles() => CityMaterials.Lit("M_Int_FloorTiles", "floor_tiles_06", 0.5f, 0.35f, Color.white, true, 2);
        private static Material IntMarble() => CityMaterials.Lit("M_Int_Marble", "marble_tiles", 0.2f, 0.6f, Color.white, true, 2);
        private static Material IntWood() => CityMaterials.Lit("M_Int_Wood", "dark_wood", 0.8f, 0.3f, new Color(0.95f, 0.85f, 0.75f), true, 2);
        private static Material IntSheets() => CityMaterials.Lit("M_Int_Sheets", "denim_fabric", 1f, 0.1f, Color.white, true, 1);
        private static Material IntPagne() => CityMaterials.Lit("M_Int_Pagne", "fabric_pattern_07", 1.2f, 0.1f, Color.white, true, 1);
        private static Material IntShowerTiles() => CityMaterials.Lit("M_Int_ShowerTiles", "marble_mosaic_tiles", 0.7f, 0.5f, Color.white, true, 1);
        private static Material IntGrass() => CityMaterials.Lit("M_Int_Grass", "leafy_grass", 0.3f, 0.1f, new Color(0.72f, 1f, 0.62f), true, 2);
        private static Material IntHospitalFloor() => CityMaterials.Lit("M_Int_HospitalFloor", "anti_skid_tiles", 0.5f, 0.3f, Color.white, true, 2);
        private static Material IntRoofTiles() => CityMaterials.Lit("M_Int_RoofTiles", "clay_roof_tiles_02", 0.4f, 0.2f, Color.white, true, 2);
        private static Material IntTerraceTiles() => CityMaterials.Lit("M_Int_TerraceTiles", "terracotta_floor_tiles", 0.45f, 0.25f, Color.white, true, 2);
        private static Material IntParquet() => CityMaterials.Lit("M_Int_Parquet", "laminate_floor_02", 0.5f, 0.4f, Color.white, true, 2);
        private static Material IntBrick() => CityMaterials.Lit("M_Int_Brick", "brick_wall_005", 0.4f, 0.12f, Color.white, true, 2);
    }
}
