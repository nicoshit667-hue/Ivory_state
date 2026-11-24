"""Modular 3D Footwear (5 Male + 5 Female) and Accessories Generator for Ivory State.

Measures exact foot, ankle, wrist, finger, eye, ear, and collarbone geometry from the
finalized A-pose basemesh so every shoe, watch, ring, earring, necklace, hat, headwrap,
sunglasses, and bag fits cleanly, rigs to mixamo_unity, and avoids clipping.
"""

import math
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from char_builder import (
    _apply_all_modifiers,
    _get_coords_array,
    attach_modular_mesh,
    get_body_landmarks,
)
from char_materials import (
    create_fabric_material,
    create_glass_lens_material,
    create_leather_material,
    create_metal_material,
    create_wax_pagne_material,
)


MALE_SHOE_SPECS = [
    ("01_Urban_Sneakers",  "S01 Urban Sneakers"),
    ("02_Leather_Loafers", "S02 Leather Loafers"),
    ("03_Abidjan_Slides",  "S03 Abidjan Slides"),
    ("04_Wheat_Boots",     "S04 Wheat Boots"),
    ("05_Sport_Runners",   "S05 Sport Runners"),
]

FEMALE_SHOE_SPECS = [
    ("01_White_Sneakers",  "S01 White Sneakers"),
    ("02_Classic_Pumps",   "S02 Classic Pumps"),
    ("03_Strappy_Sandals", "S03 Strappy Sandals"),
    ("04_Ankle_Boots",     "S04 Ankle Boots"),
    ("05_Fitness_Runners", "S05 Fitness Runners"),
]

MALE_ACCESSORY_SPECS = [
    ("01_Aviator_Glasses", "A01 Aviators"),
    ("02_Snapback_Cap",    "A02 Snapback Cap"),
    ("03_Bucket_Hat",      "A03 Wax Bucket Hat"),
    ("04_Luxury_Watch",    "A04 Gold Watch"),
    ("05_Gold_Chain",      "A05 Cuban Chain"),
    ("06_Signet_Ring",     "A06 Signet Ring"),
    ("07_Crossbody_Bag",   "A07 Sling Bag"),
]

FEMALE_ACCESSORY_SPECS = [
    ("01_CatEye_Glasses",  "A01 Cat-Eye Glasses"),
    ("02_Wax_Headwrap",    "A02 Wax Headwrap"),
    ("03_Hoop_Earrings",   "A03 Hoop Earrings"),
    ("04_Beaded_Necklace", "A04 Collar Necklace"),
    ("05_Luxury_Watch",    "A05 Rose-Gold Watch"),
    ("06_Gold_Bangles",    "A06 Gold Bangles"),
    ("07_Gold_Ring",       "A07 Gold Ring"),
    ("08_Crossbody_Bag",   "A08 Leather Bag"),
]


# ===========================================================================
# FOOTWEAR BUILDER
# ===========================================================================
def _measure_foot(body_obj, side_sign):
    """Return exact (x_min, x_max, y_toe, y_heel, z_floor, x_center) for left (+1) or right (-1) foot."""
    co = _get_coords_array(body_obj.data)
    mask = (co[:, 2] < 0.11) & (co[:, 0] * side_sign > 0.015)
    pts = co[mask]
    if len(pts) < 10:
        cx = side_sign * 0.10
        return cx - 0.045, cx + 0.045, -0.16, 0.08, 0.0, cx
    x_min = float(pts[:, 0].min())
    x_max = float(pts[:, 0].max())
    y_toe = float(pts[:, 1].min())
    y_heel = float(pts[:, 1].max())
    z_floor = float(pts[:, 2].min())
    x_c = 0.5 * (x_min + x_max)
    return x_min, x_max, y_toe, y_heel, z_floor, x_c


