"""
Build and export Building #1 (v2 Grand Luxury): Duplex Moderne Clôturé Abidjanais (Cocody / Riviera).

Upgraded Specifications (1:1 scale, mobile-optimized, 3rd-person camera comfortable):
- Lot footprint: 30.0 m (frontage along street) x 36.0 m (depth) = 1,080 m², centered at (0, 0, 0).
- Fence height: 2.45 m (pillars 2.65 m) with sliding main gate (5.4 m) & pedestrian gate (1.5 m).
- R+1 building footprint: 22.0 m x 17.5 m, height 8.95 m (RDC ceiling 3.80 m, R+1 ceiling 3.60 m).
- Staircase: wide 1.85 m U-turn flights, 11 steps per flight over 3.50 m run (30.05 deg slope, verified colliders).
- Includes front tropical garden + swimming pool & timber sun deck, 3-SUV carport, guard booth, polytank, AC units.
- Exports:
  1. IvoryState/Assets/_Project/Art/Models/Architecture/Maisons/SM_Duplex_Cocody_01.fbx
  2. IvoryState/Assets/_Project/Art/Models/Architecture/Interieurs/SM_Duplex_Cocody_01_Interieur.fbx
  3. tools/blender/architecture/Duplex_Cocody_01.blend
  4. tools/blender/architecture/duplex_cocody_01_manifest.json
"""

import math
import os
import sys
import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from arch_common import (  # noqa: E402
    ArchMeshBuilder,
    export_objects_fbx,
    render_preview_views,
    reset_scene,
    save_collider_manifest,
)


REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
EXT_FBX_PATH = os.path.join(
    REPO_ROOT,
    "IvoryState",
    "Assets",
    "_Project",
    "Art",
    "Models",
    "Architecture",
    "Maisons",
    "SM_Duplex_Cocody_01.fbx",
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
    "SM_Duplex_Cocody_01_Interieur.fbx",
)
BLEND_PATH = os.path.join(SCRIPT_DIR, "Duplex_Cocody_01.blend")
MANIFEST_PATH = os.path.join(SCRIPT_DIR, "duplex_cocody_01_manifest.json")


def make_empty_root(name, collection_name="Architecture"):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    col = bpy.data.collections.get(collection_name)
    if col is None:
        col = bpy.data.collections.new(collection_name)
        bpy.context.scene.collection.children.link(col)
    col.objects.link(empty)
    empty.location = (0.0, 0.0, 0.0)
    empty.rotation_euler = (0.0, 0.0, 0.0)
    empty.scale = (1.0, 1.0, 1.0)
    return empty


def add_door_leaf_builder(
    name,
    collection_name,
    pivot_world,
    leaf_bounds_local,
    mat_panel,
    mat_frame="Mat_Arch_Metal_Anthracite",
    mat_handle="Mat_Arch_Metal_Inox",
    glass_bounds_local=None,
    default_rot_deg=(0.0, 0.0, 0.0),
    offset_world=(0.0, 0.0, 0.0),
    is_sliding=False,
    open_note="",
):
    """Create a separate hinged or sliding door/gate builder around its true pivot."""
    origin = (
        pivot_world[0] + offset_world[0],
        pivot_world[1] + offset_world[1],
        pivot_world[2] + offset_world[2],
    )
    b = ArchMeshBuilder(name, collection_name=collection_name, origin=origin)
    x0, x1, y0, y1, z0, z1 = leaf_bounds_local
    ox, oy, oz = origin
    b.add_box_bounds(
        ox + x0, ox + x1, oy + y0, oy + y1, oz + z0, oz + z1, mat_panel, collider=True
    )
    if glass_bounds_local is not None:
        gx0, gx1, gy0, gy1, gz0, gz1 = glass_bounds_local
        b.add_box_bounds(
            ox + gx0,
            ox + gx1,
            oy + gy0,
            oy + gy1,
            oz + gz0,
            oz + gz1,
            "Mat_Arch_Vitre_Ext",
        )
        b.add_box_bounds(
            ox + x0, ox + x1, oy + y0 - 0.005, oy + y1 + 0.005, oz + z1 - 0.06, oz + z1, mat_frame
        )
        b.add_box_bounds(
            ox + x0, ox + x1, oy + y0 - 0.005, oy + y1 + 0.005, oz + z0, oz + z0 + 0.06, mat_frame
        )

    sx = x1 - x0
    sy = y1 - y0
    if sx >= sy:
        hx = ox + (x1 - 0.14 if x1 > 0.25 else x0 + 0.14)
        hz = oz + 1.05
        b.add_box_bounds(
            hx - 0.07, hx + 0.07, oy + y0 - 0.035, oy + y1 + 0.035, hz - 0.02, hz + 0.02, mat_handle
        )
    else:
        hy = oy + (y1 - 0.14 if y1 > 0.25 else y0 + 0.14)
        hz = oz + 1.05
        b.add_box_bounds(
            ox + x0 - 0.035, ox + x1 + 0.035, hy - 0.07, hy + 0.07, hz - 0.02, hz + 0.02, mat_handle
        )

    meta = {
        "name": name,
        "type": "sliding" if is_sliding else "hinged",
        "default_rot_deg": list(default_rot_deg),
        "open_note": open_note,
        "colliders": b.colliders,
    }
    return b, meta, default_rot_deg


def add_palm_tree(b, cx, cy, z0, height=5.0):
    """Add a stylized low-poly tropical royal palm for the Cocody yard."""
    b.add_cylinder(cx, cy, z0, z0 + 0.25, 0.56, 8, "Mat_Arch_Parement_Pierre")
    b.add_cylinder(cx, cy, z0 + 0.16, z0 + 0.27, 0.48, 8, "Mat_Arch_Gazon")
    trunk_h = height * 0.72
    b.add_cone(cx, cy, z0 + 0.25, z0 + 0.25 + trunk_h, 0.22, 0.12, 8, "Mat_Arch_Bois_Exterieur")
    top_z = z0 + 0.25 + trunk_h
    for tier_idx, (count, radius_off, length, z_off, pitch) in enumerate(
        [(7, 0.96, 1.90, 0.05, 0.42), (5, 0.65, 1.38, 0.38, -0.20)]
    ):
        for i in range(count):
            ang = i * (2.0 * math.pi / count) + tier_idx * 0.35
            dx = math.cos(ang) * radius_off
            dy = math.sin(ang) * radius_off
            b.add_box_center(
                cx + dx,
                cy + dy,
                top_z + z_off,
                length,
                0.34,
                0.06,
                "Mat_Arch_Gazon",
                rot_euler=(0.0, pitch, ang),
            )
    b.add_cone(cx, cy, top_z - 0.02, top_z + 0.78, 0.38, 0.05, 6, "Mat_Arch_Gazon")


