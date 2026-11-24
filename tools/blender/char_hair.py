"""Modular 3D West African / Ivorian Hairstyle Generator for Ivory State.

Generates 10 distinct Male hairstyles and 10 distinct Female hairstyles fitted directly
onto the finalized A-pose character basemesh, rigged to mixamo_unity, and synced with
Face_Preset_01..10 Shape Keys.
"""

import math
import random
import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from char_builder import (
    _apply_all_modifiers,
    attach_modular_mesh,
    get_body_landmarks,
)
from char_materials import (
    create_afro_hair_material,
    create_fabric_material,
    create_metal_material,
)


MALE_HAIR_SPECS = [
    ("01_LowFade",         "H01 Low Fade"),
    ("02_HighTopFade",     "H02 High-Top Fade"),
    ("03_ShortAfroTaper",  "H03 Afro Taper"),
    ("04_Waves360",        "H04 360 Waves"),
    ("05_SpongeTwists",    "H05 Sponge Twists"),
    ("06_Cornrows",        "H06 Cornrows"),
    ("07_ShortLocs",       "H07 Short Locs"),
    ("08_HighTopLocs",     "H08 High-Top Locs"),
    ("09_FullAfro",        "H09 Full Afro"),
    ("10_BurstFadeMohawk", "H10 Burst Mohawk"),
]

FEMALE_HAIR_SPECS = [
    ("01_ShortTWA",         "H01 Short TWA"),
    ("02_NaturalAfroCrown", "H02 Afro Crown"),
    ("03_HighAfroPuff",     "H03 High Puff"),
    ("04_DoubleAfroPuffs",  "H04 Double Puffs"),
    ("05_LongBoxBraids",    "H05 Box Braids"),
    ("06_FulaniCornrows",   "H06 Fulani Braids"),
    ("07_HighBraidedBun",   "H07 Braided Bun"),
    ("08_BraidedBob",       "H08 Braided Bob"),
    ("09_GoddessLocs",      "H09 Goddess Locs"),
    ("10_CurlySidePart",    "H10 Curly Side-Part"),
]


class _ScalpHelper:
    """Fast anatomical scalp sampler & collision avoider using BVHTree on the finalized basemesh."""

    def __init__(self, char_info):
        self.body_obj = char_info["body"]
        self.is_female = (char_info["gender"] == "female")
        self.lm = get_body_landmarks(char_info)
        self.eye_z = float(self.lm["l_eye"].z)
        self.eye_y = float(self.lm["l_eye"].y)
        self.top_z = float(self.lm["head_top_z"])
        self.skull_c = Vector((0.0, self.eye_y + 0.088, self.eye_z + 0.032))
        deps = bpy.context.evaluated_depsgraph_get()
        self.bvh = BVHTree.FromObject(self.body_obj, deps)

    def hairline_sdf(self, co):
        """Positive = inside hairline scalp area, negative = face/neck skin."""
        ax = abs(co.x)
        dy = co.y - self.eye_y
        if dy < 0.038:
            z_cut = (self.eye_z + (0.048 if self.is_female else 0.052)) - 0.09 * (ax ** 2.1)
            if ax > 0.058 and dy > 0.024:
                t_sb = (dy - 0.024) / 0.014
                z_cut -= 0.024 * t_sb
        elif dy < 0.092:
            t_ear = (dy - 0.038) / 0.054
            ear_bell = math.sin(t_ear * math.pi)
            z_base = (self.eye_z + 0.036) - 0.030 * t_ear
            z_cut = z_base + (0.024 * ear_bell if ax > 0.056 else 0.0)
        else:
            t_back = min(1.0, (dy - 0.092) / 0.085)
            z_cut = (self.eye_z + 0.006) - t_back * 0.058
        return co.z - z_cut

    def project_to_scalp(self, direction_vec, offset=0.0025):
        """Raycast outward/inward from skull_c along direction_vec to hit exact scalp surface."""
        d = direction_vec.normalized()
        origin = self.skull_c + d * 0.25
        loc, norm, _idx, _dist = self.bvh.ray_cast(origin, -d, 0.30)
        if loc is not None:
            return loc + norm * offset, norm
        # Fallback nearest point
        loc, norm, _idx, _dist = self.bvh.find_nearest(self.skull_c + d * 0.095)
        if loc is not None:
            return loc + norm * offset, norm
        return self.skull_c + d * 0.095, d

    def push_outside_body(self, pt, clearance=0.0055):
        """Ensure any braid/loc point stays strictly outside head/ears/neck/shoulders/chest."""
        loc, norm, _idx, dist = self.bvh.find_nearest(pt)
        if loc is None:
            return pt
        vec = pt - loc
        if vec.dot(norm) < clearance:
            return loc + norm * clearance
        return pt

    def build_scalp_cap_bmesh(self, subdivide_cuts=1, height_fn=None, base_thickness=0.004, min_sdf=0.0):
        """Extract scalp cap from body mesh and extrude/sculpt outward via height_fn(co, norm, sdf)->float."""
        bm = bmesh.new()
        bm.from_mesh(self.body_obj.data)
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()

        keep = [
            f for f in bm.faces
            if self.hairline_sdf(f.calc_center_median()) > (min_sdf - 0.003)
            and f.calc_center_median().z > self.eye_z - 0.056
        ]
        keep_set = set(keep)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep_set], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

        if subdivide_cuts > 0:
            bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=subdivide_cuts, use_grid_fill=True)

        # Smooth scalp hairline perimeter so forehead/temple lines are silky smooth (no polygon stair-stepping!)
        for _ in range(6):
            bverts = [v for v in bm.verts if v.is_boundary]
            if not bverts:
                break
            bmesh.ops.smooth_vert(bm, verts=bverts, factor=0.55, use_axis_x=True, use_axis_y=True, use_axis_z=True)

        bm.verts.ensure_lookup_table()
        bm.normal_update()

        for v in bm.verts:
            sdf = self.hairline_sdf(v.co)
            if height_fn is not None:
                h = height_fn(v.co, v.normal, sdf)
            else:
                edge_t = max(0.0, min(1.0, (sdf + 0.003) / 0.025))
                h = base_thickness * (0.25 + 0.75 * edge_t)
            # Re-project boundary vertices slightly onto skin surface to avoid floating gaps
            if v.is_boundary:
                loc, norm, _i, _d = self.bvh.find_nearest(v.co)
                if loc is not None:
                    v.co = loc + norm * 0.0012
                    continue
            v.co += v.normal * max(0.0008, h)

        for f in bm.faces:
            f.smooth = True
        return bm


