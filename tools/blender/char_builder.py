"""Procedural MPFB2 character basemesh builder for Ivory State (v2 — High-Detail Anatomy & 10+10 Faces).

Generates rigged, A-pose West African Male and Female basemeshes in minimal
undergarments with:
  - Athletic anatomical muscle targets + procedural clavicle/pectoral/abs/spine/patella relief
  - 10 distinct West African / Ivorian Male Face Presets & 10 Female Face Presets (Shape Keys)
  - Body Morph Shape Keys (Skinny/Muscular/Heavy for Male; Slender/Muscular/Curvy_Full/Bust/Hip for Female)
  - AO-enhanced West African skin shader, eyeballs, sculpted eyelashes, UV-feathered eyebrows, clean buzzcut
  - Compression-molded Female sports bra (zero nipple pokes) & clean elastic boxer briefs / shorts
  - Full mixamo_unity rig, 3D attachment sockets, and modular wearable attachment API.
"""

import math
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

from char_materials import (
    DEFAULT_SKIN_INDEX,
    apply_skin_tone_to_material,
    create_buzzcut_material,
    create_eye_material,
    create_eyebrow_material,
    create_eyelash_material,
    create_ivory_skin_material,
    create_undergarment_material,
)


# ---------------------------------------------------------------------------
# 10 Distinct West African / Ivorian Face Presets per sex:
#   (shapekey_name, display_label, default_skin_tone_index, mpfb_target_list)
# ---------------------------------------------------------------------------
MALE_FACE_PRESETS = [
    ("Face_Preset_01", "M01 Kouadio", 3, [
        ("head-oval", 0.22), ("chin-prominent-incr", 0.22),
        ("l-cheek-bones-incr", 0.26), ("r-cheek-bones-incr", 0.26),
        ("mouth-lowerlip-volume-incr", 0.16),
    ]),
    ("Face_Preset_02", "M02 Traore", 1, [
        ("head-square", 0.46), ("chin-width-incr", 0.50), ("chin-bones-incr", 0.42),
        ("nose-width2-incr", 0.40), ("nose-flaring-incr", 0.34),
        ("eyebrows-trans-down", 0.26),
    ]),
    ("Face_Preset_03", "M03 Konan", 5, [
        ("head-oval", 0.38), ("head-fat-decr", 0.44),
        ("nose-width1-decr", 0.38), ("nose-scale-horiz-decr", 0.28),
        ("chin-prominent-incr", 0.40), ("forehead-scale-vert-incr", 0.20),
    ]),
    ("Face_Preset_04", "M04 Bamba", 2, [
        ("head-round", 0.52), ("head-fat-incr", 0.46),
        ("l-cheek-volume-incr", 0.44), ("r-cheek-volume-incr", 0.44),
        ("mouth-upperlip-volume-incr", 0.32), ("mouth-lowerlip-volume-incr", 0.38),
        ("nose-nostrils-width-incr", 0.32),
    ]),
    ("Face_Preset_05", "M05 Yao", 0, [
        ("head-diamond", 0.46), ("l-cheek-bones-incr", 0.56), ("r-cheek-bones-incr", 0.56),
        ("chin-triangle", 0.40), ("chin-cleft-incr", 0.36),
        ("l-eye-eyefold-down", 0.25), ("r-eye-eyefold-down", 0.25), ("eyebrows-angle-down", 0.26),
    ]),
    ("Face_Preset_06", "M06 Diallo", 6, [
        ("head-rectangular", 0.40), ("head-scale-vert-incr", 0.14),
        ("chin-height-incr", 0.36), ("nose-scale-vert-incr", 0.32),
        ("nose-curve-convex", 0.28), ("forehead-nubian-incr", 0.26),
    ]),
    ("Face_Preset_07", "M07 Kouassi", 4, [
        ("l-eye-scale-incr", 0.32), ("r-eye-scale-incr", 0.32),
        ("l-eye-trans-out", 0.20), ("r-eye-trans-out", 0.20),
        ("nose-point-up", 0.32), ("mouth-cupidsbow-incr", 0.42),
        ("head-age-decr", 0.32), ("chin-width-decr", 0.22),
    ]),
    ("Face_Preset_08", "M08 Soro", 2, [
        ("head-age-incr", 0.34), ("forehead-trans-forward", 0.30),
        ("eyebrows-trans-down", 0.32), ("chin-jaw-drop-incr", 0.38),
        ("mouth-scale-horiz-incr", 0.32), ("nose-hump-incr", 0.36),
    ]),
    ("Face_Preset_09", "M09 N'Guessan", 7, [
        ("head-triangular", 0.42), ("chin-width-incr", 0.32),
        ("forehead-temple-decr", 0.28),
        ("l-cheek-inner-incr", 0.34), ("r-cheek-inner-incr", 0.34),
        ("nose-point-down", 0.28), ("l-ear-scale-incr", 0.22), ("r-ear-scale-incr", 0.22),
    ]),
    ("Face_Preset_10", "M10 Ouattara", 8, [
        ("head-invertedtriangular", 0.44), ("chin-width-decr", 0.36),
        ("l-cheek-bones-incr", 0.42), ("r-cheek-bones-incr", 0.42),
        ("l-eye-corner2-up", 0.22), ("r-eye-corner2-up", 0.22),
        ("mouth-upperlip-volume-incr", 0.34), ("mouth-lowerlip-volume-incr", 0.28),
    ]),
]

FEMALE_FACE_PRESETS = [
    ("Face_Preset_01", "F01 Aya", 3, [
        ("head-oval", 0.28), ("mouth-cupidsbow-incr", 0.34),
        ("l-cheek-bones-incr", 0.26), ("r-cheek-bones-incr", 0.26),
        ("l-eye-scale-incr", 0.18), ("r-eye-scale-incr", 0.18),
    ]),
    ("Face_Preset_02", "F02 Aminata", 0, [
        ("head-diamond", 0.38), ("l-cheek-bones-incr", 0.54), ("r-cheek-bones-incr", 0.54),
        ("chin-triangle", 0.34), ("l-eye-corner2-up", 0.28), ("r-eye-corner2-up", 0.28),
        ("head-fat-decr", 0.28), ("nose-width1-decr", 0.28),
    ]),
    ("Face_Preset_03", "F03 Fatou", 2, [
        ("head-round", 0.48), ("head-fat-incr", 0.36),
        ("l-cheek-volume-incr", 0.42), ("r-cheek-volume-incr", 0.42),
        ("mouth-upperlip-volume-incr", 0.36), ("mouth-lowerlip-volume-incr", 0.40),
        ("chin-height-decr", 0.20),
    ]),
    ("Face_Preset_04", "F04 Adjoua", 5, [
        ("head-invertedtriangular", 0.44),
        ("l-eye-scale-incr", 0.36), ("r-eye-scale-incr", 0.36),
        ("chin-width-decr", 0.38), ("chin-height-decr", 0.18),
        ("nose-scale-horiz-decr", 0.28), ("eyebrows-angle-up", 0.28),
    ]),
    ("Face_Preset_05", "F05 Mariam", 1, [
        ("head-square", 0.34), ("chin-width-incr", 0.24), ("chin-bones-incr", 0.26),
        ("nose-flaring-incr", 0.36), ("nose-width2-incr", 0.30),
        ("forehead-nubian-incr", 0.26), ("mouth-upperlip-volume-incr", 0.28),
    ]),
    ("Face_Preset_06", "F06 Aissata", 4, [
        ("head-rectangular", 0.30), ("head-scale-vert-incr", 0.14),
        ("chin-height-incr", 0.24), ("nose-scale-vert-incr", 0.26),
        ("nose-point-up", 0.26), ("l-eye-eyefold-up", 0.24), ("r-eye-eyefold-up", 0.24),
    ]),
    ("Face_Preset_07", "F07 Bintou", 6, [
        ("head-age-decr", 0.34), ("forehead-scale-vert-incr", 0.18),
        ("nose-point-up", 0.38), ("nose-curve-concave", 0.30),
        ("mouth-scale-horiz-decr", 0.20), ("mouth-lowerlip-volume-incr", 0.36),
    ]),
    ("Face_Preset_08", "F08 Clarisse", 3, [
        ("head-age-incr", 0.26), ("l-cheek-bones-incr", 0.44), ("r-cheek-bones-incr", 0.44),
        ("head-fat-decr", 0.28), ("chin-prominent-incr", 0.28),
        ("eyebrows-trans-up", 0.26), ("mouth-angles-up", 0.24),
    ]),
    ("Face_Preset_09", "F09 Salimata", 8, [
        ("head-scale-horiz-incr", 0.22),
        ("l-cheek-inner-incr", 0.38), ("r-cheek-inner-incr", 0.38),
        ("mouth-scale-horiz-incr", 0.32), ("mouth-dimples-out", 0.34),
        ("nose-point-width-incr", 0.32), ("l-eye-scale-incr", 0.22), ("r-eye-scale-incr", 0.22),
    ]),
    ("Face_Preset_10", "F10 Kadiatou", 7, [
        ("head-oval", 0.38), ("forehead-temple-decr", 0.28),
        ("l-eye-corner2-up", 0.26), ("r-eye-corner2-up", 0.26),
        ("mouth-cupidsbow-incr", 0.46), ("chin-prominent-incr", 0.30),
    ]),
]


