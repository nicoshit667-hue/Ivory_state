"""Build vehicle #16: Classic Burgundy Utility Motorcycle (Honda CG 125 / Moto-taxi) for Ivory State.

Includes sculpted burgundy teardrop tank & side covers, air-cooled finned 125cc engine,
chrome exhaust silencer, tubular rear luggage rack, spoked chrome wheels, steerable front fork
& handlebars (Guidon), round headlight & twin gauges, LOD0 + LOD1 FBX, and 4-view studio sheet.
"""

import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector, Matrix

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vehicle_utils as vu


FRONT_Y =  0.65
REAR_Y  = -0.62
WHEEL_R =  0.315


def build_teardrop_tank(bm_paint, bm_chrome):
    """Build sculpted burgundy teardrop fuel tank (+X half -> mirrored, or full symmetric) + chrome cap."""
    # Sections along Y from rear (-0.04) to front (+0.44): (y, hw, z_bot, z_mid, z_top)
    specs = [
        (-0.04, 0.082, 0.725, 0.770, 0.810),
        ( 0.04, 0.112, 0.720, 0.790, 0.865),
        ( 0.15, 0.140, 0.725, 0.818, 0.918),
        ( 0.27, 0.150, 0.738, 0.832, 0.935),
        ( 0.38, 0.132, 0.760, 0.838, 0.916),
        ( 0.44, 0.078, 0.795, 0.842, 0.882),
    ]
    rings = []
    for y, hw, zb, zm, zt in specs:
        pts = [
            (0.0,        zb),
            (hw * 0.75,  zb + 0.012),
            (hw,         zm),
            (hw * 0.85,  zt - 0.025),
            (hw * 0.45,  zt - 0.005),
            (0.0,        zt),
        ]
        # Mirror symmetrically left & right for closed ring
        full_pts = [(0.0, zb)] + [(x, z) for (x, z) in pts[1:-1]] + [(0.0, zt)] + [(-x, z) for (x, z) in reversed(pts[1:-1])]
        rings.append([bm_paint.verts.new((x, y, z)) for (x, z) in full_pts])

    n_ring = len(rings[0])
    for i in range(len(rings) - 1):
        r0, r1 = rings[i], rings[i + 1]
        for k in range(n_ring):
            nk = (k + 1) % n_ring
            bm_paint.faces.new([r0[k], r1[k], r1[nk], r0[nk]])
    # Cap front & rear ends
    c_rear = bm_paint.verts.new((0.0, specs[0][0] - 0.015, specs[0][3]))
    c_front = bm_paint.verts.new((0.0, specs[-1][0] + 0.018, specs[-1][3]))
    for k in range(n_ring):
        nk = (k + 1) % n_ring
        bm_paint.faces.new([rings[0][nk], rings[0][k], c_rear])
        bm_paint.faces.new([rings[-1][k], rings[-1][nk], c_front])

    # Chrome fuel filler cap on top of tank
    vu.add_cylinder_bm(bm_chrome, (0.0, 0.25, 0.922), (0.0, 0.25, 0.946), 0.036, segments=14)
    vu.add_cylinder_bm(bm_chrome, (0.0, 0.25, 0.944), (0.0, 0.25, 0.953), 0.026, segments=12)

    # Matching burgundy triangular side battery covers below seat (left & right)
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_paint, (0.118 * sx, -0.18, 0.625), (0.048, 0.29, 0.175), (0.18, 0.0, 0.0), 0.016, 2)