def _add_tube_strand(bm, points, radii, segs=7, ripple_amp=0.14, ripple_freq=2.6, mat_idx=0):
    """Sweep a smooth textured 3D braided/loc tube along points with rounded cap ends."""
    if len(points) < 2:
        return
    rings = []
    n_pts = len(points)
    prev_t = None
    prev_n = Vector((1.0, 0.0, 0.0))

    for i, p in enumerate(points):
        if i == 0:
            t = (points[1] - points[0]).normalized()
        elif i == n_pts - 1:
            t = (points[-1] - points[-2]).normalized()
        else:
            t = (points[i + 1] - points[i - 1]).normalized()
        if t.length < 1e-6:
            t = Vector((0.0, 0.0, -1.0))

        if prev_t is None:
            ref = Vector((1.0, 0.0, 0.0)) if abs(t.x) < 0.85 else Vector((0.0, 1.0, 0.0))
            n_vec = (ref - t * ref.dot(t)).normalized()
        else:
            rot = prev_t.rotation_difference(t)
            n_vec = (rot @ prev_n).normalized()
        b_vec = t.cross(n_vec).normalized()
        prev_t, prev_n = t, n_vec

        r_base = radii[i]
        # Sinusoidal braid/loc segmentation ripple along strand
        r_mod = r_base * (1.0 + ripple_amp * math.sin(i * ripple_freq))
        ring = []
        for s in range(segs):
            ang = (2.0 * math.pi * s) / segs + i * 0.45
            # Slight oval cross-section twist for realistic braided rope feel
            ell = 1.0 + 0.16 * math.cos(2.0 * ang)
            offset = (n_vec * math.cos(ang) + b_vec * math.sin(ang)) * (r_mod * ell)
            ring.append(bm.verts.new(p + offset))
        rings.append(ring)

    for i in range(len(rings) - 1):
        r0, r1 = rings[i], rings[i + 1]
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
            f.smooth = True
            f.material_index = mat_idx

    # Tip cap
    tip_v = bm.verts.new(points[-1] + (points[-1] - points[-2]).normalized() * (radii[-1] * 0.7))
    last_r = rings[-1]
    for s in range(segs):
        sn = (s + 1) % segs
        f = bm.faces.new((last_r[s], last_r[sn], tip_v))
        f.smooth = True
        f.material_index = mat_idx


def _add_coily_puff_sphere(bm, center, rx, ry, rz, subdiv=3, noise_amp=0.0055, seed=42, mat_idx=0):
    """Create a plush sculpted 4C Afro puff/cloud volume with fine micro-coily curl texture."""
    ret = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    verts = ret["verts"]
    for v in verts:
        nx, ny, nz = v.co.x, v.co.y, v.co.z
        w1 = math.sin(nx * 20.0 + seed) * math.cos(ny * 19.0 - seed * 0.7) * math.sin(nz * 21.0 + 1.1)
        w2 = math.sin(nx * 36.0 - nz * 32.0) * math.cos(ny * 34.0 + nx * 29.0)
        disp = 1.0 + min(noise_amp, 0.0058) * (0.60 * w1 + 0.40 * w2) / max(0.04, (rx + ry + rz) / 3.0)
        v.co = Vector((center.x + nx * rx * disp, center.y + ny * ry * disp, center.z + nz * rz * disp))
    vset = set(verts)
    for f in bm.faces:
        if all(v in vset for v in f.verts):
            f.smooth = True
            f.material_index = mat_idx