_MPFB_SERVICES = None


def ensure_mpfb_enabled():
    import addon_utils
    enabled = {a.module for a in bpy.context.preferences.addons}
    for mod in addon_utils.modules():
        if "mpfb" in mod.__name__:
            if mod.__name__ not in enabled:
                addon_utils.enable(mod.__name__, default_set=False, persistent=False)
            return


def _get_mpfb_services():
    global _MPFB_SERVICES
    if _MPFB_SERVICES is not None:
        return _MPFB_SERVICES
    ensure_mpfb_enabled()
    from bl_ext.blender_org.mpfb.services import (
        HumanService,
        MaterialService,
        NodeService,
        ObjectService,
        RigService,
        TargetService,
    )
    _MPFB_SERVICES = (HumanService, MaterialService, NodeService, ObjectService, RigService, TargetService)
    return _MPFB_SERVICES


def _build_full_mesh_with_targets(macro_dict, extra_targets, scale=0.1):
    """Create a full 19158-vert hm08 basemesh with macro details + extra targets baked into Basis."""
    HumanService, _, _, _, _, TargetService = _get_mpfb_services()
    obj = HumanService.create_human(
        mask_helpers=False,
        detailed_helpers=True,
        extra_vertex_groups=True,
        feet_on_ground=False,
        scale=scale,
        macro_detail_dict=macro_dict,
    )
    for tname, weight in extra_targets:
        if abs(weight) < 1e-4:
            continue
        path = TargetService.target_full_path(tname)
        if path:
            TargetService.load_target(obj, path, weight=weight)
        else:
            print(f"[WARN] Target not found: {tname}", flush=True)
    TargetService.bake_targets(obj)
    return obj


def _ground_body_z_zero(obj):
    """Shift vertices vertically so the lowest 'body' vertex touches Z = 0."""
    body_vg = obj.vertex_groups.get("body")
    bidx = body_vg.index
    min_z = min(v.co.z for v in obj.data.vertices if any(g.group == bidx for g in v.groups))
    for v in obj.data.vertices:
        v.co.z -= min_z
    obj.data.update()
    return min_z