def _add_shoe_mesh_for_foot(
    bm,
    x_min,
    x_max,
    y_toe,
    y_heel,
    z_floor,
    ankle_pt,
    style="sneaker",
    side_sign=1.0,
):
    """Build detailed sculpted shoe geometry (sole, upper, collar/heel/straps) for one foot."""
    pad_x = 0.0085
    pad_y_f = 0.014
    pad_y_b = 0.010

    cx = 0.5 * (x_min + x_max)
    hw = 0.5 * (x_max - x_min) + pad_x
    yf = y_toe - pad_y_f
    yb = y_heel + pad_y_b
    length = yb - yf
    cy = 0.5 * (yf + yb)

    sole_h = 0.022 if style in ("sneaker", "runner") else (0.024 if style in ("boot", "ankle_boot") else (0.016 if style == "slide" else 0.010))
    shaft_top = ankle_pt.z + (0.088 if style == "boot" else (0.072 if style == "ankle_boot" else (0.008 if style in ("sneaker", "runner") else -0.022)))

    # 1. Sculpted Sole / Midsole (material slot 1)
    segs_u = 28
    sole_rings = []
    for k in range(3):
        t = k / 2.0
        z = z_floor + t * sole_h
        expand = 1.0 + 0.05 * math.sin(t * math.pi)
        ring = []
        for s in range(segs_u):
            a = 2.0 * math.pi * s / segs_u
            ca = math.cos(a)
            sa = math.sin(a)
            w_mod = 1.0 + 0.08 * max(0.0, ca) - 0.14 * max(0.0, -ca) - 0.07 * math.exp(-((ca + 0.25) / 0.35) ** 2)
            if style in ("pump", "loafer") and ca > 0.45:
                w_mod *= (1.0 - 0.30 * ((ca - 0.45) / 0.55) ** 1.6)
            px = cx + sa * hw * w_mod * expand
            py = cy - ca * (length * 0.5) * expand
            # Elevated pump/ankle_boot arch under heel & midfoot!
            arch_lift = 0.0
            if style == "pump":
                arch_lift = 0.036 * max(0.0, min(1.0, (0.35 - ca) / 0.95))
            elif style == "ankle_boot":
                arch_lift = 0.022 * max(0.0, min(1.0, (0.30 - ca) / 0.95))
            elif style == "runner" and k == 0:
                arch_lift = 0.008 * max(0.0, ca - 0.65)
            ring.append(bm.verts.new((px, py, z + arch_lift)))
        sole_rings.append(ring)

    for k in range(2):
        r0, r1 = sole_rings[k], sole_rings[k + 1]
        for s in range(segs_u):
            sn = (s + 1) % segs_u
            f = bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
            f.smooth = True
            f.material_index = 1

    bot_c = bm.verts.new((cx, cy, z_floor + (0.015 if style == "pump" else 0.0)))
    top_c = bm.verts.new((cx, cy, z_floor + sole_h + (0.015 if style == "pump" else 0.0)))
    for s in range(segs_u):
        sn = (s + 1) % segs_u
        bm.faces.new((sole_rings[0][sn], sole_rings[0][s], bot_c)).material_index = 1
        bm.faces.new((sole_rings[2][s], sole_rings[2][sn], top_c)).material_index = 1

    if style in ("pump", "ankle_boot"):
        heel_h = 0.044 if style == "pump" else 0.030
        ret = bmesh.ops.create_cone(
            bm, cap_ends=True, segments=14,
            radius1=0.009 if style == "pump" else 0.022,
            radius2=0.017 if style == "pump" else 0.026,
            depth=heel_h,
            matrix=Matrix.Translation((cx, yb - 0.034, z_floor + heel_h * 0.5)),
        )
        vset = set(ret["verts"])
        for f in bm.faces:
            if all(v in vset for v in f.verts):
                f.smooth = True
                f.material_index = 1

    # 2. Shoe Upper (material slot 0, accent/collar slot 2)
    if style == "slide":
        y_s0 = yf + length * 0.24
        y_s1 = yf + length * 0.62
        strap_rings = []
        for iy in range(6):
            ty = iy / 5.0
            y = y_s0 * (1.0 - ty) + y_s1 * ty
            arch_h = 0.056 + 0.016 * ty
            ring = []
            for iu in range(13):
                tu = iu / 12.0
                ang = tu * math.pi
                px = cx - math.cos(ang) * (hw * 1.03)
                pz = z_floor + sole_h - 0.003 + math.sin(ang) * arch_h
                ring.append(bm.verts.new((px, y, pz)))
            strap_rings.append(ring)
        for iy in range(5):
            for iu in range(12):
                f = bm.faces.new((strap_rings[iy][iu], strap_rings[iy][iu + 1], strap_rings[iy + 1][iu + 1], strap_rings[iy + 1][iu]))
                f.smooth = True
                f.material_index = 2 if iy in (2, 3) else 0
        return

    if style == "sandal":
        for sy_frac, mat_i in [(0.24, 0), (0.44, 2)]:
            y_mid = yf + length * sy_frac
            prev_pts = None
            for dy in (-0.009, 0.009):
                pts = []
                for iu in range(11):
                    ang = (iu / 10.0) * math.pi
                    pts.append(bm.verts.new((cx - math.cos(ang) * hw * 1.01, y_mid + dy, z_floor + sole_h + math.sin(ang) * 0.044)))
                if dy < 0:
                    prev_pts = pts
                else:
                    for iu in range(10):
                        f = bm.faces.new((prev_pts[iu], prev_pts[iu + 1], pts[iu + 1], pts[iu]))
                        f.smooth = True
                        f.material_index = mat_i
        z_as = ankle_pt.z + 0.006
        r_a = 0.044
        r0 = [bm.verts.new((ankle_pt.x + math.sin(2 * math.pi * s / 20) * r_a, ankle_pt.y - math.cos(2 * math.pi * s / 20) * r_a, z_as - 0.008)) for s in range(20)]
        r1 = [bm.verts.new((ankle_pt.x + math.sin(2 * math.pi * s / 20) * r_a, ankle_pt.y - math.cos(2 * math.pi * s / 20) * r_a, z_as + 0.008)) for s in range(20)]
        for s in range(20):
            sn = (s + 1) % 20
            f = bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
            f.smooth = True
            f.material_index = 2
        # Vertical leather heel-cup strap connecting ankle strap cleanly down to heel footbed (zero floating!)
        hc0 = [bm.verts.new((cx + math.sin(a) * hw * 0.85, yb - 0.025 + math.cos(a) * 0.022, z_floor + sole_h - 0.002)) for a in (-0.9, -0.3, 0.3, 0.9)]
        hc1 = [bm.verts.new((ankle_pt.x + math.sin(a) * r_a, ankle_pt.y + math.cos(a) * r_a, z_as)) for a in (-0.9, -0.3, 0.3, 0.9)]
        for i in range(3):
            f = bm.faces.new((hc0[i], hc0[i + 1], hc1[i + 1], hc1[i]))
            f.smooth = True
            f.material_index = 0
        return

    n_tiers = 6
    upper_rings = []
    for k in range(n_tiers):
        t = k / (n_tiers - 1)
        ring = []
        for s in range(segs_u):
            a = 2.0 * math.pi * s / segs_u
            ca = math.cos(a)
            sa = math.sin(a)
            w_mod = 1.0 + 0.08 * max(0.0, ca) - 0.14 * max(0.0, -ca)
            if style in ("pump", "loafer") and ca > 0.45:
                w_mod *= (1.0 - 0.30 * ((ca - 0.45) / 0.55) ** 1.6)

            arch_lift = 0.0
            if style == "pump":
                arch_lift = 0.036 * max(0.0, min(1.0, (0.35 - ca) / 0.95))
            elif style == "ankle_boot":
                arch_lift = 0.022 * max(0.0, min(1.0, (0.30 - ca) / 0.95))

            px0 = cx + sa * hw * w_mod
            py0 = cy - ca * (length * 0.5)
            pz0 = z_floor + sole_h - 0.002 + arch_lift

            ankle_rx = 0.042 if style in ("boot", "ankle_boot") else 0.045
            ankle_ry = 0.048 if style in ("boot", "ankle_boot") else 0.064
            if style == "pump":
                ankle_ry = 0.078
            px1 = ankle_pt.x + sa * ankle_rx
            py1 = ankle_pt.y + 0.004 - ca * ankle_ry

            if style in ("boot", "ankle_boot"):
                pz1 = shaft_top + 0.006 * max(0.0, ca)
            elif style in ("sneaker", "runner"):
                pz1 = ankle_pt.z + 0.006 + 0.020 * max(0.0, ca) - 0.014 * (1.0 - abs(ca))
            elif style == "loafer":
                pz1 = ankle_pt.z - 0.018 + 0.014 * max(0.0, ca)
            else:
                pz1 = ankle_pt.z - 0.016 - 0.012 * (1.0 - abs(ca)) + 0.026 * max(0.0, -ca)

            s_xy = t ** (1.35 if ca > 0 else 0.95)
            s_z = math.sin(t * math.pi * 0.5)
            toe_doming = (0.018 if style in ("pump", "loafer") else 0.022) * math.sin(t * math.pi) * max(0.0, ca)
            px = px0 * (1.0 - s_xy) + px1 * s_xy
            py = py0 * (1.0 - s_xy) + py1 * s_xy
            pz = pz0 * (1.0 - s_z) + pz1 * s_z + toe_doming
            ring.append(bm.verts.new((px, py, pz)))
        upper_rings.append(ring)

    for k in range(n_tiers - 1):
        r0, r1 = upper_rings[k], upper_rings[k + 1]
        for s in range(segs_u):
            sn = (s + 1) % segs_u
            f = bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
            f.smooth = True
            a_mid = 2.0 * math.pi * (s + 0.5) / segs_u
            if (k == n_tiers - 2 and style in ("boot", "ankle_boot", "sneaker", "runner")) or (k in (2, 3) and abs(math.sin(a_mid)) > 0.72 and style == "runner"):
                f.material_index = 2
            elif k == n_tiers - 2 and math.cos(a_mid) > 0.5 and style == "loafer":
                f.material_index = 2
            else:
                f.material_index = 0

    # Add 5 crisp 3D lace bars across the vamp/instep for Sneakers, Runners & Boots
    if style in ("sneaker", "runner", "boot"):
        for li in range(5):
            tl = 0.28 + li * 0.13
            yl = (yf + length * 0.25) * (1.0 - tl) + (ankle_pt.y - 0.035) * tl
            zl = (z_floor + sole_h + 0.036) * (1.0 - tl) + (ankle_pt.z + (0.045 if style == "boot" else 0.012)) * tl
            wl = 0.028 * (1.0 - 0.15 * tl)
            _add_tube(bm, [Vector((cx - wl, yl, zl)), Vector((cx, yl - 0.005, zl + 0.005)), Vector((cx + wl, yl, zl))], 0.0028, segs=6, mat_idx=1 if style != "boot" else 2)


