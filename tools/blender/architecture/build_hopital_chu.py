"""Build script for Hopital_CHU in Blender (5.3+).

Generates a large-scale real-world Abidjan University Hospital Center (CHU):
- Compound: 74.0 m x 66.0 m (4,884 m2) with public entry plaza, security gates
  (main + dedicated ambulance gate), covered West ambulance drop-off canopy,
  ambulance & visitor parking, water tower, and generator.
- Hospital Building: 56.0 m x 32.0 m across 4 levels (RDC, R+1, R+2, R+3 = ~7,168 m2)
  plus rooftop SMUR helipad and illuminated 3D medical red cross emblems.
- Complete interior rooms across 4 levels:
  - RDC: Public Lobby & Triage Reception, Caisse / Billing & Treasury with
    armored teller bays & safe, Pharmacy with multi-tier shelving, Emergency /
    Shock-Trauma Bay & Exam Boxes, MRI & CT-Scan Medical Imaging Center,
    Analysis Laboratory & Blood Bank, Restrooms, Bed Elevators & Stairwells.
  - R+1: 3 Sterile Operating Rooms (Blocs Operatoires) with surgical tables &
    scialytic lamps, Intensive Care Unit (ICU / Reanimation), Post-Op Recovery,
    Hospital Director's Office & Secretariat, Grand Medical Staff Meeting Room.
  - R+2: 6 Inpatient Hospital Wards (Chambres d'Hospitalisation), Maternity
    & Pediatrics Wing, Nursing Station.
  - R+3: Medical University Amphitheater (CHU), Staff & Visitor Cafeteria,
    VIP Patient Suites.

Run with:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/architecture/build_hopital_chu.py [-- <preview_output_dir>]
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
BLEND_PATH = os.path.join(ARCH_DIR, "Hopital_CHU.blend")
MANIFEST_PATH = os.path.join(ARCH_DIR, "hopital_chu_manifest.json")
EXT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Monuments", "SM_Hopital_CHU.fbx"
)
INT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Interieurs", "SM_Hopital_CHU_Interieur.fbx"
)

# Vertical datum (meters)
Z_RDC = 0.25
Z_RDC_F = 0.29
Z_RDC_C = 4.25
Z_R1 = 4.55
Z_R1_F = 4.59
Z_R1_C = 8.35
Z_R2 = 8.65
Z_R2_F = 8.69
Z_R2_C = 12.45
Z_R3 = 12.75
Z_R3_F = 12.79
Z_R3_C = 16.55
Z_ROOF = 16.85
Z_PARAPET = 17.85

# Vertical circulation cutouts (identical across R+1..R+3)
SLAB_CUTOUTS = [
    (-9.0, -6.0, 5.0, 8.5),    # West bed elevator
    (-6.0, -1.5, 5.0, 12.0),   # West main stairwell
    (16.0, 20.5, 5.0, 12.0),   # East stairwell
    (20.5, 23.5, 5.0, 8.5),    # East bed elevator
]


def _make_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, bounds, mat_panel, mat_frame="Mat_Arch_Metal_Inox",
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


def _build_stair_and_elev_cores(b, z0, z1):
    """Build West & East staircases and bed-elevator enclosures from z0 to z1."""
    z_mid = (z0 + z1) * 0.5
    for sx0, f1_x, f2_x in ((-6.0, -4.85, -2.65), (16.0, 17.15, 19.35)):
        sx1 = sx0 + 4.5
        b.add_stair_flight("Y", f1_x, 2.0, 5.2, 9.4, z0, z_mid, 10, "Mat_Arch_Sol_Carrelage", "Mat_Arch_Beton_Brut")
        b.add_box_bounds(sx0, sx1, 9.4, 12.0, z_mid - 0.22, z_mid, "Mat_Arch_Beton_Brut", collider=True)
        b.add_box_bounds(sx0, sx1, 9.4, 12.0, z_mid, z_mid + 0.04, "Mat_Arch_Sol_Carrelage")
        b.add_stair_flight("Y", f2_x, 2.0, 9.4, 5.2, z_mid, z1, 10, "Mat_Arch_Sol_Carrelage", "Mat_Arch_Beton_Brut")

    for ex0, ex1 in ((-9.0, -6.0), (20.5, 23.5)):
        dc = (ex0 + ex1) * 0.5
        b.add_wall_with_openings("Y", ex0 + 0.10, 0.20, 5.0, 8.5, z0, z1, "Mat_Arch_Beton_Brut")
        b.add_wall_with_openings("Y", ex1 - 0.10, 0.20, 5.0, 8.5, z0, z1, "Mat_Arch_Beton_Brut")
        b.add_wall_with_openings("X", 8.40, 0.20, ex0, ex1, z0, z1, "Mat_Arch_Beton_Brut")
        b.add_wall_with_openings(
            "X", 5.10, 0.20, ex0, ex1, z0, z1, "Mat_Arch_Beton_Brut",
            openings=[{"s0": dc - 0.85, "s1": dc + 0.85, "z0": z0, "z1": z0 + 2.35, "kind": "door"}],
        )


def build_exterior():
    col = "Hopital_Exterieur"
    ext_root = amc.make_empty_root("Hopital_CHU_Ext", col)

    site = ac.ArchMeshBuilder("Hopital_Ext_Site", collection_name=col)
    walls = ac.ArchMeshBuilder("Hopital_Ext_Walls", collection_name=col)
    roof_b = ac.ArchMeshBuilder("Hopital_Ext_Roof", collection_name=col)
    det = ac.ArchMeshBuilder("Hopital_Ext_Details", collection_name=col)

    # ------------------------------------------------------------------
    # 1. Compound (74m x 66m), Ambulance Bay, Parking & Perimeter Wall
    # ------------------------------------------------------------------
    site.add_box_bounds(-37.0, 37.0, -33.0, 33.0, -0.15, 0.0, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)
    site.add_box_bounds(-24.0, -10.5, -31.5, -16.5, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(10.5, 35.5, -31.5, -16.5, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    # Ambulance lane red stripes at West Emergency Bay
    det.add_box_bounds(-35.2, -28.2, -28.0, -2.0, 0.005, 0.02, "Mat_Arch_Croix_Rouge")
    det.add_box_bounds(-34.6, -28.8, -28.0, -2.0, 0.015, 0.025, "Mat_Arch_Paves_Cour")
    # Rear visitor/staff parking bays
    for i in range(8):
        px = -16.0 + i * 4.8
        site.add_box_center(px, 27.5, 0.015, 0.14, 5.2, 0.02, "Mat_Arch_MurExt_Blanc")

    # Podium, steps, and PMR ramp
    site.add_box_bounds(-28.0, 28.0, -12.0, 20.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.45, collider=True)
    site.add_box_bounds(-9.0, 9.0, -16.0, -12.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_box_bounds(-7.5, 7.5, -17.2, -16.0, 0.0, 0.14, "Mat_Arch_Parement_Pierre", collider=True)

    # Perimeter walls (West, East, North, South with Main & Ambulance gates)
    site.add_wall_with_openings("Y", -36.85, 0.30, -33.0, 33.0, 0.0, 2.45, "Mat_Arch_MurExt_Blanc", collider=True)
    site.add_wall_with_openings("Y", 36.85, 0.30, -33.0, 33.0, 0.0, 2.45, "Mat_Arch_MurExt_Blanc", collider=True)
    site.add_wall_with_openings("X", 32.85, 0.30, -33.0, 33.0, 0.0, 2.45, "Mat_Arch_MurExt_Blanc", collider=True)
    for sx0, sx1 in ((-36.7, -34.5), (-27.5, -7.5), (7.5, 36.7)):
        site.add_wall_with_openings("X", -32.85, 0.30, sx0, sx1, 0.0, 2.45, "Mat_Arch_MurExt_Blanc", collider=True)
    # Guard booth near main gate
    site.add_box_bounds(7.6, 10.6, -32.0, -29.0, 0.0, 2.75, "Mat_Arch_Hopital_Teal", collider=True)
    site.add_box_bounds(7.4, 10.8, -32.2, -28.8, 2.75, 3.05, "Mat_Arch_MurExt_Blanc")

    # Palms, streetlamps, oxygen/water utilities
    for px, py in [(-21.0, -24.0), (-14.0, -24.0), (14.0, -24.0), (21.0, -24.0), (28.0, -24.0)]:
        amc.add_palm_tree(det, px, py, 0.0, height=6.8)
    for lx, ly in [(-10.5, -26.0), (10.5, -26.0), (-26.0, 23.0), (0.0, 23.0), (26.0, 23.0)]:
        amc.add_streetlamp(det, lx, ly, 0.0, height=5.4)
    # Medical oxygen tanks & backup generator (North-East service)
    det.add_cylinder(32.0, 24.0, 0.0, 5.8, 1.35, 16, "Mat_Arch_MurExt_Blanc")
    det.add_cylinder(32.0, 27.5, 0.0, 5.8, 1.35, 16, "Mat_Arch_MurExt_Blanc")
    det.add_box_bounds(26.0, 30.0, 23.0, 29.0, 0.0, 2.3, "Mat_Arch_Hopital_Teal", collider=True)

    # ------------------------------------------------------------------
    # 2. Main Building Shell (4 floors: X = -28..+28, Y = -12..+20)
    # ------------------------------------------------------------------
    levels = [
        (Z_RDC, Z_RDC_C, True),
        (Z_R1, Z_R1_C, False),
        (Z_R2, Z_R2_C, False),
        (Z_R3, Z_R3_C, False),
    ]
    for z0, z1, is_rdc in levels:
        f_ops = [
            {"s0": -25.5, "s1": -18.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": -16.0, "s1": -9.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 9.5, "s1": 16.0, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 18.5, "s1": 25.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
        ]
        if is_rdc:
            f_ops.extend([
                {"s0": -2.4, "s1": 2.4, "z0": z0, "z1": z0 + 3.15, "kind": "door"},
                {"s0": -7.2, "s1": -3.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
                {"s0": 3.5, "s1": 7.2, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            ])
        else:
            f_ops.extend([
                {"s0": -6.5, "s1": 6.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            ])
        r_ops = [
            {"s0": -25.5, "s1": -19.0, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": -16.5, "s1": -10.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 0.0, "s1": 7.0, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 9.0, "s1": 15.0, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 24.0, "s1": 27.0, "z0": z0 + (1.75 if is_rdc else 0.85), "z1": z1 - 0.50, "kind": "window"},
        ]
        w_ops = [
            {"s0": 6.5, "s1": 16.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
        ]
        if is_rdc:
            w_ops.append({"s0": -8.5, "s1": -5.5, "z0": z0, "z1": z0 + 3.00, "kind": "door"})  # Ambulance door
        else:
            w_ops.append({"s0": -9.5, "s1": -1.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"})
        e_ops = [
            {"s0": -9.5, "s1": -1.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
            {"s0": 6.5, "s1": 16.5, "z0": z0 + 0.85, "z1": z1 - 0.50, "kind": "window"},
        ]

        walls.add_wall_with_openings("X", -11.83, 0.34, -28.0, 28.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=f_ops)
        walls.add_wall_with_openings("X", 19.83, 0.34, -28.0, 28.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=r_ops)
        walls.add_wall_with_openings("Y", -27.83, 0.34, -12.0, 20.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=w_ops)
        walls.add_wall_with_openings("Y", 27.83, 0.34, -12.0, 20.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=e_ops)
        for ax_type, fixed, olist in (("X", -11.83, f_ops), ("X", 19.83, r_ops), ("Y", -27.83, w_ops), ("Y", 27.83, e_ops)):
            for op in olist:
                if op["kind"] == "window" and (op["s1"] - op["s0"]) >= 3.2:
                    amc.add_window_mullions(walls, ax_type, fixed, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=max(2, int(round((op["s1"] - op["s0"]) / 2.4))))

    # Teal institutional horizontal slab bands
    for sz0, sz1 in ((Z_RDC_C, Z_R1), (Z_R1_C, Z_R2), (Z_R2_C, Z_R3), (Z_R3_C, Z_ROOF)):
        walls.add_box_bounds(-28.25, 28.25, -12.25, -11.98, sz0, sz1, "Mat_Arch_Hopital_Teal")
        walls.add_box_bounds(-28.25, 28.25, 19.98, 20.25, sz0, sz1, "Mat_Arch_Hopital_Teal")
        walls.add_box_bounds(-28.25, -27.98, -12.0, 20.0, sz0, sz1, "Mat_Arch_Hopital_Teal")
        walls.add_box_bounds(27.98, 28.25, -12.0, 20.0, sz0, sz1, "Mat_Arch_Hopital_Teal")

    # ------------------------------------------------------------------
    # 3. Front Entrance Portico, Ambulance Bay Canopy & Medical Crosses
    # ------------------------------------------------------------------
    for px in (-8.0, -3.4, 3.4, 8.0):
        amc.add_colonnade_pillar(det, px, -15.6, Z_RDC, 4.45, radius=0.35, base_mat="Mat_Arch_Hopital_Teal", cap_mat="Mat_Arch_Hopital_Teal")
    det.add_box_bounds(-8.8, 8.8, -16.4, -12.0, 4.45, 5.15, "Mat_Arch_MurExt_Blanc", collider=True)
    det.add_box_bounds(-8.85, 8.85, -16.5, -16.35, 4.60, 5.00, "Mat_Arch_Hopital_Teal")
    # Glowing 3D Red Crosses (front portico + upper facade + ambulance canopy)
    amc.add_cross_emblem(det, 0.0, -16.55, 5.25, width=1.9, height=1.9, thickness=0.32, axis="X", mat_name="Mat_Arch_Lumiere_Croix")
    amc.add_cross_emblem(det, 0.0, -12.40, Z_R3 + 1.0, width=2.8, height=2.8, thickness=0.35, axis="X", mat_name="Mat_Arch_Lumiere_Croix")

    # West Emergency Ambulance Bay Canopy (-35.5..-28.0, -12.0..-2.0)
    for ay in (-11.2, -2.8):
        amc.add_colonnade_pillar(det, -35.0, ay, 0.0, 4.35, radius=0.32, base_mat="Mat_Arch_Croix_Rouge", cap_mat="Mat_Arch_Croix_Rouge")
    det.add_box_bounds(-35.6, -28.0, -12.2, -1.8, 4.35, 4.95, "Mat_Arch_MurExt_Blanc", collider=True)
    det.add_box_bounds(-35.68, -35.56, -12.2, -1.8, 4.48, 4.84, "Mat_Arch_Lumiere_Croix")

    # ------------------------------------------------------------------
    # 4. Roof, Parapet, Penthouse & SMUR Helipad
    # ------------------------------------------------------------------
    roof_b.add_box_bounds(-28.0, 28.0, -12.0, 20.0, Z_R3_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    roof_b.add_wall_with_openings("X", -11.85, 0.30, -28.0, 28.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", 19.85, 0.30, -28.0, 28.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", -27.85, 0.30, -12.0, 20.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", 27.85, 0.30, -12.0, 20.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_box_bounds(-9.2, -1.3, 5.0, 12.2, Z_ROOF, Z_ROOF + 2.8, "Mat_Arch_Beton_Brut", collider=True)
    roof_b.add_box_bounds(15.8, 23.7, 5.0, 12.2, Z_ROOF, Z_ROOF + 2.8, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_helipad_marking(roof_b, 8.0, 4.0, Z_ROOF, radius=6.4)

    site.build_object(parent=ext_root)
    walls.build_object(parent=ext_root)
    roof_obj = roof_b.build_object(parent=ext_root)
    det.build_object(parent=ext_root)

    # ------------------------------------------------------------------
    # 5. Movable Exterior Doors & Gates
    # ------------------------------------------------------------------
    door_specs, door_objs = [], []
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Hopital_Principal", pivot=(-7.5, -32.85, 0.0),
        bounds=(0.0, 15.0, -0.07, 0.07, 0.05, 2.30),
        mat_panel="Mat_Arch_Metal_Inox", offset=(-10.5, 0.0, 0.0),
        is_sliding=True, slide_axis="X", open_note="Portail principal visiteurs/personnel",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Hopital_Urgences", pivot=(-34.5, -32.85, 0.0),
        bounds=(0.0, 7.0, -0.07, 0.07, 0.05, 2.40),
        mat_panel="Mat_Arch_Croix_Rouge", offset=(-4.5, 0.0, 0.0),
        is_sliding=True, slide_axis="X", open_note="Acces rapide ambulances",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Hopital_Entree_L", pivot=(-2.4, -11.83, Z_RDC_F),
        bounds=(0.0, 2.36, -0.04, 0.04, 0.0, 3.05),
        mat_panel="Mat_Arch_Metal_Inox", glass_bounds=(0.20, 2.16, -0.015, 0.015, 0.20, 2.85),
        rot_deg=(0.0, 0.0, 25.0), open_note="Entree principale CHU gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Hopital_Entree_R", pivot=(2.4, -11.83, Z_RDC_F),
        bounds=(-2.36, 0.0, -0.04, 0.04, 0.0, 3.05),
        mat_panel="Mat_Arch_Metal_Inox", glass_bounds=(-2.16, -0.20, -0.015, 0.015, 0.20, 2.85),
        rot_deg=(0.0, 0.0, -25.0), open_note="Entree principale CHU droite",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Urgences_Ambulance", pivot=(-27.83, -8.5, Z_RDC_F),
        bounds=(-0.04, 0.04, 0.0, 2.95, 0.0, 2.90),
        mat_panel="Mat_Arch_Hopital_Teal", glass_bounds=(-0.015, 0.015, 0.25, 2.70, 0.50, 2.50),
        offset=(0.0, -2.5, 0.0), is_sliding=True, slide_axis="Y",
        open_note="SAS coulissant Urgences / Brancards",
    )

    ext_colliders = site.colliders + walls.colliders + roof_b.colliders + det.colliders
    return ext_root, ext_colliders, door_specs, door_objs, [roof_obj.name]


def build_interior():
    col = "Hopital_Interieur"
    int_root = amc.make_empty_root("Hopital_CHU_Int", col)

    rdc_s = ac.ArchMeshBuilder("Hopital_Int_RDC_Structure", collection_name=col)
    rdc_f = ac.ArchMeshBuilder("Hopital_Int_RDC_Mobilier", collection_name=col)
    r1_s = ac.ArchMeshBuilder("Hopital_Int_R1_Structure", collection_name=col)
    r1_f = ac.ArchMeshBuilder("Hopital_Int_R1_Mobilier", collection_name=col)
    r2_s = ac.ArchMeshBuilder("Hopital_Int_R2_Structure", collection_name=col)
    r2_f = ac.ArchMeshBuilder("Hopital_Int_R2_Mobilier", collection_name=col)
    r3_s = ac.ArchMeshBuilder("Hopital_Int_R3_Structure", collection_name=col)
    r3_f = ac.ArchMeshBuilder("Hopital_Int_R3_Mobilier", collection_name=col)

    door_specs, door_objs = [], []
    r1_names = ["Hopital_Int_R1_Structure", "Hopital_Int_R1_Mobilier"]
    r2_names = ["Hopital_Int_R2_Structure", "Hopital_Int_R2_Mobilier"]
    r3_names = ["Hopital_Int_R3_Structure", "Hopital_Int_R3_Mobilier"]
    r1_doors, r2_doors, r3_doors = [], [], []
    point_lights = []

    # ==================================================================
    # RDC (Z = 0.25 .. 4.25 m) — Accueil, Caisse, Pharmacie, Urgences, Imagerie, Labo
    # ==================================================================
    rdc_s.add_box_bounds(-27.7, 27.7, -11.7, 19.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Carrelage", uv_scale=0.5, collider=True)
    _build_stair_and_elev_cores(rdc_s, Z_RDC_F, Z_R1_F)

    # Central Corridor Y = +1.5..+5.0
    rdc_s.add_wall_with_openings("X", 1.5, 0.20, -27.7, 27.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -20.0, "s1": -17.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.45, "kind": "door"},  # Urgences link
        {"s0": -3.5, "s1": 3.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.00, "kind": "open"},     # Lobby arch
        {"s0": 10.5, "s1": 12.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},   # Caisse access
        {"s0": 20.5, "s1": 22.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"},   # Pharmacie
    ])
    rdc_s.add_wall_with_openings("X", 5.0, 0.20, -27.7, -9.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -24.5, "s1": -22.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},  # IRM
        {"s0": -14.8, "s1": -13.1, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},  # Scanner CT
    ])
    rdc_s.add_wall_with_openings("X", 5.0, 0.20, -1.5, 16.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": 2.5, "s1": 4.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},     # Labo
        {"s0": 11.0, "s1": 12.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},   # Banque sang
    ])
    rdc_s.add_wall_with_openings("X", 5.0, 0.20, 23.5, 27.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": 24.8, "s1": 26.0, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},   # Sanitaires
    ])
    # South room dividers
    rdc_s.add_wall_with_openings("Y", -8.0, 0.22, -11.7, 1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -6.0, "s1": -4.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.40, "kind": "door"},   # Triage Urgences
    ])
    # Caisse teller wall at X = +8.0 (guichets facing Lobby)
    rdc_s.add_wall_with_openings("Y", 8.0, 0.22, -11.7, 1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -9.2, "s1": -4.2, "z0": Z_RDC_F + 1.05, "z1": Z_RDC_F + 2.30, "kind": "open"},
    ])
    rdc_s.add_wall_with_openings("Y", 17.0, 0.20, -11.7, 1.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe")
    # North room dividers
    rdc_s.add_wall_with_openings("Y", -18.5, 0.22, 5.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe")
    rdc_s.add_wall_with_openings("Y", -9.0, 0.22, 5.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe")
    rdc_s.add_wall_with_openings("Y", 8.0, 0.20, 5.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe")
    rdc_s.add_wall_with_openings("Y", 23.5, 0.20, 5.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Hopital_Menthe")

    # RDC Furniture
    amc.add_reception_or_caisse_counter(rdc_f, -4.5, -3.5, Z_RDC_F, length=5.0, axis="X", facing="-Y", num_windows=3, armored_glass=False)
    for wy in (-8.5, -6.5, -4.5):
        rdc_f.add_box_center(4.5, wy, Z_RDC_F + 0.24, 3.8, 0.55, 0.48, "Mat_Arch_Hopital_Teal", collider=True)
    # Caisse / Billing (+8..+17)
    amc.add_reception_or_caisse_counter(rdc_f, 8.45, -6.7, Z_RDC_F, length=4.8, axis="Y", facing="-X", num_windows=3, armored_glass=True)
    amc.add_office_desk(rdc_f, 12.8, -6.5, Z_RDC_F, facing="-X", width=1.8)
    amc.add_safe_vault(rdc_f, 15.6, -9.8, Z_RDC_F, facing="-Y", size=(1.5, 1.2, 2.0))
    # Pharmacy (+17..+27.7)
    amc.add_reception_or_caisse_counter(rdc_f, 19.5, -2.5, Z_RDC_F, length=3.8, axis="X", facing="-Y", num_windows=2, armored_glass=False)
    for px in (20.0, 23.5, 26.5):
        amc.add_pharmacy_shelving(rdc_f, px, -7.8, Z_RDC_F, length=4.5, axis="Y")
    # Urgences (-27.7..-8.0, -11.7..+1.5)
    for bx in (-24.0, -19.5, -14.5):
        amc.add_hospital_bed(rdc_f, bx, -8.5, Z_RDC_F, axis="Y")
    amc.add_office_desk(rdc_f, -11.5, -2.5, Z_RDC_F, facing="-Y", width=2.0)
    # MRI & CT Scanners (-27.7..-9.0, +5..+19.7)
    amc.add_ct_mri_scanner(rdc_f, -23.2, 13.5, Z_RDC_F)
    amc.add_ct_mri_scanner(rdc_f, -13.8, 13.5, Z_RDC_F)
    amc.add_office_desk(rdc_f, -23.2, 7.5, Z_RDC_F, facing="+Y", width=1.6)
    amc.add_office_desk(rdc_f, -13.8, 7.5, Z_RDC_F, facing="+Y", width=1.6)
    # Labo & Blood Bank
    for lx in (1.5, 5.5):
        rdc_f.add_box_center(lx, 12.5, Z_RDC_F + 0.45, 1.2, 6.5, 0.90, "Mat_Arch_Plan_Travail", collider=True)
    amc.add_pharmacy_shelving(rdc_f, 12.0, 12.5, Z_RDC_F, length=5.0, axis="Y")
    amc.add_bathroom_fixtures(rdc_f, 23.8, 27.6, 5.4, 15.0, Z_RDC_F, num_stalls=3)

    amc.add_ceiling_lights_grid(rdc_s, -26.5, 26.5, -11.0, 18.8, Z_RDC_C, nx=6, ny=3)
    point_lights.extend([
        (0.0, -5.5, Z_RDC_C - 0.5, 190.0, 2.5),
        (12.5, -6.5, Z_RDC_C - 0.5, 130.0, 2.0),
        (-18.0, -5.5, Z_RDC_C - 0.5, 150.0, 2.2),
        (-18.5, 12.5, Z_RDC_C - 0.5, 150.0, 2.2),
    ])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Urgences_Hall", (-8.0, -6.0, Z_RDC_F), (-0.03, 0.03, 0.0, 1.75, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Acces Urgences"),
        ("Door_Urgences_Couloir", (-20.0, 1.5, Z_RDC_F), (0.0, 2.40, -0.04, 0.04, 0.0, 2.40), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Urgences vers Imagerie"),
        ("Door_Caisse_Hopital", (10.5, 1.5, Z_RDC_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Porte securisee Caisse"),
        ("Door_Pharmacie_RDC", (20.5, 1.5, Z_RDC_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Pharmacie Hospitaliere"),
        ("Door_IRM_RDC", (-24.5, 5.0, Z_RDC_F), (0.0, 1.65, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Salle IRM"),
        ("Door_Scanner_RDC", (-14.8, 5.0, Z_RDC_F), (0.0, 1.65, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Salle Scanner CT"),
        ("Door_Labo_RDC", (2.5, 5.0, Z_RDC_F), (0.0, 1.65, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Laboratoire Analyses"),
        ("Door_Banque_Sang", (11.0, 5.0, Z_RDC_F), (0.0, 1.55, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Croix_Rouge", (0.0, 0.0, 0.0), "Banque de Sang"),
        ("Door_Sanitaires_Hop_RDC", (24.8, 5.0, Z_RDC_F), (0.0, 1.15, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Sanitaires RDC"),
        ("Door_Asc_Hop_O_RDC", (-8.35, 5.10, Z_RDC_F), (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Monte-malades Ouest RDC"),
        ("Door_Asc_Hop_E_RDC", (21.15, 5.10, Z_RDC_F), (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Monte-malades Est RDC"),
    ]:
        _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)

    # ==================================================================
    # R+1 (Z = 4.55 .. 8.35 m) — Blocs Opératoires, Réanimation, Réunion Staff, Direction
    # ==================================================================
    amc.add_slab_with_cutouts(r1_s, -27.7, 27.7, -11.7, 19.7, Z_RDC_C, Z_R1, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -27.7, 27.7, -11.7, 19.7, Z_R1, Z_R1_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    _build_stair_and_elev_cores(r1_s, Z_R1_F, Z_R2_F)
    for sx in (-6.0, -1.5, 16.0, 20.5):
        r1_s.add_glass_railing("Y", sx, 5.0, 12.0, Z_R1_F)
    r1_s.add_glass_railing("X", 12.0, -6.0, -1.5, Z_R1_F)
    r1_s.add_glass_railing("X", 12.0, 16.0, 20.5, Z_R1_F)

    r1_s.add_wall_with_openings("X", 1.5, 0.20, -27.7, 27.7, Z_R1_F, Z_R1_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -25.0, "s1": -23.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.40, "kind": "door"},  # Bloc 1
        {"s0": -18.0, "s1": -16.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.40, "kind": "door"},  # Bloc 2
        {"s0": -11.0, "s1": -9.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.40, "kind": "door"},   # Bloc 3
        {"s0": -1.0, "s1": 0.4, "z0": Z_R1_F, "z1": Z_R1_F + 2.35, "kind": "door"},     # Direction
        {"s0": 15.0, "s1": 16.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.40, "kind": "door"},    # Reanimation
    ])
    r1_s.add_wall_with_openings("X", 5.0, 0.20, -27.7, -9.0, Z_R1_F, Z_R1_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -19.5, "s1": -17.5, "z0": Z_R1_F, "z1": Z_R1_F + 2.40, "kind": "door"},  # Salle de reveil
    ])
    r1_s.add_wall_with_openings("X", 5.0, 0.20, -1.5, 16.0, Z_R1_F, Z_R1_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": 5.5, "s1": 7.9, "z0": Z_R1_F, "z1": Z_R1_F + 2.45, "kind": "door"},      # Grande Salle de Reunion Staff
    ])
    for wx in (-21.0, -14.0, -7.0, 8.0):
        r1_s.add_wall_with_openings("Y", wx, 0.20, -11.7, 1.5, Z_R1_F, Z_R1_C, "Mat_Arch_Hopital_Menthe")
    r1_s.add_wall_with_openings("Y", -9.0, 0.20, 5.0, 19.7, Z_R1_F, Z_R1_C, "Mat_Arch_Hopital_Menthe")

    # 3 Sterile ORs
    for bx in (-24.3, -17.5, -10.5):
        amc.add_operating_table(r1_f, bx, -5.2, Z_R1_F)
    # Director Office (-7..+8)
    amc.add_office_desk(r1_f, 0.5, -6.5, Z_R1_F, facing="+Y", width=2.4, executive=True)
    amc.add_sofa_lounge(r1_f, 4.5, -4.5, Z_R1_F)
    # ICU / Reanimation (+8..+27.7, -11.7..+1.5)
    for ix in (11.0, 15.0, 19.0, 23.5):
        amc.add_hospital_bed(r1_f, ix, -7.5, Z_R1_F, axis="Y")
    # Post-Op Recovery (-27.7..-9.0, +5..+19.7)
    for rx in (-24.5, -20.5, -16.5, -12.5):
        amc.add_hospital_bed(r1_f, rx, 13.5, Z_R1_F, axis="Y")
    # Medical Staff Meeting Room (-1.5..+16.0, +5..+19.7)
    amc.add_conference_table(r1_f, 7.2, 12.5, Z_R1_F, length=9.0, width=2.4, axis="X", num_chairs_side=8)
    amc.add_cctv_wall(r1_f, 7.2, 19.2, Z_R1_F, axis="X", width=5.6, rows=2, cols=3, facing_sign=-1.0)
    amc.add_ceiling_lights_grid(r1_s, -26.5, 26.5, -11.0, 18.8, Z_R1_C, nx=6, ny=3)
    point_lights.extend([(-17.5, -5.2, Z_R1_C - 0.5, 160.0, 2.2), (7.2, 12.5, Z_R1_C - 0.5, 180.0, 2.5), (17.5, -5.5, Z_R1_C - 0.5, 150.0, 2.2)])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Bloc_Op_1", (-25.0, 1.5, Z_R1_F), (0.0, 1.65, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Bloc Operatoire 1"),
        ("Door_Bloc_Op_2", (-18.0, 1.5, Z_R1_F), (0.0, 1.65, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Bloc Operatoire 2"),
        ("Door_Bloc_Op_3", (-11.0, 1.5, Z_R1_F), (0.0, 1.65, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Bloc Operatoire 3"),
        ("Door_Direction_Hop", (-1.0, 1.5, Z_R1_F), (0.0, 1.35, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Direction Hopital"),
        ("Door_Reanimation_R1", (15.0, 1.5, Z_R1_F), (0.0, 1.75, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Soins Intensifs"),
        ("Door_Reveil_R1", (-19.5, 5.0, Z_R1_F), (0.0, 1.95, -0.04, 0.04, 0.0, 2.35), "Mat_Arch_Hopital_Teal", (0.0, 0.0, 0.0), "Salle de Reveil"),
        ("Door_Reunion_Med_L", (5.5, 5.0, Z_R1_F), (0.0, 1.18, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 25.0), "Salle Reunion Staff G"),
        ("Door_Reunion_Med_R", (7.9, 5.0, Z_R1_F), (-1.18, 0.0, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Bois_Interieur", (0.0, 0.0, -25.0), "Salle Reunion Staff D"),
        ("Door_Asc_Hop_O_R1", (-8.35, 5.10, Z_R1_F), (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Monte-malades Ouest R+1"),
        ("Door_Asc_Hop_E_R1", (21.15, 5.10, Z_R1_F), (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Monte-malades Est R+1"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)
        r1_doors.append(o.name)

    # ==================================================================
    # R+2 (Z = 8.65 .. 12.45 m) — Chambres d'Hospitalisation & Maternité
    # ==================================================================
    amc.add_slab_with_cutouts(r2_s, -27.7, 27.7, -11.7, 19.7, Z_R1_C, Z_R2, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, -27.7, 27.7, -11.7, 19.7, Z_R2, Z_R2_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    _build_stair_and_elev_cores(r2_s, Z_R2_F, Z_R3_F)
    for sx in (-6.0, -1.5, 16.0, 20.5):
        r2_s.add_glass_railing("Y", sx, 5.0, 12.0, Z_R2_F)

    r2_s.add_wall_with_openings("X", 1.5, 0.20, -27.7, 27.7, Z_R2_F, Z_R2_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -24.0, "s1": -22.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
        {"s0": -14.5, "s1": -13.1, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
        {"s0": -5.0, "s1": -3.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
        {"s0": 6.0, "s1": 7.4, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
        {"s0": 18.0, "s1": 19.4, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
    ])
    r2_s.add_wall_with_openings("X", 5.0, 0.20, -27.7, -9.0, Z_R2_F, Z_R2_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -20.0, "s1": -18.5, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
    ])
    r2_s.add_wall_with_openings("X", 5.0, 0.20, -1.5, 16.0, Z_R2_F, Z_R2_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": 6.5, "s1": 8.0, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
    ])
    for wx in (-18.5, -9.0, 1.5, 14.0):
        r2_s.add_wall_with_openings("Y", wx, 0.20, -11.7, 1.5, Z_R2_F, Z_R2_C, "Mat_Arch_Hopital_Menthe")

    # Inpatient beds across 5 South wards + Maternity North-West + Pediatrics North-Center
    for wx in (-25.0, -21.0, -15.5, -11.5, -6.0, -1.5, 5.0, 10.0, 17.5, 22.5):
        amc.add_hospital_bed(r2_f, wx, -7.2, Z_R2_F, axis="Y")
    for wx in (-24.5, -20.0, -15.0, 2.5, 7.0, 11.5):
        amc.add_hospital_bed(r2_f, wx, 13.5, Z_R2_F, axis="Y")
    amc.add_ceiling_lights_grid(r2_s, -26.5, 26.5, -11.0, 18.8, Z_R2_C, nx=6, ny=3)

    for idx, (dname, piv) in enumerate([
        ("Door_Chambre_R2_1", (-24.0, 1.5, Z_R2_F)),
        ("Door_Chambre_R2_2", (-14.5, 1.5, Z_R2_F)),
        ("Door_Chambre_R2_3", (-5.0, 1.5, Z_R2_F)),
        ("Door_Chambre_R2_4", (6.0, 1.5, Z_R2_F)),
        ("Door_Chambre_R2_5", (18.0, 1.5, Z_R2_F)),
        ("Door_Maternite_R2", (-20.0, 5.0, Z_R2_F)),
        ("Door_Pediatrie_R2", (6.5, 5.0, Z_R2_F)),
    ]):
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, (0.0, 1.35, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Hopital_Teal", open_note=f"Salle hospitalisation {idx + 1}")
        r2_doors.append(o.name)
    for dname, piv in (("Door_Asc_Hop_O_R2", (-8.35, 5.10, Z_R2_F)), ("Door_Asc_Hop_E_R2", (21.15, 5.10, Z_R2_F))):
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", open_note="Monte-malades R+2")
        r2_doors.append(o.name)

    # ==================================================================
    # R+3 (Z = 12.75 .. 16.55 m) — Amphithéâtre CHU, Cafétéria, Chambres VIP
    # ==================================================================
    amc.add_slab_with_cutouts(r3_s, -27.7, 27.7, -11.7, 19.7, Z_R2_C, Z_R3, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r3_s, -27.7, 27.7, -11.7, 19.7, Z_R3, Z_R3_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    for ex0, ex1 in ((-9.0, -6.0), (20.5, 23.5)):
        dc = (ex0 + ex1) * 0.5
        r3_s.add_wall_with_openings("Y", ex0 + 0.10, 0.20, 5.0, 8.5, Z_R3_F, Z_R3_C, "Mat_Arch_Beton_Brut")
        r3_s.add_wall_with_openings("Y", ex1 - 0.10, 0.20, 5.0, 8.5, Z_R3_F, Z_R3_C, "Mat_Arch_Beton_Brut")
        r3_s.add_wall_with_openings("X", 8.40, 0.20, ex0, ex1, Z_R3_F, Z_R3_C, "Mat_Arch_Beton_Brut")
        r3_s.add_wall_with_openings("X", 5.10, 0.20, ex0, ex1, Z_R3_F, Z_R3_C, "Mat_Arch_Beton_Brut", openings=[
            {"s0": dc - 0.85, "s1": dc + 0.85, "z0": Z_R3_F, "z1": Z_R3_F + 2.35, "kind": "door"},
        ])
    for sx in (-6.0, -1.5, 16.0, 20.5):
        r3_s.add_glass_railing("Y", sx, 5.0, 12.0, Z_R3_F)

    r3_s.add_wall_with_openings("X", 1.5, 0.20, -27.7, 27.7, Z_R3_F, Z_R3_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": -18.0, "s1": -15.8, "z0": Z_R3_F, "z1": Z_R3_F + 2.40, "kind": "door"},  # Amphitheatre CHU
        {"s0": 8.0, "s1": 9.4, "z0": Z_R3_F, "z1": Z_R3_F + 2.35, "kind": "door"},      # Suites VIP
    ])
    r3_s.add_wall_with_openings("X", 5.0, 0.20, -1.5, 16.0, Z_R3_F, Z_R3_C, "Mat_Arch_Hopital_Menthe", openings=[
        {"s0": 6.0, "s1": 7.8, "z0": Z_R3_F, "z1": Z_R3_F + 2.40, "kind": "door"},      # Cafeteria
    ])
    r3_s.add_wall_with_openings("Y", -4.0, 0.20, -11.7, 1.5, Z_R3_F, Z_R3_C, "Mat_Arch_Hopital_Menthe")
    amc.add_podium_lectern(r3_f, -24.5, -5.2, Z_R3_F, facing="-Y")
    for tx in (-20.0, -16.5, -13.0, -9.5):
        r3_f.add_box_center(tx, -5.2, Z_R3_F + 0.72, 0.70, 6.2, 0.06, "Mat_Arch_Bois_Interieur", collider=True)
    for cx in (2.0, 7.2, 12.4):
        r3_f.add_box_center(cx, 12.0, Z_R3_F + 0.74, 2.2, 1.1, 0.06, "Mat_Arch_Plan_Travail", collider=True)
    for vx in (2.0, 12.0, 22.0):
        amc.add_hospital_bed(r3_f, vx, -6.5, Z_R3_F, axis="Y")
    amc.add_ceiling_lights_grid(r3_s, -26.5, 26.5, -11.0, 18.8, Z_R3_C, nx=6, ny=3)

    for dname, piv, w, note in [
        ("Door_Amphi_CHU", (-18.0, 1.5, Z_R3_F), 2.1, "Amphitheatre CHU"),
        ("Door_Suites_VIP_R3", (8.0, 1.5, Z_R3_F), 1.35, "Suites Hospitalieres VIP"),
        ("Door_Cafeteria_R3", (6.0, 5.0, Z_R3_F), 1.75, "Cafeteria Hopital"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, (0.0, w, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Bois_Interieur", open_note=note)
        r3_doors.append(o.name)
    for dname, piv in (("Door_Asc_Hop_O_R3", (-8.35, 5.10, Z_R3_F)), ("Door_Asc_Hop_E_R3", (21.15, 5.10, Z_R3_F))):
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, (0.0, 1.65, -0.02, 0.02, 0.0, 2.30), "Mat_Arch_Metal_Inox", open_note="Monte-malades R+3")
        r3_doors.append(o.name)

    for b in (rdc_s, rdc_f, r1_s, r1_f, r2_s, r2_f, r3_s, r3_f):
        b.build_object(parent=int_root)

    int_colliders = (
        rdc_s.colliders + rdc_f.colliders + r1_s.colliders + r1_f.colliders +
        r2_s.colliders + r2_f.colliders + r3_s.colliders + r3_f.colliders
    )
    hide_rdc = r1_names + r2_names + r3_names + r1_doors + r2_doors + r3_doors
    hide_r1 = r2_names + r3_names + r2_doors + r3_doors
    return int_root, int_colliders, door_specs, door_objs, hide_rdc, hide_r1, point_lights


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
        "name": "Hopital_CHU",
        "lot_meters": [74.0, 66.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Hopital_CHU.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Hopital_CHU_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors_meta,
        "interior_doors": int_doors_meta,
    }
    ac.save_collider_manifest(MANIFEST_PATH, manifest)

    preview_dir = sys.argv[sys.argv.index("--") + 1] if ("--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1) else None
    if preview_dir:
        cam_specs = [
            ("ext_front_3q", (-54.0, -56.0, 32.0), (0.0, 2.0, 7.5), None),
            ("cutaway_rdc", (38.0, -38.0, 44.0), (0.0, 3.0, 1.5), roof_names + hide_rdc),
            ("cutaway_r1", (38.0, -38.0, 48.0), (0.0, 3.0, 6.0), roof_names + hide_r1),
            ("int_hall_caisse", (-5.0, -9.5, 2.5), (10.5, -5.0, 1.8), None),
            ("int_bloc_op_r1", (-19.8, 0.2, 6.8), (-17.5, -5.8, 5.4), None),
            ("int_reunion_r1", (-0.5, 6.0, 6.8), (11.0, 14.0, 5.5), None),
        ]
        amc.render_monument_previews(preview_dir, "hopital", cam_specs, interior_point_lights=lights)

    print(
        f"[Hopital_CHU] Built OK: ext_colliders={len(ext_cols)}, "
        f"int_colliders={len(int_cols)}, ext_doors={len(ext_doors_meta)}, "
        f"int_doors={len(int_doors_meta)}"
    )


if __name__ == "__main__":
    main()