def _get_coords_array(mesh):
    n = len(mesh.vertices)
    arr = np.empty(n * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", arr)
    return arr.reshape((n, 3))


def _set_coords_array(mesh, arr):
    mesh.vertices.foreach_set("co", arr.ravel().astype(np.float32))
    mesh.update()


def _vg_center_and_radius(obj, vg_name):
    vg = obj.vertex_groups.get(vg_name)
    if not vg:
        return None, 0.0
    idx = vg.index
    pts = np.array([v.co[:] for v in obj.data.vertices if any(g.group == idx for g in v.groups)], dtype=np.float64)
    if len(pts) == 0:
        return None, 0.0
    center = pts.mean(axis=0)
    radius = np.linalg.norm(pts - center, axis=1).mean()
    return Vector(center), float(radius)


def _extract_submesh_by_vgroup(src_obj, vgroup_name, new_name, collection):
    """Create a new mesh object containing only vertices in vgroup_name, preserving UVs & vertex groups."""
    keep_idx = src_obj.vertex_groups[vgroup_name].index
    keep_set = {v.index for v in src_obj.data.vertices if any(g.group == keep_idx for g in v.groups)}

    bm = bmesh.new()
    bm.from_mesh(src_obj.data)
    bm.verts.ensure_lookup_table()

    old_to_new = {}
    new_idx = 0
    to_del = []
    for v in bm.verts:
        if v.index in keep_set:
            old_to_new[v.index] = new_idx
            new_idx += 1
        else:
            to_del.append(v)
    bmesh.ops.delete(bm, geom=to_del, context="VERTS")

    mesh = bpy.data.meshes.new(new_name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new(new_name, mesh)
    collection.objects.link(obj)

    for vg in src_obj.vertex_groups:
        if vg.name.startswith("helper-") or vg.name.startswith("joint-") or vg.name in ("HelperGeometry", "JointCubes"):
            continue
        nvg = obj.vertex_groups.new(name=vg.name)
        for old_i, new_i in old_to_new.items():
            for g in src_obj.data.vertices[old_i].groups:
                if g.group == vg.index:
                    nvg.add([new_i], g.weight, "REPLACE")

    return obj, old_to_new


def _sculpt_anatomical_detail(body_obj, is_female=False):
    """Add smooth, realistic anatomical surface relief (clavicles, pecs, abs, spinal furrow, scapula, knees)."""
    bm = bmesh.new()
    bm.from_mesh(body_obj.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    verts_z = [v.co.z for v in bm.verts]
    h = max(verts_z) - min(verts_z)

    def gauss(val, center, sigma):
        d = (val - center) / sigma
        return math.exp(-0.5 * d * d)

    for v in bm.verts:
        x, y, z = v.co.x, v.co.y, v.co.z
        ax = abs(x)
        zn = z / h
        no = v.normal
        disp = 0.0

        # 1. Clavicles (collarbone ridge + sub-clavicular hollow + suprasternal notch)
        if y < -0.015 and 0.77 < zn < 0.855 and ax < 0.165:
            z_clav = 0.817 + 0.011 * min(1.0, ax / 0.14)
            if ax > 0.018:
                clav_x_env = min(1.0, (ax - 0.018) / 0.018) * max(0.0, 1.0 - max(0.0, ax - 0.125) / 0.035)
                amp_clav = 0.0024 if is_female else 0.0028
                disp += amp_clav * clav_x_env * gauss(zn, z_clav, 0.0055)
                disp -= (amp_clav * 0.65) * clav_x_env * gauss(zn, z_clav - 0.011, 0.0065)
                disp -= (amp_clav * 0.45) * clav_x_env * gauss(zn, z_clav + 0.010, 0.0055)
            disp -= 0.0032 * gauss(ax, 0.0, 0.014) * gauss(zn, 0.816, 0.008)

        # 2. Front Torso: Male Pectorals & Athletic Abs OR Female Toned Waist/Linea Alba
        if y < -0.030 and 0.54 < zn < 0.805 and ax < 0.18:
            if not is_female:
                z_pec_bot = 0.718 + 0.014 * ((ax / 0.145) ** 2.2)
                pec_x_env = min(1.0, max(0.0, (ax - 0.012) / 0.025)) * max(0.0, 1.0 - max(0.0, ax - 0.130) / 0.035)
                disp -= 0.0036 * pec_x_env * gauss(zn, z_pec_bot, 0.0075)
                disp += 0.0024 * pec_x_env * gauss(zn, z_pec_bot + 0.026, 0.020)
                disp -= 0.0028 * gauss(ax, 0.0, 0.013) * gauss(zn, 0.750, 0.032)

                ab_z_env = gauss(zn, 0.642, 0.056)
                disp -= 0.0024 * gauss(ax, 0.0, 0.011) * ab_z_env
                disp -= 0.0022 * gauss(ax, 0.068, 0.013) * ab_z_env
                ab_belly_x = gauss(ax, 0.034, 0.017)
                disp += 0.0022 * ab_belly_x * (
                    gauss(zn, 0.688, 0.013) + gauss(zn, 0.652, 0.013) + gauss(zn, 0.616, 0.014)
                )
                disp -= 0.0014 * ab_belly_x * (gauss(zn, 0.670, 0.0055) + gauss(zn, 0.634, 0.0055))
                z_v = 0.548 + (ax - 0.045) * 0.42
                if 0.040 < ax < 0.135:
                    disp -= 0.0018 * gauss(zn, z_v, 0.0075)
            else:
                ab_z_env = gauss(zn, 0.615, 0.044)
                disp -= 0.0016 * gauss(ax, 0.0, 0.011) * ab_z_env
                disp -= 0.0014 * gauss(ax, 0.056, 0.014) * ab_z_env

        # 3. Deltopectoral separation groove
        if y < -0.01 and 0.74 < zn < 0.82 and 0.11 < ax < 0.19:
            x_groove = 0.145 + (zn - 0.77) * 0.25 if not is_female else 0.125 + (zn - 0.77) * 0.22
            amp_dg = 0.0022 if not is_female else 0.0013
            disp -= amp_dg * gauss(ax, x_groove, 0.011) * gauss(zn, 0.785, 0.025)

        # 4. Back Anatomy: Spinal furrow, erector spinae, scapulae & lumbar dimples
        if y > 0.025 and 0.53 < zn < 0.83 and ax < 0.18:
            spine_env = max(0.0, min(1.0, (zn - 0.53) / 0.04)) * max(0.0, min(1.0, (0.83 - zn) / 0.04))
            amp_spine = 0.0034 if not is_female else 0.0028
            disp -= amp_spine * gauss(ax, 0.0, 0.014) * spine_env
            disp += (amp_spine * 0.52) * gauss(ax, 0.038, 0.018) * gauss(zn, 0.64, 0.085)
            scap_amp = 0.0020 if not is_female else 0.0014
            disp += scap_amp * gauss(ax, 0.082, 0.024) * gauss(zn, 0.758, 0.028)
            dimp_amp = 0.0022 if is_female else 0.0014
            disp -= dimp_amp * gauss(ax, 0.040, 0.011) * gauss(zn, 0.556, 0.0085)

        # 5. Kneecap (patella) relief
        if y < -0.015 and 0.24 < zn < 0.34 and 0.04 < ax < 0.16:
            kx = 0.095 if not is_female else 0.085
            disp += 0.0016 * gauss(ax, kx, 0.016) * gauss(zn, 0.280, 0.012)
            disp -= 0.0012 * gauss(ax, kx, 0.020) * gauss(zn, 0.260, 0.008)

        if abs(disp) > 1e-6:
            v.co += no * disp

    bm.to_mesh(body_obj.data)
    bm.free()
    body_obj.data.update()


def _compute_morph_delta_on_full(base_macro, base_extra, morph_macro, morph_extra, base_full_coords):
    tmp = _build_full_mesh_with_targets(morph_macro, morph_extra, scale=0.1)
    _ground_body_z_zero(tmp)
    mcoords = _get_coords_array(tmp.data)
    delta = mcoords - base_full_coords
    bpy.data.objects.remove(tmp, do_unlink=True)
    return delta


def _compute_face_delta_fast(full_obj, face_targets, base_full_coords):
    """Fast exact face preset delta computation directly on full_obj without recreating humans."""
    _, _, _, _, _, TargetService = _get_mpfb_services()
    n = len(base_full_coords)
    delta_total = np.zeros((n, 3), dtype=np.float64)
    buf = np.empty(n * 3, dtype=np.float64)

    for tname, weight in face_targets:
        if abs(weight) < 1e-4:
            continue
        path = TargetService.target_full_path(tname)
        if not path:
            print(f"[WARN] Target not found: {tname}", flush=True)
            continue
        sk = TargetService.load_target(full_obj, path, weight=1.0)
        if sk is not None:
            sk.data.foreach_get("co", buf)
            delta_total += float(weight) * (buf.reshape((n, 3)) - base_full_coords)

    # Clean up temporary shape keys on full_obj (Basis stays untouched)
    if full_obj.data.shape_keys:
        for kb in reversed(list(full_obj.data.shape_keys.key_blocks)):
            full_obj.shape_key_remove(kb)
    return delta_total


def _build_eyeballs(name, l_center, l_rad, r_center, r_rad, collection):
    """Create crisp UV-mapped left and right eyeballs with front-projected dark brown iris UVs."""
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")

    for center, rad in [(l_center, l_rad), (r_center, r_rad)]:
        r = max(0.0122, rad * 1.03)
        ret = bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=18, radius=r)
        verts = ret["verts"]
        for v in verts:
            v.co += center

        vset = set(verts)
        for f in bm.faces:
            if all(v in vset for v in f.verts):
                f.smooth = True
                for loop in f.loops:
                    rel = loop.vert.co - center
                    if rel.y < 0.0:
                        u = 0.5 + (rel.x / (2.0 * r)) * 0.92
                        v_uv = 0.5 + (rel.z / (2.0 * r)) * 0.92
                    else:
                        u = 0.95
                        v_uv = 0.95
                    loop[uv_layer].uv = (u, v_uv)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_eye_material())
    return obj


def _build_eyelashes_from_eyes(name, body_obj, l_eye, r_eye, collection, is_female=False):
    """Build realistic sculpted dark upper & subtle lower eyelash arcs seated flush along the eyelid rims."""
    bm = bmesh.new()
    lash_len_up = 0.0038 if is_female else 0.0020
    lash_len_lo = 0.0014 if is_female else 0.0008

    for side in (1.0, -1.0):
        ec = l_eye if side > 0 else r_eye
        for is_upper in (True, False):
            segs = 14
            base_row = []
            tip_row = []
            for i in range(segs):
                t = i / (segs - 1)
                x_off = side * (-0.0130 + t * 0.0275)
                arc = math.sin(t * math.pi)
                if is_upper:
                    z_base = ec.z + 0.0030 + 0.0054 * (arc ** 0.85) + 0.0010 * t
                    y_base = ec.y - 0.0108 - 0.0030 * arc
                    taper = math.sin(t * math.pi) * (0.70 + 0.42 * t)
                    flen = lash_len_up * taper
                    z_tip = z_base + flen * (0.65 if is_female else 0.42)
                    y_tip = y_base - flen * 0.84
                    x_tip = x_off + side * flen * 0.18 * (t - 0.3)
                else:
                    z_base = ec.z - 0.0028 - 0.0030 * (arc ** 0.9)
                    y_base = ec.y - 0.0104 - 0.0026 * arc
                    taper = math.sin(t * math.pi)
                    flen = lash_len_lo * taper
                    z_tip = z_base - flen * 0.35
                    y_tip = y_base - flen * 0.85
                    x_tip = x_off

                vb = bm.verts.new((ec.x + x_off, y_base, z_base))
                vt = bm.verts.new((ec.x + x_tip, y_tip, z_tip))
                base_row.append(vb)
                tip_row.append(vt)

            for i in range(segs - 1):
                if (side > 0) == is_upper:
                    bm.faces.new((base_row[i], base_row[i + 1], tip_row[i + 1], tip_row[i]))
                else:
                    bm.faces.new((base_row[i], tip_row[i], tip_row[i + 1], base_row[i + 1]))

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_eyelash_material())

    sol = obj.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.0005 if is_female else 0.0004
    sol.offset = 0.0
    _apply_all_modifiers(obj)
    return obj