def _build_shoe_pair(name, char_info, collection, materials, style="sneaker"):
    """Create a rigged Left+Right shoe pair fitted cleanly to the character's exact feet."""
    body_obj = char_info["body"]
    lm = get_body_landmarks(char_info)
    bm = bmesh.new()

    for side_sign, ankle_key in [(1.0, "l_ankle"), (-1.0, "r_ankle")]:
        x_min, x_max, y_toe, y_heel, z_floor, _xc = _measure_foot(body_obj, side_sign)
        _add_shoe_mesh_for_foot(
            bm, x_min, x_max, y_toe, y_heel, z_floor, lm[ankle_key], style=style, side_sign=side_sign
        )

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for m in materials:
        obj.data.materials.append(m)

    sol = obj.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.0028 if style in ("slide", "sandal") else 0.0022
    sol.offset = 1.0
    _apply_all_modifiers(obj)

    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_",), k=4)
    return obj


def build_footwear(char_info, parent_col):
    """Build 5 distinct Male OR 5 distinct Female shoe pairs."""
    is_female = (char_info["gender"] == "female")
    prefix = "Female" if is_female else "Male"
    col = bpy.data.collections.new(f"Shoes_{prefix}")
    parent_col.children.link(col)

    m_white = create_leather_material("Shoe_WhiteLeather", (0.92, 0.92, 0.90), roughness=0.38)
    m_sole_w = create_fabric_material("Shoe_SoleWhite", (0.90, 0.90, 0.88), roughness=0.68, bump_strength=0.05)
    m_sole_dk = create_fabric_material("Shoe_SoleDark", (0.11, 0.11, 0.12), roughness=0.72, bump_strength=0.08)
    m_emerald = create_leather_material("Shoe_Emerald", (0.09, 0.42, 0.26), roughness=0.40)
    m_espresso = create_leather_material("Shoe_Espresso", (0.24, 0.12, 0.07), roughness=0.28)
    m_cognac = create_leather_material("Shoe_Cognac", (0.56, 0.30, 0.14), roughness=0.34)
    m_wheat = create_fabric_material("Shoe_WheatNubuck", (0.72, 0.50, 0.24), roughness=0.84, bump_strength=0.14)
    m_black = create_leather_material("Shoe_BlackLeather", (0.09, 0.09, 0.10), roughness=0.26)
    m_charc = create_fabric_material("Shoe_CharcoalKnit", (0.18, 0.19, 0.21), roughness=0.80)
    m_orange = create_fabric_material("Shoe_CI_Orange", (0.90, 0.42, 0.08), roughness=0.58)
    m_teal = create_fabric_material("Shoe_LagoonTeal", (0.12, 0.48, 0.52), roughness=0.68)
    m_gold = create_metal_material("Shoe_GoldTrim", (0.90, 0.74, 0.26), roughness=0.22)

    shoes = {}
    if not is_female:
        shoes["01_Urban_Sneakers"]  = _build_shoe_pair("Male_Shoe_01_Urban_Sneakers",  char_info, col, [m_white, m_sole_w, m_emerald], style="sneaker")
        shoes["02_Leather_Loafers"] = _build_shoe_pair("Male_Shoe_02_Leather_Loafers", char_info, col, [m_espresso, m_sole_dk, m_gold], style="loafer")
        shoes["03_Abidjan_Slides"]  = _build_shoe_pair("Male_Shoe_03_Abidjan_Slides",  char_info, col, [m_charc, m_sole_dk, m_orange], style="slide")
        shoes["04_Wheat_Boots"]     = _build_shoe_pair("Male_Shoe_04_Wheat_Boots",     char_info, col, [m_wheat, m_sole_dk, m_espresso], style="boot")
        shoes["05_Sport_Runners"]   = _build_shoe_pair("Male_Shoe_05_Sport_Runners",   char_info, col, [m_charc, m_sole_w, m_orange], style="runner")
    else:
        shoes["01_White_Sneakers"]  = _build_shoe_pair("Female_Shoe_01_White_Sneakers",  char_info, col, [m_white, m_sole_w, m_gold], style="sneaker")
        shoes["02_Classic_Pumps"]   = _build_shoe_pair("Female_Shoe_02_Classic_Pumps",   char_info, col, [m_black, m_sole_dk, m_gold], style="pump")
        shoes["03_Strappy_Sandals"] = _build_shoe_pair("Female_Shoe_03_Strappy_Sandals", char_info, col, [m_cognac, m_espresso, m_gold], style="sandal")
        shoes["04_Ankle_Boots"]     = _build_shoe_pair("Female_Shoe_04_Ankle_Boots",     char_info, col, [m_black, m_sole_dk, m_gold], style="ankle_boot")
        shoes["05_Fitness_Runners"] = _build_shoe_pair("Female_Shoe_05_Fitness_Runners", char_info, col, [m_teal, m_sole_w, m_white], style="runner")

    return shoes, col


