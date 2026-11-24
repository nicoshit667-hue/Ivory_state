"""Build vehicle #18: Abidjan Gbaka / Minibus (Toyota HiAce H100 style) for Ivory State.

Includes signature light-blue side stripe, tubular metal roof rack, rear tailgate ladder,
sliding-door rail, steel wheels, light interior, LOD0 + LOD1 FBX, and 4-view studio sheet.
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


Y_MIN = -2.26
Y_MAX =  2.24
MAX_HW = 0.855
ROOF_HW = 0.715
ROOF_Z = 1.93
WHEEL_R = 0.345
WHEEL_W = 0.205
WHEEL_X = 0.740
FRONT_Y = 1.36
REAR_Y = -1.22
ARCH_R = 0.390
ROCKER_Z = 0.260


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def smoothstep(e0, e1, x):
    t = clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def van_top_z(y):
    """Side silhouette top roof/nose profile of HiAce H100 cab-over van."""
    if y <= 1.44:
        # Gently crowned flat roof tapering subtly at rear corner
        t_rear = smoothstep(-1.95, Y_MIN, y)
        return ROOF_Z - 0.055 * (t_rear ** 1.8)
    if y <= 2.05:
        # Steep raked cab-over windshield from roof header (1.44, 1.93) to cowl (2.05, 1.15)
        t = (y - 1.44) / (2.05 - 1.44)
        return ROOF_Z - (ROOF_Z - 1.15) * (t ** 1.05)
    # Stubby sloped front nose down to top of headlights/bumper
    t = clamp((y - 2.05) / (Y_MAX - 2.05), 0.0, 1.0)
    return 1.15 - 0.56 * (t ** 1.15)


def van_half_width(y, z):
    """Sculpted van body half-width (subtle side bulge, clean upper tumblehome, rounded nose/tail)."""
    hw = MAX_HW
    # Plan-view corner taper at front & rear
    if y > 1.78:
        t = clamp((y - 1.78) / (Y_MAX - 1.78), 0.0, 1.0)
        hw -= 0.095 * (t ** 2.4)
    elif y < -1.92:
        t = clamp((-1.92 - y) / (-1.92 - Y_MIN), 0.0, 1.0)
        hw -= 0.065 * (t ** 2.4)

    # Vertical cross-section: subtle tuck at rocker, fullest at belt z~0.95, tumblehome to roof
    if z < 0.65:
        t = clamp((0.65 - z) / (0.65 - ROCKER_Z), 0.0, 1.0)
        hw -= 0.048 * (t ** 1.7)
    elif z > 1.15:
        t = clamp((z - 1.15) / (ROOF_Z - 1.15), 0.0, 1.0)
        hw -= (MAX_HW - ROOF_HW) * (t ** 1.45)

    # Subtle flared wheel-arch lip
    for wy in (FRONT_Y, REAR_Y):
        d = math.hypot(y - wy, z - WHEEL_R)
        if ARCH_R - 0.01 <= d <= ARCH_R + 0.075:
            ft = 1.0 - clamp((d - ARCH_R) / 0.075, 0.0, 1.0)
            hw += 0.014 * (ft ** 1.6)
    return hw


def build_van_stations():
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
    for y in (-2.26, -2.14, -1.96, -1.76):
        if y < rear_arch[0][0] - 0.03:
            zb = ROCKER_Z + 0.25 * smoothstep(-1.68, -1.96, y)
            stations.append((y, zb, None))
    stations.extend(rear_arch)

    front_arch = arch_pts(FRONT_Y, 'front', 8)
    for y in (-0.62, -0.20, 0.30, 0.80):
        if rear_arch[-1][0] + 0.03 < y < front_arch[0][0] - 0.03:
            stations.append((y, ROCKER_Z, None))
    stations.extend(front_arch)

    for y in (1.84, 2.02, 2.15, 2.24):
        if y > front_arch[-1][0] + 0.03:
            zb = ROCKER_Z + 0.24 * smoothstep(1.78, 2.04, y)
            stations.append((y, zb, None))
    return stations


def build_van_body(bm_paint, bm_dark):
    """Build full sculpted cab-over HiAce van shell (+X half, later mirrored) + wheel wells."""
    stations = build_van_stations()
    n_st = len(stations)
    # 7 vertical rows from bottom/arch up to roof edge
    v_fracs = [0.0, 0.18, 0.38, 0.56, 0.74, 0.90, 1.0]
    n_v = len(v_fracs)

    side_grid = []
    for y, z_bot, tag in stations:
        zt = van_top_z(y)
        col = []
        for frac in v_fracs:
            z = z_bot + (zt - z_bot) * frac
            x = van_half_width(y, z)
            if frac == 1.0:
                x -= 0.025
            col.append(bm_paint.verts.new((x, y, z)))
        side_grid.append(col)

    for i in range(n_st - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([
                side_grid[i][j], side_grid[i + 1][j],
                side_grid[i + 1][j + 1], side_grid[i][j + 1],
            ])

    # Top roof & front windshield/nose bridge across X to centerline (u = 0.75, 0.40, 0.0)
    lat_u = [0.76, 0.40, 0.0]
    top_rows = []
    for i, (y, z_bot, tag) in enumerate(stations):
        vo = side_grid[i][-1]
        x_out, z_out = vo.co.x, vo.co.z
        row = [vo]
        for u in lat_u:
            x = x_out * u
            crown = 0.035 * (1.0 - u * u) if y < 1.55 else 0.020 * (1.0 - u * u)
            y_bow = y
            if y > 1.80:
                y_bow = y + 0.045 * smoothstep(1.80, Y_MAX, y) * (1.0 - u * u)
            elif y < -2.00:
                y_bow = y - 0.028 * smoothstep(-2.00, Y_MIN, y) * (1.0 - u * u)
            row.append(bm_paint.verts.new((x, y_bow, z_out + crown)))
        top_rows.append(row)

    for r in range(len(top_rows) - 1):
        for c in range(len(top_rows[0]) - 1):
            bm_paint.faces.new([
                top_rows[r][c], top_rows[r + 1][c],
                top_rows[r + 1][c + 1], top_rows[r][c + 1],
            ])

    # Front nose face (closing side_grid[-1] + top_rows[-1] to centerline)
    f_col = side_grid[-1]
    f_top = top_rows[-1]
    fascia_f = [f_col]
    for c_idx, u in enumerate(lat_u):
        col = []
        vt = f_top[c_idx + 1]
        for j in range(n_v - 1):
            vs = f_col[j]
            x = vs.co.x * u
            y = vs.co.y + 0.045 * (1.0 - u * u)
            z = vs.co.z + (vt.co.z - f_col[-1].co.z) * (v_fracs[j] ** 1.3)
            col.append(bm_paint.verts.new((x, y, z)))
        col.append(vt)
        fascia_f.append(col)
    for c in range(len(fascia_f) - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([fascia_f[c][j], fascia_f[c + 1][j], fascia_f[c + 1][j + 1], fascia_f[c][j + 1]])

    # Rear tailgate face (closing side_grid[0] + top_rows[0] to centerline)
    r_col = side_grid[0]
    r_top = top_rows[0]
    fascia_r = [r_col]
    for c_idx, u in enumerate(lat_u):
        col = []
        vt = r_top[c_idx + 1]
        for j in range(n_v - 1):
            vs = r_col[j]
            x = vs.co.x * u
            y = vs.co.y - 0.028 * (1.0 - u * u)
            z = vs.co.z + (vt.co.z - r_col[-1].co.z) * (v_fracs[j] ** 1.3)
            col.append(bm_paint.verts.new((x, y, z)))
        col.append(vt)
        fascia_r.append(col)
    for c in range(len(fascia_r) - 1):
        for j in range(n_v - 1):
            bm_paint.faces.new([fascia_r[c][j], fascia_r[c][j + 1], fascia_r[c + 1][j + 1], fascia_r[c + 1][j]])

    # Rolled wheel-arch lips + dark recessed wheel wells
    for tag, wy in (('rear', REAR_Y), ('front', FRONT_Y)):
        arch_idxs = [i for i, s in enumerate(stations) if s[2] == tag]
        lip = [(side_grid[idx][0], bm_paint.verts.new((side_grid[idx][0].co.x - 0.030, side_grid[idx][0].co.y, side_grid[idx][0].co.z - 0.004))) for idx in arch_idxs]
        for k in range(len(lip) - 1):
            bm_paint.faces.new([lip[k][0], lip[k][1], lip[k + 1][1], lip[k + 1][0]])
        for sx in (1.0, -1.0):
            o_ring, i_ring = [], []
            ix = 0.45 * sx
            for idx in arch_idxs:
                co = side_grid[idx][0].co
                o_ring.append(bm_dark.verts.new(((co.x - 0.026) * sx, co.y, co.z - 0.002)))
                i_ring.append(bm_dark.verts.new((ix, co.y, co.z + 0.015)))
            for k in range(len(o_ring) - 1):
                verts = [o_ring[k], i_ring[k], i_ring[k + 1], o_ring[k + 1]] if sx > 0 else [o_ring[k], o_ring[k + 1], i_ring[k + 1], i_ring[k]]
                bm_dark.faces.new(verts)
            cv = bm_dark.verts.new((ix, wy, ROCKER_Z))
            for k in range(len(i_ring) - 1):
                verts = [i_ring[k], cv, i_ring[k + 1]] if sx > 0 else [i_ring[k], i_ring[k + 1], cv]
                bm_dark.faces.new(verts)

    # Dark underbody floor pan
    vu.add_box_bm(bm_dark, (0.0, -0.04, ROCKER_Z + 0.03), (1.42, 4.15, 0.05))


def add_side_panel_patch(bm, y_vals, z0, z1, sx, offset=0.008, n_z=4):
    """Build a surface-conforming curved patch on the van side (+X or -X)."""
    grid = []
    for y in y_vals:
        col = []
        for k in range(n_z + 1):
            z = z0 + (z1 - z0) * (k / n_z)
            # Clamp top z safely below sloped nose/windshield
            zt = min(z, van_top_z(y) - 0.06)
            x = (van_half_width(y, zt) + offset) * sx
            col.append(bm.verts.new((x, y, zt)))
        grid.append(col)
    for i in range(len(grid) - 1):
        for j in range(n_z):
            verts = (
                [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
                if sx > 0
                else [grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]]
            )
            bm.faces.new(verts)


def build_van_windows_and_stripes(bm_glass, bm_trim, bm_stripe):
    """Build windshield, cab windows, middle sliding window, rear blind panel frame, rear glass & blue stripes."""
    # 1. Panoramic Windshield (flush + black rubber surround on sloped cab face y=1.48..2.02)
    ws_us = [-0.88, -0.52, 0.0, 0.52, 0.88]
    ws_ys = [1.50, 1.68, 1.86, 2.02]
    for target_bm, u_scale, y_pad, n_off in ((bm_trim, 1.04, 0.024, 0.010), (bm_glass, 1.0, 0.0, 0.015)):
        grid = []
        for idx, y0 in enumerate(ws_ys):
            y = y0 - y_pad if idx == 0 else (y0 + y_pad if idx == len(ws_ys) - 1 else y0)
            z_base = van_top_z(y)
            hw = van_half_width(y, z_base) - 0.03
            row = []
            for u in ws_us:
                au = abs(u) * u_scale
                x = hw * clamp(au, 0.0, 0.96) * (1.0 if u >= 0 else -1.0)
                y_bow = y + 0.045 * smoothstep(1.80, Y_MAX, y) * (1.0 - au * au)
                z = z_base + 0.020 * (1.0 - au * au)
                # Offset outward along windshield normal (~(0, +0.78, +0.62))
                row.append(target_bm.verts.new((x, y_bow + n_off * 0.78, z + n_off * 0.62)))
            grid.append(row)
        for r in range(len(grid) - 1):
            for c in range(len(ws_us) - 1):
                target_bm.faces.new([grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1]])

    # 2. Rear Tailgate Window (flush + black gasket on rear y=-2.26)
    rw_xs = [-0.62, -0.32, 0.0, 0.32, 0.62]
    for target_bm, pad_x, z0, z1, y_off in ((bm_trim, 0.030, 1.18, 1.79, -0.010), (bm_glass, 0.0, 1.21, 1.76, -0.015)):
        r0, r1 = [], []
        for xv in rw_xs:
            sx = 1.0 if xv > 0 else (-1.0 if xv < 0 else 0.0)
            x = xv + pad_x * sx
            u = abs(x) / 0.78
            y_b = Y_MIN - 0.028 * (1.0 - u * u) + y_off
            r0.append(target_bm.verts.new((x, y_b, z0)))
            r1.append(target_bm.verts.new((x, y_b + 0.022, z1)))
        for c in range(len(rw_xs) - 1):
            target_bm.faces.new([r0[c], r1[c], r1[c + 1], r0[c + 1]])

    # 3. Side Windows, Rear Blind Panel Frame, Sliding Door Track & Blue Gbaka Stripes!
    for sx in (1.0, -1.0):
        # Front cab door window (sloped front edge matching windshield!)
        add_side_panel_patch(bm_trim,  [0.98, 1.38, 1.74, 1.88], 1.16, 1.81, sx, offset=0.008, n_z=3)
        add_side_panel_patch(bm_glass, [1.02, 1.38, 1.70, 1.83], 1.19, 1.77, sx, offset=0.013, n_z=3)

        # Middle sliding passenger window + vertical center divider bar
        add_side_panel_patch(bm_trim,  [-0.22, 0.32, 0.84], 1.17, 1.80, sx, offset=0.008, n_z=3)
        add_side_panel_patch(bm_glass, [-0.18, 0.32, 0.80], 1.20, 1.76, sx, offset=0.013, n_z=3)
        add_side_panel_patch(bm_trim,  [0.29, 0.34],        1.19, 1.77, sx, offset=0.017, n_z=2)

        # Rear blind cargo panel stamped outline (matches white blind side of reference HiAce!)
        add_side_panel_patch(bm_trim, [-2.06, -1.20, -0.34], 1.17, 1.195, sx, offset=0.007, n_z=1)
        add_side_panel_patch(bm_trim, [-2.06, -1.20, -0.34], 1.765, 1.79, sx, offset=0.007, n_z=1)
        add_side_panel_patch(bm_trim, [-2.06, -2.035],       1.17, 1.79, sx, offset=0.007, n_z=2)
        add_side_panel_patch(bm_trim, [-0.365, -0.34],       1.17, 1.79, sx, offset=0.007, n_z=2)

        # Sliding door recessed track channel along rear quarter
        add_side_panel_patch(bm_trim, [-2.12, -1.20, -0.30], 0.885, 0.918, sx, offset=0.010, n_z=1)

        # Signature Abidjan Gbaka light-blue horizontal side stripes!
        # Main lower-mid blue band (z = 0.82..0.93) + slim accent band above (z = 0.96..0.985)
        stripe_ys = [-2.18, -1.75, -1.20, -0.50, 0.25, 0.95, 1.55, 1.98, 2.18]
        add_side_panel_patch(bm_stripe, stripe_ys, 0.815, 0.915, sx, offset=0.005, n_z=2)
        add_side_panel_patch(bm_stripe, stripe_ys, 0.945, 0.972, sx, offset=0.005, n_z=1)

        # Door shutlines (cab door + sliding door)
        add_side_panel_patch(bm_trim, [0.915, 0.925],  0.29, 1.82, sx, offset=0.005, n_z=4)
        add_side_panel_patch(bm_trim, [-0.285, -0.275], 0.29, 1.82, sx, offset=0.005, n_z=4)

    # Front blue stripe wrap across nose (between headlights & windshield!)
    f_xs = [-0.78, -0.48, 0.0, 0.48, 0.78]
    for z0, z1 in ((0.815, 0.915), (0.945, 0.972)):
        r0, r1 = [], []
        for x in f_xs:
            u = abs(x) / 0.82
            yb = Y_MAX - 0.02 + 0.045 * (1.0 - u * u)
            r0.append(bm_stripe.verts.new((x, yb + 0.010, z0)))
            r1.append(bm_stripe.verts.new((x, yb - 0.025, z1)))
        for c in range(len(f_xs) - 1):
            bm_stripe.faces.new([r0[c], r0[c + 1], r1[c + 1], r1[c]])


def build_roof_rack_and_ladder(bm_rack, bm_trim):
    """Build signature tubular galvanized metal roof rack & rear left tailgate ladder."""
    # Roof rack spans y = -2.06 .. +0.62, x = +/-0.64, floor at z=2.01, top rail at z=2.12
    rx = 0.64
    y0, y1 = -2.06, 0.64
    z_bot, z_top = 2.015, 2.125
    r_tube = 0.013

    # Lower & upper closed rectangular tubular perimeter frames
    for z in (z_bot, z_top):
        pts = [
            (-rx, y0, z), (rx, y0, z), (rx, y1, z), (-rx, y1, z)
        ]
        vu.add_tube_path_bm(bm_rack, pts, r_tube, segments=8, closed=True)

    # 4 vertical corner posts + 6 mid-side posts + 8 roof mounting feet clamping into rain gutter
    for py in (y0, -1.25, -0.35, y1):
        for sx in (-1.0, 1.0):
            vu.add_cylinder_bm(bm_rack, (rx * sx, py, z_bot), (rx * sx, py, z_top), r_tube * 0.85, segments=6)
            # Mounting foot down to roof rain gutter
            vu.add_box_bm(bm_rack, ((rx + 0.03) * sx, py, 1.965), (0.045, 0.055, 0.115), (0, -0.22 * sx, 0), 0.004)
            vu.add_box_bm(bm_trim, ((rx + 0.045) * sx, py, 1.915), (0.035, 0.060, 0.032))

    # 9 transverse cargo slats across rack floor
    for k in range(9):
        sy = y0 + 0.12 + (y1 - y0 - 0.24) * (k / 8.0)
        vu.add_cylinder_bm(bm_rack, (-rx, sy, z_bot), (rx, sy, z_bot), r_tube * 0.72, segments=6)
    # 2 longitudinal center cargo bars
    for lx in (-0.24, 0.24):
        vu.add_cylinder_bm(bm_rack, (lx, y0, z_bot), (lx, y1, z_bot), r_tube * 0.65, segments=6)

    # Rear Tailgate Access Ladder (mounted on left rear, x = -0.50 .. -0.18, z = 0.45 .. 2.04!)
    lad_x0, lad_x1 = -0.52, -0.18
    lad_profile = [
        (-2.22, 0.46), (-2.34, 0.50), (-2.35, 1.25), (-2.30, 1.88), (-2.12, 2.02),
    ]
    for lx in (lad_x0, lad_x1):
        vu.add_tube_path_bm(bm_rack, [(lx, py, pz) for (py, pz) in lad_profile], 0.012, segments=6, closed=False)
    # 6 horizontal ladder rungs
    for rz, ry in ((0.64, -2.345), (0.90, -2.35), (1.16, -2.35), (1.42, -2.335), (1.68, -2.315), (1.92, -2.24)):
        vu.add_cylinder_bm(bm_rack, (lad_x0, ry, rz), (lad_x1, ry, rz), 0.010, segments=6)


def build_van_details(bm_trim, bm_dark, bm_chrome, bm_head, bm_tail, bm_amber, bm_rev):
    """Build black wraparound bumpers, square HiAce headlights, grille, vertical taillights, mirrors, handles, wipers."""
    # Front & rear wraparound black plastic bumpers
    def add_van_bumper(is_front):
        if is_front:
            ya, yc, yt = FRONT_Y + ARCH_R - 0.01, 2.18, 2.315
            z_top, z_bot = 0.565, 0.275
            hws, hwc = 0.86, 0.80
        else:
            ya, yc, yt = REAR_Y - ARCH_R + 0.01, -2.20, -2.325
            z_top, z_bot = 0.555, 0.295
            hws, hwc = 0.86, 0.82
        half = [(hws, ya), (hws, ya + (0.24 if is_front else -0.30)), (hwc, yc), (0.58, yt - (0.025 if is_front else -0.02)), (0.0, yt)]
        path = [(-x, y) for (x, y) in half[:-1]] + [(x, y) for (x, y) in reversed(half)]
        prof = [(-0.14, z_top - 0.01), (0.0, z_top), (0.025, z_top - 0.04), (0.018, (z_top + z_bot) * 0.5), (-0.04, z_bot), (-0.14, z_bot + 0.015)]
        rings = []
        for i, pt in enumerate(path):
            p = Vector(pt)
            p0, p1 = Vector(path[max(0, i - 1)]), Vector(path[min(len(path) - 1, i + 1)])
            t = (p1 - p0).normalized()
            nrm = Vector((-t.y, t.x))
            if nrm.dot(p) < 0:
                nrm = -nrm
            ring = []
            for off, z in prof:
                ws = 0.5 if (i == 0 or i == len(path) - 1) else 1.0
                xy = p + nrm * (off * ws if off < 0 else off)
                ring.append(bm_trim.verts.new((xy.x, xy.y, z)))
            rings.append(ring)
        for i in range(len(rings) - 1):
            for j in range(len(prof)):
                nj = (j + 1) % len(prof)
                bm_trim.faces.new([rings[i][j], rings[i + 1][j], rings[i + 1][nj], rings[i][nj]])
        bm_trim.faces.new(list(reversed(rings[0])))
        bm_trim.faces.new(rings[-1])

    add_van_bumper(True)
    add_van_bumper(False)

    # Front grille (classic HiAce dark rectangular grille between large square headlights)
    vu.add_box_bm(bm_dark,   (0.0, 2.265, 0.675), (0.88, 0.06, 0.175), bevel_width=0.008)
    for gz in (0.625, 0.660, 0.695, 0.730):
        vu.add_box_bm(bm_trim, (0.0, 2.295, gz), (0.84, 0.02, 0.014))
    vu.add_box_bm(bm_chrome, (0.0, 2.304, 0.678), (0.095, 0.016, 0.052), bevel_width=0.008)

    # Large square HiAce headlights + lower amber turn strips
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_trim,  (0.615 * sx, 2.238, 0.680), (0.31, 0.075, 0.195), (0, 0, -0.14 * sx), 0.007)
        vu.add_box_bm(bm_head,  (0.610 * sx, 2.262, 0.708), (0.28, 0.040, 0.125), (0, 0, -0.14 * sx), 0.008)
        vu.add_box_bm(bm_amber, (0.618 * sx, 2.260, 0.612), (0.28, 0.042, 0.052), (0, 0, -0.14 * sx), 0.006)
        # Wraparound corner marker
        vu.add_box_bm(bm_amber, (0.785 * sx, 2.135, 0.675), (0.065, 0.14, 0.165), (0, 0, -0.35 * sx), 0.008)

    # Vertical rectangular rear corner taillights (red lower, amber upper, reverse white middle!)
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_trim,  (0.755 * sx, -2.235, 0.735), (0.145, 0.115, 0.340), (0, 0, 0.18 * sx), 0.008)
        vu.add_box_bm(bm_tail,  (0.762 * sx, -2.255, 0.655), (0.125, 0.095, 0.155), (0, 0, 0.18 * sx), 0.006)
        vu.add_box_bm(bm_rev,   (0.762 * sx, -2.255, 0.760), (0.125, 0.095, 0.045), (0, 0, 0.18 * sx), 0.005)
        vu.add_box_bm(bm_amber, (0.762 * sx, -2.255, 0.835), (0.125, 0.095, 0.095), (0, 0, 0.18 * sx), 0.006)

    # Side mirrors (tall rectangular van mirrors!), door handles, wipers, exhaust
    for sx in (1.0, -1.0):
        vu.add_box_bm(bm_trim,   (0.88 * sx, 1.76, 1.24), (0.14, 0.04, 0.04), (0, 0, -0.15 * sx), 0.005)
        vu.add_box_bm(bm_trim,   (0.955 * sx, 1.75, 1.28), (0.085, 0.095, 0.195), (0, 0, -0.18 * sx), 0.014)
        vu.add_box_bm(bm_chrome, (0.955 * sx, 1.702, 1.28), (0.068, 0.010, 0.165), (0, 0, -0.18 * sx), 0.003)
        # Cab door handle & vertical sliding door handle
        vu.add_box_bm(bm_trim, (0.860 * sx, 1.04, 1.05), (0.032, 0.12, 0.036), bevel_width=0.005)
        vu.add_box_bm(bm_trim, (0.860 * sx, 0.78, 1.00), (0.032, 0.045, 0.125), bevel_width=0.005)

    # Front & rear wipers + exhaust
    for wx in (-0.30, 0.25):
        vu.add_box_bm(bm_trim, (wx, 2.085, 1.18), (0.50, 0.018, 0.016), (0.72, 0, 0.12), 0.003)
    vu.add_box_bm(bm_trim, (-0.06, -2.295, 1.22), (0.42, 0.018, 0.016), (0, 0.08, 0), 0.003)
    vu.add_cylinder_bm(bm_chrome, (0.54, -1.95, 0.265), (0.54, -2.33, 0.255), 0.028, segments=10)


def build_gbaka_hiace():
    vu.clear_scene()
    col = vu.ensure_collection('VH_Gbaka_HiAce')
    # Slightly weathered Abidjan white paint
    mats = vu.create_standard_vehicle_materials(paint_rgba=(0.82, 0.82, 0.79, 1.0))
    mat_stripe = vu.make_pbr_material('M_Gbaka_BandeBleue', base_color=(0.24, 0.36, 0.54, 1.0), metallic=0.05, roughness=0.35)
    mat_rack = vu.make_pbr_material('M_Galerie_Acier', base_color=(0.62, 0.64, 0.66, 1.0), metallic=0.80, roughness=0.36)

    root = bpy.data.objects.new('VH_Gbaka_HiAce', None)
    root.empty_display_type = 'PLAIN_AXES'
    vu.link_to_collection(root, col)

    bm_paint, bm_glass, bm_stripe = bmesh.new(), bmesh.new(), bmesh.new()
    bm_trim, bm_dark, bm_rack = bmesh.new(), bmesh.new(), bmesh.new()
    bm_chrome, bm_head, bm_tail, bm_amber, bm_rev = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    build_van_body(bm_paint, bm_dark)
    build_van_windows_and_stripes(bm_glass, bm_trim, bm_stripe)
    build_roof_rack_and_ladder(bm_rack, bm_trim)
    build_van_details(bm_trim, bm_dark, bm_chrome, bm_head, bm_tail, bm_amber, bm_rev)

    # Mirror + Subsurf Level 1 on van shell
    body_obj = vu.create_mesh_object('Carrosserie', bm_paint, mats['paint'], col, parent=root, angle_deg=None)
    mod_m = body_obj.modifiers.new('Mirror', 'MIRROR')
    mod_m.use_axis = (True, False, False)
    mod_m.use_clip = True
    mod_m.merge_threshold = 0.004
    bpy.context.view_layer.objects.active = body_obj
    body_obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier='Mirror')

    bm_c = bmesh.new()
    bm_c.from_mesh(body_obj.data)
    bmesh.ops.remove_doubles(bm_c, verts=bm_c.verts, dist=0.005)
    cl = bm_c.edges.layers.float.get('crease_edge') or bm_c.edges.layers.float.new('crease_edge')
    for e in bm_c.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(52.0):
            e[cl] = 0.55
        elif len(e.link_faces) == 1:
            e[cl] = 0.85
    bmesh.ops.recalc_face_normals(bm_c, faces=bm_c.faces)
    bm_c.to_mesh(body_obj.data)
    bm_c.free()

    mod_s = body_obj.modifiers.new('Subsurf', 'SUBSURF')
    mod_s.levels = 1
    mod_s.render_levels = 1
    bpy.ops.object.modifier_apply(modifier='Subsurf')
    vu.apply_smooth_with_sharp_angle(body_obj, angle_deg=46.0)

    # Simple dark interior cab & bench silhouette
    bm_int = bmesh.new()
    vu.add_box_bm(bm_int, (0.0, 1.72, 1.10), (1.36, 0.36, 0.24), bevel_width=0.02)
    for sx in (-1.0, 1.0):
        vu.add_box_bm(bm_int, (0.36 * sx, 1.12, 1.24), (0.44, 0.14, 0.56), (-0.12, 0, 0), 0.02)
        vu.add_box_bm(bm_int, (0.36 * sx, 1.08, 1.58), (0.22, 0.09, 0.15), bevel_width=0.02)
    vu.add_box_bm(bm_int, (0.0, 0.15, 1.18), (1.28, 0.15, 0.48), (-0.12, 0, 0), 0.02)
    vu.create_mesh_object('Interieur', bm_int, mats['trim_dark'], col, parent=root, angle_deg=45.0)

    vu.create_mesh_object('Livree_Bande', bm_stripe, mat_stripe, col, parent=root, angle_deg=46.0)
    vu.create_mesh_object('Vitres', bm_glass, mats['glass'], col, parent=root, angle_deg=48.0)
    vu.create_mesh_object('Galerie', bm_rack, mat_rack, col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Plastiques', vu.merge_bms_with_mats([bm_trim, bm_dark]), [mats['trim'], mats['trim_dark']], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Chrome', bm_chrome, mats['chrome'], col, parent=root, angle_deg=42.0)
    vu.create_mesh_object('Phares', bm_head, mats['headlight'], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Feux', vu.merge_bms_with_mats([bm_tail, bm_rev]), [mats['taillight'], mats['reverse']], col, parent=root, angle_deg=40.0)
    vu.create_mesh_object('Clignotants', bm_amber, mats['amber'], col, parent=root, angle_deg=40.0)

    base_wheel = vu.build_steel_wheel_mesh(
        'M_GbakaWheel', radius=WHEEL_R, width=WHEEL_W, rim_ratio=0.56,
        segments=18, hole_count=12, mats=[mats['tyre'], mats['steel_rim'], mats['brake']],
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
    out_dir = os.path.join(repo_root, 'tools', 'blender', 'output', 'gbaka_hiace')
    root = build_gbaka_hiace()
    vu.finalize_and_export_vehicle(root, out_dir, 'gbaka_hiace', 'VH_Gbaka_HiAce', lod1_ratio=0.40, samples=16, res=(768, 432))


if __name__ == '__main__':
    main()
