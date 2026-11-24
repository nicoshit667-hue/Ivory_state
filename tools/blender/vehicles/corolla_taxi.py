"""Build pilot vehicle #17: Classic Yellow Taxi (Toyota Corolla E90 style) for Ivory State.

Pass 2: Refined sleek E90 proportions, unified continuous A/roof/C-pillar frame,
surface-conforming wraparound lights, flush curved door seams, rich taxi yellow,
mobile-optimized (~7.5k tris), warning-free Unity FBX export.

Run with Blender 5.x headless:
    blender --background --python tools/blender/vehicles/corolla_taxi.py
"""

import json
import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

import vehicle_utils as vu


# =============================================================================
# Master Dimensions (meters, sleek Toyota Corolla E90 proportions)
# =============================================================================
BODY_Y_MIN = -2.05
BODY_Y_MAX =  2.03
MAX_HW = 0.835

WHEEL_R = 0.305
WHEEL_W = 0.185
WHEEL_X = 0.718
FRONT_Y = 1.24
REAR_Y = -1.20
ARCH_R = 0.346
ROCKER_Z = 0.215


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def smoothstep(edge0, edge1, x):
    t = clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def plan_half_width(y):
    """Smooth plan-view outer half-width along Y (sleek tapered nose & tail)."""
    hw = MAX_HW
    if y > 1.25:
        t = clamp((y - 1.25) / (BODY_Y_MAX - 1.25), 0.0, 1.0)
        hw -= 0.025 * (t ** 1.5) + 0.095 * (t ** 3.0)
    if y < -1.32:
        t = clamp((-1.32 - y) / (-1.32 - BODY_Y_MIN), 0.0, 1.0)
        hw -= 0.020 * (t ** 1.5) + 0.072 * (t ** 2.8)
    return hw


def belt_z(y):
    """Sleek Corolla E90 beltline, hood slope, and rear trunk deck height."""
    if y <= -1.34:
        t = clamp((y - BODY_Y_MIN) / (-1.34 - BODY_Y_MIN), 0.0, 1.0)
        return 0.798 + 0.044 * t
    if y <= 0.94:
        t = (y + 1.34) / (0.94 + 1.34)
        return 0.842 - 0.036 * t
    t = clamp((y - 0.94) / (BODY_Y_MAX - 0.94), 0.0, 1.0)
    return 0.806 - 0.148 * (t ** 1.30) - 0.034 * (t ** 2.8)


def hood_trunk_center_z(y):
    bz = belt_z(y)
    if y >= 0.94:
        t = clamp((y - 0.94) / (BODY_Y_MAX - 0.94), 0.0, 1.0)
        return bz + 0.030 * (1.0 - 0.55 * t)
    if y <= -1.34:
        t = clamp((-1.34 - y) / (-1.34 - BODY_Y_MIN), 0.0, 1.0)
        return bz + 0.020 * (1.0 - 0.45 * t)
    return bz + 0.018


def side_x_at(y, z, z_bot, z_top):
    """Sculpted automotive side profile (tumblehome, convex waist, subtle arch flare)."""
    base_hw = plan_half_width(y)
    h_full = clamp((z - ROCKER_Z) / max(0.05, (z_top - ROCKER_Z)), 0.0, 1.0)
    if h_full < 0.42:
        t = h_full / 0.42
        profile = -0.050 * ((1.0 - t) ** 1.8)
    else:
        t = (h_full - 0.42) / 0.58
        profile = -0.028 * (t ** 1.65)

    flare = 0.0
    for wy in (FRONT_Y, REAR_Y):
        d = math.hypot(y - wy, z - WHEEL_R)
        if ARCH_R - 0.01 <= d <= ARCH_R + 0.080:
            ft = 1.0 - clamp((d - ARCH_R) / 0.080, 0.0, 1.0)
            flare = max(flare, 0.014 * (ft ** 1.6))
    return base_hw + profile + flare


def build_side_stations():
    """Return sorted list of (y, z_bot, arch_tag) from rear to front."""
    stations = []
    sin_low = clamp((ROCKER_Z - WHEEL_R) / ARCH_R, -0.95, 0.0)
    theta_low = math.asin(sin_low)
    theta_hi = math.pi - theta_low

    def arch_pts(wy, tag, n_steps=8):
        pts = []
        for i in range(n_steps + 1):
            th = theta_hi + (theta_low - theta_hi) * (i / n_steps)
            ay = wy + ARCH_R * math.cos(th)
            az = max(ROCKER_Z, WHEEL_R + ARCH_R * math.sin(th))
            pts.append((ay, az, tag))
        return pts

    rear_arch = arch_pts(REAR_Y, 'rear', n_steps=8)
    # Rear overhang yellow metal stops cleanly inside rear bumper top (z >= 0.465 -> ZERO yellow bleed!)
    for y in (-2.05, -1.96, -1.82, -1.66):
        if y < rear_arch[0][0] - 0.03:
            zb = ROCKER_Z + 0.255 * smoothstep(-1.55, -1.78, y)
            stations.append((y, zb, None))
    stations.extend(rear_arch)

    front_arch = arch_pts(FRONT_Y, 'front', n_steps=8)
    for y in (-0.70, -0.40, -0.05, 0.38, 0.74, 0.88):
        if rear_arch[-1][0] + 0.025 < y < front_arch[0][0] - 0.025:
            stations.append((y, ROCKER_Z, None))
    stations.extend(front_arch)

    # Front overhang yellow metal stops cleanly inside front bumper top (z >= 0.450 -> ZERO yellow bleed!)
    for y in (1.70, 1.85, 1.96, 2.03):
        if y > front_arch[-1][0] + 0.03:
            zb = ROCKER_Z + 0.240 * smoothstep(1.59, 1.80, y)
            stations.append((y, zb, None))
    return stations


