"""Build script for Grande_Mosquee in Blender (5.3+).

Generates a monumental Abidjan-Plateau-inspired Grand Mosque complex:
- Compound: 68.0 m x 74.0 m (5,032 m2) with enclosed Cour d'Honneur / Sahn,
  central octagonal/circular ablution fountain, palm groves, and grand gates.
- Iconic Silhouette:
  - Warm golden-ochre & white facade with azure blue mosaic entrance pishtaq,
    2 towering Minarets (48 m West Grand Minaret + 38 m East Minaret) with
    balconies & golden crescents.
  - Soaring central Majorelle Blue & Gold Dome (R = 9.2 m, crest 26.5 m) open
    interiorly into the Grand Prayer Hall, flanked by 4 corner cupolas.
- Complete interior spaces:
  - Grand Prayer Hall (40 m x 36 m) with green Qibla prayer carpets & saff
    alignment stripes, interior arcade columns, gilded Mihrab & stepped Minbar,
    giant central dome chandelier, and shoe-cubby Narthex.
  - Women's Prayer Mezzanine Gallery (R+1) overlooking the Prayer Hall behind
    a golden/glass balustrade, accessed from both side wings.
  - West Wing: Large Wudu Ablutions Hall (3 multi-station marble/inox rows),
    Restrooms, Quranic School Classrooms (R+1), Community Meeting Room (R+1).
  - East Wing: Imam's Office & Reception, Zakat / Caisse Treasury with armored
    teller bay & safe, Islamic Library, Islamic Council & Conference Hall (R+1).

Run with:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/architecture/build_grande_mosquee.py [-- <preview_output_dir>]
"""

import os
import sys

import bpy

ARCH_DIR = os.path.dirname(os.path.abspath(__file__))
if ARCH_DIR not in sys.path:
    sys.path.insert(0, ARCH_DIR)

import arch_common as ac  # noqa: E402
import arch_monuments_common as amc  # noqa: E402


REPO_ROOT = os.path.abspath(os.path.join(ARCH_DIR, "..", "..", ".."))
UNITY_ROOT = os.path.join(REPO_ROOT, "IvoryState")
BLEND_PATH = os.path.join(ARCH_DIR, "Grande_Mosquee.blend")
MANIFEST_PATH = os.path.join(ARCH_DIR, "grande_mosquee_manifest.json")
EXT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Monuments", "SM_Grande_Mosquee.fbx"
)
INT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Interieurs", "SM_Grande_Mosquee_Interieur.fbx"
)

Z_RDC = 0.60
Z_RDC_F = 0.64
Z_WING_C = 4.95
Z_R1 = 5.25
Z_R1_F = 5.29
Z_R1_C = 9.45
Z_WING_ROOF = 9.75
Z_HALL_C = 11.20
Z_HALL_ROOF = 11.65
Z_PARAPET = 12.75


def _make_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, bounds, mat_panel, mat_frame="Mat_Arch_Or_Ceremonie",
    glass_bounds=None, rot_deg=(0.0, 0.0, 0.0), open_note="",
):
    b, meta, drot = amc.add_door_leaf_builder(
        name=name,
        collection_name=col_name,
        pivot_world=pivot,
        leaf_bounds_local=bounds,
        mat_panel=mat_panel,
        mat_frame=mat_frame,
        mat_handle="Mat_Arch_Or_Ceremonie",
        glass_bounds_local=glass_bounds,
        default_rot_deg=rot_deg,
        open_note=open_note,
    )
    obj = b.build_object(parent=parent, default_rot_deg=drot)
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def _add_minaret(b, cx, cy, total_h=48.0):
    """Build a monumental Plateau-style Minaret with square plinth, balconies, dome & crescent."""
    b.add_box_center(cx, cy, 5.0, 5.2, 5.2, 10.0, "Mat_Arch_Pierre_Ocre", collider=True)
    b.add_box_center(cx, cy, 10.2, 5.5, 5.5, 0.5, "Mat_Arch_Mosquee_Bleu")
    shaft_top = total_h - 10.0
    b.add_cylinder(cx, cy, 10.4, shaft_top, 2.05, 16, "Mat_Arch_MurExt_Blanc")
    b.add_collider_center(cx, cy, (10.4 + shaft_top) * 0.5, 4.1, 4.1, shaft_top - 10.4)
    for bz in (total_h * 0.48, total_h * 0.75):
        b.add_cylinder(cx, cy, bz - 0.3, bz + 0.35, 2.95, 16, "Mat_Arch_Mosquee_Bleu")
        b.add_cylinder(cx, cy, bz + 0.35, bz + 1.25, 2.85, 16, "Mat_Arch_Or_Ceremonie")
    b.add_cylinder(cx, cy, shaft_top, shaft_top + 4.5, 1.55, 12, "Mat_Arch_Pierre_Ocre")
    amc.add_dome(b, cx, cy, shaft_top + 4.5, 1.70, 3.2, segments=16, rings=7, mat_name="Mat_Arch_Mosquee_Bleu")
    amc.add_crescent_finial(b, cx, cy, shaft_top + 7.7, scale=0.85)