def build_exterior():
    """Build grand 30m x 36m Cocody villa exterior, fence, pool, carport & exterior doors."""
    col_name = "Duplex_Exterieur"
    ext_struct = ArchMeshBuilder("Duplex_Structure_Ext", collection_name=col_name)
    ext_details = ArchMeshBuilder("Duplex_Details_Ext", collection_name=col_name)
    door_objs = []
    door_metas = []

    # -------------------------------------------------------------------------
    # 1. COURTYARD, DRIVEWAY & SWIMMING POOL (30m x 36m -> X [-15, +15], Y [-18, +18])
    # -------------------------------------------------------------------------
    # Paved driveway & front courtyard (X [-5.10, +14.70], Y [-17.75, -5.00])
    ext_struct.add_box_bounds(
        -5.10, 14.70, -17.75, -5.00, 0.0, 0.10, "Mat_Arch_Paves_Cour", uv_scale=0.5, collider=True
    )
    # Front garden lawn (left front around pool & deck)
    ext_struct.add_box_bounds(
        -14.70, -5.10, -17.75, -13.60, 0.0, 0.09, "Mat_Arch_Gazon", uv_scale=0.4, collider=True
    )
    ext_struct.add_box_bounds(
        -14.70, -13.60, -13.60, -5.00, 0.0, 0.09, "Mat_Arch_Gazon", uv_scale=0.4, collider=True
    )
    ext_struct.add_box_bounds(
        -6.20, -5.10, -13.60, -5.00, 0.0, 0.09, "Mat_Arch_Gazon", uv_scale=0.4, collider=True
    )
    # Garden curb divider
    ext_struct.add_box_bounds(-5.20, -5.05, -17.70, -5.00, 0.09, 0.16, "Mat_Arch_Beton_Brut")
    # Driveway threshold ramp bridging sidewalk (y=0.15 outside) to courtyard (z=0.10)
    ext_struct.add_box_bounds(
        -4.90, 3.10, -18.50, -17.75, 0.0, 0.10, "Mat_Arch_Beton_Brut", collider=True
    )

    # Modern Swimming Pool + Sun Deck (X [-13.60, -6.20], Y [-13.60, -5.00])
    # Pool surround coping & shallow liner + turquoise water
    ext_struct.add_box_bounds(-13.60, -6.20, -13.60, -7.40, 0.0, 0.14, "Mat_Arch_Piscine_Fond", collider=True)
    ext_struct.add_box_bounds(-13.60, -6.20, -13.60, -13.15, 0.14, 0.24, "Mat_Arch_Parement_Pierre", collider=True)
    ext_struct.add_box_bounds(-13.60, -6.20, -7.85, -7.40, 0.14, 0.24, "Mat_Arch_Parement_Pierre", collider=True)
    ext_struct.add_box_bounds(-13.60, -13.15, -13.15, -7.85, 0.14, 0.24, "Mat_Arch_Parement_Pierre", collider=True)
    ext_struct.add_box_bounds(-6.65, -6.20, -13.15, -7.85, 0.14, 0.24, "Mat_Arch_Parement_Pierre", collider=True)
    ext_details.add_box_bounds(-13.15, -6.65, -13.15, -7.85, 0.15, 0.21, "Mat_Arch_Eau_Piscine")
    # Wooden Sun Deck beside pool (Y [-7.40, -5.00]) + 2 loungers
    ext_struct.add_box_bounds(-13.60, -6.20, -7.40, -5.00, 0.0, 0.24, "Mat_Arch_Sol_Terrasse", collider=True)
    for lx in (-12.0, -9.6):
        ext_details.add_box_bounds(lx - 0.40, lx + 0.40, -6.85, -5.35, 0.24, 0.46, "Mat_Arch_Bois_Exterieur")
        ext_details.add_box_center(lx, -5.55, 0.62, 0.78, 0.55, 0.08, "Mat_Arch_MurExt_Blanc", rot_euler=(-0.55, 0.0, 0.0))

    # Wide side passages (left & right 3.75 m) & rear garden/terrace (Y [12.50, 17.75])
    ext_struct.add_box_bounds(
        -14.70, -11.00, -5.00, 17.75, 0.0, 0.10, "Mat_Arch_Paves_Cour", uv_scale=0.5, collider=True
    )
    ext_struct.add_box_bounds(
        11.00, 14.70, -5.00, 17.75, 0.0, 0.10, "Mat_Arch_Paves_Cour", uv_scale=0.5, collider=True
    )
    ext_struct.add_box_bounds(
        -11.00, 11.00, 12.50, 17.75, 0.0, 0.10, "Mat_Arch_Gazon", uv_scale=0.4, collider=True
    )
    # Covered rear patio terrace (X [-11.0, +4.5], Y [12.50, 16.00], Z = 0.10..0.33) + step
    ext_struct.add_box_bounds(
        -11.00, 4.50, 12.50, 16.00, 0.10, 0.33, "Mat_Arch_Sol_Terrasse", uv_scale=0.5, collider=True
    )
    ext_struct.add_box_bounds(
        -9.00, 2.50, 16.00, 16.45, 0.10, 0.22, "Mat_Arch_Beton_Brut", collider=True
    )

    # -------------------------------------------------------------------------
    # 2. PERIMETER FENCE WALL (H = 2.45 m, Pillars = 2.65 m)
    # -------------------------------------------------------------------------
    fence_h = 2.45
    plinth_h = 0.42
    coping_h = 0.08
    # Left wall (X [-15.0, -14.70])
    ext_struct.add_box_bounds(-15.0, -14.70, -18.0, 18.0, 0.0, plinth_h, "Mat_Arch_MurExt_Anthracite", collider=True)
    ext_struct.add_box_bounds(-15.0, -14.70, -18.0, 18.0, plinth_h, fence_h - coping_h, "Mat_Arch_MurExt_Blanc", collider=True)
    ext_struct.add_box_bounds(-15.04, -14.66, -18.0, 18.0, fence_h - coping_h, fence_h, "Mat_Arch_Beton_Brut")
    # Right wall (X [14.70, 15.0])
    ext_struct.add_box_bounds(14.70, 15.0, -18.0, 18.0, 0.0, plinth_h, "Mat_Arch_MurExt_Anthracite", collider=True)
    ext_struct.add_box_bounds(14.70, 15.0, -18.0, 18.0, plinth_h, fence_h - coping_h, "Mat_Arch_MurExt_Blanc", collider=True)
    ext_struct.add_box_bounds(14.66, 15.04, -18.0, 18.0, fence_h - coping_h, fence_h, "Mat_Arch_Beton_Brut")
    # Rear wall (Y [17.70, 18.0])
    ext_struct.add_box_bounds(-14.70, 14.70, 17.70, 18.0, 0.0, plinth_h, "Mat_Arch_MurExt_Anthracite", collider=True)
    ext_struct.add_box_bounds(-14.70, 14.70, 17.70, 18.0, plinth_h, fence_h - coping_h, "Mat_Arch_MurExt_Blanc", collider=True)
    ext_struct.add_box_bounds(-14.70, 14.70, 17.66, 18.04, fence_h - coping_h, fence_h, "Mat_Arch_Beton_Brut")
    # Pilasters
    for py in (-13.0, -7.5, -2.0, 3.5, 9.0, 14.5, 17.85):
        ext_struct.add_box_bounds(-15.08, -14.62, py - 0.18, py + 0.18, 0.0, 2.52, "Mat_Arch_MurExt_Anthracite")
        ext_struct.add_box_bounds(14.62, 15.08, py - 0.18, py + 0.18, 0.0, 2.52, "Mat_Arch_MurExt_Anthracite")
    for px in (-9.0, -3.0, 3.0, 9.0):
        ext_struct.add_box_bounds(px - 0.18, px + 0.18, 17.64, 18.06, 0.0, 2.52, "Mat_Arch_MurExt_Anthracite")

    # Front fence (Y = -17.85) with pedestrian opening [-4.60, -3.10] & main gate [-2.55, +2.85]
    def build_front_fence_span(x0, x1):
        ext_struct.add_box_bounds(x0, x1, -18.00, -17.70, 0.0, plinth_h, "Mat_Arch_MurExt_Anthracite", collider=True)
        ext_struct.add_box_bounds(x0, x1, -18.00, -17.70, plinth_h, 1.48, "Mat_Arch_MurExt_Blanc", collider=True)
        ext_struct.add_box_bounds(x0, x1, -18.03, -17.67, 1.48, 1.56, "Mat_Arch_Beton_Brut")
        for sz in (1.68, 1.85, 2.02, 2.19, 2.36):
            ext_details.add_box_bounds(
                x0, x1, -17.88, -17.82, sz - 0.035, sz + 0.035, "Mat_Arch_Metal_Anthracite"
            )
        ext_struct.add_collider_bounds(x0, x1, -18.00, -17.70, 1.48, 2.42)

    build_front_fence_span(-14.70, -5.00)
    build_front_fence_span(3.40, 12.20)

    # Guard booth / guérite (X [12.20, 14.70], Y [-18.00, -15.40])
    ext_struct.add_box_bounds(12.20, 14.70, -18.00, -15.40, 0.0, 0.14, "Mat_Arch_Beton_Brut", collider=True)
    ext_struct.add_wall_with_openings(
        "X", -17.85, 0.22, 12.20, 14.70, 0.14, 2.60, "Mat_Arch_MurExt_Blanc",
        openings=[{"s0": 12.80, "s1": 14.10, "z0": 1.10, "z1": 2.15, "kind": "window"}],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    ext_struct.add_wall_with_openings(
        "X", -15.52, 0.20, 12.20, 14.70, 0.14, 2.60, "Mat_Arch_MurExt_Blanc",
        openings=[{"s0": 12.80, "s1": 13.95, "z0": 0.14, "z1": 2.25, "kind": "door"}],
        frame_mat="Mat_Arch_Metal_Anthracite",
    )
    ext_struct.add_wall_with_openings(
        "Y", 12.32, 0.20, -17.75, -15.52, 0.14, 2.60, "Mat_Arch_MurExt_Blanc",
        openings=[{"s0": -17.30, "s1": -16.10, "z0": 1.10, "z1": 2.15, "kind": "window"}],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    ext_struct.add_box_bounds(12.05, 14.90, -18.12, -15.28, 2.60, 2.82, "Mat_Arch_MurExt_Anthracite", collider=True)

    # Gate travertine-stone pillars (H = 2.65 m) + Night sconces
    for x0, x1 in [(-15.05, -14.55), (-5.10, -4.60), (-3.10, -2.55), (2.85, 3.40)]:
        ext_struct.add_box_bounds(x0, x1, -18.08, -17.62, 0.0, 2.65, "Mat_Arch_Parement_Pierre", collider=True)
        ext_struct.add_box_bounds(x0 - 0.05, x1 + 0.05, -18.12, -17.58, 2.65, 2.74, "Mat_Arch_Beton_Brut")
    for lx in (-4.85, -2.825, 3.125):
        ext_details.add_box_bounds(lx - 0.12, lx + 0.12, -18.16, -18.08, 1.95, 2.26, "Mat_Arch_Metal_Anthracite")
        ext_details.add_box_bounds(lx - 0.09, lx + 0.09, -18.18, -18.14, 1.98, 2.23, "Mat_Arch_Lumiere_Nuit")

    # Driveway steel sliding track
    ext_details.add_box_bounds(-2.55, 11.80, -17.66, -17.56, 0.10, 0.13, "Mat_Arch_Metal_Inox")

    # Separate Gates (open for seamless walkthrough)
    gate_ped_b, gate_ped_m, gate_ped_rot = add_door_leaf_builder(
        "Gate_Pedestrian",
        col_name,
        pivot_world=(-4.60, -17.85, 0.10),
        leaf_bounds_local=(0.0, 1.48, -0.035, 0.035, 0.02, 2.34),
        mat_panel="Mat_Arch_Bois_Exterieur",
        mat_frame="Mat_Arch_Metal_Anthracite",
        default_rot_deg=(0.0, 0.0, 85.0),
        open_note="Pedestrian gate swung +85 deg inward",
    )
    door_objs.append(gate_ped_b.build_object(default_rot_deg=gate_ped_rot))
    door_metas.append(gate_ped_m)

    gate_main_b = ArchMeshBuilder("Gate_Main", collection_name=col_name, origin=(0.65, -17.60, 0.10))
    ox, oy, oz = 0.65, -17.60, 0.10
    gate_main_b.add_box_bounds(ox, ox + 5.45, oy - 0.045, oy + 0.045, oz + 0.03, oz + 2.35, "Mat_Arch_Metal_Anthracite", collider=True)
    for kz in range(6):
        sz0 = oz + 0.25 + kz * 0.32
        gate_main_b.add_box_bounds(ox + 0.20, ox + 5.25, oy - 0.055, oy + 0.055, sz0, sz0 + 0.19, "Mat_Arch_Bois_Exterieur")
    door_objs.append(gate_main_b.build_object())
    door_metas.append({
        "name": "Gate_Main",
        "type": "sliding",
        "closed_offset_x": -3.20,
        "open_note": "Slides along X between closed (-2.55) and open (+0.65), leaving 3.20m clear entry",
        "colliders": gate_main_b.colliders,
    })

    # -------------------------------------------------------------------------
    # 3. 3-SUV CARPORT, PALMS, AC SPLITS & POLYTANK
    # -------------------------------------------------------------------------
    for px in (4.55, 14.15):
        for py in (-13.70, -5.60):
            ext_details.add_box_bounds(px - 0.16, px + 0.16, py - 0.16, py + 0.16, 0.10, 0.26, "Mat_Arch_Beton_Brut")
            ext_details.add_box_bounds(px - 0.09, px + 0.09, py - 0.09, py + 0.09, 0.26, 3.22, "Mat_Arch_Metal_Anthracite", collider=True)
    ext_details.add_box_bounds(4.25, 14.45, -14.05, -5.00, 3.22, 3.44, "Mat_Arch_Metal_Anthracite")
    ext_details.add_box_bounds(4.35, 14.35, -13.95, -5.05, 3.19, 3.22, "Mat_Arch_Bois_Exterieur")
    ext_details.add_box_bounds(4.45, 14.25, -13.80, -13.74, 3.17, 3.20, "Mat_Arch_Lumiere_Nuit")

    add_palm_tree(ext_details, -13.4, -15.8, 0.09, height=5.2)
    add_palm_tree(ext_details, -7.8, -15.6, 0.09, height=4.6)
    add_palm_tree(ext_details, -13.6, 15.2, 0.10, height=4.8)

    # Polytank tower at back-left utility corner
    ext_details.add_box_bounds(-14.35, -12.45, 14.60, 16.50, 0.10, 0.34, "Mat_Arch_Beton_Brut", collider=True)
    for lx in (-14.05, -12.75):
        for ly in (14.90, 16.20):
            ext_details.add_box_bounds(lx - 0.05, lx + 0.05, ly - 0.05, ly + 0.05, 0.34, 1.55, "Mat_Arch_Metal_Anthracite")
    ext_details.add_box_bounds(-14.20, -12.60, 14.75, 16.35, 1.50, 1.60, "Mat_Arch_Metal_Anthracite", collider=True)
    ext_details.add_cylinder(-13.40, 15.55, 1.60, 3.25, 0.72, 12, "Mat_Arch_Cuve_Bleue")
    ext_details.add_cylinder(-13.40, 15.55, 3.25, 3.36, 0.28, 10, "Mat_Arch_Metal_Anthracite")

    # Outdoor AC split units strictly on solid masonry piers
    ext_details.add_ac_split_outdoor(-11.16, 2.55, 2.95, facing_axis="-X")
    ext_details.add_ac_split_outdoor(-11.16, 2.55, 6.45, facing_axis="-X")
    ext_details.add_ac_split_outdoor(-11.16, 11.20, 2.95, facing_axis="-X")
    ext_details.add_ac_split_outdoor(11.16, 2.85, 2.95, facing_axis="+X")
    ext_details.add_ac_split_outdoor(11.16, 5.15, 6.45, facing_axis="+X")
    ext_details.add_ac_split_outdoor(11.16, 11.20, 6.45, facing_axis="+X")

    # -------------------------------------------------------------------------
    # 4. GRAND R+1 VILLA SHELL (X [-11.00, +11.00], Y [-5.00, +12.50], H = 8.95 m)
    # -------------------------------------------------------------------------
    # Foundation slab (Z = 0.00..0.32)
    ext_struct.add_box_bounds(-11.00, 11.00, -5.00, 12.50, 0.0, 0.32, "Mat_Arch_Beton_Brut", collider=True)
    # Grand front porch (X [-5.20, +0.40], Y [-7.20, -5.00]) + 3 smooth entry steps
    ext_struct.add_box_bounds(-5.20, 0.40, -7.20, -5.00, 0.0, 0.32, "Mat_Arch_Beton_Brut", collider=True)
    ext_struct.add_box_bounds(-5.20, 0.40, -7.20, -5.00, 0.32, 0.35, "Mat_Arch_Sol_Terrasse")
    ext_struct.add_box_bounds(-4.60, -0.20, -7.55, -7.20, 0.10, 0.27, "Mat_Arch_Beton_Brut", collider=True)
    ext_struct.add_box_bounds(-4.60, -0.20, -7.90, -7.55, 0.10, 0.19, "Mat_Arch_Beton_Brut", collider=True)
    # Porch travertine columns
    ext_struct.add_box_bounds(-5.15, -4.65, -7.10, -6.60, 0.32, 4.15, "Mat_Arch_Parement_Pierre", collider=True)
    ext_struct.add_box_bounds(-0.15, 0.35, -7.10, -6.60, 0.32, 4.15, "Mat_Arch_Parement_Pierre", collider=True)

    # RDC Exterior Walls (Z = 0.32..4.15 -> 3.80 m ceiling!)
    # Front Wall (Y = -4.89)
    ext_struct.add_wall_with_openings(
        "X", -4.89, 0.22, -11.00, 11.00, 0.32, 4.15, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -9.60, "s1": -5.80, "z0": 0.70, "z1": 3.65, "kind": "window"},
            {"s0": -3.55, "s1": -1.35, "z0": 0.35, "z1": 3.00, "kind": "door"},
            {"s0": 0.65, "s1": 3.55, "z0": 0.70, "z1": 4.15, "kind": "window"},
            {"s0": 6.10, "s1": 9.30, "z0": 1.15, "z1": 3.30, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    # Rear Wall (Y = +12.39)
    ext_struct.add_wall_with_openings(
        "X", 12.39, 0.22, -11.00, 11.00, 0.32, 4.15, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -8.40, "s1": -4.40, "z0": 0.35, "z1": 3.20, "kind": "door"},
            {"s0": 0.20, "s1": 3.60, "z0": 1.35, "z1": 3.25, "kind": "window"},
            {"s0": 6.80, "s1": 8.30, "z0": 0.35, "z1": 2.95, "kind": "door"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    # Left Wall (X = -10.89)
    ext_struct.add_wall_with_openings(
        "Y", -10.89, 0.22, -4.78, 12.28, 0.32, 4.15, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -2.40, "s1": 1.60, "z0": 0.70, "z1": 3.65, "kind": "window"},
            {"s0": 6.20, "s1": 9.60, "z0": 1.00, "z1": 3.30, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    # Right Wall (X = +10.89)
    ext_struct.add_wall_with_openings(
        "Y", 10.89, 0.22, -4.78, 12.28, 0.32, 4.15, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -2.20, "s1": 1.20, "z0": 1.15, "z1": 3.30, "kind": "window"},
            {"s0": 3.40, "s1": 4.60, "z0": 1.95, "z1": 3.25, "kind": "window"},
            {"s0": 7.40, "s1": 10.20, "z0": 1.35, "z1": 3.25, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )

    # -------------------------------------------------------------------------
    # R+1 Structural Slab (Z = 4.15..4.37) with Double-Height Living Void & Stairwell Cutout
    # Living Void: X [-10.78, -1.80], Y [-4.78, +2.60]
    # Stairwell Cutout: X [+0.20, +4.45], Y [+0.20, +5.40]
    # -------------------------------------------------------------------------
    slab_rects = [
        (-11.00, -10.78, -5.00, 12.50),
        (-10.78, -1.80, -5.00, -4.78),
        (-10.78, -1.80, 2.60, 12.50),
        (-1.80, 0.20, -5.00, 12.50),
        (0.20, 4.45, -5.00, 0.20),
        (0.20, 4.45, 5.40, 12.50),
        (4.45, 11.00, -5.00, 12.50),
    ]
    for sx0, sx1, sy0, sy1 in slab_rects:
        ext_struct.add_box_bounds(sx0, sx1, sy0, sy1, 4.15, 4.37, "Mat_Arch_MurExt_Blanc", collider=True)

    # Front Filant Balcony (X [-11.00, +5.20], Y [-7.40, -5.00]) & Rear Master Balcony ([-6.20, -1.80], [12.50, 14.70])
    ext_struct.add_box_bounds(-11.00, 5.20, -7.40, -5.00, 4.15, 4.37, "Mat_Arch_MurExt_Anthracite", collider=True)
    ext_struct.add_box_bounds(-10.94, 5.14, -7.34, -5.00, 4.37, 4.40, "Mat_Arch_Sol_Terrasse")
    ext_struct.add_box_bounds(-10.94, 5.14, -7.34, -5.05, 4.12, 4.15, "Mat_Arch_Bois_Exterieur")
    ext_details.add_glass_railing("X", -7.32, -10.92, 5.12, 4.40)
    ext_details.add_glass_railing("Y", -10.92, -7.32, -5.00, 4.40)
    ext_details.add_glass_railing("Y", 5.12, -7.32, -5.00, 4.40)

    ext_struct.add_box_bounds(-6.20, -1.80, 12.50, 14.70, 4.13, 4.37, "Mat_Arch_MurExt_Anthracite", collider=True)
    ext_struct.add_box_bounds(-6.14, -1.86, 12.50, 14.64, 4.37, 4.40, "Mat_Arch_Sol_Terrasse")
    ext_struct.add_box_bounds(-6.14, -1.86, 12.50, 14.64, 4.10, 4.13, "Mat_Arch_Bois_Exterieur")
    ext_details.add_glass_railing("X", 14.62, -6.12, -1.88, 4.40)
    ext_details.add_glass_railing("Y", -6.12, 12.50, 14.62, 4.40)
    ext_details.add_glass_railing("Y", -1.88, 12.50, 14.62, 4.40)

    # Rear terrace louvered pergola flanking Master Balcony
    ext_struct.add_box_bounds(-11.00, -6.20, 12.50, 16.00, 4.02, 4.18, "Mat_Arch_Metal_Anthracite")
    ext_struct.add_box_bounds(-1.80, 4.50, 12.50, 16.00, 4.02, 4.18, "Mat_Arch_Metal_Anthracite")
    ext_struct.add_box_bounds(-6.20, -1.80, 14.70, 16.00, 4.02, 4.18, "Mat_Arch_Metal_Anthracite")
    for px in range(-106, 44, 5):
        xp = px * 0.10
        y_start = 14.72 if -6.25 <= xp <= -1.75 else 12.55
        ext_details.add_box_bounds(xp - 0.035, xp + 0.035, y_start, 15.90, 4.04, 4.16, "Mat_Arch_Bois_Exterieur")
    ext_details.add_box_bounds(-10.85, -10.55, 15.60, 15.90, 0.33, 4.04, "Mat_Arch_Parement_Pierre", collider=True)
    ext_details.add_box_bounds(4.15, 4.45, 15.60, 15.90, 0.33, 4.04, "Mat_Arch_Parement_Pierre", collider=True)
    ext_struct.add_box_bounds(-6.35, -1.65, 12.50, 14.85, 7.82, 8.02, "Mat_Arch_MurExt_Anthracite")

    # R+1 Exterior Walls (Z = 4.37..8.00 -> 3.60 m ceiling!)
    ext_struct.add_wall_with_openings(
        "X", -4.89, 0.22, -11.00, 11.00, 4.37, 8.00, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -9.60, "s1": -5.80, "z0": 4.80, "z1": 7.50, "kind": "window"},
            {"s0": -2.35, "s1": 0.05, "z0": 4.40, "z1": 7.15, "kind": "door"},
            {"s0": 0.65, "s1": 3.55, "z0": 4.37, "z1": 7.55, "kind": "window"},
            {"s0": 6.10, "s1": 9.30, "z0": 5.15, "z1": 7.25, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    ext_struct.add_wall_with_openings(
        "X", 12.39, 0.22, -11.00, 11.00, 4.37, 8.00, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -9.50, "s1": -7.20, "z0": 5.85, "z1": 7.20, "kind": "window"},
            {"s0": -5.80, "s1": -2.80, "z0": 4.40, "z1": 7.15, "kind": "door"},
            {"s0": -0.30, "s1": 3.50, "z0": 5.15, "z1": 7.25, "kind": "window"},
            {"s0": 6.20, "s1": 9.20, "z0": 5.15, "z1": 7.25, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    ext_struct.add_wall_with_openings(
        "Y", -10.89, 0.22, -4.78, 12.28, 4.37, 8.00, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -2.40, "s1": 1.60, "z0": 4.80, "z1": 7.50, "kind": "window"},
            {"s0": 4.60, "s1": 7.80, "z0": 5.15, "z1": 7.25, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    ext_struct.add_wall_with_openings(
        "Y", 10.89, 0.22, -4.78, 12.28, 4.37, 8.00, "Mat_Arch_MurExt_Blanc",
        openings=[
            {"s0": -2.20, "s1": 1.20, "z0": 5.15, "z1": 7.25, "kind": "window"},
            {"s0": 6.80, "s1": 9.80, "z0": 5.15, "z1": 7.25, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )

    # Roof Slab, Cantilever Overhang & Parapet (Total Height = 8.85..8.95 m)
    ext_struct.add_box_bounds(-11.00, 11.00, -5.00, 12.50, 8.00, 8.28, "Mat_Arch_Toiture_Terrasse", collider=True)
    ext_struct.add_box_bounds(-11.20, 11.20, -7.65, -5.00, 8.00, 8.28, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(-11.10, 11.10, -7.55, -5.00, 7.96, 8.00, "Mat_Arch_Bois_Exterieur")
    ext_details.add_box_bounds(-10.50, 10.50, -7.45, -7.38, 7.93, 7.96, "Mat_Arch_Lumiere_Nuit")

    ext_struct.add_box_bounds(-11.05, 11.05, -5.05, -4.80, 8.28, 8.85, "Mat_Arch_MurExt_Blanc")
    ext_struct.add_box_bounds(-11.05, 11.05, 12.28, 12.55, 8.28, 8.85, "Mat_Arch_MurExt_Blanc")
    ext_struct.add_box_bounds(-11.05, -10.80, -4.80, 12.28, 8.28, 8.85, "Mat_Arch_MurExt_Blanc")
    ext_struct.add_box_bounds(10.80, 11.05, -4.80, 12.28, 8.28, 8.85, "Mat_Arch_MurExt_Blanc")
    ext_struct.add_box_bounds(-11.08, 11.08, -5.08, -4.77, 8.82, 8.88, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(-11.08, 11.08, 12.25, 12.58, 8.82, 8.88, "Mat_Arch_MurExt_Anthracite")

    # Signature Stairwell Stone Tower & Claustra Brise-Soleil (up to 8.95m)
    ext_struct.add_box_bounds(0.25, 0.65, -5.10, -4.98, 0.32, 8.95, "Mat_Arch_Parement_Pierre")
    ext_struct.add_box_bounds(3.55, 4.05, -5.10, -4.98, 0.32, 8.95, "Mat_Arch_Parement_Pierre")
    ext_struct.add_box_bounds(0.25, 4.05, -5.10, -4.98, 7.55, 8.95, "Mat_Arch_Parement_Pierre")
    for i in range(9):
        bx = 0.80 + i * 0.32
        ext_details.add_box_bounds(bx - 0.035, bx + 0.035, -5.18, -5.02, 0.70, 7.55, "Mat_Arch_Bois_Exterieur")
    for hz in (2.15, 4.25, 5.95):
        ext_details.add_box_bounds(0.65, 3.55, -5.16, -5.02, hz - 0.035, hz + 0.035, "Mat_Arch_Metal_Anthracite")

    # Right wing R+1 architectural surround around Bedroom 2 window (X [6.10, 9.30], Z [5.15, 7.25])
    ext_struct.add_box_bounds(5.45, 6.10, -5.10, -4.98, 4.37, 8.00, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(9.30, 10.05, -5.10, -4.98, 4.37, 8.00, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(6.10, 9.30, -5.10, -4.98, 4.37, 5.15, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(6.10, 9.30, -5.10, -4.98, 7.25, 8.00, "Mat_Arch_MurExt_Anthracite")
    ext_struct.add_box_bounds(5.75, 9.75, -5.32, -5.02, 4.98, 5.15, "Mat_Arch_Beton_Brut")
    ext_struct.add_box_bounds(5.75, 9.75, -5.32, -5.02, 7.25, 7.42, "Mat_Arch_Beton_Brut")

    # Porch sconces & recessed downlight
    ext_details.add_box_bounds(-5.00, -4.88, -7.15, -7.10, 2.35, 2.65, "Mat_Arch_Lumiere_Nuit")
    ext_details.add_box_bounds(0.05, 0.17, -7.15, -7.10, 2.35, 2.65, "Mat_Arch_Lumiere_Nuit")
    ext_details.add_box_bounds(-2.90, -2.00, -6.40, -5.90, 4.09, 4.12, "Mat_Arch_Lumiere_Nuit")

    # -------------------------------------------------------------------------
    # 5. SEPARATE EXTERIOR DOORS (Open for Seamless Walkthrough)
    # -------------------------------------------------------------------------
    d_fl, d_fl_m, d_fl_r = add_door_leaf_builder(
        "Door_FrontEntry_L", col_name, (-3.52, -4.89, 0.35),
        (0.0, 1.05, -0.035, 0.035, 0.0, 2.62), "Mat_Arch_Bois_Exterieur",
        glass_bounds_local=(0.68, 0.90, -0.04, 0.04, 0.40, 2.25),
        default_rot_deg=(0.0, 0.0, 85.0), open_note="Left front entry door open +85 deg",
    )
    door_objs.append(d_fl.build_object(default_rot_deg=d_fl_r))
    door_metas.append(d_fl_m)

    d_fr, d_fr_m, d_fr_r = add_door_leaf_builder(
        "Door_FrontEntry_R", col_name, (-1.38, -4.89, 0.35),
        (-1.05, 0.0, -0.035, 0.035, 0.0, 2.62), "Mat_Arch_Bois_Exterieur",
        glass_bounds_local=(-0.90, -0.68, -0.04, 0.04, 0.40, 2.25),
        default_rot_deg=(0.0, 0.0, -85.0), open_note="Right front entry door open -85 deg",
    )
    door_objs.append(d_fr.build_object(default_rot_deg=d_fr_r))
    door_metas.append(d_fr_m)

    # Back Terrace Slider (X [-8.40, -4.40], Y = 12.39 -> 2.0m fixed + 2.0m slid open)
    ext_details.add_box_bounds(-8.38, -6.38, 12.37, 12.41, 0.37, 3.17, "Mat_Arch_Vitre_Ext")
    ext_details.add_box_bounds(-8.40, -8.34, 12.35, 12.43, 0.35, 3.20, "Mat_Arch_Metal_Anthracite")
    ext_details.add_box_bounds(-6.42, -6.36, 12.35, 12.43, 0.35, 3.20, "Mat_Arch_Metal_Anthracite")
    d_bt, d_bt_m, d_bt_r = add_door_leaf_builder(
        "Door_BackTerrace", col_name, (-8.25, 12.32, 0.35),
        (0.0, 2.02, -0.03, 0.03, 0.0, 2.82), "Mat_Arch_Metal_Anthracite",
        glass_bounds_local=(0.08, 1.94, -0.035, 0.035, 0.08, 2.74),
        is_sliding=True, open_note="Back terrace slider open leaving 1.95m clear passage",
    )
    door_objs.append(d_bt.build_object(default_rot_deg=d_bt_r))
    door_metas.append(d_bt_m)

    d_bs, d_bs_m, d_bs_r = add_door_leaf_builder(
        "Door_BackService", col_name, (6.84, 12.39, 0.35),
        (0.0, 1.42, -0.03, 0.03, 0.0, 2.58), "Mat_Arch_Metal_Anthracite",
        default_rot_deg=(0.0, 0.0, 82.0), open_note="Service door open +82 deg",
    )
    door_objs.append(d_bs.build_object(default_rot_deg=d_bs_r))
    door_metas.append(d_bs_m)

    # Upper Front Balcony Double French Glass Doors (X [-2.35, +0.05], Y = -4.89)
    d_ubl, d_ubl_m, d_ubl_r = add_door_leaf_builder(
        "Door_UpperBalcony_L", col_name, (-2.32, -4.89, 4.40),
        (0.0, 1.15, -0.03, 0.03, 0.0, 2.72), "Mat_Arch_Metal_Anthracite",
        glass_bounds_local=(0.08, 1.07, -0.035, 0.035, 0.08, 2.64),
        default_rot_deg=(0.0, 0.0, 85.0), open_note="Upper balcony left glass door open inward",
    )
    door_objs.append(d_ubl.build_object(default_rot_deg=d_ubl_r))
    door_metas.append(d_ubl_m)

    d_ubr, d_ubr_m, d_ubr_r = add_door_leaf_builder(
        "Door_UpperBalcony_R", col_name, (0.02, -4.89, 4.40),
        (-1.15, 0.0, -0.03, 0.03, 0.0, 2.72), "Mat_Arch_Metal_Anthracite",
        glass_bounds_local=(-1.07, -0.08, -0.035, 0.035, 0.08, 2.64),
        default_rot_deg=(0.0, 0.0, -85.0), open_note="Upper balcony right glass door open inward",
    )
    door_objs.append(d_ubr.build_object(default_rot_deg=d_ubr_r))
    door_metas.append(d_ubr_m)

    # Master Rear Balcony Slider (X [-5.80, -2.80], Y = 12.39)
    ext_details.add_box_bounds(-5.78, -4.28, 12.37, 12.41, 4.42, 7.12, "Mat_Arch_Vitre_Ext")
    d_mb, d_mb_m, d_mb_r = add_door_leaf_builder(
        "Door_MasterBalcony", col_name, (-5.70, 12.32, 4.40),
        (0.0, 1.52, -0.03, 0.03, 0.0, 2.72), "Mat_Arch_Metal_Anthracite",
        glass_bounds_local=(0.07, 1.45, -0.035, 0.035, 0.07, 2.65),
        is_sliding=True, open_note="Master balcony slider open leaving 1.50m clear",
    )
    door_objs.append(d_mb.build_object(default_rot_deg=d_mb_r))
    door_metas.append(d_mb_m)

    root = make_empty_root("Duplex_Cocody_01_Ext", collection_name=col_name)
    obj_struct = ext_struct.build_object(parent=root)
    obj_details = ext_details.build_object(parent=root)
    for dobj in door_objs:
        dobj.parent = root

    return root, [root, obj_struct, obj_details] + door_objs, ext_struct.colliders + ext_details.colliders, door_metas


def build_interior():
    """Build grand, bright, 3rd-person friendly RDC + R+1 interior with walkable U-stairs."""
    col_name = "Duplex_Interieur"
    int_arch = ArchMeshBuilder("Duplex_Int_Structure", collection_name=col_name)
    int_furn = ArchMeshBuilder("Duplex_Int_Mobilier", collection_name=col_name)
    door_objs = []
    door_metas = []

    # -------------------------------------------------------------------------
    # 1. FINISH FLOORS (RDC Z = 0.32..0.35, R+1 Z = 4.37..4.40)
    # -------------------------------------------------------------------------
    # RDC Floors
    int_arch.add_box_bounds(-10.78, 5.05, -4.78, 5.50, 0.32, 0.35, "Mat_Arch_Sol_Marbre", uv_scale=0.45)
    int_arch.add_box_bounds(-10.78, -1.80, 5.50, 12.28, 0.32, 0.35, "Mat_Arch_Sol_Marbre", uv_scale=0.45)
    int_arch.add_box_bounds(-1.80, 10.78, 5.50, 12.28, 0.32, 0.35, "Mat_Arch_Sol_Carrelage", uv_scale=0.5)
    int_arch.add_box_bounds(5.05, 10.78, -4.78, 2.60, 0.32, 0.35, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    int_arch.add_box_bounds(5.05, 10.78, 2.60, 5.50, 0.32, 0.35, "Mat_Arch_Sol_Carrelage", uv_scale=0.5)

    # R+1 Floors (matching double-height living void & stairwell cutout)
    int_arch.add_box_bounds(-1.80, 5.05, -4.78, 0.20, 4.37, 4.40, "Mat_Arch_Sol_Marbre", uv_scale=0.45)
    int_arch.add_box_bounds(-1.80, 0.20, 0.20, 7.20, 4.37, 4.40, "Mat_Arch_Sol_Marbre", uv_scale=0.45)
    int_arch.add_box_bounds(0.20, 5.05, 5.40, 7.20, 4.37, 4.40, "Mat_Arch_Sol_Marbre", uv_scale=0.45)
    int_arch.add_box_bounds(-10.78, -1.80, 2.60, 7.60, 4.37, 4.40, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    int_arch.add_box_bounds(-6.20, -1.80, 7.60, 12.28, 4.37, 4.40, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    int_arch.add_box_bounds(-10.78, -6.20, 7.60, 12.28, 4.37, 4.40, "Mat_Arch_Sol_Carrelage", uv_scale=0.5)
    int_arch.add_box_bounds(-1.80, 5.05, 7.20, 12.28, 4.37, 4.40, "Mat_Arch_Sol_Parquet", uv_scale=0.5)
    int_arch.add_box_bounds(5.05, 10.78, -4.78, 12.28, 4.37, 4.40, "Mat_Arch_Sol_Parquet", uv_scale=0.5)

    # -------------------------------------------------------------------------
    # 2. INTERIOR PARTITIONS (0.16 m thick, spacious 1.4m–1.5m doorways)
    # -------------------------------------------------------------------------
    # RDC Right Wing Spine Wall X = 5.08
    int_arch.add_wall_with_openings(
        "Y", 5.08, 0.16, -4.78, 12.28, 0.35, 4.15, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -1.80, "s1": -0.35, "z0": 0.35, "z1": 2.95, "kind": "door"},
            {"s0": 7.40, "s1": 8.90, "z0": 0.35, "z1": 2.95, "kind": "door"},
        ],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # RDC Guest Room vs Guest Bath (Y = 2.60)
    int_arch.add_wall_with_openings(
        "X", 2.60, 0.16, 5.16, 10.78, 0.35, 4.15, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 6.20, "s1": 7.55, "z0": 0.35, "z1": 2.90, "kind": "door"}],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # RDC Guest Bath vs African Kitchen (Y = 5.50)
    int_arch.add_box_bounds(5.16, 10.78, 5.42, 5.58, 0.35, 4.15, "Mat_Arch_MurInt_Creme", collider=True)
    # RDC Euro Kitchen partial cabinet backing wall (Y = 5.50, X [2.50, 5.00])
    int_arch.add_box_bounds(2.50, 5.00, 5.42, 5.58, 0.35, 4.15, "Mat_Arch_MurInt_Creme", collider=True)
    # RDC WC Visiteurs (X [2.50, 5.00], Y [-4.78, -2.05])
    int_arch.add_box_bounds(2.42, 2.58, -4.78, -2.00, 0.35, 4.15, "Mat_Arch_MurInt_Creme", collider=True)
    int_arch.add_wall_with_openings(
        "X", -2.08, 0.16, 2.58, 5.00, 0.35, 4.15, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 3.00, "s1": 4.30, "z0": 0.35, "z1": 2.90, "kind": "door"}],
        frame_mat="Mat_Arch_Bois_Interieur",
    )

    # R+1 Master Suite South Wall (Y = +2.60, X [-10.78, -1.80])
    int_arch.add_box_bounds(-10.78, -1.80, 2.52, 2.68, 4.40, 8.00, "Mat_Arch_MurInt_Creme", collider=True)
    # R+1 Master Suite East Wall (X = -1.80, Y [2.60, 12.28])
    int_arch.add_wall_with_openings(
        "Y", -1.80, 0.16, 2.60, 12.28, 4.40, 8.00, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": 3.50, "s1": 5.00, "z0": 4.40, "z1": 7.00, "kind": "door"}],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # R+1 Master Bath Partition
    int_arch.add_box_bounds(-6.28, -6.12, 7.60, 12.28, 4.40, 8.00, "Mat_Arch_MurInt_Creme", collider=True)
    int_arch.add_wall_with_openings(
        "X", 7.60, 0.16, -10.78, -6.20, 4.40, 8.00, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -8.10, "s1": -6.75, "z0": 4.40, "z1": 6.95, "kind": "door"}],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # R+1 Study / Gym South Wall (Y = 7.20, X [-1.72, 5.00])
    int_arch.add_wall_with_openings(
        "X", 7.20, 0.16, -1.72, 5.00, 4.40, 8.00, "Mat_Arch_MurInt_Creme",
        openings=[{"s0": -0.60, "s1": 0.90, "z0": 4.40, "z1": 7.00, "kind": "door"}],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # R+1 Right Wing Spine Wall (X = 5.08)
    int_arch.add_wall_with_openings(
        "Y", 5.08, 0.16, -4.78, 12.28, 4.40, 8.00, "Mat_Arch_MurInt_Creme",
        openings=[
            {"s0": -2.80, "s1": -1.30, "z0": 4.40, "z1": 7.00, "kind": "door"},
            {"s0": 5.55, "s1": 7.05, "z0": 4.40, "z1": 7.00, "kind": "door"},
        ],
        frame_mat="Mat_Arch_Bois_Interieur",
    )
    # R+1 Bed 2 vs Bed 3 wall (Y = 3.60)
    int_arch.add_box_bounds(5.16, 10.78, 3.52, 3.68, 4.40, 8.00, "Mat_Arch_MurInt_Creme", collider=True)

    # Interior Accent Feature Wall Panels (Salon TV wall & Master headboard wall)
    int_arch.add_box_bounds(-10.77, -10.72, -2.50, 2.50, 0.35, 7.95, "Mat_Arch_MurInt_Accent")
    int_arch.add_box_bounds(-6.00, -2.20, 2.69, 2.74, 4.40, 7.95, "Mat_Arch_MurInt_Accent")

    # -------------------------------------------------------------------------
    # 3. GRAND WALKABLE U-TURN STAIRCASE (1.85 m wide flights, 30.05 deg slope)
    # -------------------------------------------------------------------------
    # Flight 1: X [0.20, 2.05], Y [0.20 -> 3.70], Z [0.35 -> 2.375] (11 steps, rise 0.184m, run 0.318m)
    int_arch.add_stair_flight(
        "Y", 1.125, 1.82, 0.20, 3.70, 0.35, 2.375, 11, "Mat_Arch_Bois_Interieur", "Mat_Arch_Metal_Anthracite"
    )
    # Mid Landing: X [0.20, 4.40], Y [3.70, 5.40], Z = 2.18..2.375
    int_arch.add_box_bounds(0.20, 4.40, 3.70, 5.40, 2.18, 2.35, "Mat_Arch_Metal_Anthracite", collider=True)
    int_arch.add_box_bounds(0.20, 4.40, 3.70, 5.40, 2.35, 2.375, "Mat_Arch_Sol_Marbre")
    int_arch.add_box_bounds(0.25, 0.40, 5.20, 5.35, 0.35, 2.18, "Mat_Arch_Metal_Anthracite", collider=True)
    int_arch.add_box_bounds(4.20, 4.35, 5.20, 5.35, 0.35, 2.18, "Mat_Arch_Metal_Anthracite", collider=True)
    # Flight 2: X [2.55, 4.40], Y [3.70 -> 0.20], Z [2.375 -> 4.40]
    int_arch.add_stair_flight(
        "Y", 3.475, 1.82, 3.70, 0.20, 2.375, 4.40, 11, "Mat_Arch_Bois_Interieur", "Mat_Arch_Metal_Anthracite"
    )
    # Center stair handrail between Flight 1 & Flight 2 (X = 2.30)
    int_arch.add_box_bounds(2.26, 2.34, 0.20, 0.28, 0.35, 1.35, "Mat_Arch_Metal_Inox")
    int_arch.add_box_bounds(2.26, 2.34, 3.66, 3.74, 2.375, 3.40, "Mat_Arch_Metal_Inox")
    int_arch.add_box_bounds(2.26, 2.34, 0.20, 0.28, 4.40, 5.42, "Mat_Arch_Metal_Inox")
    int_arch.add_beam((2.30, 0.24, 1.32), (2.30, 3.70, 3.36), 0.06, 0.06, "Mat_Arch_Metal_Inox")
    int_arch.add_beam((2.30, 3.70, 3.36), (2.30, 0.24, 5.38), 0.06, 0.06, "Mat_Arch_Metal_Inox")

    # R+1 Mezzanine & Stairwell Glass Guardrails (full perimeter protection)
    int_arch.add_glass_railing("Y", -1.76, -4.75, 2.55, 4.40)
    int_arch.add_glass_railing("Y", 0.16, 0.20, 5.40, 4.40)
    int_arch.add_glass_railing("X", 5.40, 0.16, 4.45, 4.40)
    int_arch.add_glass_railing("X", 0.20, 0.16, 2.30, 4.40)

    # -------------------------------------------------------------------------
    # 4. GRAND MODERN FURNITURE & LIGHTING
    # -------------------------------------------------------------------------
    # Living Room L-sofa + coffee table + area rug + media unit
    int_furn.add_box_bounds(-9.20, -4.20, -2.80, 2.20, 0.35, 0.37, "Mat_Arch_Tissu_Mobilier")
    int_furn.add_box_bounds(-8.40, -7.25, -2.50, 2.00, 0.35, 0.78, "Mat_Arch_Tissu_Mobilier", collider=True)
    int_furn.add_box_bounds(-8.40, -4.20, 1.00, 2.10, 0.35, 0.78, "Mat_Arch_Tissu_Mobilier", collider=True)
    int_furn.add_box_bounds(-8.40, -8.12, -2.50, 2.10, 0.78, 1.12, "Mat_Arch_Tissu_Mobilier")
    int_furn.add_box_bounds(-8.40, -4.20, 1.82, 2.10, 0.78, 1.12, "Mat_Arch_Tissu_Mobilier")
    int_furn.add_box_bounds(-6.30, -4.70, -1.60, -0.20, 0.35, 0.75, "Mat_Arch_Bois_Interieur", collider=True)
    int_furn.add_box_bounds(-10.70, -10.15, -1.80, 1.80, 0.35, 0.85, "Mat_Arch_Bois_Interieur", collider=True)
    int_furn.add_box_bounds(-10.70, -10.62, -1.40, 1.40, 1.20, 2.65, "Mat_Arch_Metal_Anthracite")  # Wide wall TV

    # Dining Room 8-seater table + chairs (X [-8.0, -5.2], Y [6.6, 9.8])
    int_furn.add_box_bounds(-7.60, -5.20, 6.80, 9.80, 1.08, 1.16, "Mat_Arch_Bois_Interieur", collider=True)
    int_furn.add_box_bounds(-7.35, -5.45, 7.05, 9.55, 0.35, 1.08, "Mat_Arch_Metal_Anthracite")
    for cy in (7.15, 8.30, 9.45):
        int_furn.add_box_bounds(-8.20, -7.72, cy - 0.24, cy + 0.24, 0.35, 0.84, "Mat_Arch_Tissu_Mobilier")
        int_furn.add_box_bounds(-5.08, -4.60, cy - 0.24, cy + 0.24, 0.35, 0.84, "Mat_Arch_Tissu_Mobilier")

    # European Kitchen: Large Central Island (X [-0.20, +2.80], Y [7.20, 8.35]) + Rear Counters + Double Fridge
    int_furn.add_box_bounds(-0.20, 2.80, 7.20, 8.35, 0.35, 1.24, "Mat_Arch_Bois_Interieur", collider=True)
    int_furn.add_box_bounds(-0.26, 2.86, 7.14, 8.41, 1.24, 1.31, "Mat_Arch_Plan_Travail")
    int_furn.add_box_bounds(-0.60, 4.95, 11.45, 12.26, 0.35, 1.24, "Mat_Arch_Bois_Interieur", collider=True)
    int_furn.add_box_bounds(-0.60, 4.95, 11.42, 12.26, 1.24, 1.31, "Mat_Arch_Plan_Travail")
    int_furn.add_box_bounds(3.85, 4.95, 10.20, 11.45, 0.35, 2.55, "Mat_Arch_Metal_Inox", collider=True)

    # African Kitchen counters
    int_furn.add_box_bounds(5.20, 6.00, 10.20, 12.26, 0.35, 1.26, "Mat_Arch_Plan_Travail", collider=True)
    int_furn.add_box_bounds(9.80, 10.75, 6.00, 12.26, 0.35, 1.26, "Mat_Arch_Plan_Travail", collider=True)

    def add_bed(cx, cy, z_floor, w=2.00, length=2.20, head_dir="+Y"):
        int_furn.add_box_center(cx, cy, z_floor + 0.24, w, length, 0.48, "Mat_Arch_Bois_Interieur", collider=True)
        int_furn.add_box_center(cx, cy, z_floor + 0.54, w - 0.10, length - 0.12, 0.20, "Mat_Arch_Tissu_Mobilier")
        if head_dir == "+Y":
            int_furn.add_box_center(cx, cy + length * 0.5 - 0.06, z_floor + 0.72, w + 0.14, 0.14, 1.35, "Mat_Arch_Bois_Interieur")
        elif head_dir == "+X":
            int_furn.add_box_center(cx + w * 0.5 - 0.06, cy, z_floor + 0.72, 0.14, length + 0.14, 1.35, "Mat_Arch_Bois_Interieur")
        elif head_dir == "-Y":
            int_furn.add_box_center(cx, cy - length * 0.5 + 0.06, z_floor + 0.72, w + 0.14, 0.14, 1.35, "Mat_Arch_Bois_Interieur")

    add_bed(8.85, -1.80, 0.35, w=1.90, length=2.15, head_dir="+X")   # Guest Room RDC
    add_bed(-4.10, 4.20, 4.40, w=2.20, length=2.25, head_dir="-Y")   # Master King Bed against accent wall
    add_bed(8.90, -0.50, 4.40, w=1.90, length=2.15, head_dir="+X")   # Bed 2
    add_bed(8.90, 9.20, 4.40, w=1.90, length=2.15, head_dir="+X")    # Bed 3
    int_furn.add_box_bounds(0.65, 3.25, 11.20, 12.15, 4.40, 5.20, "Mat_Arch_Bois_Interieur", collider=True)  # Study Desk

    # Vanities
    for x0, x1, y0, y1, z0 in [
        (3.45, 4.88, -4.68, -4.08, 0.35),
        (8.60, 10.65, 4.65, 5.32, 0.35),
        (-10.68, -8.40, 11.55, 12.22, 4.40),
    ]:
        int_furn.add_box_bounds(x0, x1, y0, y1, z0, z0 + 0.88, "Mat_Arch_Bois_Interieur", collider=True)
        int_furn.add_box_bounds(x0 + 0.08, x1 - 0.08, y0 + 0.06, y1 - 0.06, z0 + 0.88, z0 + 0.99, "Mat_Arch_Ceramique_SDB")

    # Warm ceiling LED panels (IvoryState/NightLight)
    for lx, ly, lz in [
        (-6.2, -0.5, 7.98), (-6.2, 8.2, 4.13), (1.8, 8.8, 4.13), (7.9, -1.2, 4.13),
        (-0.8, -2.2, 7.98), (-4.2, 6.2, 7.98), (7.9, -0.5, 7.98), (7.9, 8.2, 7.98),
    ]:
        int_arch.add_box_bounds(lx - 0.45, lx + 0.45, ly - 0.45, ly + 0.45, lz - 0.02, lz, "Mat_Arch_Lumiere_Int_Nuit")

    # -------------------------------------------------------------------------
    # 5. SEPARATE INTERIOR DOORS (Open ~85 deg)
    # -------------------------------------------------------------------------
    int_door_specs = [
        ("Door_Int_Guest", (5.08, -1.76, 0.35), (-0.03, 0.03, 0.0, 1.38, 0.0, 2.58), (0, 0, -85)),
        ("Door_Int_GuestBath", (6.24, 2.60, 0.35), (0.0, 1.28, -0.03, 0.03, 0.0, 2.52), (0, 0, 85)),
        ("Door_Int_WC", (3.04, -2.08, 0.35), (0.0, 1.22, -0.03, 0.03, 0.0, 2.52), (0, 0, -85)),
        ("Door_Int_AfricanKitchen", (5.08, 7.44, 0.35), (-0.03, 0.03, 0.0, 1.42, 0.0, 2.58), (0, 0, -85)),
        ("Door_Int_MasterSuite", (-1.80, 3.54, 4.40), (-0.03, 0.03, 0.0, 1.42, 0.0, 2.58), (0, 0, 85)),
        ("Door_Int_MasterBath", (-8.06, 7.60, 4.40), (0.0, 1.28, -0.03, 0.03, 0.0, 2.52), (0, 0, 85)),
        ("Door_Int_Study", (-0.56, 7.20, 4.40), (0.0, 1.42, -0.03, 0.03, 0.0, 2.58), (0, 0, 85)),
        ("Door_Int_Bed2", (5.08, -2.76, 4.40), (-0.03, 0.03, 0.0, 1.42, 0.0, 2.58), (0, 0, -85)),
        ("Door_Int_Bed3", (5.08, 5.59, 4.40), (-0.03, 0.03, 0.0, 1.42, 0.0, 2.58), (0, 0, -85)),
    ]
    for dname, dpiv, dbounds, drot in int_door_specs:
        db, dm, dr = add_door_leaf_builder(
            dname,
            col_name,
            pivot_world=dpiv,
            leaf_bounds_local=dbounds,
            mat_panel="Mat_Arch_Bois_Interieur",
            default_rot_deg=drot,
            open_note="Interior hinged door swung 85 deg open into room",
        )
        door_objs.append(db.build_object(default_rot_deg=dr))
        door_metas.append(dm)

    root = make_empty_root("Duplex_Cocody_01_Int", collection_name=col_name)
    obj_arch = int_arch.build_object(parent=root)
    obj_furn = int_furn.build_object(parent=root)
    for dobj in door_objs:
        dobj.parent = root

    return root, [root, obj_arch, obj_furn] + door_objs, int_arch.colliders + int_furn.colliders, door_metas


def main():
    reset_scene()
    ext_root, ext_objects, ext_cols, ext_doors = build_exterior()
    int_root, int_objects, int_cols, int_doors = build_interior()

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    export_objects_fbx(ext_objects, EXT_FBX_PATH)
    export_objects_fbx(int_objects, INT_FBX_PATH)

    manifest = {
        "name": "Duplex_Cocody_01",
        "lot_meters": [30.0, 36.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Maisons/SM_Duplex_Cocody_01.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Duplex_Cocody_01_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors,
        "interior_doors": int_doors,
    }
    save_collider_manifest(MANIFEST_PATH, manifest)

    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1 :]
        if args:
            preview_dir = args[0]
            render_preview_views(
                preview_dir,
                "duplex_cocody_v2",
                [
                    ("front_3q", (-25.0, -32.0, 14.0), (0.0, 0.0, 3.8)),
                    ("int_stairs", (-6.0, -3.5, 2.6), (2.8, 3.5, 2.4)),
                ],
            )

    total_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in ext_objects + int_objects
        if o.type == "MESH"
    )
    print(f"[SUMMARY] Duplex v2 total tris={total_tris}, ext_colliders={len(ext_cols)}, int_colliders={len(int_cols)}")


if __name__ == "__main__":
    main()