def build_lower_body_and_wells(bm_paint, bm_dark):
    stations = build_side_stations()
    n_st = len(stations)
    v_fracs = [0.0, 0.25, 0.54, 0.82, 1.0]
    n_v = len(v_fracs)

    side_grid = []
    for y, z_bot, tag in stations:
        zt = belt_z(y)
        col = []
        for frac in v_fracs:
            z = z_bot + (zt - z_bot) * frac
            x = side_x_at(y, z, z_bot, zt)
            if frac == 1.0:
                x -= 0.024
            col.append(bm_paint.verts.new((x, y, z)))
        side_grid.append(col)

    for i in range(n_st - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([
                side_grid[i][j], side_grid[i + 1][j],
                side_grid[i + 1][j + 1], side_grid[i][j + 1],
            ])

    lat_u = [0.82, 0.45, 0.0]

    def build_deck(indices, is_front):
        deck_rows = []
        for idx in indices:
            y, z_bot, tag = stations[idx]
            v_outer = side_grid[idx][-1]
            x_out, z_out = v_outer.co.x, v_outer.co.z
            zc = hood_trunk_center_z(y)
            row = [v_outer]
            for u in lat_u:
                x = x_out * u
                z = z_out + (zc - z_out) * (1.0 - (u ** 1.8))
                y_bow = y
                if is_front and y > 1.72:
                    y_bow = y + 0.055 * smoothstep(1.72, BODY_Y_MAX, y) * (1.0 - u * u)
                elif (not is_front) and y < -1.78:
                    y_bow = y - 0.032 * smoothstep(-1.78, BODY_Y_MIN, y) * (1.0 - u * u)
                row.append(bm_paint.verts.new((x, y_bow, z)))
            deck_rows.append(row)
        for r in range(len(deck_rows) - 1):
            for c in range(len(deck_rows[0]) - 1):
                bm_paint.faces.new([
                    deck_rows[r][c], deck_rows[r + 1][c],
                    deck_rows[r + 1][c + 1], deck_rows[r][c + 1],
                ])
        return deck_rows

    rear_indices = [i for i, s in enumerate(stations) if s[0] <= -1.30]
    front_indices = [i for i, s in enumerate(stations) if s[0] >= 0.90]
    rear_deck = build_deck(rear_indices, is_front=False)
    front_deck = build_deck(front_indices, is_front=True)

    # Beltline inner shelf under greenhouse
    inner_belt = []
    for i in range(rear_indices[-1], front_indices[0] + 1):
        vo = side_grid[i][-1]
        vi = bm_paint.verts.new((vo.co.x - 0.11, vo.co.y, vo.co.z + 0.008))
        inner_belt.append((vo, vi))
    for k in range(len(inner_belt) - 1):
        bm_paint.faces.new([inner_belt[k][0], inner_belt[k + 1][0], inner_belt[k + 1][1], inner_belt[k][1]])

    # Front fascia
    front_col = side_grid[-1]
    front_top_row = front_deck[-1]
    fascia_f = [front_col]
    for c_idx, u in enumerate(lat_u):
        col = []
        v_top = front_top_row[c_idx + 1]
        for j in range(n_v - 1):
            v_side = front_col[j]
            x = v_side.co.x * u
            y = v_side.co.y + 0.055 * (1.0 - u * u) - 0.015 * (1.0 - v_fracs[j])
            z = v_side.co.z + (v_top.co.z - front_col[-1].co.z) * (v_fracs[j] ** 1.4)
            col.append(bm_paint.verts.new((x, y, z)))
        col.append(v_top)
        fascia_f.append(col)
    for c in range(len(fascia_f) - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([fascia_f[c][j], fascia_f[c + 1][j], fascia_f[c + 1][j + 1], fascia_f[c][j + 1]])

    # Rear fascia
    rear_col = side_grid[0]
    rear_top_row = rear_deck[0]
    fascia_r = [rear_col]
    for c_idx, u in enumerate(lat_u):
        col = []
        v_top = rear_top_row[c_idx + 1]
        for j in range(n_v - 1):
            v_side = rear_col[j]
            x = v_side.co.x * u
            y = v_side.co.y - 0.032 * (1.0 - u * u) + 0.015 * (1.0 - v_fracs[j])
            z = v_side.co.z + (v_top.co.z - rear_col[-1].co.z) * (v_fracs[j] ** 1.4)
            col.append(bm_paint.verts.new((x, y, z)))
        col.append(v_top)
        fascia_r.append(col)
    for c in range(len(fascia_r) - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([fascia_r[c][j], fascia_r[c][j + 1], fascia_r[c + 1][j + 1], fascia_r[c + 1][j]])

    # Rolled wheel-arch lips & dark recessed wheel wells
    for tag, wy in (('rear', REAR_Y), ('front', FRONT_Y)):
        arch_idxs = [i for i, s in enumerate(stations) if s[2] == tag]
        lip_verts = []
        for idx in arch_idxs:
            v0 = side_grid[idx][0]
            vl = bm_paint.verts.new((v0.co.x - 0.028, v0.co.y, v0.co.z - 0.004))
            lip_verts.append((v0, vl))
        for k in range(len(lip_verts) - 1):
            bm_paint.faces.new([lip_verts[k][0], lip_verts[k][1], lip_verts[k + 1][1], lip_verts[k + 1][0]])

        for sx in (1.0, -1.0):
            outer_ring, inner_ring = [], []
            inner_x = 0.44 * sx
            for idx in arch_idxs:
                co = side_grid[idx][0].co
                outer_ring.append(bm_dark.verts.new(((co.x - 0.025) * sx, co.y, co.z - 0.002)))
                inner_ring.append(bm_dark.verts.new((inner_x, co.y, co.z + 0.015)))
            for k in range(len(outer_ring) - 1):
                verts = (
                    [outer_ring[k], inner_ring[k], inner_ring[k + 1], outer_ring[k + 1]]
                    if sx > 0
                    else [outer_ring[k], outer_ring[k + 1], inner_ring[k + 1], inner_ring[k]]
                )
                bm_dark.faces.new(verts)
            center_v = bm_dark.verts.new((inner_x, wy, ROCKER_Z))
            for k in range(len(inner_ring) - 1):
                verts = (
                    [inner_ring[k], center_v, inner_ring[k + 1]]
                    if sx > 0
                    else [inner_ring[k], inner_ring[k + 1], center_v]
                )
                bm_dark.faces.new(verts)

    # Dark underbody tray & black rocker skirts
    vu.add_box_bm(bm_dark, (0.0, -0.02, ROCKER_Z + 0.025), (1.34, 3.65, 0.04))
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_dark, (0.745 * sx, 0.02, ROCKER_Z + 0.016), (0.08, 1.72, 0.042), bevel_width=0.007)


def build_cabin_and_glass(bm_paint, bm_glass, bm_trim, bm_dark):
    """Build sleek unified greenhouse: roof + seamless A/rail/C arch + recessed glass + dark cabin core."""
    gh_specs = [
        ( 0.94, 0.800, 0.745,  1.00, 0.822),  # 0: cowl
        ( 0.25, 1.300, 0.578,  0.31, 1.326),  # 1: front roof header
        (-0.20, 1.322, 0.588, -0.20, 1.348),  # 2: roof apex
        (-0.65, 1.298, 0.574, -0.71, 1.322),  # 3: rear roof header
        (-1.34, 0.836, 0.720, -1.39, 0.856),  # 4: rear window base
    ]

    def gh_point(spec_idx, u, inset=0.0):
        ye, ze, hw, yc, zc = gh_specs[spec_idx]
        x = max(0.0, (hw - inset) * u)
        bow = 1.0 - u * u
        y = ye + (yc - ye) * bow
        z = ze + (zc - ze) * (1.0 - (u ** 1.85)) - inset * 0.22
        return Vector((x, y, z))

    def interp_gh(i0, i1, t, u, inset=0.0):
        return gh_point(i0, u, inset).lerp(gh_point(i1, u, inset), t)

    # 1. PAINTED ROOF PANEL (+X half, mirrored later)
    roof_us = [1.0, 0.91, 0.65, 0.32, 0.0]
    roof_stations = []
    roof_outer_pts = []
    for seg_i0, seg_i1, ts in ((1, 2, (0.0, 0.35, 0.70, 1.0)), (2, 3, (0.35, 0.70, 1.0))):
        for t in ts:
            row = []
            for u in roof_us:
                p = interp_gh(seg_i0, seg_i1, t, u)
                if u == 1.0:
                    p.z -= 0.010
                row.append(bm_paint.verts.new(p))
            roof_stations.append(row)
            roof_outer_pts.append(row[0].co.copy())

    for r in range(len(roof_stations) - 1):
        for c in range(len(roof_us) - 1):
            bm_paint.faces.new([
                roof_stations[r][c], roof_stations[r][c + 1],
                roof_stations[r + 1][c + 1], roof_stations[r + 1][c],
            ])

    # Front & rear roof header downward lips (connect cleanly to windshield/rear-glass gaskets)
    for row_idx, dy in ((0, -0.025), (-1, 0.025)):
        top_row = roof_stations[row_idx]
        lip_row = [bm_paint.verts.new((v.co.x, v.co.y + dy, v.co.z - 0.026)) for v in top_row]
        for c in range(len(roof_us) - 1):
            if row_idx == 0:
                bm_paint.faces.new([top_row[c], lip_row[c], lip_row[c + 1], top_row[c + 1]])
            else:
                bm_paint.faces.new([top_row[c], top_row[c + 1], lip_row[c + 1], lip_row[c]])

    # 2. UNIFIED CONTINUOUS ARCH: A-pillar -> Roof Side Rail -> C-pillar (ZERO notch/gap!)
    # Each entry: (p_outer, p_window_edge, p_glass_edge)
    arch_rings = []

    # A-pillar ascending (t = 0.0, 0.33, 0.66)
    for t in (0.0, 0.33, 0.66):
        po = interp_gh(0, 1, t, 1.0)
        pg = interp_gh(0, 1, t, 0.905)
        if t == 0.0:
            po.z -= 0.018
            pg.z -= 0.018
        # Inner side-window edge along A-pillar
        pw = Vector((po.x + 0.008, po.y - 0.072, po.z - 0.016))
        arch_rings.append((po, pw, pg))

    # Roof side rail along exact roof_outer_pts (7 stations from front header to rear header)
    n_roof = len(roof_outer_pts)
    for r_idx, po in enumerate(roof_outer_pts):
        f = r_idx / (n_roof - 1)
        # Smooth corner rounding at front (f=0) and rear (f=1) of side window
        y_shift = -0.045 * ((1.0 - f) ** 3) + 0.135 * (f ** 3)
        pw = Vector((po.x + 0.014, po.y + y_shift, po.z - 0.042))
        pg = Vector((po.x - 0.032, po.y + y_shift, po.z - 0.012))
        arch_rings.append((po, pw, pg))

    # C-pillar descending (t = 0.30, 0.65, 1.0)
    for t in (0.30, 0.65, 1.0):
        po = interp_gh(3, 4, t, 1.0)
        pg = interp_gh(3, 4, t, 0.895)
        if t == 1.0:
            po.z -= 0.020
            pg.z -= 0.020
        # Classic Corolla sloped C-pillar smoothly widening toward beltline
        plen = 0.165 + 0.145 * (t ** 1.05)
        pw = Vector((po.x + 0.012, po.y + plen, po.z - 0.024 * (1.0 - t)))
        arch_rings.append((po, pw, pg))

    # Build welded 4-sided tube along the continuous arch
    bm_rings = []
    for po, pw, pg in arch_rings:
        pi = Vector((pw.x - 0.032, pw.y, pw.z - 0.004))
        bm_rings.append([
            bm_paint.verts.new(po),
            bm_paint.verts.new(pw),
            bm_paint.verts.new(pi),
            bm_paint.verts.new(pg),
        ])
    for i in range(len(bm_rings) - 1):
        r0, r1 = bm_rings[i], bm_rings[i + 1]
        for k in range(4):
            nk = (k + 1) % 4
            bm_paint.faces.new([r0[k], r0[nk], r1[nk], r1[k]])

    # 3. RECESSED GLASS CORE (Windshield, Rear Window, and Arch-Fitted Side Glass!)
    ws_us = [-0.93, -0.60, -0.22, 0.22, 0.60, 0.93]
    ws_ts = [0.02, 0.35, 0.68, 0.985]
    ws_grid = []
    for t in ws_ts:
        row = []
        for u_s in ws_us:
            sx = 1.0 if u_s >= 0 else -1.0
            p = interp_gh(0, 1, t, abs(u_s), inset=0.022)
            p.x *= sx
            p.y -= 0.022
            row.append(bm_glass.verts.new(p))
        ws_grid.append(row)
    for r in range(len(ws_grid) - 1):
        for c in range(len(ws_us) - 1):
            bm_glass.faces.new([ws_grid[r][c], ws_grid[r][c + 1], ws_grid[r + 1][c + 1], ws_grid[r + 1][c]])

    rw_grid = []
    for t in ws_ts:
        row = []
        for u_s in ws_us:
            sx = 1.0 if u_s >= 0 else -1.0
            p = interp_gh(3, 4, t, abs(u_s), inset=0.022)
            p.x *= sx
            p.y += 0.022
            row.append(bm_glass.verts.new(p))
        rw_grid.append(row)
    for r in range(len(rw_grid) - 1):
        for c in range(len(ws_us) - 1):
            bm_glass.faces.new([rw_grid[r][c], rw_grid[r + 1][c], rw_grid[r + 1][c + 1], rw_grid[r][c + 1]])

    # Black rubber gaskets flush around windshield & rear glass (zero visor overhang)
    def add_glass_gasket(i0, i1, y_shift):
        g_us = [-0.94, -0.58, 0.0, 0.58, 0.94]
        for t0, t1 in ((0.005, 0.042), (0.958, 0.992)):
            r0, r1 = [], []
            for u_s in g_us:
                sx = 1.0 if u_s >= 0 else -1.0
                p0 = interp_gh(i0, i1, t0, abs(u_s), inset=0.020)
                p1 = interp_gh(i0, i1, t1, abs(u_s), inset=0.020)
                p0.x *= sx; p1.x *= sx
                p0.y += y_shift; p1.y += y_shift
                r0.append(bm_trim.verts.new(p0))
                r1.append(bm_trim.verts.new(p1))
            for c in range(len(g_us) - 1):
                bm_trim.faces.new([r0[c], r0[c + 1], r1[c + 1], r1[c]])

    add_glass_gasket(0, 1, -0.020)
    add_glass_gasket(3, 4,  0.020)

    # Side glass fitted EXACTLY inside the continuous arch opening (1.8 cm inset -> ZERO poke-through!)
    # Extract the inner arch polyline pw[0..12]
    win_arch = [pw for (_, pw, _) in arch_rings]
    for sx in (1.0, -1.0):
        # Build side glass fan/grid from beltline up to win_arch (indices 1..len-2 span the entire window top!)
        top_pts = []
        bot_pts = []
        for k in range(len(win_arch)):
            pt = win_arch[k]
            zb = belt_z(pt.y) - 0.012
            xb = side_x_at(pt.y, zb, ROCKER_Z, belt_z(pt.y)) - 0.055
            xt = pt.x - 0.018
            zt = max(zb + 0.008, pt.z + 0.008)
            top_pts.append(Vector((xt * sx, pt.y, zt)))
            bot_pts.append(Vector((xb * sx, pt.y, zb)))

        # 3 vertical rows (bottom -> mid curved bow -> top)
        v_rows = []
        for f in (0.0, 0.5, 1.0):
            row = []
            for k in range(len(top_pts)):
                p = bot_pts[k].lerp(top_pts[k], f)
                p.x += 0.014 * math.sin(f * math.pi) * sx
                row.append(bm_glass.verts.new(p))
            v_rows.append(row)
        for r in range(2):
            for k in range(len(top_pts) - 1):
                verts = (
                    [v_rows[r][k], v_rows[r][k + 1], v_rows[r + 1][k + 1], v_rows[r + 1][k]]
                    if sx > 0
                    else [v_rows[r][k], v_rows[r + 1][k], v_rows[r + 1][k + 1], v_rows[r][k + 1]]
                )
                bm_glass.faces.new(verts)

        # Black matte B-pillar (sleek 7.4 cm width flush between front & rear side windows)
        b_bot = Vector((0.750 * sx, -0.05, 0.822))
        b_top = Vector((0.592 * sx, -0.05, 1.286))
        mid_b = (b_bot + b_top) * 0.5
        tilt_b = math.atan2(b_top.x - b_bot.x, b_top.z - b_bot.z)
        vu.add_box_bm(bm_trim, mid_b, (0.022, 0.074, (b_top - b_bot).length), (0.0, tilt_b, 0.0), 0.004)

        # Thin rear-door vertical quarter-glass divider bar (signature E90 detail!)
        q_bot = Vector((0.742 * sx, -0.72, 0.832))
        q_top = Vector((0.594 * sx, -0.60, 1.258))
        mid_q = (q_bot + q_top) * 0.5
        tilt_qx = math.atan2(q_top.x - q_bot.x, q_top.z - q_bot.z)
        tilt_qy = -math.atan2(q_top.y - q_bot.y, q_top.z - q_bot.z)
        vu.add_box_bm(bm_trim, mid_q, (0.018, 0.020, (q_top - q_bot).length), (tilt_qy, tilt_qx, 0.0), 0.003)

        # Black lower window beltline weatherstrip
        vu.add_box_bm(bm_trim, (0.755 * sx, -0.10, 0.830), (0.030, 1.94, 0.020), (-0.016, 0.0, 0.0), 0.004)

        # Black front corner mirror-mount triangle
        vu.add_box_bm(bm_trim, (0.738 * sx,  0.74, 0.862), (0.028, 0.14, 0.095), (0.0, -0.25 * sx, 0.0), 0.005)


def build_interior_silhouette(bm_int):
    """Build ultra-light (~220 tris) dark cabin interior strictly inside the glass envelope."""
    # Cabin lower tub & rear parcel shelf (all strictly below beltline z=0.83)
    vu.add_box_bm(bm_int, (0.0, -0.14, 0.60), (1.28, 1.80, 0.42))
    vu.add_box_bm(bm_int, (0.0, -0.96, 0.825), (1.16, 0.36, 0.04))

    # Dashboard & driver instrument binnacle (y <= 0.72 -> safely 20+ cm inside windshield!)
    vu.add_box_bm(bm_int, (0.0,   0.56, 0.81), (1.20, 0.32, 0.18), bevel_width=0.02)
    vu.add_box_bm(bm_int, (-0.31, 0.50, 0.91), (0.34, 0.22, 0.08), bevel_width=0.015)

    # Steering wheel rim & column (left-hand drive, Ivory Coast!)
    vu.add_cylinder_bm(bm_int, (-0.31, 0.46, 0.84), (-0.31, 0.35, 0.90), 0.028, segments=8)
    sw_c = Vector((-0.31, 0.33, 0.92))
    sw_r = 0.155
    sw_tilt = math.radians(24.0)
    n_sw = 12
    for i in range(n_sw):
        a0 = 2.0 * math.pi * i / n_sw
        a1 = 2.0 * math.pi * (i + 1) / n_sw
        p0 = sw_c + Vector((math.cos(a0) * sw_r, -math.sin(a0) * sw_r * math.sin(sw_tilt), math.sin(a0) * sw_r * math.cos(sw_tilt)))
        p1 = sw_c + Vector((math.cos(a1) * sw_r, -math.sin(a1) * sw_r * math.sin(sw_tilt), math.sin(a1) * sw_r * math.cos(sw_tilt)))
        vu.add_cylinder_bm(bm_int, p0, p1, 0.014, segments=6, cap_ends=False)

    # 2 front seatbacks + headrests (center of cabin, well below roof z=1.34!)
    for sx in (-1.0, 1.0):
        vu.add_box_bm(bm_int, (0.31 * sx, -0.17, 0.89), (0.42, 0.12, 0.36), (-0.22, 0.0, 0.0), bevel_width=0.025)
        vu.add_box_bm(bm_int, (0.31 * sx, -0.22, 1.13), (0.21, 0.085, 0.14), (-0.15, 0.0, 0.0), bevel_width=0.02)

    # Rear bench backrest + 2 rear headrests (strictly clear of rear glass!)
    vu.add_box_bm(bm_int, (0.0, -0.78, 0.88), (1.14, 0.13, 0.32), (-0.24, 0.0, 0.0), bevel_width=0.025)
    for sx in (-1.0, 1.0):
        vu.add_box_bm(bm_int, (0.32 * sx, -0.78, 1.08), (0.20, 0.08, 0.11), (-0.18, 0.0, 0.0), bevel_width=0.018)


def build_wraparound_bumper(bm_trim, is_front=True):
    """Build sculpted wraparound black plastic bumper hugging tapered fenders seamlessly."""
    if is_front:
        y_arch, y_corner, y_tip = FRONT_Y + ARCH_R - 0.012, 1.98, 2.132
        z_top, z_mid, z_bot = 0.478, 0.345, 0.228
        hw_side = plan_half_width(y_arch) + 0.004
        hw_mid_w = plan_half_width(y_arch + 0.20) + 0.008
        hw_corner = plan_half_width(y_corner) + 0.014
        hw_flat = 0.56
    else:
        y_arch, y_corner, y_tip = REAR_Y - ARCH_R + 0.012, -1.99, -2.132
        z_top, z_mid, z_bot = 0.495, 0.365, 0.252
        hw_side = plan_half_width(y_arch) + 0.004
        hw_mid_w = plan_half_width(y_arch - 0.22) + 0.008
        hw_corner = plan_half_width(y_corner) + 0.016
        hw_flat = 0.60

    half_pts = [
        (hw_side, y_arch),
        (hw_mid_w, y_arch + (0.20 if is_front else -0.22)),
        (hw_corner, y_corner),
        (hw_flat, y_tip - (0.042 if is_front else -0.032)),
        (hw_flat * 0.5, y_tip - (0.010 if is_front else -0.008)),
        (0.0, y_tip),
    ]
    path = [(-x, y) for (x, y) in half_pts[:-1]] + [(x, y) for (x, y) in reversed(half_pts)]

    prof = [
        (-0.14, z_top - 0.008),
        ( 0.00, z_top),
        ( 0.022, z_top - 0.042),
        ( 0.016, z_mid),
        (-0.022, z_bot + 0.030),
        (-0.056, z_bot),
        (-0.14,  z_bot + 0.015),
    ]

    rings = []
    n_p = len(path)
    for i in range(n_p):
        p = Vector(path[i])
        p_prev = Vector(path[max(0, i - 1)])
        p_next = Vector(path[min(n_p - 1, i + 1)])
        tang = (p_next - p_prev).normalized()
        nrm = Vector((-tang.y, tang.x))
        if nrm.dot(p) < 0:
            nrm = -nrm
        ring = []
        for off, z in prof:
            wscale = 0.55 if (i == 0 or i == n_p - 1) else 1.0
            eff_off = off * wscale if off < 0 else off
            xy = p + nrm * eff_off
            ring.append(bm_trim.verts.new((xy.x, xy.y, z)))
        rings.append(ring)

    n_prof = len(prof)
    for i in range(n_p - 1):
        r0, r1 = rings[i], rings[i + 1]
        for j in range(n_prof):
            nj = (j + 1) % n_prof
            bm_trim.faces.new([r0[j], r1[j], r1[nj], r0[nj]])
    bm_trim.faces.new(list(reversed(rings[0])))
    bm_trim.faces.new(rings[-1])


def add_curved_lens_bm(bm, pts_xy, z0, z1, thickness=0.018):
    """Build a curved flush-fitting automotive lens/bezel along a 2D contour pts_xy."""
    n = len(pts_xy)
    # Compute outward 2D normals along contour
    nrms = []
    for i in range(n):
        p = Vector(pts_xy[i])
        p0 = Vector(pts_xy[max(0, i - 1)])
        p1 = Vector(pts_xy[min(n - 1, i + 1)])
        t = (p1 - p0).normalized()
        nrm = Vector((-t.y, t.x))
        if nrm.dot(p) < 0:
            nrm = -nrm
        nrms.append(nrm)

    rings = []
    for i in range(n):
        p = Vector(pts_xy[i])
        nm = nrms[i]
        p_in = p - nm * 0.015
        p_out = p + nm * thickness
        rings.append([
            bm.verts.new((p_in.x,  p_in.y,  z0)),
            bm.verts.new((p_out.x, p_out.y, z0)),
            bm.verts.new((p_out.x, p_out.y, z1)),
            bm.verts.new((p_in.x,  p_in.y,  z1)),
        ])
    for i in range(n - 1):
        r0, r1 = rings[i], rings[i + 1]
        for k in range(4):
            nk = (k + 1) % 4
            bm.faces.new([r0[k], r1[k], r1[nk], r0[nk]])
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def add_door_seam_curve(bm_dark, y_val, z_start, z_end, sx, n_steps=6):
    """Add a flush 4 mm surface-hugging dark door seam along the curved side body."""
    zt = belt_z(y_val)
    ribbon_l = []
    ribbon_r = []
    half_gap = 0.0028
    for i in range(n_steps + 1):
        z = z_start + (z_end - z_start) * (i / n_steps)
        x = (side_x_at(y_val, z, ROCKER_Z, zt) + 0.0025) * sx
        ribbon_l.append(bm_dark.verts.new((x, y_val - half_gap, z)))
        ribbon_r.append(bm_dark.verts.new((x, y_val + half_gap, z)))
    for i in range(n_steps):
        verts = (
            [ribbon_l[i], ribbon_r[i], ribbon_r[i + 1], ribbon_l[i + 1]]
            if sx > 0
            else [ribbon_l[i], ribbon_l[i + 1], ribbon_r[i + 1], ribbon_r[i]]
        )
        bm_dark.faces.new(verts)


def build_exterior_details(bm_trim, bm_dark, bm_chrome, bm_plate, bm_head, bm_tail, bm_amber, bm_rev, bm_sign_f, bm_sign_g):
    """Build bumpers, flush wraparound headlights/taillights, grille, moldings, handles, mirrors, TAXI sign."""
    build_wraparound_bumper(bm_trim, is_front=True)
    build_wraparound_bumper(bm_trim, is_front=False)

    # Front bumper lower air intake (cleanly recessed, no clipping)
    vu.add_box_bm(bm_dark, (0.0, 2.125, 0.292), (0.86, 0.036, 0.078))
    vu.add_box_bm(bm_trim, (0.0, 2.142, 0.302), (0.84, 0.020, 0.013))
    vu.add_box_bm(bm_trim, (0.0, 2.138, 0.274), (0.82, 0.020, 0.013))

    # Front bumper amber turn-signal strips flush in bumper upper shelf
    for sx in (1.0, -1.0):
        bumper_sig_pts = [(0.45 * sx, 2.118), (0.58 * sx, 2.094), (0.67 * sx, 2.062)]
        if sx < 0:
            bumper_sig_pts = list(reversed(bumper_sig_pts))
        add_curved_lens_bm(bm_amber, bumper_sig_pts, 0.415, 0.448, thickness=0.016)

    # -------------------------------------------------------------------------
    # FRONT GRILLE & FLUSH WRAPAROUND HEADLIGHTS (conforming to nose curve!)
    # -------------------------------------------------------------------------
    def nose_pt(u, sx=1.0):
        """Approximate front fascia contour at headlight height z ~ 0.55."""
        # u=0 centerline, u=0.45 grille end, u=0.80 headlight corner, u=1.0 fender side
        if u <= 0.80:
            t = u / 0.80
            x = 0.675 * t
            y = 2.078 - 0.055 * (t ** 2.1)
        else:
            t = (u - 0.80) / 0.20
            x = 0.675 + 0.098 * (t ** 0.85)
            y = 2.023 - 0.145 * (t ** 1.25)
        return (x * sx, y)

    # Recessed black grille center
    grille_pts = [nose_pt(u, -1.0) for u in (0.45, 0.25, 0.0)] + [nose_pt(u, 1.0) for u in (0.25, 0.45)]
    add_curved_lens_bm(bm_dark, grille_pts, 0.495, 0.592, thickness=0.010)
    # 3 crisp horizontal grille slats + upper chrome trim
    for gz in (0.515, 0.542, 0.568):
        add_curved_lens_bm(bm_trim, grille_pts, gz - 0.005, gz + 0.005, thickness=0.016)
    add_curved_lens_bm(bm_chrome, grille_pts, 0.585, 0.596, thickness=0.017)
    # Tiny neutral silver badge oval in center
    vu.add_box_bm(bm_chrome, (0.0, 2.096, 0.544), (0.068, 0.014, 0.032), bevel_width=0.006)

    # Flush curved headlights + flush wraparound corner turn lamps
    for sx in (1.0, -1.0):
        bezel_pts = [nose_pt(u, sx) for u in (0.455, 0.62, 0.79, 0.90, 0.985)]
        head_pts  = [nose_pt(u, sx) for u in (0.47, 0.62, 0.785)]
        corn_pts  = [nose_pt(u, sx) for u in (0.795, 0.89, 0.975)]
        if sx < 0:
            bezel_pts.reverse(); head_pts.reverse(); corn_pts.reverse()
        add_curved_lens_bm(bm_trim,  bezel_pts, 0.492, 0.596, thickness=0.011)
        add_curved_lens_bm(bm_head,  head_pts,  0.502, 0.586, thickness=0.018)
        add_curved_lens_bm(bm_amber, corn_pts,  0.502, 0.586, thickness=0.017)

    # -------------------------------------------------------------------------
    # REAR FLUSH WRAPAROUND TAILLIGHTS (smooth unbroken corner wrap!)
    # -------------------------------------------------------------------------
    def tail_pt(u, sx=1.0):
        """Rear fascia contour at taillight height z ~ 0.59 hugging outside the corner."""
        if u <= 0.78:
            t = u / 0.78
            x = 0.722 * t
            y = -2.078 + 0.022 * (t ** 2.2)
        else:
            t = (u - 0.78) / 0.22
            ang = t * (math.pi * 0.48)
            x = 0.722 + 0.072 * math.sin(ang)
            y = -2.056 + 0.145 * (1.0 - math.cos(ang))
        return (x * sx, y)

    for sx in (1.0, -1.0):
        bz_pts  = [tail_pt(u, sx) for u in (0.39, 0.58, 0.76, 0.84, 0.91, 0.99)]
        red_pts = [tail_pt(u, sx) for u in (0.405, 0.58, 0.76, 0.84, 0.91, 0.98)]
        amb_pts = [tail_pt(u, sx) for u in (0.63, 0.76, 0.84, 0.91, 0.98)]
        rev_pts = [tail_pt(u, sx) for u in (0.405, 0.52, 0.615)]
        if sx > 0:
            bz_pts.reverse(); red_pts.reverse(); amb_pts.reverse(); rev_pts.reverse()
        add_curved_lens_bm(bm_trim,  bz_pts,  0.515, 0.652, thickness=0.012)
        add_curved_lens_bm(bm_tail,  red_pts, 0.524, 0.598, thickness=0.020)
        add_curved_lens_bm(bm_amber, amb_pts, 0.603, 0.643, thickness=0.019)
        add_curved_lens_bm(bm_rev,   rev_pts, 0.603, 0.643, thickness=0.019)

    # Rear license plate & trunk keyhole (clean recessed rear pocket, matching reference!)
    vu.add_box_bm(bm_trim,  (0.0, -2.076, 0.585), (0.37, 0.022, 0.128), bevel_width=0.004)
    vu.add_box_bm(bm_plate, (0.0, -2.088, 0.585), (0.33, 0.014, 0.102), bevel_width=0.002)
    vu.add_cylinder_bm(bm_trim, (0.0, -2.064, 0.680), (0.0, -2.090, 0.680), 0.012, segments=10)

    # Front bumper clean recessed plate pad (no protruding white slab — matches reference E90 nose!)
    vu.add_box_bm(bm_dark, (0.0, 2.150, 0.368), (0.34, 0.014, 0.072), bevel_width=0.003)

    # Chrome single exhaust tailpipe (right rear)
    vu.add_cylinder_bm(bm_chrome, (0.45, -1.86, 0.215), (0.45, -2.145, 0.205), 0.024, segments=10)
    vu.add_cylinder_bm(bm_dark,   (0.45, -2.12, 0.206), (0.45, -2.148, 0.205), 0.019, segments=10)

    # Side moldings, handles, mirrors, fender repeaters, surface-hugging door seams
    for sx in (1.0, -1.0):
        # Continuous black door rubbing strip
        vu.add_box_bm(bm_trim, (0.834 * sx,  0.02, 0.455), (0.030, 1.72, 0.040), bevel_width=0.006)
        vu.add_box_bm(bm_trim, (0.822 * sx,  1.68, 0.455), (0.028, 0.20, 0.038), (0, 0, -0.06 * sx), 0.005)
        vu.add_box_bm(bm_trim, (0.822 * sx, -1.66, 0.458), (0.028, 0.22, 0.038), (0, 0,  0.05 * sx), 0.005)

        # Classic subtle side fender amber repeater indicator behind front arch
        vu.add_box_bm(bm_amber, (0.824 * sx, 0.855, 0.615), (0.016, 0.055, 0.024), bevel_width=0.004)

        # Front & rear black door handles
        for hy in (0.08, -0.66):
            vu.add_box_bm(bm_dark, (0.812 * sx, hy, 0.732), (0.022, 0.130, 0.038), bevel_width=0.004)
            vu.add_box_bm(bm_trim, (0.828 * sx, hy, 0.736), (0.020, 0.110, 0.022), bevel_width=0.004)

        # Side mirrors (sleek aerodynamic E90 mirror)
        vu.add_box_bm(bm_trim,   (0.805 * sx, 0.715, 0.862), (0.110, 0.050, 0.032), (0.0, -0.16 * sx, -0.15 * sx), 0.006)
        vu.add_box_bm(bm_trim,   (0.895 * sx, 0.700, 0.880), (0.138, 0.088, 0.082), (0.0, -0.04 * sx, -0.18 * sx), 0.016, 2)
        vu.add_box_bm(bm_chrome, (0.895 * sx, 0.655, 0.880), (0.112, 0.010, 0.064), (0.0, -0.04 * sx, -0.18 * sx), 0.003)

        # Flush surface-hugging curved door seams (ZERO poking!)
        add_door_seam_curve(bm_dark,  0.82, 0.245, 0.795, sx)
        add_door_seam_curve(bm_dark, -0.05, 0.245, 0.818, sx)
        add_door_seam_curve(bm_dark, -0.82, 0.560, 0.828, sx)

    # Windshield wipers & cowl vent
    for wx in (-0.28, 0.22):
        vu.add_box_bm(bm_trim, (wx, 0.975, 0.834), (0.43, 0.016, 0.012), (0.62, 0.0, 0.14), 0.003)
    vu.add_box_bm(bm_dark, (0.0, 0.965, 0.818), (1.36, 0.082, 0.016), bevel_width=0.004)

    # Compact sleek Roof TAXI Sign (Lumignon) matching reference proportions!
    z0, z1, cy = 1.338, 1.432, 0.04
    hw0, hd0, hw1, hd1 = 0.168, 0.072, 0.128, 0.042
    v = [
        bm_sign_f.verts.new((-hw0, cy - hd0, z0)), bm_sign_f.verts.new(( hw0, cy - hd0, z0)),
        bm_sign_f.verts.new(( hw0, cy + hd0, z0)), bm_sign_f.verts.new((-hw0, cy + hd0, z0)),
        bm_sign_f.verts.new((-hw1, cy - hd1, z1)), bm_sign_f.verts.new(( hw1, cy - hd1, z1)),
        bm_sign_f.verts.new(( hw1, cy + hd1, z1)), bm_sign_f.verts.new((-hw1, cy + hd1, z1)),
    ]
    for fverts in ([v[0],v[3],v[2],v[1]], [v[4],v[5],v[6],v[7]], [v[0],v[1],v[5],v[4]],
                   [v[1],v[2],v[6],v[5]], [v[2],v[3],v[7],v[6]], [v[3],v[0],v[4],v[7]]):
        bm_sign_f.faces.new(fverts)
    bmesh.ops.bevel(bm_sign_f, geom=list(bm_sign_f.edges), offset=0.010, segments=2, affect='EDGES', clamp_overlap=True)

    ihw0, ihd0, ihw1, ihd1 = 0.128, 0.074, 0.098, 0.045
    vg = [
        bm_sign_g.verts.new((-ihw0, cy - ihd0, z0 + 0.012)), bm_sign_g.verts.new(( ihw0, cy - ihd0, z0 + 0.012)),
        bm_sign_g.verts.new(( ihw0, cy + ihd0, z0 + 0.012)), bm_sign_g.verts.new((-ihw0, cy + ihd0, z0 + 0.012)),
        bm_sign_g.verts.new((-ihw1, cy - ihd1, z1 - 0.003)), bm_sign_g.verts.new(( ihw1, cy - ihd1, z1 - 0.003)),
        bm_sign_g.verts.new(( ihw1, cy + ihd1, z1 - 0.003)), bm_sign_g.verts.new((-ihw1, cy + ihd1, z1 - 0.003)),
    ]
    for fverts in ([vg[0],vg[3],vg[2],vg[1]], [vg[4],vg[5],vg[6],vg[7]], [vg[0],vg[1],vg[5],vg[4]],
                   [vg[1],vg[2],vg[6],vg[5]], [vg[2],vg[3],vg[7],vg[6]], [vg[3],vg[0],vg[4],vg[7]]):
        bm_sign_g.faces.new(fverts)
    vu.add_box_bm(bm_trim, (0.0, cy, z0 - 0.004), (0.34, 0.15, 0.014), bevel_width=0.003)


def merge_two_bms(bm_a, bm_b):
    bm_out = bmesh.new()
    for f in bm_a.faces:
        f.material_index = 0
    for f in bm_b.faces:
        f.material_index = 1
    m1 = bpy.data.meshes.new('_tmp_m1')
    bm_a.to_mesh(m1); bm_a.free()
    m2 = bpy.data.meshes.new('_tmp_m2')
    bm_b.to_mesh(m2); bm_b.free()
    bm_out.from_mesh(m1)
    bm_out.from_mesh(m2)
    bpy.data.meshes.remove(m1)
    bpy.data.meshes.remove(m2)
    return bm_out


def build_corolla_taxi():
    vu.clear_scene()
    col = vu.ensure_collection('VH_Taxi_Corolla')
    # Vibrant golden-canary taxi yellow matching reference E90 under AgX Punchy
    mats = vu.create_standard_vehicle_materials(paint_rgba=(0.98, 0.58, 0.002, 1.0))

    root = bpy.data.objects.new('VH_Taxi_Corolla', None)
    root.empty_display_type = 'PLAIN_AXES'
    vu.link_to_collection(root, col)

    bm_paint, bm_glass = bmesh.new(), bmesh.new()
    bm_trim, bm_dark, bm_int = bmesh.new(), bmesh.new(), bmesh.new()
    bm_chrome, bm_plate = bmesh.new(), bmesh.new()
    bm_head, bm_tail, bm_amber, bm_rev = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    bm_sign_f, bm_sign_g = bmesh.new(), bmesh.new()

    build_lower_body_and_wells(bm_paint, bm_dark)
    build_cabin_and_glass(bm_paint, bm_glass, bm_trim, bm_dark)
    build_interior_silhouette(bm_int)
    build_exterior_details(
        bm_trim, bm_dark, bm_chrome, bm_plate, bm_head, bm_tail, bm_amber, bm_rev, bm_sign_f, bm_sign_g
    )

    body_obj = vu.create_mesh_object('Carrosserie', bm_paint, mats['paint'], col, parent=root, angle_deg=None)

    mod_mirror = body_obj.modifiers.new('Mirror', 'MIRROR')
    mod_mirror.use_axis = (True, False, False)
    mod_mirror.use_clip = True
    mod_mirror.merge_threshold = 0.003

    bpy.context.view_layer.objects.active = body_obj
    body_obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier='Mirror')

    bm_c = bmesh.new()
    bm_c.from_mesh(body_obj.data)
    bmesh.ops.remove_doubles(bm_c, verts=bm_c.verts, dist=0.004)
    crease_layer = bm_c.edges.layers.float.get('crease_edge') or bm_c.edges.layers.float.new('crease_edge')
    for e in bm_c.edges:
        if len(e.link_faces) == 2:
            ang = e.calc_face_angle(0.0)
            if ang > math.radians(58.0):
                e[crease_layer] = 0.56
            elif ang > math.radians(34.0):
                e[crease_layer] = 0.28
        elif len(e.link_faces) == 1:
            e[crease_layer] = 0.85
    bmesh.ops.recalc_face_normals(bm_c, faces=bm_c.faces)
    bm_c.to_mesh(body_obj.data)
    bm_c.free()

    mod_sub = body_obj.modifiers.new('Subsurf', 'SUBSURF')
    mod_sub.levels = 1
    mod_sub.render_levels = 1
    bpy.ops.object.modifier_apply(modifier='Subsurf')
    vu.apply_smooth_with_sharp_angle(body_obj, angle_deg=46.0)

    vu.create_mesh_object('Vitres', bm_glass, mats['glass'], col, parent=root, angle_deg=48.0)
    vu.create_mesh_object('Interieur', bm_int, mats['trim_dark'], col, parent=root, angle_deg=45.0)
    vu.create_mesh_object('Plastiques', merge_two_bms(bm_trim, bm_dark), [mats['trim'], mats['trim_dark']], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Chrome', bm_chrome, mats['chrome'], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Plaques', bm_plate, mats['plate'], col, parent=root, angle_deg=30.0)
    vu.create_mesh_object('Phares', bm_head, mats['headlight'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Feux', merge_two_bms(bm_tail, bm_rev), [mats['taillight'], mats['reverse']], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Clignotants', bm_amber, mats['amber'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Lumignon', merge_two_bms(bm_sign_f, bm_sign_g), [mats['taxi_sign_frame'], mats['taxi_sign_glow']], col, parent=root, angle_deg=35.0)

    # Mobile-optimized steel wheels (copy mesh per wheel to keep FBX export 100% warning-free!)
    base_wheel = vu.build_steel_wheel_mesh(
        'M_CorollaSteelWheel', radius=WHEEL_R, width=WHEEL_W, rim_ratio=0.58,
        segments=18, hole_count=12, mats=[mats['tyre'], mats['steel_rim'], mats['brake']],
    )
    for idx, (wname, pos, rot_z) in enumerate((
        ('Roue_FL', (-WHEEL_X, FRONT_Y, WHEEL_R), math.pi),
        ('Roue_FR', ( WHEEL_X, FRONT_Y, WHEEL_R), 0.0),
        ('Roue_RL', (-WHEEL_X, REAR_Y,  WHEEL_R), math.pi),
        ('Roue_RR', ( WHEEL_X, REAR_Y,  WHEEL_R), 0.0),
    )):
        wmesh = base_wheel if idx == 0 else base_wheel.copy()
        wmesh.name = f'Mesh_{wname}'
        wobj = bpy.data.objects.new(wname, wmesh)
        vu.link_to_collection(wobj, col)
        wobj.parent = root
        wobj.location = pos
        wobj.rotation_euler = (0.0, 0.0, rot_z)

    return root


def main():
    repo_root = os.path.abspath(os.path.join(SCRIPT_DIR, '..', '..', '..'))
    out_dir = os.path.join(repo_root, 'tools', 'blender', 'output', 'corolla_taxi')
    unity_dir = os.path.join(repo_root, 'IvoryState', 'Assets', '_Project', 'Art', 'Models', 'Vehicles')
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(unity_dir, exist_ok=True)

    root = build_corolla_taxi()

    stats_lod0 = vu.report_scene_stats(root)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, 'corolla_taxi.blend'))

    fbx_lod0 = os.path.join(unity_dir, 'VH_Taxi_Corolla.fbx')
    fbx_lod1 = os.path.join(unity_dir, 'VH_Taxi_Corolla_LOD1.fbx')
    vu.export_unity_fbx(root, fbx_lod0)
    stats_lod1 = vu.export_unity_lod1_fbx(root, fbx_lod1, ratio=0.40)

    summary = {'lod0': stats_lod0, 'lod1': stats_lod1, 'fbx_lod0': fbx_lod0, 'fbx_lod1': fbx_lod1}
    with open(os.path.join(out_dir, 'corolla_taxi_stats.json'), 'w', encoding='utf-8') as f:
        json.dump(summary, f, indent=2)
    print('=== VEHICLE_STATS_JSON ===')
    print(json.dumps(summary, indent=2))
    print('=== END_STATS ===')

    if '--no-render' not in sys.argv:
        renders = vu.render_studio_sheet(root, out_dir, prefix='corolla_taxi', samples=22, res=(800, 450))
        print('RENDERED:', json.dumps(renders, indent=2))


if __name__ == '__main__':
    main()