def build_exterior():
    col = "Mosquee_Exterieur"
    ext_root = amc.make_empty_root("Grande_Mosquee_Ext", col)

    site = ac.ArchMeshBuilder("Mosq_Ext_Site", collection_name=col)
    walls = ac.ArchMeshBuilder("Mosq_Ext_Walls", collection_name=col)
    roof_b = ac.ArchMeshBuilder("Mosq_Ext_Roof", collection_name=col)
    det = ac.ArchMeshBuilder("Mosq_Ext_Details", collection_name=col)

    # 1. Domain (68m x 74m), Sahn Courtyard, Fountain, Perimeter Arcades & Gates
    site.add_box_bounds(-34.0, 34.0, -37.0, 37.0, -0.15, 0.0, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)
    site.add_box_bounds(-31.5, -16.0, -34.0, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(16.0, 31.5, -34.0, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(-31.5, 31.5, 30.0, 35.5, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)

    # Central Sahn Courtyard Ablution Fountain (0, -22.5)
    det.add_cylinder(0.0, -22.5, 0.0, 0.55, 4.8, 16, "Mat_Arch_Parement_Pierre")
    det.add_cylinder(0.0, -22.5, 0.10, 0.48, 4.2, 16, "Mat_Arch_Eau_Bassin")
    det.add_cylinder(0.0, -22.5, 0.0, 3.6, 0.55, 12, "Mat_Arch_Sol_Marbre")
    amc.add_dome(det, 0.0, -22.5, 3.6, 2.4, 1.8, segments=16, rings=6, mat_name="Mat_Arch_Mosquee_Bleu")
    det.add_collider_center(0.0, -22.5, 1.5, 9.6, 9.6, 3.0)

    # Perimeter walls & Monumental South Courtyard Gate (-3.5..+3.5 at Y = -37)
    site.add_wall_with_openings("Y", -33.85, 0.30, -37.0, 37.0, 0.0, 2.45, "Mat_Arch_Pierre_Ocre", collider=True)
    site.add_wall_with_openings("Y", 33.85, 0.30, -37.0, 37.0, 0.0, 2.45, "Mat_Arch_Pierre_Ocre", collider=True)
    site.add_wall_with_openings("X", 36.85, 0.30, -34.0, 34.0, 0.0, 2.45, "Mat_Arch_Pierre_Ocre", collider=True)
    for sx0, sx1 in ((-33.7, -3.6), (3.6, 33.7)):
        site.add_wall_with_openings("X", -36.85, 0.30, sx0, sx1, 0.0, 2.45, "Mat_Arch_Pierre_Ocre", collider=True)
    # South Gate Arch Portal
    det.add_box_bounds(-4.6, -3.6, -37.2, -36.2, 0.0, 5.2, "Mat_Arch_Mosquee_Bleu", collider=True)
    det.add_box_bounds(3.6, 4.6, -37.2, -36.2, 0.0, 5.2, "Mat_Arch_Mosquee_Bleu", collider=True)
    det.add_box_bounds(-4.8, 4.8, -37.3, -36.1, 4.2, 5.8, "Mat_Arch_Mosquee_Bleu")
    det.add_box_bounds(-4.8, 4.8, -37.35, -36.05, 5.8, 6.15, "Mat_Arch_Or_Ceremonie")

    # Building Podium & Forecourt Steps
    site.add_box_bounds(-31.0, 31.0, -9.5, 28.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", collider=True)
    for s in range(3):
        site.add_box_bounds(-12.0, 12.0, -11.6 + s * 0.70, -9.4, 0.0, (s + 1) * 0.20, "Mat_Arch_Sol_Marbre", collider=True)

    for px, py in [(-24.0, -29.0), (-24.0, -18.0), (24.0, -29.0), (24.0, -18.0), (-24.0, 32.0), (24.0, 32.0)]:
        amc.add_palm_tree(det, px, py, 0.0, height=7.2)
    for lx, ly in [(-12.0, -30.0), (12.0, -30.0), (-12.0, -16.0), (12.0, -16.0)]:
        amc.add_streetlamp(det, lx, ly, 0.0, height=5.5)

    # 2. Twin Minarets (-26.5, -10.5) & (+26.5, -10.5)
    _add_minaret(det, -26.5, -10.5, total_h=48.0)
    _add_minaret(det, 26.5, -10.5, total_h=38.0)

    # 3. Main Mosque Sanctuary Shell (-20..+20, -8..+28, Z = 0.60..11.20)
    walls.add_wall_with_openings("X", -7.80, 0.40, -20.0, 20.0, Z_RDC, Z_HALL_C, "Mat_Arch_Pierre_Ocre", openings=[
        {"s0": -3.0, "s1": 3.0, "z0": Z_RDC, "z1": Z_RDC + 4.50, "kind": "door"},
        {"s0": -16.0, "s1": -11.0, "z0": 2.4, "z1": 9.2, "kind": "window"},
        {"s0": 11.0, "s1": 16.0, "z0": 2.4, "z1": 9.2, "kind": "window"},
    ])
    # Blue Mosaic Pishtaq Portal Framing Main Entrance
    det.add_box_bounds(-4.6, -3.1, -8.45, -7.85, Z_RDC, 12.2, "Mat_Arch_Mosquee_Bleu")
    det.add_box_bounds(3.1, 4.6, -8.45, -7.85, Z_RDC, 12.2, "Mat_Arch_Mosquee_Bleu")
    det.add_box_bounds(-4.6, 4.6, -8.50, -7.80, 5.4, 12.5, "Mat_Arch_Mosquee_Bleu")
    det.add_box_bounds(-4.8, 4.8, -8.55, -7.75, 12.5, 13.0, "Mat_Arch_Or_Ceremonie")

    # Rear Qibla Wall (+28.0) with high stained-glass windows flanking Mihrab
    walls.add_wall_with_openings("X", 27.80, 0.40, -20.0, 20.0, Z_RDC, Z_HALL_C, "Mat_Arch_Pierre_Ocre", openings=[
        {"s0": -16.5, "s1": -11.5, "z0": 5.5, "z1": 9.8, "kind": "window"},
        {"s0": 11.5, "s1": 16.5, "z0": 5.5, "z1": 9.8, "kind": "window"},
    ])
    # Sanctuary side walls (X = +-20.0) with arch-passages to side wings
    for sx_f in (-19.83, 19.83):
        walls.add_wall_with_openings("Y", sx_f, 0.34, -8.0, 28.0, Z_RDC, Z_HALL_C, "Mat_Arch_Pierre_Ocre", openings=[
            {"s0": -4.0, "s1": -1.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.80, "kind": "door"},
            {"s0": 12.0, "s1": 14.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.80, "kind": "door"},
            {"s0": -4.0, "s1": -1.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "door"},
        ])

    # Sanctuary Roof with Center Dome Cutout (-5.5..+5.5, +4.5..+15.5)
    dome_cut = [(-5.5, 5.5, 4.5, 15.5)]
    amc.add_slab_with_cutouts(roof_b, -20.2, 20.2, -8.2, 28.2, Z_HALL_C, Z_HALL_ROOF, dome_cut, "Mat_Arch_Toiture_Terrasse", collider=True)
    roof_b.add_wall_with_openings("X", -7.85, 0.30, -20.0, 20.0, Z_HALL_ROOF, Z_PARAPET, "Mat_Arch_Pierre_Ocre")
    roof_b.add_wall_with_openings("X", 27.85, 0.30, -20.0, 20.0, Z_HALL_ROOF, Z_PARAPET, "Mat_Arch_Pierre_Ocre")
    roof_b.add_wall_with_openings("Y", -19.85, 0.30, -8.0, 28.0, Z_HALL_ROOF, Z_PARAPET, "Mat_Arch_Pierre_Ocre")
    roof_b.add_wall_with_openings("Y", 19.85, 0.30, -8.0, 28.0, Z_HALL_ROOF, Z_PARAPET, "Mat_Arch_Pierre_Ocre")
    roof_b.add_box_bounds(-20.3, 20.3, -8.3, 28.3, Z_PARAPET, Z_PARAPET + 0.30, "Mat_Arch_Mosquee_Bleu")

    # Grand Central Majorelle Blue & Gold Dome + Open Drum + 4 Corner Domes
    amc.add_hollow_drum(roof_b, 0.0, 10.0, Z_HALL_ROOF, Z_HALL_ROOF + 3.6, 8.0, 9.2, segments=20, mat_name="Mat_Arch_Pierre_Ocre")
    amc.add_dome(
        roof_b, 0.0, 10.0, Z_HALL_ROOF + 3.6, 9.5, 8.2, segments=28, rings=10,
        mat_name="Mat_Arch_Mosquee_Bleu", open_bottom=True, inner_mat="Mat_Arch_MurInt_Creme",
    )
    amc.add_crescent_finial(roof_b, 0.0, 10.0, Z_HALL_ROOF + 11.8, scale=1.35)
    for cx in (-14.5, 14.5):
        for cy in (-2.5, 22.5):
            roof_b.add_cylinder(cx, cy, Z_HALL_ROOF, Z_HALL_ROOF + 1.4, 3.0, 16, "Mat_Arch_Pierre_Ocre")
            amc.add_dome(roof_b, cx, cy, Z_HALL_ROOF + 1.4, 3.1, 3.4, segments=16, rings=7, mat_name="Mat_Arch_Mosquee_Bleu")
            amc.add_crescent_finial(roof_b, cx, cy, Z_HALL_ROOF + 4.8, scale=0.65)

    # 4. Flanking 2-Level Service & Education Wings (West -31..-20, East +20..+31, Y = -8..+28)
    for fixed_x, wx0, wx1 in ((-30.83, -31.0, -20.0), (30.83, 20.0, 31.0)):
        for z0, z1 in ((Z_RDC, Z_WING_C), (Z_R1, Z_R1_C)):
            walls.add_wall_with_openings("Y", fixed_x, 0.34, -8.0, 28.0, z0, z1, "Mat_Arch_Pierre_Ocre", openings=[
                {"s0": -4.0, "s1": 3.0, "z0": z0 + 0.90, "z1": z1 - 0.50, "kind": "window"},
                {"s0": 7.0, "s1": 14.0, "z0": z0 + 0.90, "z1": z1 - 0.50, "kind": "window"},
                {"s0": 18.0, "s1": 24.0, "z0": z0 + 0.90, "z1": z1 - 0.50, "kind": "window"},
            ])
            s_ops = [{"s0": (wx0 + wx1) * 0.5 - 2.0, "s1": (wx0 + wx1) * 0.5 + 2.0, "z0": z0 + 0.90, "z1": z1 - 0.50, "kind": "window"}]
            if z0 == Z_RDC:
                f_ops = [{"s0": (wx0 + wx1) * 0.5 - 1.2, "s1": (wx0 + wx1) * 0.5 + 1.2, "z0": z0, "z1": z0 + 2.65, "kind": "door"}]
            else:
                f_ops = s_ops
            walls.add_wall_with_openings("X", -7.83, 0.34, wx0, wx1, z0, z1, "Mat_Arch_Pierre_Ocre", openings=f_ops)
            walls.add_wall_with_openings("X", 27.83, 0.34, wx0, wx1, z0, z1, "Mat_Arch_Pierre_Ocre", openings=s_ops)
    roof_b.add_box_bounds(-31.2, -20.0, -8.2, 28.2, Z_R1_C, Z_WING_ROOF, "Mat_Arch_Toiture_Terrasse", collider=True)
    roof_b.add_box_bounds(20.0, 31.2, -8.2, 28.2, Z_R1_C, Z_WING_ROOF, "Mat_Arch_Toiture_Terrasse", collider=True)

    site.build_object(parent=ext_root)
    walls.build_object(parent=ext_root)
    roof_obj = roof_b.build_object(parent=ext_root)
    det.build_object(parent=ext_root)

    # 5. Courtyard & Sanctuary Doors
    door_specs, door_objs = [], []
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Mosquee_Principal_L", pivot=(-3.6, -36.85, 0.0),
        bounds=(0.0, 3.55, -0.07, 0.07, 0.05, 3.80),
        mat_panel="Mat_Arch_Or_Ceremonie", rot_deg=(0.0, 0.0, 30.0), open_note="Portail d'honneur cour gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Mosquee_Principal_R", pivot=(3.6, -36.85, 0.0),
        bounds=(-3.55, 0.0, -0.07, 0.07, 0.05, 3.80),
        mat_panel="Mat_Arch_Or_Ceremonie", rot_deg=(0.0, 0.0, -30.0), open_note="Portail d'honneur cour droit",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mosquee_Entree_L", pivot=(-3.0, -7.80, Z_RDC_F),
        bounds=(0.0, 2.95, -0.06, 0.06, 0.0, 4.40),
        mat_panel="Mat_Arch_Bois_Exterieur", glass_bounds=(0.50, 2.45, -0.02, 0.02, 0.65, 3.80),
        rot_deg=(0.0, 0.0, 28.0), open_note="Portail monumental Salle de Priere G",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mosquee_Entree_R", pivot=(3.0, -7.80, Z_RDC_F),
        bounds=(-2.95, 0.0, -0.06, 0.06, 0.0, 4.40),
        mat_panel="Mat_Arch_Bois_Exterieur", glass_bounds=(-2.45, -0.50, -0.02, 0.02, 0.65, 3.80),
        rot_deg=(0.0, 0.0, -28.0), open_note="Portail monumental Salle de Priere D",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Ablutions_Cour", pivot=(-26.7, -7.83, Z_RDC_F),
        bounds=(0.0, 2.35, -0.04, 0.04, 0.0, 2.55),
        mat_panel="Mat_Arch_Bois_Exterieur", open_note="Acces cour vers Salle d'Ablutions",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Accueil_Imam_Cour", pivot=(24.3, -7.83, Z_RDC_F),
        bounds=(0.0, 2.35, -0.04, 0.04, 0.0, 2.55),
        mat_panel="Mat_Arch_Bois_Exterieur", open_note="Acces cour vers Accueil & Imam",
    )

    ext_colliders = site.colliders + walls.colliders + roof_b.colliders + det.colliders
    return ext_root, ext_colliders, door_specs, door_objs, [roof_obj.name]


def build_interior():
    col = "Mosquee_Interieur"
    int_root = amc.make_empty_root("Grande_Mosquee_Int", col)

    rdc_s = ac.ArchMeshBuilder("Mosq_Int_RDC_Structure", collection_name=col)
    rdc_f = ac.ArchMeshBuilder("Mosq_Int_RDC_Mobilier", collection_name=col)
    r1_s = ac.ArchMeshBuilder("Mosq_Int_R1_Structure", collection_name=col)
    r1_f = ac.ArchMeshBuilder("Mosq_Int_R1_Mobilier", collection_name=col)

    door_specs, door_objs = [], []
    r1_names = ["Mosq_Int_R1_Structure", "Mosq_Int_R1_Mobilier"]
    r1_doors = []
    point_lights = []

    # ------------------------------------------------------------------
    # Main Prayer Hall, Prayer Carpets (Qibla +Y), Mihrab, Minbar & Mezzanine
    # ------------------------------------------------------------------
    rdc_s.add_box_bounds(-30.7, 30.7, -7.7, 27.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Marbre", uv_scale=0.4, collider=True)
    # Green Prayer Carpet covering Sanctuary (-19.6..+19.6, -2.5..+27.5) + gold saff row bands
    rdc_s.add_box_bounds(-19.5, 19.5, -2.5, 27.4, Z_RDC_F, Z_RDC_F + 0.015, "Mat_Arch_Mosquee_Vert")
    for sy in range(-1, 27, 2):
        rdc_s.add_box_bounds(-19.2, 19.2, float(sy), float(sy) + 0.07, Z_RDC_F + 0.015, Z_RDC_F + 0.022, "Mat_Arch_Or_Ceremonie")

    # Shoe Cubby Racks in Narthex (-16..-5 and +5..+16 at Y = -6.0)
    for sx in (-12.5, -7.5, 7.5, 12.5):
        rdc_f.add_box_center(sx, -5.8, Z_RDC_F + 0.65, 3.8, 0.50, 1.30, "Mat_Arch_Bois_Interieur", collider=True)

    # Interior Sanctuary Arcade Columns
    for cx in (-11.0, 11.0):
        for cy in (2.5, 10.0, 17.5, 23.0):
            amc.add_colonnade_pillar(rdc_s, cx, cy, Z_RDC_F, Z_HALL_C, radius=0.42, base_mat="Mat_Arch_Sol_Marbre", shaft_mat="Mat_Arch_MurExt_Blanc")

    # Gilded Mihrab & Stepped Minbar on North Qibla Wall (Y = +27.5)
    amc.add_mihrab_and_minbar(rdc_f, 0.0, 27.4, Z_RDC_F)
    # Monumental Central Chandelier hanging from Dome Apex Vault
    amc.add_chandelier(rdc_f, 0.0, 10.0, Z_HALL_ROOF + 11.2, drop=11.8, radius=3.2)

    # Women's Prayer Mezzanine Balcony (R+1: X = -19.6..+19.6, Y = -7.7..+2.0)
    r1_s.add_box_bounds(-19.6, 19.6, -7.7, 2.0, Z_WING_C, Z_R1, "Mat_Arch_Beton_Brut", collider=True)
    r1_s.add_box_bounds(-19.6, 19.6, -7.7, 2.0, Z_R1, Z_R1_F, "Mat_Arch_Mosquee_Vert")
    r1_s.add_glass_railing("X", 1.90, -19.6, 19.6, Z_R1_F, height=1.20, post_mat="Mat_Arch_Or_Ceremonie")
    for sy in (-6.0, -4.0, -2.0, 0.0):
        r1_s.add_box_bounds(-19.2, 19.2, sy, sy + 0.06, Z_R1_F, Z_R1_F + 0.01, "Mat_Arch_Or_Ceremonie")

    # ------------------------------------------------------------------
    # West Wing: Wudu Ablutions (RDC), Stairs, Quranic School & Meeting (R+1)
    # ------------------------------------------------------------------
    wing_w_cut = [(-27.0, -20.2, 22.0, 27.7)]
    rdc_s.add_stair_flight("X", 23.4, 2.2, -21.0, -25.2, Z_RDC_F, (Z_RDC_F + Z_R1_F) * 0.5, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")
    rdc_s.add_box_bounds(-27.0, -25.2, 22.0, 27.7, (Z_RDC_F + Z_R1_F) * 0.5 - 0.2, (Z_RDC_F + Z_R1_F) * 0.5, "Mat_Arch_Beton_Brut", collider=True)
    rdc_s.add_stair_flight("X", 26.0, 2.2, -25.2, -21.0, (Z_RDC_F + Z_R1_F) * 0.5, Z_R1_F, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")

    # West RDC: Wudu Hall (-30.7..-20, -7.7..+15.0), Restrooms (+15.0..+22.0)
    rdc_s.add_wall_with_openings("X", 15.0, 0.20, -30.7, -20.0, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -24.0, "s1": -22.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},
    ])
    rdc_s.add_wall_with_openings("X", 22.0, 0.20, -30.7, -20.0, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -22.5, "s1": -20.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "open"},
    ])
    for wy in (-2.0, 4.0, 10.0):
        amc.add_ablution_station(rdc_f, -25.5, wy, Z_RDC_F, length=6.8, axis="X", stations=6)
    amc.add_bathroom_fixtures(rdc_f, -30.5, -24.0, 15.4, 21.6, Z_RDC_F, num_stalls=3)

    # West R+1: Quranic School Classrooms & Meeting Room
    amc.add_slab_with_cutouts(r1_s, -30.7, -20.0, -7.7, 27.7, Z_WING_C, Z_R1, wing_w_cut, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -30.7, -20.0, -7.7, 27.7, Z_R1, Z_R1_F, wing_w_cut, "Mat_Arch_Sol_Parquet", collider=False)
    r1_s.add_glass_railing("Y", -27.0, 22.0, 27.7, Z_R1_F)
    r1_s.add_wall_with_openings("Y", -22.5, 0.20, -7.7, 22.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 0.0, "s1": 1.6, "z0": Z_R1_F, "z1": Z_R1_F + 2.35, "kind": "door"},   # Ecole Coranique
        {"s0": 13.0, "s1": 14.6, "z0": Z_R1_F, "z1": Z_R1_F + 2.35, "kind": "door"}, # Salle Reunion
    ])
    r1_s.add_wall_with_openings("X", 9.0, 0.20, -30.7, -22.5, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings("X", 22.0, 0.20, -30.7, -20.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -22.2, "s1": -20.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "open"},
    ])
    for sy in (-4.0, -1.0, 2.0, 5.0):
        r1_f.add_box_center(-26.8, sy, Z_R1_F + 0.72, 5.2, 0.65, 0.06, "Mat_Arch_Bois_Interieur", collider=True)
    amc.add_conference_table(r1_f, -26.8, 15.5, Z_R1_F, length=6.8, width=1.9, axis="Y", num_chairs_side=6)

    # ------------------------------------------------------------------
    # East Wing: Imam, Caisse / Zakat, Library (RDC), Islamic Council Hall (R+1)
    # ------------------------------------------------------------------
    wing_e_cut = [(20.2, 27.0, 22.0, 27.7)]
    rdc_s.add_stair_flight("X", 23.4, 2.2, 21.0, 25.2, Z_RDC_F, (Z_RDC_F + Z_R1_F) * 0.5, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")
    rdc_s.add_box_bounds(25.2, 27.0, 22.0, 27.7, (Z_RDC_F + Z_R1_F) * 0.5 - 0.2, (Z_RDC_F + Z_R1_F) * 0.5, "Mat_Arch_Beton_Brut", collider=True)
    rdc_s.add_stair_flight("X", 26.0, 2.2, 25.2, 21.0, (Z_RDC_F + Z_R1_F) * 0.5, Z_R1_F, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")

    rdc_s.add_wall_with_openings("Y", 22.5, 0.20, -7.7, 22.0, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -3.0, "s1": -1.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},  # Bureau Imam
        {"s0": 6.0, "s1": 7.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},    # Caisse / Zakat
        {"s0": 15.0, "s1": 16.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},  # Bibliotheque
    ])
    rdc_s.add_wall_with_openings("X", 3.0, 0.20, 22.5, 30.7, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("X", 12.0, 0.20, 22.5, 30.7, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("X", 22.0, 0.20, 20.0, 30.7, Z_RDC_F, Z_WING_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 20.3, "s1": 22.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "open"},
    ])
    # Imam Suite, Caisse Zakat + Safe, Library
    amc.add_office_desk(rdc_f, 26.8, -2.5, Z_RDC_F, facing="-Y", width=2.2, executive=True)
    amc.add_sofa_lounge(rdc_f, 26.8, -5.8, Z_RDC_F, fabric_mat="Mat_Arch_Mosquee_Vert")
    amc.add_reception_or_caisse_counter(rdc_f, 25.0, 7.2, Z_RDC_F, length=4.2, axis="X", facing="-Y", num_windows=2, armored_glass=True)
    amc.add_safe_vault(rdc_f, 29.5, 10.5, Z_RDC_F, facing="-Y", size=(1.4, 1.1, 1.9))
    for bx in (24.5, 28.5):
        amc.add_armory_rack(rdc_f, bx, 17.0, Z_RDC_F, length=5.5, axis="Y")

    # East R+1: Islamic Council & Conference Hall
    amc.add_slab_with_cutouts(r1_s, 20.0, 30.7, -7.7, 27.7, Z_WING_C, Z_R1, wing_e_cut, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, 20.0, 30.7, -7.7, 27.7, Z_R1, Z_R1_F, wing_e_cut, "Mat_Arch_Sol_Parquet", collider=False)
    r1_s.add_glass_railing("Y", 27.0, 22.0, 27.7, Z_R1_F)
    r1_s.add_wall_with_openings("Y", 22.5, 0.20, -7.7, 22.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 6.0, "s1": 8.0, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "door"},
    ])
    r1_s.add_wall_with_openings("X", 22.0, 0.20, 20.0, 30.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 20.3, "s1": 22.2, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "open"},
    ])
    amc.add_conference_table(r1_f, 26.8, 7.0, Z_R1_F, length=12.0, width=2.5, axis="Y", num_chairs_side=10, chair_mat="Mat_Arch_Mosquee_Vert")
    amc.add_podium_lectern(r1_f, 26.8, 17.5, Z_R1_F, facing="-Y")

    amc.add_ceiling_lights_grid(rdc_s, -18.0, 18.0, 3.5, 26.0, Z_HALL_C, nx=4, ny=3)
    amc.add_ceiling_lights_grid(rdc_s, -29.5, -21.0, -6.5, 20.0, Z_WING_C, nx=1, ny=4)
    amc.add_ceiling_lights_grid(rdc_s, 23.0, 29.8, -6.5, 20.0, Z_WING_C, nx=1, ny=4)
    point_lights.extend([
        (0.0, 10.0, Z_HALL_ROOF - 1.5, 460.0, 6.0),
        (0.0, 22.0, 8.0, 240.0, 3.5),
        (-25.5, 4.0, Z_WING_C - 0.6, 160.0, 2.2),
        (26.5, 7.0, Z_R1_C - 0.6, 170.0, 2.4),
    ])

    for dname, piv, bnds, dmat, note, is_r1 in [
        ("Door_Salle_Priere_W1", (-19.83, -4.0, Z_RDC_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.70), "Mat_Arch_Bois_Interieur", "Priere vers Ablutions", False),
        ("Door_Salle_Priere_W2", (-19.83, 12.0, Z_RDC_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.70), "Mat_Arch_Bois_Interieur", "Priere vers Sanitaires", False),
        ("Door_Salle_Priere_E1", (19.83, -4.0, Z_RDC_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.70), "Mat_Arch_Bois_Interieur", "Priere vers Accueil / Imam", False),
        ("Door_Salle_Priere_E2", (19.83, 12.0, Z_RDC_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.70), "Mat_Arch_Bois_Interieur", "Priere vers Bibliotheque", False),
        ("Door_Imam_RDC", (22.5, -3.0, Z_RDC_F), (-0.03, 0.03, 0.0, 1.45, 0.0, 2.35), "Mat_Arch_Bois_Interieur", "Bureau de l'Imam", False),
        ("Door_Caisse_Mosquee_RDC", (22.5, 6.0, Z_RDC_F), (-0.03, 0.03, 0.0, 1.45, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", "Caisse / Zakat / Dons", False),
        ("Door_Bibliotheque_Mosq", (22.5, 15.0, Z_RDC_F), (-0.03, 0.03, 0.0, 1.55, 0.0, 2.35), "Mat_Arch_Bois_Interieur", "Bibliotheque Islamique", False),
        ("Door_Galerie_Femmes_O", (-19.83, -4.0, Z_R1_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.50), "Mat_Arch_Bois_Interieur", "Galerie Femmes Ouest", True),
        ("Door_Galerie_Femmes_E", (19.83, -4.0, Z_R1_F), (-0.03, 0.03, 0.0, 2.15, 0.0, 2.50), "Mat_Arch_Bois_Interieur", "Galerie Femmes Est", True),
        ("Door_Ecole_Coranique_R1", (-22.5, 0.0, Z_R1_F), (-0.03, 0.03, 0.0, 1.55, 0.0, 2.30), "Mat_Arch_Bois_Interieur", "Ecole Coranique", True),
        ("Door_Reunion_Mosquee_R1", (-22.5, 13.0, Z_R1_F), (-0.03, 0.03, 0.0, 1.55, 0.0, 2.30), "Mat_Arch_Bois_Interieur", "Salle Reunion Communaute", True),
        ("Door_Conseil_Islam_R1", (22.5, 6.0, Z_R1_F), (-0.03, 0.03, 0.0, 1.95, 0.0, 2.45), "Mat_Arch_Bois_Interieur", "Salle Conference / Conseil", True),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, open_note=note)
        if is_r1:
            r1_doors.append(o.name)

    for b in (rdc_s, rdc_f, r1_s, r1_f):
        b.build_object(parent=int_root)

    int_colliders = rdc_s.colliders + rdc_f.colliders + r1_s.colliders + r1_f.colliders
    return int_root, int_colliders, door_specs, door_objs, r1_names + r1_doors, point_lights


def main():
    amc.register_monument_materials()
    ac.reset_scene()

    ext_root, ext_cols, ext_doors_meta, _, roof_names = build_exterior()
    int_root, int_cols, int_doors_meta, _, hide_rdc, lights = build_interior()

    os.makedirs(ARCH_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    ac.export_objects_fbx([ext_root] + list(ext_root.children_recursive), EXT_FBX_PATH)
    ac.export_objects_fbx([int_root] + list(int_root.children_recursive), INT_FBX_PATH)

    manifest = {
        "name": "Grande_Mosquee",
        "lot_meters": [68.0, 74.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Grande_Mosquee.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Grande_Mosquee_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors_meta,
        "interior_doors": int_doors_meta,
    }
    ac.save_collider_manifest(MANIFEST_PATH, manifest)

    preview_dir = sys.argv[sys.argv.index("--") + 1] if ("--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1) else None
    if preview_dir:
        cam_specs = [
            ("ext_front_3q", (54.0, -64.0, 34.0), (0.0, 0.0, 12.0), None),
            ("ext_rear_3q", (-54.0, 56.0, 28.0), (0.0, 6.0, 10.0), None),
            ("cutaway_rdc", (42.0, -42.0, 52.0), (0.0, 6.0, 2.0), roof_names + hide_rdc),
            ("int_priere_mihrab", (-7.5, 4.0, 5.2), (0.0, 16.0, 7.2), None),
            ("int_ablutions", (-25.5, -6.5, 2.8), (-25.5, 11.0, 1.4), None),
        ]
        amc.render_monument_previews(preview_dir, "mosquee", cam_specs, interior_point_lights=lights)

    print(
        f"[Grande_Mosquee] Built OK: ext_colliders={len(ext_cols)}, "
        f"int_colliders={len(int_cols)}, ext_doors={len(ext_doors_meta)}, "
        f"int_doors={len(int_doors_meta)}"
    )


if __name__ == "__main__":
    main()