# ===========================================================================
# MODULAR ACCESSORIES BUILDERS
# ===========================================================================
def _add_torus(bm, center, axis_vec, major_r, minor_r, segs_u=24, segs_v=10, mat_idx=0):
    axis = axis_vec.normalized()
    ref = Vector((1.0, 0.0, 0.0)) if abs(axis.x) < 0.85 else Vector((0.0, 1.0, 0.0))
    u_vec = (ref - axis * ref.dot(axis)).normalized()
    v_vec = axis.cross(u_vec).normalized()

    rings = []
    for i in range(segs_u):
        a = 2.0 * math.pi * i / segs_u
        rad = u_vec * math.cos(a) + v_vec * math.sin(a)
        c = center + rad * major_r
        ring = []
        for j in range(segs_v):
            b = 2.0 * math.pi * j / segs_v
            ring.append(bm.verts.new(c + (rad * math.cos(b) + axis * math.sin(b)) * minor_r))
        rings.append(ring)

    for i in range(segs_u):
        r0 = rings[i]
        r1 = rings[(i + 1) % segs_u]
        for j in range(segs_v):
            jn = (j + 1) % segs_v
            f = bm.faces.new((r0[j], r1[j], r1[jn], r0[jn]))
            f.smooth = True
            f.material_index = mat_idx


def _add_tube(bm, points, r_val, segs=8, mat_idx=0):
    if len(points) < 2:
        return
    rings = []
    prev_t = None
    prev_n = Vector((1.0, 0.0, 0.0))
    for i, p in enumerate(points):
        t = (points[1] - points[0]).normalized() if i == 0 else (
            (points[-1] - points[-2]).normalized() if i == len(points) - 1 else (points[i + 1] - points[i - 1]).normalized()
        )
        if prev_t is None:
            ref = Vector((0.0, 0.0, 1.0)) if abs(t.z) < 0.85 else Vector((1.0, 0.0, 0.0))
            n = (ref - t * ref.dot(t)).normalized()
        else:
            n = (prev_t.rotation_difference(t) @ prev_n).normalized()
        b = t.cross(n).normalized()
        prev_t, prev_n = t, n
        r = r_val[i] if isinstance(r_val, (list, tuple)) else r_val
        ring = [bm.verts.new(p + (n * math.cos(2 * math.pi * s / segs) + b * math.sin(2 * math.pi * s / segs)) * r) for s in range(segs)]
        rings.append(ring)
    for i in range(len(rings) - 1):
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((rings[i][s], rings[i][sn], rings[i + 1][sn], rings[i + 1][s]))
            f.smooth = True
            f.material_index = mat_idx


def _build_sunglasses(name, char_info, col, mat_frame, mat_lens, cat_eye=False):
    lm = get_body_landmarks(char_info)
    le, re = lm["l_eye"], lm["r_eye"]
    ez = 0.5 * (le.z + re.z)
    ey = min(le.y, re.y) - 0.024
    ex = abs(le.x)

    bm = bmesh.new()
    rx = 0.027
    rz = 0.020 if not cat_eye else 0.019

    for side in (-1.0, 1.0):
        cx = side * (ex + 0.003)
        segs = 22
        rim_pts = []
        lens_v = []
        for s in range(segs):
            a = 2.0 * math.pi * s / segs
            ca, sa = math.cos(a), math.sin(a)
            y_curve = ey + 0.005 * (ca ** 2)
            z_mod = rz * sa
            if not cat_eye and sa < 0:
                z_mod *= 1.18
            if cat_eye and (side * ca) > 0.25 and sa > 0.0:
                z_mod += 0.008 * abs(ca)
            p = Vector((cx + ca * rx, y_curve, ez + z_mod))
            rim_pts.append(p)
            lens_v.append(bm.verts.new(p))
        rim_pts.append(rim_pts[0])
        _add_tube(bm, rim_pts, 0.0022, segs=8, mat_idx=0)
        c_lens = bm.verts.new((cx, ey - 0.001, ez))
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((lens_v[s], lens_v[sn], c_lens))
            f.smooth = True
            f.material_index = 1

        x_hinge = cx + side * rx
        temple_pts = [
            Vector((x_hinge, ey + 0.004, ez + (0.008 if cat_eye else 0.004))),
            Vector((side * (ex + 0.044), ey + 0.032, ez + 0.007)),
            Vector((side * (ex + 0.048), ey + 0.088, ez + 0.005)),
            Vector((side * (ex + 0.044), ey + 0.112, ez - 0.018)),
        ]
        _add_tube(bm, temple_pts, 0.0020, segs=6, mat_idx=0)

    _add_tube(bm, [Vector((-0.012, ey + 0.002, ez + 0.004)), Vector((0.0, ey - 0.002, ez + 0.007)), Vector((0.012, ey + 0.002, ez + 0.004))], 0.0020, segs=6, mat_idx=0)
    if not cat_eye:
        _add_tube(bm, [Vector((-0.016, ey + 0.001, ez + 0.017)), Vector((0.0, ey - 0.002, ez + 0.018)), Vector((0.016, ey + 0.001, ez + 0.017))], 0.0018, segs=6, mat_idx=0)

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_frame)
    obj.data.materials.append(mat_lens)
    attach_modular_mesh(char_info, obj, rigid_bone="mixamorig:Head", transfer_shapekeys=False)
    return obj