def _add_ring_band(bm, center, axis_vec, radius, tube_r=0.006, segs_u=24, segs_v=8, mat_idx=1):
    """Create a sleek hair tie / gold cuff torus band around a puff or bun base."""
    axis = axis_vec.normalized()
    ref = Vector((1.0, 0.0, 0.0)) if abs(axis.x) < 0.85 else Vector((0.0, 1.0, 0.0))
    u_vec = (ref - axis * ref.dot(axis)).normalized()
    v_vec = axis.cross(u_vec).normalized()

    rings = []
    for i in range(segs_u):
        a = 2.0 * math.pi * i / segs_u
        radial = u_vec * math.cos(a) + v_vec * math.sin(a)
        c_pt = center + radial * radius
        ring = []
        for j in range(segs_v):
            b = 2.0 * math.pi * j / segs_v
            pt = c_pt + (radial * math.cos(b) + axis * math.sin(b)) * tube_r
            ring.append(bm.verts.new(pt))
        rings.append(ring)

    for i in range(segs_u):
        r0 = rings[i]
        r1 = rings[(i + 1) % segs_u]
        for j in range(segs_v):
            jn = (j + 1) % segs_v
            f = bm.faces.new((r0[j], r1[j], r1[jn], r0[jn]))
            f.smooth = True
            f.material_index = mat_idx


def _drape_braid_points(sh, root_pt, root_norm, length, out_bias, side_bias, back_bias, steps=14, wave_amp=0.004, seed=0, clear_face=True):
    """Generate a smooth gravity-draped strand curve starting at root_pt that clears face, ears, neck & shoulders."""
    pts = [root_pt.copy()]
    cur = root_pt.copy()
    step_len = length / steps
    vel = (root_norm * out_bias + Vector((side_bias, back_bias, -0.35))).normalized()
    grav = Vector((0.0, 0.0, -1.0))

    for s in range(1, steps + 1):
        t = s / steps
        vel = (vel * (1.0 - 0.28) + grav * 0.45 + Vector((side_bias * 0.12, back_bias * 0.10, 0.0))).normalized()
        nxt = cur + vel * step_len
        if wave_amp > 0.0:
            nxt.x += math.sin(t * math.pi * 3.0 + seed) * wave_amp * t
            nxt.y += math.cos(t * math.pi * 2.5 + seed * 1.3) * wave_amp * t
        # Keep strands parted cleanly around the face (eyes/nose/mouth stay completely unobstructed!)
        if clear_face and nxt.y < sh.eye_y + 0.055 and nxt.z < sh.eye_z + 0.048 and nxt.z > sh.eye_z - 0.115:
            min_face_x = 0.068
            if abs(nxt.x) < min_face_x:
                sgn = 1.0 if (nxt.x > 0.0 or (abs(nxt.x) < 1e-4 and side_bias >= 0.0)) else -1.0
                nxt.x = sgn * min_face_x
        nxt = sh.push_outside_body(nxt, clearance=0.0065)
        vel = (nxt - cur).normalized() if (nxt - cur).length > 1e-5 else vel
        pts.append(nxt)
        cur = nxt
    return pts


def _finalize_hair_object(name, bm, mat_list, collection, char_info, long_hair=False):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for m in mat_list:
        obj.data.materials.append(m)

    # Short hairstyles bind cleanly to Head; long draped braids/locs bind via k-NN to Head/Neck/Shoulders
    attach_modular_mesh(
        char_info,
        obj,
        rigid_bone=None if long_hair else "mixamorig:Head",
        transfer_shapekeys=True,
        key_prefix_filter=("Face_Preset_",),
        k=4,
    )
    return obj


