"""
Build and export Building #4: Stade Félix Houphouët-Boigny ("Le Félicia", Plateau, Abidjan).

Architectural faithfulness (post-CAN 2023 Mota-Engil renovation):
- Footprint: 160.0 m (X [-80, +80]) x 200.0 m (Y [-100, +100]), total canopy height ~27.5 m.
- FIFA Pitch: 105.0 m x 68.0 m with touchlines, center circle, penalty boxes & goal posts.
- Athletics track oval surrounding pitch (walkable).
- Full oval stepped bowl seating in Ivorian colors (Orange / White / Green), VIP glass box,
  and walkable N/S/E/W access tunnels directly from perimeter plaza to track & pitch.
- Modern saucer exterior cladding + full-coverage cantilevered steel roof with LED floodlight ring.
- Exports:
  1. IvoryState/Assets/_Project/Art/Models/Architecture/Monuments/SM_Stade_FHB.fbx
  2. tools/blender/architecture/Stade_FHB.blend
  3. tools/blender/architecture/stade_fhb_manifest.json
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
    "Monuments",
    "SM_Stade_FHB.fbx",
)
BLEND_PATH = os.path.join(SCRIPT_DIR, "Stade_FHB.blend")
MANIFEST_PATH = os.path.join(SCRIPT_DIR, "stade_fhb_manifest.json")


def make_empty_root(name, collection_name):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    col = bpy.data.collections.get(collection_name)
    if col is None:
        col = bpy.data.collections.new(collection_name)
        bpy.context.scene.collection.children.link(col)
    col.objects.link(empty)
    empty.location = (0.0, 0.0, 0.0)
    empty.scale = (1.0, 1.0, 1.0)
    return empty


def oval_ring_points(rx, ry, z, segments=36):
    """Generate CCW oval/stadium-shaped points at elevation z."""
    pts = []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        ca, sa = math.cos(a), math.sin(a)
        # Superellipse exponent ~1.7 gives realistic rounded-rectangle stadium bowl silhouette
        sx = math.copysign(abs(ca) ** 0.82, ca) * rx
        sy = math.copysign(abs(sa) ** 0.82, sa) * ry
        pts.append((sx, sy, z))
    return pts


def build_stade():
    """Build 160m x 200m x 27.5m Stade Félix Houphouët-Boigny."""
    col_name = "Stade_FHB"
    stade_field = ArchMeshBuilder("Stade_Field_Bowl", collection_name=col_name)
    stade_roof = ArchMeshBuilder("Stade_Facade_Roof", collection_name=col_name)

    # -------------------------------------------------------------------------
    # 1. PERIMETER PLAZA SLAB, SUBMERGED LAGOON QUAY & NW CAUSEWAY TO AVE AOUSSOU KOFFI
    # -------------------------------------------------------------------------
    # Submerged concrete marine embankment / quay wall down below LagoonDepth (-2.4 m)
    stade_field.add_box_bounds(-80.25, 80.25, -100.25, 100.25, -2.65, 0.26, "Mat_Arch_Beton_Brut", uv_scale=0.15, collider=True)

    # Full esplanade pad (160m x 200m, Z = 0.00..0.30)
    stade_field.add_box_bounds(-80.0, 80.0, -100.0, 100.0, 0.0, 0.30, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)

    # NW Access Causeway Plaza (world X in [232.0, 260.0], Z in [107.6, 120.0] for pos=(340,20), ToStreet=(0,0,1))
    # Bridges sidewalk[712] (Y=0.15) seamlessly onto the stadium esplanade (Y=0.30) via two 7.5 cm steps.
    stade_field.add_box_bounds(82.0, 108.0, -100.0, -87.6, -0.80, 0.15, "Mat_Arch_Beton_Brut", uv_scale=0.2, collider=True)
    stade_field.add_box_bounds(82.0, 108.0, -100.0, -87.6, 0.12, 0.15, "Mat_Arch_Paves_Cour", uv_scale=0.25)
    stade_field.add_box_bounds(80.0, 82.0, -100.0, -87.6, -0.80, 0.225, "Mat_Arch_Paves_Cour", uv_scale=0.25, collider=True)
    for bx in (84.0, 91.5, 99.0, 106.5):
        for by in (-99.65, -87.95):
            stade_field.add_box_bounds(bx - 0.18, bx + 0.18, by - 0.18, by + 0.18, 0.15, 0.95, "Mat_Arch_Metal_Inox", collider=True)

    # Athletics Track Oval (rx=47m, ry=76m)
    track_b = oval_ring_points(48.0, 76.0, 0.30, 36)
    track_t = oval_ring_points(48.0, 76.0, 0.36, 36)
    stade_field.add_loft_section(track_b, track_t, "Mat_Arch_Piste_Athletisme", uv_scale=0.35)

    # FIFA Pitch (68.0 m x 105.0 m -> X [-34, +34], Y [-52.5, +52.5], slightly raised Z = 0.36..0.40)
    stade_field.add_box_bounds(-37.0, 37.0, -56.0, 56.0, 0.36, 0.40, "Mat_Arch_Pelouse_Stade", uv_scale=0.2, collider=True)
    # Crisp white pitch markings
    line_z0, line_z1 = 0.40, 0.415
    stade_field.add_box_bounds(-34.0, 34.0, -52.5, -52.32, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
    stade_field.add_box_bounds(-34.0, 34.0, 52.32, 52.5, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
    stade_field.add_box_bounds(-34.0, -33.82, -52.5, 52.5, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
    stade_field.add_box_bounds(33.82, 34.0, -52.5, 52.5, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
    stade_field.add_box_bounds(-34.0, 34.0, -0.09, 0.09, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")  # Halfway line
    # Center circle (12-segment ring)
    for i in range(16):
        a0 = 2.0 * math.pi * i / 16.0
        a1 = 2.0 * math.pi * (i + 1) / 16.0
        stade_field.add_beam(
            (math.cos(a0) * 9.15, math.sin(a0) * 9.15, 0.408),
            (math.cos(a1) * 9.15, math.sin(a1) * 9.15, 0.408),
            0.16, 0.02, "Mat_Arch_MurExt_Blanc",
        )
    # Penalty boxes + 2 Goal Frames (7.32m wide x 2.44m tall)
    for gy_sign in (-1, 1):
        py_edge = gy_sign * 52.5
        py_box = gy_sign * (52.5 - 16.5)
        stade_field.add_box_bounds(-20.16, 20.16, min(py_box, py_box) - 0.09, max(py_box, py_box) + 0.09, line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
        stade_field.add_box_bounds(-20.16, -19.98, min(py_edge, py_box), max(py_edge, py_box), line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
        stade_field.add_box_bounds(19.98, 20.16, min(py_edge, py_box), max(py_edge, py_box), line_z0, line_z1, "Mat_Arch_MurExt_Blanc")
        # Goal frame
        stade_field.add_box_bounds(-3.72, -3.60, py_edge - 0.06, py_edge + 0.06, 0.40, 2.84, "Mat_Arch_Metal_Inox", collider=True)
        stade_field.add_box_bounds(3.60, 3.72, py_edge - 0.06, py_edge + 0.06, 0.40, 2.84, "Mat_Arch_Metal_Inox", collider=True)
        stade_field.add_box_bounds(-3.72, 3.72, py_edge - 0.06, py_edge + 0.06, 2.74, 2.86, "Mat_Arch_Metal_Inox")

    # -------------------------------------------------------------------------
    # 2. BOWL STANDS, ACCESS TUNNELS & EXTERIOR SAUCER FACADE
    # -------------------------------------------------------------------------
    # Build 36 bowl sectors, leaving 4 walkable ground-level entry portals at N/S/E/W
    # Portal indices: 0 (+X east), 9 (+Y north), 18 (-X west), 27 (-Y south)
    segments = 36
    portal_indices = {0, 9, 18, 27}
    r_in_x, r_in_y = 48.5, 76.5
    r_mid_x, r_mid_y = 60.0, 84.5
    r_out_x, r_out_y = 75.5, 95.5
    r_ext_x, r_ext_y = 78.5, 98.0

    def pt_at(idx, rx, ry):
        a = 2.0 * math.pi * (idx % segments) / segments
        ca, sa = math.cos(a), math.sin(a)
        return (
            math.copysign(abs(ca) ** 0.82, ca) * rx,
            math.copysign(abs(sa) ** 0.82, sa) * ry,
        )

    for i in range(segments):
        p0_in = pt_at(i, r_in_x, r_in_y)
        p1_in = pt_at(i + 1, r_in_x, r_in_y)
        p0_mid = pt_at(i, r_mid_x, r_mid_y)
        p1_mid = pt_at(i + 1, r_mid_x, r_mid_y)
        p0_out = pt_at(i, r_out_x, r_out_y)
        p1_out = pt_at(i + 1, r_out_x, r_out_y)
        p0_ext = pt_at(i, r_ext_x, r_ext_y)
        p1_ext = pt_at(i + 1, r_ext_x, r_ext_y)

        is_portal = i in portal_indices
        # Ivorian flag tricolor seating pattern across sections (Orange, White, Green)
        seat_mat = (
            "Mat_Arch_Siege_Orange" if (i % 6) in (0, 1)
            else ("Mat_Arch_Siege_Blanc" if (i % 6) in (2, 3) else "Mat_Arch_Siege_Vert")
        )

        # Lower tier seats (starts above Z=4.2m if portal so players walk freely underneath!)
        z_low_base = 4.5 if is_portal else 1.2
        if not is_portal:
            # Front perimeter pitch-side wall (1.2 m high)
            stade_field.add_loft_section(
                [(p0_in[0], p0_in[1], 0.30), (p1_in[0], p1_in[1], 0.30), (p1_in[0]*1.01, p1_in[1]*1.01, 0.30), (p0_in[0]*1.01, p0_in[1]*1.01, 0.30)],
                [(p0_in[0], p0_in[1], 1.25), (p1_in[0], p1_in[1], 1.25), (p1_in[0]*1.01, p1_in[1]*1.01, 1.25), (p0_in[0]*1.01, p0_in[1]*1.01, 1.25)],
                "Mat_Arch_MurExt_Blanc",
            )
            # Lower tier sloped stands (Z = 1.25 -> 8.5 m)
            stade_field.add_loft_section(
                [(p0_in[0], p0_in[1], 1.25), (p1_in[0], p1_in[1], 1.25), (p1_mid[0], p1_mid[1], 8.50), (p0_mid[0], p0_mid[1], 8.50)],
                [(p0_in[0], p0_in[1], 0.30), (p1_in[0], p1_in[1], 0.30), (p1_mid[0], p1_mid[1], 0.30), (p0_mid[0], p0_mid[1], 0.30)],
                seat_mat,
            )
        else:
            # Tunnel ceiling soffit at Z = 4.6 m so player walks straight through portal into stadium!
            stade_field.add_loft_section(
                [(p0_in[0], p0_in[1], 4.60), (p1_in[0], p1_in[1], 4.60), (p1_mid[0], p1_mid[1], 8.50), (p0_mid[0], p0_mid[1], 8.50)],
                [(p0_in[0], p0_in[1], 4.20), (p1_in[0], p1_in[1], 4.20), (p1_mid[0], p1_mid[1], 4.20), (p0_mid[0], p0_mid[1], 4.20)],
                seat_mat,
            )

        # Upper tier stands (Z = 8.50 -> 19.2 m)
        stade_field.add_loft_section(
            [(p0_mid[0], p0_mid[1], 8.50), (p1_mid[0], p1_mid[1], 8.50), (p1_out[0], p1_out[1], 19.20), (p0_out[0], p0_out[1], 19.20)],
            [(p0_mid[0], p0_mid[1], 6.50), (p1_mid[0], p1_mid[1], 6.50), (p1_out[0], p1_out[1], 6.50), (p0_out[0], p0_out[1], 6.50)],
            seat_mat,
        )

        # Exterior modern saucer facade wall (Z = 0.30 or 4.60 -> 21.8 m)
        z_ext_bot = 4.60 if is_portal else 0.30
        stade_roof.add_loft_section(
            [(p0_out[0], p0_out[1], z_ext_bot), (p1_out[0], p1_out[1], z_ext_bot), (p1_ext[0], p1_ext[1], z_ext_bot), (p0_ext[0], p0_ext[1], z_ext_bot)],
            [(p0_out[0], p0_out[1], 21.80), (p1_out[0], p1_out[1], 21.80), (p1_ext[0], p1_ext[1], 22.80), (p0_ext[0], p0_ext[1], 22.80)],
            "Mat_Arch_MurExt_Blanc",
        )
        # Exterior vertical structural rib every segment
        stade_roof.add_beam(
            (p0_ext[0] * 1.005, p0_ext[1] * 1.005, 0.30),
            (p0_ext[0] * 1.005, p0_ext[1] * 1.005, 23.20),
            0.65, 0.85, "Mat_Arch_Metal_Anthracite",
        )

        # Perimeter BoxCollider segment (skipped at ground level on portal sectors!)
        if not is_portal:
            mx = 0.5 * (p0_out[0] + p1_out[0])
            my = 0.5 * (p0_out[1] + p1_out[1])
            seg_len = math.hypot(p1_out[0] - p0_out[0], p1_out[1] - p0_out[1])
            ang = math.atan2(p1_out[1] - p0_out[1], p1_out[0] - p0_out[0])
            stade_field.add_collider_center(mx, my, 10.0, seg_len, 4.0, 20.0, rot_euler=(0.0, 0.0, ang))
            # Inner pitch-side bowl wall collider
            ix = 0.5 * (p0_in[0] + p1_in[0])
            iy = 0.5 * (p0_in[1] + p1_in[1])
            i_len = math.hypot(p1_in[0] - p0_in[0], p1_in[1] - p0_in[1])
            stade_field.add_collider_center(ix, iy, 2.2, i_len, 2.0, 4.0, rot_euler=(0.0, 0.0, ang))

    # VIP / Presidential Glass Pavilion on West Stand (X [-76, -63], Y [-28, +28], Z = 13.5..21.5)
    stade_roof.add_box_bounds(-76.0, -64.0, -28.0, 28.0, 13.5, 21.2, "Mat_Arch_Vitre_GratteCiel")
    stade_roof.add_box_bounds(-76.4, -63.6, -28.4, 28.4, 20.8, 21.8, "Mat_Arch_Metal_Anthracite")

    # -------------------------------------------------------------------------
    # 3. FULL-COVERAGE CANTILEVERED CANOPY ROOF (Z = 22.8..27.5 m) & FLOODLIGHT RING
    # -------------------------------------------------------------------------
    roof_outer_b = oval_ring_points(80.0, 99.5, 22.8, segments)
    roof_outer_t = oval_ring_points(80.0, 99.5, 24.4, segments)
    roof_inner_b = oval_ring_points(46.0, 74.0, 26.2, segments)
    roof_inner_t = oval_ring_points(46.0, 74.0, 27.5, segments)

    for i in range(segments):
        j = (i + 1) % segments
        # Canopy top & soffit sloped ring
        stade_roof.add_loft_section(
            [roof_outer_b[i], roof_outer_b[j], roof_inner_b[j], roof_inner_b[i]],
            [roof_outer_t[i], roof_outer_t[j], roof_inner_t[j], roof_inner_t[i]],
            "Mat_Arch_MurExt_Blanc", uv_scale=0.15,
        )
        # Radial steel truss beam supporting canopy
        stade_roof.add_beam(roof_outer_b[i], roof_inner_b[i], 0.55, 0.85, "Mat_Arch_Metal_Anthracite")
        # Inner ring floodlight strip (NightLight)
        stade_roof.add_beam(
            (roof_inner_b[i][0], roof_inner_b[i][1], 25.8),
            (roof_inner_b[j][0], roof_inner_b[j][1], 25.8),
            0.40, 0.45, "Mat_Arch_Lumiere_Nuit",
        )

    root = make_empty_root("Stade_FHB", collection_name=col_name)
    o_field = stade_field.build_object(parent=root)
    o_roof = stade_roof.build_object(parent=root)
    return root, [root, o_field, o_roof], stade_field.colliders + stade_roof.colliders


def main():
    reset_scene()
    root, objects, colliders = build_stade()

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    export_objects_fbx(objects, EXT_FBX_PATH)

    manifest = {
        "name": "Stade_FHB",
        "lot_meters": [160.0, 200.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Stade_FHB.fbx",
        "interior_fbx": "",
        "exterior_colliders": colliders,
        "interior_colliders": [],
        "exterior_doors": [],
        "interior_doors": [],
    }
    save_collider_manifest(MANIFEST_PATH, manifest)

    total_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in objects
        if o.type == "MESH"
    )
    print(f"[SUMMARY] Stade FHB total tris={total_tris}, colliders={len(colliders)}")


if __name__ == "__main__":
    main()