def _build_snapback_cap(name, char_info, col, mat_cap, mat_brim):
    lm = get_body_landmarks(char_info)
    ez = lm["l_eye"].z
    ey = lm["l_eye"].y
    top_z = lm["head_top_z"]
    cy = ey + 0.090
    z_rim = ez + 0.048

    bm = bmesh.new()
    segs = 32
    rings = []
    for iv in range(8):
        t = iv / 7.0
        phi = t * (math.pi * 0.49)
        rx = 0.088 * math.cos(phi) + 0.004
        ry = 0.106 * math.cos(phi) + 0.004
        z = z_rim + (top_z - z_rim + 0.018) * math.sin(phi)
        ring = [bm.verts.new((math.sin(2 * math.pi * s / segs) * rx, cy - math.cos(2 * math.pi * s / segs) * ry, z)) for s in range(segs)]
        rings.append(ring)
    for iv in range(7):
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((rings[iv][s], rings[iv][sn], rings[iv + 1][sn], rings[iv + 1][s]))
            f.smooth = True
            f.material_index = 0
    c_top = bm.verts.new((0.0, cy, top_z + 0.021))
    for s in range(segs):
        sn = (s + 1) % segs
        bm.faces.new((rings[-1][s], rings[-1][sn], c_top)).material_index = 1

    brim_rings = []
    for ib in range(4):
        tb = ib / 3.0
        row = []
        for iu in range(15):
            a = -0.92 + (iu / 14.0) * 1.84
            rx_b = 0.088 + 0.012 * tb
            ry_b = 0.106 + 0.072 * tb
            px = math.sin(a) * rx_b
            py = cy - math.cos(a) * ry_b
            pz = z_rim - 0.008 * tb + 0.006 * math.cos(a)
            row.append(bm.verts.new((px, py, pz)))
        brim_rings.append(row)
    for ib in range(3):
        for iu in range(14):
            f = bm.faces.new((brim_rings[ib][iu], brim_rings[ib][iu + 1], brim_rings[ib + 1][iu + 1], brim_rings[ib + 1][iu]))
            f.smooth = True
            f.material_index = 1

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_cap)
    obj.data.materials.append(mat_brim)
    sol = obj.modifiers.new("Thick", "SOLIDIFY"); sol.thickness = 0.0032; sol.offset = 1.0
    _apply_all_modifiers(obj)
    attach_modular_mesh(char_info, obj, rigid_bone="mixamorig:Head", transfer_shapekeys=False)
    return obj


def _build_bucket_hat(name, char_info, col, mat_wax, mat_band):
    lm = get_body_landmarks(char_info)
    ez = lm["l_eye"].z
    ey = lm["l_eye"].y
    top_z = lm["head_top_z"]
    cy = ey + 0.090
    z_rim = ez + 0.044

    bm = bmesh.new()
    segs = 32
    profile = [
        (0.140, 0.158, z_rim - 0.032, 0),
        (0.112, 0.130, z_rim - 0.012, 0),
        (0.088, 0.106, z_rim,         1),
        (0.085, 0.102, z_rim + 0.025, 1),
        (0.076, 0.092, top_z + 0.018, 0),
        (0.052, 0.064, top_z + 0.022, 0),
    ]
    rings = []
    for rx, ry, z, _mi in profile:
        rings.append([bm.verts.new((math.sin(2 * math.pi * s / segs) * rx, cy - math.cos(2 * math.pi * s / segs) * ry, z)) for s in range(segs)])
    for i in range(len(rings) - 1):
        mi = profile[i][3]
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((rings[i][s], rings[i][sn], rings[i + 1][sn], rings[i + 1][s]))
            f.smooth = True
            f.material_index = mi
    c_top = bm.verts.new((0.0, cy, top_z + 0.022))
    for s in range(segs):
        sn = (s + 1) % segs
        bm.faces.new((rings[-1][s], rings[-1][sn], c_top)).material_index = 0

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_wax)
    obj.data.materials.append(mat_band)
    sol = obj.modifiers.new("Thick", "SOLIDIFY"); sol.thickness = 0.0028; sol.offset = 1.0
    _apply_all_modifiers(obj)
    attach_modular_mesh(char_info, obj, rigid_bone="mixamorig:Head", transfer_shapekeys=False)
    return obj


def _build_wax_headwrap(name, char_info, col, mat_wax, mat_gold):
    """Build an authentic West African sculpted Wax Headwrap / Foulard with layered turban folds & front twist knot."""
    lm = get_body_landmarks(char_info)
    ez = lm["l_eye"].z
    ey = lm["l_eye"].y
    top_z = lm["head_top_z"]
    cy = ey + 0.096
    z_base = ez + 0.044

    bm = bmesh.new()
    segs = 36
    rings = []
    for iv in range(10):
        t = iv / 9.0
        ang_v = t * (math.pi * 0.62)
        pleat = 1.0 + 0.045 * math.sin(t * math.pi * 7.0)
        rx = (0.090 + 0.026 * math.sin(t * math.pi * 0.9)) * math.cos(ang_v * 0.72) * pleat
        ry = (0.108 + 0.032 * math.sin(t * math.pi * 0.9)) * math.cos(ang_v * 0.72) * pleat
        z = z_base + (top_z - z_base + 0.072) * math.sin(ang_v)
        ring = []
        for s in range(segs):
            a = 2.0 * math.pi * s / segs
            fold_z = 0.009 * math.sin(a * 2.0 + t * 4.5)
            ring.append(bm.verts.new((math.sin(a) * rx, cy - math.cos(a) * ry, z + fold_z)))
        rings.append(ring)

    for iv in range(9):
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((rings[iv][s], rings[iv][sn], rings[iv + 1][sn], rings[iv + 1][s]))
            f.smooth = True
            f.material_index = 0

    c_top = bm.verts.new((0.0, cy + 0.008, top_z + 0.074))
    for s in range(segs):
        sn = (s + 1) % segs
        bm.faces.new((rings[-1][s], rings[-1][sn], c_top)).material_index = 0

    knot_c = Vector((0.0, ey + 0.002, ez + 0.088))
    _add_torus(bm, knot_c, Vector((0.0, -1.0, 0.25)), major_r=0.026, minor_r=0.015, segs_u=18, segs_v=10, mat_idx=0)
    _add_torus(bm, knot_c + Vector((0.032, 0.004, -0.005)), Vector((0.4, -0.9, 0.1)), major_r=0.010, minor_r=0.003, segs_u=14, segs_v=6, mat_idx=1)

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_wax)
    obj.data.materials.append(mat_gold)
    sol = obj.modifiers.new("Thick", "SOLIDIFY"); sol.thickness = 0.0026; sol.offset = 1.0
    _apply_all_modifiers(obj)
    attach_modular_mesh(char_info, obj, rigid_bone="mixamorig:Head", transfer_shapekeys=False)
    return obj