def _build_eyebrows_from_eyes(name, body_obj, l_eye, r_eye, collection, is_female=False):
    """Build UV-mapped feathered eyebrow strips projected cleanly along +Y onto the supraorbital ridge."""
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    arch_lift = 0.0046 if is_female else 0.0020

    for side in (1.0, -1.0):
        ey = l_eye.y if side > 0 else r_eye.y
        ez = l_eye.z if side > 0 else r_eye.z

        cols = 14
        rows = 4
        grid = []
        for i in range(cols):
            t = i / (cols - 1)
            x = side * (0.012 + t * 0.041)
            z_mid = ez + 0.0170 + arch_lift * math.sin(t * math.pi * 0.88) - 0.0025 * (t ** 2)
            half_h = (0.0042 if is_female else 0.0052) * (1.0 - 0.45 * (t ** 1.7)) * (0.85 + 0.15 * math.sin(t * math.pi))
            y_front = ey - 0.055
            col_verts = []
            for j in range(rows):
                s = (j / (rows - 1)) - 0.5
                col_verts.append(bm.verts.new((x, y_front, z_mid + s * 2.0 * half_h)))
            grid.append(col_verts)

        for i in range(cols - 1):
            u0 = i / (cols - 1)
            u1 = (i + 1) / (cols - 1)
            for j in range(rows - 1):
                v0 = j / (rows - 1)
                v1 = (j + 1) / (rows - 1)
                if side > 0:
                    f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
                    uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
                else:
                    f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
                    uvs = [(u0, v0), (u0, v1), (u1, v1), (u1, v0)]
                for k_loop, loop in enumerate(f.loops):
                    loop[uv_layer].uv = uvs[k_loop]

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_eyebrow_material())

    sub = obj.modifiers.new("Sub", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1

    sw = obj.modifiers.new("Fit", "SHRINKWRAP")
    sw.target = body_obj
    sw.wrap_method = "PROJECT"
    sw.use_project_y = True
    sw.use_positive_direction = True
    sw.use_negative_direction = False
    sw.offset = 0.0005

    sol = obj.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.0004
    sol.offset = 1.0

    _apply_all_modifiers(obj)
    return obj


def _apply_all_modifiers(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(depsgraph)
    new_mesh = bpy.data.meshes.new_from_object(eval_obj)
    old_mesh = obj.data
    obj.modifiers.clear()
    obj.data = new_mesh
    if old_mesh.users == 0:
        bpy.data.meshes.remove(old_mesh)
    for p in obj.data.polygons:
        p.use_smooth = True


def _build_scalp_buzzcut(name, body_obj, head_center, head_top_z, l_eye, collection, is_female=False):
    """Extract upper scalp polygons above a clean anatomical hairline + natural temple fade."""
    bm = bmesh.new()
    bm.from_mesh(body_obj.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    eye_z = l_eye.z
    eye_y = l_eye.y

    def hairline_sdf(co):
        ax = abs(co.x)
        dy = co.y - eye_y
        if dy < 0.038:
            z_cut = (eye_z + (0.048 if is_female else 0.052)) - 0.09 * (ax ** 2.1)
            if ax > 0.058 and dy > 0.024:
                t_sb = (dy - 0.024) / 0.014
                z_cut -= 0.024 * t_sb
        elif dy < 0.092:
            t_ear = (dy - 0.038) / 0.054
            ear_bell = math.sin(t_ear * math.pi)
            z_base = (eye_z + 0.036) - 0.030 * t_ear
            z_cut = z_base + (0.024 * ear_bell if ax > 0.056 else 0.0)
        else:
            t_back = min(1.0, (dy - 0.092) / 0.085)
            z_cut = (eye_z + 0.006) - t_back * 0.060
        return co.z - z_cut

    keep_faces = [f for f in bm.faces if hairline_sdf(f.calc_center_median()) > -0.004 and f.calc_center_median().z > eye_z - 0.058]
    keep_face_set = set(keep_faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep_face_set], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    fade_vals = []
    thickness = 0.0006 if is_female else 0.0010
    for v in bm.verts:
        d = hairline_sdf(v.co)
        alpha = max(0.0, min(1.0, (d + 0.003) / 0.022))
        if not is_female and v.co.y < eye_y + 0.028 and abs(v.co.x) < 0.054:
            alpha = max(0.0, min(1.0, (d + 0.002) / 0.011))
        offset = thickness * (0.20 + 0.80 * alpha)
        v.co += v.normal * offset
        fade_vals.append(alpha)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True

    attr = mesh.attributes.new(name="fade_alpha", type="FLOAT", domain="POINT")
    attr.data.foreach_set("value", fade_vals)

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_buzzcut_material())
    return obj


def _create_bridged_fit_target(body_obj, max_z, collection, compress_bust=False):
    """Create a smooth garment-fit surface bridging cleavage & glute cleft, with optional bust compression."""
    bm = bmesh.new()
    bm.from_mesh(body_obj.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    orig_co = {v: v.co.copy() for v in bm.verts}
    orig_no = {v: v.normal.copy() for v in bm.verts}

    valley_verts = [
        v for v in bm.verts
        if abs(v.co.x) < 0.062 and max_z * 0.43 < v.co.z < max_z * 0.82
    ]
    for _ in range(10):
        bmesh.ops.smooth_vert(bm, verts=valley_verts, factor=0.75, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        for v in valley_verts:
            if (v.co - orig_co[v]).dot(orig_no[v]) < 0.0:
                v.co = orig_co[v]

    if compress_bust:
        bust_verts = [
            v for v in bm.verts
            if abs(v.co.x) < 0.145 and v.co.y < -0.040 and max_z * 0.655 < v.co.z < max_z * 0.785
        ]
        # Smooth strictly OUTWARD (bridging cleavage and leveling nipple cones outward into a smooth molded cup!)
        for _ in range(14):
            bmesh.ops.smooth_vert(bm, verts=bust_verts, factor=0.72, use_axis_x=True, use_axis_y=True, use_axis_z=True)
            for v in bust_verts:
                # Never allow any bust vertex to sink behind its original Y + 0.0015mm clearance
                if v.co.y > orig_co[v].y - 0.0020:
                    v.co.y = orig_co[v].y - 0.0020
        # Extra gentle outward mold over the front bust dome so fabric clears breast skin smoothly everywhere
        for v in bust_verts:
            if v.co.y < -0.075:
                v.co.y -= 0.0032

    mesh = bpy.data.meshes.new("__tmp_fit_target")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("__tmp_fit_target", mesh)
    collection.objects.link(obj)
    return obj


def _smooth_garment_boundaries(bm, fit_target_obj=None, body_obj=None, passes=6, keep_z=False):
    """Smooth boundary edge loops cleanly so necklines/armholes/cuffs are silky smooth."""
    for _ in range(passes):
        bverts = [v for v in bm.verts if v.is_boundary]
        if not bverts:
            break
        bmesh.ops.smooth_vert(bm, verts=bverts, factor=0.55, use_axis_x=True, use_axis_y=True, use_axis_z=(not keep_z))


def _finalize_garment_modifiers(obj, fit_target, body_obj, offset=0.0028, thickness=0.0028, smooth_after=0, guard_body=True):
    """Subdivide, snap to fit_target, smooth cloth, then ALWAYS guard strictly OUTSIDE body_obj before Solidify."""
    sub = obj.modifiers.new("Subsurf", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1

    if fit_target is not None:
        sw1 = obj.modifiers.new("WrapFit", "SHRINKWRAP")
        sw1.target = fit_target
        sw1.wrap_method = "NEAREST_SURFACEPOINT"
        sw1.wrap_mode = "ON_SURFACE"
        sw1.offset = offset

    # Always smooth BEFORE final OUTSIDE guard so smoothing NEVER shrinks cloth back inside skin!
    if smooth_after > 0:
        sm = obj.modifiers.new("SmoothCloth", "SMOOTH")
        sm.factor = 0.45
        sm.iterations = smooth_after

    if guard_body and body_obj is not None:
        sw2 = obj.modifiers.new("WrapBody", "SHRINKWRAP")
        sw2.target = body_obj
        sw2.wrap_method = "NEAREST_SURFACEPOINT"
        sw2.wrap_mode = "OUTSIDE"
        sw2.offset = max(offset, 0.0032)

    sol = obj.modifiers.new("Solidify", "SOLIDIFY")
    sol.thickness = thickness
    sol.offset = 1.0
    sol.use_rim = True

    _apply_all_modifiers(obj)


def _build_lower_undergarment(name, body_obj, fit_target, z_top, z_band, z_bot, collection):
    """Build clean fitted boxer briefs or athletic shorts with crisp waistband."""
    bm = bmesh.new()
    bm.from_mesh(fit_target.data if fit_target is not None else body_obj.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    arm_x_limit = 0.240
    del_verts = [
        v for v in bm.verts
        if abs(v.co.x) > arm_x_limit or v.co.z > z_top + 0.08 or v.co.z < z_bot - 0.08
    ]
    bmesh.ops.delete(bm, geom=del_verts, context="VERTS")

    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_top), plane_no=(0, 0, 1), clear_outer=True)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_bot), plane_no=(0, 0, 1), clear_inner=True)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_band), plane_no=(0, 0, 1))

    _smooth_garment_boundaries(bm, fit_target, body_obj, passes=3, keep_z=True)

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.smooth = True
        f.material_index = 1 if f.calc_center_median().z >= z_band - 0.001 else 0

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_undergarment_material("Ivory_Fabric_DarkGrey", is_band=False))
    obj.data.materials.append(create_undergarment_material("Ivory_Elastic_Charcoal", is_band=True))

    _finalize_garment_modifiers(obj, fit_target, body_obj, offset=0.0018, thickness=0.0024, smooth_after=2)
    return obj