def add_arc_fender_bm(bm, center_yz, radius, half_width, ang_start_deg, ang_end_deg, steps=12, thick=0.008):
    """Build a smoothly curved motorcycle fender arching around a wheel center (Y, Z)."""
    cy, cz = center_yz
    rings = []
    for i in range(steps + 1):
        ang = math.radians(ang_start_deg + (ang_end_deg - ang_start_deg) * (i / steps))
        uy, uz = math.cos(ang), math.sin(ang)
        # Curved cross-section across X (cupping around tire crown!)
        prof = [
            (-half_width,        radius - 0.022),
            (-half_width * 0.65, radius - 0.006),
            ( 0.0,               radius),
            ( half_width * 0.65, radius - 0.006),
            ( half_width,        radius - 0.022),
            ( half_width * 0.92, radius - 0.022 - thick),
            ( 0.0,               radius - thick),
            (-half_width * 0.92, radius - 0.022 - thick),
        ]
        rings.append([bm.verts.new((px, cy + uy * r, cz + uz * r)) for (px, r) in prof])
    n_p = len(rings[0])
    for i in range(steps):
        for k in range(n_p):
            nk = (k + 1) % n_p
            bm.faces.new([rings[i][k], rings[i + 1][k], rings[i + 1][nk], rings[i][nk]])
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def build_frame_engine_seat_exhaust(bm_frame, bm_engine, bm_seat, bm_chrome, bm_tail, bm_amber):
    """Build steel frame, finned 125cc engine, chrome exhaust, bench seat, rear rack & shocks."""
    # 1. Steel cradle frame + rear swingarm
    vu.add_tube_path_bm(bm_frame, [(0.0, 0.44, 0.87), (0.0, 0.05, 0.74), (0.0, -0.58, 0.74)], 0.017, segments=8)
    vu.add_tube_path_bm(bm_frame, [(0.0, 0.44, 0.84), (0.0, 0.36, 0.44), (0.0, -0.08, 0.36)], 0.015, segments=8)
    vu.add_cylinder_bm(bm_frame, (0.0, 0.46, 0.93), (0.0, 0.41, 0.78), 0.024, segments=10)
    for sx in (-1.0, 1.0):
        # Rear seat subframe & diagonal struts
        vu.add_tube_path_bm(bm_frame, [(0.09 * sx, -0.05, 0.73), (0.105 * sx, -0.62, 0.74)], 0.012, segments=6)
        vu.add_tube_path_bm(bm_frame, [(0.095 * sx, -0.06, 0.40), (0.105 * sx, -0.50, 0.73)], 0.011, segments=6)
        # Rear swingarm fork
        vu.add_tube_path_bm(bm_frame, [(0.085 * sx, -0.10, 0.36), (0.095 * sx, REAR_Y, WHEEL_R)], 0.015, segments=8)
        # Rear twin chrome shock absorbers + coil spring rings
        s0 = Vector((0.120 * sx, -0.54, 0.72))
        s1 = Vector((0.118 * sx, REAR_Y + 0.02, WHEEL_R + 0.04))
        vu.add_cylinder_bm(bm_chrome, s0, s1, 0.011, segments=8)
        for k in range(8):
            f = 0.18 + 0.64 * (k / 7.0)
            p = s0.lerp(s1, f)
            p2 = s0.lerp(s1, f + 0.045)
            vu.add_cylinder_bm(bm_chrome, p, p2, 0.022, segments=10)
        # Rider & passenger footpegs
        vu.add_cylinder_bm(bm_frame, (0.08 * sx, 0.08, 0.33), (0.24 * sx, 0.08, 0.33), 0.013, segments=8)
        vu.add_cylinder_bm(bm_frame, (0.10 * sx, -0.34, 0.39), (0.23 * sx, -0.34, 0.39), 0.012, segments=8)

    # Chain guard on left side (-X)
    vu.add_box_bm(bm_frame, (-0.112, -0.34, 0.34), (0.032, 0.56, 0.075), (0.04, 0, 0), 0.01)

    # 2. Air-cooled 125cc single-cylinder engine (crankcase + finned cylinder + head + carb)
    vu.add_box_bm(bm_engine, (0.0, 0.06, 0.41), (0.21, 0.28, 0.16), bevel_width=0.025, bevel_segs=2)
    vu.add_cylinder_bm(bm_engine, (-0.115, 0.10, 0.40), (0.115, 0.10, 0.40), 0.075, segments=14)
    # Vertical cylinder core + 8 crisp cooling fins!
    vu.add_cylinder_bm(bm_engine, (0.0, 0.15, 0.48), (0.0, 0.18, 0.68), 0.056, segments=12)
    for k in range(8):
        fz = 0.50 + k * 0.022
        fy = 0.155 + k * 0.0035
        vu.add_box_bm(bm_engine, (0.0, fy, fz), (0.155, 0.145, 0.007), (-0.14, 0, 0), 0.002)
    # Cylinder head cover & carburetor behind cylinder
    vu.add_box_bm(bm_engine, (0.0, 0.185, 0.695), (0.135, 0.125, 0.048), (-0.14, 0, 0), 0.012)
    vu.add_cylinder_bm(bm_chrome, (0.0, 0.02, 0.60), (0.0, 0.12, 0.63), 0.028, segments=10)

    # 3. Chrome Exhaust Header & Long Tapered Silencer (RIGHT side +X!)
    header_pts = [
        (0.035, 0.25, 0.62), (0.095, 0.33, 0.52), (0.115, 0.28, 0.34),
        (0.130, 0.10, 0.27), (0.142, -0.18, 0.28),
    ]
    vu.add_tube_path_bm(bm_chrome, header_pts, 0.019, segments=10)
    # Tapered chrome muffler silencer from y=-0.16 to y=-0.78
    muff_p0, muff_p1 = Vector((0.148, -0.16, 0.285)), Vector((0.158, -0.78, 0.355))
    diff = muff_p1 - muff_p0
    rot = Vector((0, 0, 1)).rotation_difference(diff.normalized()).to_matrix().to_4x4()
    mat_m = Matrix.Translation((muff_p0 + muff_p1) * 0.5) @ rot
    bmesh.ops.create_cone(
        bm_chrome, cap_ends=True, cap_tris=False, segments=14,
        radius1=0.024, radius2=0.046, depth=diff.length, matrix=mat_m,
    )

    # 4. Flat Quilted Black Two-Seater Bench Seat
    vu.add_box_bm(bm_seat, (0.0, -0.34, 0.785), (0.235, 0.64, 0.115), (-0.03, 0, 0), bevel_width=0.036, bevel_segs=2)
    # Subtle stitched passenger step ridge
    vu.add_box_bm(bm_seat, (0.0, -0.50, 0.808), (0.228, 0.28, 0.090), (0.0, 0, 0), bevel_width=0.030, bevel_segs=2)

    # 5. Signature Tubular Chrome Rear Cargo Rack!
    rx, r_t = 0.155, 0.0085
    rack_loop = [(-rx, -0.54, 0.835), (rx, -0.54, 0.835), (rx, -0.94, 0.845), (-rx, -0.94, 0.845)]
    vu.add_tube_path_bm(bm_chrome, rack_loop, r_t, segments=8, closed=True)
    for ry in (-0.64, -0.74, -0.84):
        vu.add_cylinder_bm(bm_chrome, (-rx, ry, 0.840), (rx, ry, 0.840), r_t * 0.8, segments=6)
    for sx in (-1.0, 1.0):
        # Side grab rail & diagonal rack support struts
        vu.add_tube_path_bm(bm_chrome, [(0.12 * sx, -0.42, 0.74), (rx * sx, -0.56, 0.835)], r_t, segments=6)
        vu.add_tube_path_bm(bm_chrome, [(0.115 * sx, -0.58, 0.73), (rx * sx, -0.82, 0.842)], r_t, segments=6)

    # 6. Black Rear Mudguard, Taillight & Rear Turn Signals
    add_arc_fender_bm(bm_frame, (REAR_Y, WHEEL_R), WHEEL_R + 0.055, 0.072, 95.0, 178.0, steps=10)
    vu.add_box_bm(bm_frame, (0.0, -0.94, 0.66), (0.095, 0.06, 0.14), (0.35, 0, 0), 0.008)
    vu.add_box_bm(bm_tail,  (0.0, -0.92, 0.745), (0.095, 0.055, 0.065), bevel_width=0.012)
    for sx in (-1.0, 1.0):
        vu.add_cylinder_bm(bm_frame, (0.06 * sx, -0.84, 0.75), (0.145 * sx, -0.84, 0.75), 0.008, segments=6)
        vu.add_cylinder_bm(bm_amber, (0.155 * sx, -0.82, 0.75), (0.155 * sx, -0.87, 0.75), 0.022, segments=10)