# ===========================================================================
# 10 MALE HAIRSTYLES
# ===========================================================================
def build_male_hairstyles(char_info, parent_col):
    """Build all 10 modular 3D West African Male hairstyles in dedicated child collection."""
    sh = _ScalpHelper(char_info)
    mat_coily = create_afro_hair_material("Ivory_Hair_Male_Coily", style="coily", tint_srgb=(0.022, 0.018, 0.016))
    mat_waves = create_afro_hair_material("Ivory_Hair_Male_Waves", style="waves", tint_srgb=(0.020, 0.016, 0.014))
    mat_braid = create_afro_hair_material("Ivory_Hair_Male_Braid", style="braid", tint_srgb=(0.022, 0.018, 0.016))
    mat_gold = create_metal_material("Ivory_Hair_GoldBead", (0.90, 0.72, 0.24), roughness=0.22)

    hair_col = bpy.data.collections.new("Hair_Male")
    parent_col.children.link(hair_col)
    created = {}

    ez = sh.eye_z
    ey = sh.eye_y
    top_z = sh.top_z

    # --- 01: Low Fade Buzz ---
    def h01_fn(co, no, sdf):
        t_top = max(0.0, min(1.0, (co.z - (ez + 0.040)) / 0.055))
        t_edge = max(0.0, min(1.0, (sdf + 0.002) / 0.016))
        return (0.0018 + 0.0072 * t_top) * t_edge

    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=h01_fn)
    created["01_LowFade"] = _finalize_hair_object("Male_Hair_01_LowFade", bm, [mat_coily], hair_col, char_info)

    # --- 02: High-Top Fade ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0022 * max(0.0, min(1.0, (sdf + 0.003) / 0.015)))
    bm_top = bmesh.new()
    segs = 28
    rings_z = 8
    top_base_z = top_z - 0.044
    top_peak_z = top_z + 0.046
    ht_rings = []
    for rz_i in range(rings_z):
        t = rz_i / (rings_z - 1)
        z = top_base_z + t * (top_peak_z - top_base_z)
        rx = 0.072 + 0.005 * math.sin(t * math.pi * 0.70)
        ry = 0.092 + 0.005 * math.sin(t * math.pi * 0.70)
        if rz_i == rings_z - 1:
            rx *= 0.90; ry *= 0.90
        ring = []
        for s in range(segs):
            a = 2.0 * math.pi * s / segs
            # Slightly squared front hairline, rounded back
            ca, sa = math.cos(a), math.sin(a)
            sq = 1.0 + 0.08 * (abs(ca * sa))
            curl = 1.0 + 0.025 * math.sin(a * 11.0 + t * 9.0)
            px = ca * rx * sq * curl
            py = (ey + 0.086) + sa * ry * sq * curl
            pz = z + (0.003 * math.sin(a * 7.0) if rz_i == rings_z - 1 else 0.0)
            ring.append(bm_top.verts.new((px, py, pz)))
        ht_rings.append(ring)
    for i in range(rings_z - 1):
        for s in range(segs):
            sn = (s + 1) % segs
            f = bm_top.faces.new((ht_rings[i][s], ht_rings[i][sn], ht_rings[i + 1][sn], ht_rings[i + 1][s]))
            f.smooth = True
    # Cap top flat surface with subtle dome
    c_top = bm_top.verts.new((0.0, ey + 0.086, top_peak_z + 0.004))
    for s in range(segs):
        sn = (s + 1) % segs
        f = bm_top.faces.new((ht_rings[-1][s], ht_rings[-1][sn], c_top))
        f.smooth = True
    # Merge bm_top into bm
    tmp_m = bpy.data.meshes.new("__ht_tmp"); bm_top.to_mesh(tmp_m); bm_top.free()
    bm.from_mesh(tmp_m); bpy.data.meshes.remove(tmp_m)
    created["02_HighTopFade"] = _finalize_hair_object("Male_Hair_02_HighTopFade", bm, [mat_coily], hair_col, char_info)

    # --- 03: Short Afro Taper ---
    def h03_fn(co, no, sdf):
        t_z = max(0.0, min(1.0, (co.z - (ez + 0.025)) / 0.085))
        t_edge = max(0.0, min(1.0, (sdf + 0.002) / 0.024))
        curl = 0.0035 * math.sin(co.x * 95.0) * math.cos(co.y * 95.0)
        return (0.003 + 0.032 * (t_z ** 0.85) + curl * t_z) * t_edge

    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=2, height_fn=h03_fn)
    created["03_ShortAfroTaper"] = _finalize_hair_object("Male_Hair_03_ShortAfroTaper", bm, [mat_coily], hair_col, char_info)

    # --- 04: 360 Waves ---
    def h04_fn(co, no, sdf):
        t_edge = max(0.0, min(1.0, (sdf + 0.002) / 0.015))
        r = math.hypot(co.x, co.y - (ey + 0.10))
        ripple = 0.0012 * math.sin(r * 165.0)
        return (0.0055 + ripple) * t_edge

    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=2, height_fn=h04_fn)
    created["04_Waves360"] = _finalize_hair_object("Male_Hair_04_Waves360", bm, [mat_waves], hair_col, char_info)

    # --- 05: Sponge Twists (Drop Fade + 95 3D Crown Twists) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0028 * max(0.0, min(1.0, (sdf + 0.003) / 0.018)))
    rng = random.Random(105)
    for idx in range(96):
        # Fibonacci dome sampling over upper crown
        fi = (idx + 0.5) / 96.0
        phi = math.acos(1.0 - fi * 0.52)  # Upper cap (0..61 deg from zenith)
        theta = idx * 2.399963
        dvec = Vector((math.sin(phi) * math.cos(theta) * 0.92, math.sin(phi) * math.sin(theta) * 1.15, math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.001)
        if root.z < ez + 0.048:
            continue
        t_len = rng.uniform(0.026, 0.042) * (1.0 - 0.35 * fi)
        dir_t = (norm + Vector((rng.uniform(-0.18, 0.18), rng.uniform(-0.18, 0.18), 0.25))).normalized()
        pts = [root + dir_t * (t_len * k / 4.0) for k in range(5)]
        rads = [0.0068, 0.0062, 0.0055, 0.0044, 0.0024]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.18, ripple_freq=2.8)
    created["05_SpongeTwists"] = _finalize_hair_object("Male_Hair_05_SpongeTwists", bm, [mat_coily], hair_col, char_info)

    # --- 06: Classic Cornrows (9 crisp front-to-back scalp-hugging braids) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0012 * max(0.0, min(1.0, (sdf + 0.002) / 0.015)))
    n_rows = 9
    for r_i in range(n_rows):
        u = (r_i / (n_rows - 1)) * 2.0 - 1.0  # -1..+1 across width
        lat_ang = u * 1.05  # radians left/right
        pts = []
        rads = []
        steps = 22
        for s in range(steps):
            t = s / (steps - 1)  # 0 = front hairline, 1 = nape
            pitch = -0.82 + t * 2.25  # front -> top -> back nape
            dvec = Vector((math.sin(lat_ang) * 0.86, math.sin(pitch) * math.cos(lat_ang * 0.65), math.cos(pitch) * math.cos(lat_ang * 0.72)))
            p, n = sh.project_to_scalp(dvec, offset=0.0055)
            pts.append(p)
            rads.append(0.0052 + 0.0018 * math.sin(t * math.pi))
        # Short hanging tail at nape (3.5cm)
        tail_start = pts[-1]
        for k in range(1, 5):
            tp = sh.push_outside_body(tail_start + Vector((u * 0.004 * k, 0.005 * k, -0.009 * k)), clearance=0.005)
            pts.append(tp)
            rads.append(0.0046 * (1.0 - 0.12 * k))
        _add_tube_strand(bm, pts, rads, segs=7, ripple_amp=0.18, ripple_freq=3.0)
    created["06_Cornrows"] = _finalize_hair_object("Male_Hair_06_Cornrows", bm, [mat_braid], hair_col, char_info)

    # --- 07: Short Freeform Locs (56 locs draping around crown, temples & nape) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0035 * max(0.0, min(1.0, (sdf + 0.003) / 0.020)))
    rng = random.Random(107)
    for idx in range(56):
        fi = (idx + 0.5) / 56.0
        phi = math.acos(1.0 - fi * 0.78)
        theta = idx * 2.399963
        dvec = Vector((math.sin(phi) * math.cos(theta), math.sin(phi) * math.sin(theta), math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.003)
        if sh.hairline_sdf(root) < 0.005:
            continue
        loc_len = rng.uniform(0.095, 0.145)
        side_b = 0.65 * math.cos(theta)
        back_b = 0.65 * math.sin(theta)
        # Keep front bangs slightly shorter so eyes stay visible
        if root.y < ey + 0.055:
            loc_len *= 0.68
            side_b += 0.35 if root.x >= 0 else -0.35
        pts = _drape_braid_points(sh, root, norm, loc_len, 0.65, side_b, back_b, steps=10, wave_amp=0.004, seed=idx)
        rads = [0.0058 * (1.0 - 0.22 * (k / len(pts))) for k in range(len(pts))]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.14, ripple_freq=2.5)
    created["07_ShortLocs"] = _finalize_hair_object("Male_Hair_07_ShortLocs", bm, [mat_braid], hair_col, char_info, long_hair=True)

    # --- 08: High-Top Locs (Faded sides + 44 crown locs sweeping forward & sideways) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0022 * max(0.0, min(1.0, (sdf + 0.003) / 0.016)))
    rng = random.Random(108)
    for idx in range(44):
        fi = (idx + 0.5) / 44.0
        phi = math.acos(1.0 - fi * 0.44)  # Crown only!
        theta = idx * 2.399963
        dvec = Vector((math.sin(phi) * math.cos(theta) * 0.88, math.sin(phi) * math.sin(theta) * 1.12, math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.003)
        loc_len = rng.uniform(0.115, 0.165)
        side_b = 0.85 * math.cos(theta)
        back_b = 0.55 * math.sin(theta) - 0.15
        pts = _drape_braid_points(sh, root, norm, loc_len, 0.90, side_b, back_b, steps=11, wave_amp=0.005, seed=idx + 30)
        rads = [0.0062 * (1.0 - 0.18 * (k / len(pts))) for k in range(len(pts))]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.15, ripple_freq=2.6)
        # Add subtle gold cuff on 5 front-side locs
        if idx in (5, 12, 21, 29, 36) and len(pts) > 7:
            _add_ring_band(bm, pts[7], pts[8] - pts[6], 0.0064, tube_r=0.0018, segs_u=12, segs_v=6, mat_idx=1)
    created["08_HighTopLocs"] = _finalize_hair_object("Male_Hair_08_HighTopLocs", bm, [mat_braid, mat_gold], hair_col, char_info, long_hair=True)

    # --- 09: Full Classic Afro ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.005 * max(0.0, min(1.0, (sdf + 0.003) / 0.015)))
    afro_c = Vector((0.0, ey + 0.095, top_z - 0.025))
    _add_coily_puff_sphere(bm, afro_c, rx=0.132, ry=0.145, rz=0.128, subdiv=4, noise_amp=0.013, seed=9)
    # Trim any afro sphere vertices protruding below the forehead brow/eyes
    for v in bm.verts:
        if v.co.y < ey + 0.025 and v.co.z < ez + 0.052:
            v.co.z = max(v.co.z, ez + 0.052)
    created["09_FullAfro"] = _finalize_hair_object("Male_Hair_09_FullAfro", bm, [mat_coily], hair_col, char_info)

    # --- 10: Burst Fade Mohawk ---
    def h10_fn(co, no, sdf):
        t_edge = max(0.0, min(1.0, (sdf + 0.002) / 0.016))
        # Tall crest along central strip |x| < 0.042 running front to nape
        crest = math.exp(-0.5 * (co.x / 0.036) ** 2.0)
        curl = 0.004 * math.sin(co.y * 85.0 + co.z * 75.0)
        return (0.0022 + (0.054 + curl) * crest) * t_edge

    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=2, height_fn=h10_fn)
    created["10_BurstFadeMohawk"] = _finalize_hair_object("Male_Hair_10_BurstFadeMohawk", bm, [mat_coily], hair_col, char_info)

    return created, hair_col


