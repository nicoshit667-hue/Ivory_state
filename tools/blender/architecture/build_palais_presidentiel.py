"""Build script for Palais_Presidentiel in Blender (5.3+).

Generates a monumental, Abidjan-Plateau-inspired Presidential Palace complex:
- Domain: 100.0 m x 92.0 m (9,200 m2) with ornamental perimeter railings,
  3 motorized gates, 2 Republican Guard posts, Cour d'Honneur with circular
  fountain, red-carpet grand perron, rear gardens, reflecting pool, and helipad.
- Palace Building: 84.0 m facade x 36.0 m depth in H-plan across 3 levels
  (RDC, R+1, R+2, ~6,800 m2) featuring a double-height Salle des Pas Perdus
  atrium, monumental south colonnade, overhanging modernist canopies, and
  central pediment crest.
- Complete interior rooms:
  - RDC: Double-height Salle des Pas Perdus & Imperial Y-Staircase, Salon
    d'Honneur (Ambassadors), Protocol Lounge, Press Conference Auditorium,
    Treasury / Caisse & Armored Vault, Republican Guard Security Command,
    Grand State Banquet Hall, State Kitchen, VIP Reception & Restrooms.
  - R+1: Mezzanine Gallery overlooking Atrium + Presidential Speech Balcony,
    Grand Presidential Office Suite, Secretariat & Cabinet Offices, Monumental
    Cabinet Room (Conseil des Ministres), Strategic Defense Meeting Room,
    Diplomatic Treaty Room, Special Advisors Offices.
  - R+2: Panoramic State Salon, Private Presidential Suite, Visiting Heads of
    State Suites, State Library & Archives, Restricted Crisis Room, Private
    Dining Hall, Fitness & Lounge.

Run with:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/architecture/build_palais_presidentiel.py [-- <preview_output_dir>]
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
BLEND_PATH = os.path.join(ARCH_DIR, "Palais_Presidentiel.blend")
MANIFEST_PATH = os.path.join(ARCH_DIR, "palais_presidentiel_manifest.json")
EXT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Monuments", "SM_Palais_Presidentiel.fbx"
)
INT_FBX_PATH = os.path.join(
    UNITY_ROOT, "Assets", "_Project", "Art", "Models", "Architecture", "Interieurs", "SM_Palais_Presidentiel_Interieur.fbx"
)

# Vertical datum (meters)
Z_RDC = 1.20
Z_RDC_F = 1.25
Z_RDC_C = 6.00
Z_R1 = 6.35
Z_R1_F = 6.40
Z_R1_C = 10.80
Z_R2 = 11.15
Z_R2_F = 11.20
Z_R2_C = 15.15
Z_ROOF = 15.55
Z_PARAPET = 16.95

# Slab cutouts (stairwells + elevators + R+1 atrium void)
WING_STAIRS_ELEVS = [
    (-26.8, -22.3, 6.5, 14.0),   # West wing stairwell
    (-29.6, -26.8, 6.5, 9.5),    # West elevator shaft
    (22.3, 26.8, 6.5, 14.0),     # East wing stairwell
    (26.8, 29.6, 6.5, 9.5),      # East elevator shaft
]
R1_CUTOUTS = WING_STAIRS_ELEVS + [
    (-11.0, 11.0, -5.5, 7.5),    # Salle des Pas Perdus double-height atrium void
    (-5.5, 5.5, 7.5, 19.5),      # Imperial Y-staircase hall void
]
R2_CUTOUTS = WING_STAIRS_ELEVS


def _make_door(
    door_specs, door_objs, parent, col_name,
    name, pivot, bounds, mat_panel, mat_frame="Mat_Arch_Or_Ceremonie",
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
        mat_handle="Mat_Arch_Or_Ceremonie",
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


def _add_palace_shell_level(walls, z0, z1, include_main_entry=False):
    """Build exterior palace shell walls (H-plan footprint) for one floor."""
    # Corps Central South front (Y = -8.0, X = -22..+22)
    c_front_ops = [
        {"s0": -19.5, "s1": -15.5, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
        {"s0": -12.0, "s1": -7.0, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 7.0, "s1": 12.0, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 15.5, "s1": 19.5, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
    ]
    if include_main_entry:
        c_front_ops.append({"s0": -2.4, "s1": 2.4, "z0": z0, "z1": z0 + 3.90, "kind": "door"})
    else:
        # Balcony / tall ceremonial glazing at R+1 & R+2
        c_front_ops.append({"s0": -2.2, "s1": 2.2, "z0": z0, "z1": z0 + 3.40, "kind": "door"})
        c_front_ops.append({"s0": -5.8, "s1": -3.2, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"})
        c_front_ops.append({"s0": 3.2, "s1": 5.8, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"})
    walls.add_wall_with_openings("X", -7.83, 0.34, -22.0, 22.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=c_front_ops)

    # Corps Central North rear (Y = +20.0, X = -22..+22)
    c_rear_ops = [
        {"s0": -19.0, "s1": -12.0, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"},
        {"s0": -9.5, "s1": -5.5, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 5.5, "s1": 9.5, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 12.0, "s1": 19.0, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"},
    ]
    if include_main_entry:
        c_rear_ops.append({"s0": -2.0, "s1": 2.0, "z0": z0, "z1": z0 + 3.40, "kind": "door"})
    else:
        c_rear_ops.append({"s0": -3.5, "s1": 3.5, "z0": z0 + 0.65, "z1": z1 - 0.55, "kind": "window"})
    walls.add_wall_with_openings("X", 19.83, 0.34, -42.0, 42.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=[
        # West Wing North rear (-42..-22)
        {"s0": -39.0, "s1": -34.0, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
        {"s0": -30.5, "s1": -25.0, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
    ] + c_rear_ops + [
        # East Wing North rear (+22..+42): high privacy window over RDC restrooms
        {"s0": 25.0, "s1": 30.5, "z0": z0 + (1.85 if include_main_entry else 0.70), "z1": z1 - 0.55, "kind": "window"},
        {"s0": 34.0, "s1": 39.0, "z0": z0 + 0.70, "z1": z1 - 0.55, "kind": "window"},
    ])

    # West Wing South front (Y = -16.0, X = -42..-22) & East Wing South front (Y = -16.0, X = +22..+42)
    walls.add_wall_with_openings("X", -15.83, 0.34, -42.0, -22.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=[
        {"s0": -39.5, "s1": -33.5, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
        {"s0": -30.5, "s1": -24.5, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
    ])
    walls.add_wall_with_openings("X", -15.83, 0.34, 22.0, 42.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=[
        {"s0": 24.5, "s1": 30.5, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 33.5, "s1": 39.5, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
    ])

    # Inner courtyard walls of wings (X = -22.0 and X = +22.0, Y = -16.0..-8.0)
    walls.add_wall_with_openings("Y", -22.17, 0.34, -16.0, -8.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=[
        {"s0": -14.0, "s1": -10.0, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
    ])
    walls.add_wall_with_openings("Y", 22.17, 0.34, -16.0, -8.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=[
        {"s0": -14.0, "s1": -10.0, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
    ])

    # Outer West wall (X = -42.0) & Outer East wall (X = +42.0), Y = -16.0..+20.0
    side_ops = [
        {"s0": -13.0, "s1": -7.0, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
        {"s0": -4.0, "s1": 1.0, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
        {"s0": 8.0, "s1": 15.0, "z0": z0 + 0.75, "z1": z1 - 0.55, "kind": "window"},
    ]
    walls.add_wall_with_openings("Y", -41.83, 0.34, -16.0, 20.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=side_ops)
    walls.add_wall_with_openings("Y", 41.83, 0.34, -16.0, 20.0, z0, z1, "Mat_Arch_MurExt_Blanc", openings=side_ops)
    # Architectural window mullions on wide exterior bays
    for op in c_front_ops:
        if op["kind"] == "window" and (op["s1"] - op["s0"]) >= 3.2:
            amc.add_window_mullions(walls, "X", -7.83, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=max(2, int(round((op["s1"] - op["s0"]) / 2.0))), mat_name="Mat_Arch_Or_Ceremonie")
    for op in side_ops:
        amc.add_window_mullions(walls, "Y", -41.83, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=3)
        amc.add_window_mullions(walls, "Y", 41.83, op["s0"], op["s1"], op["z0"], op["z1"], num_bays=3)
    for s0, s1 in ((-39.5, -33.5), (-30.5, -24.5), (24.5, 30.5), (33.5, 39.5)):
        amc.add_window_mullions(walls, "X", -15.83, s0, s1, z0 + 0.75, z1 - 0.55, num_bays=3, mat_name="Mat_Arch_Or_Ceremonie")


def build_exterior():
    col = "Palais_Exterieur"
    ext_root = amc.make_empty_root("Palais_Presidentiel_Ext", col)

    site = ac.ArchMeshBuilder("Palais_Ext_Site", collection_name=col)
    walls = ac.ArchMeshBuilder("Palais_Ext_Walls", collection_name=col)
    roof_b = ac.ArchMeshBuilder("Palais_Ext_Roof", collection_name=col)
    det = ac.ArchMeshBuilder("Palais_Ext_Details", collection_name=col)

    # ------------------------------------------------------------------
    # 1. Domain Grounds (100m x 92m), Cour d'Honneur, Fountain, Gardens
    # ------------------------------------------------------------------
    site.add_box_bounds(-50.0, 50.0, -46.0, 46.0, -0.18, 0.0, "Mat_Arch_Paves_Cour", uv_scale=0.22, collider=True)
    # Formal front lawns flanking Cour d'Honneur
    site.add_box_bounds(-49.0, -23.0, -44.5, -19.5, 0.0, 0.10, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(23.0, 49.0, -44.5, -19.5, 0.0, 0.10, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    # Rear park lawns (+Y = +21..+45)
    site.add_box_bounds(-49.0, -14.0, 21.5, 45.0, 0.0, 0.10, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)
    site.add_box_bounds(14.0, 49.0, 21.5, 45.0, 0.0, 0.10, "Mat_Arch_Gazon", uv_scale=0.35, collider=True)

    # Grand Bassin Fontaine in Cour d'Honneur (X = 0, Y = -30.5)
    det.add_cylinder(0.0, -30.5, 0.0, 0.55, 6.4, 24, "Mat_Arch_Parement_Pierre")
    det.add_cylinder(0.0, -30.5, 0.12, 0.46, 5.7, 24, "Mat_Arch_Eau_Bassin")
    det.add_cylinder(0.0, -30.5, 0.0, 1.55, 1.30, 16, "Mat_Arch_Parement_Pierre")
    det.add_cylinder(0.0, -30.5, 1.55, 1.85, 2.50, 20, "Mat_Arch_Sol_Marbre")
    det.add_cylinder(0.0, -30.5, 1.85, 2.95, 0.70, 16, "Mat_Arch_Parement_Pierre")
    det.add_cylinder(0.0, -30.5, 2.95, 3.20, 1.35, 16, "Mat_Arch_Sol_Marbre")
    det.add_cone(0.0, -30.5, 3.20, 4.90, 0.35, 0.06, 12, "Mat_Arch_Or_Ceremonie")
    det.add_collider_center(0.0, -30.5, 1.2, 12.6, 12.6, 2.4)

    # Rear Lagoon-View Reflecting Pool (X = -12..+12, Y = +30..+41)
    det.add_box_bounds(-12.5, 12.5, 29.5, 41.5, 0.0, 0.45, "Mat_Arch_Parement_Pierre", collider=True)
    det.add_box_bounds(-11.6, 11.6, 30.4, 40.6, 0.10, 0.38, "Mat_Arch_Eau_Bassin")

    # Presidential Helipad on East Garden Lawn
    amc.add_helipad_marking(det, 36.0, 34.0, 0.10, radius=7.0)

    # Ceremonial Perimeter Fence (plinth + wrought iron + piers)
    # West, East, North walls
    site.add_wall_with_openings("Y", -49.85, 0.30, -46.0, 46.0, 0.0, 1.15, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("Y", 49.85, 0.30, -46.0, 46.0, 0.0, 1.15, "Mat_Arch_Parement_Pierre", collider=True)
    site.add_wall_with_openings("X", 45.85, 0.30, -46.0, 46.0, 0.0, 1.15, "Mat_Arch_Parement_Pierre", collider=True)
    for (x0, x1, y0, y1) in [
        (-50.0, -49.7, -46.0, 46.0),
        (49.7, 50.0, -46.0, 46.0),
        (-49.7, 49.7, 45.7, 46.0),
    ]:
        det.add_box_bounds(x0, x1, y0, y1, 1.15, 3.25, "Mat_Arch_Metal_Anthracite")

    # Front Fence along Y = -46.0 with 3 gate openings: (-32.5..-27.5), (-4.2..+4.2), (+27.5..+32.5)
    for sx0, sx1 in ((-49.7, -32.5), (-27.5, -4.2), (4.2, 27.5), (32.5, 49.7)):
        site.add_wall_with_openings("X", -45.85, 0.30, sx0, sx1, 0.0, 1.15, "Mat_Arch_Parement_Pierre", collider=True)
        det.add_box_bounds(sx0, sx1, -46.0, -45.7, 1.15, 3.35, "Mat_Arch_Metal_Anthracite")
        det.add_box_bounds(sx0, sx1, -45.95, -45.75, 3.35, 3.55, "Mat_Arch_Or_Ceremonie")
    # Monumental gate piers
    for px in (-32.7, -27.3, -4.4, 4.4, 27.3, 32.7):
        det.add_box_center(px, -45.85, 2.05, 0.90, 0.90, 4.10, "Mat_Arch_Parement_Pierre", collider=True)
        det.add_box_center(px, -45.85, 4.30, 0.50, 0.50, 0.40, "Mat_Arch_Or_Ceremonie")

    # 2 Guard Pavilions flanking central gate
    for gx in (-7.6, 5.2):
        site.add_wall_with_openings(
            "X", -45.2, 0.22, gx, gx + 2.4, 0.0, 3.10, "Mat_Arch_Parement_Pierre",
            openings=[{"s0": gx + 0.5, "s1": gx + 1.9, "z0": 1.0, "z1": 2.2, "kind": "window"}],
        )
        site.add_wall_with_openings(
            "X", -42.6, 0.22, gx, gx + 2.4, 0.0, 3.10, "Mat_Arch_Parement_Pierre",
            openings=[{"s0": gx + 0.5, "s1": gx + 1.9, "z0": 1.0, "z1": 2.2, "kind": "window"}],
        )
        site.add_wall_with_openings("Y", gx + 0.1, 0.22, -45.2, -42.6, 0.0, 3.10, "Mat_Arch_Parement_Pierre")
        site.add_wall_with_openings(
            "Y", gx + 2.3, 0.22, -45.2, -42.6, 0.0, 3.10, "Mat_Arch_Parement_Pierre",
            openings=[{"s0": -44.4, "s1": -43.4, "z0": 0.0, "z1": 2.15, "kind": "door"}],
        )
        det.add_box_bounds(gx - 0.2, gx + 2.6, -45.5, -42.3, 3.10, 3.40, "Mat_Arch_Or_Ceremonie")

    # Flagpoles, Palms, Lamps
    for fx, fd in ((-13.0, "+X"), (-10.0, "+X"), (10.0, "-X"), (13.0, "-X")):
        amc.add_flagpole_ivorian(det, fx, -19.0, 0.0, height=13.0, flag_dir=fd, police_flag=False)
    for px, py in [
        (-28.0, -35.0), (-38.0, -35.0), (28.0, -35.0), (38.0, -35.0),
        (-28.0, -24.0), (-38.0, -24.0), (28.0, -24.0), (38.0, -24.0),
        (-20.0, 32.0), (-32.0, 32.0), (20.0, 32.0),
    ]:
        amc.add_palm_tree(det, px, py, 0.0, height=7.2)
    for lx, ly in [(-14.0, -38.0), (14.0, -38.0), (-14.0, -23.0), (14.0, -23.0), (-15.0, 26.0), (15.0, 26.0)]:
        amc.add_streetlamp(det, lx, ly, 0.0, height=5.8)

    # ------------------------------------------------------------------
    # 2. Palace Podium, Grand Perron, Red Carpet & Rear Garden Terrace
    # ------------------------------------------------------------------
    site.add_box_bounds(-22.0, 22.0, -11.2, 20.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.4, collider=True)
    site.add_box_bounds(-42.0, -22.0, -17.5, 20.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.4, collider=True)
    site.add_box_bounds(22.0, 42.0, -17.5, 20.0, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", uv_scale=0.4, collider=True)
    # Grand Perron steps (-9.5..+9.5, -16.0..-11.2 rising Z 0.0 -> 1.20)
    for s in range(6):
        sy0 = -16.0 + s * 0.80
        sz = (s + 1) * 0.20
        site.add_box_bounds(-9.5, 9.5, sy0, -11.2, 0.0, sz, "Mat_Arch_Sol_Marbre", collider=True)
        det.add_box_bounds(-2.5, 2.5, sy0 - 0.015, sy0 + 0.82, sz - 0.20, sz + 0.025, "Mat_Arch_Tapis_Rouge")
    det.add_box_bounds(-2.5, 2.5, -11.2, -8.0, Z_RDC, Z_RDC_F + 0.01, "Mat_Arch_Tapis_Rouge")
    # Rear garden terrace & steps
    site.add_box_bounds(-20.0, 20.0, 20.0, 25.5, 0.0, Z_RDC, "Mat_Arch_Parement_Pierre", collider=True)
    for s in range(6):
        sy1 = 25.5 + (s + 1) * 0.70
        sz = 1.20 - (s + 1) * 0.20
        if sz > 0.05:
            site.add_box_bounds(-6.5, 6.5, 25.5, sy1, 0.0, sz, "Mat_Arch_Sol_Marbre", collider=True)

    # ------------------------------------------------------------------
    # 3. Palace Exterior Walls (RDC, R+1, R+2) & Modernist Overhangs
    # ------------------------------------------------------------------
    _add_palace_shell_level(walls, Z_RDC, Z_RDC_C, include_main_entry=True)
    _add_palace_shell_level(walls, Z_R1, Z_R1_C, include_main_entry=False)
    _add_palace_shell_level(walls, Z_R2, Z_R2_C, include_main_entry=False)

    # Perimeter exterior slab trim / cantilevered modernist eaves (zero overlap on interior voids!)
    for z0, z1, ov in ((Z_RDC_C, Z_R1, 0.65), (Z_R1_C, Z_R2, 0.85)):
        # Corps central perimeter band & wings
        det.add_box_bounds(-22.0, 22.0, -8.0 - ov, -7.95, z0, z1, "Mat_Arch_Parement_Pierre")
        det.add_box_bounds(-42.0 - ov, -22.0, -16.0 - ov, -15.95, z0, z1, "Mat_Arch_Parement_Pierre")
        det.add_box_bounds(22.0, 42.0 + ov, -16.0 - ov, -15.95, z0, z1, "Mat_Arch_Parement_Pierre")
        det.add_box_bounds(-42.0 - ov, 42.0 + ov, 19.95, 20.0 + ov, z0, z1, "Mat_Arch_Parement_Pierre")
        det.add_box_bounds(-42.0 - ov, -41.95, -16.0 - ov, 20.0 + ov, z0, z1, "Mat_Arch_Parement_Pierre")
        det.add_box_bounds(41.95, 42.0 + ov, -16.0 - ov, 20.0 + ov, z0, z1, "Mat_Arch_Parement_Pierre")

    # Grand R+1 Presidential Balcony over South Colonnade (X = -22..+22, Y = -11.2..-8.0)
    det.add_box_bounds(-22.0, 22.0, -11.2, -8.0, Z_RDC_C, Z_R1_F, "Mat_Arch_Sol_Marbre", collider=True)
    det.add_glass_railing("X", -11.1, -22.0, 22.0, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    # Balcony roof canopy at R+2 level
    det.add_box_bounds(-22.5, 22.5, -11.5, -8.0, Z_R1_C, Z_R2, "Mat_Arch_Parement_Pierre", collider=True)

    # Monumental Colonnades (10 columns across Corps Central + 4 per Wing)
    for cx in (-20.0, -16.0, -12.0, -7.2, -3.6, 3.6, 7.2, 12.0, 16.0, 20.0):
        amc.add_colonnade_pillar(det, cx, -10.35, Z_RDC, Z_R1_C, radius=0.46)
    for cx in (-39.5, -34.5, -29.5, -24.5, 24.5, 29.5, 34.5, 39.5):
        amc.add_colonnade_pillar(det, cx, -17.0, Z_RDC, Z_R1_C, radius=0.38)
    det.add_box_bounds(-42.2, -22.0, -17.5, -16.0, Z_R1_C, Z_R2, "Mat_Arch_Parement_Pierre")
    det.add_box_bounds(22.0, 42.2, -17.5, -16.0, Z_R1_C, Z_R2, "Mat_Arch_Parement_Pierre")
    # Decorative gold vertical frieze ribs on R+2 upper facade
    for cx in (-18.0, -12.0, -6.0, 6.0, 12.0, 18.0):
        det.add_box_center(cx, -8.12, (Z_R2 + Z_R2_C) * 0.5, 0.28, 0.24, Z_R2_C - Z_R2, "Mat_Arch_Or_Ceremonie")

    # ------------------------------------------------------------------
    # 4. Roof Slabs, Parapets, Central Presidential Pediment Crest
    # ------------------------------------------------------------------
    roof_b.add_box_bounds(-22.0, 22.0, -8.0, 20.0, Z_R2_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    roof_b.add_box_bounds(-42.0, -22.0, -16.0, 20.0, Z_R2_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    roof_b.add_box_bounds(22.0, 42.0, -16.0, 20.0, Z_R2_C, Z_ROOF, "Mat_Arch_Toiture_Terrasse", uv_scale=0.35, collider=True)
    # Broad overhanging roof cornice
    roof_b.add_box_bounds(-43.0, -22.0, -17.0, 21.0, Z_ROOF - 0.35, Z_ROOF, "Mat_Arch_Parement_Pierre")
    roof_b.add_box_bounds(22.0, 43.0, -17.0, 21.0, Z_ROOF - 0.35, Z_ROOF, "Mat_Arch_Parement_Pierre")
    roof_b.add_box_bounds(-22.0, 22.0, -9.5, 21.0, Z_ROOF - 0.35, Z_ROOF, "Mat_Arch_Parement_Pierre")

    # Roof parapets & monumental front pediment
    roof_b.add_wall_with_openings("X", -7.85, 0.30, -22.0, 22.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", -15.85, 0.30, -42.0, -22.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", -15.85, 0.30, 22.0, 42.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("X", 19.85, 0.30, -42.0, 42.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", -41.85, 0.30, -16.0, 20.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")
    roof_b.add_wall_with_openings("Y", 41.85, 0.30, -16.0, 20.0, Z_ROOF, Z_PARAPET, "Mat_Arch_MurExt_Blanc")

    # Central Presidential Fronton / Crest & Gold Emblem + Flagpole on Roof
    roof_b.add_box_bounds(-10.5, 10.5, -8.3, -7.3, Z_PARAPET, Z_PARAPET + 1.35, "Mat_Arch_Parement_Pierre")
    roof_b.add_box_bounds(-10.8, 10.8, -8.5, -7.2, Z_PARAPET + 1.35, Z_PARAPET + 1.65, "Mat_Arch_Or_Ceremonie")
    roof_b.add_cylinder(0.0, -8.45, Z_PARAPET + 0.60, Z_PARAPET + 0.85, 0.90, 16, "Mat_Arch_Or_Ceremonie", rot_euler=(1.5708, 0.0, 0.0))
    amc.add_flagpole_ivorian(roof_b, 0.0, -7.8, Z_PARAPET + 1.65, height=6.5, flag_dir="+X")

    site.build_object(parent=ext_root)
    walls.build_object(parent=ext_root)
    roof_obj = roof_b.build_object(parent=ext_root)
    det.build_object(parent=ext_root)

    # ------------------------------------------------------------------
    # 5. Movable Exterior Ceremonial Gates & Bronze Palace Doors
    # ------------------------------------------------------------------
    door_specs = []
    door_objs = []

    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Palais_Principal_L",
        pivot=(-4.2, -45.85, 0.0),
        bounds=(0.0, 4.15, -0.08, 0.08, 0.08, 3.45),
        mat_panel="Mat_Arch_Metal_Anthracite",
        rot_deg=(0.0, 0.0, 28.0),
        open_note="Grand portail d'honneur gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Palais_Principal_R",
        pivot=(4.2, -45.85, 0.0),
        bounds=(-4.15, 0.0, -0.08, 0.08, 0.08, 3.45),
        mat_panel="Mat_Arch_Metal_Anthracite",
        rot_deg=(0.0, 0.0, -28.0),
        open_note="Grand portail d'honneur droit",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Palais_Ouest",
        pivot=(-32.5, -45.85, 0.0),
        bounds=(0.0, 5.0, -0.07, 0.07, 0.08, 3.20),
        mat_panel="Mat_Arch_Metal_Anthracite",
        offset=(-3.5, 0.0, 0.0),
        is_sliding=True,
        slide_axis="X",
        open_note="Portail Protocole Ouest",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Gate_Palais_Est",
        pivot=(27.5, -45.85, 0.0),
        bounds=(0.0, 5.0, -0.07, 0.07, 0.08, 3.20),
        mat_panel="Mat_Arch_Metal_Anthracite",
        offset=(3.5, 0.0, 0.0),
        is_sliding=True,
        slide_axis="X",
        open_note="Portail Service Est",
    )
    # Bronze & Gold Monumental Entry Doors (X = -2.4..+2.4 at Y = -8.0)
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Entree_L",
        pivot=(-2.4, -7.83, Z_RDC_F),
        bounds=(0.0, 2.36, -0.06, 0.06, 0.0, 3.80),
        mat_panel="Mat_Arch_Or_Ceremonie",
        glass_bounds=(0.35, 2.00, -0.02, 0.02, 0.55, 3.30),
        rot_deg=(0.0, 0.0, 24.0),
        open_note="Porte monumentale bronze/or gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Entree_R",
        pivot=(2.4, -7.83, Z_RDC_F),
        bounds=(-2.36, 0.0, -0.06, 0.06, 0.0, 3.80),
        mat_panel="Mat_Arch_Or_Ceremonie",
        glass_bounds=(-2.00, -0.35, -0.02, 0.02, 0.55, 3.30),
        rot_deg=(0.0, 0.0, -24.0),
        open_note="Porte monumentale bronze/or droite",
    )
    # Rear garden doors & R+1 balcony doors
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Jardin_L",
        pivot=(-2.0, 19.83, Z_RDC_F),
        bounds=(0.0, 1.96, -0.04, 0.04, 0.0, 3.30),
        mat_panel="Mat_Arch_Bois_Exterieur",
        glass_bounds=(0.25, 1.70, -0.015, 0.015, 0.35, 3.00),
        open_note="Sortie jardin lagune gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Jardin_R",
        pivot=(2.0, 19.83, Z_RDC_F),
        bounds=(-1.96, 0.0, -0.04, 0.04, 0.0, 3.30),
        mat_panel="Mat_Arch_Bois_Exterieur",
        glass_bounds=(-1.70, -0.25, -0.015, 0.015, 0.35, 3.00),
        open_note="Sortie jardin lagune droite",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Balcon_L",
        pivot=(-2.2, -7.83, Z_R1_F),
        bounds=(0.0, 2.16, -0.04, 0.04, 0.0, 3.30),
        mat_panel="Mat_Arch_Or_Ceremonie",
        glass_bounds=(0.25, 1.90, -0.015, 0.015, 0.35, 3.00),
        open_note="Acces balcon presidentiel R+1 gauche",
    )
    _make_door(
        door_specs, door_objs, ext_root, col,
        "Door_Palais_Balcon_R",
        pivot=(2.2, -7.83, Z_R1_F),
        bounds=(-2.16, 0.0, -0.04, 0.04, 0.0, 3.30),
        mat_panel="Mat_Arch_Or_Ceremonie",
        glass_bounds=(-1.90, -0.25, -0.015, 0.015, 0.35, 3.00),
        open_note="Acces balcon presidentiel R+1 droite",
    )

    ext_colliders = site.colliders + walls.colliders + roof_b.colliders + det.colliders
    return ext_root, ext_colliders, door_specs, door_objs, [roof_obj.name]


def _build_wing_stair_and_elev(b, side, z0, z1):
    """Build West or East wing staircase + elevator shaft between z0 and z1."""
    z_mid = (z0 + z1) * 0.5
    if side == "W":
        sx0, sx1 = -26.8, -22.3
        ex0, ex1 = -29.6, -26.8
        f1_x, f2_x = -25.65, -23.45
        door_center = -28.2
    else:
        sx0, sx1 = 22.3, 26.8
        ex0, ex1 = 26.8, 29.6
        f1_x, f2_x = 23.45, 25.65
        door_center = 28.2

    # U-staircase (Y = +6.5..+14.0, opens South at Y = +6.5)
    b.add_stair_flight("Y", f1_x, 2.0, 6.7, 11.2, z0, z_mid, 12, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")
    b.add_box_bounds(sx0, sx1, 11.2, 14.0, z_mid - 0.22, z_mid, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(sx0, sx1, 11.2, 14.0, z_mid, z_mid + 0.04, "Mat_Arch_Sol_Marbre")
    b.add_stair_flight("Y", f2_x, 2.0, 11.2, 6.7, z_mid, z1, 12, "Mat_Arch_Sol_Marbre", "Mat_Arch_Parement_Pierre")

    # Elevator shaft (Y = +6.5..+9.5, opens South directly onto Grand Gallery Y = +6.0!)
    b.add_wall_with_openings("Y", ex0 + 0.10, 0.20, 6.5, 9.5, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("Y", ex1 - 0.10, 0.20, 6.5, 9.5, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("X", 9.40, 0.20, ex0, ex1, z0, z1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings(
        "X", 6.60, 0.20, ex0, ex1, z0, z1, "Mat_Arch_Beton_Brut",
        openings=[{"s0": door_center - 0.65, "s1": door_center + 0.65, "z0": z0, "z1": z0 + 2.30, "kind": "door"}],
    )


def build_interior():
    col = "Palais_Interieur"
    int_root = amc.make_empty_root("Palais_Presidentiel_Int", col)

    rdc_s = ac.ArchMeshBuilder("Palais_Int_RDC_Structure", collection_name=col)
    rdc_f = ac.ArchMeshBuilder("Palais_Int_RDC_Mobilier", collection_name=col)
    r1_s = ac.ArchMeshBuilder("Palais_Int_R1_Structure", collection_name=col)
    r1_f = ac.ArchMeshBuilder("Palais_Int_R1_Mobilier", collection_name=col)
    r2_s = ac.ArchMeshBuilder("Palais_Int_R2_Structure", collection_name=col)
    r2_f = ac.ArchMeshBuilder("Palais_Int_R2_Mobilier", collection_name=col)

    door_specs = []
    door_objs = []
    r1_names = ["Palais_Int_R1_Structure", "Palais_Int_R1_Mobilier"]
    r2_names = ["Palais_Int_R2_Structure", "Palais_Int_R2_Mobilier"]
    r1_doors = []
    r2_doors = []
    point_lights = []

    # ==================================================================
    # RDC (Z = 1.20 .. 6.00 m)
    # ==================================================================
    # Finish marble floors across entire H-plan
    rdc_s.add_box_bounds(-22.0, 22.0, -7.7, 19.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Marbre", uv_scale=0.4, collider=True)
    rdc_s.add_box_bounds(-41.7, -22.0, -15.7, 19.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Marbre", uv_scale=0.4, collider=True)
    rdc_s.add_box_bounds(22.0, 41.7, -15.7, 19.7, Z_RDC, Z_RDC_F, "Mat_Arch_Sol_Marbre", uv_scale=0.4, collider=True)
    # Parquet in Salon d'Honneur / Protocole & Red Carpet in Salle des Pas Perdus
    rdc_s.add_box_bounds(-21.8, -14.2, -7.6, 1.8, Z_RDC_F, Z_RDC_F + 0.012, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    rdc_s.add_box_bounds(14.2, 21.8, -7.6, 1.8, Z_RDC_F, Z_RDC_F + 0.012, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    rdc_s.add_box_bounds(-2.5, 2.5, -7.7, 8.0, Z_RDC_F, Z_RDC_F + 0.015, "Mat_Arch_Tapis_Rouge")

    # Grand Imperial Y-Staircase inside Atrium North (X = -5.5..+5.5, Y = +7.5..+19.5)
    z_mid = (Z_RDC_F + Z_R1_F) * 0.5
    rdc_s.add_stair_flight("Y", 0.0, 4.40, 8.0, 14.2, Z_RDC_F, z_mid, 14, "Mat_Arch_Tapis_Rouge", "Mat_Arch_Parement_Pierre")
    rdc_s.add_box_bounds(-5.5, 5.5, 14.2, 19.5, z_mid - 0.25, z_mid, "Mat_Arch_Parement_Pierre", collider=True)
    rdc_s.add_box_bounds(-5.5, 5.5, 14.2, 19.5, z_mid, z_mid + 0.04, "Mat_Arch_Sol_Marbre")
    rdc_s.add_box_bounds(-2.2, 2.2, 14.2, 19.3, z_mid + 0.04, z_mid + 0.06, "Mat_Arch_Tapis_Rouge")
    rdc_s.add_stair_flight("Y", -4.0, 2.80, 14.2, 7.5, z_mid, Z_R1_F, 14, "Mat_Arch_Tapis_Rouge", "Mat_Arch_Parement_Pierre")
    rdc_s.add_stair_flight("Y", 4.0, 2.80, 14.2, 7.5, z_mid, Z_R1_F, 14, "Mat_Arch_Tapis_Rouge", "Mat_Arch_Parement_Pierre")

    # Wing staircases & elevators RDC -> R1
    _build_wing_stair_and_elev(rdc_s, "W", Z_RDC_F, Z_R1_F)
    _build_wing_stair_and_elev(rdc_s, "E", Z_RDC_F, Z_R1_F)

    # Interior double-height atrium colonnade (6 columns around Salle des Pas Perdus)
    for cx in (-11.0, 11.0):
        for cy in (-5.0, 1.5, 7.2):
            amc.add_colonnade_pillar(rdc_s, cx, cy, Z_RDC_F, Z_R1_C, radius=0.40, base_mat="Mat_Arch_Sol_Marbre", shaft_mat="Mat_Arch_MurInt_Creme")

    # RDC Partition Walls
    # Atrium side walls X = -14.0 and X = +14.0 (Y = -7.7..+6.0)
    rdc_s.add_wall_with_openings(
        "Y", -14.0, 0.24, -7.7, 6.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -4.0, "s1": -1.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.10, "kind": "door"},   # Salon Honneur
            {"s0": 2.1, "s1": 5.9, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.60, "kind": "open"},     # West Grand Gallery
        ],
    )
    rdc_s.add_wall_with_openings(
        "Y", 14.0, 0.24, -7.7, 6.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -4.0, "s1": -1.6, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.10, "kind": "door"},   # Salon Protocole
            {"s0": 2.1, "s1": 5.9, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.60, "kind": "open"},     # East Grand Gallery
        ],
    )
    # North walls of Salons at Y = +2.0 (forming Grand Corridor Y = +2.0..+6.0)
    rdc_s.add_wall_with_openings("X", 2.0, 0.22, -41.7, -14.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -33.2, "s1": -30.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.10, "kind": "door"},  # Press Auditorium
    ])
    rdc_s.add_wall_with_openings("X", 2.0, 0.22, 14.0, 41.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 30.8, "s1": 33.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.10, "kind": "door"},   # Banquet Hall
    ])
    # North corridor boundary Y = +6.0 across wings & central side-rooms
    rdc_s.add_wall_with_openings("X", 6.0, 0.22, -41.7, -5.5, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -37.6, "s1": -36.2, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "door"},  # Caisse / Tresorerie
        {"s0": -31.4, "s1": -30.1, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "door"},  # Garde Republicaine PC
        {"s0": -28.9, "s1": -27.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "open"},  # West elevator alcove
        {"s0": -26.7, "s1": -22.5, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.20, "kind": "open"},  # West stair access
        {"s0": -14.8, "s1": -12.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.90, "kind": "door"},  # Salon Accreditations
    ])
    rdc_s.add_wall_with_openings("X", 6.0, 0.22, 5.5, 41.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 12.8, "s1": 14.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.90, "kind": "door"},   # Vestiaires Apparat
        {"s0": 22.5, "s1": 26.7, "z0": Z_RDC_F, "z1": Z_RDC_F + 3.20, "kind": "open"},   # East stair access
        {"s0": 27.5, "s1": 28.9, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "open"},   # East elevator alcove
        {"s0": 30.1, "s1": 31.4, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.50, "kind": "door"},   # Toilettes VIP
        {"s0": 36.2, "s1": 37.8, "z0": Z_RDC_F, "z1": Z_RDC_F + 2.60, "kind": "door"},   # Cuisine d'Etat
    ])
    # Wing internal room dividers (Y = +6.0..+19.7)
    rdc_s.add_wall_with_openings("Y", -32.0, 0.24, 6.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_Beton_Brut")  # Caisse vault wall
    rdc_s.add_wall_with_openings("Y", -22.0, 0.24, -8.0, 2.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", -22.0, 0.24, 6.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 22.0, 0.24, -8.0, 2.0, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 22.0, 0.24, 6.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")
    rdc_s.add_wall_with_openings("Y", 32.5, 0.22, 6.0, 19.7, Z_RDC_F, Z_RDC_C, "Mat_Arch_MurInt_Creme")

    # --- RDC Furniture ---
    # 2 Giant Chandeliers in Salle des Pas Perdus (hanging from Z_R1_C!)
    amc.add_chandelier(rdc_f, 0.0, -3.5, Z_R1_C, drop=2.2, radius=1.85)
    amc.add_chandelier(rdc_f, 0.0, 3.5, Z_R1_C, drop=2.2, radius=1.85)
    # Salon d'Honneur & Protocole lounges
    amc.add_sofa_lounge(rdc_f, -18.0, -3.0, Z_RDC_F, fabric_mat="Mat_Arch_Tapis_Rouge")
    amc.add_chandelier(rdc_f, -18.0, -3.0, Z_RDC_C, drop=1.2, radius=1.0)
    amc.add_sofa_lounge(rdc_f, 18.0, -3.0, Z_RDC_F, fabric_mat="Mat_Arch_Tissu_Mobilier")
    amc.add_chandelier(rdc_f, 18.0, -3.0, Z_RDC_C, drop=1.2, radius=1.0)

    # Press Conference Auditorium (West Wing South: -41.7..-22.0, -15.7..+2.0)
    rdc_f.add_box_bounds(-40.5, -23.5, -14.5, -10.5, Z_RDC_F, Z_RDC_F + 0.45, "Mat_Arch_Bois_Interieur", collider=True)
    amc.add_podium_lectern(rdc_f, -32.0, -11.5, Z_RDC_F + 0.45, facing="+Y")
    amc.add_cctv_wall(rdc_f, -32.0, -15.2, Z_RDC_F + 0.45, axis="X", width=8.0, rows=1, cols=3, facing_sign=1.0)
    for row in range(5):
        ry = -8.2 + row * 1.8
        for cx in (-37.0, -32.0, -27.0):
            rdc_f.add_box_center(cx, ry, Z_RDC_F + 0.25, 3.6, 0.60, 0.48, "Mat_Arch_Tapis_Rouge", collider=True)
            rdc_f.add_box_center(cx, ry + 0.24, Z_RDC_F + 0.62, 3.6, 0.12, 0.50, "Mat_Arch_Tapis_Rouge")

    # Caisse / Trésorerie du Palais (-41.7..-32.0, +6.0..+19.7)
    amc.add_reception_or_caisse_counter(rdc_f, -36.8, 10.5, Z_RDC_F, length=5.2, axis="X", facing="-Y", num_windows=3, armored_glass=True)
    amc.add_office_desk(rdc_f, -39.0, 14.5, Z_RDC_F, facing="-Y", width=1.8, executive=True)
    amc.add_safe_vault(rdc_f, -34.2, 17.8, Z_RDC_F, facing="-Y", size=(2.2, 1.5, 2.3))

    # Republican Guard Security Command (-32.0..-27.5, +9.8..+19.7 & -32.0..-29.8, +6.0..+9.8)
    amc.add_cctv_wall(rdc_f, -29.8, 19.2, Z_RDC_F, axis="X", width=3.8, rows=2, cols=3)
    amc.add_office_desk(rdc_f, -29.8, 15.8, Z_RDC_F, facing="+Y", width=2.0)
    amc.add_armory_rack(rdc_f, -31.2, 11.8, Z_RDC_F, length=3.2, axis="Y")

    # Grand State Banquet Hall (East Wing South: +22.0..+41.7, -15.7..+2.0)
    for bx in (26.5, 32.0, 37.5):
        amc.add_conference_table(
            rdc_f, bx, -7.2, Z_RDC_F, length=11.0, width=1.8, axis="Y", num_chairs_side=8,
            table_mat="Mat_Arch_MurExt_Blanc", chair_mat="Mat_Arch_Tapis_Rouge",
        )
    for cx in (27.5, 36.5):
        for cy in (-11.0, -3.5):
            amc.add_chandelier(rdc_f, cx, cy, Z_RDC_C, drop=1.35, radius=1.15)
    # State Kitchen (+32.5..+41.7, +6.0..+19.7)
    for kx in (35.5, 39.2):
        rdc_f.add_box_center(kx, 12.8, Z_RDC_F + 0.45, 1.3, 6.5, 0.90, "Mat_Arch_Metal_Inox", collider=True)
    amc.add_bathroom_fixtures(rdc_f, 27.8, 32.3, 10.5, 19.2, Z_RDC_F, num_stalls=3)
    amc.add_sofa_lounge(rdc_f, -14.0, 13.0, Z_RDC_F, fabric_mat="Mat_Arch_Tissu_Mobilier")

    for (lx0, lx1, ly0, ly1, nx, ny) in [
        (-40.0, -23.0, -14.0, 1.5, 2, 2),
        (23.0, 40.0, -14.0, 1.5, 2, 2),
        (-40.0, -28.0, 7.0, 18.5, 2, 2),
        (28.0, 40.0, 7.0, 18.5, 2, 2),
    ]:
        amc.add_ceiling_lights_grid(rdc_s, lx0, lx1, ly0, ly1, Z_RDC_C, nx=nx, ny=ny)
    point_lights.extend([
        (0.0, 0.0, Z_R1_C - 1.5, 380.0, 4.0),
        (-32.0, -7.0, Z_RDC_C - 0.8, 220.0, 3.0),
        (32.0, -7.0, Z_RDC_C - 0.8, 220.0, 3.0),
        (-36.5, 13.0, Z_RDC_C - 0.8, 160.0, 2.2),
    ])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Salon_Honneur_L", (-14.0, -4.0, Z_RDC_F), (-0.04, 0.04, 0.0, 1.18, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Salon d'Honneur G"),
        ("Door_Salon_Honneur_R", (-14.0, -1.6, Z_RDC_F), (-0.04, 0.04, -1.18, 0.0, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Salon d'Honneur D"),
        ("Door_Salon_Protocole_L", (14.0, -4.0, Z_RDC_F), (-0.04, 0.04, 0.0, 1.18, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Salon Protocole G"),
        ("Door_Salon_Protocole_R", (14.0, -1.6, Z_RDC_F), (-0.04, 0.04, -1.18, 0.0, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Salon Protocole D"),
        ("Door_Presse_L", (-33.2, 2.0, Z_RDC_F), (0.0, 1.18, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 25.0), "Salle de Presse G"),
        ("Door_Presse_R", (-30.8, 2.0, Z_RDC_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Bois_Interieur", (0.0, 0.0, -25.0), "Salle de Presse D"),
        ("Door_Banquets_L", (30.8, 2.0, Z_RDC_F), (0.0, 1.18, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 25.0), "Salle des Banquets G"),
        ("Door_Banquets_R", (33.2, 2.0, Z_RDC_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, -25.0), "Salle des Banquets D"),
        ("Door_Caisse_Palais", (-37.6, 6.0, Z_RDC_F), (0.0, 1.35, -0.04, 0.04, 0.0, 2.45), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Porte blindee Caisse / Tresorerie"),
        ("Door_Garde_Rep_RDC", (-31.4, 6.0, Z_RDC_F), (0.0, 1.25, -0.04, 0.04, 0.0, 2.45), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "PC Garde Republicaine"),
        ("Door_Cuisine_RDC", (36.2, 6.0, Z_RDC_F), (0.0, 1.55, -0.04, 0.04, 0.0, 2.50), "Mat_Arch_Metal_Inox", (0.0, 0.0, 0.0), "Cuisine d'Etat"),
        ("Door_Sanitaires_Palais_RDC", (30.1, 6.0, Z_RDC_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.40), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Sanitaires d'Apparat"),
        ("Door_Asc_Ouest_RDC", (-28.85, 6.60, Z_RDC_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Ouest RDC"),
        ("Door_Asc_Est_RDC", (27.55, 6.60, Z_RDC_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Est RDC"),
    ]:
        _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)

    # ==================================================================
    # R+1 (Z = 6.35 .. 10.80 m) — Bureau Président, Conseil Ministres, Réunions
    # ==================================================================
    amc.add_slab_with_cutouts(r1_s, -22.0, 22.0, -7.7, 19.7, Z_RDC_C, Z_R1, R1_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -22.0, 22.0, -7.7, 19.7, Z_R1, Z_R1_F, R1_CUTOUTS, "Mat_Arch_Sol_Marbre", collider=False)
    amc.add_slab_with_cutouts(r1_s, -41.7, -22.0, -15.7, 19.7, Z_RDC_C, Z_R1, R1_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, -41.7, -22.0, -15.7, 19.7, Z_R1, Z_R1_F, R1_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)
    amc.add_slab_with_cutouts(r1_s, 22.0, 41.7, -15.7, 19.7, Z_RDC_C, Z_R1, R1_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r1_s, 22.0, 41.7, -15.7, 19.7, Z_R1, Z_R1_F, R1_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)
    # Parquet in Presidential Office (-22..-5.5, +8..+19.7)
    r1_s.add_box_bounds(-21.8, -5.6, 8.2, 19.5, Z_R1_F, Z_R1_F + 0.012, "Mat_Arch_Sol_Parquet", uv_scale=0.5)

    # Mezzanine balustrades wrapping the Atrium & Imperial Stairwell void!
    r1_s.add_glass_railing("X", -5.45, -10.9, 10.9, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("Y", -10.9, -5.45, 7.5, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("Y", 10.9, -5.45, 7.5, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 7.5, -10.9, -5.4, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 7.5, -2.5, 2.5, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 7.5, 5.4, 10.9, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("Y", -5.4, 7.5, 19.5, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("Y", 5.4, 7.5, 19.5, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")
    r1_s.add_glass_railing("X", 19.4, -5.4, 5.4, Z_R1_F, height=1.08, post_mat="Mat_Arch_Or_Ceremonie")

    _build_wing_stair_and_elev(r1_s, "W", Z_R1_F, Z_R2_F)
    _build_wing_stair_and_elev(r1_s, "E", Z_R1_F, Z_R2_F)

    # R+1 Interior Partitions
    r1_s.add_wall_with_openings("Y", -14.0, 0.22, -7.7, 6.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -4.0, "s1": -2.0, "z0": Z_R1_F, "z1": Z_R1_F + 2.80, "kind": "door"},   # Secretariat Particulier
        {"s0": 2.2, "s1": 5.8, "z0": Z_R1_F, "z1": Z_R1_F + 3.40, "kind": "open"},
    ])
    r1_s.add_wall_with_openings("Y", 14.0, 0.22, -7.7, 6.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -4.0, "s1": -2.0, "z0": Z_R1_F, "z1": Z_R1_F + 2.80, "kind": "door"},   # Attente Ministres
        {"s0": 2.2, "s1": 5.8, "z0": Z_R1_F, "z1": Z_R1_F + 3.40, "kind": "open"},
    ])
    # North gallery & Presidential Office / Cabinet walls
    r1_s.add_wall_with_openings("X", 8.0, 0.24, -22.0, -5.5, Z_R1_F, Z_R1_C, "Mat_Arch_Bois_Interieur", openings=[
        {"s0": -10.5, "s1": -8.1, "z0": Z_R1_F, "z1": Z_R1_F + 3.10, "kind": "door"},  # Grand Bureau President
    ])
    r1_s.add_wall_with_openings("X", 8.0, 0.24, 5.5, 22.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 8.1, "s1": 10.5, "z0": Z_R1_F, "z1": Z_R1_F + 3.10, "kind": "door"},    # Cabinet / Sec Gen
    ])
    r1_s.add_wall_with_openings("Y", -22.0, 0.22, -8.0, 19.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 2.2, "s1": 5.8, "z0": Z_R1_F, "z1": Z_R1_F + 3.30, "kind": "open"},     # West Wing Gallery link
    ])
    r1_s.add_wall_with_openings("Y", 22.0, 0.22, -8.0, 19.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 2.2, "s1": 5.8, "z0": Z_R1_F, "z1": Z_R1_F + 3.30, "kind": "open"},     # East Wing Gallery link
    ])
    # Wing corridors (Y = +2.0 and Y = +6.0)
    r1_s.add_wall_with_openings("X", 2.0, 0.22, -41.7, -22.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -33.2, "s1": -30.6, "z0": Z_R1_F, "z1": Z_R1_F + 3.20, "kind": "door"},  # Conseil des Ministres
    ])
    r1_s.add_wall_with_openings("X", 2.0, 0.22, 22.0, 41.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 29.0, "s1": 31.2, "z0": Z_R1_F, "z1": Z_R1_F + 2.90, "kind": "door"},   # Salle Traités / Reunion
    ])
    r1_s.add_wall_with_openings("X", -4.0, 0.22, 22.0, 41.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 31.0, "s1": 33.4, "z0": Z_R1_F, "z1": Z_R1_F + 3.00, "kind": "door"},   # Salle Defense
    ])
    r1_s.add_wall_with_openings("X", 6.0, 0.22, -41.7, -22.0, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -36.0, "s1": -34.7, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "door"},  # Conseillers
        {"s0": -28.9, "s1": -27.5, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "open"},  # West elevator alcove
        {"s0": -26.7, "s1": -22.5, "z0": Z_R1_F, "z1": Z_R1_F + 3.10, "kind": "open"},
    ])
    r1_s.add_wall_with_openings("X", 6.0, 0.22, 22.0, 41.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 22.5, "s1": 26.7, "z0": Z_R1_F, "z1": Z_R1_F + 3.10, "kind": "open"},
        {"s0": 27.5, "s1": 28.9, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "open"},   # East elevator alcove
        {"s0": 34.5, "s1": 35.8, "z0": Z_R1_F, "z1": Z_R1_F + 2.50, "kind": "door"},   # Etat-Major
    ])
    r1_s.add_wall_with_openings("Y", -31.5, 0.20, 6.0, 19.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")
    r1_s.add_wall_with_openings("Y", 31.5, 0.20, 6.0, 19.7, Z_R1_F, Z_R1_C, "Mat_Arch_MurInt_Creme")

    # R+1 Furniture
    # Grand Bureau du Président (-22..-5.5, +8..+19.7)
    r1_f.add_box_center(-13.8, 13.8, Z_R1_F + 0.02, 7.5, 5.5, 0.02, "Mat_Arch_Tapis_Rouge")
    amc.add_office_desk(r1_f, -13.8, 16.0, Z_R1_F, facing="-Y", width=3.0, depth=1.25, executive=True)
    amc.add_sofa_lounge(r1_f, -13.8, 11.4, Z_R1_F, fabric_mat="Mat_Arch_Tapis_Rouge")
    amc.add_flagpole_ivorian(r1_f, -18.5, 18.2, Z_R1_F, height=3.2, flag_dir="+X")
    amc.add_chandelier(r1_f, -13.8, 13.8, Z_R1_C, drop=1.15, radius=1.05)

    # Cabinet / Secrétariat Général (+5.5..+22, +8..+19.7)
    amc.add_office_desk(r1_f, 13.8, 15.2, Z_R1_F, facing="-Y", width=2.4, depth=1.0, executive=True)
    amc.add_conference_table(r1_f, 13.8, 10.8, Z_R1_F, length=4.5, width=1.6, axis="X", num_chairs_side=4)
    amc.add_office_desk(r1_f, -18.0, -3.0, Z_R1_F, facing="+Y", width=2.0)
    amc.add_sofa_lounge(r1_f, 18.0, -3.0, Z_R1_F, fabric_mat="Mat_Arch_Tissu_Mobilier")

    # MONUMENTAL CONSEIL DES MINISTRES (-41.7..-22.0, -15.7..+2.0)
    r1_f.add_box_center(-32.0, -7.0, Z_R1_F + 0.02, 13.5, 11.5, 0.02, "Mat_Arch_Tapis_Rouge")
    amc.add_conference_table(
        r1_f, -32.0, -7.0, Z_R1_F, length=11.0, width=4.8, axis="Y", num_chairs_side=11,
        table_mat="Mat_Arch_Bois_Interieur", chair_mat="Mat_Arch_Tapis_Rouge", hollow_center=True,
    )
    amc.add_cctv_wall(r1_f, -32.0, -15.2, Z_R1_F, axis="X", width=7.0, rows=2, cols=4, facing_sign=1.0)
    for cy in (-11.0, -3.2):
        amc.add_chandelier(r1_f, -32.0, cy, Z_R1_C, drop=1.25, radius=1.25)

    # Strategic Defense Meeting Room (+22..+41.7, -15.7..-4.0) & Diplomatic Room (-4.0..+2.0)
    amc.add_conference_table(r1_f, 32.0, -9.8, Z_R1_F, length=9.5, width=2.6, axis="X", num_chairs_side=8)
    amc.add_cctv_wall(r1_f, 41.2, -9.8, Z_R1_F, axis="Y", width=5.6, rows=2, cols=3)
    amc.add_conference_table(r1_f, 32.0, -1.0, Z_R1_F, length=6.0, width=1.9, axis="X", num_chairs_side=5, chair_mat="Mat_Arch_Tapis_Rouge")
    # Advisors & Staff offices (North R+1 wings)
    amc.add_office_desk(r1_f, -36.5, 13.0, Z_R1_F, facing="-Y", width=2.0, executive=True)
    amc.add_office_desk(r1_f, 36.5, 13.0, Z_R1_F, facing="-Y", width=2.0, executive=True)

    for (lx0, lx1, ly0, ly1, nx, ny) in [
        (-40.0, -23.0, -14.0, 1.5, 2, 2),
        (23.0, 40.0, -14.0, 1.5, 2, 2),
        (-21.0, -6.5, 9.0, 18.5, 2, 2),
        (6.5, 21.0, 9.0, 18.5, 2, 2),
    ]:
        amc.add_ceiling_lights_grid(r1_s, lx0, lx1, ly0, ly1, Z_R1_C, nx=nx, ny=ny)
    point_lights.extend([
        (-32.0, -7.0, Z_R1_C - 0.8, 260.0, 3.2),
        (-13.8, 13.8, Z_R1_C - 0.8, 220.0, 2.5),
        (32.0, -9.5, Z_R1_C - 0.8, 200.0, 2.5),
    ])

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Secretariat_R1", (-14.0, -4.0, Z_R1_F), (-0.03, 0.03, 0.0, 1.95, 0.0, 2.75), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Secretariat Particulier"),
        ("Door_Attente_Min_R1", (14.0, -4.0, Z_R1_F), (-0.03, 0.03, 0.0, 1.95, 0.0, 2.75), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Attente Ministres"),
        ("Door_Bureau_President_L", (-10.5, 8.0, Z_R1_F), (0.0, 1.18, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 28.0), "Grand Bureau du President G"),
        ("Door_Bureau_President_R", (-8.1, 8.0, Z_R1_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, -28.0), "Grand Bureau du President D"),
        ("Door_Cabinet_R1_L", (8.1, 8.0, Z_R1_F), (0.0, 1.18, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Cabinet Sec Gen G"),
        ("Door_Cabinet_R1_R", (10.5, 8.0, Z_R1_F), (-1.18, 0.0, -0.04, 0.04, 0.0, 3.0), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Cabinet Sec Gen D"),
        ("Door_Conseil_Min_L", (-33.2, 2.0, Z_R1_F), (0.0, 1.28, -0.04, 0.04, 0.0, 3.10), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 32.0), "Conseil des Ministres G"),
        ("Door_Conseil_Min_R", (-30.6, 2.0, Z_R1_F), (-1.28, 0.0, -0.04, 0.04, 0.0, 3.10), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, -32.0), "Conseil des Ministres D"),
        ("Door_Diplomatique_R1", (29.0, 2.0, Z_R1_F), (0.0, 2.15, -0.04, 0.04, 0.0, 2.85), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Salle Diplomatique"),
        ("Door_Defense_R1", (31.0, -4.0, Z_R1_F), (0.0, 2.35, -0.04, 0.04, 0.0, 2.95), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Salle Reunion Defense"),
        ("Door_Conseillers_R1", (-36.0, 6.0, Z_R1_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.45), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureaux Conseillers"),
        ("Door_EtatMajor_R1", (34.5, 6.0, Z_R1_F), (0.0, 1.25, -0.03, 0.03, 0.0, 2.45), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bureaux Etat-Major"),
        ("Door_Asc_Ouest_R1", (-28.85, 6.60, Z_R1_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Ouest R+1"),
        ("Door_Asc_Est_R1", (27.55, 6.60, Z_R1_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Est R+1"),
    ]:
        o = _make_door(door_specs, door_objs, int_root, col, dname, piv, bnds, dmat, rot_deg=drot, open_note=note)
        r1_doors.append(o.name)

    # ==================================================================
    # R+2 (Z = 11.15 .. 15.15 m) — Salon Panoramique, Suite Présidentielle, Bibliothèque, Crise
    # ==================================================================
    amc.add_slab_with_cutouts(r2_s, -22.0, 22.0, -7.7, 19.7, Z_R1_C, Z_R2, R2_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, -22.0, 22.0, -7.7, 19.7, Z_R2, Z_R2_F, R2_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)
    amc.add_slab_with_cutouts(r2_s, -41.7, -22.0, -15.7, 19.7, Z_R1_C, Z_R2, R2_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, -41.7, -22.0, -15.7, 19.7, Z_R2, Z_R2_F, R2_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)
    amc.add_slab_with_cutouts(r2_s, 22.0, 41.7, -15.7, 19.7, Z_R1_C, Z_R2, R2_CUTOUTS, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_slab_with_cutouts(r2_s, 22.0, 41.7, -15.7, 19.7, Z_R2, Z_R2_F, R2_CUTOUTS, "Mat_Arch_Sol_Parquet", collider=False)

    # Stairwell enclosures & guardrails at R+2
    for side, x0, x1 in (("W", -26.8, -22.3), ("E", 22.3, 26.8)):
        r2_s.add_glass_railing("Y", x0 if side == "W" else x1, 6.5, 14.0, Z_R2_F, post_mat="Mat_Arch_Or_Ceremonie")
        r2_s.add_glass_railing("X", 14.0, x0, x1, Z_R2_F, post_mat="Mat_Arch_Or_Ceremonie")
    for side, ex0, ex1, dc in (("W", -29.6, -26.8, -28.2), ("E", 26.8, 29.6, 28.2)):
        r2_s.add_wall_with_openings("Y", ex0 + 0.10, 0.20, 6.5, 9.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
        r2_s.add_wall_with_openings("Y", ex1 - 0.10, 0.20, 6.5, 9.5, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
        r2_s.add_wall_with_openings("X", 9.40, 0.20, ex0, ex1, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut")
        r2_s.add_wall_with_openings(
            "X", 6.60, 0.20, ex0, ex1, Z_R2_F, Z_R2_C, "Mat_Arch_Beton_Brut",
            openings=[{"s0": dc - 0.65, "s1": dc + 0.65, "z0": Z_R2_F, "z1": Z_R2_F + 2.30, "kind": "door"}],
        )

    # R+2 Walls
    r2_s.add_wall_with_openings("X", 6.0, 0.22, -41.7, 41.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -35.0, "s1": -33.2, "z0": Z_R2_F, "z1": Z_R2_F + 2.50, "kind": "door"},  # Cellule Crise R+2
        {"s0": -28.9, "s1": -27.5, "z0": Z_R2_F, "z1": Z_R2_F + 2.50, "kind": "open"},
        {"s0": -26.7, "s1": -22.5, "z0": Z_R2_F, "z1": Z_R2_F + 3.00, "kind": "open"},
        {"s0": -12.0, "s1": -10.5, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "door"},  # Suite Presidentielle
        {"s0": 10.5, "s1": 12.0, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "door"},    # Suites Hotes
        {"s0": 22.5, "s1": 26.7, "z0": Z_R2_F, "z1": Z_R2_F + 3.00, "kind": "open"},
        {"s0": 27.5, "s1": 28.9, "z0": Z_R2_F, "z1": Z_R2_F + 2.50, "kind": "open"},
        {"s0": 34.0, "s1": 35.6, "z0": Z_R2_F, "z1": Z_R2_F + 2.50, "kind": "door"},    # Sport
    ])
    r2_s.add_wall_with_openings("X", 2.0, 0.22, -41.7, -22.0, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": -33.0, "s1": -31.0, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "door"},  # Bibliotheque
    ])
    r2_s.add_wall_with_openings("X", 2.0, 0.22, 22.0, 41.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 31.0, "s1": 33.0, "z0": Z_R2_F, "z1": Z_R2_F + 2.60, "kind": "door"},    # Salle a manger privee
    ])
    r2_s.add_wall_with_openings("Y", -22.0, 0.22, -8.0, 19.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 2.2, "s1": 5.8, "z0": Z_R2_F, "z1": Z_R2_F + 3.00, "kind": "open"},
    ])
    r2_s.add_wall_with_openings("Y", 22.0, 0.22, -8.0, 19.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme", openings=[
        {"s0": 2.2, "s1": 5.8, "z0": Z_R2_F, "z1": Z_R2_F + 3.00, "kind": "open"},
    ])
    r2_s.add_wall_with_openings("Y", 0.0, 0.22, 6.0, 19.7, Z_R2_F, Z_R2_C, "Mat_Arch_MurInt_Creme")

    # R+2 Furniture
    amc.add_sofa_lounge(r2_f, -7.0, -2.5, Z_R2_F, fabric_mat="Mat_Arch_Tapis_Rouge")
    amc.add_sofa_lounge(r2_f, 7.0, -2.5, Z_R2_F, fabric_mat="Mat_Arch_Tissu_Mobilier")
    amc.add_chandelier(r2_f, 0.0, -2.0, Z_R2_C, drop=1.1, radius=1.2)
    # Presidential Bed Suite (-22..0, +6..+19.7)
    r2_f.add_box_center(-11.0, 15.8, Z_R2_F + 0.30, 2.50, 2.20, 0.50, "Mat_Arch_Bois_Interieur", collider=True)
    r2_f.add_box_center(-11.0, 15.8, Z_R2_F + 0.65, 2.30, 2.00, 0.25, "Mat_Arch_MurExt_Blanc")
    amc.add_sofa_lounge(r2_f, -11.0, 10.5, Z_R2_F, fabric_mat="Mat_Arch_Tissu_Mobilier")
    # Guest Head-of-State Suite (0..+22, +6..+19.7)
    r2_f.add_box_center(11.0, 15.8, Z_R2_F + 0.30, 2.30, 2.15, 0.50, "Mat_Arch_Bois_Interieur", collider=True)
    r2_f.add_box_center(11.0, 15.8, Z_R2_F + 0.65, 2.10, 1.95, 0.24, "Mat_Arch_MurExt_Blanc")
    # Library (West South) & War Room (West North) & Private Dining (East South)
    for bx in (-38.0, -32.0, -26.0):
        r2_f.add_box_center(bx, -12.0, Z_R2_F + 1.25, 0.65, 4.8, 2.5, "Mat_Arch_Bois_Interieur", collider=True)
    amc.add_conference_table(r2_f, -35.0, 13.0, Z_R2_F, length=6.0, width=2.0, axis="X", num_chairs_side=5)
    amc.add_cctv_wall(r2_f, -41.2, 13.0, Z_R2_F, axis="Y", width=4.8, rows=2, cols=3, facing_sign=1.0)
    amc.add_conference_table(r2_f, 32.0, -7.0, Z_R2_F, length=6.8, width=2.0, axis="Y", num_chairs_side=6, chair_mat="Mat_Arch_Tapis_Rouge")

    amc.add_ceiling_lights_grid(r2_s, -40.0, 40.0, -14.0, 18.0, Z_R2_C, nx=6, ny=3)

    for dname, piv, bnds, dmat, drot, note in [
        ("Door_Bibliotheque_R2", (-33.0, 2.0, Z_R2_F), (0.0, 1.95, -0.03, 0.03, 0.0, 2.55), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Bibliotheque d'Etat"),
        ("Door_Crise_R2", (-35.0, 6.0, Z_R2_F), (0.0, 1.75, -0.04, 0.04, 0.0, 2.45), "Mat_Arch_Metal_Anthracite", (0.0, 0.0, 0.0), "Salle de Crise Restreinte"),
        ("Door_Suite_President_R2", (-12.0, 6.0, Z_R2_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.55), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Suite Presidentielle Privee"),
        ("Door_Suite_Hotes_R2", (10.5, 6.0, Z_R2_F), (0.0, 1.45, -0.03, 0.03, 0.0, 2.55), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Suite Hotes d'Etat"),
        ("Door_Salle_Manger_R2", (31.0, 2.0, Z_R2_F), (0.0, 1.95, -0.03, 0.03, 0.0, 2.55), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Salle a Manger Privee"),
        ("Door_Sport_R2", (34.0, 6.0, Z_R2_F), (0.0, 1.55, -0.03, 0.03, 0.0, 2.45), "Mat_Arch_Bois_Interieur", (0.0, 0.0, 0.0), "Salle de Sport"),
        ("Door_Asc_Ouest_R2", (-28.85, 6.60, Z_R2_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Ouest R+2"),
        ("Door_Asc_Est_R2", (27.55, 6.60, Z_R2_F), (0.0, 1.25, -0.02, 0.02, 0.0, 2.25), "Mat_Arch_Or_Ceremonie", (0.0, 0.0, 0.0), "Ascenseur Est R+2"),
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
        "name": "Palais_Presidentiel",
        "lot_meters": [100.0, 92.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Palais_Presidentiel.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Palais_Presidentiel_Interieur.fbx",
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
            ("ext_front_3q", (64.0, -74.0, 34.0), (0.0, -8.0, 6.5), None),
            ("ext_cour_honneur", (-18.0, -44.0, 6.5), (0.0, -8.0, 7.0), None),
            ("ext_rear_3q", (-62.0, 62.0, 32.0), (2.0, 8.0, 6.0), None),
            ("cutaway_rdc", (48.0, -48.0, 56.0), (0.0, 2.0, 2.0), roof_names + hide_rdc),
            ("cutaway_r1", (48.0, -48.0, 60.0), (0.0, 2.0, 7.0), roof_names + hide_r1),
            ("int_pas_perdus", (-8.5, -6.5, 4.2), (1.0, 10.0, 3.8), None),
            ("int_caisse", (-40.5, 7.2, 3.3), (-35.5, 14.5, 2.1), None),
            ("int_banquets", (24.0, 0.5, 4.2), (34.0, -9.5, 2.2), None),
            ("int_conseil_ministres", (-24.5, 0.5, 9.2), (-33.0, -9.0, 7.3), None),
            ("int_bureau_president", (-7.5, 9.5, 8.8), (-15.0, 15.5, 7.3), None),
        ]
        amc.render_monument_previews(preview_dir, "palais", cam_specs, interior_point_lights=lights)

    print(
        f"[Palais_Presidentiel] Built OK: ext_colliders={len(ext_cols)}, "
        f"int_colliders={len(int_cols)}, ext_doors={len(ext_doors_meta)}, "
        f"int_doors={len(int_doors_meta)}"
    )


if __name__ == "__main__":
    main()
