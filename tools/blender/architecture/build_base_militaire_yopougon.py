"""Build a complete 1:1 scale Military Base at the extremity of Yopougon (Abidjan)
for Ivory State ("Base Militaire de Yopougon - Camp Nord / Lisiere Forestiere").

Dimensions & World Placement:
- Main Fortified Compound: 220.0 m (X: North-South in Unity) x 240.0 m (Y: West-East in Unity)
- Western Access Boulevard & North-West Logistics Spur: extends to Y = -176.0 m
  to connect directly with "Route de prison, voie de Yopougon-N'Dotre".
- Unity Placement: Position = (-660.0, 0.0, 745.0), ToStreet = (-1.0, 0.0, 0.0)
- Scale: 1 Blender unit = 1.0 meter (1:1).
- Orientation: -Y faces the main access street (West in Unity), +Z is Up.

Modular Blender Collections created in Base_Militaire_Yopougon.blend:
1. BaseMilitaire_01_Perimetre_Securite
2. BaseMilitaire_02_Commandement_QG
3. BaseMilitaire_03_Casernes_Troupes
4. BaseMilitaire_04_Mess_Infirmerie
5. BaseMilitaire_05_Munitions_Logistique
6. BaseMilitaire_06_Vehicules_Ateliers
7. BaseMilitaire_07_Zone_Aerienne_Heliport
8. BaseMilitaire_08_Voirie_Environnement
9. BaseMilitaire_09_Interieurs
10. BaseMilitaire_10_Interactifs_Spawns

Outputs:
- tools/blender/architecture/Base_Militaire_Yopougon.blend
- IvoryState/Assets/_Project/Art/Models/Architecture/Monuments/SM_Base_Militaire_Yopougon.fbx
- IvoryState/Assets/_Project/Art/Models/Architecture/Interieurs/SM_Base_Militaire_Yopougon_Interieur.fbx
- tools/blender/architecture/base_militaire_yopougon_manifest.json
"""

import math
import os
import sys
import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import arch_common as ac
import arch_monuments_common as amc

REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
BLEND_PATH = os.path.join(SCRIPT_DIR, "Base_Militaire_Yopougon.blend")
EXT_FBX_PATH = os.path.join(
    REPO_ROOT,
    "IvoryState",
    "Assets",
    "_Project",
    "Art",
    "Models",
    "Architecture",
    "Monuments",
    "SM_Base_Militaire_Yopougon.fbx",
)
INT_FBX_PATH = os.path.join(
    REPO_ROOT,
    "IvoryState",
    "Assets",
    "_Project",
    "Art",
    "Models",
    "Architecture",
    "Interieurs",
    "SM_Base_Militaire_Yopougon_Interieur.fbx",
)
MANIFEST_PATH = os.path.join(SCRIPT_DIR, "base_militaire_yopougon_manifest.json")
PREVIEW_DIR = os.path.join(SCRIPT_DIR, "previews")

# 1:1 Base Footprint (meters)
LOT_W = 220.0  # X in [-110.0, +110.0] (North-South in Unity when ToStreet = (-1,0,0))
LOT_D = 240.0  # Y in [-120.0, +120.0] (West-East in Unity when ToStreet = (-1,0,0))

# Modular collections for clean organization in Blender
COL_ROOT_EXT = "Base_Militaire_Exterieur"
COL_ROOT_INT = "Base_Militaire_Interieur"
COL_01_PERIMETRE = "BaseMilitaire_01_Perimetre_Securite"
COL_02_QG = "BaseMilitaire_02_Commandement_QG"
COL_03_CASERNES = "BaseMilitaire_03_Casernes_Troupes"
COL_04_MESS_INF = "BaseMilitaire_04_Mess_Infirmerie"
COL_05_LOGISTIQUE = "BaseMilitaire_05_Munitions_Logistique"
COL_06_VEHICULES = "BaseMilitaire_06_Vehicules_Ateliers"
COL_07_HELIPORT = "BaseMilitaire_07_Zone_Aerienne_Heliport"
COL_08_VOIRIE = "BaseMilitaire_08_Voirie_Environnement"
COL_09_INTERIEURS = "BaseMilitaire_09_Interieurs"
COL_10_SPAWNS = "BaseMilitaire_10_Interactifs_Spawns"


def _ensure_collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col


def _link_also_to_collection(obj, col_name):
    col = _ensure_collection(col_name)
    if obj.name not in col.objects:
        col.objects.link(obj)


def _make_door(
    door_specs,
    door_objs,
    parent,
    col_name,
    thematic_col,
    name,
    pivot,
    bounds,
    mat_panel,
    mat_frame="Mat_Arch_Metal_Anthracite",
    glass_bounds=None,
    rot_deg=(0.0, 0.0, 0.0),
    offset=(0.0, 0.0, 0.0),
    is_sliding=False,
    slide_axis="X",
    open_note="",
    add_handles=True,
):
    if len(bounds) == 2:
        (x0, y0, z0), (x1, y1, z1) = bounds
        bounds = (x0, x1, y0, y1, z0, z1)
    if glass_bounds is not None and len(glass_bounds) == 2:
        (gx0, gy0, gz0), (gx1, gy1, gz1) = glass_bounds
        glass_bounds = (gx0, gx1, gy0, gy1, gz0, gz1)
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
    _link_also_to_collection(obj, thematic_col)
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def _link_also_to_collection(obj, col_name):
    col = _ensure_collection(col_name)
    if obj.name not in col.objects:
        col.objects.link(obj)