# ===========================================================================
# 10 FEMALE HAIRSTYLES
# ===========================================================================
def build_female_hairstyles(char_info, parent_col):
    """Build all 10 modular 3D West African Female hairstyles in dedicated child collection."""
    sh = _ScalpHelper(char_info)
    mat_coily = create_afro_hair_material("Ivory_Hair_Fem_Coily", style="coily", tint_srgb=(0.022, 0.018, 0.016))
    mat_braid = create_afro_hair_material("Ivory_Hair_Fem_Braid", style="braid", tint_srgb=(0.020, 0.016, 0.014))
    mat_burg = create_afro_hair_material("Ivory_Hair_Fem_WarmBrown", style="braid", tint_srgb=(0.082, 0.036, 0.024))
    mat_band = create_fabric_material("Ivory_Hair_TieBlack", (0.08, 0.08, 0.09), roughness=0.72)
    mat_gold = create_metal_material("Ivory_Hair_FemGold", (0.92, 0.74, 0.26), roughness=0.20)

    hair_col = bpy.data.collections.new("Hair_Female")
    parent_col.children.link(hair_col)
    created = {}

    ez = sh.eye_z
    ey = sh.eye_y
    top_z = sh.top_z

    # --- 01: Short Natural TWA ---
    def fh01_fn(co, no, sdf):
        t_edge = max(0.0, min(1.0, (sdf + 0.002) / 0.020))
        curl = 0.0038 * math.sin(co.x * 115.0) * math.cos(co.y * 110.0) * math.sin(co.z * 115.0)
        return (0.020 + curl) * t_edge

    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=2, height_fn=fh01_fn)
    created["01_ShortTWA"] = _finalize_hair_object("Female_Hair_01_ShortTWA", bm, [mat_coily], hair_col, char_info)

    # --- 02: Full Natural Afro Crown ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.004 * max(0.0, min(1.0, (sdf + 0.002) / 0.014)))
    afro_c = Vector((0.0, ey + 0.096, top_z - 0.022))
    _add_coily_puff_sphere(bm, afro_c, rx=0.140, ry=0.145, rz=0.132, subdiv=4, noise_amp=0.0055, seed=22)
    for v in bm.verts:
        if v.co.y < ey + 0.022 and v.co.z < ez + 0.048:
            v.co.z = max(v.co.z, ez + 0.048)
    created["02_NaturalAfroCrown"] = _finalize_hair_object("Female_Hair_02_NaturalAfroCrown", bm, [mat_coily], hair_col, char_info)

    # --- 03: High Afro Puff ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0038 * max(0.0, min(1.0, (sdf + 0.002) / 0.016)))
    puff_base = Vector((0.0, ey + 0.104, top_z - 0.004))
    puff_center = Vector((0.0, ey + 0.112, top_z + 0.064))
    _add_ring_band(bm, puff_base, Vector((0.0, 0.25, 1.0)), radius=0.042, tube_r=0.008, segs_u=24, segs_v=8, mat_idx=1)
    _add_coily_puff_sphere(bm, puff_center, rx=0.088, ry=0.086, rz=0.080, subdiv=4, noise_amp=0.0055, seed=33, mat_idx=0)
    created["03_HighAfroPuff"] = _finalize_hair_object("Female_Hair_03_HighAfroPuff", bm, [mat_coily, mat_gold], hair_col, char_info)

    # --- 04: Double Afro Puffs ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0038 * max(0.0, min(1.0, (sdf + 0.002) / 0.016)))
    for side in (-1.0, 1.0):
        b_pt = Vector((side * 0.064, ey + 0.095, top_z - 0.016))
        p_pt = Vector((side * 0.098, ey + 0.100, top_z + 0.032))
        ax = Vector((side * 0.65, 0.10, 0.78))
        _add_ring_band(bm, b_pt, ax, radius=0.030, tube_r=0.007, segs_u=20, segs_v=8, mat_idx=1)
        _add_coily_puff_sphere(bm, p_pt, rx=0.066, ry=0.064, rz=0.062, subdiv=3, noise_amp=0.0052, seed=40 + int(side * 5), mat_idx=0)
    created["04_DoubleAfroPuffs"] = _finalize_hair_object("Female_Hair_04_DoubleAfroPuffs", bm, [mat_coily, mat_band], hair_col, char_info)

    # --- 05: Long Box Braids (50 waist/chest-length braids with gold cuffs) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0035 * max(0.0, min(1.0, (sdf + 0.002) / 0.015)))
    rng = random.Random(205)
    for idx in range(52):
        # Distribute roots cleanly in 3 concentric crown/perimeter tiers
        tier = 0 if idx < 18 else (1 if idx < 36 else 2)
        t_n = 18 if tier < 2 else 16
        t_i = idx if tier == 0 else (idx - 18 if tier == 1 else idx - 36)
        ang = (2.0 * math.pi * t_i / t_n) + tier * 0.21
        phi = [0.35, 0.72, 1.08][tier]
        dvec = Vector((math.sin(phi) * math.cos(ang), math.sin(phi) * math.sin(ang), math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.004)
        if sh.hairline_sdf(root) < 0.002:
            continue
        # Part cleanly away from eyes at center forehead
        side_push = 0.65 * math.cos(ang)
        if abs(root.x) < 0.025 and root.y < ey + 0.06:
            side_push = 0.70 if (idx % 2 == 0) else -0.70
        back_push = 0.50 * math.sin(ang) + 0.25
        blen = rng.uniform(0.34, 0.42)
        pts = _drape_braid_points(sh, root, norm, blen, 0.55, side_push, back_push, steps=18, wave_amp=0.005, seed=idx)
        rads = [0.0056 * (1.0 - 0.18 * (k / len(pts))) for k in range(len(pts))]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.18, ripple_freq=3.0, mat_idx=0)
        if idx % 7 == 0 and len(pts) > 10:
            _add_ring_band(bm, pts[10], pts[11] - pts[9], 0.0062, tube_r=0.0018, segs_u=12, segs_v=6, mat_idx=1)
    created["05_LongBoxBraids"] = _finalize_hair_object("Female_Hair_05_LongBoxBraids", bm, [mat_braid, mat_gold], hair_col, char_info, long_hair=True)

    # --- 06: Fulani Cornrows (Scalp cornrows + long back braids + 2 face-framing beaded side braids) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0015 * max(0.0, min(1.0, (sdf + 0.002) / 0.015)))
    n_rows = 11
    for r_i in range(n_rows):
        u = (r_i / (n_rows - 1)) * 2.0 - 1.0
        lat = u * 1.02
        pts = []
        rads = []
        for s in range(20):
            t = s / 19.0
            pitch = -0.80 + t * 2.15
            dvec = Vector((math.sin(lat) * 0.85, math.sin(pitch) * math.cos(lat * 0.65), math.cos(pitch) * math.cos(lat * 0.72)))
            p, _n = sh.project_to_scalp(dvec, offset=0.005)
            pts.append(p)
            rads.append(0.0050 + 0.0014 * math.sin(t * math.pi))
        # Continue into long draped back braids (24cm)
        tail = _drape_braid_points(sh, pts[-1], Vector((u * 0.25, 0.8, -0.3)).normalized(), 0.24, 0.4, u * 0.35, 0.45, steps=12, wave_amp=0.004, seed=r_i)
        pts.extend(tail[1:])
        rads.extend([0.0048 * (1.0 - 0.20 * (k / len(tail))) for k in range(1, len(tail))])
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.18, ripple_freq=3.0, mat_idx=0)
        if len(pts) > 26:
            _add_ring_band(bm, pts[-3], pts[-2] - pts[-4], 0.0054, tube_r=0.0020, segs_u=12, segs_v=6, mat_idx=1)

    # Signature 2 Fulani side braids framing cheeks with gold beads
    for side in (-1.0, 1.0):
        root, norm = sh.project_to_scalp(Vector((side * 0.78, -0.42, 0.38)), offset=0.004)
        s_pts = _drape_braid_points(sh, root, norm, 0.22, 0.6, side * 0.45, -0.15, steps=12, wave_amp=0.003, seed=int(side * 10))
        s_rads = [0.0048] * len(s_pts)
        _add_tube_strand(bm, s_pts, s_rads, segs=6, ripple_amp=0.18, ripple_freq=3.0, mat_idx=0)
        for bi in (7, 10):
            _add_ring_band(bm, s_pts[bi], s_pts[bi + 1] - s_pts[bi - 1], 0.0056, tube_r=0.0022, segs_u=12, segs_v=6, mat_idx=1)
    created["06_FulaniCornrows"] = _finalize_hair_object("Female_Hair_06_FulaniCornrows", bm, [mat_braid, mat_gold], hair_col, char_info, long_hair=True)

    # --- 07: High Royal Braided Bun ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0042 * max(0.0, min(1.0, (sdf + 0.002) / 0.016)))
    bun_base = Vector((0.0, ey + 0.102, top_z + 0.004))
    _add_ring_band(bm, bun_base, Vector((0.0, 0.12, 1.0)), radius=0.044, tube_r=0.008, segs_u=24, segs_v=8, mat_idx=1)
    # Spiral coiled braided rope forming tall royal bun (4 ascending spiral turns)
    spiral_pts = []
    spiral_rads = []
    n_sp = 64
    for i in range(n_sp):
        t = i / (n_sp - 1)
        ang = t * 4.2 * 2.0 * math.pi
        rad = 0.056 * math.sin(t * math.pi * 0.82 + 0.28)
        z = top_z + 0.014 + t * 0.088
        spiral_pts.append(Vector((math.cos(ang) * rad, (ey + 0.105) + math.sin(ang) * rad, z)))
        spiral_rads.append(0.0155 * (1.0 - 0.15 * t))
    _add_tube_strand(bm, spiral_pts, spiral_rads, segs=8, ripple_amp=0.14, ripple_freq=2.8, mat_idx=0)
    created["07_HighBraidedBun"] = _finalize_hair_object("Female_Hair_07_HighBraidedBun", bm, [mat_braid, mat_gold], hair_col, char_info)

    # --- 08: Chic Braided Bob (Chin-length blunt-cut 3D braided bob) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.0038 * max(0.0, min(1.0, (sdf + 0.002) / 0.016)))
    bob_cut_z = ez - 0.088  # Crisp chin/jawline bob hem
    for idx in range(46):
        tier = 0 if idx < 22 else 1
        t_n = 22 if tier == 0 else 24
        t_i = idx if tier == 0 else idx - 22
        ang = (2.0 * math.pi * t_i / t_n) + tier * 0.15
        phi = 0.42 if tier == 0 else 0.88
        dvec = Vector((math.sin(phi) * math.cos(ang), math.sin(phi) * math.sin(ang), math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.004)
        if sh.hairline_sdf(root) < 0.002:
            continue
        side_push = 0.72 * math.cos(ang)
        if root.y < ey + 0.055 and abs(root.x) < 0.030:
            side_push = 0.85 if root.x >= -0.008 else -0.85  # Sleek slight side part
        back_push = 0.42 * math.sin(ang) + 0.12
        # Compute length so every strand terminates cleanly near bob_cut_z
        blen = max(0.11, (root.z - bob_cut_z) * 1.18)
        pts = _drape_braid_points(sh, root, norm, blen, 0.65, side_push, back_push, steps=12, wave_amp=0.002, seed=idx)
        # Taper softly inward at bob hem
        if len(pts) >= 2:
            pts[-1].x *= 0.94
        rads = [0.0056 * (1.0 - 0.12 * (k / len(pts))) for k in range(len(pts))]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.18, ripple_freq=3.0, mat_idx=0)
    created["08_BraidedBob"] = _finalize_hair_object("Female_Hair_08_BraidedBob", bm, [mat_braid], hair_col, char_info, long_hair=True)

    # --- 09: Goddess Locs (Warm dark-chestnut textured shoulder-draped locs) ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.004 * max(0.0, min(1.0, (sdf + 0.002) / 0.015)))
    rng = random.Random(209)
    for idx in range(48):
        tier = 0 if idx < 16 else (1 if idx < 32 else 2)
        t_n = 16
        t_i = idx % 16
        ang = (2.0 * math.pi * t_i / t_n) + tier * 0.24
        phi = [0.36, 0.72, 1.05][tier]
        dvec = Vector((math.sin(phi) * math.cos(ang), math.sin(phi) * math.sin(ang), math.cos(phi)))
        root, norm = sh.project_to_scalp(dvec, offset=0.004)
        if sh.hairline_sdf(root) < 0.002:
            continue
        side_push = 0.70 * math.cos(ang)
        if root.y < ey + 0.055 and abs(root.x) < 0.028:
            side_push = 0.80 if (idx % 2 == 0) else -0.80
        blen = rng.uniform(0.24, 0.31)
        pts = _drape_braid_points(sh, root, norm, blen, 0.65, side_push, 0.45 * math.sin(ang) + 0.20, steps=15, wave_amp=0.009, seed=idx + 17)
        rads = [0.0068 * (1.0 - 0.24 * (k / len(pts))) for k in range(len(pts))]
        _add_tube_strand(bm, pts, rads, segs=6, ripple_amp=0.16, ripple_freq=2.4, mat_idx=0)
    created["09_GoddessLocs"] = _finalize_hair_object("Female_Hair_09_GoddessLocs", bm, [mat_burg], hair_col, char_info, long_hair=True)

    # --- 10: Voluminous Curly Side-Part ---
    bm = sh.build_scalp_cap_bmesh(subdivide_cuts=1, height_fn=lambda co, no, sdf: 0.005 * max(0.0, min(1.0, (sdf + 0.002) / 0.015)))
    # Sculpted overlapping lush curly cloud clusters sweeping dramatically from a left side-part down over shoulders
    clusters = [
        # (center_offset_from_skull_c, rx, ry, rz, seed)
        (Vector(( 0.055, -0.005,  0.078)), 0.072, 0.088, 0.062, 1),  # High swept right-side crown volume
        (Vector((-0.048,  0.012,  0.062)), 0.058, 0.078, 0.052, 2),  # Left side crown
        (Vector(( 0.095, -0.012,  0.015)), 0.064, 0.075, 0.088, 3),  # Right face-framing cascade
        (Vector(( 0.102,  0.008, -0.072)), 0.062, 0.068, 0.082, 4),  # Right lower shoulder curls
        (Vector((-0.085,  0.015, -0.008)), 0.054, 0.072, 0.078, 5),  # Left side curls
        (Vector((-0.090,  0.025, -0.078)), 0.055, 0.064, 0.072, 6),  # Left lower curls
        (Vector(( 0.000,  0.088,  0.032)), 0.092, 0.078, 0.088, 7),  # Back upper volume
        (Vector(( 0.000,  0.092, -0.058)), 0.105, 0.070, 0.092, 8),  # Back lower cascade
    ]
    for off, rx, ry, rz, sd in clusters:
        _add_coily_puff_sphere(bm, sh.skull_c + off, rx, ry, rz, subdiv=3, noise_amp=0.012, seed=sd * 13, mat_idx=0)
    # Push any vertex gently clear of face/cheeks/shoulders
    for v in bm.verts:
        if v.co.y < ey + 0.035 and abs(v.co.x) < 0.058 and v.co.z < ez + 0.050:
            v.co.x += 0.035 if v.co.x >= 0 else -0.035
    created["10_CurlySidePart"] = _finalize_hair_object("Female_Hair_10_CurlySidePart", bm, [mat_coily], hair_col, char_info, long_hair=True)

    return created, hair_col