def _build_wristwatch(name, char_info, col, mat_metal, mat_dial, side="l"):
    """Build a sculpted luxury wristwatch oriented strictly perpendicular to the A-pose forearm axis."""
    lm = get_body_landmarks(char_info)
    el = lm[f"{side}_elbow"]
    wr = lm[f"{side}_wrist"]
    u_arm = (wr - el).normalized()
    w_center = wr - u_arm * 0.020

    co = _get_coords_array(char_info["body"].data)
    rel = co - np.array(w_center)
    proj = rel @ np.array(u_arm)
    perp = rel - proj[:, None] * np.array(u_arm)[None, :]
    r_dist = np.linalg.norm(perp, axis=1)
    # CRITICAL: filter locally within 6.5cm of the wrist center so infinite plane never grabs torso/hips!
    near = r_dist[(np.abs(proj) < 0.015) & (r_dist < 0.065)]
    wrist_r = float(np.percentile(near, 92)) + 0.0032 if len(near) > 8 else 0.030
    wrist_r = max(0.024, min(0.036, wrist_r))

    bm = bmesh.new()
    _add_torus(bm, w_center, u_arm, major_r=wrist_r, minor_r=0.0042, segs_u=24, segs_v=8, mat_idx=0)

    dorsal = Vector((0.0, 0.25, 1.0))
    dorsal = (dorsal - u_arm * dorsal.dot(u_arm)).normalized()
    case_c = w_center + dorsal * (wrist_r + 0.003)
    quat = Vector((0.0, 0.0, 1.0)).rotation_difference(dorsal)
    xf = Matrix.Translation(case_c) @ quat.to_matrix().to_4x4()
    ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=22, radius1=0.019, radius2=0.018, depth=0.008, matrix=xf)
    vset = set(ret["verts"])
    for f in bm.faces:
        if all(v in vset for v in f.verts):
            f.smooth = True
            f.material_index = 0
    xf_d = Matrix.Translation(case_c + dorsal * 0.0045) @ quat.to_matrix().to_4x4()
    ret_d = bmesh.ops.create_cone(bm, cap_ends=True, segments=22, radius1=0.015, radius2=0.015, depth=0.0018, matrix=xf_d)
    vset_d = set(ret_d["verts"])
    for f in bm.faces:
        if all(v in vset_d for v in f.verts):
            f.smooth = True
            f.material_index = 1

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_metal)
    obj.data.materials.append(mat_dial)
    bone = "mixamorig:LeftForeArm" if side == "l" else "mixamorig:RightForeArm"
    attach_modular_mesh(char_info, obj, rigid_bone=bone, transfer_shapekeys=False)
    return obj


def _build_bangles(name, char_info, col, mat_gold, side="r"):
    """Build 3 stacked polished gold wrist bangles on Right wrist."""
    lm = get_body_landmarks(char_info)
    el, wr = lm[f"{side}_elbow"], lm[f"{side}_wrist"]
    u_arm = (wr - el).normalized()
    w_center = wr - u_arm * 0.025
    co = _get_coords_array(char_info["body"].data)
    rel = co - np.array(w_center)
    proj = rel @ np.array(u_arm)
    perp = rel - proj[:, None] * np.array(u_arm)[None, :]
    r_dist = np.linalg.norm(perp, axis=1)
    near = r_dist[(np.abs(proj) < 0.018) & (r_dist < 0.065)]
    wrist_r = float(np.percentile(near, 92)) + 0.0055 if len(near) > 8 else 0.030
    wrist_r = max(0.025, min(0.038, wrist_r))

    bm = bmesh.new()
    for off in (-0.012, 0.0, 0.012):
        _add_torus(bm, w_center + u_arm * off, u_arm, major_r=wrist_r, minor_r=0.0032, segs_u=24, segs_v=8, mat_idx=0)
    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_gold)
    bone = "mixamorig:RightForeArm" if side == "r" else "mixamorig:LeftForeArm"
    attach_modular_mesh(char_info, obj, rigid_bone=bone, transfer_shapekeys=False)
    return obj


def _build_ring(name, char_info, col, mat_gold, is_signet=False, side="l"):
    """Build a polished gold ring fitted to Left Ring Finger."""
    rig = char_info["rig"]
    bname = "mixamorig:LeftHandRing1" if side == "l" else "mixamorig:RightHandRing1"
    b = rig.data.bones.get(bname)
    if b is None:
        return None
    p0 = rig.matrix_world @ b.head_local
    p1 = rig.matrix_world @ b.tail_local
    u_fin = (p1 - p0).normalized()
    rc = p0.lerp(p1, 0.48)

    bm = bmesh.new()
    rad = 0.0102 if is_signet else 0.0086
    _add_torus(bm, rc, u_fin, major_r=rad, minor_r=0.0024 if is_signet else 0.0018, segs_u=20, segs_v=8, mat_idx=0)
    if is_signet:
        dorsal = Vector((0.0, 0.3, 1.0))
        dorsal = (dorsal - u_fin * dorsal.dot(u_fin)).normalized()
        ret = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.0045)
        for v in ret["verts"]:
            v.co = rc + dorsal * (rad + 0.0018) + Vector((v.co.x * 1.1, v.co.y * 0.7, v.co.z * 1.2))
    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_gold)
    attach_modular_mesh(char_info, obj, rigid_bone=bname, transfer_shapekeys=False)
    return obj