def build_steerable_front_assembly(col, root, mats):
    """Build steerable Guidon (forks, handlebars, round headlight, twin gauges, mirrors, chrome fender, Roue_F)."""
    # Pivot at steering head (0, 0.44, 0.87)
    pivot = Vector((0.0, 0.44, 0.87))
    guidon = bpy.data.objects.new('Guidon', None)
    guidon.empty_display_type = 'PLAIN_AXES'
    vu.link_to_collection(guidon, col)
    guidon.parent = root
    guidon.location = pivot

    def loc(p):
        return Vector(p) - pivot

    bm_chrome, bm_trim, bm_head, bm_amber = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    # Twin telescopic front forks (upper chrome stanchions + lower silver/black sliders)
    for sx in (-1.0, 1.0):
        p_top = loc((0.088 * sx, 0.45, 0.94))
        p_mid = loc((0.088 * sx, 0.56, 0.58))
        p_bot = loc((0.088 * sx, FRONT_Y, WHEEL_R))
        vu.add_cylinder_bm(bm_chrome, p_top, p_mid, 0.016, segments=10)
        vu.add_cylinder_bm(bm_trim,   p_mid, p_bot, 0.021, segments=10)
        # Triple-clamp fork bridges
        vu.add_box_bm(bm_trim, loc((0.0, 0.46, 0.92)), (0.21, 0.065, 0.022), (0.42, 0, 0), 0.005)
        vu.add_box_bm(bm_trim, loc((0.0, 0.50, 0.78)), (0.21, 0.065, 0.024), (0.42, 0, 0), 0.005)

    # Raised tubular chrome handlebars + black rubber grips + levers + round mirrors
    hbar_pts = [
        loc((-0.36, 0.36, 1.03)), loc((-0.18, 0.42, 1.03)),
        loc((-0.07, 0.45, 0.95)), loc(( 0.07, 0.45, 0.95)),
        loc(( 0.18, 0.42, 1.03)), loc(( 0.36, 0.36, 1.03)),
    ]
    vu.add_tube_path_bm(bm_chrome, hbar_pts, 0.011, segments=8)
    for sx in (-1.0, 1.0):
        vu.add_cylinder_bm(bm_trim, loc((0.23 * sx, 0.403, 1.03)), loc((0.37 * sx, 0.356, 1.03)), 0.0165, segments=10)
        # Round black rear-view mirror on chrome stalk
        vu.add_tube_path_bm(bm_chrome, [loc((0.21 * sx, 0.41, 1.04)), loc((0.26 * sx, 0.42, 1.18))], 0.005, segments=6)
        vu.add_cylinder_bm(bm_trim, loc((0.27 * sx, 0.43, 1.20)), loc((0.27 * sx, 0.40, 1.20)), 0.052, segments=14)
        vu.add_cylinder_bm(bm_chrome, loc((0.27 * sx, 0.402, 1.20)), loc((0.27 * sx, 0.396, 1.20)), 0.045, segments=14)
        # Front amber turn signals flanking headlight
        vu.add_cylinder_bm(bm_chrome, loc((0.08 * sx, 0.53, 0.85)), loc((0.16 * sx, 0.53, 0.85)), 0.007, segments=6)
        vu.add_cylinder_bm(bm_amber,  loc((0.165 * sx, 0.51, 0.85)), loc((0.165 * sx, 0.56, 0.85)), 0.022, segments=10)

    # Classic Round Chrome Headlight Bucket + Lens + Twin Round Speedo/Tacho Gauges
    vu.add_cylinder_bm(bm_trim,   loc((0.0, 0.50, 0.86)), loc((0.0, 0.60, 0.86)), 0.078, segments=16)
    vu.add_cylinder_bm(bm_chrome, loc((0.0, 0.59, 0.86)), loc((0.0, 0.625, 0.86)), 0.084, segments=16)
    vu.add_cylinder_bm(bm_head,   loc((0.0, 0.615, 0.86)), loc((0.0, 0.635, 0.86)), 0.076, segments=16)
    # Twin round gauges above headlight
    for sx in (-1.0, 1.0):
        vu.add_cylinder_bm(bm_trim,   loc((0.046 * sx, 0.51, 0.96)), loc((0.046 * sx, 0.48, 1.015)), 0.036, segments=12)
        vu.add_cylinder_bm(bm_chrome, loc((0.046 * sx, 0.482, 1.012)), loc((0.046 * sx, 0.478, 1.020)), 0.032, segments=12)

    # Curved Chrome Front Fender hugging front wheel!
    add_arc_fender_bm(bm_chrome, (FRONT_Y - pivot.y, WHEEL_R - pivot.z), WHEEL_R + 0.035, 0.064, 48.0, 138.0, steps=12)

    vu.create_mesh_object('Guidon_Chrome', bm_chrome, mats['chrome'], col, parent=guidon, angle_deg=42.0)
    vu.create_mesh_object('Guidon_Plastiques', bm_trim, mats['trim'], col, parent=guidon, angle_deg=42.0)
    vu.create_mesh_object('Phares', bm_head, mats['headlight'], col, parent=guidon, angle_deg=40.0)
    vu.create_mesh_object('Clignotants_Avant', bm_amber, mats['amber'], col, parent=guidon, angle_deg=40.0)

    # Front Spoked Wheel parented to Guidon so steering rotates it!
    fw_mesh = vu.build_spoked_moto_wheel_mesh(
        'Mesh_Roue_F', radius=WHEEL_R, width=0.086, rim_ratio=0.73,
        segments=22, spokes=14, mats=[mats['tyre'], mats['chrome'], mats['brake']],
    )
    fw_obj = bpy.data.objects.new('Roue_F', fw_mesh)
    vu.link_to_collection(fw_obj, col)
    fw_obj.parent = guidon
    fw_obj.location = loc((0.0, FRONT_Y, WHEEL_R))


