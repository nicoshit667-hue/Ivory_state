"""Modular 3D Wardrobe Generator for Ivory State (10 Male Outfits + 10 Female Outfits).

Every outfit is tailored directly onto the finalized A-pose character basemesh with:
  - Proper multi-layer clearance (inner tops -> bottoms -> outer jackets/blazers) so layers never clip
  - Perpendicular arm-axis sleeve cuts, 3D collars, lapels, hoods, belts, cargo pockets & real skirt/dress hulls
  - Procedural PBR fabrics including West African Wax / Ankara / Kita pagne prints, denim, leather, linen, wool
  - Full mixamo_unity skinning weights and synchronized Body Shape Keys (Body_*, Bust_Adjust, Hip_Adjust).
"""

import math
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

from char_builder import (
    _apply_all_modifiers,
    _create_bridged_fit_target,
    _finalize_garment_modifiers,
    _get_coords_array,
    _smooth_garment_boundaries,
    attach_modular_mesh,
    get_body_landmarks,
)
from char_materials import (
    create_fabric_material,
    create_leather_material,
    create_metal_material,
    create_wax_pagne_material,
)


MALE_OUTFIT_SPECS = [
    ("01_Abidjan_Streetwear", "O01 Streetwear"),
    ("02_Plateau_Business",   "O02 Business Suit"),
    ("03_Cocody_SmartCasual", "O03 Smart Polo"),
    ("04_Wax_Dashiki_Set",    "O04 Wax Tunic"),
    ("05_Nightlife_Biker",    "O05 Biker Leather"),
    ("06_Lagune_Sport",       "O06 Sport Hoodie"),
    ("07_GrandBassam_Resort", "O07 Resort Linen"),
    ("08_Kita_Royal_Boubou",  "O08 Kita Boubou"),
    ("09_Treichville_Denim",  "O09 Denim Jacket"),
    ("10_Gym_Elephants",      "O10 Elephants Kit"),
]

FEMALE_OUTFIT_SPECS = [
    ("01_Abidjan_Wax_Dress",   "O01 Wax Midi Dress"),
    ("02_Plateau_Executive",   "O02 Boss Blazer"),
    ("03_Cocody_Chic_Crop",    "O03 Chic Denim"),
    ("04_Kita_Royal_Gown",     "O04 Kita Gown"),
    ("05_Lagune_Athleisure",   "O05 Athleisure"),
    ("06_GrandBassam_Resort",  "O06 Summer Dress"),
    ("07_Marcory_Biker_Night", "O07 Night Moto"),
    ("08_Wax_Pagne_Ensemble",  "O08 Pagne & Blouse"),
    ("09_Street_Hoodie_Cargo", "O09 Urban Hoodie"),
    ("10_Smart_Jumpsuit",      "O10 Emerald Suit"),
]


def _create_relaxed_fit_target(body_obj, max_z, collection, is_female=False, inflate=0.0):
    """Build a smooth garment-draping fit hull bridging waist/chest/glutes with relaxed midsection ease."""
    base_fit = _create_bridged_fit_target(body_obj, max_z, collection, compress_bust=is_female)
    bm = bmesh.new()
    bm.from_mesh(base_fit.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    # Smooth torso belly/waist/back hollows for natural relaxed cloth drape
    torso_verts = [
        v for v in bm.verts
        if abs(v.co.x) < 0.22 and max_z * 0.48 < v.co.z < max_z * 0.84
    ]
    for _ in range(12):
        bmesh.ops.smooth_vert(bm, verts=torso_verts, factor=0.58, use_axis_x=True, use_axis_y=True, use_axis_z=False)

    bm.normal_update()
    for v in bm.verts:
        extra = inflate
        # Add subtle natural waist/midsection fabric ease on relaxed Male shirts/hoodies/jackets
        if not is_female and abs(v.co.x) < 0.21 and max_z * 0.50 < v.co.z < max_z * 0.72:
            t_w = math.sin(((v.co.z - max_z * 0.50) / (max_z * 0.22)) * math.pi)
            extra += 0.009 * t_w
        if extra > 0.0:
            v.co += v.normal * extra

    bm.to_mesh(base_fit.data)
    bm.free()
    base_fit.data.update()
    return base_fit


def _bisect_arm_sleeves(bm, lm, sleeve_mode):
    """Trim left and right arms cleanly with planes perpendicular to the A-pose arm bone axis."""
    if sleeve_mode == "full_body":
        return

    for side in ("l", "r"):
        sh = lm[f"{side}_shoulder"]
        el = lm[f"{side}_elbow"]
        wr = lm[f"{side}_wrist"]
        sx = 1.0 if side == "l" else -1.0

        u_upper = (el - sh).normalized()
        u_lower = (wr - el).normalized()

        if sleeve_mode == "none":
            # Sleeveless cut right around shoulder cap
            cut_pt = sh + u_upper * 0.012
            cut_no = Vector((sx * 0.96, 0.0, -0.28)).normalized()
        elif sleeve_mode == "short":
            cut_pt = sh.lerp(el, 0.56)
            cut_no = u_upper
        elif sleeve_mode == "three_quarter":
            cut_pt = el.lerp(wr, 0.38)
            cut_no = u_lower
        elif sleeve_mode == "long":
            cut_pt = el.lerp(wr, 0.90)
            cut_no = u_lower
        else:
            continue

        # Only bisect arm vertices on this side (strictly lateral to torso!)
        arm_verts = [
            v for v in bm.verts
            if (v.co.x * sx) > (abs(sh.x) - 0.025) and v.co.z < lm["neck"].z + 0.04
        ]
        if not arm_verts:
            continue
        vset = set(arm_verts)
        edges = [e for e in bm.edges if e.verts[0] in vset or e.verts[1] in vset]
        faces = [f for f in bm.faces if any(v in vset for v in f.verts)]
        geom = arm_verts + edges + faces
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=cut_pt, plane_no=cut_no, clear_outer=True)


def _add_collar_band(bm, neck_pt, h, radius_x, radius_y, collar_h, mat_idx=0, open_front=False, fold_down=False):
    """Add a sculpted 3D shirt/polo/turtleneck collar ring around the neck."""
    segs = 28
    z0 = neck_pt.z - 0.016
    rings = []
    for k in range(3):
        t = k / 2.0
        z = z0 + t * collar_h
        flare = 1.0 + (0.22 * t if fold_down else 0.06 * t)
        ring = []
        for s in range(segs):
            # Angle 0 at -Y (front center), going around neck
            a = 2.0 * math.pi * s / segs
            # Leave front V opening if open_front
            if open_front and (a < 0.28 or a > 2.0 * math.pi - 0.28):
                ring.append(None)
                continue
            px = math.sin(a) * radius_x * flare
            py = neck_pt.y - math.cos(a) * radius_y * flare
            pz = z - 0.012 * math.cos(a)  # Slopes slightly lower in front
            ring.append(bm.verts.new((px, py, pz)))
        rings.append(ring)

    for k in range(2):
        r0, r1 = rings[k], rings[k + 1]
        for s in range(segs):
            sn = (s + 1) % segs
            if r0[s] and r0[sn] and r1[s] and r1[sn]:
                f = bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                f.smooth = True
                f.material_index = mat_idx