def _build_necklace(name, char_info, col, mat_main, mat_accent=None, beaded=False):
    """Build a draped gold Cuban chain or layered West African beaded collar necklace clearing the chest."""
    body_obj = char_info["body"]
    lm = get_body_landmarks(char_info)
    neck = lm["neck"]
    deps = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(body_obj, deps)

    bm = bmesh.new()
    n_pts = 44
    tiers = [(0.115, 0.0055, 0)] if not beaded else [(0.088, 0.0070, 0), (0.125, 0.0080, 1)]
    for drop, r_tube, m_idx in tiers:
        pts = []
        for i in range(n_pts):
            a = 2.0 * math.pi * i / n_pts
            ca = math.cos(a)
            sa = math.sin(a)
            rx = 0.080 + 0.012 * max(0.0, ca)
            ry = 0.082 + 0.016 * max(0.0, ca)
            z = neck.z - 0.025 - drop * (max(0.0, (ca + 0.35) / 1.35) ** 1.4)
            guess = Vector((sa * rx, neck.y + 0.005 - ca * ry, z))
            loc, norm, _idx, _d = bvh.find_nearest(guess)
            if loc is not None:
                p = loc + norm * (r_tube + 0.005)
            else:
                p = guess
            pts.append(p)
        pts.append(pts[0])
        if not beaded:
            _add_tube(bm, pts, r_tube, segs=8, mat_idx=m_idx)
        else:
            for k in range(n_pts):
                if k % 2 == 0:
                    ret = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r_tube * (1.25 if k % 6 == 0 else 0.92))
                    for v in ret["verts"]:
                        v.co += pts[k]
                    vset = set(ret["verts"])
                    for f in bm.faces:
                        if all(v in vset for v in f.verts):
                            f.smooth = True
                            f.material_index = 1 if (k % 4 == 0 and mat_accent is not None) else 0

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_main)
    if mat_accent is not None:
        obj.data.materials.append(mat_accent)
    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_",), k=4)
    return obj


def _build_hoop_earrings(name, char_info, col, mat_gold):
    """Build polished medium-large gold hoop earrings hanging cleanly from both earlobes."""
    lm = get_body_landmarks(char_info)
    co = _get_coords_array(char_info["body"].data)
    ez, ey = lm["l_eye"].z, lm["l_eye"].y

    bm = bmesh.new()
    for side in (-1.0, 1.0):
        ear_pts = co[
            (co[:, 0] * side > 0.068)
            & (co[:, 2] > ez - 0.065) & (co[:, 2] < ez - 0.015)
            & (co[:, 1] > ey + 0.045) & (co[:, 1] < ey + 0.115)
        ]
        if len(ear_pts) > 4:
            lobe_x = float(np.percentile(ear_pts[:, 0] * side, 88)) * side
            lobe_y = float(np.median(ear_pts[:, 1]))
            lobe_z = float(np.percentile(ear_pts[:, 2], 12))
        else:
            lobe_x, lobe_y, lobe_z = side * 0.082, ey + 0.075, ez - 0.036

        hoop_r = 0.022
        center = Vector((lobe_x + side * 0.004, lobe_y, lobe_z - hoop_r + 0.004))
        _add_torus(bm, center, Vector((side * 0.92, -0.38, 0.0)), major_r=hoop_r, minor_r=0.0022, segs_u=24, segs_v=8, mat_idx=0)

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_gold)
    attach_modular_mesh(char_info, obj, rigid_bone="mixamorig:Head", transfer_shapekeys=False)
    return obj


def _build_crossbody_bag(name, char_info, col, mat_leather, mat_metal, is_female=False):
    """Build a stylish diagonal crossbody sling pack (Male) or designer leather crossbody handbag (Female)."""
    body_obj = char_info["body"]
    lm = get_body_landmarks(char_info)
    h = lm["height"]
    co = _get_coords_array(body_obj.data)
    # Torso-only point cloud (excludes A-pose outstretched arms completely!)
    torso = co[(np.abs(co[:, 0]) < 0.205) & (co[:, 2] > h * 0.48) & (co[:, 2] < h * 0.86)]

    bm = bmesh.new()
    n_strap = 44
    strap_pts = []
    for i in range(n_strap):
        a = 2.0 * math.pi * i / n_strap  # 0..pi front diagonal, pi..2pi back diagonal
        ca, sa = math.cos(a), math.sin(a)
        t_diag = 0.5 * (1.0 - ca)  # 0 at L shoulder, 1 at R hip
        px = 0.088 * (1.0 - t_diag) + (-0.165) * t_diag
        pz = (h * 0.828) * (1.0 - t_diag) + (h * 0.530) * t_diag

        # Sample exact front (-Y) or back (+Y) torso depth near (px, pz)
        near = torso[(np.abs(torso[:, 0] - px) < 0.035) & (np.abs(torso[:, 2] - pz) < 0.035)]
        if len(near) > 3:
            if sa >= 0.0:
                py = float(np.percentile(near[:, 1], 5)) - 0.018
            else:
                py = float(np.percentile(near[:, 1], 95)) + 0.018
        else:
            py = -0.145 if sa >= 0.0 else 0.145

        # Smooth transition over shoulder top (t_diag near 0) and right hip edge (t_diag near 1)
        if t_diag < 0.08:
            pz += 0.018 * (1.0 - t_diag / 0.08)
        elif t_diag > 0.92:
            px -= 0.018 * ((t_diag - 0.92) / 0.08)
        strap_pts.append(Vector((px, py, pz)))

    # Smooth strap polyline slightly (2 passes) so it flows effortlessly like real leather/chain
    for _ in range(2):
        sm = []
        for i in range(n_strap):
            sm.append(0.25 * strap_pts[(i - 1) % n_strap] + 0.50 * strap_pts[i] + 0.25 * strap_pts[(i + 1) % n_strap])
        strap_pts = sm
    strap_pts.append(strap_pts[0])
    _add_tube(bm, strap_pts, 0.009 if not is_female else 0.0045, segs=8, mat_idx=1 if is_female else 0)

    if not is_female:
        # Chest/midriff urban sling pack resting cleanly on front strap
        bag_c = strap_pts[11] + Vector((0.0, -0.022, -0.01))
        sx, sy, sz = 0.135, 0.052, 0.195
    else:
        # Chic designer leather crossbody purse resting neatly against Right Hip!
        bag_c = strap_pts[22] + Vector((-0.032, -0.010, -0.068))
        sx, sy, sz = 0.062, 0.165, 0.130

    ret = bmesh.ops.create_cube(bm, size=1.0)
    bverts = ret["verts"]
    for v in bverts:
        v.co = bag_c + Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
    bmesh.ops.bevel(bm, geom=list({e for v in bverts for e in v.link_edges}), offset=0.012, segments=2, affect="EDGES")
    for f in bm.faces:
        if f.calc_center_median().z < bag_c.z + sz * 0.6 and (f.calc_center_median() - bag_c).length < 0.22:
            f.smooth = True
            f.material_index = 0

    ret_c = bmesh.ops.create_cube(bm, size=1.0)
    for v in ret_c["verts"]:
        off_clasp = Vector((0.0, -sy * 0.52, 0.02)) if not is_female else Vector((-sx * 0.52, 0.0, 0.01))
        v.co = bag_c + off_clasp + Vector((v.co.x * 0.024, v.co.y * 0.014, v.co.z * 0.024))
    vset_c = set(ret_c["verts"])
    for f in bm.faces:
        if all(v in vset_c for v in f.verts):
            f.material_index = 1

    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); col.objects.link(obj)
    obj.data.materials.append(mat_leather)
    obj.data.materials.append(mat_metal)
    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_",), k=4)
    return obj


