"""Build vehicle #15: Urban City Bus (Blue & White SOTRA-style low-floor bus) for Ivory State.

Includes rounded low-floor shell with wheel-arch cutouts, blue/sky-blue/white livery,
panoramic windows, double right-side glass passenger doors, roof AC unit with 3 fans,
gooseneck bus mirrors, heavy commercial wheels, light passenger interior, LOD0 + LOD1 FBX.
"""

import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vehicle_utils as vu


Y_MIN = -5.70
Y_MAX =  5.70
MAX_HW = 1.24
ROOF_Z = 2.94
WHEEL_R = 0.50
WHEEL_W = 0.29
WHEEL_X = 1.06
FRONT_Y = 2.85
REAR_Y = -2.95
ARCH_R = 0.565
ROCKER_Z = 0.28


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def smoothstep(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def bus_hw(y, z):
    """Low-floor city bus half-width with subtle roof bevel and front/rear corner rounding."""
    hw = MAX_HW
    # Gentle roof edge rounding near top
    if z > 2.65:
        t = clamp((z - 2.65) / (ROOF_Z - 2.65), 0.0, 1.0)
        hw -= 0.075 * (t ** 2.2)
    # Subtle lower skirt tuck
    elif z < 0.48:
        t = clamp((0.48 - z) / (0.48 - ROCKER_Z), 0.0, 1.0)
        hw -= 0.025 * (t ** 1.6)
    # Plan-view corner rounding at front and rear ends
    if y > 5.32:
        t = clamp((y - 5.32) / (Y_MAX - 5.32), 0.0, 1.0)
        hw -= 0.14 * (t ** 2.4)
    elif y < -5.36:
        t = clamp((-5.36 - y) / (-5.36 - Y_MIN), 0.0, 1.0)
        hw -= 0.11 * (t ** 2.4)
    return hw


def build_bus_stations():
    stations = []
    sin_low = clamp((ROCKER_Z - WHEEL_R) / ARCH_R, -0.95, 0.0)
    th_low = math.asin(sin_low)
    th_hi = math.pi - th_low

    def arch_pts(wy, tag, n_steps=8):
        pts = []
        for i in range(n_steps + 1):
            th = th_hi + (th_low - th_hi) * (i / n_steps)
            ay = wy + ARCH_R * math.cos(th)
            az = max(ROCKER_Z, WHEEL_R + ARCH_R * math.sin(th))
            pts.append((ay, az, tag))
        return pts

    rear_arch = arch_pts(REAR_Y, 'rear', 8)
    for y in (-5.70, -5.54, -5.32, -4.60, -3.85):
        if y < rear_arch[0][0] - 0.05:
            stations.append((y, ROCKER_Z, None))
    stations.extend(rear_arch)

    front_arch = arch_pts(FRONT_Y, 'front', 8)
    for y in (-1.95, -0.95, 0.0, 0.95, 1.95):
        if rear_arch[-1][0] + 0.05 < y < front_arch[0][0] - 0.05:
            stations.append((y, ROCKER_Z, None))
    stations.extend(front_arch)

    for y in (3.85, 4.65, 5.32, 5.54, 5.70):
        if y > front_arch[-1][0] + 0.05:
            stations.append((y, ROCKER_Z, None))
    return stations


def build_bus_body(bm_white, bm_blue, bm_sky, bm_dark):
    """Build main city bus shell (+X half, mirrored later) with blue/sky/white livery zones."""
    stations = build_bus_stations()
    n_st = len(stations)
    # 6 vertical stations: 0=bottom/arch, 1=lower skirt top (0.72), 2=beltline (1.22), 3=mid glass (2.0), 4=upper eave (2.66), 5=roof (2.94)
    z_levels = [0.70, 1.22, 2.02, 2.66, ROOF_Z]

    side_grid = []
    for y, zb, tag in stations:
        col = [bm_white.verts.new((bus_hw(y, zb), y, zb))]
        for zl in z_levels:
            z = max(zb + 0.04, zl)
            x = bus_hw(y, z)
            if zl == ROOF_Z:
                x -= 0.03
            col.append(bm_white.verts.new((x, y, z)))
        side_grid.append(col)

    n_v = len(z_levels) + 1
    for i in range(n_st - 1):
        for j in range(n_v - 1):
            bm_white.faces.new([
                side_grid[i][j], side_grid[i + 1][j],
                side_grid[i + 1][j + 1], side_grid[i][j + 1],
            ])

    # Roof bridge across X
    lat_u = [0.75, 0.38, 0.0]
    top_rows = []
    for i, (y, zb, tag) in enumerate(stations):
        vo = side_grid[i][-1]
        row = [vo]
        for u in lat_u:
            x = vo.co.x * u
            crown = 0.045 * (1.0 - u * u)
            y_bow = y
            if y > 5.35:
                y_bow = y + 0.055 * smoothstep(5.35, Y_MAX, y) * (1.0 - u * u)
            elif y < -5.38:
                y_bow = y - 0.035 * smoothstep(-5.38, Y_MIN, y) * (1.0 - u * u)
            row.append(bm_white.verts.new((x, y_bow, vo.co.z + crown)))
        top_rows.append(row)
    for r in range(len(top_rows) - 1):
        for c in range(len(top_rows[0]) - 1):
            bm_white.faces.new([top_rows[r][c], top_rows[r + 1][c], top_rows[r + 1][c + 1], top_rows[r][c + 1]])

    # Front & rear flat-rounded faces
    for is_front in (True, False):
        col0 = side_grid[-1] if is_front else side_grid[0]
        trow = top_rows[-1] if is_front else top_rows[0]
        cols = [col0]
        for ci, u in enumerate(lat_u):
            col = []
            vt = trow[ci + 1]
            for j in range(n_v - 1):
                vs = col0[j]
                x = vs.co.x * u
                bow = (0.055 if is_front else -0.035) * (1.0 - u * u)
                # Slight forward aerodynamic rake at top of front windshield
                rake = -0.09 * smoothstep(1.2, ROOF_Z, vs.co.z) if is_front else 0.04 * smoothstep(1.2, ROOF_Z, vs.co.z)
                col.append(bm_white.verts.new((x, vs.co.y + bow + rake, vs.co.z)))
            col.append(vt)
            cols.append(col)
        for c in range(len(cols) - 1):
            for j in range(n_v - 1):
                if is_front:
                    bm_white.faces.new([cols[c][j], cols[c + 1][j], cols[c + 1][j + 1], cols[c][j + 1]])
                else:
                    bm_white.faces.new([cols[c][j], cols[c][j + 1], cols[c + 1][j + 1], cols[c + 1][j]])

    # Dark recessed wheel wells (both sides!)
    for tag, wy in (('rear', REAR_Y), ('front', FRONT_Y)):
        arch_idxs = [i for i, s in enumerate(stations) if s[2] == tag]
        for sx in (1.0, -1.0):
            o_ring, i_ring = [], []
            ix = 0.72 * sx
            for idx in arch_idxs:
                co = side_grid[idx][0].co
                o_ring.append(bm_dark.verts.new(((co.x - 0.025) * sx, co.y, co.z - 0.002)))
                i_ring.append(bm_dark.verts.new((ix, co.y, co.z + 0.02)))
            for k in range(len(o_ring) - 1):
                verts = [o_ring[k], i_ring[k], i_ring[k + 1], o_ring[k + 1]] if sx > 0 else [o_ring[k], o_ring[k + 1], i_ring[k + 1], i_ring[k]]
                bm_dark.faces.new(verts)
            cv = bm_dark.verts.new((ix, wy, ROCKER_Z))
            for k in range(len(i_ring) - 1):
                verts = [i_ring[k], cv, i_ring[k + 1]] if sx > 0 else [i_ring[k], i_ring[k + 1], cv]
                bm_dark.faces.new(verts)

    # Dark underbody chassis tray
    vu.add_box_bm(bm_dark, (0.0, 0.0, ROCKER_Z + 0.03), (2.20, 10.90, 0.06))

    # -------------------------------------------------------------------------
    # SIGNATURE ROYAL-BLUE & SKY-BLUE LIVERY PANELS (clean flush overlay!)
    # -------------------------------------------------------------------------
    def add_side_livery_quad(bm, y0, y1, z0_a, z1_a, z0_b, z1_b, sx, off=0.009):
        """Add curved livery panel along bus side between y0 and y1."""
        steps = 6
        cols = []
        for s in range(steps + 1):
            t = s / steps
            y = y0 + (y1 - y0) * t
            zb = z0_a + (z0_b - z0_a) * t
            zt = z1_a + (z1_b - z1_a) * t
            # Respect wheel arch bottom cutout!
            for wy in (FRONT_Y, REAR_Y):
                dy = abs(y - wy)
                if dy < ARCH_R:
                    za = WHEEL_R + math.sqrt(max(0.0, ARCH_R * ARCH_R - dy * dy))
                    zb = max(zb, za + 0.008)
            if zt <= zb + 0.01:
                zt = zb + 0.015
            col = []
            for f in (0.0, 0.5, 1.0):
                z = zb + (zt - zb) * f
                x = (bus_hw(y, z) + off) * sx
                col.append(bm.verts.new((x, y, z)))
            cols.append(col)
        for i in range(steps):
            for j in range(2):
                verts = [cols[i][j], cols[i + 1][j], cols[i + 1][j + 1], cols[i][j + 1]] if sx > 0 else [cols[i][j], cols[i][j + 1], cols[i + 1][j + 1], cols[i + 1][j]]
                bm.faces.new(verts)

    for sx in (1.0, -1.0):
        # Lower continuous royal-blue skirt along entire bus (z=0.28..0.64)
        add_side_livery_quad(bm_blue, -5.58, -3.52, 0.28, 1.24, 0.28, 1.24, sx, off=0.008)
        # Blue swoop curving down from rear quarter over rear wheel arch to mid skirt
        add_side_livery_quad(bm_blue, -3.52, -2.15, 0.28, 1.24, 0.28, 0.64, sx, off=0.008)
        add_side_livery_quad(bm_blue, -2.15,  5.58, 0.28, 0.64, 0.28, 0.64, sx, off=0.008)
        # Upper rear royal-blue header above rear windows
        add_side_livery_quad(bm_blue, -5.58, -3.25, 2.48, 2.91, 2.48, 2.91, sx, off=0.008)
        # Light sky-blue diagonal accent stripe sweeping across rear upper & mid corner!
        add_side_livery_quad(bm_sky,  -3.25, -2.75, 2.48, 2.91, 2.48, 2.91, sx, off=0.009)
        add_side_livery_quad(bm_sky,  -2.45, -1.65, 0.64, 1.05, 0.64, 0.64, sx, off=0.009)

    # Lower front blue bumper skirt & rear blue bumper skirt
    vu.add_box_bm(bm_blue, (0.0,  5.66, 0.44), (2.28, 0.14, 0.34), bevel_width=0.025)
    vu.add_box_bm(bm_blue, (0.0, -5.66, 0.48), (2.32, 0.14, 0.40), bevel_width=0.025)
    # Blue roof end caps (matching top view of reference!)
    vu.add_box_bm(bm_blue, (0.0, -4.45, ROOF_Z + 0.036), (2.16, 2.30, 0.024), bevel_width=0.01)
    vu.add_box_bm(bm_sky,  (0.0, -3.05, ROOF_Z + 0.036), (2.16, 0.52, 0.024), bevel_width=0.01)


def build_bus_windows_doors_and_details(
    bm_white, bm_glass, bm_trim, bm_dark, bm_chrome, bm_head, bm_tail, bm_amber, bm_pole
):
    """Build panoramic windows, right-side double doors, roof AC unit, mirrors, lights & interior."""
    # 1. Giant continuous dark side window frames & glass (both sides, z=1.24..2.48)
    for sx in (1.0, -1.0):
        # Black continuous window band surround
        vu.add_box_bm(bm_trim,  (1.244 * sx, -0.05, 1.86), (0.022, 10.65, 1.26), bevel_width=0.006)
        vu.add_box_bm(bm_glass, (1.250 * sx, -0.05, 1.86), (0.014, 10.55, 1.18))
        # Crisp black vertical window mullions (subdividing into 7 realistic bus window panes)
        for my in (-4.40, -3.10, -1.80, -0.75, 0.65, 1.95, 3.30, 4.45):
            vu.add_box_bm(bm_trim, (1.256 * sx, my, 1.86), (0.018, 0.065, 1.24))

    # 2. Two Double Full-Height Glass Passenger Doors on RIGHT side (+X)
    # Front door ahead of front wheel (+4.02..+5.12), Middle door (-0.70..+0.60)
    for dy0, dy1 in ((4.00, 5.10), (-0.70, 0.60)):
        ym = 0.5 * (dy0 + dy1)
        w = dy1 - dy0
        # Full-height matte black outer door surround frame (z = 0.32..2.48)
        vu.add_box_bm(bm_trim,  (1.248, ym, 1.40), (0.028, w + 0.08, 2.16), bevel_width=0.006)
        # Double glass leaf panels
        vu.add_box_bm(bm_glass, (1.256, ym - w * 0.24, 1.40), (0.016, w * 0.43, 2.04))
        vu.add_box_bm(bm_glass, (1.256, ym + w * 0.24, 1.40), (0.016, w * 0.43, 2.04))
        # Center vertical rubber meeting strip between door leaves
        vu.add_box_bm(bm_trim,  (1.262, ym, 1.40), (0.018, 0.048, 2.10))
        # Subtle yellow interior handrail bar visible right inside door glass
        vu.add_cylinder_bm(bm_pole, (1.16, ym - w * 0.22, 0.55), (1.16, ym - w * 0.22, 2.15), 0.018, segments=8)
        vu.add_cylinder_bm(bm_pole, (1.16, ym + w * 0.22, 0.55), (1.16, ym + w * 0.22, 2.15), 0.018, segments=8)

    # 3. Front Panoramic Windshield, LED Destination Display & Wipers
    vu.add_box_bm(bm_trim,  (0.0, 5.665, 1.88), (2.28, 0.04, 1.88), (-0.032, 0, 0), 0.008)
    vu.add_box_bm(bm_glass, (0.0, 5.680, 1.72), (2.16, 0.025, 1.44), (-0.032, 0, 0))
    # Dark LED destination header screen above windshield
    vu.add_box_bm(bm_dark,  (0.0, 5.655, 2.62), (1.76, 0.035, 0.30), (-0.032, 0, 0), 0.006)
    # Two large bus windshield wipers
    for wx in (-0.48, 0.44):
        vu.add_box_bm(bm_trim, (wx, 5.715, 1.08), (0.86, 0.024, 0.022), (0, 0, 0.06), 0.004)

    # 4. Rear Window, Engine Louver Grilles & Rear Lights
    vu.add_box_bm(bm_trim,  (0.0, -5.680, 2.08), (2.16, 0.04, 0.88), bevel_width=0.008)
    vu.add_box_bm(bm_glass, (0.0, -5.695, 2.08), (2.04, 0.025, 0.78))
    # Rear engine cooling louvered vent panels (rear + right side!)
    for vx in (-0.72, 0.72):
        vu.add_box_bm(bm_dark, (vx, -5.705, 1.28), (0.48, 0.025, 0.36), bevel_width=0.005)
        for vz in range(6):
            vu.add_box_bm(bm_trim, (vx, -5.715, 1.13 + vz * 0.056), (0.44, 0.015, 0.016))
    # Side rear engine intake grille (right & left rear corner below window)
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_dark, (1.252 * sx, -4.95, 0.80), (0.020, 0.86, 0.68), bevel_width=0.005)
        for vz in range(8):
            vu.add_box_bm(bm_trim, (1.260 * sx, -4.95, 0.50 + vz * 0.082), (0.014, 0.82, 0.020))

    # 5. Roof HVAC Air-Conditioning Pod (with 3 circular fans!) + 2 Roof Escape Hatches
    vu.add_box_bm(bm_white, (0.0,  0.15, 3.09), (1.72, 3.35, 0.28), bevel_width=0.06, bevel_segs=2)
    # Recessed dark HVAC fan shroud on rear half of AC pod
    vu.add_box_bm(bm_trim,  (0.0, -0.45, 3.21), (1.42, 0.72, 0.06), bevel_width=0.02)
    for fx in (-0.44, 0.0, 0.44):
        vu.add_cylinder_bm(bm_dark, (fx, -0.45, 3.22), (fx, -0.45, 3.255), 0.18, segments=14)
        vu.add_cylinder_bm(bm_trim, (fx, -0.45, 3.23), (fx, -0.45, 3.262), 0.06, segments=10)
    # Two clean roof escape hatches (front & rear of AC unit)
    for hy in (-3.85, 3.65):
        vu.add_box_bm(bm_white, (0.0, hy, 2.985), (0.86, 0.76, 0.055), bevel_width=0.015)

    # 6. Front Gooseneck Bus Mirrors (long overhead arm on right, side arm on left!)
    vu.add_tube_path_bm(bm_trim, [(1.14, 5.56, 2.52), (1.36, 5.95, 2.58), (1.46, 6.08, 2.42)], 0.018, segments=8)
    vu.add_box_bm(bm_trim,   (1.48, 6.08, 2.24), (0.14, 0.10, 0.34), bevel_width=0.015)
    vu.add_box_bm(bm_chrome, (1.48, 6.03, 2.24), (0.11, 0.014, 0.29), bevel_width=0.004)

    vu.add_tube_path_bm(bm_trim, [(-1.18, 5.56, 2.38), (-1.42, 5.72, 2.38), (-1.46, 5.76, 2.22)], 0.018, segments=8)
    vu.add_box_bm(bm_trim,   (-1.46, 5.76, 2.08), (0.13, 0.09, 0.32), bevel_width=0.015)
    vu.add_box_bm(bm_chrome, (-1.46, 5.71, 2.08), (0.10, 0.014, 0.27), bevel_width=0.004)

    # 7. Front Headlights, Rear Corner Vertical Taillights & Side Markers
    for sx in (1.0, -1.0):
        # Front recessed black headlight pod with dual round lamps
        vu.add_box_bm(bm_trim,  (0.84 * sx, 5.705, 0.66), (0.48, 0.055, 0.16), (0, 0, -0.08 * sx), 0.01)
        vu.add_cylinder_bm(bm_head,  (0.75 * sx, 5.715, 0.66), (0.75 * sx, 5.742, 0.66), 0.058, segments=12)
        vu.add_cylinder_bm(bm_head,  (0.92 * sx, 5.705, 0.66), (0.92 * sx, 5.734, 0.66), 0.048, segments=12)
        vu.add_cylinder_bm(bm_amber, (1.04 * sx, 5.695, 0.66), (1.04 * sx, 5.722, 0.66), 0.036, segments=10)
        # Rear vertical corner taillight clusters (red + amber + reverse)
        vu.add_box_bm(bm_trim,  (1.06 * sx, -5.695, 0.86), (0.20, 0.08, 0.56), (0, 0, 0.12 * sx), 0.01)
        vu.add_box_bm(bm_tail,  (1.07 * sx, -5.715, 0.72), (0.17, 0.06, 0.22), (0, 0, 0.12 * sx), 0.008)
        vu.add_box_bm(bm_amber, (1.07 * sx, -5.715, 0.94), (0.17, 0.06, 0.14), (0, 0, 0.12 * sx), 0.008)
        vu.add_box_bm(bm_tail,  (1.07 * sx, -5.715, 1.07), (0.17, 0.06, 0.09), (0, 0, 0.12 * sx), 0.006)
        # Upper rear roof corner red brake/marker lamps
        vu.add_cylinder_bm(bm_tail, (1.02 * sx, -5.68, 2.68), (1.02 * sx, -5.715, 2.68), 0.055, segments=10)
        # Amber side marker lights along skirt
        for my in (-4.20, -1.30, 1.50, 4.30):
            vu.add_box_bm(bm_amber, (1.255 * sx, my, 0.44), (0.016, 0.09, 0.036), bevel_width=0.004)