def _add_draped_hood(bm, neck_pt, h, mat_idx=0):
    """Add a realistic 3D relaxed streetwear hood resting neatly behind the neck & upper shoulders."""
    segs_u = 18
    segs_v = 8
    grid = []
    cx, cy, cz = 0.0, neck_pt.y + 0.025, neck_pt.z - 0.012
    for iv in range(segs_v):
        tv = iv / (segs_v - 1)  # 0 = neck seam, 1 = back draped fold
        row = []
        for iu in range(segs_u):
            tu = iu / (segs_u - 1)
            ang = -math.pi * 0.62 + tu * (math.pi * 1.24)  # wraps around sides & back of neck
            # Hood swells outward & backward then folds down onto upper back
            rx = (0.078 + 0.048 * math.sin(tv * math.pi * 0.85))
            ry_back = 0.045 + 0.105 * (tv ** 0.85)
            z_arc = cz + 0.048 * math.sin(tv * math.pi * 0.72) - 0.088 * (tv ** 1.65)
            px = math.sin(ang) * rx
            py = cy + math.cos(ang) * ry_back * (0.55 + 0.45 * tv)
            pz = z_arc + 0.012 * math.cos(ang) * (1.0 - tv)
            row.append(bm.verts.new((px, py, pz)))
        grid.append(row)

    for iv in range(segs_v - 1):
        for iu in range(segs_u - 1):
            f = bm.faces.new((grid[iv][iu], grid[iv][iu + 1], grid[iv + 1][iu + 1], grid[iv + 1][iu]))
            f.smooth = True
            f.material_index = mat_idx


def _add_front_lapels(bm, lm, h, z_bot, mat_idx=0, is_female=False):
    """Add crisp sculpted 3D folded jacket/blazer lapels flanking the open front."""
    z_neck = lm["neck"].z - 0.025
    z_waist = max(z_bot + 0.06, h * 0.61)
    y_front = lm["spine2"].y - (0.125 if is_female else 0.135)

    for side in (-1.0, 1.0):
        rows = 7
        inner_v = []
        outer_v = []
        for i in range(rows):
            t = i / (rows - 1)  # 0 = collar top, 1 = button stance
            z = z_neck * (1.0 - t) + z_waist * t
            w_lapel = (0.036 + 0.024 * math.sin(t * math.pi * 0.85)) * (0.88 if is_female else 1.0)
            x_in = side * (0.056 - 0.028 * t)
            x_out = x_in + side * w_lapel
            y_cur = y_front - 0.012 * math.sin(t * math.pi)
            inner_v.append(bm.verts.new((x_in, y_cur + 0.010, z)))
            outer_v.append(bm.verts.new((x_out, y_cur - 0.008, z)))
        for i in range(rows - 1):
            if side > 0:
                f = bm.faces.new((inner_v[i], outer_v[i], outer_v[i + 1], inner_v[i + 1]))
            else:
                f = bm.faces.new((inner_v[i], inner_v[i + 1], outer_v[i + 1], outer_v[i]))
            f.smooth = True
            f.material_index = mat_idx


def _add_belt_and_buckle(bm, body_obj, z_belt, belt_h=0.032, offset=0.009, belt_mat_idx=1, buckle_mat_idx=2):
    """Build a crisp 3D leather/fabric belt hugging the waistline + metallic front buckle."""
    co = _get_coords_array(body_obj.data)
    # Sample waist contour radii at z_belt in 32 angular sectors
    band = co[(np.abs(co[:, 2] - z_belt) < 0.025) & (np.abs(co[:, 0]) < 0.24)]
    if len(band) < 16:
        return
    cy = float(band[:, 1].mean())
    segs = 32
    r_prof = []
    for s in range(segs):
        a = 2.0 * math.pi * s / segs
        dx, dy = math.sin(a), -math.cos(a)
        # Find max projection along (dx, dy) from (0, cy) among nearby angular points
        rel_x = band[:, 0]
        rel_y = band[:, 1] - cy
        ang_v = np.arctan2(rel_x, -rel_y)
        diff_a = np.abs(np.arctan2(np.sin(ang_v - a), np.cos(ang_v - a)))
        sel = band[diff_a < 0.25]
        if len(sel) > 0:
            r = float(np.percentile(np.hypot(sel[:, 0], sel[:, 1] - cy), 92)) + offset
        else:
            r = 0.145 + offset
        r_prof.append(r)

    # Smooth radius profile slightly
    r_smooth = [0.25 * r_prof[(i - 1) % segs] + 0.50 * r_prof[i] + 0.25 * r_prof[(i + 1) % segs] for i in range(segs)]
    half_h = belt_h * 0.5
    r_bot = []
    r_top = []
    for s in range(segs):
        a = 2.0 * math.pi * s / segs
        px = math.sin(a) * r_smooth[s]
        py = cy - math.cos(a) * r_smooth[s]
        r_bot.append(bm.verts.new((px, py, z_belt - half_h)))
        r_top.append(bm.verts.new((px, py, z_belt + half_h)))

    for s in range(segs):
        sn = (s + 1) % segs
        f = bm.faces.new((r_bot[s], r_bot[sn], r_top[sn], r_top[s]))
        f.smooth = True
        f.material_index = belt_mat_idx

    # Add 3D metallic rectangular buckle centered at front (a = 0 => x=0, y = cy - r_smooth[0])
    y_buck = cy - r_smooth[0] - 0.005
    ret = bmesh.ops.create_cube(bm, size=1.0)
    bverts = ret["verts"]
    for v in bverts:
        v.co = Vector((v.co.x * 0.048, y_buck + v.co.y * 0.010, z_belt + v.co.z * (belt_h * 1.18)))
    vset = set(bverts)
    for f in bm.faces:
        if all(v in vset for v in f.verts):
            f.material_index = buckle_mat_idx