def _make_rising_barrier(
    door_specs,
    door_objs,
    parent,
    col_name,
    thematic_col,
    name,
    pivot_world,
    arm_dir="+X",
    arm_length=5.5,
):
    """Create an interactive rising security barrier (Gate_Barriere_*) with pivot at motor hinge."""
    b = ac.ArchMeshBuilder(name, collection_name=col_name, origin=pivot_world)
    if arm_dir == "+X":
        x0, x1 = -0.35, arm_length
        y0, y1 = -0.08, 0.08
    elif arm_dir == "-X":
        x0, x1 = -arm_length, 0.35
        y0, y1 = -0.08, 0.08
    elif arm_dir == "+Y":
        x0, x1 = -0.08, 0.08
        y0, y1 = -0.35, arm_length
    else:
        x0, x1 = -0.08, 0.08
        y0, y1 = -arm_length, 0.35

    # Main barrier boom arm + high-visibility warning stripes
    b.add_box_bounds(x0, x1, y0, y1, -0.07, 0.07, "Mat_Arch_MurExt_Blanc", collider=True)
    # Red/yellow warning bands along the boom
    span = abs(x1 - x0) if "X" in arm_dir else abs(y1 - y0)
    num_bands = max(3, int(span // 1.1))
    for i in range(num_bands):
        if i % 2 == 1:
            continue
        t0 = 0.25 + i * 0.95
        t1 = min(span - 0.15, t0 + 0.55)
        if t1 <= t0:
            continue
        if arm_dir == "+X":
            b.add_box_bounds(t0, t1, y0 - 0.01, y1 + 0.01, -0.075, 0.075, "Mat_Arch_Croix_Rouge")
        elif arm_dir == "-X":
            b.add_box_bounds(-t1, -t0, y0 - 0.01, y1 + 0.01, -0.075, 0.075, "Mat_Arch_Croix_Rouge")
        elif arm_dir == "+Y":
            b.add_box_bounds(x0 - 0.01, x1 + 0.01, t0, t1, -0.075, 0.075, "Mat_Arch_Croix_Rouge")
        else:
            b.add_box_bounds(x0 - 0.01, x1 + 0.01, -t1, -t0, -0.075, 0.075, "Mat_Arch_Croix_Rouge")

    # Counterweight & LED warning light on top of boom
    b.add_box_bounds(-0.32, 0.28, -0.14, 0.14, -0.14, 0.14, "Mat_Arch_Metal_Anthracite")
    b.add_box_bounds(-0.10, 0.10, -0.09, 0.09, 0.08, 0.18, "Mat_Arch_Lumiere_Gyro_Rouge")

    obj = b.build_object(parent=parent)
    _link_also_to_collection(obj, thematic_col)
    meta = {
        "name": name,
        "type": "barrier_hinge",
        "pivot_blender": [round(pivot_world[0], 4), round(pivot_world[1], 4), round(pivot_world[2], 4)],
        "open_note": "Barriere levante motorisee (rotation 90 deg autour de l'axe horizontal du pivot)",
        "colliders": b.colliders,
    }
    door_specs.append(meta)
    door_objs.append(obj)
    return obj


def _add_tropical_shade_tree(b, cx, cy, z0, height=9.5, canopy_r=4.2):
    """Low-poly tropical forest/shade tree (mango/iroko style) for Yopougon forest edge."""
    trunk_h = height * 0.44
    b.add_cylinder(cx, cy, z0, z0 + trunk_h, 0.36, 7, "Mat_Arch_Bois_Exterieur")
    b.add_cone(cx, cy, z0 + trunk_h - 0.4, z0 + height * 0.78, canopy_r, canopy_r * 0.45, 8, "Mat_Arch_Gazon")
    b.add_cone(cx, cy, z0 + height * 0.64, z0 + height, canopy_r * 0.72, 0.35, 8, "Mat_Arch_Pelouse_Stade")
    b.add_collider_center(cx, cy, z0 + trunk_h * 0.5, 0.75, 0.75, trunk_h)


def _add_watchtower(b, cx, cy, z0=0.0, stair_dir="+Y"):
    """Build a 1:1 military perimeter watchtower (Mirador, H=11.8m, platform at Z=7.6m) with climbable stairs."""
    plat_z = z0 + 7.60
    roof_z = plat_z + 2.80
    leg_d = 1.95
    # 4 reinforced concrete corner legs
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            lx = cx + sx * leg_d
            ly = cy + sy * leg_d
            b.add_box_center(lx, ly, z0 + 3.80, 0.42, 0.42, 7.60, "Mat_Arch_Beton_Brut", collider=True)

    # Cross bracing beams at Z=2.6m and Z=5.2m
    for bz in (z0 + 2.60, z0 + 5.20):
        b.add_box_bounds(cx - leg_d, cx + leg_d, cy - leg_d - 0.15, cy - leg_d + 0.15, bz - 0.15, bz + 0.15, "Mat_Arch_Metal_Anthracite")
        b.add_box_bounds(cx - leg_d, cx + leg_d, cy + leg_d - 0.15, cy + leg_d + 0.15, bz - 0.15, bz + 0.15, "Mat_Arch_Metal_Anthracite")
        b.add_box_bounds(cx - leg_d - 0.15, cx - leg_d + 0.15, cy - leg_d, cy + leg_d, bz - 0.15, bz + 0.15, "Mat_Arch_Metal_Anthracite")
        b.add_box_bounds(cx + leg_d - 0.15, cx + leg_d + 0.15, cy - leg_d, cy + leg_d, bz - 0.15, bz + 0.15, "Mat_Arch_Metal_Anthracite")

    # Observation deck slab (5.2m x 5.2m)
    b.add_box_bounds(cx - 2.60, cx + 2.60, cy - 2.60, cy + 2.60, plat_z - 0.25, plat_z, "Mat_Arch_Beton_Brut", collider=True)

    # Armored khaki parapet walls (height 1.15m) with entry gap on stair side
    parapet_h = 1.15
    # North (-X), South (+X), West (-Y), East (+Y)
    b.add_box_bounds(cx - 2.60, cx + 2.60, cy - 2.60, cy - 2.38, plat_z, plat_z + parapet_h, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(cx - 2.60, cx - 2.38, cy - 2.38, cy + 2.38, plat_z, plat_z + parapet_h, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(cx + 2.38, cx + 2.60, cy - 2.38, cy + 2.38, plat_z, plat_z + parapet_h, "Mat_Arch_Militaire_Kaki", collider=True)
    # East side (+Y) leaves a 1.3m stair entry opening on cx + 0.6..1.9
    b.add_box_bounds(cx - 2.38, cx + 0.45, cy + 2.38, cy + 2.60, plat_z, plat_z + parapet_h, "Mat_Arch_Militaire_Kaki", collider=True)

    # Corner roof posts + tropical overhang roof + searchlights
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.add_box_center(cx + sx * 2.35, cy + sy * 2.35, plat_z + 1.40, 0.20, 0.20, 2.80, "Mat_Arch_Metal_Anthracite")
    b.add_box_bounds(cx - 2.95, cx + 2.95, cy - 2.95, cy + 2.95, roof_z, roof_z + 0.22, "Mat_Arch_Toiture_Tole_Verte", collider=True)
    b.add_cone(cx, cy, roof_z + 0.22, roof_z + 1.35, 3.10, 0.20, 4, "Mat_Arch_Toiture_Tole_Verte")

    # Searchlights on watchtower corners
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.add_box_center(cx + sx * 2.70, cy + sy * 2.70, roof_z - 0.15, 0.35, 0.35, 0.28, "Mat_Arch_Lumiere_Nuit")

    # Two-flight climbable stair with mid-landing at Z=3.80m
    mid_z = z0 + 3.80
    # Flight 1: ground (cy - 2.2) -> mid-landing (cy + 3.8) along X = cx + 3.30
    b.add_stair_flight(
        axis="Y",
        lateral_center=cx + 3.30,
        width=1.20,
        start_pos=cy - 2.20,
        end_pos=cy + 3.80,
        z_start=z0,
        z_end=mid_z,
        num_steps=16,
        tread_mat="Mat_Arch_Beton_Brut",
        riser_mat="Mat_Arch_Metal_Anthracite",
    )
    # Mid-landing platform
    b.add_box_bounds(cx + 1.30, cx + 3.95, cy + 3.80, cy + 5.20, mid_z - 0.20, mid_z, "Mat_Arch_Beton_Brut", collider=True)
    # Flight 2: mid-landing (cy + 3.8) -> top deck (cy + 2.60) along X = cx + 1.45
    b.add_stair_flight(
        axis="Y",
        lateral_center=cx + 1.45,
        width=1.20,
        start_pos=cy + 3.80,
        end_pos=cy - 2.20,
        z_start=mid_z,
        z_end=plat_z,
        num_steps=16,
        tread_mat="Mat_Arch_Beton_Brut",
        riser_mat="Mat_Arch_Metal_Anthracite",
    )
    # Upper walkway connecting flight 2 top (cy - 2.20) to platform (cx + 1.45)
    b.add_box_bounds(cx + 0.85, cx + 2.60, cy - 2.60, cy + 2.60, plat_z - 0.25, plat_z, "Mat_Arch_Beton_Brut", collider=True)


def _add_military_truck_6x6(b, cx, cy, z0, axis="Y", facing=1.0, camo_mat="Mat_Arch_Militaire_Kaki"):
    """Low-poly 1:1 tactical 6x6 military transport truck (7.6m x 2.5m x 3.2m)."""
    # Build aligned along Y (length 7.6m, width 2.5m)
    if axis == "Y":
        # Chassis
        b.add_box_center(cx, cy, z0 + 0.85, 2.40, 7.50, 0.55, "Mat_Arch_Metal_Anthracite", collider=True)
        # Cab (at front: cy + facing * 2.45)
        cab_y = cy + facing * 2.45
        b.add_box_center(cx, cab_y, z0 + 1.95, 2.42, 2.20, 1.75, camo_mat, collider=True)
        b.add_box_center(cx, cab_y + facing * 1.12, z0 + 2.10, 2.10, 0.08, 0.72, "Mat_Arch_Vitre_Ext")
        # Headlights
        b.add_box_center(cx - 0.88, cab_y + facing * 1.12, z0 + 1.25, 0.32, 0.08, 0.22, "Mat_Arch_Lumiere_Nuit")
        b.add_box_center(cx + 0.88, cab_y + facing * 1.12, z0 + 1.25, 0.32, 0.08, 0.22, "Mat_Arch_Lumiere_Nuit")
        # Covered troop/cargo bed
        bed_y = cy - facing * 1.10
        b.add_box_center(cx, bed_y, z0 + 2.05, 2.48, 4.70, 1.95, "Mat_Arch_Militaire_CamoVert", collider=True)
        # 6 wheels
        for wy in (-2.55, -1.15, 2.35):
            for sx in (-1.15, 1.15):
                b.add_box_center(cx + sx, cy + facing * wy, z0 + 0.52, 0.36, 1.02, 1.02, "Mat_Arch_MurExt_Anthracite")
    else:
        b.add_box_center(cx, cy, z0 + 0.85, 7.50, 2.40, 0.55, "Mat_Arch_Metal_Anthracite", collider=True)
        cab_x = cx + facing * 2.45
        b.add_box_center(cab_x, cy, z0 + 1.95, 2.20, 2.42, 1.75, camo_mat, collider=True)
        b.add_box_center(cab_x + facing * 1.12, cy, z0 + 2.10, 0.08, 2.10, 0.72, "Mat_Arch_Vitre_Ext")
        bed_x = cx - facing * 1.10
        b.add_box_center(bed_x, cy, z0 + 2.05, 4.70, 2.48, 1.95, "Mat_Arch_Militaire_CamoVert", collider=True)
        for wx in (-2.55, -1.15, 2.35):
            for sy in (-1.15, 1.15):
                b.add_box_center(cx + facing * wx, cy + sy, z0 + 0.52, 1.02, 0.36, 1.02, "Mat_Arch_MurExt_Anthracite")


def _add_armored_apc(b, cx, cy, z0, facing=1.0):
    """Low-poly 1:1 4x4/6x6 armored personnel carrier (VAB / Blindé, 6.1m x 2.5m x 2.6m)."""
    b.add_box_center(cx, cy, z0 + 1.35, 2.48, 5.90, 1.55, "Mat_Arch_Militaire_Kaki", collider=True)
    # Sloped armored nose & turret
    b.add_box_center(cx, cy + facing * 2.65, z0 + 1.25, 2.30, 0.90, 1.05, "Mat_Arch_Militaire_CamoVert", collider=True)
    b.add_cylinder(cx, cy + facing * 0.45, z0 + 2.12, z0 + 2.65, 0.85, 10, "Mat_Arch_Militaire_CamoVert")
    # Cannon barrel
    b.add_box_center(cx, cy + facing * 2.15, z0 + 2.40, 0.16, 2.60, 0.16, "Mat_Arch_Metal_Anthracite")
    # 4 heavy wheels
    for wy in (-1.85, 1.85):
        for sx in (-1.18, 1.18):
            b.add_box_center(cx + sx, cy + facing * wy, z0 + 0.56, 0.42, 1.10, 1.10, "Mat_Arch_MurExt_Anthracite")


def _add_military_ambulance(b, cx, cy, z0):
    """1:1 military ambulance parked under the infirmary emergency canopy."""
    b.add_box_center(cx, cy, z0 + 1.35, 2.25, 5.60, 1.90, "Mat_Arch_MurExt_Blanc", collider=True)
    b.add_box_center(cx, cy, z0 + 1.40, 2.28, 5.62, 0.28, "Mat_Arch_Croix_Rouge")
    b.add_box_center(cx, cy - 2.10, z0 + 2.38, 0.90, 0.35, 0.22, "Mat_Arch_Lumiere_Gyro_Rouge")
    for wy in (-1.80, 1.80):
        for sx in (-1.05, 1.05):
            b.add_box_center(cx + sx, cy + wy, z0 + 0.44, 0.32, 0.86, 0.86, "Mat_Arch_MurExt_Anthracite")


# ==============================================================================
# 1. PERIMETRE & SECURITE (220m x 240m + Postes de garde + 6 Miradors)
# ==============================================================================
def build_zone_01_perimetre(ext_root, door_specs, door_objs):
    b = ac.ArchMeshBuilder("BM_01_Perimetre_Enceinte", collection_name=COL_ROOT_EXT)
    half_w = LOT_W * 0.5  # 110.0
    half_d = LOT_D * 0.5  # 120.0
    wall_h = 3.80
    wall_t = 0.45

    def add_perimeter_segment(axis, fixed_pos, s0, s1):
        length = s1 - s0
        if length <= 0.2:
            return
        if axis == "X":
            # Lower concrete wall
            b.add_box_bounds(s0, s1, fixed_pos - wall_t * 0.5, fixed_pos + wall_t * 0.5, 0.0, wall_h, "Mat_Arch_Beton_Brut", collider=True)
            # Khaki military band along middle
            b.add_box_bounds(s0, s1, fixed_pos - wall_t * 0.54, fixed_pos + wall_t * 0.54, 1.20, 1.75, "Mat_Arch_Militaire_Kaki")
            # Top security grille & concertina barbed wire rail (Z = 3.80..4.55m)
            b.add_box_bounds(s0, s1, fixed_pos - 0.08, fixed_pos + 0.08, wall_h, wall_h + 0.55, "Mat_Arch_Metal_Anthracite")
            b.add_box_bounds(s0, s1, fixed_pos - 0.28, fixed_pos + 0.28, wall_h + 0.55, wall_h + 0.75, "Mat_Arch_Metal_Inox")
            # Stiffener pillars every 10m
            n_pil = max(1, int(length // 10.0))
            for i in range(n_pil + 1):
                px = s0 + (length * i / n_pil)
                b.add_box_center(px, fixed_pos, wall_h * 0.5 + 0.20, 0.70, 0.75, wall_h + 0.40, "Mat_Arch_Militaire_Sable")
        else:
            b.add_box_bounds(fixed_pos - wall_t * 0.5, fixed_pos + wall_t * 0.5, s0, s1, 0.0, wall_h, "Mat_Arch_Beton_Brut", collider=True)
            b.add_box_bounds(fixed_pos - wall_t * 0.54, fixed_pos + wall_t * 0.54, s0, s1, 1.20, 1.75, "Mat_Arch_Militaire_Kaki")
            b.add_box_bounds(fixed_pos - 0.08, fixed_pos + 0.08, s0, s1, wall_h, wall_h + 0.55, "Mat_Arch_Metal_Anthracite")
            b.add_box_bounds(fixed_pos - 0.28, fixed_pos + 0.28, s0, s1, wall_h + 0.55, wall_h + 0.75, "Mat_Arch_Metal_Inox")
            n_pil = max(1, int(length // 10.0))
            for i in range(n_pil + 1):
                py = s0 + (length * i / n_pil)
                b.add_box_center(fixed_pos, py, wall_h * 0.5 + 0.20, 0.75, 0.70, wall_h + 0.40, "Mat_Arch_Militaire_Sable")

    # West Front Wall (Y = -120.0):
    # - Gate 2 (Logistics North-West): X in [-71.0, -59.0] (12m wide)
    # - Gate 1 (Main Checkpoint West): X in [-11.0, +11.0] (22m wide)
    add_perimeter_segment("X", -half_d, -half_w, -71.0)
    add_perimeter_segment("X", -half_d, -59.0, -11.0)
    add_perimeter_segment("X", -half_d, 11.0, half_w)

    # East Forest-Edge Wall (Y = +120.0):
    # - Gate 3 (Forest Tactical Gate East): X in [-6.0, +6.0] (12m wide)
    add_perimeter_segment("X", half_d, -half_w, -6.0)
    add_perimeter_segment("X", half_d, 6.0, half_w)

    # North Wall (X = -110.0) and South Wall (X = +110.0) - continuous
    add_perimeter_segment("Y", -half_w, -half_d, half_d)
    add_perimeter_segment("Y", half_w, -half_d, half_d)

    # 6 Watchtowers (Miradors) at 4 corners + North/South midpoints
    watchtower_coords = [
        (-103.5, -113.5),
        (103.5, -113.5),
        (-103.5, 113.5),
        (103.5, 113.5),
        (-103.5, 0.0),
        (103.5, 0.0),
    ]
    for wx, wy in watchtower_coords:
        _add_watchtower(b, wx, wy, 0.0)

    # --- POSTE DE GARDE PRINCIPAL OUEST (X in [-16, +16], Y in [-126, -108]) ---
    # Monumental overhead security arch spanning the main entrance (X in [-13, 13], Y in [-121.5, -117.5])
    b.add_box_bounds(-13.5, -10.5, -121.8, -117.2, 0.0, 6.80, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(10.5, 13.5, -121.8, -117.2, 0.0, 6.80, "Mat_Arch_Militaire_Kaki", collider=True)
    # Central checkpoint island & armored guard booth (X in [-2.2, 2.2], Y in [-123.5, -112.5])
    b.add_box_bounds(-2.30, 2.30, -124.0, -111.5, 0.0, 0.22, "Mat_Arch_Paves_Cour", collider=True)
    b.add_box_bounds(-1.95, 1.95, -121.5, -114.5, 0.22, 3.60, "Mat_Arch_Militaire_Sable", collider=True)
    # Armored observation windows on central guard booth
    b.add_box_bounds(-2.02, 2.02, -120.8, -115.2, 1.35, 2.55, "Mat_Arch_Vitre_Ext")
    # Overhead canopy above main checkpoint
    b.add_box_bounds(-14.2, 14.2, -122.8, -115.8, 5.80, 6.65, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(-14.4, 14.4, -123.0, -115.6, 6.65, 6.95, "Mat_Arch_Militaire_Kaki")
    # Ivorian colors emblem strip on main gate fascia
    b.add_box_bounds(-6.0, -2.0, -123.1, -122.9, 6.00, 6.50, "Mat_Arch_Drapeau_Orange")
    b.add_box_bounds(-2.0, 2.0, -123.1, -122.9, 6.00, 6.50, "Mat_Arch_MurExt_Blanc")
    b.add_box_bounds(2.0, 6.0, -123.1, -122.9, 6.00, 6.50, "Mat_Arch_Drapeau_Vert")
    # Night floodlights & red/blue security beacons on main gate
    for gx in (-9.5, -4.5, 4.5, 9.5):
        b.add_box_center(gx, -122.2, 5.72, 1.20, 0.45, 0.14, "Mat_Arch_Lumiere_Nuit")
    b.add_box_center(-11.8, -121.9, 7.10, 0.45, 0.45, 0.35, "Mat_Arch_Lumiere_Gyro_Rouge")
    b.add_box_center(11.8, -121.9, 7.10, 0.45, 0.45, 0.35, "Mat_Arch_Lumiere_Gyro_Bleu")

    # Side Security Check-In Building (Guichet de controle pietons, X in [13.5, 22.5], Y in [-118.5, -108.5])
    b.add_box_bounds(13.5, 22.5, -118.5, -108.5, 0.0, 0.22, "Mat_Arch_Beton_Brut", collider=True)
    b.add_wall_with_openings(
        "X", 13.7, 0.30, -118.5, -108.5, 0.22, 3.60, "Mat_Arch_Militaire_Sable",
        openings=[
            {"s0": -115.0, "s1": -112.0, "z0": 0.22, "z1": 2.45, "kind": "open"},
            {"s0": -117.5, "s1": -115.6, "z0": 1.15, "z1": 2.40, "kind": "window"},
        ],
    )
    b.add_wall_with_openings("X", 22.3, 0.30, -118.5, -108.5, 0.22, 3.60, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings("Y", -118.3, 0.30, 13.5, 22.5, 0.22, 3.60, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings(
        "Y", -108.7, 0.30, 13.5, 22.5, 0.22, 3.60, "Mat_Arch_Militaire_Sable",
        openings=[{"s0": 16.5, "s1": 19.5, "z0": 0.22, "z1": 2.45, "kind": "open"}],
    )
    b.add_box_bounds(13.1, 22.9, -118.9, -108.1, 3.60, 3.95, "Mat_Arch_Toiture_Tole_Verte", collider=True)
    # Interior security portal & counter inside pedestrian checkpoint
    amc.add_security_portal_scanner(b, 16.2, -113.5, 0.22)
    amc.add_reception_or_caisse_counter(b, 19.8, -113.5, 0.22, length=4.2, axis="Y", facing="-X", num_windows=2, armored_glass=True)

    # Anti-ram concrete jersey barriers & speed bumps in front of main gate
    for bx in (-6.5, 6.5):
        b.add_box_center(bx, -114.0, 0.04, 7.20, 0.65, 0.08, "Mat_Arch_Marquage_Jaune")
        b.add_box_center(bx, -126.0, 0.04, 7.20, 0.65, 0.08, "Mat_Arch_Marquage_Jaune")

    # Motor pedestals for the 2 main rising barriers
    b.add_box_center(-2.65, -115.2, 0.55, 0.48, 0.48, 1.10, "Mat_Arch_Marquage_Jaune", collider=True)
    b.add_box_center(2.65, -115.2, 0.55, 0.48, 0.48, 1.10, "Mat_Arch_Marquage_Jaune", collider=True)

    # --- POSTE LOGISTIQUE NORD-OUEST (X = -65.0, Y = -120.0) ---
    b.add_box_bounds(-73.2, -71.0, -121.4, -118.6, 0.0, 5.20, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(-59.0, -56.8, -121.4, -118.6, 0.0, 5.20, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(-58.5, -54.5, -118.5, -114.0, 0.0, 3.30, "Mat_Arch_Militaire_Sable", collider=True)
    b.add_box_bounds(-58.8, -54.2, -118.8, -113.7, 3.30, 3.55, "Mat_Arch_Toiture_Tole_Verte", collider=True)
    b.add_box_center(-59.4, -116.5, 0.55, 0.48, 0.48, 1.10, "Mat_Arch_Marquage_Jaune", collider=True)

    # --- POSTE TACTIQUE FORET EST (X = 0.0, Y = +120.0) ---
    b.add_box_bounds(-8.2, -6.0, 118.6, 121.4, 0.0, 5.00, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(6.0, 8.2, 118.6, 121.4, 0.0, 5.00, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(6.5, 10.5, 114.0, 118.5, 0.0, 3.30, "Mat_Arch_Militaire_Sable", collider=True)
    b.add_box_bounds(6.2, 10.8, 113.7, 118.8, 3.30, 3.55, "Mat_Arch_Toiture_Tole_Verte", collider=True)

    obj = b.build_object(parent=ext_root)
    _link_also_to_collection(obj, COL_01_PERIMETRE)

    # Interactive Rising Barriers & Sliding Armored Gates (kept non-static in Unity via Gate_* prefix)
    _make_rising_barrier(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Barriere_Entree_Principale_N", (-2.65, -115.2, 0.95), arm_dir="-X", arm_length=7.20,
    )
    _make_rising_barrier(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Barriere_Entree_Principale_S", (2.65, -115.2, 0.95), arm_dir="+X", arm_length=7.20,
    )
    _make_door(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Portail_Blindee_Ouest_N",
        pivot=(-6.4, -120.0, 0.0),
        bounds=((-4.1, -0.14, 0.05), (4.1, 0.14, 3.45)),
        mat_panel="Mat_Arch_Militaire_Kaki",
        is_sliding=True,
        slide_axis="X",
        open_note="Portail blinde coulissant Nord de l'entree principale Ouest",
        add_handles=False,
    )
    _make_door(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Portail_Blindee_Ouest_S",
        pivot=(6.4, -120.0, 0.0),
        bounds=((-4.1, -0.14, 0.05), (4.1, 0.14, 3.45)),
        mat_panel="Mat_Arch_Militaire_Kaki",
        is_sliding=True,
        slide_axis="X",
        open_note="Portail blinde coulissant Sud de l'entree principale Ouest",
        add_handles=False,
    )
    _make_rising_barrier(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Barriere_Logistique_NO", (-59.4, -116.5, 0.95), arm_dir="-X", arm_length=10.50,
    )
    _make_door(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Portail_Logistique_NO",
        pivot=(-65.0, -120.0, 0.0),
        bounds=((-5.9, -0.15, 0.05), (5.9, 0.15, 3.50)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        is_sliding=True,
        slide_axis="X",
        open_note="Portail coulissant logistique Nord-Ouest (poids lourds et blindes)",
        add_handles=False,
    )
    _make_door(
        door_specs, door_objs, ext_root, COL_ROOT_EXT, COL_01_PERIMETRE,
        "Gate_Portail_Foret_Est",
        pivot=(0.0, 120.0, 0.0),
        bounds=((-5.9, -0.15, 0.05), (5.9, 0.15, 3.50)),
        mat_panel="Mat_Arch_Militaire_Kaki",
        is_sliding=True,
        slide_axis="X",
        open_note="Portail tactique arriere Est (lisiere forestiere du Banco)",
        add_handles=False,
    )

    return b.colliders


# ==============================================================================
# 2. COMMANDEMENT & ADMINISTRATION (Quartier General + Radar + Antenne + Interieur)
# ==============================================================================
def build_zone_02_qg(ext_root, int_root, ext_door_specs, ext_door_objs, int_door_specs, int_door_objs):
    b = ac.ArchMeshBuilder("BM_02_Quartier_General_Ext", collection_name=COL_ROOT_EXT)
    # HQ Footprint: X in [-24.0, +24.0] (48m wide), Y in [2.0, 34.0] (32m deep), 3 levels (Z = 0..13.2m)
    x0, x1 = -24.0, 24.0
    y0, y1 = 2.0, 34.0
    z_rdc = 0.30
    z_r1 = 4.70
    z_r2 = 9.10
    z_roof = 13.20

    # Raised podium plinth & ceremonial steps facing Parade Ground (-Y)
    b.add_box_bounds(x0 - 1.2, x1 + 1.2, y0 - 4.0, y1 + 1.2, 0.0, z_rdc, "Mat_Arch_Parement_Pierre", collider=True)
    # Ceremonial colonnade portico at Y = y0 - 3.2 (8 tall pillars)
    for px in (-18.0, -13.0, -8.0, -3.5, 3.5, 8.0, 13.0, 18.0):
        amc.add_colonnade_pillar(b, px, y0 - 3.0, z_rdc, z_r2 + 0.2, radius=0.48, base_mat="Mat_Arch_Parement_Pierre", shaft_mat="Mat_Arch_Militaire_Sable", cap_mat="Mat_Arch_Or_Ceremonie")
    b.add_box_bounds(-20.0, 20.0, y0 - 3.8, y0 + 0.2, z_r2, z_r2 + 0.85, "Mat_Arch_Militaire_Kaki", collider=True)

    # Outer walls of HQ (RDC + R+1 + R+2)
    front_openings = [
        {"s0": -2.4, "s1": 2.4, "z0": z_rdc, "z1": z_rdc + 3.10, "kind": "open"},
    ]
    for wx in (-19.5, -14.5, -9.5, 9.5, 14.5, 19.5):
        for fz in (z_rdc, z_r1, z_r2):
            front_openings.append({"s0": wx - 1.4, "s1": wx + 1.4, "z0": fz + 1.0, "z1": fz + 2.85, "kind": "window"})
    for wx in (-4.5, 4.5):
        for fz in (z_r1, z_r2):
            front_openings.append({"s0": wx - 1.6, "s1": wx + 1.6, "z0": fz + 0.9, "z1": fz + 2.95, "kind": "window"})

    b.add_wall_with_openings("Y", y0, 0.38, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=front_openings)

    back_openings = [
        {"s0": -1.8, "s1": 1.8, "z0": z_rdc, "z1": z_rdc + 2.65, "kind": "open"},
    ]
    for wx in (-18.0, -12.0, -6.0, 6.0, 12.0, 18.0):
        for fz in (z_rdc, z_r1, z_r2):
            back_openings.append({"s0": wx - 1.3, "s1": wx + 1.3, "z0": fz + 1.05, "z1": fz + 2.80, "kind": "window"})
    b.add_wall_with_openings("Y", y1, 0.38, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=back_openings)

    side_openings = []
    for wy in (7.5, 13.5, 19.5, 25.5, 30.0):
        for fz in (z_rdc, z_r1, z_r2):
            side_openings.append({"s0": wy - 1.3, "s1": wy + 1.3, "z0": fz + 1.05, "z1": fz + 2.80, "kind": "window"})
    b.add_wall_with_openings("X", x0, 0.38, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=side_openings)
    b.add_wall_with_openings("X", x1, 0.38, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=side_openings)

    # Khaki & gold architectural cornice bands + roof terrace slab
    for fz in (z_r1, z_r2, z_roof):
        b.add_box_bounds(x0 - 0.30, x1 + 0.30, y0 - 0.30, y1 + 0.30, fz - 0.22, fz + 0.12, "Mat_Arch_Militaire_Kaki")
    b.add_box_bounds(x0 - 0.45, x1 + 0.45, y0 - 0.45, y1 + 0.45, z_roof, z_roof + 0.40, "Mat_Arch_Toiture_Terrasse", collider=True)
    # Roof parapet
    b.add_box_bounds(x0 - 0.45, x1 + 0.45, y0 - 0.45, y0 - 0.10, z_roof + 0.40, z_roof + 1.35, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(x0 - 0.45, x1 + 0.45, y1 + 0.10, y1 + 0.45, z_roof + 0.40, z_roof + 1.35, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(x0 - 0.45, x0 - 0.10, y0 - 0.10, y1 + 0.10, z_roof + 0.40, z_roof + 1.35, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(x1 + 0.10, x1 + 0.45, y0 - 0.10, y1 + 0.10, z_roof + 0.40, z_roof + 1.35, "Mat_Arch_Militaire_Kaki", collider=True)

    # --- TELECOM LATTICE TOWER, RADAR DOME & RELAY DISHES ON HQ ROOF ---
    z_top = z_roof + 0.40
    # Communications lattice mast at (-12.0, 22.0), height 16.5m (reaches Z = 30.1m)
    tx, ty = -12.0, 22.0
    b.add_box_bounds(tx - 2.2, tx + 2.2, ty - 2.2, ty + 2.2, z_top, z_top + 0.65, "Mat_Arch_Beton_Brut", collider=True)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            amc.add_beam_between(
                b,
                (tx + sx * 1.65, ty + sy * 1.65, z_top + 0.65),
                (tx + sx * 0.42, ty + sy * 0.42, z_top + 13.5),
                0.12,
                "Mat_Arch_Metal_Anthracite",
            )
    for ring_z in (z_top + 3.5, z_top + 6.8, z_top + 10.0, z_top + 13.2):
        t = (ring_z - (z_top + 0.65)) / 12.85
        hw = 1.65 * (1.0 - t) + 0.42 * t
        b.add_box_bounds(tx - hw, tx + hw, ty - hw, ty + hw, ring_z - 0.08, ring_z + 0.08, "Mat_Arch_Metal_Inox")
    # Whip antenna spire + red aviation warning beacon at Z = 30.5m
    b.add_cylinder(tx, ty, z_top + 13.2, z_top + 17.2, 0.10, 8, "Mat_Arch_Metal_Inox")
    b.add_box_center(tx, ty, z_top + 17.35, 0.42, 0.42, 0.42, "Mat_Arch_Lumiere_Gyro_Rouge")
    # Microwave relay drums on tower
    for drum_z in (z_top + 8.5, z_top + 11.5):
        b.add_cylinder(tx - 0.95, ty, drum_z - 0.45, drum_z + 0.45, 0.65, 10, "Mat_Arch_MurExt_Blanc")
        b.add_cylinder(tx + 0.95, ty, drum_z - 0.45, drum_z + 0.45, 0.65, 10, "Mat_Arch_MurExt_Blanc")

    # Rotating Primary Surveillance Radar Tower & Radome at (+12.0, 22.0)
    rx, ry = 12.0, 22.0
    b.add_cylinder(rx, ry, z_top, z_top + 2.80, 2.60, 12, "Mat_Arch_Militaire_Kaki")
    # Primary sweeping radar array bar
    b.add_box_center(rx, ry, z_top + 3.45, 5.80, 0.75, 1.15, "Mat_Arch_Metal_Inox", collider=True)
    b.add_box_center(rx, ry, z_top + 4.25, 0.55, 0.55, 0.45, "Mat_Arch_Lumiere_Gyro_Bleu")
    # Secondary geodesic radome sphere/dome at (0.0, 24.0)
    amc.add_dome(b, 0.0, 24.0, z_top + 1.20, radius=2.85, height=2.90, segments=16, rings=6, mat_name="Mat_Arch_MurExt_Blanc", pointed=False)
    b.add_cylinder(0.0, 24.0, z_top, z_top + 1.25, 2.50, 12, "Mat_Arch_Beton_Brut")

    obj_ext = b.build_object(parent=ext_root)
    _link_also_to_collection(obj_ext, COL_02_QG)

    # Interactive HQ Main Entrance Double Doors
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_02_QG,
        "Door_QG_Entree_L",
        pivot=(-2.35, y0, z_rdc),
        bounds=((0.0, -0.06, 0.0), (2.30, 0.06, 2.95)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        glass_bounds=((0.22, -0.02, 0.30), (2.08, 0.02, 2.70)),
        open_note="Porte vitree blindee gauche du Quartier General",
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_02_QG,
        "Door_QG_Entree_R",
        pivot=(2.35, y0, z_rdc),
        bounds=((-2.30, -0.06, 0.0), (0.0, 0.06, 2.95)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        glass_bounds=((-2.08, -0.02, 0.30), (-0.22, 0.02, 2.70)),
        open_note="Porte vitree blindee droite du Quartier General",
    )

    # --- HQ INTERIOR (RDC + R+1 + R+2 with Briefing War Room, Command Center & Stairs) ---
    bi = ac.ArchMeshBuilder("BM_02_Quartier_General_Int", collection_name=COL_ROOT_INT)
    # RDC floor slab
    bi.add_box_bounds(x0 + 0.35, x1 - 0.35, y0 + 0.35, y1 - 0.35, 0.0, z_rdc, "Mat_Arch_Sol_Marbre", collider=True)
    # R+1 floor slab with stairwell cutout on East wing (X in [14.0, 21.0], Y in [12.0, 22.0])
    amc.add_slab_with_cutouts(
        bi, x0 + 0.35, x1 - 0.35, y0 + 0.35, y1 - 0.35, z_r1 - 0.25, z_r1,
        cutouts=[(14.0, 21.0, 12.0, 22.0)],
        mat_name="Mat_Arch_Sol_Carrelage",
    )
    # R+2 floor slab with stairwell cutout
    amc.add_slab_with_cutouts(
        bi, x0 + 0.35, x1 - 0.35, y0 + 0.35, y1 - 0.35, z_r2 - 0.25, z_r2,
        cutouts=[(14.0, 21.0, 12.0, 22.0)],
        mat_name="Mat_Arch_Sol_Parquet",
    )

    # Climbable interior stairs RDC (0.30) -> R+1 (4.70) and R+1 (4.70) -> R+2 (9.10)
    bi.add_stair_flight(
        axis="Y", lateral_center=16.2, width=2.10,
        start_pos=12.2, end_pos=21.8, z_start=z_rdc, z_end=z_r1,
        num_steps=18, tread_mat="Mat_Arch_Sol_Marbre", riser_mat="Mat_Arch_Beton_Brut",
    )
    bi.add_stair_flight(
        axis="Y", lateral_center=19.2, width=2.10,
        start_pos=21.8, end_pos=12.2, z_start=z_r1, z_end=z_r2,
        num_steps=18, tread_mat="Mat_Arch_Sol_Carrelage", riser_mat="Mat_Arch_Beton_Brut",
    )

    # RDC Partition Walls: Central Lobby (X in [-6, 6]), West Wing War Room / Briefing (X in [-23.5, -6]), East Wing Staff Offices (X in [6, 23.5])
    bi.add_wall_with_openings(
        "X", -6.0, 0.24, y0 + 0.4, y1 - 0.4, z_rdc, z_r1 - 0.25, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 15.0, "s1": 18.2, "z0": z_rdc, "z1": z_rdc + 2.40, "kind": "open"}],
    )
    bi.add_wall_with_openings(
        "X", 6.0, 0.24, y0 + 0.4, y1 - 0.4, z_rdc, z_r1 - 0.25, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 15.0, "s1": 18.2, "z0": z_rdc, "z1": z_rdc + 2.40, "kind": "open"}],
    )

    # Lobby Reception & Security Portal
    amc.add_security_portal_scanner(bi, 0.0, y0 + 4.5, z_rdc)
    amc.add_reception_or_caisse_counter(bi, 0.0, y0 + 10.5, z_rdc, length=6.0, axis="X", facing="-Y", num_windows=3, armored_glass=True)

    # --- SALLE DE BRIEFING & ETAT-MAJOR (WAR ROOM, West Wing RDC, X in [-23, -6.5], Y in [4, 32]) ---
    # Giant tactical command table + 14 chairs + podium + wall-sized tactical map & CCTV display
    amc.add_conference_table(bi, -15.0, 18.0, z_rdc, length=11.0, width=3.2, axis="Y", num_chairs_side=7, table_mat="Mat_Arch_Plan_Travail", chair_mat="Mat_Arch_Militaire_Kaki", hollow_center=True)
    amc.add_podium_lectern(bi, -15.0, 29.2, z_rdc, facing="-Y")
    amc.add_cctv_wall(bi, -15.0, 33.3, z_rdc + 1.10, axis="X", width=9.5, rows=2, cols=5, facing_sign=-1.0)
    amc.add_ceiling_lights_grid(bi, -22.5, -7.0, 5.0, 31.0, z_r1 - 0.28, nx=3, ny=4)

    # East Wing RDC Operations & Communications desks
    for ox in (9.5, 13.5):
        for oy in (7.5, 13.5, 25.5, 29.5):
            amc.add_office_desk(bi, ox, oy, z_rdc, facing="-Y", executive=False, with_monitor=True)

    # R+1 General's Executive Command Suite (Center-West) & Intelligence Offices
    amc.add_office_desk(bi, -12.0, 12.0, z_r1, facing="+Y", width=2.40, depth=1.10, executive=True, with_monitor=True)
    amc.add_sofa_lounge(bi, -12.0, 22.0, z_r1, fabric_mat="Mat_Arch_Militaire_Kaki")
    amc.add_safe_vault(bi, -21.5, 8.0, z_r1, facing="+X")
    amc.add_conference_table(bi, 0.0, 18.0, z_r1, length=7.5, width=2.4, axis="X", num_chairs_side=5)
    amc.add_ceiling_lights_grid(bi, -22.0, 12.0, 5.0, 31.0, z_r2 - 0.28, nx=5, ny=3)

    # R+2 Cyber/Radar Control & Strategic Command Floor
    amc.add_cctv_wall(bi, 0.0, 33.3, z_r2 + 1.10, axis="X", width=12.0, rows=2, cols=6, facing_sign=-1.0)
    for rx_desk in (-12.0, -6.0, 0.0, 6.0):
        amc.add_office_desk(bi, rx_desk, 24.0, z_r2, facing="+Y", executive=True, with_monitor=True)

    obj_int = bi.build_object(parent=int_root)
    _link_also_to_collection(obj_int, COL_02_QG)
    _link_also_to_collection(obj_int, COL_09_INTERIEURS)

    # Interactive Interior Doors for War Room
    _make_door(
        int_door_specs, int_door_objs, int_root, COL_ROOT_INT, COL_09_INTERIEURS,
        "Door_QG_Salle_Briefing",
        pivot=(-6.0, 15.0, z_rdc),
        bounds=((-0.05, 0.0, 0.0), (0.05, 1.55, 2.35)),
        mat_panel="Mat_Arch_Bois_Interieur",
        open_note="Porte d'acces a la Salle de Briefing et d'Etat-Major (QG RDC)",
    )

    return b.colliders, bi.colliders


# ==============================================================================
# 3. CASERNES & DORTOIRS DES TROUPES (Quadrant Sud-Ouest: 3 Batiments + Cour)
# ==============================================================================
def build_zone_03_casernes(ext_root, int_root, ext_door_specs, ext_door_objs):
    b = ac.ArchMeshBuilder("BM_03_Casernes_Troupes_Ext", collection_name=COL_ROOT_EXT)
    bi = ac.ArchMeshBuilder("BM_03_Casernes_Troupes_Int", collection_name=COL_ROOT_INT)

    # Central Barracks Courtyard (X in [34, 96], Y in [-94, -20])
    b.add_box_bounds(34.0, 96.0, -94.0, -20.0, 0.0, 0.14, "Mat_Arch_Paves_Cour", collider=True)
    # Green lawn islands & shade palms in the barracks courtyard
    for cy_lawn in (-72.0, -57.0, -42.0):
        b.add_box_bounds(46.0, 74.0, cy_lawn - 4.5, cy_lawn + 4.5, 0.14, 0.22, "Mat_Arch_Gazon")
        amc.add_palm_tree(b, 51.0, cy_lawn, 0.22, height=7.2)
        amc.add_palm_tree(b, 69.0, cy_lawn, 0.22, height=7.5)
        amc.add_streetlamp(b, 60.0, cy_lawn, 0.22, height=5.0)

    def build_barrack_block(tag, x0, x1, y0, y1, orient="X"):
        z_rdc = 0.22
        z_r1 = 3.82
        z_roof = 7.42
        b.add_box_bounds(x0 - 0.6, x1 + 0.6, y0 - 0.6, y1 + 0.6, 0.0, z_rdc, "Mat_Arch_Beton_Brut", collider=True)
        bi.add_box_bounds(x0 + 0.3, x1 - 0.3, y0 + 0.3, y1 - 0.3, 0.0, z_rdc, "Mat_Arch_Sol_Carrelage", collider=True)
        bi.add_box_bounds(x0 - 1.8, x1 + 1.8, y0 - 1.8, y1 + 1.8, z_r1 - 0.22, z_r1, "Mat_Arch_Sol_Carrelage", collider=True)

        if orient == "X":
            mid_x = 0.5 * (x0 + x1)
            openings_front = [
                {"s0": mid_x - 1.5, "s1": mid_x + 1.5, "z0": z_rdc, "z1": z_rdc + 2.40, "kind": "open"},
                {"s0": mid_x - 1.5, "s1": mid_x + 1.5, "z0": z_r1, "z1": z_r1 + 2.40, "kind": "open"},
            ]
            for wx in range(int(x0 + 4), int(x1 - 3), 5):
                if abs(wx - mid_x) < 3.2:
                    continue
                openings_front.append({"s0": wx - 1.1, "s1": wx + 1.1, "z0": z_rdc + 1.0, "z1": z_rdc + 2.3, "kind": "window"})
                openings_front.append({"s0": wx - 1.1, "s1": wx + 1.1, "z0": z_r1 + 1.0, "z1": z_r1 + 2.3, "kind": "window"})
            b.add_wall_with_openings("Y", y0, 0.32, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=openings_front)
            b.add_wall_with_openings("Y", y1, 0.32, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=openings_front)
            b.add_wall_with_openings("X", x0, 0.32, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable")
            b.add_wall_with_openings("X", x1, 0.32, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable")

            # Exterior climbable stair to R+1 walkway
            b.add_stair_flight(
                axis="X", lateral_center=y0 - 1.10, width=1.40,
                start_pos=x0 + 1.0, end_pos=x0 + 7.5, z_start=z_rdc, z_end=z_r1,
                num_steps=15, tread_mat="Mat_Arch_Beton_Brut", riser_mat="Mat_Arch_Metal_Anthracite",
            )
            # Interactive Barrack Door
            _make_door(
                ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_03_CASERNES,
                f"Door_Caserne_{tag}_Entree",
                pivot=(mid_x - 1.45, y0, z_rdc),
                bounds=((0.0, -0.06, 0.0), (1.45, 0.06, 2.35)),
                mat_panel="Mat_Arch_Militaire_Kaki",
                open_note=f"Porte d'entree RDC de la Caserne {tag}",
            )

            # Interior bunks & military lockers (RDC & R+1)
            for fz in (z_rdc, z_r1):
                for bx in range(int(x0 + 3), int(x1 - 3), 4):
                    if abs(bx - mid_x) < 2.8:
                        continue
                    # Double military bunk beds along North & South walls
                    bi.add_box_center(bx, y0 + 1.5, fz + 0.45, 1.10, 2.10, 0.45, "Mat_Arch_Militaire_Kaki", collider=True)
                    bi.add_box_center(bx, y0 + 1.5, fz + 1.35, 1.10, 2.10, 0.35, "Mat_Arch_Militaire_CamoVert")
                    bi.add_box_center(bx, y1 - 1.5, fz + 0.45, 1.10, 2.10, 0.45, "Mat_Arch_Militaire_Kaki", collider=True)
                    bi.add_box_center(bx, y1 - 1.5, fz + 1.35, 1.10, 2.10, 0.35, "Mat_Arch_Militaire_CamoVert")
                amc.add_locker_bank(bi, mid_x - 6.0, 0.5 * (y0 + y1), fz, count=6, axis="X", mat="Mat_Arch_Militaire_Kaki")
                amc.add_locker_bank(bi, mid_x + 6.0, 0.5 * (y0 + y1), fz, count=6, axis="X", mat="Mat_Arch_Militaire_Kaki")
        else:
            mid_y = 0.5 * (y0 + y1)
            openings_front = [
                {"s0": mid_y - 1.5, "s1": mid_y + 1.5, "z0": z_rdc, "z1": z_rdc + 2.40, "kind": "open"},
                {"s0": mid_y - 1.5, "s1": mid_y + 1.5, "z0": z_r1, "z1": z_r1 + 2.40, "kind": "open"},
            ]
            for wy in range(int(y0 + 4), int(y1 - 3), 5):
                if abs(wy - mid_y) < 3.2:
                    continue
                openings_front.append({"s0": wy - 1.1, "s1": wy + 1.1, "z0": z_rdc + 1.0, "z1": z_rdc + 2.3, "kind": "window"})
                openings_front.append({"s0": wy - 1.1, "s1": wy + 1.1, "z0": z_r1 + 1.0, "z1": z_r1 + 2.3, "kind": "window"})
            b.add_wall_with_openings("X", x0, 0.32, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=openings_front)
            b.add_wall_with_openings("X", x1, 0.32, y0, y1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable", openings=openings_front)
            b.add_wall_with_openings("Y", y0, 0.32, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable")
            b.add_wall_with_openings("Y", y1, 0.32, x0, x1, z_rdc, z_roof, "Mat_Arch_Militaire_Sable")

            b.add_stair_flight(
                axis="Y", lateral_center=x0 - 1.10, width=1.40,
                start_pos=y0 + 1.0, end_pos=y0 + 7.5, z_start=z_rdc, z_end=z_r1,
                num_steps=15, tread_mat="Mat_Arch_Beton_Brut", riser_mat="Mat_Arch_Metal_Anthracite",
            )
            _make_door(
                ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_03_CASERNES,
                f"Door_Caserne_{tag}_Entree",
                pivot=(x0, mid_y - 1.45, z_rdc),
                bounds=((-0.06, 0.0, 0.0), (0.06, 1.45, 2.35)),
                mat_panel="Mat_Arch_Militaire_Kaki",
                open_note=f"Porte d'entree RDC de la Caserne {tag}",
            )
            for fz in (z_rdc, z_r1):
                amc.add_locker_bank(bi, 0.5 * (x0 + x1), mid_y - 6.0, fz, count=6, axis="Y", mat="Mat_Arch_Militaire_Kaki")
                amc.add_locker_bank(bi, 0.5 * (x0 + x1), mid_y + 6.0, fz, count=6, axis="Y", mat="Mat_Arch_Militaire_Kaki")

        # Khaki band + green corrugated metal pitched roof overhang
        b.add_box_bounds(x0 - 1.85, x1 + 1.85, y0 - 1.85, y1 + 1.85, z_r1 - 0.25, z_r1 + 0.08, "Mat_Arch_Militaire_Kaki")
        b.add_box_bounds(x0 - 2.0, x1 + 2.0, y0 - 2.0, y1 + 2.0, z_roof, z_roof + 0.35, "Mat_Arch_Toiture_Tole_Verte", collider=True)

    # 3 Modular 2-story Barrack Buildings around the South-West troop courtyard
    build_barrack_block("Alpha", 36.0, 72.0, -92.0, -78.0, orient="X")
    build_barrack_block("Bravo", 36.0, 72.0, -36.0, -22.0, orient="X")
    build_barrack_block("Charlie", 80.0, 94.0, -76.0, -38.0, orient="Y")

    obj_ext = b.build_object(parent=ext_root)
    _link_also_to_collection(obj_ext, COL_03_CASERNES)
    obj_int = bi.build_object(parent=int_root)
    _link_also_to_collection(obj_int, COL_03_CASERNES)
    _link_also_to_collection(obj_int, COL_09_INTERIEURS)
    return b.colliders, bi.colliders


# ==============================================================================
# 4. REFECTOIRE / MESS & INFIRMERIE MILITAIRE (Zone Sud-Centre & Sud-Est)
# ==============================================================================
def build_zone_04_mess_infirmerie(ext_root, int_root, ext_door_specs, ext_door_objs):
    b = ac.ArchMeshBuilder("BM_04_Mess_Infirmerie_Ext", collection_name=COL_ROOT_EXT)
    bi = ac.ArchMeshBuilder("BM_04_Mess_Infirmerie_Int", collection_name=COL_ROOT_INT)

    # --- 4A. REFECTOIRE / MESS MILITAIRE (X in [38, 74], Y in [4, 28], 36m x 24m) ---
    mx0, mx1, my0, my1 = 38.0, 74.0, 4.0, 28.0
    mz0, mz1 = 0.22, 5.20
    b.add_box_bounds(mx0 - 1.5, mx1 + 1.5, my0 - 3.5, my1 + 1.5, 0.0, mz0, "Mat_Arch_Sol_Terrasse", collider=True)
    bi.add_box_bounds(mx0 + 0.3, mx1 - 0.3, my0 + 0.3, my1 - 0.3, 0.0, mz0, "Mat_Arch_Sol_Carrelage", collider=True)

    mess_front_openings = [
        {"s0": 53.5, "s1": 58.5, "z0": mz0, "z1": mz0 + 2.65, "kind": "open"},
        {"s0": 42.0, "s1": 50.0, "z0": mz0 + 0.95, "z1": mz0 + 2.80, "kind": "window"},
        {"s0": 62.0, "s1": 70.0, "z0": mz0 + 0.95, "z1": mz0 + 2.80, "kind": "window"},
    ]
    b.add_wall_with_openings("Y", my0, 0.32, mx0, mx1, mz0, mz1, "Mat_Arch_Militaire_Sable", openings=mess_front_openings)
    b.add_wall_with_openings("Y", my1, 0.32, mx0, mx1, mz0, mz1, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings("X", mx0, 0.32, my0, my1, mz0, mz1, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings("X", mx1, 0.32, my0, my1, mz0, mz1, "Mat_Arch_Militaire_Sable")
    b.add_box_bounds(mx0 - 1.8, mx1 + 1.8, my0 - 3.8, my1 + 1.8, mz1, mz1 + 0.38, "Mat_Arch_Toiture_Tole_Verte", collider=True)

    # Mess Hall interior tables & stainless serving counter
    for tx in (45.0, 56.0, 67.0):
        for ty in (10.0, 17.0):
            amc.add_conference_table(bi, tx, ty, mz0, length=6.0, width=1.6, axis="X", num_chairs_side=4, table_mat="Mat_Arch_Bois_Interieur", chair_mat="Mat_Arch_Metal_Anthracite")
    bi.add_box_bounds(42.0, 70.0, 22.5, 24.0, mz0, mz0 + 1.05, "Mat_Arch_Metal_Inox", collider=True)

    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_04_MESS_INF,
        "Door_Mess_Entree_L",
        pivot=(53.5, my0, mz0),
        bounds=((0.0, -0.06, 0.0), (2.45, 0.06, 2.60)),
        mat_panel="Mat_Arch_Bois_Exterieur",
        open_note="Porte gauche du Refectoire / Mess Militaire",
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_04_MESS_INF,
        "Door_Mess_Entree_R",
        pivot=(58.5, my0, mz0),
        bounds=((-2.45, -0.06, 0.0), (0.0, 0.06, 2.60)),
        mat_panel="Mat_Arch_Bois_Exterieur",
        open_note="Porte droite du Refectoire / Mess Militaire",
    )

    # --- 4B. INFIRMERIE / CENTRE MEDICAL MILITAIRE (X in [38, 72], Y in [42, 68], 34m x 26m) ---
    ix0, ix1, iy0, iy1 = 38.0, 72.0, 42.0, 68.0
    iz0, iz1 = 0.22, 5.40
    b.add_box_bounds(ix0 - 1.2, ix1 + 1.2, iy0 - 7.5, iy1 + 1.2, 0.0, iz0, "Mat_Arch_Beton_Brut", collider=True)
    bi.add_box_bounds(ix0 + 0.3, ix1 - 0.3, iy0 + 0.3, iy1 - 0.3, 0.0, iz0, "Mat_Arch_Sol_Carrelage", collider=True)

    inf_openings = [
        {"s0": 52.5, "s1": 57.5, "z0": iz0, "z1": iz0 + 2.70, "kind": "open"},
        {"s0": 42.0, "s1": 49.0, "z0": iz0 + 1.0, "z1": iz0 + 2.7, "kind": "window"},
        {"s0": 61.0, "s1": 68.0, "z0": iz0 + 1.0, "z1": iz0 + 2.7, "kind": "window"},
    ]
    b.add_wall_with_openings("Y", iy0, 0.32, ix0, ix1, iz0, iz1, "Mat_Arch_MurExt_Blanc", openings=inf_openings)
    b.add_wall_with_openings("Y", iy1, 0.32, ix0, ix1, iz0, iz1, "Mat_Arch_MurExt_Blanc")
    b.add_wall_with_openings("X", ix0, 0.32, iy0, iy1, iz0, iz1, "Mat_Arch_MurExt_Blanc")
    b.add_wall_with_openings("X", ix1, 0.32, iy0, iy1, iz0, iz1, "Mat_Arch_MurExt_Blanc")
    # Medical teal & red-cross bands + roof
    b.add_box_bounds(ix0 - 0.35, ix1 + 0.35, iy0 - 0.35, iy1 + 0.35, iz1 - 0.55, iz1, "Mat_Arch_Hopital_Teal")
    b.add_box_bounds(ix0 - 0.60, ix1 + 0.60, iy0 - 0.60, iy1 + 0.60, iz1, iz1 + 0.38, "Mat_Arch_Toiture_Terrasse", collider=True)

    # Ambulance drive-through drop-off canopy (Y in [iy0 - 7.0, iy0]) + illuminated Red Cross emblem
    b.add_box_bounds(48.0, 62.0, iy0 - 7.0, iy0, 4.10, 4.55, "Mat_Arch_MurExt_Blanc", collider=True)
    b.add_box_center(49.0, iy0 - 6.4, 2.05, 0.45, 0.45, 4.10, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_center(61.0, iy0 - 6.4, 2.05, 0.45, 0.45, 4.10, "Mat_Arch_Beton_Brut", collider=True)
    amc.add_cross_emblem(b, 55.0, iy0 - 7.15, 4.65, width=2.2, height=2.2, thickness=0.25, axis="X", mat_name="Mat_Arch_Lumiere_Croix")
    # Parked Military Ambulance under the emergency bay
    _add_military_ambulance(b, 55.0, iy0 - 3.6, iz0)

    # Infirmary Interior: Triage, 6 Hospital Beds, Operating Theater, Pharmacy
    for bx in (42.5, 47.0, 51.5):
        amc.add_hospital_bed(bi, bx, 50.0, iz0, axis="Y", with_monitor=True)
        amc.add_hospital_bed(bi, bx, 62.0, iz0, axis="Y", with_monitor=False)
    amc.add_operating_table(bi, 64.0, 58.0, iz0)
    amc.add_pharmacy_shelving(bi, 64.0, 47.0, iz0, length=4.5, axis="X")

    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_04_MESS_INF,
        "Door_Infirmerie_Urgences_L",
        pivot=(52.5, iy0, iz0),
        bounds=((0.0, -0.06, 0.0), (2.45, 0.06, 2.65)),
        mat_panel="Mat_Arch_Hopital_Teal",
        glass_bounds=((0.25, -0.02, 0.90), (2.20, 0.02, 2.40)),
        open_note="Porte gauche des urgences de l'Infirmerie Militaire",
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_04_MESS_INF,
        "Door_Infirmerie_Urgences_R",
        pivot=(57.5, iy0, iz0),
        bounds=((-2.45, -0.06, 0.0), (0.0, 0.06, 2.65)),
        mat_panel="Mat_Arch_Hopital_Teal",
        glass_bounds=((-2.20, -0.02, 0.90), (-0.25, 0.02, 2.40)),
        open_note="Porte droite des urgences de l'Infirmerie Militaire",
    )

    obj_ext = b.build_object(parent=ext_root)
    _link_also_to_collection(obj_ext, COL_04_MESS_INF)
    obj_int = bi.build_object(parent=int_root)
    _link_also_to_collection(obj_int, COL_04_MESS_INF)
    _link_also_to_collection(obj_int, COL_09_INTERIEURS)
    return b.colliders, bi.colliders


# ==============================================================================
# 5. DEPOT DE MUNITIONS / ARMURERIE, ENTREPOTS LOGISTIQUES & CARBURANT (Quadrant NE)
# ==============================================================================
def build_zone_05_munitions_logistique(ext_root, int_root, ext_door_specs, ext_door_objs):
    b = ac.ArchMeshBuilder("BM_05_Munitions_Logistique_Ext", collection_name=COL_ROOT_EXT)
    bi = ac.ArchMeshBuilder("BM_05_Munitions_Logistique_Int", collection_name=COL_ROOT_INT)

    # --- 5A. DEPOT DE MUNITIONS & ARMURERIE FORTIFIEE (Bunker NE: X in [-98, -64], Y in [68, 102]) ---
    ax0, ax1, ay0, ay1 = -96.0, -66.0, 72.0, 100.0
    az0, az1 = 0.25, 4.80
    # Outer blast berm & inner security fence around the munitions depot
    b.add_box_bounds(ax0 - 4.0, ax1 + 4.0, ay0 - 4.0, ay1 + 4.0, 0.0, az0, "Mat_Arch_Beton_Brut", collider=True)
    # Thick 0.65m bunker walls
    b.add_wall_with_openings(
        "Y", ay0, 0.65, ax0, ax1, az0, az1, "Mat_Arch_Beton_Brut",
        openings=[{"s0": -83.5, "s1": -78.5, "z0": az0, "z1": az0 + 2.85, "kind": "open"}],
    )
    b.add_wall_with_openings("Y", ay1, 0.65, ax0, ax1, az0, az1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("X", ax0, 0.65, ay0, ay1, az0, az1, "Mat_Arch_Beton_Brut")
    b.add_wall_with_openings("X", ax1, 0.65, ay0, ay1, az0, az1, "Mat_Arch_Beton_Brut")
    # Blast roof slab + hazard yellow/red warning stripes
    b.add_box_bounds(ax0 - 0.8, ax1 + 0.8, ay0 - 0.8, ay1 + 0.8, az1, az1 + 0.65, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(ax0 - 0.7, ax1 + 0.7, ay0 - 0.45, ay0 + 0.35, 1.40, 1.85, "Mat_Arch_Marquage_Jaune")
    b.add_box_center(-81.0, ay0 - 0.45, az1 + 0.85, 0.50, 0.50, 0.45, "Mat_Arch_Lumiere_Gyro_Rouge")

    # Armory Interior: Weapon Racks, Heavy Vaults, Ammo Crates
    bi.add_box_bounds(ax0 + 0.6, ax1 - 0.6, ay0 + 0.6, ay1 - 0.6, 0.0, az0, "Mat_Arch_Beton_Brut", collider=True)
    for rx in (-91.0, -86.0, -76.0, -71.0):
        amc.add_armory_rack(bi, rx, 80.0, az0, length=4.5, axis="Y")
        amc.add_armory_rack(bi, rx, 91.0, az0, length=4.5, axis="Y")
    amc.add_safe_vault(bi, -88.0, 96.5, az0, facing="-Y", size=(2.2, 1.5, 2.3))
    amc.add_safe_vault(bi, -74.0, 96.5, az0, facing="-Y", size=(2.2, 1.5, 2.3))
    # Stacked ammunition crates in center aisle
    for cy_crate in (81.0, 86.0, 91.0):
        bi.add_box_center(-81.0, cy_crate, az0 + 0.65, 2.40, 2.40, 1.30, "Mat_Arch_Militaire_Kaki", collider=True)
        bi.add_box_center(-81.0, cy_crate, az0 + 1.55, 1.80, 1.80, 0.50, "Mat_Arch_Marquage_Jaune")

    # Interactive Heavy Blast Doors
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_05_LOGISTIQUE,
        "Door_Armurerie_Blindee_L",
        pivot=(-83.5, ay0, az0),
        bounds=((0.0, -0.14, 0.0), (2.45, 0.14, 2.80)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        open_note="Porte blindee gauche du Depot de Munitions / Armurerie",
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_05_LOGISTIQUE,
        "Door_Armurerie_Blindee_R",
        pivot=(-78.5, ay0, az0),
        bounds=((-2.45, -0.14, 0.0), (0.0, 0.14, 2.80)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        open_note="Porte blindee droite du Depot de Munitions / Armurerie",
    )

    # --- 5B. ENTREPOTS LOGISTIQUES & COUR A CONTENEURS (X in [-98, -36], Y in [6, 52]) ---
    b.add_box_bounds(-98.0, -34.0, 4.0, 56.0, 0.0, 0.16, "Mat_Arch_Beton_Brut", collider=True)
    # Two large covered logistics warehouses (24m x 18m each)
    for idx, wx0 in enumerate((-95.0, -65.0)):
        wx1 = wx0 + 24.0
        wy0, wy1 = 8.0, 28.0
        wz1 = 6.80
        b.add_wall_with_openings(
            "Y", wy0, 0.30, wx0, wx1, 0.16, wz1, "Mat_Arch_Militaire_Sable",
            openings=[
                {"s0": wx0 + 4.0, "s1": wx0 + 10.0, "z0": 0.16, "z1": 4.60, "kind": "open"},
                {"s0": wx0 + 14.0, "s1": wx0 + 20.0, "z0": 0.16, "z1": 4.60, "kind": "open"},
            ],
        )
        b.add_wall_with_openings("Y", wy1, 0.30, wx0, wx1, 0.16, wz1, "Mat_Arch_Militaire_Sable")
        b.add_wall_with_openings("X", wx0, 0.30, wy0, wy1, 0.16, wz1, "Mat_Arch_Militaire_Sable")
        b.add_wall_with_openings("X", wx1, 0.30, wy0, wy1, 0.16, wz1, "Mat_Arch_Militaire_Sable")
        b.add_box_bounds(wx0 - 0.8, wx1 + 0.8, wy0 - 1.5, wy1 + 0.8, wz1, wz1 + 0.40, "Mat_Arch_Toiture_Tole_Verte", collider=True)
        # Interior crate stacks
        for cx_c in (wx0 + 6.0, wx0 + 12.0, wx0 + 18.0):
            for cy_c in (15.0, 22.0):
                b.add_box_center(cx_c, cy_c, 0.16 + 1.10, 2.8, 2.8, 2.2, "Mat_Arch_Bois_Exterieur", collider=True)

    # ISO 40ft Shipping Containers Yard (Y in [34, 52])
    container_mats = ["Mat_Arch_Militaire_Kaki", "Mat_Arch_Militaire_CamoVert", "Mat_Arch_Militaire_Sable", "Mat_Arch_Cuve_Bleue"]
    for c_idx, cx_cont in enumerate((-90.0, -82.0, -74.0, -66.0, -58.0, -50.0, -42.0)):
        mat1 = container_mats[c_idx % len(container_mats)]
        mat2 = container_mats[(c_idx + 1) % len(container_mats)]
        # Bottom container (12.2m x 2.44m x 2.60m)
        b.add_box_center(cx_cont, 43.0, 0.16 + 1.30, 2.44, 12.20, 2.60, mat1, collider=True)
        if c_idx % 2 == 0:
            # Stacked second container
            b.add_box_center(cx_cont, 43.0, 0.16 + 3.90, 2.44, 12.20, 2.60, mat2, collider=True)

    # --- 5C. ZONE DE RAVITAILLEMENT CARBURANT SECURISEE (X in [-58, -34], Y in [68, 102]) ---
    # Far from barracks (which are at X > +36, Y < -20)
    fx0, fx1, fy0, fy1 = -58.0, -34.0, 68.0, 102.0
    # Concrete containment bund wall (muret de retention)
    b.add_box_bounds(fx0, fx1, fy0, fy1, 0.0, 0.22, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(fx0, fx1, fy1 - 0.4, fy1, 0.22, 1.15, "Mat_Arch_Marquage_Jaune", collider=True)
    b.add_box_bounds(fx0, fx0 + 0.4, fy0 + 14.0, fy1, 0.22, 1.15, "Mat_Arch_Marquage_Jaune", collider=True)
    b.add_box_bounds(fx1 - 0.4, fx1, fy0 + 14.0, fy1, 0.22, 1.15, "Mat_Arch_Marquage_Jaune", collider=True)

    # 3 Large Cylindrical Fuel Storage Silos (Z = 0.22..7.80m, radius 3.2m)
    for sx_tank in (-52.5, -46.0, -39.5):
        b.add_cylinder(sx_tank, 92.5, 0.22, 7.80, 2.85, 14, "Mat_Arch_Metal_Inox")
        b.add_box_center(sx_tank, 92.5, 4.20, 5.80, 5.80, 0.55, "Mat_Arch_Marquage_Jaune")
        b.add_collider_center(sx_tank, 92.5, 4.0, 5.7, 5.7, 7.6)

    # Covered 2-lane Heavy Vehicle Fuel Dispensing Canopy (Y in [71, 82])
    b.add_box_bounds(-55.0, -37.0, 71.0, 82.0, 5.20, 5.75, "Mat_Arch_Militaire_Kaki", collider=True)
    b.add_box_bounds(-55.2, -36.8, 70.8, 82.2, 5.75, 5.95, "Mat_Arch_Marquage_Jaune")
    for px in (-53.5, -38.5):
        for py in (72.5, 80.5):
            b.add_box_center(px, py, 2.70, 0.50, 0.50, 5.00, "Mat_Arch_Metal_Anthracite", collider=True)
    # Central fuel pump island
    b.add_box_bounds(-51.0, -41.0, 75.5, 77.5, 0.22, 0.42, "Mat_Arch_Beton_Brut", collider=True)
    for pump_x in (-49.0, -46.0, -43.0):
        b.add_box_center(pump_x, 76.5, 1.35, 0.85, 0.65, 1.85, "Mat_Arch_Croix_Rouge", collider=True)

    obj_ext = b.build_object(parent=ext_root)
    _link_also_to_collection(obj_ext, COL_05_LOGISTIQUE)
    obj_int = bi.build_object(parent=int_root)
    _link_also_to_collection(obj_int, COL_05_LOGISTIQUE)
    _link_also_to_collection(obj_int, COL_09_INTERIEURS)
    return b.colliders, bi.colliders


# ==============================================================================
# 6. PLACE D'ARMES, PARKING BLINDES & ATELIERS MECANIQUES (Centre & Nord-Ouest)
# ==============================================================================
def build_zone_06_vehicules_ateliers(ext_root, ext_door_specs, ext_door_objs):
    b = ac.ArchMeshBuilder("BM_06_PlaceArmes_Garages_Ateliers", collection_name=COL_ROOT_EXT)

    # --- 6A. PLACE D'ARMES / ESPLANADE CENTRALE (X in [-26, 26], Y in [-76, -4], 52m x 72m) ---
    b.add_box_bounds(-26.0, 26.0, -76.0, -4.0, 0.0, 0.14, "Mat_Arch_Paves_Cour", collider=True)
    # Tactical formation ground markings (yellow/white company squares)
    for fx in (-16.0, 0.0, 16.0):
        for fy in (-60.0, -42.0, -24.0):
            b.add_box_bounds(fx - 5.0, fx + 5.0, fy - 5.0, fy + 5.0, 0.14, 0.16, "Mat_Arch_Asphalte_Base")
            b.add_box_bounds(fx - 5.2, fx + 5.2, fy - 5.2, fy - 4.9, 0.16, 0.17, "Mat_Arch_Marquage_Jaune")
            b.add_box_bounds(fx - 5.2, fx + 5.2, fy + 4.9, fy + 5.2, 0.16, 0.17, "Mat_Arch_Marquage_Jaune")

    # Central Ceremonial Flag Monument at (0.0, -14.0) facing Parade Ground & HQ
    b.add_box_bounds(-3.6, 3.6, -17.6, -10.4, 0.14, 0.65, "Mat_Arch_Parement_Pierre", collider=True)
    b.add_box_bounds(-2.2, 2.2, -16.2, -11.8, 0.65, 1.35, "Mat_Arch_Sol_Marbre", collider=True)
    amc.add_flagpole_ivorian(b, 0.0, -14.0, 1.35, height=16.0, flag_dir="+Y")
    amc.add_flagpole_ivorian(b, -2.6, -14.0, 0.65, height=12.0, flag_dir="+Y")
    amc.add_flagpole_ivorian(b, 2.6, -14.0, 0.65, height=12.0, flag_dir="+Y")

    # Covered Reviewing Stand / Tribune d'Honneur on West side of Parade Ground (X in [-10, 10], Y in [-76, -68])
    b.add_box_bounds(-10.0, 10.0, -75.5, -68.5, 0.14, 0.95, "Mat_Arch_Parement_Pierre", collider=True)
    b.add_box_bounds(-10.5, 10.5, -76.0, -68.0, 4.30, 4.75, "Mat_Arch_Militaire_Kaki", collider=True)
    for tx in (-9.0, -3.0, 3.0, 9.0):
        b.add_box_center(tx, -69.0, 2.60, 0.35, 0.35, 3.40, "Mat_Arch_Or_Ceremonie")
        b.add_box_center(tx, -75.0, 2.60, 0.35, 0.35, 3.40, "Mat_Arch_Metal_Anthracite")
    b.add_stair_flight(
        axis="Y", lateral_center=0.0, width=3.20,
        start_pos=-66.5, end_pos=-68.5, z_start=0.14, z_end=0.95,
        num_steps=5, tread_mat="Mat_Arch_Sol_Marbre", riser_mat="Mat_Arch_Parement_Pierre",
    )

    # --- 6B. ATELIERS MECANIQUES LOURDS & GARAGES BLINDES (Quadrant Nord-Ouest: X in [-98, -36], Y in [-98, -16]) ---
    b.add_box_bounds(-98.0, -34.0, -98.0, -14.0, 0.0, 0.16, "Mat_Arch_Asphalte_Base", collider=True)

    # Main 4-Bay Heavy Mechanical Workshop (X in [-94, -38], Y in [-95, -69], 56m x 26m, H = 7.8m)
    gx0, gx1, gy0, gy1 = -94.0, -38.0, -95.0, -69.0
    gz0, gz1 = 0.18, 7.80
    # 4 maintenance bays along +Y side (facing motor pool apron Y in [-69, -16]):
    # Bays at X in [-90..-80], [-77..-67], [-65..-55], [-52..-42]
    bay_openings = [
        {"s0": -90.0, "s1": -80.0, "z0": gz0, "z1": 5.40, "kind": "open"},
        {"s0": -77.0, "s1": -67.0, "z0": gz0, "z1": 5.40, "kind": "open"},
        {"s0": -65.0, "s1": -55.0, "z0": gz0, "z1": 5.40, "kind": "open"},
        {"s0": -52.0, "s1": -42.0, "z0": gz0, "z1": 5.40, "kind": "open"},
    ]
    b.add_wall_with_openings("Y", gy1, 0.38, gx0, gx1, gz0, gz1, "Mat_Arch_Militaire_Sable", openings=bay_openings)
    b.add_wall_with_openings("Y", gy0, 0.38, gx0, gx1, gz0, gz1, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings("X", gx0, 0.38, gy0, gy1, gz0, gz1, "Mat_Arch_Militaire_Sable")
    b.add_wall_with_openings("X", gx1, 0.38, gy0, gy1, gz0, gz1, "Mat_Arch_Militaire_Sable")
    b.add_box_bounds(gx0 - 0.8, gx1 + 0.8, gy0 - 0.8, gy1 + 2.5, gz1, gz1 + 0.45, "Mat_Arch_Toiture_Tole_Verte", collider=True)

    # Interior inspection pits markings, lift columns & tool benches inside open Bays 1 & 2
    for bay_cx in (-85.0, -72.0):
        # Recessed dark inspection pit surround with yellow safety curbing
        b.add_box_bounds(bay_cx - 1.1, bay_cx + 1.1, -88.0, -74.0, gz0, gz0 + 0.06, "Mat_Arch_MurExt_Anthracite")
        b.add_box_bounds(bay_cx - 1.35, bay_cx - 1.1, -88.0, -74.0, gz0, gz0 + 0.14, "Mat_Arch_Marquage_Jaune")
        b.add_box_bounds(bay_cx + 1.1, bay_cx + 1.35, -88.0, -74.0, gz0, gz0 + 0.14, "Mat_Arch_Marquage_Jaune")
        # Hydraulic lift columns
        for lx in (-2.4, 2.4):
            b.add_box_center(bay_cx + lx, -81.0, gz0 + 2.10, 0.45, 0.45, 4.20, "Mat_Arch_Marquage_Jaune", collider=True)
        # Workbenches against back wall
        b.add_box_bounds(bay_cx - 3.5, bay_cx + 3.5, gy0 + 0.5, gy0 + 1.6, gz0, gz0 + 0.95, "Mat_Arch_Croix_Rouge", collider=True)

    # One military 6x6 truck inside Bay 1 undergoing maintenance
    _add_military_truck_6x6(b, -85.0, -81.0, gz0, axis="Y", facing=1.0)

    # Interactive rolling shutter doors on Bays 3 & 4
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_06_VEHICULES,
        "Gate_Atelier_Baie_3",
        pivot=(-60.0, gy1, gz0),
        bounds=((-4.95, -0.10, 0.0), (4.95, 0.10, 5.35)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        is_sliding=True,
        slide_axis="X",
        open_note="Rideau metallique motorise de la Baie Mecanique 3",
        add_handles=False,
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_06_VEHICULES,
        "Gate_Atelier_Baie_4",
        pivot=(-47.0, gy1, gz0),
        bounds=((-4.95, -0.10, 0.0), (4.95, 0.10, 5.35)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        is_sliding=True,
        slide_axis="X",
        open_note="Rideau metallique motorise de la Baie Mecanique 4",
        add_handles=False,
    )

    # Covered Armored Vehicle & Troop Truck Carports (Auvents Blindes: X in [-94, -38], Y in [-44, -20])
    b.add_box_bounds(-94.0, -38.0, -44.0, -20.0, 5.20, 5.60, "Mat_Arch_Toiture_Tole_Verte", collider=True)
    for px in range(-92, -36, 11):
        b.add_box_center(px, -43.0, 2.68, 0.40, 0.40, 5.05, "Mat_Arch_Metal_Anthracite", collider=True)
        b.add_box_center(px, -21.0, 2.68, 0.40, 0.40, 5.05, "Mat_Arch_Metal_Anthracite", collider=True)
        # Yellow parking bay stripes
        b.add_box_bounds(px - 0.12, px + 0.12, -43.0, -21.0, 0.16, 0.18, "Mat_Arch_Marquage_Jaune")

    # Static Fleet parked under the carport + open bays left ready for dynamic Unity vehicle spawns
    _add_armored_apc(b, -86.5, -32.0, 0.16, facing=-1.0)
    _add_armored_apc(b, -75.5, -32.0, 0.16, facing=-1.0)
    _add_military_truck_6x6(b, -64.5, -32.0, 0.16, axis="Y", facing=-1.0)
    _add_military_truck_6x6(b, -53.5, -32.0, 0.16, axis="Y", facing=-1.0)

    obj = b.build_object(parent=ext_root)
    _link_also_to_collection(obj, COL_06_VEHICULES)
    return b.colliders


# ==============================================================================
# 7. ZONE AERIENNE / HELIPORT (Pads H1 & H2 + Manches a Air + 2 Grands Hangars)
# ==============================================================================
def build_zone_07_heliport(ext_root, ext_door_specs, ext_door_objs):
    b = ac.ArchMeshBuilder("BM_07_Zone_Aerienne_Heliport", collection_name=COL_ROOT_EXT)

    # Aviation Apron & Helipad Concrete Tarmac (X in [-24, 96], Y in [74, 112], 120m x 38m)
    b.add_box_bounds(-24.0, 96.0, 74.0, 112.0, 0.0, 0.20, "Mat_Arch_Beton_Brut", collider=True)

    # Helipad 1 (Main Transport Helicopter Pad at X = 12.0, Y = 93.0, radius 9.5m)
    # Helipad 2 (Tactical / Medevac Pad at X = 44.0, Y = 93.0, radius 8.5m)
    for h_idx, (hx, hy, hr) in enumerate(((12.0, 93.0, 9.0), (44.0, 93.0, 8.0))):
        b.add_cylinder(hx, hy, 0.20, 0.26, hr + 1.5, 24, "Mat_Arch_Asphalte_Base")
        amc.add_helipad_marking(b, hx, hy, 0.26, radius=hr)
        # Yellow safety perimeter octagon markers & perimeter night lights
        for k in range(12):
            ang = (2.0 * math.pi * k) / 12.0
            lx = hx + (hr + 1.2) * math.cos(ang)
            ly = hy + (hr + 1.2) * math.sin(ang)
            mat_l = "Mat_Arch_Lumiere_Gyro_Bleu" if k % 2 == 0 else "Mat_Arch_Lumiere_Nuit"
            b.add_box_center(lx, ly, 0.34, 0.35, 0.35, 0.18, mat_l)

    # 2 Windsocks (Manches a air bicolores orange/blanc) at (-4.0, 108.0) and (60.0, 108.0)
    for wx, wy in ((-4.0, 108.0), (60.0, 108.0)):
        b.add_cylinder(wx, wy, 0.20, 7.60, 0.09, 8, "Mat_Arch_Metal_Inox")
        b.add_box_center(wx, wy, 0.55, 0.65, 0.65, 0.70, "Mat_Arch_Marquage_Jaune", collider=True)
        # Segmented horizontal windsock cone pointing +Y (downwind)
        b.add_cone(wx, wy + 0.55, 7.20, 7.55, 0.42, 0.32, 8, "Mat_Arch_Drapeau_Orange")
        b.add_cone(wx, wy + 1.20, 7.12, 7.42, 0.32, 0.22, 8, "Mat_Arch_MurExt_Blanc")
        b.add_cone(wx, wy + 1.80, 7.05, 7.30, 0.22, 0.12, 8, "Mat_Arch_Drapeau_Orange")

    # 2 Large Modular Aircraft / Helicopter Hangars (X in [64, 94], Y in [74, 110] and X in [-24, -2], Y in [76, 106])
    # Hangar 1 (East Main Helicopter Hangar: X in [66, 94], Y in [76, 106], 28m x 30m, H = 10.2m, opening toward -X pads)
    hx0, hx1, hy0, hy1 = 66.0, 94.0, 76.0, 106.0
    hz0, hz1 = 0.22, 10.20
    # Open wide front on X = hx0 (facing Helipads at X = 44 and X = 12)
    b.add_wall_with_openings(
        "X", hx0, 0.42, hy0, hy1, hz0, hz1, "Mat_Arch_Militaire_Kaki",
        openings=[{"s0": hy0 + 3.0, "s1": hy1 - 3.0, "z0": hz0, "z1": 7.80, "kind": "open"}],
    )
    b.add_wall_with_openings("X", hx1, 0.42, hy0, hy1, hz0, hz1, "Mat_Arch_Militaire_Kaki")
    b.add_wall_with_openings("Y", hy0, 0.42, hx0, hx1, hz0, hz1, "Mat_Arch_Militaire_Kaki")
    b.add_wall_with_openings("Y", hy1, 0.42, hx0, hx1, hz0, hz1, "Mat_Arch_Militaire_Kaki")
    b.add_box_bounds(hx0 - 1.2, hx1 + 1.2, hy0 - 1.2, hy1 + 1.2, hz1, hz1 + 0.55, "Mat_Arch_Toiture_Tole_Verte", collider=True)

    # Interactive sliding hangar doors on Hangar 1
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_07_HELIPORT,
        "Gate_Hangar_Aero_1_L",
        pivot=(hx0, hy0 + 4.5, hz0),
        bounds=((-0.14, -1.5, 0.0), (0.14, 4.5, 7.75)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        is_sliding=True,
        slide_axis="Y",
        open_note="Vantail coulissant gauche du Grand Hangar Helicoptere 1",
        add_handles=False,
    )
    _make_door(
        ext_door_specs, ext_door_objs, ext_root, COL_ROOT_EXT, COL_07_HELIPORT,
        "Gate_Hangar_Aero_1_R",
        pivot=(hx0, hy1 - 4.5, hz0),
        bounds=((-0.14, -4.5, 0.0), (0.14, 1.5, 7.75)),
        mat_panel="Mat_Arch_Metal_Anthracite",
        is_sliding=True,
        slide_axis="Y",
        open_note="Vantail coulissant droit du Grand Hangar Helicoptere 1",
        add_handles=False,
    )

    # Hangar 2 (Open-sided Modular Maintenance Hangar at X in [-24, -4], Y in [76, 104], 20m x 28m, H = 9.2m)
    b.add_box_bounds(-24.5, -3.5, 75.5, 104.5, 8.80, 9.35, "Mat_Arch_Toiture_Tole_Verte", collider=True)
    for px in (-23.5, -14.0, -4.5):
        for py in (76.5, 85.5, 94.5, 103.5):
            b.add_box_center(px, py, 4.50, 0.48, 0.48, 8.60, "Mat_Arch_Metal_Anthracite", collider=True)
    # Rear wall on Y = 104.0
    b.add_box_bounds(-24.0, -4.0, 103.6, 104.2, 0.20, 8.80, "Mat_Arch_Militaire_Kaki", collider=True)

    obj = b.build_object(parent=ext_root)
    _link_also_to_collection(obj, COL_07_HELIPORT)
    return b.colliders


# ==============================================================================
# 8. VOIRIE INTERNE, ROUTE D'ACCES YOPOUGON & ENVIRONNEMENT TROPICAL
# ==============================================================================
def build_zone_08_voirie_environnement(ext_root):
    b = ac.ArchMeshBuilder("BM_08_Voirie_Environnement", collection_name=COL_ROOT_EXT)

    # Base ground slab inside the 220m x 240m compound (Z = -0.25..0.04m)
    b.add_box_bounds(-110.0, 110.0, -120.0, 120.0, -0.30, 0.04, "Mat_Arch_Gazon", collider=True)

    # --- EXTERNAL ACCESS ROADS CONNECTING TO "ROUTE DE PRISON, VOIE DE YOPOUGON-N'DOTRE" ---
    # 1. Main Western Military Access Boulevard (Y in [-178.0, -120.0], X in [-9.0, +9.0], width 18m)
    # In Unity world space (with center at (-660, 0, 745) and ToStreet=(-1,0,0)),
    # Y = -120..-178 maps to world X = -780..-838 at Z = 745, joining "Route de prison" (X = -835.5)!
    b.add_box_bounds(-9.0, 9.0, -178.0, -120.0, -0.20, 0.06, "Mat_Arch_Asphalte_Base", collider=True)
    # Concrete sidewalks & yellow center line along main access boulevard
    b.add_box_bounds(-11.2, -9.0, -176.0, -120.0, -0.15, 0.16, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(9.0, 11.2, -176.0, -120.0, -0.15, 0.16, "Mat_Arch_Beton_Brut", collider=True)
    for dash_y in range(-174, -122, 6):
        b.add_box_bounds(-0.20, 0.20, dash_y, dash_y + 3.2, 0.06, 0.08, "Mat_Arch_Marquage_Jaune")

    # 2. Secondary Logistics Spur at North-West Gate (X in [-71.0, -59.0], Y in [-148.0, -120.0])
    # Connects to "Route de prison" at world Z = 810, X = -806!
    b.add_box_bounds(-71.0, -59.0, -148.0, -120.0, -0.20, 0.06, "Mat_Arch_Asphalte_Base", collider=True)

    # --- INTERNAL ASPHALT ROAD NETWORK (12m to 16m wide heavy-duty roadways) ---
    # Main West-East Central Axis from Main Gate (Y = -120) to Parade Ground (Y = -76)
    b.add_box_bounds(-10.0, 10.0, -120.0, -76.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # Perimeter Ring Road (Rocade Interne):
    # - West Avenue: Y in [-110, -98], X in [-100, 100]
    b.add_box_bounds(-100.0, 100.0, -110.0, -98.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - Central Transverse Avenue (between Parade Ground and HQ/Mess/Logistics): Y in [-12, 0], X in [-100, 100]
    b.add_box_bounds(-100.0, 100.0, -12.0, 0.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - East Logistics/Heliport Transverse Avenue: Y in [58, 70], X in [-100, 100]
    b.add_box_bounds(-100.0, 100.0, 58.0, 70.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - North Longitudinal Avenue (Logistics & Motor Pool): X in [-34, -24], Y in [-108, 108]
    b.add_box_bounds(-34.0, -24.0, -108.0, 108.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - South Longitudinal Avenue (Barracks, Mess, Infirmary, Heliport): X in [26, 36], Y in [-108, 108]
    b.add_box_bounds(26.0, 36.0, -108.0, 108.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - North-West Gate Connector: X in [-71, -59], Y in [-120, -98]
    b.add_box_bounds(-71.0, -59.0, -120.0, -98.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")
    # - East Forest Gate Connector: X in [-6, 6], Y in [34, 120]
    b.add_box_bounds(-6.0, 6.0, 34.0, 120.0, 0.04, 0.10, "Mat_Arch_Asphalte_Base")

    # Yellow road center dashes along main avenues
    for rx in range(-92, 96, 10):
        b.add_box_bounds(rx, rx + 4.5, -104.2, -103.8, 0.10, 0.11, "Mat_Arch_Marquage_Jaune")
        b.add_box_bounds(rx, rx + 4.5, -6.2, -5.8, 0.10, 0.11, "Mat_Arch_Marquage_Jaune")
        b.add_box_bounds(rx, rx + 4.5, 63.8, 64.2, 0.10, 0.11, "Mat_Arch_Marquage_Jaune")
    for ry in range(-92, 100, 10):
        b.add_box_bounds(-29.2, -28.8, ry, ry + 4.5, 0.10, 0.11, "Mat_Arch_Marquage_Jaune")
        b.add_box_bounds(30.8, 31.2, ry, ry + 4.5, 0.10, 0.11, "Mat_Arch_Marquage_Jaune")

    # --- STREETLAMPS & NIGHT LIGHTING ---
    for ly in (-165.0, -148.0, -132.0):
        amc.add_streetlamp(b, -10.2, ly, 0.16, height=6.0)
        amc.add_streetlamp(b, 10.2, ly, 0.16, height=6.0)
    for lx in range(-85, 90, 25):
        amc.add_streetlamp(b, lx, -97.0, 0.04, height=5.8)
        amc.add_streetlamp(b, lx, 1.0, 0.04, height=5.8)
        amc.add_streetlamp(b, lx, 71.0, 0.04, height=5.8)
    for ly in range(-80, 90, 28):
        amc.add_streetlamp(b, -23.0, ly, 0.04, height=5.8)
        amc.add_streetlamp(b, 25.0, ly, 0.04, height=5.8)

    # --- TROPICAL VEGETATION & YOPOUGON / BANCO FOREST EDGE ---
    # Royal Palms along the Main Access Boulevard and Parade Ground
    for py in (-168.0, -152.0, -136.0, -90.0, -64.0, -48.0, -32.0, -16.0):
        amc.add_palm_tree(b, -13.5, py, 0.04, height=7.8)
        amc.add_palm_tree(b, 13.5, py, 0.04, height=7.8)

    # Dense Tropical Forest Belt outside the North, East, and South perimeter walls
    # creating a natural transition with the Yopougon / Banco forest outskirts
    forest_ring = []
    for fx in range(-105, 115, 18):
        forest_ring.append((fx, 127.0, 10.5, 4.8))
        forest_ring.append((fx + 7.0, 136.0, 12.0, 5.4))
    for fy in range(-105, 125, 20):
        forest_ring.append((-118.0, fy, 10.0, 4.5))
        forest_ring.append((118.0, fy, 10.2, 4.6))
    for tx, ty, th, tr in forest_ring:
        _add_tropical_shade_tree(b, tx, ty, 0.0, height=th, canopy_r=tr)

    obj = b.build_object(parent=ext_root)
    _link_also_to_collection(obj, COL_08_VOIRIE)
    return b.colliders


# ==============================================================================
# 9. POINTS D'APPARITION, VEHICULES, PNJ & ZONES DE MISSION (Unity Markers)
# ==============================================================================
def build_zone_10_spawns_and_markers(ext_root):
    """Create Empty hierarchy nodes under ext_root so Unity can query exact transform markers
    for vehicles, helicopters, guards, officers, and mission zones."""
    markers = [
        # Player & Checkpoint Spawns
        ("Spawn_Player_Entree_Base", (0.0, -135.0, 0.10)),
        ("Spawn_Garde_Porte_Ouest_1", (-3.5, -118.0, 0.25)),
        ("Spawn_Garde_Porte_Ouest_2", (3.5, -118.0, 0.25)),
        ("Spawn_Garde_Porte_Logistique_NO", (-57.0, -116.0, 0.15)),
        ("Spawn_Garde_Porte_Foret_Est", (8.0, 116.0, 0.15)),
        # Watchtower Sniper / Guard Spawns (Z = 7.65m platform height)
        ("Spawn_Garde_Mirador_NO", (-103.5, -113.5, 7.65)),
        ("Spawn_Garde_Mirador_SO", (103.5, -113.5, 7.65)),
        ("Spawn_Garde_Mirador_NE", (-103.5, 113.5, 7.65)),
        ("Spawn_Garde_Mirador_SE", (103.5, 113.5, 7.65)),
        ("Spawn_Garde_Mirador_Nord", (-103.5, 0.0, 7.65)),
        ("Spawn_Garde_Mirador_Sud", (103.5, 0.0, 7.65)),
        # Command & Barracks NPCs
        ("Spawn_Officier_Commandant_QG", (-15.0, 27.5, 0.35)),
        ("Spawn_Officier_General_R1", (-12.0, 10.5, 4.75)),
        ("Spawn_Armurier_Depot", (-81.0, 76.0, 0.30)),
        ("Spawn_Medecin_Infirmerie", (55.0, 54.0, 0.25)),
        # Dynamic Playable Vehicle & Helicopter Spawns
        ("Spawn_Helico_Pad_1", (12.0, 93.0, 0.30)),
        ("Spawn_Helico_Pad_2", (44.0, 93.0, 0.30)),
        ("Spawn_Helico_Hangar_1", (80.0, 91.0, 0.30)),
        ("Spawn_Vehicule_Blinde_1", (-42.5, -32.0, 0.20)),
        ("Spawn_Vehicule_Blinde_2", (-47.0, -60.0, 0.20)),
        ("Spawn_Camion_Militaire_1", (-60.0, -60.0, 0.20)),
        ("Spawn_Camion_Militaire_2", (-72.0, -60.0, 0.20)),
        ("Spawn_Vehicule_EtatMajor_QG", (-12.0, -6.0, 0.15)),
        # Gameplay Mission Trigger Zones
        ("Zone_Mission_Place_Armes", (0.0, -40.0, 0.18)),
        ("Zone_Mission_Briefing_QG", (-15.0, 18.0, 0.35)),
        ("Zone_Mission_Armurerie_Munitions", (-81.0, 86.0, 0.30)),
        ("Zone_Ravitaillement_Carburant", (-46.0, 76.5, 0.25)),
    ]

    marker_manifest = []
    for name, pos in markers:
        emp = bpy.data.objects.new(name, None)
        emp.empty_display_type = "PLAIN_AXES"
        emp.empty_display_size = 1.5
        emp.location = pos
        emp.parent = ext_root
        _link_also_to_collection(emp, COL_ROOT_EXT)
        _link_also_to_collection(emp, COL_10_SPAWNS)
        marker_manifest.append(
            {
                "name": name,
                "blender_pos": [round(pos[0], 3), round(pos[1], 3), round(pos[2], 3)],
                "unity_local_pos": [-round(pos[0], 3), round(pos[2], 3), -round(pos[1], 3)],
            }
        )
    return marker_manifest


def main():
    amc.register_monument_materials()
    ac.reset_scene()

    # Create all 10 modular thematic collections + 2 export root collections
    for cname in (
        COL_ROOT_EXT,
        COL_ROOT_INT,
        COL_01_PERIMETRE,
        COL_02_QG,
        COL_03_CASERNES,
        COL_04_MESS_INF,
        COL_05_LOGISTIQUE,
        COL_06_VEHICULES,
        COL_07_HELIPORT,
        COL_08_VOIRIE,
        COL_09_INTERIEURS,
        COL_10_SPAWNS,
    ):
        _ensure_collection(cname)

    ext_root = amc.make_empty_root("Base_Militaire_Yopougon_Ext", COL_ROOT_EXT)
    int_root = amc.make_empty_root("Base_Militaire_Yopougon_Int", COL_ROOT_INT)

    ext_colliders = []
    int_colliders = []
    ext_doors_meta = []
    ext_door_objs = []
    int_doors_meta = []
    int_door_objs = []

    # Build all 8 architectural zones + interiors + gameplay markers
    ext_colliders.extend(build_zone_01_perimetre(ext_root, ext_doors_meta, ext_door_objs))

    c_qg_ext, c_qg_int = build_zone_02_qg(
        ext_root, int_root, ext_doors_meta, ext_door_objs, int_doors_meta, int_door_objs
    )
    ext_colliders.extend(c_qg_ext)
    int_colliders.extend(c_qg_int)

    c_cas_ext, c_cas_int = build_zone_03_casernes(ext_root, int_root, ext_doors_meta, ext_door_objs)
    ext_colliders.extend(c_cas_ext)
    int_colliders.extend(c_cas_int)

    c_mess_ext, c_mess_int = build_zone_04_mess_infirmerie(ext_root, int_root, ext_doors_meta, ext_door_objs)
    ext_colliders.extend(c_mess_ext)
    int_colliders.extend(c_mess_int)

    c_log_ext, c_log_int = build_zone_05_munitions_logistique(ext_root, int_root, ext_doors_meta, ext_door_objs)
    ext_colliders.extend(c_log_ext)
    int_colliders.extend(c_log_int)

    ext_colliders.extend(build_zone_06_vehicules_ateliers(ext_root, ext_doors_meta, ext_door_objs))
    ext_colliders.extend(build_zone_07_heliport(ext_root, ext_doors_meta, ext_door_objs))
    ext_colliders.extend(build_zone_08_voirie_environnement(ext_root))

    marker_manifest = build_zone_10_spawns_and_markers(ext_root)

    # Save master .blend file with all modular collections intact
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    # Export Exterior and Interior FBXs (1:1 scale, -Z forward / Y up for Unity)
    ext_objs = [ext_root] + list(ext_root.children_recursive)
    int_objs = [int_root] + list(int_root.children_recursive)
    ac.export_objects_fbx(ext_objs, EXT_FBX_PATH)
    ac.export_objects_fbx(int_objs, INT_FBX_PATH)

    manifest = {
        "name": "Base_Militaire_Yopougon",
        "lot_meters": [LOT_W, LOT_D],
        "unity_placement": {
            "commune": "Yopougon",
            "position": [-660.0, 0.0, 745.0],
            "to_street": [-1.0, 0.0, 0.0],
        },
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Base_Militaire_Yopougon.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Base_Militaire_Yopougon_Interieur.fbx",
        "exterior_colliders": ext_colliders,
        "interior_colliders": int_colliders,
        "exterior_doors": ext_doors_meta,
        "interior_doors": int_doors_meta,
        "gameplay_markers": marker_manifest,
    }
    ac.save_collider_manifest(MANIFEST_PATH, manifest)

    # Render preview views of the complete military base
    cam_specs = [
        ("ext_aerial_global", (-195.0, -215.0, 135.0), (0.0, -10.0, 4.0), []),
        ("ext_entree_principale", (-32.0, -168.0, 14.0), (0.0, -110.0, 3.5), []),
        ("ext_qg_place_armes", (-38.0, -68.0, 18.0), (0.0, 18.0, 7.5), []),
        ("ext_heliport_hangars", (-18.0, 52.0, 26.0), (45.0, 92.0, 3.0), []),
    ]
    amc.render_monument_previews(PREVIEW_DIR, "base_militaire_yopougon", cam_specs)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(
        f"[BaseMilitaireYopougon] Termine : ext_colliders={len(ext_colliders)} "
        f"int_colliders={len(int_colliders)} ext_doors={len(ext_doors_meta)} "
        f"int_doors={len(int_doors_meta)} markers={len(marker_manifest)}"
    )


if __name__ == "__main__":
    main()