def build_accessories(char_info, parent_col):
    """Build complete modular accessories pack for Male or Female."""
    is_female = (char_info["gender"] == "female")
    prefix = "Female" if is_female else "Male"
    col = bpy.data.collections.new(f"Accessories_{prefix}")
    parent_col.children.link(col)

    m_gold = create_metal_material("Acc_Gold", (0.92, 0.74, 0.26), roughness=0.18)
    m_rosegold = create_metal_material("Acc_RoseGold", (0.90, 0.62, 0.50), roughness=0.20)
    m_lens = create_glass_lens_material("Acc_DarkLens", (0.05, 0.06, 0.08))
    m_cap_blk = create_fabric_material("Acc_CapBlack", (0.10, 0.11, 0.12), roughness=0.82)
    m_cap_org = create_fabric_material("Acc_CapOrange", (0.90, 0.42, 0.08), roughness=0.76)
    m_wax_abj = create_wax_pagne_material("Acc_WaxAbidjan", palette="abidjan")
    m_wax_kita = create_wax_pagne_material("Acc_WaxKita", palette="kita")
    m_navy = create_fabric_material("Acc_NavyBand", (0.09, 0.14, 0.30), roughness=0.78)
    m_leather_blk = create_leather_material("Acc_LeatherBlk", (0.10, 0.10, 0.11), roughness=0.34)
    m_leather_cog = create_leather_material("Acc_LeatherCognac", (0.56, 0.28, 0.12), roughness=0.32)
    m_bead_terra = create_fabric_material("Acc_BeadTerracotta", (0.76, 0.26, 0.12), roughness=0.30)

    accs = {}
    if not is_female:
        accs["01_Aviator_Glasses"] = _build_sunglasses("Male_Acc_01_Aviator_Glasses", char_info, col, m_gold, m_lens, cat_eye=False)
        accs["02_Snapback_Cap"]    = _build_snapback_cap("Male_Acc_02_Snapback_Cap", char_info, col, m_cap_blk, m_cap_org)
        accs["03_Bucket_Hat"]      = _build_bucket_hat("Male_Acc_03_Bucket_Hat", char_info, col, m_wax_abj, m_navy)
        accs["04_Luxury_Watch"]    = _build_wristwatch("Male_Acc_04_Luxury_Watch", char_info, col, m_gold, m_lens, side="l")
        accs["05_Gold_Chain"]      = _build_necklace("Male_Acc_05_Gold_Chain", char_info, col, m_gold, beaded=False)
        accs["06_Signet_Ring"]     = _build_ring("Male_Acc_06_Signet_Ring", char_info, col, m_gold, is_signet=True, side="l")
        accs["07_Crossbody_Bag"]   = _build_crossbody_bag("Male_Acc_07_Crossbody_Bag", char_info, col, m_leather_blk, m_gold, is_female=False)
    else:
        accs["01_CatEye_Glasses"]  = _build_sunglasses("Female_Acc_01_CatEye_Glasses", char_info, col, m_gold, m_lens, cat_eye=True)
        accs["02_Wax_Headwrap"]    = _build_wax_headwrap("Female_Acc_02_Wax_Headwrap", char_info, col, m_wax_kita, m_gold)
        accs["03_Hoop_Earrings"]   = _build_hoop_earrings("Female_Acc_03_Hoop_Earrings", char_info, col, m_gold)
        accs["04_Beaded_Necklace"] = _build_necklace("Female_Acc_04_Beaded_Necklace", char_info, col, m_gold, m_bead_terra, beaded=True)
        accs["05_Luxury_Watch"]    = _build_wristwatch("Female_Acc_05_Luxury_Watch", char_info, col, m_rosegold, m_lens, side="l")
        accs["06_Gold_Bangles"]    = _build_bangles("Female_Acc_06_Gold_Bangles", char_info, col, m_gold, side="r")
        accs["07_Gold_Ring"]       = _build_ring("Female_Acc_07_Gold_Ring", char_info, col, m_gold, is_signet=False, side="l")
        accs["08_Crossbody_Bag"]   = _build_crossbody_bag("Female_Acc_08_Crossbody_Bag", char_info, col, m_leather_cog, m_gold, is_female=True)

    return accs, col