def _build_top_garment(
    name,
    char_info,
    fit_target,
    collection,
    materials,
    sleeve="short",
    z_bot_frac=0.535,
    neckline="crew",
    open_front=False,
    offset=0.0040,
    thickness=0.0028,
    add_collar=False,
    collar_fold=True,
    add_hood=False,
    add_lapels=False,
    add_trim_v=False,
    add_kangaroo_pocket=False,
    hem_band_h=0.0,
    smooth_after=3,
):
    """Build a tailored 3D upper-body garment (t-shirt, polo, hoodie, tunic, jacket, blazer, blouse)."""
    body_obj = char_info["body"]
    is_female = (char_info["gender"] == "female")
    lm = get_body_landmarks(char_info)
    h = lm["height"]
    z_bot = h * z_bot_frac
    z_neck = lm["neck"].z

    bm = bmesh.new()
    bm.from_mesh(fit_target.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    # Delete head/neck above collar and legs far below hem
    del_v = [v for v in bm.verts if v.co.z > z_neck + 0.015 or v.co.z < z_bot - 0.14]
    bmesh.ops.delete(bm, geom=del_v, context="VERTS")

    # Trim arms perpendicularly at requested sleeve length
    _bisect_arm_sleeves(bm, lm, sleeve)

    # Clean horizontal hem cut at z_bot (exclude hanging hands/arms if long sleeve passes below z_bot!)
    torso_hem_verts = [v for v in bm.verts if abs(v.co.x) < (0.24 if sleeve != "long" else 0.22)]
    vset = set(torso_hem_verts)
    t_edges = [e for e in bm.edges if e.verts[0] in vset or e.verts[1] in vset]
    t_faces = [f for f in bm.faces if any(v in vset for v in f.verts)]
    bmesh.ops.bisect_plane(bm, geom=torso_hem_verts + t_edges + t_faces, plane_co=(0, 0, z_bot), plane_no=(0, 0, 1), clear_inner=True)

    # Carve neckline & optional open-front jacket gap
    bm.faces.ensure_lookup_table()
    to_del = []
    for f in bm.faces:
        c = f.calc_center_median()
        ax = abs(c.x)
        # Neckline carving on front
        if c.y < lm["neck"].y + 0.015:
            if neckline == "crew":
                neck_cut = z_neck - 0.048 + ((ax / 0.068) ** 2.0) * 0.045
                if c.z > neck_cut and ax < 0.072:
                    to_del.append(f); continue
            elif neckline == "vneck":
                neck_cut = z_neck - 0.115 + (ax / 0.075) * 0.105
                if c.z > neck_cut and ax < 0.080:
                    to_del.append(f); continue
            elif neckline == "scoop":
                neck_cut = z_neck - 0.092 + ((ax / 0.088) ** 2.0) * 0.085
                if c.z > neck_cut and ax < 0.092:
                    to_del.append(f); continue
            elif neckline == "off_shoulder":
                # Straight chic off-shoulder neckline below clavicles
                if c.z > h * 0.788:
                    to_del.append(f); continue
            elif neckline == "collar_open":
                neck_cut = z_neck - 0.075 + ((ax / 0.068) ** 1.6) * 0.068
                if c.z > neck_cut and ax < 0.072:
                    to_del.append(f); continue
            elif neckline == "tank":
                neck_cut = z_neck - 0.088 + ((ax / 0.064) ** 2.0) * 0.078
                if c.z > neck_cut and ax < 0.068:
                    to_del.append(f); continue
        else:
            # Back neckline trim cleanly at nape
            if neckline == "off_shoulder" and c.z > h * 0.790:
                to_del.append(f); continue
            elif c.z > z_neck - 0.010 and ax < 0.072:
                to_del.append(f); continue

        if open_front and c.y < -0.025 and ax < (0.048 if is_female else 0.056):
            to_del.append(f); continue

    bmesh.ops.delete(bm, geom=to_del, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    _smooth_garment_boundaries(bm, passes=6, keep_z=False)

    # Assign trim / hem / placket material indices if multiple materials provided
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.smooth = True
        c = f.calc_center_median()
        if len(materials) >= 2:
            if hem_band_h > 0.0 and c.z < z_bot + hem_band_h and abs(c.x) < 0.24:
                f.material_index = 1
            elif add_trim_v and c.y < -0.04 and abs(c.x) < 0.036 and (z_neck - 0.24) < c.z < (z_neck - 0.02):
                f.material_index = 1
            else:
                f.material_index = 0
        else:
            f.material_index = 0

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for m in materials:
        obj.data.materials.append(m)

    _finalize_garment_modifiers(
        obj, fit_target, body_obj, offset=offset, thickness=thickness,
        smooth_after=smooth_after, guard_body=True
    )

    # Add crisp 3D sculptural details (collar, hood, lapels, kangaroo pocket) AFTER shrinkwrap
    if add_collar or add_hood or add_lapels or add_kangaroo_pocket:
        bm_det = bmesh.new()
        bm_det.from_mesh(obj.data)
        trim_idx = 1 if len(materials) >= 2 else 0
        if add_collar:
            _add_collar_band(
                bm_det, lm["neck"], h,
                radius_x=0.068 if is_female else 0.076,
                radius_y=0.066 if is_female else 0.074,
                collar_h=0.040 if collar_fold else 0.034,
                mat_idx=trim_idx if add_trim_v else 0,
                open_front=(open_front or neckline in ("vneck", "collar_open")),
                fold_down=collar_fold,
            )
        if add_hood:
            _add_draped_hood(bm_det, lm["neck"], h, mat_idx=0)
        if add_lapels:
            _add_front_lapels(bm_det, lm, h, z_bot, mat_idx=0, is_female=is_female)
        if add_kangaroo_pocket:
            # Sculpted front hoodie kangaroo pouch resting on lower front torso
            co_t = _get_coords_array(obj.data)
            z_pk = z_bot + 0.095
            front_sl = co_t[(np.abs(co_t[:, 2] - z_pk) < 0.035) & (np.abs(co_t[:, 0]) < 0.09) & (co_t[:, 1] < -0.05)]
            y_pk = float(np.percentile(front_sl[:, 1], 10)) - 0.010 if len(front_sl) > 5 else -0.155
            ret_p = bmesh.ops.create_cube(bm_det, size=1.0)
            pverts = ret_p["verts"]
            for v in pverts:
                t_up = 0.5 + v.co.z  # 0 bottom, 1 top (trapezoid pouch wider at bottom)
                w_x = 0.22 * (1.0 - 0.24 * t_up)
                v.co = Vector((v.co.x * w_x, y_pk + v.co.y * 0.022, z_pk + v.co.z * 0.135))
            vset_p = set(pverts)
            for f in bm_det.faces:
                if all(v in vset_p for v in f.verts):
                    f.smooth = True
                    f.material_index = 0
        bm_det.to_mesh(obj.data)
        bm_det.free()
        obj.data.update()

    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_", "Bust_", "Hip_"))
    return obj


def _build_pants_garment(
    name,
    char_info,
    fit_target,
    collection,
    materials,
    z_top_frac=0.565,
    z_bot_frac=0.065,
    offset=0.0052,
    thickness=0.0030,
    relaxed_legs=False,
    add_belt=False,
    add_cargo_pockets=False,
    cuff_h=0.0,
    smooth_after=3,
):
    """Build tailored 3D shorts, jeans, chinos, joggers, leggings, or trousers."""
    body_obj = char_info["body"]
    lm = get_body_landmarks(char_info)
    h = lm["height"]
    z_top = h * z_top_frac
    z_bot = h * z_bot_frac

    bm = bmesh.new()
    bm.from_mesh(fit_target.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    # Keep only pelvis/legs (strictly exclude A-pose arms/hands!)
    del_v = [
        v for v in bm.verts
        if abs(v.co.x) > 0.255 or v.co.z > z_top + 0.08 or v.co.z < z_bot - 0.08
    ]
    bmesh.ops.delete(bm, geom=del_v, context="VERTS")

    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_top), plane_no=(0, 0, 1), clear_outer=True)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_bot), plane_no=(0, 0, 1), clear_inner=True)

    # Relax leg cylinders outward cleanly for straight tailored trousers / chinos / cargo pants / joggers
    if relaxed_legs:
        bm.normal_update()
        for v in bm.verts:
            if v.co.z < h * 0.48:
                t_low = max(0.0, min(1.0, (h * 0.48 - v.co.z) / (h * 0.40)))
                # Generous trouser ease around knees & calves (tapering slightly at jogger ankle cuffs if cuff_h > 0)
                cuff_taper = (0.45 if (cuff_h > 0.0 and v.co.z < z_bot + cuff_h + 0.025) else 1.0)
                ease = (0.011 + 0.016 * t_low) * cuff_taper
                v.co += Vector((v.normal.x, v.normal.y, 0.0)) * ease
        # Smooth legs vertically so trouser drape looks crisp and straight
        leg_v = [v for v in bm.verts if v.co.z < h * 0.47 and not v.is_boundary]
        for _ in range(6):
            bmesh.ops.smooth_vert(bm, verts=leg_v, factor=0.55, use_axis_x=True, use_axis_y=True, use_axis_z=False)

    _smooth_garment_boundaries(bm, passes=4, keep_z=True)

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.smooth = True
        c = f.calc_center_median()
        if cuff_h > 0.0 and len(materials) >= 2 and (c.z < z_bot + cuff_h or c.z > z_top - 0.032):
            f.material_index = 1
        else:
            f.material_index = 0

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for m in materials:
        obj.data.materials.append(m)

    _finalize_garment_modifiers(
        obj, None if relaxed_legs else fit_target, body_obj,
        offset=offset, thickness=thickness, smooth_after=smooth_after, guard_body=True
    )

    if add_belt or add_cargo_pockets:
        bm_d = bmesh.new()
        bm_d.from_mesh(obj.data)
        if add_belt and len(materials) >= 3:
            _add_belt_and_buckle(bm_d, body_obj, z_top - 0.020, belt_h=0.032, offset=offset + 0.005, belt_mat_idx=1, buckle_mat_idx=2)
        if add_cargo_pockets:
            # Sculpted 3D side thigh pockets on outer left & right thighs
            z_pock = h * 0.365
            for side in (-1.0, 1.0):
                # Measure outer thigh X at z_pock
                co = _get_coords_array(obj.data)
                sl = co[(np.abs(co[:, 2] - z_pock) < 0.03) & (co[:, 0] * side > 0.05)]
                if len(sl) > 0:
                    x_out = float(np.percentile(np.abs(sl[:, 0]), 95)) + 0.008
                    y_mid = float(sl[:, 1].mean())
                    ret = bmesh.ops.create_cube(bm_d, size=1.0)
                    pverts = ret["verts"]
                    for v in pverts:
                        v.co = Vector((side * (x_out + v.co.x * 0.018), y_mid + v.co.y * 0.105, z_pock + v.co.z * 0.135))
                    vset = set(pverts)
                    for f in bm_d.faces:
                        if all(v in vset for v in f.verts):
                            f.smooth = False
                            f.material_index = 0
        bm_d.to_mesh(obj.data)
        bm_d.free()
        obj.data.update()

    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_", "Bust_", "Hip_"))
    return obj


