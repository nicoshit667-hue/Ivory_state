"""Build script for Mairie_Plateau in Blender (5.3+).

Generates a grand Abidjan-Plateau-inspired City Hall (Hotel de Ville):
- Compound: 66.0 m x 58.0 m (3,828 m2) with open civic parvis, fountain basin,
  flagpoles, staff parking gate & rear parking, and royal palms.
- Building: 50.0 m x 28.0 m across 3 levels (RDC, R+1, R+2 = ~4,200 m2) with
  quartz-stone colonnade, proclamation balcony, modernist brise-soleil fins,
  and a 22.5 m central Civic Clock Tower / Belfry.
- Complete interior rooms:
  - RDC: Grand Hall & Orientation Desk, Civil Registry (Etat Civil), Municipal
    Treasury / Caisse with armored teller bays & safe, Legalization & Passports,
    Municipal Archives & Cadastre, Municipal Police Post, Restrooms.
  - R+1: Grand Wedding & Ceremony Hall (Salle des Mariages) with red carpet,
    Mayor's ceremony desk & guest pews, Monumental City Council / Meeting Hall
    (Conseil Municipal), Mayor's Suite & VIP Lounge, Deputy Offices.
  - R+2: Urban Planning & Public Works, Commission Meeting Room, Finance &
    Accounting, Staff Cafeteria & Reception Salon.

Run with:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/architecture/build_mairie_plateau.py [-- <preview_output_dir>]
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
BLEND_PATH = os.path.join(ARCH_DIR, "Mairie_Plateau.blend")
MANIFEST_PATH = os.path.join(ARCH_DIR, "mairie_plateau_manifest.json")
EXT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Monuments", "SM_Mairie_Plateau.fbx"
)
INT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Interieurs", "SM_Mairie_Plateau_Interieur.fbx"
)

Z_RDC = 0.30
Z_RDC_F = 0.34
Z_RDC_C = 4.45
Z_R1 = 4.75
Z_R1_F = 4.79
Z_R1_C = 8.75
Z_R2 = 9.05
Z_R2_F = 9.09
Z_R2_C = 12.85
Z_ROOF = 13.15
Z_PARAPET = 14.25

SLAB_CUTOUTS = [
    (-7.5, -4.5, 2.0, 5.5),    # Central elevator shaft
    (-4.5, 1.5, 2.0, 9.5),     # Main U-stairwell
    (18.5, 24.5, 2.0, 9.5),    # East stairwell
]


def _make_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, bounds, mat_panel, mat_frame="Mat_Arch_Or_Ceremonie",
    glass_bounds=None, rot_deg=(0.0, 0.0, 0.0), offset=(0.0, 0.0, 0.0),
    is_sliding=False, slide_axis="X", open_note="",
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
        offset_world=offset,
        is_sliding=is_sliding,
        slide_axis=slide_axis,
        open_note=open_note,
    )
    obj = b.build_object(parent=parent, default_rot_deg=drot)
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def _build_stair_cores(b, z0, z1):
    z_mid = (z0 + z1) * 0.5
    for sx0, f1_x, f2_x in ((-4.5, -3.15, -0.15), (18.5, 19.85, 22.85)):
        sx1 = sx0 + 6.0
        b.add_stair_flight("Y", f1_x, 2.4, 2.2, 6.8, z0, z_mid, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")
        b.add_box_bounds(sx0, sx1, 6.8, 9.5, z_mid - 0.22, z_mid, "Mat_Arch_Beton_Brut", collider=True)
        b.add_box_bounds(sx0, sx1, 6.8, 9.5, z_mid, z_mid + 0.04, "Mat_Arch_Sol_Marbre")
        b.add_stair_flight("Y", f2_x, 2.4, 6.8, 2.2, z_mid, z1, 11, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")

    # Elevator (-7.5..-4.5, 2.0..5.5) opening South
    b.add_wall_with_openings("Y", -7.40, 0.20, 2.0, 5.5, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("Y", -4.60, 0.20, 2.0, 5.5, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("X", 5.40, 0.20, -7.5, -4.5, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings(
        "X", 2.10, 0.20, -7.5, -4.5, z0, z1, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -6.65, "s1": -5.35, "z0": z0, "z1": z0 + 2.30, "kind": "door"}],
    )


def build_exterior():
    col = "Mairie_Exterieur"
    ext_root = amc.make_empty_root("Mairie_Plateau_Ext", col)

    site = ac.ArchMeshBuilder("Mairie_Ext_Site", collection_name=col)
    walls = ac.ArchMeshBuilder("Mairie_Ext_Walls", collection_name=col)
    roof_b = ac.ArchMeshBuilder("Mairie_Ext_Roof", collection_name=col)
    det = ac.ArchMeshBuilder("Mairie_Ext_Details", collection_name=col)

    # 1. Civic Parvis, Fountain, Rear Parking & Perimeter Walls
    site.add_box_bounds(-33.0, 33.0, -29.0, 29.0, -0.15, 0.0, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)
    site.add_box_bounds(-31.5, -14.0, -27.5, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(14.0, 24.0, -27.5, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    for i in range(8):
        site.add_box_center(-18.0 + i * 5.0, 24.5, 0.015, 0.14, 5.0, 0.02, "Mat_Arch_MurExt_Blanc")

    # Civic Fountain in front plaza (X = 0, Y = -21.5)
    det.add_cylinder(0.0, -21.5, 0.0, 0.48, 3.8, 20, "Mat_Arch_Parement_Pierre")
    det.add_cylinder(0.0, -21.5, 0.10, 0.42, 3.3, 20, "Mat_Arch_Eau_Bassin")
    det.add_cylinder(0.0, -21.5, 0.0, 1.85, 0.65, 12, "Mat_Arch_Sol_Marbre")
    det.add_collider_center(0.0, -21.5, 0.8, 7.6, 7.6, 1.6)

    # Podium & ceremonial steps
    site.add_box_bounds(-25.0, 25.0, -10.0, 18.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.45, collider=True)
    site.add_box_bounds(-10.5, 10.5, -13.6, -10.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_box_bounds(-8.5, 8.5, -14.8, -13.6, 0.0, 0.16, "Mat_Arch_Sol_Marbre", collider=True)

    # Low stone perimeter walls (open front civic plaza X = -22..+22)
    site.add_wall_with_openings("Y", -32.85, 0.30, -29.0, 29.0, 0.0, 1.65, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("Y", 32.85, 0.30, -29.0, 29.0, 0.0, 1.65, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("X", 28.85, 0.30, -33.0, 33.0, 0.0, 1.65, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("X", -28.85, 0.30, -32.7, -22.0, 0.0, 1.45, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("X", -28.85, 0.30, 22.0, 25.0, 0.0, 1.45, "Mat_Arch_Parement_Pierre", collider=True)

    # Flagpoles, palms, lamps
    amc.add_flagpole_ivorian(det, -11.5, -15.5, 0.0, height=11.5, flag_dir="+X")
    amc.add_flagpole_ivorian(det, 11.5, -15.5, 0.0, height=11.5, flag_dir="-X")
    for px, py in [(-24.0, -22.0), (-18.0, -22.0), (18.0, -22.0), (-24.0, -16.0), (18.0, -16.0)]:
        amc.add_palm_tree(det, px, py, 0.0, height=6.6)
    for lx, ly in [(-13.5, -24.0), (13.5, -24.0), (-25.0, 21.0), (25.0, 21.0)]:
        amc.add_streetlamp(det, lx, ly, 0.0, height=5.4)

    # 2. Shell Walls across 3 levels (X = -25..+25, Y = -10..+18)
    for z0, z1, is_rdc in ((Z_RDC, Z_RDC_C, True), (Z_R1, Z_R1_C, False), (Z_R2, Z_R2_C, False)):
        f_ops = [
            {"s0": -22.5, "s1": -14.5, "z0": z0 + 0.75, "z1": z1 - 0.50, "kind": "window"},
            {"s0": -12.0, "s1": -5.5, "z0": z0 + 0.75, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 5.5, "s1": 12.0, "z0": z0 + 0.75, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 14.5, "s1": 22.5, "z0": z0 + 0.75, "z1": z1 - 0.50, "kind": "window"},
        ]
        if is_rdc:
            f_ops.append({"s0": -2.4, "s1": 2.4, "z0": z0, "z1": z0 + 3.25, "kind": "door"})
        elif z0 == Z_R1:
            f_ops.append({"s0": -2.0, "s1": 2.0, "z0": z0, "z1": z0 + 3.00, "kind": "door"})
        else:
            f_ops.append({"s0": -3.5, "s1": 3.5, "z0": z0 + 0.75, "z1": z1 - 0.50, "kind": "window"})

        r_ops = [
            {"s0": -22.0, "s1": -14.0, "z0": z0 + 0.80, "z1": z1 - 0.50, "kind": "window"},
            {"s0": -11.5, "s1": -5.5, "z0": z0 + 0.80, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 3.5, "s1": 11.0, "z0": z0 + 0.80, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 13.5, "s1": 17.5, "z0": z0 + (1.70 if is_rdc else 0.80), "z1": z1 - 0.50, "kind": "window"},
        ]
        if is_rdc:
            r_ops.append({"s0": -2.0, "s1": 2.0, "z0": z0, "z1": z0 + 2.60, "kind": "door"})
        s_ops = [
            {"s0": -8.0, "s1": -2.5, "z0": z0 + 0.80, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 4.0, "s1": 14.5, "z0": z0 + 0.80, "z1": z1 - 0.50, "kind": "window"},
        ]
        walls.add_wall_with_openings("X", -9.83, 0.34, -25.0, 25.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=f_ops)
        walls.add_wall_with_openings("X", 17.83, 0.34, -25.0, 25.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=r_ops)
        walls.add_wall_with_openings("Y", -24.83, 0.34, -10.0, 18.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=s_ops)
        walls.add_wall_with_openings("Y", 24.83, 0.34, -10.0, 18.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=s_ops)
        for ax_type, fixed, olist in (("X", -9.83, f_ops), ("X", 17.83, r_ops), ("Y", -24.83, s_ops), ("Y", 24.83, s_ops)):
            for op in olist:
                if op["kind"] == "window" and (op["s1"] - op["s0"]) >= 3.0:
                    amc.add_window_mullions(walls, ax_type, fixed, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=max(2, int(round((op["s1"] - op["s0"]) / 2.2))), mat_name="Mat_Arch_Or_Ceremonie")

    for sz0, sz1 in ((Z_RDC_C, Z_R1), (Z_R1_C, Z_R2), (Z_R2_C, Z_ROOF)):
        walls.add_box_bounds(-25.25, 25.25, -10.25, -9.98, sz0, sz1, "Mat_Arch_Parement_Pierre")
        walls.add_box_bounds(-25.25, 25.25, 17.98, 18.25, sz0, sz1, "Mat_Arch_Parement_Pierre")
        walls.add_box_bounds(-25.25, -24.98, -10.0, 18.0, sz0, sz1, "Mat_Arch_Parement_Pierre")
        walls.add_box_bounds(24.98, 25.25, -10.0, 18.0, sz0, sz1, "Mat_Arch_Parement_Pierre")

    # 3. Colonnade Portico, Proclamation Balcony & Brise-Soleil Fins
    for px in (-10.0, -6.2, -2.8, 2.8, 6.2, 10.0):
        amc.add_colonnade_pillar(det, px, -12.8, Z_RDC, Z_R1_C, radius=0.40)
    det.add_box_bounds(-10.8, 10.8, -13.4, -10.0, Z_RDC_C, Z_R1_F, "Mat_Arch_Sol_Marbre", collider=True)
    det.add_glass_railing("X", -13.3, -10.8, 10.8, Z_R1_F, post_mat="Mat_Arch_Or_Ceremonie")
    det.add_box_bounds(-11.0, 11.0, -13.6, -10.0, Z_R1_C, Z_R2, "Mat_Arch_Parement_Pierre", collider=True)
    for fx in (-24.0, -19.0, -14.0, 14.0, 19.0, 24.0):
        det.add_box_center(fx, -10.25, (Z_R1 + Z_R2_C) * 0.5, 0.24, 0.50, Z_R2_C - Z_R1, "Mat_Arch_Parement_Pierre")

    # 4. Roof & Civic Clock Tower / Belfry (rising to Z = 22.5m)
    roof_b.add_box_bounds(-25.0, 25.0, -10.0, 18.0, Z_R2_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    roof_b.add_wall_with_openings("X", -9.85, 0.30, -25.0, 25.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", 17.85, 0.30, -25.0, 25.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", -24.85, 0.30, -10.0, 18.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", 24.85, 0.30, -10.0, 18.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    # Belfry / Clock Tower centered at (0, -7.2)
    roof_b.add_box_bounds(-3.2, 3.2, -9.8, -4.4, Z_ROOF, 20.8, "Mat_Arch_Parement_Pierre", collider=True)
    roof_b.add_box_bounds(-3.4, 3.4, -10.0, -4.2, 20.8, 21.4, "Mat_Arch_Or_Ceremonie")
    roof_b.add_cone(0.0, -7.1, 21.4, 24.0, 2.8, 0.25, 4, "Mat_Arch_Metal_Anthracite")
    # Illuminated Clock Dial Faces on 4 sides of Belfry
    for dx, dy, rot in ((0.0, -2.75, (1.5708, 0.0, 0.0)), (0.0, 2.75, (1.5708, 0.0, 0.0)), (-3.25, 0.0, (0.0, 1.5708, 0.0)), (3.25, 0.0, (0.0, 1.5708, 0.0))):
        roof_b.add_cylinder(dx, -7.1 + dy, 18.4, 18.65, 1.35, 16, "Mat_Arch_Lumiere_Nuit", rot_euler=rot)

    site.build_object(parent=ext_root)
    walls.build_object(parent=ext_root)
    roof_obj = roof_b.build_object(parent=ext_root)
    det.build_object(parent=ext_root)

    # 5. Exterior Doors & Parking Gate
    door_specs, door_objs = [], []
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Mairie_Parking", pivot=(25.0, -28.85, 0.0),
        bounds=(0.0, 6.5, -0.07, 0.07, 0.05, 1.85),
        mat_panel="Mat_Arch_Metal_Anthracite", offset=(-4.5, 0.0, 0.0),
        is_sliding=True, slide_axis="X", open_note="Portail parking personnel Mairie",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mairie_Entree_L", pivot=(-2.4, -9.83, Z_RDC_F),
        bounds=(0.0, 2.36, -0.05, 0.05, 0.0, 3.15),
        mat_panel="Mat_Arch_Or_Ceremonie", glass_bounds=(0.30, 2.06, -0.02, 0.02, 0.35, 2.85),
        rot_deg=(0.0, 0.0, 25.0), open_note="Entree principale Hotel de Ville G",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mairie_Entree_R", pivot=(2.4, -9.83, Z_RDC_F),
        bounds=(-2.36, 0.0, -0.05, 0.05, 0.0, 3.15),
        mat_panel="Mat_Arch_Or_Ceremonie", glass_bounds=(-2.06, -0.30, -0.02, 0.02, 0.35, 2.85),
        rot_deg=(0.0, 0.0, -25.0), open_note="Entree principale Hotel de Ville D",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mairie_Balcon_L", pivot=(-2.0, -9.83, Z_R1_F),
        bounds=(0.0, 1.96, -0.04, 0.04, 0.0, 2.90),
        mat_panel="Mat_Arch_Bois_Exterieur", glass_bounds=(0.25, 1.70, -0.015, 0.015, 0.30, 2.65),
        open_note="Acces balcon officiel G",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Mairie_Balcon_R", pivot=(2.0, -9.83, Z_R1_F),
        bounds=(-1.96, 0.0, -0.04, 0.04, 0.0, 2.90),
        mat_panel="Mat_Arch_Bois_Exterieur", glass_bounds=(-1.70, -0.25, -0.015, 0.015, 0.30, 2.65),
        open_note="Acces balcon officiel D",
    )

    ext_colliders = site.colliders + walls.colliders + roof_b.colliders + det.colliders
    return ext_root, ext_colliders, door_specs, door_objs, [roof_obj.name]


def build_interior():
    col = "Mairie_Interieur"
    int_root = amc.make_empty_root("Mairie_Plateau_Int", col)

    rdc_s = ac.ArchMeshBuilder("Mairie_Int_RDC_Structure", collection_name=col)
    rdc_f = ac.ArchMeshBuilder("Mairie_Int_RDC_Mobilier", collection_name=col)
    r1_s = ac.ArchMeshBuilder("Mairie_Int_R1_Structure", collection_name=col)
    r1_f = ac.ArchMeshBuilder("Mairie_Int_R1_Mobilier", collection_name=col)
    r2_s = ac.ArchMeshBuilder("Mairie_Int_R2_Structure", collection_name=col)
    r2_f = ac.ArchMeshBuilder("Mairie_Int_R2_Mobilier", collection_name=col)

    door_specs, door_objs = [], []
    r1_names = ["Mairie_Int_R1_Structure", "Mairie_Int_R1_Mobilier"]
    r2_names = ["Mairie_Int_R2_Structure", "Mairie_Int_R2_Mobilier"]
    r1_doors, r2_doors = [], []
    point_lights = []

    # ==================================================================
    # RDC (Z = 0.30 .. 4.45 m) — Hall, Etat Civil, Caisse Municipale, Archives
    # ==================================================================
    rdc_s.add_box_bounds(-24.7, 24.7, -9.7, 17.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Marbre", uv_scale=0.45, collider=True)
    _build_stair_cores(rdc_s, Z_RDC_F, Z_R1_F)

    # Central Corridor Y = -1.5..+2.0
    rdc_s.add_wall_with_openings("X", -1.5, 0.20, -24.7, 24.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -18.0, "s1": -15.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.60, "kind": "open"},   # Etat Civil
        {"s0": -4.0, "s1": 4.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.10, "kind": "open"},      # Hall arch
        {"s0": 12.0, "s1": 13.4, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},    # Caisse access
        {"s0": 20.5, "s1": 22.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},    # Passeports
    ])
    rdc_s.add_wall_with_openings("X", 2.0, 0.20, -24.7, -7.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -17.0, "s1": -15.4, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},  # Archives
    ])
    rdc_s.add_wall_with_openings("X", 2.0, 0.20, 1.5, 18.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 5.5, "s1": 7.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},      # Police Municipale
        {"s0": 14.5, "s1": 15.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},    # Sanitaires
    ])
    rdc_s.add_wall_with_openings("Y", -9.0, 0.22, -9.7, -1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -7.2, "s1": -3.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.75, "kind": "open"},
    ])
    # Caisse armored teller wall (X = +9.0, facing Public Hall)
    rdc_s.add_wall_with_openings("Y", 9.0, 0.22, -9.7, -1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -8.2, "s1": -3.2, "z0": Z_RDC_F + 1.05, "z1": Z_RDC_F + 2.35, "kind": "open"},
    ])
    rdc_s.add_wall_with_openings("Y", 18.0, 0.20, -9.7, -1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 12.5, 0.20, 2.0, 17.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")

    # RDC Furniture
    amc.add_reception_or_caisse_counter(rdc_f, 0.0, -4.5, Z_RDC_F, length=5.2, axis="X", facing="-Y", num_windows=3, armored_glass=False)
    for ex in (-21.0, -16.5, -12.0):
        amc.add_office_desk(rdc_f, ex, -5.8, Z_RDC_F, facing="+Y", width=1.8)
    # Caisse / Recette Municipale (+9..+18)
    amc.add_reception_or_caisse_counter(rdc_f, 9.45, -5.7, Z_RDC_F, length=4.8, axis="Y", facing="-X", num_windows=3, armored_glass=True)
    amc.add_office_desk(rdc_f, 13.5, -5.5, Z_RDC_F, facing="-X", width=1.8)
    amc.add_safe_vault(rdc_f, 16.5, -8.3, Z_RDC_F, facing="-Y", size=(1.6, 1.2, 2.1))
    amc.add_office_desk(rdc_f, 21.5, -5.8, Z_RDC_F, facing="+Y", width=1.8)
    # Archives (-24.7..-7.5, +2..+17.7)
    for ax in (-22.0, -17.5, -13.0):
        amc.add_armory_rack(rdc_f, ax, 10.5, Z_RDC_F, length=7.0, axis="Y")
    # Municipal Police & Restrooms
    amc.add_cctv_wall(rdc_f, 7.0, 17.2, Z_RDC_F, axis="X", width=4.8, rows=2, cols=3)
    amc.add_office_desk(rdc_f, 7.0, 12.5, Z_RDC_F, facing="+Y", width=1.9)
    amc.add_locker_bank(rdc_f, 3.0, 11.5, Z_RDC_F, count=6, axis="Y")
    amc.add_bathroom_fixtures(rdc_f, 12.8, 18.2, 3.0, 14.5, Z_RDC_F, num_stalls=3)

    amc.add_ceiling_lights_grid(rdc_s, -23.5, 23.5, -9.0, 16.8, Z_RDC_C, nx=5, ny=3)
    point_lights.extend([
        (0.0, -5.5, Z_RDC_C - 0.5, 190.0, 2.5),
        (13.5, -5.5, Z_RDC_C - 0.5, 140.0, 2.0),
        (-16.5, -5.5, Z_RDC_C - 0.5, 150.0, 2.2),
    ])

    for dname, piv, bnds, dmat, note in [
        ("Door_Caisse_Mairie", (12.0, -1.5, Z_RDC_F), (0.0, 1.35, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", "Recette Municipale / Caisse"),
        ("Door_Passeports_RDC", (20.5, -1.5, Z_RDC_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", "Legalisations / Passeports"),
        ("Door_Archives_Mairie", (-17.0, 2.0, Z_RDC_F), (0.0, 1.55, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Metal_Anthracite", "Archives & Cadastre"),
        ("Door_Police_Mun_RDC", (5.5, 2.0, Z_RDC_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Police_Bleu", "Police Municipale"),
        ("Door_Sanitaires_Mairie", (14.5, 2.0, Z_RDC_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", "Sanitaires Publics"),
        ("Door_Asc_Mairie_RDC", (-6.65, 2.10, Z_RDC_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Metal_Inox", "Ascenseur RDC"),
    ]:
        _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, open_note=note)

    # ==================================================================
    # R+1 (Z = 4.75 .. 8.75 m) — Salle des Mariages, Conseil Municipal, Bureau du Maire
    # ==================================================================
    amc.add_slab_with_cutouts(r1_s, -24.7, 24.7, -9.7, 17.7, Z_RDC_C, Z_R1, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -24.7, 24.7, -9.7, 17.7, Z_R1, Z_R1_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)
    _build_stair_cores(r1_s, Z_R1_F, Z_R2_F)
    for sx in (-4.5, 1.5, 18.5, 24.5):
        r1_s.add_glass_railing("Y", sx, 2.0, 9.5, Z_R1_F, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 9.5, -4.5, 1.5, Z_R1_F, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 9.5, 18.5, 24.5, Z_R1_F, post_mat="Mat_Arch_Or_Ceremonie")

    r1_s.add_wall_with_openings("X", -1.5, 0.20, -24.7, 24.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -16.0, "s1": -13.6, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "door"},  # Salle des Mariages
        {"s0": -4.0, "s1": 4.0, "z0": Z_R1_F, "z1": Z_R1_F + 3.00, "kind": "open"},
        {"s0": 13.6, "s1": 16.0, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "door"},    # Conseil Municipal
    ])
    r1_s.add_wall_with_openings("X", 2.0, 0.20, -24.7, -7.5, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -16.0, "s1": -13.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "door"},  # Bureau du Maire
    ])
    r1_s.add_wall_with_openings("X", 2.0, 0.20, 1.5, 18.5, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 5.5, "s1": 6.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.35, "kind": "door"},      # Adjoint
        {"s0": 13.0, "s1": 14.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.35, "kind": "door"},    # Secretariat Maire
    ])
    r1_s.add_wall_with_openings("Y", -5.0, 0.20, -9.7, -1.5, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings("Y", 5.0, 0.20, -9.7, -1.5, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings("Y", 10.5, 0.20, 2.0, 17.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")

    # Salle des Mariages (-24.7..-5.0, -9.7..-1.5)
    r1_f.add_box_center(-15.0, -5.6, Z_R1_F + 0.015, 13.0, 2.2, 0.02, "Mat_Arch_Tapis_Rouge")
    amc.add_conference_table(r1_f, -21.8, -5.6, Z_R1_F, length=4.2, width=1.4, axis="Y", num_chairs_side=2, chair_mat="Mat_Arch_Tapis_Rouge")
    for px in (-18.0, -15.5, -13.0, -10.5):
        amc.add_church_pew(r1_f, px, -3.2, Z_R1_F, length=2.8, axis="Y")
        amc.add_church_pew(r1_f, px, -8.0, Z_R1_F, length=2.8, axis="Y")
    amc.add_chandelier(r1_f, -15.0, -5.6, Z_R1_C, drop=1.1, radius=1.1)

    # Grande Salle du Conseil Municipal (+5.0..+24.7, -9.7..-1.5)
    amc.add_conference_table(
        r1_f, 15.0, -5.6, Z_R1_F, length=10.5, width=4.6, axis="X", num_chairs_side=9,
        table_mat="Mat_Arch_Bois_Interieur", chair_mat="Mat_Arch_Tapis_Rouge", hollow_center=True,
    )
    amc.add_podium_lectern(r1_f, 22.8, -5.6, Z_R1_F, facing="-Y")
    amc.add_cctv_wall(r1_f, 24.3, -5.6, Z_R1_F, axis="Y", width=5.0, rows=2, cols=3, facing_sign=-1.0)
    amc.add_chandelier(r1_f, 15.0, -5.6, Z_R1_C, drop=1.1, radius=1.2)

    # Bureau du Maire (-24.7..-7.5, +2..+17.7)
    r1_f.add_box_center(-16.0, 10.5, Z_R1_F + 0.015, 7.5, 5.5, 0.02, "Mat_Arch_Tapis_Rouge")
    amc.add_office_desk(r1_f, -16.0, 13.8, Z_R1_F, facing="-Y", width=2.6, depth=1.1, executive=True)
    amc.add_sofa_lounge(r1_f, -16.0, 8.2, Z_R1_F, fabric_mat="Mat_Arch_Tapis_Rouge")
    amc.add_flagpole_ivorian(r1_f, -21.5, 15.8, Z_R1_F, height=3.0, flag_dir="+X")
    amc.add_office_desk(r1_f, 6.0, 10.5, Z_R1_F, facing="-Y", width=2.0, executive=True)
    amc.add_office_desk(r1_f, 14.5, 10.5, Z_R1_F, facing="-Y", width=1.8)
    amc.add_ceiling_lights_grid(r1_s, -23.5, 23.5, -9.0, 16.8, Z_R1_C, nx=5, ny=3)
    point_lights.extend([(-15.0, -5.6, Z_R1_C - 0.5, 170.0, 2.4), (15.0, -5.6, Z_R1_C - 0.5, 190.0, 2.5), (-16.0, 11.0, Z_R1_C - 0.5, 160.0, 2.2)])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Mariages_R1_L", (-16.0, -1.5, Z_R1_F), (0.0, 1.18, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 25.0), "Salle des Mariages G"),
        ("Door_Mariages_R1_R", (-13.6, -1.5, Z_R1_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, -25.0), "Salle des Mariages D"),
        ("Door_Conseil_Mun_L", (13.6, -1.5, Z_R1_F), (0.0, 1.18, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 25.0), "Conseil Municipal G"),
        ("Door_Conseil_Mun_R", (16.0, -1.5, Z_R1_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Bois_Interieur", (0.0, 0.0, -25.0), "Conseil Municipal D"),
        ("Door_Maire_R1_L", (-16.0, 2.0, Z_R1_F), (0.0, 1.08, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 25.0), "Grand Bureau du Maire G"),
        ("Door_Maire_R1_R", (-13.8, 2.0, Z_R1_F), (-1.08, 0.0, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, -25.0), "Grand Bureau du Maire D"),
        ("Door_Adjoint_Mairie_R1", (5.5, 2.0, Z_R1_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureau Adjoint"),
        ("Door_Secretariat_Maire", (13.0, 2.0, Z_R1_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Secretariat du Maire"),
        ("Door_Asc_Mairie_R1", (-6.65, 2.10, Z_R1_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Ascenseur R+1"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)
        r1_doors.append(o.name)

    # ==================================================================
    # R+2 (Z = 9.05 .. 12.85 m) — Urbanisme, Commissions, Finances, Réfectoire
    # ==================================================================
    amc.add_slab_with_cutouts(r2_s, -24.7, 24.7, -9.7, 17.7, Z_R1_C, Z_R2, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, -24.7, 24.7, -9.7, 17.7, Z_R2, Z_R2_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    for sx in (-4.5, 1.5, 18.5, 24.5):
        r2_s.add_glass_railing("Y", sx, 2.0, 9.5, Z_R2_F)
    r2_s.add_wall_with_openings("Y", -7.40, 0.20, 2.0, 5.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings("Y", -4.60, 0.20, 2.0, 5.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings("X", 5.40, 0.20, -7.5, -4.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings("X", 2.10, 0.20, -7.5, -4.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut", openings=[
        {"s0": -6.65, "s1": -5.35, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
    ])

    r2_s.add_wall_with_openings("X", -1.5, 0.20, -24.7, 24.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -16.0, "s1": -14.2, "z0": Z_R2_F, "z1": Z_R2_F + 2.35, "kind": "door"},
        {"s0": 14.0, "s1": 16.0, "z0": Z_R2_F, "z1": Z_R2_F + 2.40, "kind": "door"},
    ])
    r2_s.add_wall_with_openings("X", 2.0, 0.20, -24.7, -7.5, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -16.0, "s1": -14.4, "z0": Z_R2_F, "z1": Z_R2_F + 2.35, "kind": "door"},
    ])
    r2_s.add_wall_with_openings("X", 2.0, 0.20, 1.5, 18.5, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 9.0, "s1": 10.8, "z0": Z_R2_F, "z1": Z_R2_F + 2.35, "kind": "door"},
    ])
    r2_s.add_wall_with_openings("Y", -5.0, 0.20, -9.7, -1.5, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")
    r2_s.add_wall_with_openings("Y", 5.0, 0.20, -9.7, -1.5, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")

    for ux in (-20.0, -14.5, -9.0):
        amc.add_office_desk(r2_f, ux, -5.8, Z_R2_F, facing="+Y", width=2.0)
    amc.add_conference_table(r2_f, 15.0, -5.6, Z_R2_F, length=8.5, width=2.2, axis="X", num_chairs_side=7)
    for fx in (-20.0, -14.0):
        amc.add_office_desk(r2_f, fx, 10.5, Z_R2_F, facing="-Y", width=1.8)
    for rx in (6.5, 13.5):
        r2_f.add_box_center(rx, 10.5, Z_R2_F + 0.74, 2.8, 1.2, 0.06, "Mat_Arch_Plan_Travail", collider=True)
    amc.add_ceiling_lights_grid(r2_s, -23.5, 23.5, -9.0, 16.8, Z_R2_C, nx=5, ny=3)

    for dname, piv, w, note in [
        ("Door_Urbanisme_R2", (-16.0, -1.5, Z_R2_F), 1.75, "Direction Urbanisme"),
        ("Door_Commissions_R2", (14.0, -1.5, Z_R2_F), 1.95, "Salle Commissions"),
        ("Door_Finances_R2", (-16.0, 2.0, Z_R2_F), 1.55, "Finances / Comptabilite"),
        ("Door_Refectoire_Mairie", (9.0, 2.0, Z_R2_F), 1.75, "Refectoire / Reception"),
        ("Door_Asc_Mairie_R2", (-6.65, 2.10, Z_R2_F), 1.25, "Ascenseur R+2"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, (0.0, w, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", open_note=note)
        r2_doors.append(o.name)

    for b in (rdc_s, rdc_f, r1_s, r1_f, r2_s, r2_f):
        b.build_object(parent=int_root)

    int_colliders = rdc_s.colliders + rdc_f.colliders + r1_s.colliders + r1_f.colliders + r2_s.colliders + r2_f.colliders
    return int_root, int_colliders, door_specs, door_objs, r1_names + r2_names + r1_doors + r2_doors, r2_names + r2_doors, point_lights


def main():
    amc.register_monument_materials()
    ac.reset_scene()

    ext_root, ext_cols, ext_doors_meta, _, roof_names = build_exterior()
    int_root, int_cols, int_doors_meta, _, hide_rdc, hide_r1, lights = build_interior()

    os.makedirs(ARCH_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    ac.export_objects_fbx([ext_root] + list(ext_root.children_recursive), EXT_FBX_PATH)
    ac.export_objects_fbx([int_root] + list(int_root.children_recursive), INT_FBX_PATH)

    manifest = {
        "name": "Mairie_Plateau",
        "lot_meters": [66.0, 58.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Mairie_Plateau.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Mairie_Plateau_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors_meta,
        "interior_doors": int_doors_meta,
    }
    ac.save_collider_manifest(MANIFEST_PATH, manifest)

    preview_dir = sys.argv[sys.argv.index("--") + 1] if ("--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1) else None
    if preview_dir:
        cam_specs = [
            ("ext_front_3q", (48.0, -52.0, 28.0), (0.0, -2.0, 6.5), None),
            ("cutaway_rdc", (34.0, -34.0, 40.0), (0.0, 2.0, 1.5), roof_names + hide_rdc),
            ("cutaway_r1", (34.0, -34.0, 44.0), (0.0, 2.0, 6.0), roof_names + hide_r1),
            ("int_hall_caisse", (-5.0, -8.5, 2.6), (11.5, -5.5, 1.8), None),
            ("int_mariages_r1", (-7.0, -5.6, 6.8), (-22.0, -5.6, 5.6), None),
            ("int_conseil_r1", (7.0, -5.6, 7.0), (22.0, -5.6, 5.6), None),
        ]
        amc.render_monument_previews(preview_dir, "mairie", cam_specs, interior_point_lights=lights)

    print(
        f"[Mairie_Plateau] Built OK: ext_colliders={len(ext_cols)}, "
        f"int_colliders={len(int_cols)}, ext_doors={len(ext_doors_meta)}, "
        f"int_doors={len(int_doors_meta)}"
    )


if __name__ == "__main__":
    main()