def build_moto_utilitaire():
    vu.clear_scene()
    col = vu.ensure_collection('VH_Moto_Utilitaire')
    # Deep metallic burgundy wine-red matching reference CG 125!
    mats = vu.create_standard_vehicle_materials(paint_rgba=(0.24, 0.015, 0.020, 1.0))
    mat_engine = vu.make_pbr_material('M_Moteur_Fonte', base_color=(0.06, 0.062, 0.068, 1.0), metallic=0.65, roughness=0.45)
    mat_seat = vu.make_pbr_material('M_Selle_Cuir', base_color=(0.025, 0.025, 0.028, 1.0), metallic=0.0, roughness=0.68)

    root = bpy.data.objects.new('VH_Moto_Utilitaire', None)
    root.empty_display_type = 'PLAIN_AXES'
    vu.link_to_collection(root, col)

    bm_paint, bm_frame, bm_engine = bmesh.new(), bmesh.new(), bmesh.new()
    bm_seat, bm_chrome, bm_tail, bm_amber = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    build_teardrop_tank(bm_paint, bm_chrome)
    build_frame_engine_seat_exhaust(bm_frame, bm_engine, bm_seat, bm_chrome, bm_tail, bm_amber)

    # Apply light Subsurf on sculpted burgundy tank & side covers
    tank_obj = vu.create_mesh_object('Carrosserie', bm_paint, mats['paint'], col, parent=root, angle_deg=None)
    bpy.context.view_layer.objects.active = tank_obj
    tank_obj.select_set(True)
    mod_s = tank_obj.modifiers.new('Subsurf', 'SUBSURF')
    mod_s.levels = 1
    mod_s.render_levels = 1
    bpy.ops.object.modifier_apply(modifier='Subsurf')
    vu.apply_smooth_with_sharp_angle(tank_obj, angle_deg=48.0)

    vu.create_mesh_object('Cadre', bm_frame, mats['trim'], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Moteur', bm_engine, mat_engine, col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Selle', bm_seat, mat_seat, col, parent=root, angle_deg=45.0)
    vu.create_mesh_object('Chrome', bm_chrome, mats['chrome'], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Feux', bm_tail, mats['taillight'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Clignotants', bm_amber, mats['amber'], col, parent=root, angle_deg=40.0)

    # Steerable front assembly + Roue_F
    build_steerable_front_assembly(col, root, mats)

    # Rear Spoked Wheel (slightly wider rear tire)
    rw_mesh = vu.build_spoked_moto_wheel_mesh(
        'Mesh_Roue_R', radius=WHEEL_R, width=0.098, rim_ratio=0.72,
        segments=22, spokes=14, mats=[mats['tyre'], mats['chrome'], mats['brake']],
    )
    rw_obj = bpy.data.objects.new('Roue_R', rw_mesh)
    vu.link_to_collection(rw_obj, col)
    rw_obj.parent = root
    rw_obj.location = (0.0, REAR_Y, WHEEL_R)

    return root


def main():
    repo_root = os.path.abspath(os.path.join(SCRIPT_DIR, '..', '..', '..'))
    out_dir = os.path.join(repo_root, 'tools', 'blender', 'output', 'moto_utilitaire')
    root = build_moto_utilitaire()
    vu.finalize_and_export_vehicle(root, out_dir, 'moto_utilitaire', 'VH_Moto_Utilitaire', lod1_ratio=0.45, samples=16, res=(768, 432))


if __name__ == '__main__':
    main()
