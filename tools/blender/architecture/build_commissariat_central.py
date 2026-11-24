"""Build script for Commissariat_Central in Blender (5.3+).

Generates a large-scale real-world Abidjan Central Police Station compound:
- Lot: 60.0 m x 56.0 m (3,360 m2) with public front parvis, guard booth,
  sliding vehicle security gate, prisoner extraction yard & garage.
- Building: 48.0 m x 26.0 m across 3 floors (RDC, R+1, R+2) + rooftop helipad
  & telecom tower (~3,700 m2 floor area).
- Complete interior rooms:
  - RDC: Public Reception & Security Screening, Caisse / Fines Treasury with
    armored teller bays & vault, Complaint Offices, Locker Room, Armory & Evidence,
    Restrooms, and isolated Prison / Garde a Vue Wing (4 barred cells including
    collective & isolation cells, Guard Post, Mugshot/Fingerprint station,
    3 Interrogation Rooms with one-way observation glass, Extraction Airlock).
  - R+1: Commissioner's Suite & Secretariat, Deputy Offices, Large Briefing &
    Crisis Meeting Room, Judicial Police Open-Space, CCTV/Radio Dispatch Center,
    Archives & Server Room.
  - R+2: Training/Conference Hall, General Administration & Accounting/Caisse,
    Staff Cafeteria & Lounge, Tactical Dojo / Gym.

Run with:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/architecture/build_commissariat_central.py [-- <preview_output_dir>]
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
BLEND_PATH = os.path.join(ARCH_DIR, "Commissariat_Central.blend")
MANIFEST_PATH = os.path.join(ARCH_DIR, "commissariat_central_manifest.json")
EXT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Monuments", "SM_Commissariat_Central.fbx"
)
INT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Interieurs", "SM_Commissariat_Central_Interieur.fbx"
)

# Vertical datum (meters)
Z_RDC = 0.25
Z_RDC_F = 0.29
Z_RDC_C = 4.05
Z_R1 = 4.35
Z_R1_F = 4.39
Z_R1_C = 7.95
Z_R2 = 8.25
Z_R2_F = 8.29
Z_R2_C = 11.85
Z_ROOF = 12.15
Z_PARAPET = 13.15

# Slab cutouts (stairwells + elevator shaft)
SLAB_CUTOUTS = [
    (-1.0, 4.0, 1.5, 8.5),     # Main U-stairwell
    (-4.0, -1.2, 1.5, 4.5),    # Elevator shaft
    (18.0, 23.8, 9.0, 15.8),   # Secondary staff stairwell
]


def _make_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, bounds, mat_panel, mat_frame="Mat_Arch_Metal_Anthracite",
    glass_bounds=None, rot_deg=(0.0, 0.0, 0.0), offset=(0.0, 0.0, 0.0),
    is_sliding=False, slide_axis="X", open_note="", add_handles=True,
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
        add_handles=add_handles,
    )
    obj = b.build_object(parent=parent, default_rot_deg=drot)
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def _make_cell_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, axis, width, height, rot_deg=(0.0, 0.0, 0.0), open_note="",
):
    b, meta, drot = amc.add_barred_cell_door_builder(
        name=name,
        collection_name=col_name,
        pivot_world=pivot,
        axis=axis,
        width=width,
        height=height,
        default_rot_deg=rot_deg,
        open_note=open_note,
    )
    obj = b.build_object(parent=parent, default_rot_deg=drot)
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def build_exterior():
    col = "Commissariat_Exterieur"
    ext_root = amc.make_empty_root("Commissariat_Central_Ext", col)

    site = ac.ArchMeshBuilder("Comm_Ext_Site", collection_name=col)
    roof_b = ac.ArchMeshBuilder("Comm_Ext_Roof", collection_name=col)
    det = ac.ArchMeshBuilder("Comm_Ext_Details", collection_name=col)

    # ------------------------------------------------------------------
    # 1. Compound ground, driveways, public parvis, rear police yard
    # ------------------------------------------------------------------
    site.add_box_bounds(-30.0, 30.0, -28.0, 28.0, -0.15, 0.0, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)
    # Green planter lawns flanking front parvis
    site.add_box_bounds(-29.5, -26.0, -27.5, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(15.0, 29.5, -27.5, -14.0, 0.0, 0.12, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    # Rear police yard parking bay markings
    for i in range(7):
        px = -10.0 + i * 5.2
        site.add_box_center(px, 23.0, 0.015, 0.14, 5.2, 0.02, "Mat_Arch_MurExt_Blanc")
    site.add_box_bounds(-12.0, 26.0, 20.2, 20.36, 0.005, 0.025, "Mat_Arch_MurExt_Blanc")

    # Building podium & grand front stairs + PMR ramp
    site.add_box_bounds(-24.0, 24.0, -10.0, 16.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.5, collider=True)
    site.add_box_bounds(-9.5, 9.5, -14.2, -10.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.5, collider=True)
    site.add_box_bounds(-7.5, 7.5, -15.4, -14.2, 0.0, 0.14, "Mat_Arch_Parement_Pierre", uv_scale=0.5, collider=True)
    # Side walkways
    site.add_box_bounds(-25.5, 25.5, 16.0, 18.0, 0.0, 0.14, "Mat_Arch_Beton_Brut", uv_scale=0.5, collider=True)

    # Perimeter fortified security walls (H = 2.80m concrete + 0.60m steel spikes)
    # West wall (X = -30)
    site.add_wall_with_openings("Y", -29.85, 0.30, -28.0, 28.0, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    # East wall (X = +30)
    site.add_wall_with_openings("Y", 29.85, 0.30, -28.0, 28.0, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    # Rear North wall (Y = +28)
    site.add_wall_with_openings("X", 27.85, 0.30, -30.0, 30.0, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    # Front West wall segments around 7.0m vehicle gate (-25.0..-18.0) and East wall (+14.0..+30.0)
    site.add_wall_with_openings("X", -27.85, 0.30, -29.7, -25.0, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    site.add_wall_with_openings("X", -27.85, 0.30, -18.0, -13.5, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    site.add_wall_with_openings("X", -27.85, 0.30, 13.5, 29.7, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    # Internal security separation wall between public parvis and west prisoner vehicle lane
    site.add_wall_with_openings("Y", -13.65, 0.30, -27.7, -10.0, 0.0, 2.75, "Mat_Arch_Beton_Brut", collider=True)
    # Top security rail above perimeter walls
    for (x0, x1, y0, y1) in [
        (-30.0, -29.7, -28.0, 28.0),
        (29.7, 30.0, -28.0, 28.0),
        (-29.7, 29.7, 27.7, 28.0),
        (-29.7, -25.0, -28.0, -27.7),
        (-18.0, -13.5, -28.0, -27.7),
        (13.5, 29.7, -28.0, -27.7),
    ]:
        det.add_box_bounds(x0, x1, y0, y1, 2.75, 3.25, "Mat_Arch_Metal_Anthracite")

    # Anti-ram steel bollards across front public parvis
    for bx in range(-12, 13, 2):
        det.add_cylinder(float(bx), -26.8, 0.0, 0.92, 0.11, 10, "Mat_Arch_Metal_Inox")
        det.add_collider_center(float(bx), -26.8, 0.46, 0.24, 0.24, 0.92)

    # Guard booth near vehicle gate (-17.5..-14.2, -26.5..-23.5)
    site.add_wall_with_openings(
        "X", -26.4, 0.22, -17.5, -14.2, 0.0, 2.85, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -16.9, "s1": -14.8, "z0": 1.0, "z1": 2.1, "kind": "window"}],
    )
    site.add_wall_with_openings(
        "X", -23.6, 0.22, -17.5, -14.2, 0.0, 2.85, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -16.4, "s1": -15.3, "z0": 0.0, "z1": 2.15, "kind": "door"}],
    )
    site.add_wall_with_openings(
        "Y", -17.4, 0.22, -26.5, -23.5, 0.0, 2.85, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -25.9, "s1": -24.1, "z0": 1.0, "z1": 2.1, "kind": "window"}],
    )
    site.add_wall_with_openings("Y", -14.3, 0.22, -26.5, -23.5, 0.0, 2.85, "Mat_Arch_Beton_Brut")
    site.add_box_bounds(-17.8, -13.9, -26.8, -23.2, 2.85, 3.10, "Mat_Arch_Police_Bleu", collider=True)

    # Flagpoles, palm trees, streetlamps, backup generator & water tanks
    amc.add_flagpole_ivorian(det, -11.8, -18.5, 0.0, height=11.0, flag_dir="+X", police_flag=False)
    amc.add_flagpole_ivorian(det, 11.8, -18.5, 0.0, height=11.0, flag_dir="-X", police_flag=True)
    for px, py in [(-27.8, -22.0), (-27.8, -16.0), (17.5, -22.0), (24.5, -22.0), (17.5, -16.0), (24.5, -16.0)]:
        amc.add_palm_tree(det, px, py, 0.0, height=6.4)
    for lx, ly in [(-12.5, -22.5), (12.5, -22.5), (-26.0, 19.5), (0.0, 19.5), (26.0, 19.5)]:
        amc.add_streetlamp(det, lx, ly, 0.0, height=5.2)

    # Backup generator & elevated water tanks (rear east service corner)
    det.add_box_bounds(24.5, 28.8, 18.0, 21.8, 0.0, 2.1, "Mat_Arch_Metal_Anthracite", collider=True)
    det.add_box_bounds(24.8, 28.5, 22.8, 26.5, 0.0, 2.4, "Mat_Arch_Metal_Inox", collider=True)
    det.add_cylinder(26.6, 24.6, 2.4, 4.6, 1.35, 16, "Mat_Arch_Cuve_Bleue")

    # ------------------------------------------------------------------
    # 2. Main Building Shell Walls (X = -24..+24, Y = -10..+16)
    # ------------------------------------------------------------------
    front_rdc_ops = [
        {"s0": -2.0, "s1": 2.0, "z0": Z_RDC, "z1": Z_RDC + 3.05, "kind": "door"},      # Grand double public entry
        {"s0": -7.5, "s1": -3.5, "z0": Z_RDC + 0.90, "z1": Z_RDC + 3.05, "kind": "window"},
        {"s0": 3.5, "s1": 7.5, "z0": Z_RDC + 0.90, "z1": Z_RDC + 3.05, "kind": "window"},
        {"s0": 10.5, "s1": 14.5, "z0": Z_RDC + 0.90, "z1": Z_RDC + 2.85, "kind": "window"},
        {"s0": 17.5, "s1": 22.5, "z0": Z_RDC + 0.90, "z1": Z_RDC + 2.85, "kind": "window"},
        {"s0": -22.5, "s1": -19.5, "z0": Z_RDC + 2.30, "z1": Z_RDC + 3.25, "kind": "window"},
        {"s0": -13.5, "s1": -10.2, "z0": Z_RDC + 2.30, "z1": Z_RDC + 3.25, "kind": "window"},
    ]
    front_r1_ops = [
        {"s0": -22.5, "s1": -18.5, "z0": Z_R1 + 0.90, "z1": Z_R1 + 2.95, "kind": "window"},
        {"s0": -16.0, "s1": -7.5, "z0": Z_R1 + 0.70, "z1": Z_R1 + 2.95, "kind": "window"},
        {"s0": -4.5, "s1": 4.5, "z0": Z_R1 + 0.70, "z1": Z_R1 + 2.95, "kind": "window"},
        {"s0": 7.5, "s1": 15.0, "z0": Z_R1 + 0.70, "z1": Z_R1 + 2.95, "kind": "window"},
        {"s0": 17.0, "s1": 22.5, "z0": Z_R1 + 0.70, "z1": Z_R1 + 2.95, "kind": "window"},
    ]
    front_r2_ops = [
        {"s0": -22.5, "s1": -14.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": -11.5, "s1": -5.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": -3.5, "s1": 3.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": 6.0, "s1": 14.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": 16.5, "s1": 22.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
    ]
    rear_rdc_ops = [
        {"s0": -22.5, "s1": -18.5, "z0": Z_RDC, "z1": Z_RDC + 3.20, "kind": "open"},   # Garage gate opening
        {"s0": -13.5, "s1": -10.0, "z0": Z_RDC + 2.35, "z1": Z_RDC + 3.25, "kind": "window"},
        {"s0": 5.5, "s1": 8.8, "z0": Z_RDC + 1.10, "z1": Z_RDC + 2.85, "kind": "window"},
        {"s0": 11.2, "s1": 14.8, "z0": Z_RDC + 1.40, "z1": Z_RDC + 2.85, "kind": "window"},
        {"s0": 19.5, "s1": 21.5, "z0": Z_RDC, "z1": Z_RDC + 2.35, "kind": "door"},      # Rear staff exit door
    ]
    rear_r1_ops = [
        {"s0": -22.0, "s1": -14.0, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.90, "kind": "window"},
        {"s0": -11.5, "s1": -5.5, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.90, "kind": "window"},
        {"s0": 5.5, "s1": 14.5, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.90, "kind": "window"},
    ]
    rear_r2_ops = [
        {"s0": -22.0, "s1": -14.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": -11.5, "s1": -5.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
        {"s0": 5.5, "s1": 15.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.90, "kind": "window"},
    ]

    walls_clean = ac.ArchMeshBuilder("Comm_Ext_Walls", collection_name=col)
    # Re-run walls cleanly via small helper loop:
    for z0, z1, ops_f, ops_r, ops_w, ops_e, wmat_w in [
        (Z_RDC, Z_RDC_C, front_rdc_ops, rear_rdc_ops, [
            {"s0": -8.5, "s1": -6.5, "z0": Z_RDC + 2.40, "z1": Z_RDC + 3.25, "kind": "window"},
            {"s0": -3.5, "s1": -1.5, "z0": Z_RDC + 2.40, "z1": Z_RDC + 3.25, "kind": "window"},
            {"s0": 5.0, "s1": 8.5, "z0": Z_RDC + 2.40, "z1": Z_RDC + 3.25, "kind": "window"},
        ], [
            {"s0": -8.0, "s1": -3.5, "z0": Z_RDC + 0.90, "z1": Z_RDC + 2.85, "kind": "window"},
            {"s0": 3.0, "s1": 7.5, "z0": Z_RDC + 1.40, "z1": Z_RDC + 2.85, "kind": "window"},
        ], "Mat_Arch_Beton_Brut"),
        (Z_R1, Z_R1_C, front_r1_ops, rear_r1_ops, [
            {"s0": -8.0, "s1": -3.0, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.85, "kind": "window"},
            {"s0": 3.0, "s1": 14.0, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.85, "kind": "window"},
        ], [
            {"s0": -8.0, "s1": -3.5, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.85, "kind": "window"},
            {"s0": 2.5, "s1": 7.5, "z0": Z_R1 + 0.85, "z1": Z_R1 + 2.85, "kind": "window"},
        ], "Mat_Arch_MurExt_Blanc"),
        (Z_R2, Z_R2_C, front_r2_ops, rear_r2_ops, [
            {"s0": -8.0, "s1": -3.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.85, "kind": "window"},
            {"s0": 3.0, "s1": 14.0, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.85, "kind": "window"},
        ], [
            {"s0": -8.0, "s1": -3.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.85, "kind": "window"},
            {"s0": 2.5, "s1": 7.5, "z0": Z_R2 + 0.85, "z1": Z_R2 + 2.85, "kind": "window"},
        ], "Mat_Arch_MurExt_Blanc"),
    ]:
        walls_clean.add_wall_with_openings("X", -9.83, 0.34, -24.0, 24.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=ops_f)
        walls_clean.add_wall_with_openings("X", 15.83, 0.34, -24.0, 24.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=ops_r)
        walls_clean.add_wall_with_openings("Y", -23.83, 0.34, -10.0, 16.0, z0, z1, wmat_w, openings=ops_w)
        walls_clean.add_wall_with_openings("Y", 23.83, 0.34, -10.0, 16.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=ops_e)
        for ax_type, fixed, olist in (("X", -9.83, ops_f), ("X", 15.83, ops_r), ("Y", -23.83, ops_w), ("Y", 23.83, ops_e)):
            for op in olist:
                if op["kind"] == "window" and (op["s1"] - op["s0"]) >= 3.0:
                    nb = max(2, int(round((op["s1"] - op["s0"]) / 2.2)))
                    amc.add_window_mullions(walls_clean, ax_type, fixed, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=nb)

    # Perimeter exterior slab trim bands (only outside wall plane, zero stair overlap!)
    for sz0, sz1 in ((Z_RDC_C, Z_R1), (Z_R1_C, Z_R2), (Z_R2_C, Z_ROOF)):
        walls_clean.add_box_bounds(-24.18, 24.18, -10.18, -9.98, sz0, sz1, "Mat_Arch_Beton_Brut")
        walls_clean.add_box_bounds(-24.18, 24.18, 15.98, 16.18, sz0, sz1, "Mat_Arch_Beton_Brut")
        walls_clean.add_box_bounds(-24.18, -23.98, -10.0, 16.0, sz0, sz1, "Mat_Arch_Beton_Brut")
        walls_clean.add_box_bounds(23.98, 24.18, -10.0, 16.0, sz0, sz1, "Mat_Arch_Beton_Brut")

    # ------------------------------------------------------------------
    # 3. Monumental Front Portico, Police Blue Fascia, Beacons, Louvers
    # ------------------------------------------------------------------
    for px in (-9.0, -3.5, 3.5, 9.0):
        amc.add_colonnade_pillar(
            det, px, -13.6, Z_RDC, 8.20, radius=0.42,
            base_mat="Mat_Arch_Parement_Pierre", shaft_mat="Mat_Arch_Beton_Brut", cap_mat="Mat_Arch_Police_Bleu",
        )
    # Portico roof canopy & iconic Police Blue fascia band + luminous night signage
    det.add_box_bounds(-10.2, 10.2, -14.5, -10.0, 8.20, 8.95, "Mat_Arch_Police_Bleu", collider=True)
    det.add_box_bounds(-10.2, 10.2, -14.65, -14.48, 8.32, 8.82, "Mat_Arch_Lumiere_Nuit")
    # Decorative illuminated police beacons flanking entrance portico
    det.add_box_center(-9.6, -14.6, 9.25, 0.45, 0.45, 0.55, "Mat_Arch_Lumiere_Gyro_Bleu")
    det.add_box_center(9.6, -14.6, 9.25, 0.45, 0.45, 0.55, "Mat_Arch_Lumiere_Gyro_Rouge")
    # Vertical modernist concrete brise-soleil fins on front & rear facades
    for fx in (-23.0, -18.0, -14.0, 11.5, 15.5, 19.5, 23.0):
        det.add_box_center(fx, -10.28, (Z_R1 + Z_R2_C) * 0.5, 0.24, 0.55, Z_R2_C - Z_R1, "Mat_Arch_Parement_Pierre")

    # Outdoor AC split units mounted cleanly on solid rear piers
    for ax in (-12.8, -4.2, 4.2, 16.8):
        walls_clean.add_ac_split_outdoor(ax, 16.25, Z_R1 + 0.45, facing_axis="+Y")
        walls_clean.add_ac_split_outdoor(ax, 16.25, Z_R2 + 0.45, facing_axis="+Y")

    # ------------------------------------------------------------------
    # 4. Roof Slab, Parapet, Helipad & Police Radio Tower
    # ------------------------------------------------------------------
    roof_b.add_box_bounds(-24.0, 24.0, -10.0, 16.0, Z_R2_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    # Blue institutional ribbon along front roof parapet
    roof_b.add_box_bounds(-24.2, 24.2, -10.24, -10.02, Z_ROOF + 0.35, Z_ROOF + 0.85, "Mat_Arch_Police_Bleu")
    # Parapet walls
    roof_b.add_wall_with_openings("X", -9.85, 0.30, -24.0, 24.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", 15.85, 0.30, -24.0, 24.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", -23.85, 0.30, -10.0, 16.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", 23.85, 0.30, -10.0, 16.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    # Rooftop stair/elevator penthouse access
    roof_b.add_box_bounds(-4.2, 4.2, 1.5, 8.8, Z_ROOF, Z_ROOF + 2.6, "Mat_Arch_Beton_Brut", collider=True)
    # Helipad on East Roof
    amc.add_helipad_marking(roof_b, 13.5, 3.0, Z_ROOF, radius=6.0)
    # Police Telecom / Radar Mast on West Roof
    roof_b.add_box_center(-19.0, 10.5, Z_ROOF + 0.6, 2.2, 2.2, 1.2, "Mat_Arch_Beton_Brut", collider=True)
    roof_b.add_cylinder(-19.0, 10.5, Z_ROOF + 1.2, Z_ROOF + 6.8, 0.16, 8, "Mat_Arch_Metal_Inox")
    roof_b.add_box_center(-19.0, 10.5, Z_ROOF + 5.2, 2.6, 0.15, 0.15, "Mat_Arch_Metal_Inox")
    roof_b.add_box_center(-19.0, 10.5, Z_ROOF + 6.9, 0.25, 0.25, 0.25, "Mat_Arch_Lumiere_Gyro_Rouge")

    site.build_object(parent=ext_root)
    walls_clean.build_object(parent=ext_root)
    roof_obj = roof_b.build_object(parent=ext_root)
    det.build_object(parent=ext_root)

    # ------------------------------------------------------------------
    # 5. Movable Exterior Gates & Doors
    # ------------------------------------------------------------------
    door_specs = []
    door_objs = []

    # Sliding compound vehicle gate (7.0m wide at Y = -28, X = -25..-18)
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Vehicules",
        pivot=(-25.0, -27.85, 0.0),
        bounds=(0.0, 7.0, -0.07, 0.07, 0.05, 2.65),
        mat_panel="Mat_Arch_Metal_Anthracite",
        offset=(-5.2, 0.0, 0.0),
        is_sliding=True,
        slide_axis="X",
        open_note="Portail vehicules police glissant (-X)",
    )
    # Sliding prisoner extraction garage gate (4.0m wide at Y = +16, X = -22.5..-18.5)
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Garage_Cellulaire",
        pivot=(-22.5, 15.83, Z_RDC),
        bounds=(0.0, 4.0, -0.07, 0.07, 0.02, 3.15),
        mat_panel="Mat_Arch_Metal_Anthracite",
        offset=(0.0, 0.0, 0.0),
        is_sliding=True,
        slide_axis="X",
        open_note="Porte securisee SAS fourgon cellulaire",
    )
    # Front public double glazed entrance doors (X = -2..+2 at Y = -10)
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Comm_Entree_L",
        pivot=(-2.0, -9.83, Z_RDC_F),
        bounds=(0.0, 1.97, -0.04, 0.04, 0.0, 2.95),
        mat_panel="Mat_Arch_Metal_Anthracite",
        glass_bounds=(0.18, 1.79, -0.015, 0.015, 0.25, 2.72),
        rot_deg=(0.0, 0.0, 25.0),
        open_note="Entree principale commissariat gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Comm_Entree_R",
        pivot=(2.0, -9.83, Z_RDC_F),
        bounds=(-1.97, 0.0, -0.04, 0.04, 0.0, 2.95),
        mat_panel="Mat_Arch_Metal_Anthracite",
        glass_bounds=(-1.79, -0.18, -0.015, 0.015, 0.25, 2.72),
        rot_deg=(0.0, 0.0, -25.0),
        open_note="Entree principale commissariat droite",
    )
    # Rear staff door (Y = +16, X = +19.5..+21.5)
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Comm_Sortie_Nord",
        pivot=(19.5, 15.83, Z_RDC_F),
        bounds=(0.0, 1.95, -0.04, 0.04, 0.0, 2.30),
        mat_panel="Mat_Arch_Metal_Anthracite",
        open_note="Sortie nord personnel vers parking",
    )

    ext_colliders = (
        site.colliders + walls_clean.colliders + roof_b.colliders + det.colliders
    )
    return ext_root, ext_colliders, door_specs, door_objs, [roof_obj.name]


def _build_stair_core(b, z0, z1, include_slab_finish=True):
    """Build main U-staircase + elevator shaft walls + secondary fire stair from z0 to z1."""
    z_mid = (z0 + z1) * 0.5
    # Main U-staircase (X = -1.0..+4.0, Y = +1.5..+8.5)
    b.add_stair_flight("Y", 0.20, 2.20, 1.70, 5.70, z0, z_mid, 10, "Mat_Arch_Sol_Carrelage", "Mat_Arch_Beton_Brut")
    b.add_box_bounds(-1.0, 4.0, 5.70, 8.50, z_mid - 0.22, z_mid, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(-1.0, 4.0, 5.70, 8.50, z_mid, z_mid + 0.04, "Mat_Arch_Sol_Carrelage")
    b.add_stair_flight("Y", 2.80, 2.20, 5.70, 1.70, z_mid, z1, 10, "Mat_Arch_Sol_Carrelage", "Mat_Arch_Beton_Brut")

    # Elevator shaft enclosure (X = -4.0..-1.2, Y = +1.5..+4.5)
    b.add_wall_with_openings("Y", -3.90, 0.20, 1.50, 4.50, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("Y", -1.30, 0.20, 1.50, 4.50, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("X", 4.40, 0.20, -4.0, -1.2, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings(
        "X", 1.60, 0.20, -4.0, -1.2, z0, z1, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -3.20, "s1": -2.00, "z0": z0, "z1": z0 + 2.25, "kind": "door"}],
    )

    # Secondary staff staircase (X = +18.0..+23.8, Y = +9.0..+15.8)
    b.add_stair_flight("X", 10.65, 2.20, 18.4, 21.8, z0, z_mid, 10, "Mat_Arch_Beton_Brut", "Mat_Arch_Beton_Brut")
    b.add_box_bounds(21.8, 23.8, 9.0, 15.8, z_mid - 0.20, z_mid, "Mat_Arch_Beton_Brut", collider=True)
    b.add_stair_flight("X", 14.15, 2.20, 21.8, 18.4, z_mid, z1, 10, "Mat_Arch_Beton_Brut", "Mat_Arch_Beton_Brut")


def build_interior():
    col = "Commissariat_Interieur"
    int_root = amc.make_empty_root("Commissariat_Central_Int", col)

    rdc_s = ac.ArchMeshBuilder("Comm_Int_RDC_Structure", collection_name=col)
    rdc_f = ac.ArchMeshBuilder("Comm_Int_RDC_Mobilier", collection_name=col)
    r1_s = ac.ArchMeshBuilder("Comm_Int_R1_Structure", collection_name=col)
    r1_f = ac.ArchMeshBuilder("Comm_Int_R1_Mobilier", collection_name=col)
    r2_s = ac.ArchMeshBuilder("Comm_Int_R2_Structure", collection_name=col)
    r2_f = ac.ArchMeshBuilder("Comm_Int_R2_Mobilier", collection_name=col)

    door_specs = []
    door_objs = []
    r1_names = ["Comm_Int_R1_Structure", "Comm_Int_R1_Mobilier"]
    r2_names = ["Comm_Int_R2_Structure", "Comm_Int_R2_Mobilier"]
    r1_doors = []
    r2_doors = []
    point_lights = []

    # ==================================================================
    # RDC (Z = 0.25 .. 4.05 m)
    # ==================================================================
    # Finish floor across building interior (-23.66..+23.66, -9.66..+15.66)
    rdc_s.add_box_bounds(-23.7, 23.7, -9.7, 15.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Carrelage", uv_scale=0.5, collider=True)
    # Marble centerpiece in Public Hall (-8.5..+8.5, -9.7..-2.0)
    rdc_s.add_box_bounds(-8.4, 8.4, -9.6, -2.1, Z_RDC_F, Z_RDC_F + 0.012, "Mat_Arch_Sol_Marbre", uv_scale=0.5)

    # Stair core RDC -> R1
    _build_stair_core(rdc_s, Z_RDC_F, Z_R1_F)

    # --- Corridor walls along Y = -2.0 and Y = +1.5 ---
    # Y = -2.0 divides south rooms from central corridor (-14.5..-8.5 is Geôlier south wall, -8.5..+8.5 is Public Hall opening)
    rdc_s.add_wall_with_openings(
        "X", -2.0, 0.20, -8.5, 8.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -3.2, "s1": 3.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.95, "kind": "open"}],
    )
    rdc_s.add_wall_with_openings(
        "X", -2.0, 0.20, 8.5, 23.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 10.5, "s1": 11.7, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},   # Caisse access
            {"s0": 17.5, "s1": 18.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},   # Bureau Plaintes 1
            {"s0": 21.2, "s1": 22.3, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},   # Bureau Plaintes 2
        ],
    )
    # Y = +1.5 north wall of main corridor (from X = -8.5 to +18.0; open at stairwell -1.0..+4.0 & elev -4.0..-1.2)
    rdc_s.add_wall_with_openings(
        "X", 1.5, 0.20, 4.0, 18.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 6.2, "s1": 7.4, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},     # Vestiaires
            {"s0": 12.2, "s1": 13.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},   # Armurerie
            {"s0": 16.0, "s1": 17.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.65, "kind": "open"},   # Service corridor
        ],
    )
    # Service branch corridor X = +18.0 (Y = +1.5..+15.7) separating Restrooms/Stair from Armory
    rdc_s.add_wall_with_openings(
        "Y", 18.0, 0.20, 1.5, 15.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 4.2, "s1": 5.3, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.30, "kind": "door"},     # Sanitaires RDC
            {"s0": 10.0, "s1": 11.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.60, "kind": "open"},   # Fire stair entrance
        ],
    )
    rdc_s.add_wall_with_openings("X", 9.0, 0.20, 18.0, 23.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 10.0, 0.20, 1.5, 15.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 16.0, 0.20, 1.5, 15.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut")

    # Caisse / Fines Treasury separation & armored teller wall (X = +8.5, Y = -9.7..-2.0)
    rdc_s.add_wall_with_openings(
        "Y", 8.5, 0.24, -9.7, -2.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -8.2, "s1": -3.2, "z0": Z_RDC_F + 1.05, "z1": Z_RDC_F + 2.30, "kind": "open"}],
    )
    rdc_s.add_wall_with_openings("Y", 16.5, 0.20, -9.7, -2.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 20.2, 0.20, -9.7, -2.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")

    # --- QUARTIER PRISON / GARDE À VUE (X = -23.7 .. -8.5, Y = -9.7 .. +15.7) ---
    # Heavy concrete boundary at X = -8.5 with ONE secure airlock door at Y = -0.7..+0.7
    rdc_s.add_wall_with_openings(
        "Y", -8.5, 0.30, -9.7, 15.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -0.7, "s1": 0.7, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"}],
    )
    # Cell corridor East wall (X = -14.5) & West wall (X = -17.5)
    # East side (-14.5..-8.5): Signalétique (-9.7..-2.0), Poste Geôlier (-2.0..+3.5), Cellules 1,2,3 (+3.5..+15.7)
    rdc_s.add_wall_with_openings(
        "Y", -14.5, 0.24, -9.7, 3.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut",
        openings=[
            {"s0": -6.2, "s1": -5.1, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.25, "kind": "door"},   # Signaletique
            {"s0": -0.6, "s1": 0.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.25, "kind": "door"},    # Geolier door
            {"s0": 0.9, "s1": 3.1, "z0": Z_RDC_F + 1.05, "z1": Z_RDC_F + 2.25, "kind": "window"},  # Geolier observation
        ],
    )
    # Cross walls on East Prison side
    rdc_s.add_wall_with_openings("X", -2.0, 0.22, -14.5, -8.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut")
    for cy in (3.5, 7.5, 11.5):
        rdc_s.add_wall_with_openings("X", cy, 0.24, -14.5, -8.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut")

    # Barred Prison Cell Fronts along X = -14.5 (Cells 1, 2, 3)
    amc.add_barred_partition(rdc_s, "Y", -14.5, 3.5, 7.5, Z_RDC_F, Z_RDC_C, 4.9, 6.0, Z_RDC_F + 2.25)
    amc.add_barred_partition(rdc_s, "Y", -14.5, 7.5, 11.5, Z_RDC_F, Z_RDC_C, 8.9, 10.0, Z_RDC_F + 2.25)
    amc.add_barred_partition(rdc_s, "Y", -14.5, 11.5, 15.7, Z_RDC_F, Z_RDC_C, 13.0, 14.1, Z_RDC_F + 2.25)

    # West side (-23.7..-17.5): 3 Interrogation rooms (-9.7..+3.5), Cell 4 Collective (+3.5..+10.0), Extraction Sas (+10.0..+15.7)
    rdc_s.add_wall_with_openings(
        "Y", -17.5, 0.24, -9.7, 3.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut",
        openings=[
            {"s0": -8.2, "s1": -7.1, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.25, "kind": "door"},
            {"s0": -3.7, "s1": -2.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.25, "kind": "door"},
            {"s0": 0.8, "s1": 1.9, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.25, "kind": "door"},
        ],
    )
    for wy in (-5.3, -0.9, 3.5, 10.0):
        rdc_s.add_wall_with_openings("X", wy, 0.24, -23.7, -17.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut")
    # Barred front for Collective Cell 4 (Y = 3.5..10.0 at X = -17.5) + Security Extraction Airlock door
    amc.add_barred_partition(rdc_s, "Y", -17.5, 3.5, 10.0, Z_RDC_F, Z_RDC_C, 6.0, 7.2, Z_RDC_F + 2.25)
    rdc_s.add_wall_with_openings(
        "Y", -17.5, 0.26, 10.0, 15.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut",
        openings=[{"s0": 12.2, "s1": 13.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.35, "kind": "door"}],
    )

    # --- RDC Furniture & Equipment ---
    # 1. Public Reception & Metal Detectors
    amc.add_security_portal_scanner(rdc_f, 0.0, -8.0, Z_RDC_F)
    amc.add_reception_or_caisse_counter(rdc_f, -5.2, -4.6, Z_RDC_F, length=4.8, axis="X", facing="-Y", num_windows=3, armored_glass=False)
    for wy in (-7.6, -5.8, -4.0):  # Waiting room benches
        rdc_f.add_box_center(5.5, wy, Z_RDC_F + 0.24, 3.6, 0.55, 0.48, "Mat_Arch_Tissu_Mobilier", collider=True)
    # 2. Caisse / Régie des Amendes (X = +8.5..+16.5)
    amc.add_reception_or_caisse_counter(rdc_f, 8.95, -5.7, Z_RDC_F, length=4.8, axis="Y", facing="-X", num_windows=3, armored_glass=True)
    amc.add_office_desk(rdc_f, 12.6, -5.5, Z_RDC_F, facing="-X", width=1.6, depth=0.75)
    amc.add_safe_vault(rdc_f, 15.3, -8.3, Z_RDC_F, facing="-Y", size=(1.6, 1.2, 2.05))
    # 3. Complaint Offices (X = +16.5..+23.7)
    amc.add_office_desk(rdc_f, 18.4, -6.5, Z_RDC_F, facing="+Y")
    amc.add_office_desk(rdc_f, 22.0, -6.5, Z_RDC_F, facing="+Y")
    # 4. Lockers, Armory, Restrooms
    amc.add_locker_bank(rdc_f, 7.0, 6.5, Z_RDC_F, count=8, axis="Y")
    amc.add_locker_bank(rdc_f, 9.2, 6.5, Z_RDC_F, count=8, axis="Y")
    rdc_f.add_box_center(8.1, 11.5, Z_RDC_F + 0.22, 3.2, 0.45, 0.44, "Mat_Arch_Bois_Interieur", collider=True)
    amc.add_armory_rack(rdc_f, 13.0, 5.5, Z_RDC_F, length=3.6, axis="Y")
    amc.add_armory_rack(rdc_f, 13.0, 12.0, Z_RDC_F, length=3.6, axis="Y")
    amc.add_reception_or_caisse_counter(rdc_f, 13.0, 3.0, Z_RDC_F, length=2.8, axis="X", facing="-Y", num_windows=1, armored_glass=True)
    amc.add_bathroom_fixtures(rdc_f, 18.2, 23.6, 1.8, 8.8, Z_RDC_F, num_stalls=3)
    # 5. Prison Wing Interiors
    amc.add_office_desk(rdc_f, -11.5, 0.8, Z_RDC_F, facing="-X", width=2.0)  # Guard post
    amc.add_cctv_wall(rdc_f, -9.2, 0.8, Z_RDC_F, axis="Y", width=3.2, rows=2, cols=3)
    # Mugshot backdrop & fingerprint kiosk
    rdc_f.add_box_center(-9.1, -6.2, Z_RDC_F + 1.25, 0.10, 2.2, 2.3, "Mat_Arch_MurExt_Blanc")
    amc.add_office_desk(rdc_f, -12.0, -8.0, Z_RDC_F, facing="+Y", width=1.4)
    # 3 Interrogation rooms
    for iy in (-7.5, -3.1, 1.3):
        amc.add_interrogation_set(rdc_f, -20.6, iy, Z_RDC_F)
    # 4 Prison cells
    amc.add_prison_cell_interior(rdc_f, -14.3, -8.7, 3.7, 7.3, Z_RDC_F, bunk_side="-Y")
    amc.add_prison_cell_interior(rdc_f, -14.3, -8.7, 7.7, 11.3, Z_RDC_F, bunk_side="-Y")
    amc.add_prison_cell_interior(rdc_f, -14.3, -8.7, 11.7, 15.5, Z_RDC_F, bunk_side="-Y")
    amc.add_prison_cell_interior(rdc_f, -23.5, -17.7, 3.7, 9.8, Z_RDC_F, bunk_side="-X", double_bunk=True)
    amc.add_prison_cell_interior(rdc_f, -23.5, -17.7, 3.7, 9.8, Z_RDC_F, bunk_side="+Y", double_bunk=True)

    # RDC Ceiling Lights & Point Lights
    for (lx0, lx1, ly0, ly1, nx, ny) in [
        (-8.0, 8.0, -9.5, -2.2, 3, 2),
        (8.8, 16.2, -9.5, -2.2, 2, 1),
        (-16.0, 23.0, -1.8, 1.3, 5, 1),
        (-23.5, -14.6, -9.5, 15.5, 2, 4),
        (-14.4, -8.6, 3.6, 15.5, 1, 3),
        (4.2, 17.8, 1.8, 15.5, 2, 2),
    ]:
        amc.add_ceiling_lights_grid(rdc_s, lx0, lx1, ly0, ly1, Z_RDC_C, nx=nx, ny=ny)
    point_lights.extend([
        (0.0, -5.8, Z_RDC_C - 0.5, 180.0, 2.5),
        (12.5, -5.8, Z_RDC_C - 0.5, 130.0, 2.0),
        (-16.0, 7.5, Z_RDC_C - 0.5, 130.0, 2.2),
        (-11.5, 5.5, Z_RDC_C - 0.5, 110.0, 1.8),
        (-20.5, -5.5, Z_RDC_C - 0.5, 100.0, 1.8),
        (11.0, 8.5, Z_RDC_C - 0.5, 130.0, 2.0),
    ])

    # RDC Interior Doors
    rdc_doors_cfg = [
        ("Door_Caisse_RDC", (10.5, -2.0, Z_RDC_F), (0.0, 1.15, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Porte blindee Caisse"),
        ("Door_Plaintes_1", (17.5, -2.0, Z_RDC_F), (0.0, 1.05, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureau Plaintes 1"),
        ("Door_Plaintes_2", (21.2, -2.0, Z_RDC_F), (0.0, 1.05, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureau Plaintes 2"),
        ("Door_Vestiaires", (6.2, 1.5, Z_RDC_F), (0.0, 1.15, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Vestiaires Agents"),
        ("Door_Armurerie", (12.2, 1.5, Z_RDC_F), (0.0, 1.25, -0.04, 0.04, 0.0, 2.25), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Porte blindee Armurerie"),
        ("Door_Sanitaires_RDC", (18.0, 4.2, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Sanitaires RDC"),
        ("Door_Prison_Sas", (-8.5, -0.7, Z_RDC_F), (-0.04, 0.04, 0.0, 1.35, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 35.0), "Sas securise Quartier Prison"),
        ("Door_Geolier", (-14.5, -0.6, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.20), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Poste de Geolier"),
        ("Door_Signaletique", (-14.5, -6.2, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.20), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Station Signaletique"),
        ("Door_Interrogatoire_1", (-17.5, -8.2, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.20), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Salle Interrogatoire 1"),
        ("Door_Interrogatoire_2", (-17.5, -3.7, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.20), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Salle Interrogatoire 2"),
        ("Door_Interrogatoire_3", (-17.5, 0.8, Z_RDC_F), (-0.03, 0.03, 0.0, 1.05, 0.0, 2.20), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Salle Interrogatoire 3"),
        ("Door_Sas_Extraction", (-17.5, 12.2, Z_RDC_F), (-0.04, 0.04, 0.0, 1.35, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "SAS Extraction Garage"),
        ("Door_Asc_RDC", (-3.2, 1.60, Z_RDC_F), (0.0, 1.18, -0.02, 0.02, 0.0, 2.20), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Ascenseur RDC"),
    ]
    for dname, piv, bnds, dmat, drot, note in rdc_doors_cfg:
        _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)

    # 4 Barred Prison Cell Doors!
    for cname, cpiv, cw, crot, cnote in [
        ("Door_Cell_1", (-14.5, 4.9, Z_RDC_F), 1.08, (0.0, 0.0, 0.0), "Cellule 1"),
        ("Door_Cell_2", (-14.5, 8.9, Z_RDC_F), 1.08, (0.0, 0.0, 0.0), "Cellule 2"),
        ("Door_Cell_3", (-14.5, 13.0, Z_RDC_F), 1.08, (0.0, 0.0, 0.0), "Cellule 3 Isolement"),
        ("Door_Cell_4_Collective", (-17.5, 6.0, Z_RDC_F), 1.18, (0.0, 0.0, -30.0), "Grande Cellule Collective"),
    ]:
        _make_cell_door(door_specs, door_objs, int_root, col, cname, cpiv, "Y", cw, 2.22, rot_deg=crot, open_note=cnote)

    # ==================================================================
    # R+1 (Z = 4.35 .. 7.95 m) — Commandement, Réunion/Crise, PJ, Dispatch
    # ==================================================================
    amc.add_slab_with_cutouts(r1_s, -23.7, 23.7, -9.7, 15.7, Z_RDC_C, Z_R1, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -23.7, 23.7, -9.7, 15.7, Z_R1, Z_R1_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    # Parquet in Commissioner Suite & Crisis Meeting Room
    r1_s.add_box_bounds(-17.0, -6.0, -9.6, -2.1, Z_R1_F, Z_R1_F + 0.012, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    r1_s.add_box_bounds(4.2, 23.6, -9.6, -2.1, Z_R1_F, Z_R1_F + 0.012, "Mat_Arch_Sol_Parquet", uv_scale=0.5)

    _build_stair_core(r1_s, Z_R1_F, Z_R2_F)
    # Stairwell guardrails at R+1
    r1_s.add_glass_railing("Y", -1.0, 1.5, 8.5, Z_R1_F)
    r1_s.add_glass_railing("Y", 4.0, 1.5, 8.5, Z_R1_F)
    r1_s.add_glass_railing("X", 8.5, -1.0, 4.0, Z_R1_F)
    r1_s.add_glass_railing("Y", 1.5, 1.5, 5.7, Z_R1_F)

    # Corridor & room walls R+1
    r1_s.add_wall_with_openings(
        "X", -2.0, 0.20, -23.7, 23.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -21.2, "s1": -20.1, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"},  # Adjoint 1
            {"s0": -14.0, "s1": -12.9, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"},  # Bureau Commissaire
            {"s0": -5.0, "s1": -3.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"},    # Secretariat Commissaire
            {"s0": 6.5, "s1": 8.9, "z0": Z_R1_F, "z1": Z_R1_F + 2.45, "kind": "door"},      # Double doors Salle de Reunion
        ],
    )
    r1_s.add_wall_with_openings("Y", -17.0, 0.20, -9.7, -2.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings(
        "Y", -6.0, 0.20, -9.7, -2.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -6.5, "s1": -5.4, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"}],
    )
    r1_s.add_wall_with_openings("Y", 4.0, 0.20, -9.7, -2.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")

    # North Wing R+1: PJ Open Space (-23.7..-4.0), Dispatch (+4.0..+12.5 & +4..+16 north), Archives/Serveurs (+12.5..+16.0, +1.5..+9.0), Service Corridor (+16..+18), Sanitaires (+18..+23.7, +1.5..+9.0)
    r1_s.add_wall_with_openings(
        "X", 1.5, 0.20, -23.7, -4.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -13.0, "s1": -10.5, "z0": Z_R1_F, "z1": Z_R1_F + 2.45, "kind": "door"}],  # PJ Open space double
    )
    r1_s.add_wall_with_openings(
        "X", 1.5, 0.20, 4.0, 18.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 8.0, "s1": 9.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"},   # Dispatch Center
            {"s0": 16.0, "s1": 17.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "open"},
        ],
    )
    r1_s.add_wall_with_openings("Y", 12.5, 0.20, 1.5, 9.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings("X", 9.0, 0.20, 12.5, 16.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings(
        "Y", 16.0, 0.20, 1.5, 15.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 4.2, "s1": 5.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"}],  # Archives / Server
    )
    r1_s.add_wall_with_openings(
        "Y", 18.0, 0.20, 1.5, 15.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 4.2, "s1": 5.3, "z0": Z_R1_F, "z1": Z_R1_F + 2.30, "kind": "door"},
            {"s0": 10.0, "s1": 11.6, "z0": Z_R1_F, "z1": Z_R1_F + 2.60, "kind": "open"},
        ],
    )
    r1_s.add_wall_with_openings("X", 9.0, 0.20, 18.0, 23.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")

    # R+1 Furniture
    # Commissioner's Grand Office (-17..-6, -9.7..-2.0)
    amc.add_office_desk(r1_f, -11.5, -7.2, Z_R1_F, facing="+Y", width=2.4, depth=1.0, executive=True)
    amc.add_sofa_lounge(r1_f, -11.5, -4.2, Z_R1_F, fabric_mat="Mat_Arch_Tissu_Mobilier")
    r1_f.add_box_center(-16.4, -5.8, Z_R1_F + 1.2, 0.48, 3.6, 2.3, "Mat_Arch_Bois_Interieur", collider=True)
    # Secretariat & Deputy
    amc.add_office_desk(r1_f, -2.5, -5.8, Z_R1_F, facing="-Y", width=1.8)
    amc.add_office_desk(r1_f, -20.5, -6.2, Z_R1_F, facing="+Y", width=1.9, executive=True)
    # Grand Briefing / Crisis Meeting Room (+4..+23.7, -9.7..-2.0)
    amc.add_conference_table(r1_f, 13.0, -5.8, Z_R1_F, length=9.5, width=2.5, axis="X", num_chairs_side=8)
    amc.add_podium_lectern(r1_f, 20.6, -5.8, Z_R1_F, facing="-Y")
    amc.add_cctv_wall(r1_f, 23.3, -5.8, Z_R1_F, axis="Y", width=5.6, rows=2, cols=3)
    # Judicial Police Open-Space (-23.7..-4.0, +1.5..+15.7)
    for ox in (-19.5, -14.5, -9.5):
        for oy in (5.0, 11.2):
            amc.add_office_desk(r1_f, ox, oy, Z_R1_F, facing="-Y", width=1.6)
            amc.add_office_desk(r1_f, ox, oy + 1.4, Z_R1_F, facing="+Y", width=1.6)
    # Dispatch / CCTV Command (+4.0..+16.0, +1.5..+15.7)
    amc.add_cctv_wall(r1_f, 9.5, 15.3, Z_R1_F, axis="X", width=7.5, rows=2, cols=5)
    for dx in (6.5, 9.5, 12.8):
        amc.add_office_desk(r1_f, dx, 11.2, Z_R1_F, facing="+Y", width=1.7)
    amc.add_office_desk(r1_f, 8.2, 6.2, Z_R1_F, facing="+Y", width=2.0)
    # Archives & Server racks (+12.5..+16.0, +1.5..+9.0)
    r1_f.add_box_center(13.2, 3.6, Z_R1_F + 1.05, 0.65, 2.8, 2.1, "Mat_Arch_Metal_Anthracite", collider=True)
    r1_f.add_box_center(13.2, 7.0, Z_R1_F + 1.05, 0.65, 2.8, 2.1, "Mat_Arch_Metal_Inox", collider=True)
    amc.add_bathroom_fixtures(r1_f, 18.2, 23.6, 1.8, 8.8, Z_R1_F, num_stalls=3)

    for (lx0, lx1, ly0, ly1, nx, ny) in [
        (-23.0, -4.5, -9.5, -2.2, 3, 1),
        (4.5, 23.0, -9.5, -2.2, 3, 1),
        (-23.0, -4.5, 2.0, 15.2, 3, 2),
        (4.5, 15.5, 2.0, 15.2, 2, 2),
    ]:
        amc.add_ceiling_lights_grid(r1_s, lx0, lx1, ly0, ly1, Z_R1_C, nx=nx, ny=ny)
    point_lights.extend([
        (-11.5, -5.8, Z_R1_C - 0.5, 140.0, 2.2),
        (13.5, -5.8, Z_R1_C - 0.5, 170.0, 2.5),
        (-14.0, 8.5, Z_R1_C - 0.5, 170.0, 2.5),
        (10.0, 9.0, Z_R1_C - 0.5, 140.0, 2.2),
    ])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Adjoint_R1", (-21.2, -2.0, Z_R1_F), (0.0, 1.08, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureau Adjoint"),
        ("Door_Commissaire_R1", (-14.0, -2.0, Z_R1_F), (0.0, 1.08, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 25.0), "Bureau du Commissaire"),
        ("Door_Secretariat_R1", (-5.0, -2.0, Z_R1_F), (0.0, 1.15, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Secretariat Direction"),
        ("Door_Reunion_R1_L", (6.5, -2.0, Z_R1_F), (0.0, 1.18, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 30.0), "Salle de Reunion / Crise G"),
        ("Door_Reunion_R1_R", (8.9, -2.0, Z_R1_F), (-1.18, 0.0, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Bois_Interieur", (0.0, 0.0, -30.0), "Salle de Reunion / Crise D"),
        ("Door_PJ_Open_L", (-13.0, 1.5, Z_R1_F), (0.0, 1.22, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Open-Space PJ G"),
        ("Door_PJ_Open_R", (-10.5, 1.5, Z_R1_F), (-1.22, 0.0, -0.03, 0.03, 0.0, 2.38), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Open-Space PJ D"),
        ("Door_Dispatch_R1", (8.0, 1.5, Z_R1_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Centre Dispatch / CCTV"),
        ("Door_Archives_R1", (16.0, 4.2, Z_R1_F), (-0.03, 0.03, 0.0, 1.08, 0.0, 2.25), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Archives / Serveurs"),
        ("Door_Sanitaires_R1", (18.0, 4.2, Z_R1_F), (-0.03, 0.03, 0.0, 1.08, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Sanitaires R+1"),
        ("Door_Asc_R1", (-3.2, 1.60, Z_R1_F), (0.0, 1.18, -0.02, 0.02, 0.0, 2.20), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Ascenseur R+1"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)
        r1_doors.append(o.name)

    # ==================================================================
    # R+2 (Z = 8.25 .. 11.85 m) — Formation, Admin/Caisse Gen, Réfectoire, Dojo
    # ==================================================================
    amc.add_slab_with_cutouts(r2_s, -23.7, 23.7, -9.7, 15.7, Z_R1_C, Z_R2, SLAB_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, -23.7, 23.7, -9.7, 15.7, Z_R2, Z_R2_F, SLAB_CUTOUTS, "Mat_Arch_Sol_Carrelage", collider=False)
    # Dojo blue mat floor (+4.0..+16.0, +1.5..+15.7)
    r2_s.add_box_bounds(4.2, 15.8, 1.8, 15.4, Z_R2_F, Z_R2_F + 0.04, "Mat_Arch_Cuve_Bleue", uv_scale=0.5, collider=True)

    # Elevator shaft top & stairwell guardrails
    r2_s.add_wall_with_openings("Y", -3.90, 0.20, 1.50, 4.50, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings("Y", -1.30, 0.20, 1.50, 4.50, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings("X", 4.40, 0.20, -4.0, -1.2, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
    r2_s.add_wall_with_openings(
        "X", 1.60, 0.20, -4.0, -1.2, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -3.20, "s1": -2.00, "z0": Z_R2_F, "z1": Z_R2_F + 2.25, "kind": "door"}],
    )
    r2_s.add_glass_railing("Y", -1.0, 1.5, 8.5, Z_R2_F)
    r2_s.add_glass_railing("Y", 4.0, 1.5, 8.5, Z_R2_F)
    r2_s.add_glass_railing("X", 8.5, -1.0, 4.0, Z_R2_F)
    r2_s.add_glass_railing("Y", 1.5, 1.5, 5.7, Z_R2_F)

    # R+2 Walls
    r2_s.add_wall_with_openings(
        "X", -2.0, 0.20, -23.7, 23.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -13.0, "s1": -10.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.40, "kind": "door"},  # Salle de formation
            {"s0": 8.0, "s1": 9.2, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},      # Admin / Caisse generale
        ],
    )
    r2_s.add_wall_with_openings("Y", 4.0, 0.20, -9.7, -2.0, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")
    r2_s.add_wall_with_openings(
        "X", 1.5, 0.20, -23.7, -4.0, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -12.0, "s1": -10.5, "z0": Z_R2_F, "z1": Z_R2_F + 2.35, "kind": "door"}],  # Refectoire
    )
    r2_s.add_wall_with_openings(
        "X", 1.5, 0.20, 4.0, 18.0, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 9.0, "s1": 10.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.35, "kind": "door"},   # Dojo
            {"s0": 16.0, "s1": 17.8, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "open"},
        ],
    )
    r2_s.add_wall_with_openings("Y", 16.0, 0.20, 1.5, 15.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")
    r2_s.add_wall_with_openings(
        "Y", 18.0, 0.20, 1.5, 15.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": 4.2, "s1": 5.3, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"},
            {"s0": 10.0, "s1": 11.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "open"},
        ],
    )
    r2_s.add_wall_with_openings("X", 9.0, 0.20, 18.0, 23.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")

    # R+2 Furniture
    amc.add_podium_lectern(r2_f, -21.0, -5.8, Z_R2_F, facing="-Y")
    for tx in (-17.5, -14.5, -11.5, -8.5):
        r2_f.add_box_center(tx, -5.8, Z_R2_F + 0.72, 0.70, 5.2, 0.06, "Mat_Arch_Bois_Interieur", collider=True)
    for ax in (8.5, 13.5, 19.0):
        amc.add_office_desk(r2_f, ax, -6.0, Z_R2_F, facing="+Y", width=1.8)
    amc.add_safe_vault(r2_f, 22.4, -8.3, Z_R2_F, facing="-Y", size=(1.5, 1.1, 2.0))
    # Cafeteria tables
    for cx in (-18.5, -13.5, -8.5):
        for cy in (5.5, 10.5):
            r2_f.add_box_center(cx, cy, Z_R2_F + 0.74, 2.4, 1.1, 0.06, "Mat_Arch_Plan_Travail", collider=True)
    # Dojo heavy bags
    for bx in (7.0, 10.0, 13.0):
        r2_f.add_cylinder(bx, 12.0, Z_R2_F + 1.0, Z_R2_F + 2.4, 0.24, 12, "Mat_Arch_Tissu_Mobilier")
        r2_f.add_cylinder(bx, 12.0, Z_R2_F + 2.4, Z_R2_C, 0.025, 6, "Mat_Arch_Metal_Inox")
    amc.add_bathroom_fixtures(r2_f, 18.2, 23.6, 1.8, 8.8, Z_R2_F, num_stalls=3)

    amc.add_ceiling_lights_grid(r2_s, -22.5, 22.5, -9.2, 15.0, Z_R2_C, nx=5, ny=3)

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Formation_R2_L", (-13.0, -2.0, Z_R2_F), (0.0, 1.18, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Salle Formation G"),
        ("Door_Formation_R2_R", (-10.6, -2.0, Z_R2_F), (-1.18, 0.0, -0.03, 0.03, 0.0, 2.35), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Salle Formation D"),
        ("Door_Admin_Caisse_R2", (8.0, -2.0, Z_R2_F), (0.0, 1.18, -0.03, 0.03, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Caisse Generale / Admin"),
        ("Door_Refectoire_R2", (-12.0, 1.5, Z_R2_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Refectoire Agents"),
        ("Door_Dojo_R2", (9.0, 1.5, Z_R2_F), (0.0, 1.55, -0.03, 0.03, 0.0, 2.30), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Dojo / Entrainement"),
        ("Door_Sanitaires_R2", (18.0, 4.2, Z_R2_F), (-0.03, 0.03, 0.0, 1.08, 0.0, 2.25), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Sanitaires R+2"),
        ("Door_Asc_R2", (-3.2, 1.60, Z_R2_F), (0.0, 1.18, -0.02, 0.02, 0.0, 2.20), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Ascenseur R+2"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)
        r2_doors.append(o.name)

    for b in (rdc_s, rdc_f, r1_s, r1_f, r2_s, r2_f):
        b.build_object(parent=int_root)

    int_colliders = (
        rdc_s.colliders + rdc_f.colliders +
        r1_s.colliders + r1_f.colliders +
        r2_s.colliders + r2_f.colliders
    )
    hide_for_rdc_cutaway = r1_names + r2_names + r1_doors + r2_doors
    hide_for_r1_cutaway = r2_names + r2_doors
    return int_root, int_colliders, door_specs, door_objs, hide_for_rdc_cutaway, hide_for_r1_cutaway, point_lights


def main():
    amc.register_monument_materials()
    ac.reset_scene()

    ext_root, ext_cols, ext_doors_meta, ext_door_objs, roof_names = build_exterior()
    int_root, int_cols, int_doors_meta, int_door_objs, hide_rdc, hide_r1, lights = build_interior()

    os.makedirs(ARCH_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    ext_objs = [ext_root] + list(ext_root.children_recursive)
    int_objs = [int_root] + list(int_root.children_recursive)
    ac.export_objects_fbx(ext_objs, EXT_FBX_PATH)
    ac.export_objects_fbx(int_objs, INT_FBX_PATH)

    manifest = {
        "name": "Commissariat_Central",
        "lot_meters": [60.0, 56.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Commissariat_Central.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Commissariat_Central_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors_meta,
        "interior_doors": int_doors_meta,
    }
    ac.save_collider_manifest(MANIFEST_PATH, manifest)

    preview_dir = None
    if "--" in sys.argv:
        extra = sys.argv[sys.argv.index("--") + 1:]
        if extra:
            preview_dir = extra[0]

    if preview_dir:
        cam_specs = [
            ("ext_front_3q", (46.0, -48.0, 26.0), (0.0, 0.0, 5.5), None),
            ("ext_rear_3q", (-46.0, 44.0, 24.0), (0.0, 2.0, 5.0), None),
            ("cutaway_rdc", (32.0, -32.0, 34.0), (0.0, 2.5, 1.5), roof_names + hide_rdc),
            ("cutaway_r1", (32.0, -32.0, 38.0), (0.0, 2.5, 5.5), roof_names + hide_r1),
            ("int_hall_caisse", (-6.0, -8.5, 2.3), (10.5, -5.0, 1.6), None),
            ("int_prison_cells", (-16.0, -1.5, 2.2), (-14.5, 11.0, 1.3), None),
            ("int_reunion_r1", (5.5, -8.5, 6.4), (16.5, -5.0, 5.4), None),
        ]
        amc.render_monument_previews(preview_dir, "commissariat", cam_specs, interior_point_lights=lights)

    print(
        f"[Commissariat_Central] Built OK: ext_colliders={len(ext_cols)}, "
        f"int_colliders={len(int_cols)}, ext_doors={len(ext_doors_meta)}, "
        f"int_doors={len(int_doors_meta)}"
    )


if __name__ == "__main__":
    main()