def build_bus_urbain():
    vu.clear_scene()
    col = vu.ensure_collection('VH_Bus_Urbain')
    mats = vu.create_standard_vehicle_materials(paint_rgba=(0.89, 0.90, 0.90, 1.0))
    mat_blue = vu.make_pbr_material('M_Bus_Bleu', base_color=(0.025, 0.22, 0.68, 1.0), metallic=0.06, roughness=0.22, coat_weight=0.9)
    mat_sky = vu.make_pbr_material('M_Bus_BleuCiel', base_color=(0.24, 0.56, 0.88, 1.0), metallic=0.05, roughness=0.24, coat_weight=0.9)
    mat_pole = vu.make_pbr_material('M_Bus_BarreJaune', base_color=(0.95, 0.65, 0.02, 1.0), metallic=0.35, roughness=0.30)

    root = bpy.data.objects.new('VH_Bus_Urbain', None)
    root.empty_display_type = 'PLAIN_AXES'
    vu.link_to_collection(root, col)

    bm_white, bm_blue, bm_sky = bmesh.new(), bmesh.new(), bmesh.new()
    bm_glass, bm_trim, bm_dark = bmesh.new(), bmesh.new(), bmesh.new()
    bm_chrome, bm_head, bm_tail, bm_amber, bm_pole = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    build_bus_body(bm_white, bm_blue, bm_sky, bm_dark)

    # Mirror white bus shell first
    body_obj = vu.create_mesh_object('Carrosserie', bm_white, mats['paint'], col, parent=root, angle_deg=None)
    mod_m = body_obj.modifiers.new('Mirror', 'MIRROR')
    mod_m.use_axis = (True, False, False)
    mod_m.use_clip = True
    mod_m.merge_threshold = 0.005
    bpy.context.view_layer.objects.active = body_obj
    body_obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier='Mirror')
    vu.apply_smooth_with_sharp_angle(body_obj, angle_deg=44.0)

    bm_roof_ac = bmesh.new()
    build_bus_windows_doors_and_details(
        bm_roof_ac, bm_glass, bm_trim, bm_dark, bm_chrome, bm_head, bm_tail, bm_amber, bm_pole
    )
    vu.create_mesh_object('Climatisation_Toit', bm_roof_ac, mats['paint'], col, parent=root, angle_deg=44.0)
    vu.create_mesh_object('Livree_Bleue', vu.merge_bms_with_mats([bm_blue, bm_sky]), [mat_blue, mat_sky], col, parent=root, angle_deg=44.0)
    vu.create_mesh_object('Vitres', bm_glass, mats['glass'], col, parent=root, angle_deg=45.0)
    vu.create_mesh_object('Plastiques', vu.merge_bms_with_mats([bm_trim, bm_dark]), [mats['trim'], mats['trim_dark']], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Chrome', bm_chrome, mats['chrome'], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Phares', bm_head, mats['headlight'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Feux', bm_tail, mats['taillight'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Clignotants', bm_amber, mats['amber'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Barres_Interieur', bm_pole, mat_pole, col, parent=root, angle_deg=40.0)

    # Light bus passenger interior silhouette (driver cab + 2 rows of seats visible through panoramic windows!)
    bm_int = bmesh.new()
    vu.add_box_bm(bm_int, (-0.52, 4.95, 1.15), (0.95, 0.55, 0.36), bevel_width=0.03)
    vu.add_box_bm(bm_int, (-0.58, 4.28, 1.38), (0.48, 0.16, 0.65), (-0.12, 0, 0), 0.02)
    for sy in (-4.40, -3.50, -2.50, -1.55, 1.35, 2.35, 3.25):
        for sx in (-0.78, 0.78):
            vu.add_box_bm(bm_int, (sx, sy, 1.34), (0.68, 0.14, 0.62), (-0.10, 0, 0), 0.02)
    vu.add_box_bm(bm_int, (0.0, -5.05, 1.48), (1.95, 0.18, 0.64), (-0.10, 0, 0), 0.02)
    vu.create_mesh_object('Interieur', bm_int, mats['trim_dark'], col, parent=root, angle_deg=45.0)

    # 4 Heavy Commercial Bus Wheels
    base_wheel = vu.build_heavy_wheel_mesh(
        'M_BusWheel', radius=WHEEL_R, width=WHEEL_W, rim_ratio=0.56,
        segments=20, mats=[mats['tyre'], mats['steel_rim'], mats['brake']],
    )
    for idx, (wname, pos, rz) in enumerate((
        ('Roue_FL', (-WHEEL_X, FRONT_Y, WHEEL_R), math.pi),
        ('Roue_FR', ( WHEEL_X, FRONT_Y, WHEEL_R), 0.0),
        ('Roue_RL', (-WHEEL_X, REAR_Y,  WHEEL_R), math.pi),
        ('Roue_RR', ( WHEEL_X, REAR_Y,  WHEEL_R), 0.0),
    )):
        wm = base_wheel if idx == 0 else base_wheel.copy()
        wm.name = f'Mesh_{wname}'
        wo = bpy.data.objects.new(wname, wm)
        vu.link_to_collection(wo, col)
        wo.parent = root
        wo.location = pos
        wo.rotation_euler = (0.0, 0.0, rz)

    return root


def main():
    repo_root = os.path.abspath(os.path.join(SCRIPT_DIR, '..', '..', '..'))
    out_dir = os.path.join(repo_root, 'tools', 'blender', 'output', 'bus_urbain')
    root = build_bus_urbain()
    vu.finalize_and_export_vehicle(root, out_dir, 'bus_urbain', 'VH_Bus_Urbain', lod1_ratio=0.40, samples=16, res=(768, 432))


if __name__ == '__main__':
    main()