def _build_skirt_hull(
    name,
    char_info,
    collection,
    materials,
    z_top_frac=0.590,
    z_bot_frac=0.250,
    flare=1.25,
    pleats=6,
    pleat_amp=0.008,
    band_h=0.036,
    offset=0.008,
):
    """Build a genuine continuous 3D skirt hull enveloping both legs smoothly (zero leg-cleft suction!)."""
    body_obj = char_info["body"]
    co = _get_coords_array(body_obj.data)
    h = float(co[:, 2].max())
    z_top = h * z_top_frac
    z_bot = h * z_bot_frac

    # Measure exact waist & hip ellipse profiles from the character mesh
    def measure_slice(z_val):
        sl = co[(np.abs(co[:, 2] - z_val) < 0.025) & (np.abs(co[:, 0]) < 0.26)]
        if len(sl) < 10:
            return 0.16, 0.11, 0.0
        rx = float(np.percentile(np.abs(sl[:, 0]), 97)) + offset
        y_min = float(np.percentile(sl[:, 1], 3)) - offset
        y_max = float(np.percentile(sl[:, 1], 97)) + offset
        ry = (y_max - y_min) * 0.5
        cy = (y_max + y_min) * 0.5
        return rx, ry, cy

    rx_waist, ry_waist, cy_waist = measure_slice(z_top)
    rx_hip, ry_hip, cy_hip = measure_slice(h * 0.505)

    bm = bmesh.new()
    segs_u = 44
    segs_v = 18
    rings = []
    z_hip = h * 0.505

    for iv in range(segs_v):
        t = iv / (segs_v - 1)  # 0 = waist, 1 = hem
        z = z_top * (1.0 - t) + z_bot * t
        if z >= z_hip:
            # Smoothly follow hourglass waist -> hip curve
            tw = (z_top - z) / max(0.01, (z_top - z_hip))
            s = tw * tw * (3.0 - 2.0 * tw)
            rx = rx_waist * (1.0 - s) + rx_hip * s
            ry = ry_waist * (1.0 - s) + ry_hip * s
            cy = cy_waist * (1.0 - s) + cy_hip * s
            fl = 1.0
            p_amp = 0.0
        else:
            # Below hips: flare smoothly (or taper for pencil skirt when flare < 1.0)
            tf = (z_hip - z) / max(0.01, (z_hip - z_bot))
            rx = rx_hip * (1.0 + (flare - 1.0) * (tf ** 1.15))
            ry = ry_hip * (1.0 + (flare - 1.0) * 0.85 * (tf ** 1.15))
            cy = cy_hip * (1.0 - tf * 0.35)
            fl = 1.0
            p_amp = pleat_amp * (tf ** 1.2)

        ring = []
        for iu in range(segs_u):
            a = 2.0 * math.pi * iu / segs_u
            wave = 1.0 + (p_amp / max(0.08, rx)) * math.sin(a * pleats)
            # Slight back glute roundness
            back_boost = 1.06 if math.cos(a) < 0 and z > h * 0.44 else 1.0
            px = math.sin(a) * rx * fl * wave
            py = cy - math.cos(a) * ry * fl * wave * back_boost
            ring.append(bm.verts.new((px, py, z)))
        rings.append(ring)

    for iv in range(segs_v - 1):
        r0, r1 = rings[iv], rings[iv + 1]
        z_mid = (r0[0].co.z + r1[0].co.z) * 0.5
        midx = 1 if (band_h > 0.0 and len(materials) >= 2 and z_mid > z_top - band_h) else 0
        for iu in range(segs_u):
            iun = (iu + 1) % segs_u
            f = bm.faces.new((r0[iu], r1[iu], r1[iun], r0[iun]))
            f.smooth = True
            f.material_index = midx

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for m in materials:
        obj.data.materials.append(m)

    # Guard outside body_obj so skin never pokes through, then solidify cleanly
    sw = obj.modifiers.new("Guard", "SHRINKWRAP")
    sw.target = body_obj
    sw.wrap_method = "NEAREST_SURFACEPOINT"
    sw.wrap_mode = "OUTSIDE"
    sw.offset = offset * 0.75

    sol = obj.modifiers.new("Solidify", "SOLIDIFY")
    sol.thickness = 0.0032
    sol.offset = 1.0
    sol.use_rim = True
    _apply_all_modifiers(obj)

    attach_modular_mesh(char_info, obj, transfer_shapekeys=True, key_prefix_filter=("Body_", "Bust_", "Hip_"))
    return obj