def _build_boxer_briefs(name, body_obj, collection):
    body_verts = _get_coords_array(body_obj.data)
    max_z = float(body_verts[:, 2].max())
    z_top = max_z * 0.556
    z_band = z_top - 0.036
    z_bot = max_z * 0.402
    fit_target = _create_bridged_fit_target(body_obj, max_z, collection, compress_bust=False)
    obj = _build_lower_undergarment(name, body_obj, fit_target, z_top, z_band, z_bot, collection)
    bpy.data.objects.remove(fit_target, do_unlink=True)
    return obj


def _build_female_shorts(name, body_obj, collection):
    body_verts = _get_coords_array(body_obj.data)
    max_z = float(body_verts[:, 2].max())
    z_top = max_z * 0.576
    z_band = z_top - 0.032
    z_bot = max_z * 0.415
    fit_target = _create_bridged_fit_target(body_obj, max_z, collection, compress_bust=False)
    obj = _build_lower_undergarment(name, body_obj, fit_target, z_top, z_band, z_bot, collection)
    bpy.data.objects.remove(fit_target, do_unlink=True)
    return obj


def _build_female_sports_bra(name, body_obj, collection):
    """Build smooth molded-cup dark grey athletic racerback sports bra with clean wide straps & zero pokes."""
    body_verts = _get_coords_array(body_obj.data)
    max_z = float(body_verts[:, 2].max())

    fit_target = _create_bridged_fit_target(body_obj, max_z, collection, compress_bust=True)

    bm = bmesh.new()
    bm.from_mesh(fit_target.data)
    bm.verts.ensure_lookup_table()
    bm.normal_update()

    z_bot = max_z * 0.654
    z_band = z_bot + 0.030
    z_top = max_z * 0.855

    del_verts = [v for v in bm.verts if abs(v.co.x) > 0.170 or v.co.z < z_bot - 0.05 or v.co.z > z_top + 0.05]
    bmesh.ops.delete(bm, geom=del_verts, context="VERTS")

    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_bot), plane_no=(0, 0, 1), clear_inner=True)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_top), plane_no=(0, 0, 1), clear_outer=True)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z_band), plane_no=(0, 0, 1))

    bm.faces.ensure_lookup_table()
    to_remove = []
    for f in bm.faces:
        c = f.calc_center_median()
        ax = abs(c.x)
        if c.z > max_z * 0.778 and ax > 0.108:
            to_remove.append(f)
            continue
        if c.y < -0.012:
            scoop_z = max_z * 0.762 + ((ax / 0.058) ** 2.2) * 0.056
            if c.z > scoop_z and ax < 0.058:
                to_remove.append(f)
                continue
            if ax > 0.138 and c.z > z_band + 0.042:
                to_remove.append(f)
                continue
        else:
            if c.z > z_band + 0.022:
                t_up = (c.z - (z_band + 0.022)) / max(0.01, (z_top - (z_band + 0.022)))
                if t_up < 0.56:
                    allow_w = 0.132 * (1.0 - t_up / 0.56) + 0.048 * (t_up / 0.56)
                    if ax > allow_w:
                        to_remove.append(f)
                        continue
                else:
                    if ax < 0.044 or ax > 0.104:
                        to_remove.append(f)
                        continue

    bmesh.ops.delete(bm, geom=to_remove, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")

    _smooth_garment_boundaries(bm, fit_target, body_obj, passes=8)

    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f.smooth = True
        f.material_index = 1 if f.calc_center_median().z <= z_band + 0.001 else 0

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.data.materials.append(create_undergarment_material("Ivory_Fabric_DarkGrey", is_band=False))
    obj.data.materials.append(create_undergarment_material("Ivory_Elastic_Charcoal", is_band=True))

    _finalize_garment_modifiers(obj, fit_target, body_obj, offset=0.0032, thickness=0.0032, smooth_after=4, guard_body=True)
    bpy.data.objects.remove(fit_target, do_unlink=True)
    return obj


def _pose_clean_apose(rig):
    """Adjust mixamo_unity arms from hm08 default forward-bent elbows into a crisp symmetric A-pose."""
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"

    arm_l = rig.data.bones.get("mixamorig:LeftArm")
    fore_l = rig.data.bones.get("mixamorig:LeftForeArm")

    if arm_l and fore_l:
        angle_rad = math.radians(44.0)
        target_upper = Vector((math.cos(angle_rad), -0.02, -math.sin(angle_rad))).normalized()
        target_fore = Vector((math.cos(angle_rad + 0.02), -0.09, -math.sin(angle_rad + 0.02))).normalized()

        for side, sx in [("Left", 1.0), ("Right", -1.0)]:
            pb_arm = rig.pose.bones[f"mixamorig:{side}Arm"]
            pb_fore = rig.pose.bones[f"mixamorig:{side}ForeArm"]
            b_arm = rig.data.bones[f"mixamorig:{side}Arm"]
            b_fore = rig.data.bones[f"mixamorig:{side}ForeArm"]

            t_u = Vector((sx * target_upper.x, target_upper.y, target_upper.z)).normalized()
            c_u = (b_arm.tail_local - b_arm.head_local).normalized()
            qu = c_u.rotation_difference(t_u)

            m_arm = b_arm.matrix_local.to_3x3()
            r_arm_local = m_arm.inverted() @ qu.to_matrix() @ m_arm
            pb_arm.rotation_mode = "QUATERNION"
            pb_arm.rotation_quaternion = r_arm_local.to_quaternion()

            t_f = Vector((sx * target_fore.x, target_fore.y, target_fore.z)).normalized()
            c_f = (b_fore.tail_local - b_fore.head_local).normalized()
            qf = (qu @ c_f).rotation_difference(t_f)
            m_fore_after = qu.to_matrix() @ b_fore.matrix_local.to_3x3()
            r_fore_local = m_fore_after.inverted() @ qf.to_matrix() @ m_fore_after
            pb_fore.rotation_mode = "QUATERNION"
            pb_fore.rotation_quaternion = r_fore_local.to_quaternion()

    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")


def _bake_apose_and_transform_shapekeys(rig, mesh_objs):
    """Bake current armature pose into rest pose AND transform every shape key delta via exact LBS matrix."""
    bpy.context.view_layer.update()
    bone_mats = {}
    for pb in rig.pose.bones:
        bone_mats[pb.name] = pb.matrix @ pb.bone.matrix_local.inverted()

    for obj in mesh_objs:
        mesh = obj.data
        n = len(mesh.vertices)
        vg_names = [vg.name for vg in obj.vertex_groups]
        r_mats = np.zeros((n, 3, 3), dtype=np.float64)
        for vi, v in enumerate(mesh.vertices):
            m_acc = np.zeros((3, 3), dtype=np.float64)
            w_sum = 0.0
            for g in v.groups:
                bname = vg_names[g.group]
                if bname in bone_mats and g.weight > 0.0:
                    m_acc += g.weight * np.array(bone_mats[bname].to_3x3(), dtype=np.float64)
                    w_sum += g.weight
            if w_sum > 1e-6:
                r_mats[vi] = m_acc / w_sum
            else:
                r_mats[vi] = np.eye(3, dtype=np.float64)

        old_basis = _get_coords_array(mesh)
        saved_sks = []
        if mesh.shape_keys and len(mesh.shape_keys.key_blocks) > 1:
            for kb in mesh.shape_keys.key_blocks[1:]:
                sk_co = np.empty(n * 3, dtype=np.float64)
                kb.data.foreach_get("co", sk_co)
                sk_co = sk_co.reshape((n, 3))
                saved_sks.append((kb.name, float(kb.value), sk_co - old_basis))

        if mesh.shape_keys:
            for kb in reversed(list(mesh.shape_keys.key_blocks)):
                obj.shape_key_remove(kb)

        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        for mod in list(obj.modifiers):
            if mod.type == "ARMATURE":
                bpy.ops.object.modifier_apply(modifier=mod.name)

        new_basis = _get_coords_array(mesh)

        if saved_sks:
            obj.shape_key_add(name="Basis", from_mix=False)
            for sk_name, _sk_val, delta in saved_sks:
                kb = obj.shape_key_add(name=sk_name, from_mix=False)
                rot_delta = np.einsum("vij,vj->vi", r_mats, delta)
                new_co = (new_basis + rot_delta).ravel().astype(np.float32)
                kb.data.foreach_set("co", new_co)
                kb.value = 0.0

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    for obj in mesh_objs:
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = rig


def _build_mesh_kdtree(mesh):
    n = len(mesh.vertices)
    kd = KDTree(n)
    for i, v in enumerate(mesh.vertices):
        kd.insert(v.co, i)
    kd.balance()
    return kd


def _transfer_weights_knn(src_obj, dst_obj, k=4):
    """Transfer vertex group bone weights from src_obj to dst_obj via C++ KDTree k-NN IDW."""
    src_vgs = src_obj.vertex_groups
    for vg in list(dst_obj.vertex_groups):
        dst_obj.vertex_groups.remove(vg)

    vg_map = {}
    for vg in src_vgs:
        vg_map[vg.index] = dst_obj.vertex_groups.new(name=vg.name)

    src_weights = [
        [(g.group, g.weight) for g in v.groups if g.weight > 1e-5]
        for v in src_obj.data.vertices
    ]

    kd = _build_mesh_kdtree(src_obj.data)
    k_eff = min(k, len(src_obj.data.vertices))

    # Batch per vertex group for minimal Python overhead
    per_vg_adds = {}
    for vi, v in enumerate(dst_obj.data.vertices):
        hits = kd.find_n(v.co, k_eff)
        inv_d = [1.0 / (dist + 1e-5) for _co, _idx, dist in hits]
        w_sum = sum(inv_d)
        acc = {}
        for (_co, sv, _dist), w_sp in zip(hits, inv_d):
            ws = w_sp / w_sum
            for gid, w_bone in src_weights[sv]:
                acc[gid] = acc.get(gid, 0.0) + ws * w_bone
        for gid, w in acc.items():
            if w > 1e-4:
                vg_map[gid].add([vi], w, "REPLACE")


def _transfer_shapekeys_knn(src_obj, dst_obj, k=4, rigid_translate=False, key_prefix_filter=None):
    """Propagate non-Basis Shape Keys from src_obj onto dst_obj via C++ KDTree k-NN delta interpolation."""
    if not src_obj.data.shape_keys or len(src_obj.data.shape_keys.key_blocks) <= 1:
        return

    src_co = _get_coords_array(src_obj.data)
    dst_co = _get_coords_array(dst_obj.data)
    n_dst = len(dst_co)
    if n_dst == 0:
        return

    key_blocks = [
        kb for kb in src_obj.data.shape_keys.key_blocks[1:]
        if key_prefix_filter is None or any(kb.name.startswith(p) for p in key_prefix_filter)
    ]
    if not key_blocks:
        return

    kd = _build_mesh_kdtree(src_obj.data)

    if rigid_translate:
        left_mask = dst_co[:, 0] >= 0.0
        l_pts = dst_co[left_mask]
        r_pts = dst_co[~left_mask]
        l_c = Vector(l_pts.mean(axis=0)) if len(l_pts) else Vector(dst_co.mean(axis=0))
        r_c = Vector(r_pts.mean(axis=0)) if len(r_pts) else Vector(dst_co.mean(axis=0))
        l_nn = [idx for _co, idx, _d in kd.find_n(l_c, 12)]
        r_nn = [idx for _co, idx, _d in kd.find_n(r_c, 12)]

        if not dst_obj.data.shape_keys:
            dst_obj.shape_key_add(name="Basis", from_mix=False)
        for kb in key_blocks:
            sk_src = np.empty(len(src_co) * 3, dtype=np.float64)
            kb.data.foreach_get("co", sk_src)
            delta_src = sk_src.reshape((len(src_co), 3)) - src_co
            dl = delta_src[l_nn].mean(axis=0)
            dr = delta_src[r_nn].mean(axis=0)
            new_dst = dst_co.copy()
            new_dst[left_mask] += dl
            new_dst[~left_mask] += dr
            nkb = dst_obj.shape_key_add(name=kb.name, from_mix=False)
            nkb.data.foreach_set("co", new_dst.ravel().astype(np.float32))
            nkb.value = 0.0
        return

    k_eff = min(k, len(src_co))
    all_nn_idx = np.empty((n_dst, k_eff), dtype=np.int64)
    all_nn_w = np.empty((n_dst, k_eff), dtype=np.float64)
    for vi, v in enumerate(dst_obj.data.vertices):
        hits = kd.find_n(v.co, k_eff)
        ws = [1.0 / (dist + 1e-5) for _co, _idx, dist in hits]
        wsum = sum(ws)
        for j, ((_co, idx, _dist), wv) in enumerate(zip(hits, ws)):
            all_nn_idx[vi, j] = idx
            all_nn_w[vi, j] = wv / wsum

    if not dst_obj.data.shape_keys:
        dst_obj.shape_key_add(name="Basis", from_mix=False)

    sk_src = np.empty(len(src_co) * 3, dtype=np.float64)
    for kb in key_blocks:
        kb.data.foreach_get("co", sk_src)
        delta_src = sk_src.reshape((len(src_co), 3)) - src_co
        delta_dst = np.sum(delta_src[all_nn_idx] * all_nn_w[:, :, None], axis=1)
        nkb = dst_obj.shape_key_add(name=kb.name, from_mix=False)
        nkb.data.foreach_set("co", (dst_co + delta_dst).ravel().astype(np.float32))
        nkb.value = 0.0


def attach_modular_mesh(char_info, item_obj, rigid_bone=None, transfer_shapekeys=True, key_prefix_filter=None, k=4):
    """Rig and Shape-Key-sync any modular hair, outfit, shoe, or accessory mesh onto a finalized character."""
    rig = char_info["rig"]
    body_obj = char_info["body"]

    if rigid_bone is not None:
        for vg in list(item_obj.vertex_groups):
            item_obj.vertex_groups.remove(vg)
        vg = item_obj.vertex_groups.new(name=rigid_bone)
        vg.add(list(range(len(item_obj.data.vertices))), 1.0, "REPLACE")
    else:
        _transfer_weights_knn(body_obj, item_obj, k=k)

    if transfer_shapekeys:
        _transfer_shapekeys_knn(body_obj, item_obj, k=k, key_prefix_filter=key_prefix_filter)

    item_obj.parent = rig
    mod = item_obj.modifiers.get("Armature") or item_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return item_obj


def get_body_landmarks(char_info):
    """Return exact world-space anatomical landmarks from the finalized A-pose character."""
    body_obj = char_info["body"]
    rig = char_info["rig"]
    co = _get_coords_array(body_obj.data)
    h = float(co[:, 2].max())

    def bone_head(bname):
        b = rig.data.bones.get(bname)
        return (rig.matrix_world @ b.head_local) if b else Vector((0, 0, h * 0.5))

    def bone_tail(bname):
        b = rig.data.bones.get(bname)
        return (rig.matrix_world @ b.tail_local) if b else Vector((0, 0, h * 0.5))

    return {
        "height": h,
        "head_top_z": h,
        "head_center": char_info["head_center"].copy(),
        "l_eye": char_info["l_eye"].copy(),
        "r_eye": char_info["r_eye"].copy(),
        "neck": bone_head("mixamorig:Neck"),
        "head_bone": bone_head("mixamorig:Head"),
        "spine2": bone_head("mixamorig:Spine2"),
        "hips": bone_head("mixamorig:Hips"),
        "l_shoulder": bone_head("mixamorig:LeftArm"),
        "r_shoulder": bone_head("mixamorig:RightArm"),
        "l_elbow": bone_head("mixamorig:LeftForeArm"),
        "r_elbow": bone_head("mixamorig:RightForeArm"),
        "l_wrist": bone_head("mixamorig:LeftHand"),
        "r_wrist": bone_head("mixamorig:RightHand"),
        "l_ring": bone_head("mixamorig:LeftHandRing1"),
        "r_ring": bone_head("mixamorig:RightHandRing1"),
        "l_knee": bone_head("mixamorig:LeftLeg"),
        "r_knee": bone_head("mixamorig:RightLeg"),
        "l_ankle": bone_head("mixamorig:LeftFoot"),
        "r_ankle": bone_head("mixamorig:RightFoot"),
        "l_toe": bone_head("mixamorig:LeftToeBase"),
        "r_toe": bone_head("mixamorig:RightToeBase"),
        "l_toe_end": bone_tail("mixamorig:LeftToeBase"),
        "r_toe_end": bone_tail("mixamorig:RightToeBase"),
    }


def _create_socket_empties(rig, collection, prefix):
    """Create 3D attachment socket empties parented to the corresponding Humanoid bones."""
    sockets = [
        ("HEAD",       "mixamorig:Head",          (0.0, 0.095, 0.0), 0.035),
        ("WRIST_L",    "mixamorig:LeftHand",      (0.0, -0.015, 0.0), 0.022),
        ("WRIST_R",    "mixamorig:RightHand",     (0.0, -0.015, 0.0), 0.022),
        ("FINGER_L",   "mixamorig:LeftHandRing1", (0.0, 0.012, 0.0), 0.012),
        ("FINGER_R",   "mixamorig:RightHandRing1",(0.0, 0.012, 0.0), 0.012),
        ("TORSO_LEGS", "mixamorig:Hips",          (0.0, 0.0, 0.0),   0.045),
        ("FEET_L",     "mixamorig:LeftFoot",      (0.0, 0.02, 0.0),  0.028),
        ("FEET_R",     "mixamorig:RightFoot",     (0.0, 0.02, 0.0),  0.028),
    ]
    created = {}
    for slot_name, bone_name, offset, disp_size in sockets:
        emp = bpy.data.objects.new(f"{prefix}_Socket_{slot_name}", None)
        emp.empty_display_type = "PLAIN_AXES"
        emp.empty_display_size = disp_size
        collection.objects.link(emp)
        if bone_name in rig.data.bones:
            emp.parent = rig
            emp.parent_type = "BONE"
            emp.parent_bone = bone_name
            emp.location = offset
        created[slot_name] = emp
    return created


def build_character_asset(gender, collection, tone_index=DEFAULT_SKIN_INDEX):
    """Build complete high-detail West African basemesh (Male or Female) with 10 Face Presets, Body Morphs & Rig."""
    HumanService, _, _, _, _, TargetService = _get_mpfb_services()
    is_female = (gender.lower() == "female")
    prefix = "Female" if is_female else "Male"

    base_macro = TargetService.get_default_macro_info_dict()
    base_macro["age"] = 0.48

    if not is_female:
        base_macro.update({
            "race": {"african": 0.78, "asian": 0.0, "caucasian": 0.22},
            "gender": 1.0,
            "muscle": 0.72,
            "weight": 0.44,
            "proportions": 0.54,
            "height": 0.52,
        })
        base_extra = [
            ("torso-muscle-pectoral-incr", 0.55),
            ("torso-muscle-dorsi-incr", 0.48),
            ("torso-vshape-incr", 0.30),
            ("measure-shoulder-dist-incr", 0.18),
            ("stomach-tone-incr", 0.78),
            ("pelvis-tone-incr", 0.48),
            ("l-upperarm-shoulder-muscle-incr", 0.50),
            ("r-upperarm-shoulder-muscle-incr", 0.50),
            ("l-upperarm-muscle-incr", 0.44),
            ("r-upperarm-muscle-incr", 0.44),
            ("l-upperarm-fat-decr", 0.30),
            ("r-upperarm-fat-decr", 0.30),
            ("l-lowerarm-muscle-incr", 0.35),
            ("r-lowerarm-muscle-incr", 0.35),
            ("l-upperleg-muscle-incr", 0.40),
            ("r-upperleg-muscle-incr", 0.40),
            ("l-upperleg-fat-decr", 0.25),
            ("r-upperleg-fat-decr", 0.25),
            ("l-lowerleg-muscle-incr", 0.38),
            ("r-lowerleg-muscle-incr", 0.38),
            ("buttocks-volume-incr", 0.14),
            ("measure-neck-circ-incr", 0.14),
            ("l-cheek-bones-incr", 0.18),
            ("r-cheek-bones-incr", 0.18),
            ("head-oval", 0.14),
            ("chin-prominent-incr", 0.12),
        ]
    else:
        base_macro.update({
            "race": {"african": 0.74, "asian": 0.0, "caucasian": 0.26},
            "gender": 0.0,
            "muscle": 0.54,
            "weight": 0.42,
            "proportions": 0.52,
            "height": 0.46,
            "cupsize": 0.44,
            "firmness": 0.94,
        })
        base_extra = [
            ("stomach-tone-incr", 0.56),
            ("pelvis-tone-incr", 0.38),
            ("measure-waist-circ-decr", 0.16),
            ("measure-hips-circ-incr", 0.12),
            ("buttocks-volume-incr", 0.16),
            ("torso-muscle-dorsi-incr", 0.20),
            ("l-upperarm-shoulder-muscle-incr", 0.22),
            ("r-upperarm-shoulder-muscle-incr", 0.22),
            ("l-upperarm-fat-decr", 0.24),
            ("r-upperarm-fat-decr", 0.24),
            ("l-upperleg-muscle-incr", 0.26),
            ("r-upperleg-muscle-incr", 0.26),
            ("l-lowerleg-muscle-incr", 0.28),
            ("r-lowerleg-muscle-incr", 0.28),
            ("breast-trans-up", 0.18),
            ("breast-point-decr", 0.30),
            ("l-cheek-bones-incr", 0.18),
            ("r-cheek-bones-incr", 0.18),
            ("head-oval", 0.18),
        ]

    # 1. Build full 19158-vert baked basemesh
    full_obj = _build_full_mesh_with_targets(base_macro, base_extra, scale=0.1)
    _ground_body_z_zero(full_obj)
    base_full_coords = _get_coords_array(full_obj.data)

    l_eye_c, l_eye_r = _vg_center_and_radius(full_obj, "helper-l-eye")
    r_eye_c, r_eye_r = _vg_center_and_radius(full_obj, "helper-r-eye")
    head_c, _ = _vg_center_and_radius(full_obj, "joint-head")

    # 2. Extract clean 13,380-vert 'body' mesh & apply anatomical micro-relief sculpting
    body_obj, old_to_new = _extract_submesh_by_vgroup(full_obj, "body", f"{prefix}_Basemesh", collection)
    _sculpt_anatomical_detail(body_obj, is_female=is_female)

    body_indices = sorted(old_to_new.keys())
    body_coords = _get_coords_array(body_obj.data)
    head_top_z = float(body_coords[:, 2].max())

    # 3. Compute Body Morph & 10 Face Preset Shape Keys
    body_obj.shape_key_add(name="Basis", from_mix=False)

    if not is_female:
        body_morphs = [
            ("Body_Skinny",   {"muscle": 0.12, "weight": 0.12, "proportions": 0.35}, [("torso-vshape-decr", 0.25)]),
            ("Body_Muscular", {"muscle": 0.98, "weight": 0.54, "proportions": 0.78}, [("torso-vshape-incr", 0.40), ("torso-muscle-pectoral-incr", 0.50), ("torso-muscle-dorsi-incr", 0.45)]),
            ("Body_Heavy",    {"muscle": 0.45, "weight": 0.94, "proportions": 0.52}, [("measure-waist-circ-incr", 0.35), ("measure-hips-circ-incr", 0.25)]),
        ]
        face_presets = MALE_FACE_PRESETS
    else:
        body_morphs = [
            ("Body_Slender",    {"muscle": 0.24, "weight": 0.16, "proportions": 0.52, "cupsize": 0.32}, [("measure-hips-circ-decr", 0.25)]),
            ("Body_Muscular",   {"muscle": 0.90, "weight": 0.46, "proportions": 0.72, "cupsize": 0.50}, [("stomach-tone-incr", 0.60), ("torso-muscle-dorsi-incr", 0.35)]),
            ("Body_Curvy_Full", {"muscle": 0.42, "weight": 0.82, "proportions": 0.86, "cupsize": 0.86}, [("measure-hips-circ-incr", 0.45), ("buttocks-volume-incr", 0.45)]),
            ("Bust_Adjust",     {"cupsize": 0.95, "firmness": 0.78}, [("measure-bust-circ-incr", 0.35)]),
            ("Hip_Adjust",      {}, [("measure-hips-circ-incr", 0.50), ("buttocks-volume-incr", 0.45)]),
        ]
        face_presets = FEMALE_FACE_PRESETS

    for sk_name, macro_over, extra_over in body_morphs:
        mm = dict(base_macro)
        mm.update(macro_over)
        delta_full = _compute_morph_delta_on_full(base_macro, base_extra, mm, base_extra + extra_over, base_full_coords)
        kb = body_obj.shape_key_add(name=sk_name, from_mix=False)
        kb.data.foreach_set("co", (body_coords + delta_full[body_indices]).ravel().astype(np.float32))
        kb.value = 0.0

    for sk_name, _label, _tone, ftargets in face_presets:
        delta_full = _compute_face_delta_fast(full_obj, ftargets, base_full_coords)
        kb = body_obj.shape_key_add(name=sk_name, from_mix=False)
        kb.data.foreach_set("co", (body_coords + delta_full[body_indices]).ravel().astype(np.float32))
        kb.value = 0.0

    # 4. Build Secondary Modular Meshes (Eyes, Eyelashes, Eyebrows, BuzzCut Scalp, Undergarments)
    eyes_obj = _build_eyeballs(f"{prefix}_Eyes", l_eye_c, l_eye_r, r_eye_c, r_eye_r, collection)
    lashes_obj = _build_eyelashes_from_eyes(f"{prefix}_Eyelashes", body_obj, l_eye_c, r_eye_c, collection, is_female=is_female)
    brows_obj = _build_eyebrows_from_eyes(f"{prefix}_Eyebrows", body_obj, l_eye_c, r_eye_c, collection, is_female=is_female)
    scalp_obj = _build_scalp_buzzcut(f"{prefix}_Scalp_BuzzCut", body_obj, head_c, head_top_z, l_eye_c, collection, is_female=is_female)

    clothes_objs = []
    if not is_female:
        briefs = _build_boxer_briefs(f"{prefix}_BoxerBriefs", body_obj, collection)
        clothes_objs.append(briefs)
    else:
        bra = _build_female_sports_bra(f"{prefix}_SportsBra", body_obj, collection)
        shorts = _build_female_shorts(f"{prefix}_Shorts", body_obj, collection)
        clothes_objs.extend([bra, shorts])

    # 5. Propagate Body & 10 Face Shape Keys cleanly onto Secondary Meshes
    _transfer_shapekeys_knn(body_obj, eyes_obj, rigid_translate=True)
    _transfer_shapekeys_knn(body_obj, lashes_obj, k=4)
    _transfer_shapekeys_knn(body_obj, brows_obj, k=4)
    _transfer_shapekeys_knn(body_obj, scalp_obj, k=4)
    for cobj in clothes_objs:
        _transfer_shapekeys_knn(body_obj, cobj, k=4)

    # 6. Apply realistic AO-enhanced West African skin shader
    skin_mat = create_ivory_skin_material(f"Ivory_{prefix}_Skin", body_obj, tone_index=tone_index)

    # 7. Rig full_obj with mixamo_unity, transfer vertex weights to body_obj & all secondaries
    rig = HumanService.add_builtin_rig(full_obj, "mixamo_unity", import_weights=True)
    rig.name = f"{prefix}_Rig"
    rig.data.name = f"{prefix}_RigData"
    if rig.name not in collection.objects:
        collection.objects.link(rig)
    for col in list(rig.users_collection):
        if col != collection:
            col.objects.unlink(rig)

    all_meshes = [body_obj, eyes_obj, lashes_obj, brows_obj, scalp_obj] + clothes_objs
    for mobj in all_meshes:
        if mobj is not body_obj:
            _transfer_weights_knn(full_obj, mobj, k=4)
        else:
            _transfer_weights_knn(full_obj, body_obj, k=1)
        mobj.parent = rig
        mod = mobj.modifiers.new("Armature", "ARMATURE")
        mod.object = rig

    bpy.data.objects.remove(full_obj, do_unlink=True)

    # 8. Pose into crisp symmetric A-pose and bake Rest Pose + LBS-rotated Shape Keys
    _pose_clean_apose(rig)
    _bake_apose_and_transform_shapekeys(rig, all_meshes)

    # 9. Re-ground lowest body vertex to Z=0
    bpy.context.view_layer.update()
    bco = _get_coords_array(body_obj.data)
    zmin = float(bco[:, 2].min())
    if abs(zmin) > 1e-4:
        rig.location.z -= zmin
        l_eye_c.z -= zmin
        r_eye_c.z -= zmin
        head_c.z -= zmin

    # 10. Create 3D Attachment Socket Empties
    sockets = _create_socket_empties(rig, collection, prefix)

    # 11. Smooth Subdivision Surface on body_obj (render level 2 for crisp anatomical contours)
    sub_body = body_obj.modifiers.new("Subsurf", "SUBSURF")
    sub_body.levels = 1
    sub_body.render_levels = 2

    return {
        "gender": gender.lower(),
        "prefix": prefix,
        "rig": rig,
        "body": body_obj,
        "skin_mat": skin_mat,
        "eyes": eyes_obj,
        "lashes": lashes_obj,
        "brows": brows_obj,
        "scalp": scalp_obj,
        "clothes": clothes_objs,
        "sockets": sockets,
        "height": float(bco[:, 2].max() - zmin),
        "head_center": head_c,
        "l_eye": l_eye_c,
        "r_eye": r_eye_c,
        "face_presets": face_presets,
    }