# ===========================================================================
# 10 COMPLETE MALE OUTFITS
# ===========================================================================
def build_male_wardrobe(char_info, parent_col):
    """Build all 10 wearable Male outfits in organized child collections under Wardrobe_Male."""
    body_obj = char_info["body"]
    h = char_info["height"]
    root_col = bpy.data.collections.new("Wardrobe_Male")
    parent_col.children.link(root_col)

    # Shared temporary fit targets (deleted at end of function)
    tmp_col = bpy.data.collections.new("__TmpMaleFit")
    bpy.context.scene.collection.children.link(tmp_col)
    fit_tight = _create_bridged_fit_target(body_obj, h, tmp_col, compress_bust=False)
    fit_relax = _create_relaxed_fit_target(body_obj, h, tmp_col, is_female=False, inflate=0.002)
    fit_outer = _create_relaxed_fit_target(body_obj, h, tmp_col, is_female=False, inflate=0.0085)

    # Reusable PBR materials
    m_white = create_fabric_material("M_Cotton_White", (0.90, 0.90, 0.88), roughness=0.84)
    m_black = create_fabric_material("M_Cotton_Black", (0.10, 0.10, 0.11), roughness=0.82)
    m_charcoal = create_fabric_material("M_Wool_Charcoal", (0.18, 0.19, 0.21), roughness=0.78)
    m_olive = create_fabric_material("M_Twill_Olive", (0.24, 0.29, 0.19), roughness=0.84)
    m_sky = create_fabric_material("M_Shirt_SkyBlue", (0.64, 0.78, 0.90), roughness=0.74)
    m_burgundy = create_fabric_material("M_Polo_Burgundy", (0.42, 0.10, 0.14), roughness=0.80)
    m_sand = create_fabric_material("M_Chino_Sand", (0.76, 0.68, 0.54), roughness=0.82)
    m_indigo = create_fabric_material("M_Denim_Indigo", (0.16, 0.24, 0.38), roughness=0.86, weave_scale=460.0)
    m_denim_lt = create_fabric_material("M_Denim_Washed", (0.28, 0.40, 0.56), roughness=0.86, weave_scale=460.0)
    m_emerald = create_fabric_material("M_Sport_Emerald", (0.09, 0.38, 0.24), roughness=0.78)
    m_orange = create_fabric_material("M_CI_Orange", (0.90, 0.42, 0.08), roughness=0.76)
    m_cream = create_fabric_material("M_Linen_Cream", (0.89, 0.85, 0.76), roughness=0.88)
    m_teal = create_fabric_material("M_Resort_Teal", (0.14, 0.46, 0.50), roughness=0.80)
    m_gold_trim = create_fabric_material("M_Embroidery_Gold", (0.86, 0.66, 0.18), roughness=0.45, metallic=0.35)
    m_leather_blk = create_leather_material("M_Leather_Black", (0.11, 0.11, 0.12), roughness=0.34)
    m_leather_brn = create_leather_material("M_Leather_Brown", (0.30, 0.16, 0.09), roughness=0.40)
    m_buckle_gold = create_metal_material("M_Buckle_Gold", (0.88, 0.72, 0.26), roughness=0.22)
    m_buckle_silv = create_metal_material("M_Buckle_Silver", (0.82, 0.84, 0.86), roughness=0.20)
    m_wax_abj = create_wax_pagne_material("M_Wax_Abidjan", palette="abidjan")
    m_wax_kita = create_wax_pagne_material("M_Wax_Kita", palette="kita")

    outfits = {}

    def make_col(key):
        c = bpy.data.collections.new(f"Male_Outfit_{key}")
        root_col.children.link(c)
        return c

    # 01: Abidjan Streetwear (White Tee + Olive Cargo Shorts w/ Belt & Pockets)
    c = make_col("01_Abidjan_Streetwear")
    t1 = _build_top_garment("M01_WhiteTee", char_info, fit_relax, c, [m_white], sleeve="short", z_bot_frac=0.515, neckline="crew", offset=0.0045)
    b1 = _build_pants_garment("M01_CargoShorts", char_info, fit_relax, c, [m_olive, m_leather_blk, m_buckle_silv], z_top_frac=0.560, z_bot_frac=0.310, offset=0.0060, relaxed_legs=True, add_belt=True, add_cargo_pockets=True)
    outfits["01_Abidjan_Streetwear"] = (c, [t1, b1])

    # 02: Plateau Business Suit (Sky Collared Shirt + Charcoal Blazer + Tailored Trousers)
    c = make_col("02_Plateau_Business")
    s2 = _build_top_garment("M02_DressShirt", char_info, fit_tight, c, [m_sky], sleeve="short", z_bot_frac=0.540, neckline="collar_open", offset=0.0038, add_collar=True, collar_fold=True)
    j2 = _build_top_garment("M02_Blazer", char_info, fit_outer, c, [m_charcoal], sleeve="long", z_bot_frac=0.475, neckline="vneck", open_front=True, offset=0.0045, thickness=0.0034, add_collar=True, collar_fold=True, add_lapels=True)
    p2 = _build_pants_garment("M02_SuitTrousers", char_info, fit_relax, c, [m_charcoal, m_leather_brn, m_buckle_gold], z_top_frac=0.562, z_bot_frac=0.062, offset=0.0055, relaxed_legs=True, add_belt=True)
    outfits["02_Plateau_Business"] = (c, [s2, j2, p2])

    # 03: Cocody Smart Casual (Burgundy Polo + Sand Chinos)
    c = make_col("03_Cocody_SmartCasual")
    t3 = _build_top_garment("M03_BurgundyPolo", char_info, fit_tight, c, [m_burgundy, m_gold_trim], sleeve="short", z_bot_frac=0.520, neckline="collar_open", offset=0.0042, add_collar=True, collar_fold=True, add_trim_v=True)
    p3 = _build_pants_garment("M03_SandChinos", char_info, fit_relax, c, [m_sand, m_leather_brn, m_buckle_gold], z_top_frac=0.562, z_bot_frac=0.068, offset=0.0055, relaxed_legs=True, add_belt=True)
    outfits["03_Cocody_SmartCasual"] = (c, [t3, p3])

    # 04: Wax Dashiki Tunic Set (Abidjan Wax Tunic w/ Gold Chest Placket & Continuous Tailored Hem + Indigo Trousers)
    c = make_col("04_Wax_Dashiki_Set")
    t4 = _build_top_garment("M04_WaxTunicTop", char_info, fit_relax, c, [m_wax_abj, m_gold_trim], sleeve="short", z_bot_frac=0.525, neckline="vneck", offset=0.0070, thickness=0.0030, add_trim_v=True)
    h4 = _build_skirt_hull("M04_WaxTunicHem", char_info, c, [m_wax_abj], z_top_frac=0.560, z_bot_frac=0.410, flare=1.08, pleats=4, pleat_amp=0.004, band_h=0.0, offset=0.014)
    p4 = _build_pants_garment("M04_IndigoTrousers", char_info, fit_tight, c, [m_indigo], z_top_frac=0.558, z_bot_frac=0.065, offset=0.0048, relaxed_legs=True)
    outfits["04_Wax_Dashiki_Set"] = (c, [t4, h4, p4])

    # 05: Marcory Nightlife Biker (Black Leather Jacket + Charcoal Tee + Indigo Slim Jeans)
    c = make_col("05_Nightlife_Biker")
    i5 = _build_top_garment("M05_InnerTee", char_info, fit_tight, c, [m_charcoal], sleeve="short", z_bot_frac=0.535, neckline="crew", offset=0.0038)
    j5 = _build_top_garment("M05_BikerJacket", char_info, fit_outer, c, [m_leather_blk], sleeve="long", z_bot_frac=0.535, neckline="vneck", open_front=True, offset=0.0045, thickness=0.0038, add_collar=True, add_lapels=True)
    p5 = _build_pants_garment("M05_SlimJeans", char_info, fit_tight, c, [m_indigo, m_leather_blk, m_buckle_silv], z_top_frac=0.562, z_bot_frac=0.065, offset=0.0052, relaxed_legs=True, add_belt=True)
    outfits["05_Nightlife_Biker"] = (c, [i5, j5, p5])

    # 06: Lagune Sport Tracksuit (Emerald Hoodie w/ 3D Hood & Kangaroo Pocket + Charcoal Contrast Joggers)
    c = make_col("06_Lagune_Sport")
    t6 = _build_top_garment("M06_EmeraldHoodie", char_info, fit_relax, c, [m_emerald, m_gold_trim], sleeve="long", z_bot_frac=0.510, neckline="crew", offset=0.0075, thickness=0.0038, add_hood=True, add_kangaroo_pocket=True, hem_band_h=0.045)
    p6 = _build_pants_garment("M06_CharcoalJoggers", char_info, fit_relax, c, [m_charcoal, m_emerald], z_top_frac=0.560, z_bot_frac=0.072, offset=0.0058, relaxed_legs=True, cuff_h=0.050)
    outfits["06_Lagune_Sport"] = (c, [t6, p6])

    # 07: Grand-Bassam Resort Linen (Cream Relaxed Linen Shirt + Lagoon Teal Shorts)
    c = make_col("07_GrandBassam_Resort")
    t7 = _build_top_garment("M07_LinenShirt", char_info, fit_relax, c, [m_cream], sleeve="three_quarter", z_bot_frac=0.505, neckline="collar_open", offset=0.0068, add_collar=True, collar_fold=True)
    p7 = _build_pants_garment("M07_TealShorts", char_info, fit_relax, c, [m_teal, m_cream], z_top_frac=0.558, z_bot_frac=0.320, offset=0.0052, relaxed_legs=True, cuff_h=0.025)
    outfits["07_GrandBassam_Resort"] = (c, [t7, p7])

    # 08: Royal Kita Boubou Set (Long Royal Kita Tunic w/ Continuous Tailored Lower Hem + Black Tailored Trousers)
    c = make_col("08_Kita_Royal_Boubou")
    t8 = _build_top_garment("M08_KitaBoubouTop", char_info, fit_outer, c, [m_wax_kita, m_gold_trim], sleeve="three_quarter", z_bot_frac=0.525, neckline="vneck", offset=0.0070, thickness=0.0032, add_collar=True, collar_fold=False, add_trim_v=True)
    h8 = _build_skirt_hull("M08_KitaBoubouHem", char_info, c, [m_wax_kita, m_gold_trim], z_top_frac=0.565, z_bot_frac=0.355, flare=1.12, pleats=6, pleat_amp=0.006, band_h=0.0, offset=0.016)
    p8 = _build_pants_garment("M08_BlackTrousers", char_info, fit_tight, c, [m_black], z_top_frac=0.555, z_bot_frac=0.062, offset=0.0046, relaxed_legs=True)
    outfits["08_Kita_Royal_Boubou"] = (c, [t8, h8, p8])

    # 09: Treichville Denim (Washed Denim Jacket + CI Orange Tee + Charcoal Cargo Pants)
    c = make_col("09_Treichville_Denim")
    i9 = _build_top_garment("M09_OrangeTee", char_info, fit_tight, c, [m_orange], sleeve="short", z_bot_frac=0.530, neckline="crew", offset=0.0038)
    j9 = _build_top_garment("M09_DenimJacket", char_info, fit_outer, c, [m_denim_lt], sleeve="long", z_bot_frac=0.525, neckline="vneck", open_front=True, offset=0.0045, thickness=0.0036, add_collar=True, add_lapels=True)
    p9 = _build_pants_garment("M09_CharcoalCargo", char_info, fit_relax, c, [m_charcoal, m_leather_blk, m_buckle_silv], z_top_frac=0.562, z_bot_frac=0.068, offset=0.0055, relaxed_legs=True, add_belt=True, add_cargo_pockets=True)
    outfits["09_Treichville_Denim"] = (c, [i9, j9, p9])

    # 10: Elephants Training Kit (CI Orange Athletic Tank w/ Emerald Trim + Black Sport Shorts)
    c = make_col("10_Gym_Elephants")
    t10 = _build_top_garment("M10_OrangeTank", char_info, fit_tight, c, [m_orange, m_emerald], sleeve="none", z_bot_frac=0.525, neckline="tank", offset=0.0042, hem_band_h=0.028)
    p10 = _build_pants_garment("M10_SportShorts", char_info, fit_relax, c, [m_black, m_orange], z_top_frac=0.560, z_bot_frac=0.335, offset=0.0056, cuff_h=0.026)
    outfits["10_Gym_Elephants"] = (c, [t10, p10])

    # Cleanup temporary fit targets
    for obj in list(tmp_col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(tmp_col)

    return outfits, root_col


# ===========================================================================
# 10 COMPLETE FEMALE OUTFITS
# ===========================================================================
def build_female_wardrobe(char_info, parent_col):
    """Build all 10 wearable Female outfits in organized child collections under Wardrobe_Female."""
    body_obj = char_info["body"]
    h = char_info["height"]
    root_col = bpy.data.collections.new("Wardrobe_Female")
    parent_col.children.link(root_col)

    tmp_col = bpy.data.collections.new("__TmpFemFit")
    bpy.context.scene.collection.children.link(tmp_col)
    fit_tight = _create_bridged_fit_target(body_obj, h, tmp_col, compress_bust=True)
    fit_relax = _create_relaxed_fit_target(body_obj, h, tmp_col, is_female=True, inflate=0.002)
    fit_outer = _create_relaxed_fit_target(body_obj, h, tmp_col, is_female=True, inflate=0.0085)

    f_cream = create_fabric_material("F_Tailored_Cream", (0.91, 0.88, 0.82), roughness=0.76)
    f_white = create_fabric_material("F_Cotton_White", (0.94, 0.93, 0.91), roughness=0.80)
    f_black = create_fabric_material("F_Crepe_Black", (0.10, 0.10, 0.11), roughness=0.78)
    f_terra = create_fabric_material("F_Silk_Terracotta", (0.76, 0.30, 0.16), roughness=0.52, sheen=0.45)
    f_olive = create_fabric_material("F_Knit_Olive", (0.28, 0.34, 0.21), roughness=0.82)
    f_indigo = create_fabric_material("F_Denim_Indigo", (0.17, 0.26, 0.42), roughness=0.84, weave_scale=460.0)
    f_teal = create_fabric_material("F_Active_Teal", (0.10, 0.44, 0.48), roughness=0.62)
    f_emerald = create_fabric_material("F_Luxury_Emerald", (0.08, 0.40, 0.26), roughness=0.60, sheen=0.40)
    f_wine = create_fabric_material("F_Velvet_Wine", (0.46, 0.10, 0.16), roughness=0.68, sheen=0.55)
    f_mustard = create_fabric_material("F_Fleece_Mustard", (0.86, 0.62, 0.12), roughness=0.86)
    f_navy = create_fabric_material("F_Sash_Indigo", (0.09, 0.14, 0.30), roughness=0.72)
    f_leather = create_leather_material("F_Leather_Moto", (0.11, 0.10, 0.11), roughness=0.32)
    f_gold = create_metal_material("F_Belt_Gold", (0.90, 0.73, 0.26), roughness=0.20)
    f_wax_abj = create_wax_pagne_material("F_Wax_Abidjan", palette="abidjan")
    f_wax_kita = create_wax_pagne_material("F_Wax_Kita", palette="kita")
    f_wax_terra = create_wax_pagne_material("F_Wax_Terracotta", palette="terracotta")

    outfits = {}

    def make_col(key):
        c = bpy.data.collections.new(f"Female_Outfit_{key}")
        root_col.children.link(c)
        return c

    # 01: Abidjan Wax Midi Dress (Fitted Wax Bodice + Flared A-line Wax Midi Skirt + Indigo Sash)
    c = make_col("01_Abidjan_Wax_Dress")
    t1 = _build_top_garment("F01_WaxBodice", char_info, fit_tight, c, [f_wax_abj], sleeve="short", z_bot_frac=0.575, neckline="scoop", offset=0.0040)
    s1 = _build_skirt_hull("F01_WaxMidiSkirt", char_info, c, [f_wax_abj, f_navy], z_top_frac=0.595, z_bot_frac=0.235, flare=1.36, pleats=8, pleat_amp=0.010, band_h=0.042, offset=0.007)
    outfits["01_Abidjan_Wax_Dress"] = (c, [t1, s1])

    # 02: Plateau Boss Blazer Set (Cream Blazer + Terracotta Top + Black Pencil Skirt)
    c = make_col("02_Plateau_Executive")
    i2 = _build_top_garment("F02_TerraTop", char_info, fit_tight, c, [f_terra], sleeve="none", z_bot_frac=0.575, neckline="scoop", offset=0.0038)
    j2 = _build_top_garment("F02_CreamBlazer", char_info, fit_outer, c, [f_cream], sleeve="long", z_bot_frac=0.515, neckline="vneck", open_front=True, offset=0.0045, thickness=0.0032, add_collar=True, add_lapels=True)
    s2 = _build_skirt_hull("F02_PencilSkirt", char_info, c, [f_black, f_gold], z_top_frac=0.600, z_bot_frac=0.295, flare=0.93, pleats=2, pleat_amp=0.0, band_h=0.032, offset=0.009)
    outfits["02_Plateau_Executive"] = (c, [i2, j2, s2])

    # 03: Cocody Street Chic (Olive Cropped Top + High-Waist Indigo Jeans + Gold Belt)
    c = make_col("03_Cocody_Chic_Crop")
    t3 = _build_top_garment("F03_OliveCrop", char_info, fit_tight, c, [f_olive], sleeve="short", z_bot_frac=0.630, neckline="crew", offset=0.0042, add_collar=True, collar_fold=False)
    p3 = _build_pants_garment("F03_HighWaistJeans", char_info, fit_tight, c, [f_indigo, f_leather, f_gold], z_top_frac=0.595, z_bot_frac=0.068, offset=0.0052, add_belt=True)
    outfits["03_Cocody_Chic_Crop"] = (c, [t3, p3])

    # 04: Royal Kita Evening Gown (Off-Shoulder Kita Bodice + Sweeping Maxi Gown w/ Gold Waistband)
    c = make_col("04_Kita_Royal_Gown")
    t4 = _build_top_garment("F04_KitaBodice", char_info, fit_tight, c, [f_wax_kita], sleeve="short", z_bot_frac=0.580, neckline="off_shoulder", offset=0.0042)
    s4 = _build_skirt_hull("F04_KitaMaxiSkirt", char_info, c, [f_wax_kita, f_gold], z_top_frac=0.600, z_bot_frac=0.055, flare=1.42, pleats=10, pleat_amp=0.012, band_h=0.038, offset=0.007)
    outfits["04_Kita_Royal_Gown"] = (c, [t4, s4])

    # 05: Lagune Athleisure (Fitted Teal Long-Sleeve Zip Crop + Sculpting Leggings)
    c = make_col("05_Lagune_Athleisure")
    t5 = _build_top_garment("F05_TealZipTop", char_info, fit_tight, c, [f_teal, f_white], sleeve="long", z_bot_frac=0.628, neckline="crew", offset=0.0042, add_collar=True, collar_fold=False, hem_band_h=0.025)
    p5 = _build_pants_garment("F05_TealLeggings", char_info, fit_tight, c, [f_teal, f_black], z_top_frac=0.600, z_bot_frac=0.072, offset=0.0042, cuff_h=0.028)
    outfits["05_Lagune_Athleisure"] = (c, [t5, p5])

    # 06: Grand-Bassam Summer Resort Dress (Sleeveless Terracotta Wax Flare Mini Dress)
    c = make_col("06_GrandBassam_Resort")
    t6 = _build_top_garment("F06_ResortBodice", char_info, fit_tight, c, [f_wax_terra], sleeve="none", z_bot_frac=0.580, neckline="vneck", offset=0.0042)
    s6 = _build_skirt_hull("F06_ResortFlareMini", char_info, c, [f_wax_terra, f_cream], z_top_frac=0.598, z_bot_frac=0.335, flare=1.34, pleats=8, pleat_amp=0.010, band_h=0.034, offset=0.007)
    outfits["06_GrandBassam_Resort"] = (c, [t6, s6])

    # 07: Marcory Night Moto (Black Cropped Leather Jacket + White Crop Tee + Wine Mini Skirt)
    c = make_col("07_Marcory_Biker_Night")
    i7 = _build_top_garment("F07_WhiteCrop", char_info, fit_tight, c, [f_white], sleeve="none", z_bot_frac=0.625, neckline="scoop", offset=0.0038)
    j7 = _build_top_garment("F07_MotoJacket", char_info, fit_outer, c, [f_leather], sleeve="long", z_bot_frac=0.608, neckline="vneck", open_front=True, offset=0.0045, thickness=0.0036, add_collar=True, add_lapels=True)
    s7 = _build_skirt_hull("F07_WineMiniSkirt", char_info, c, [f_wine, f_leather], z_top_frac=0.595, z_bot_frac=0.365, flare=0.96, pleats=2, pleat_amp=0.0, band_h=0.030, offset=0.0065)
    outfits["07_Marcory_Biker_Night"] = (c, [i7, j7, s7])

    # 08: Modern Wax Pagne Ensemble (Crisp White Off-Shoulder Blouse + Terracotta Wax Wrap Midi Skirt)
    c = make_col("08_Wax_Pagne_Ensemble")
    t8 = _build_top_garment("F08_WhiteBlouse", char_info, fit_relax, c, [f_white], sleeve="short", z_bot_frac=0.578, neckline="off_shoulder", offset=0.0048)
    s8 = _build_skirt_hull("F08_PagneWrapSkirt", char_info, c, [f_wax_terra, f_terra], z_top_frac=0.602, z_bot_frac=0.220, flare=1.10, pleats=4, pleat_amp=0.006, band_h=0.045, offset=0.0075)
    outfits["08_Wax_Pagne_Ensemble"] = (c, [t8, s8])

    # 09: Urban Abidjan Hoodie (Mustard Cropped Hoodie w/ 3D Hood + Olive Cargo Joggers)
    c = make_col("09_Street_Hoodie_Cargo")
    t9 = _build_top_garment("F09_MustardHoodie", char_info, fit_relax, c, [f_mustard, f_black], sleeve="long", z_bot_frac=0.608, neckline="crew", offset=0.0062, thickness=0.0034, add_hood=True, hem_band_h=0.040)
    p9 = _build_pants_garment("F09_OliveCargoJoggers", char_info, fit_relax, c, [f_olive, f_black], z_top_frac=0.590, z_bot_frac=0.072, offset=0.0056, relaxed_legs=True, add_cargo_pockets=True, cuff_h=0.045)
    outfits["09_Street_Hoodie_Cargo"] = (c, [t9, p9])

    # 10: Ivory Executive Jumpsuit (Emerald V-Neck Bodice + Tailored Wide-Leg Trousers + Gold Belt)
    c = make_col("10_Smart_Jumpsuit")
    t10 = _build_top_garment("F10_EmeraldBodice", char_info, fit_tight, c, [f_emerald], sleeve="none", z_bot_frac=0.575, neckline="vneck", offset=0.0040)
    p10 = _build_pants_garment("F10_EmeraldTrousers", char_info, fit_relax, c, [f_emerald, f_emerald, f_gold], z_top_frac=0.600, z_bot_frac=0.058, offset=0.0052, relaxed_legs=True, add_belt=True)
    outfits["10_Smart_Jumpsuit"] = (c, [t10, p10])

    for obj in list(tmp_col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(tmp_col)

    return outfits, root_col
