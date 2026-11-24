"""Shared Blender 5.x helpers for Ivory State mobile-optimized 3D vehicles.

Conventions:
- Units: meters (1 Blender unit = 1 meter)
- Axes in Blender: +Y = Forward, +X = Right, +Z = Up, Ground = Z=0
- Unity export (FBX): Forward = +Z, Up = +Y, Pivot at ground (0, 0, 0)
- Object naming compatible with IvoryState VehicleMotor:
  Carrosserie, Vitres, Plastiques, Chrome, Plaques,
  Phares, Feux, Clignotants, Lumignon,
  Roue_FL, Roue_FR, Roue_RL, Roue_RR
"""

import math
import os
import bpy
import bmesh
from mathutils import Matrix, Vector, Euler


def clear_scene():
    """Reset the Blender scene completely."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def ensure_collection(name, parent=None):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        if parent is None:
            bpy.context.scene.collection.children.link(col)
        else:
            parent.children.link(col)
    return col


def link_to_collection(obj, col):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)


def make_pbr_material(
    name,
    base_color=(0.8, 0.8, 0.8, 1.0),
    metallic=0.0,
    roughness=0.5,
    coat_weight=0.0,
    coat_roughness=0.1,
    specular=0.5,
    emission_color=None,
    emission_strength=0.0,
):
    """Create a Blender 5.x Principled BSDF material (also clean for Unity URP FBX/GLB)."""
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new('ShaderNodeOutputMaterial')
    out.location = (300, 0)

    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.location = (0, 0)
    bsdf.inputs['Base Color'].default_value = base_color
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = roughness
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = specular
    if 'Coat Weight' in bsdf.inputs:
        bsdf.inputs['Coat Weight'].default_value = coat_weight
    if 'Coat Roughness' in bsdf.inputs:
        bsdf.inputs['Coat Roughness'].default_value = coat_roughness
    if emission_color is not None and emission_strength > 0.0:
        if 'Emission Color' in bsdf.inputs:
            bsdf.inputs['Emission Color'].default_value = emission_color
        if 'Emission Strength' in bsdf.inputs:
            bsdf.inputs['Emission Strength'].default_value = emission_strength

    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    return mat


def create_standard_vehicle_materials(paint_rgba=(0.98, 0.58, 0.003, 1.0)):
    """Streamlined Ivory State material palette (aligned with TaxiModelBuilder)."""
    glass_mat = make_pbr_material(
        'M_Vitres',
        base_color=(0.05, 0.07, 0.09, 1.0),
        metallic=0.06,
        roughness=0.05,
        coat_weight=1.0,
        coat_roughness=0.02,
        specular=0.78,
    )
    bsdf = next((n for n in glass_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is not None:
        if 'Alpha' in bsdf.inputs:
            bsdf.inputs['Alpha'].default_value = 0.64
        if 'IOR' in bsdf.inputs:
            bsdf.inputs['IOR'].default_value = 1.52

    return {
        'paint': make_pbr_material(
            'M_Carrosserie',
            base_color=paint_rgba,
            metallic=0.04,
            roughness=0.18,
            coat_weight=1.0,
            coat_roughness=0.05,
            specular=0.52,
        ),
        'glass': glass_mat,
        'trim': make_pbr_material(
            'M_Plastiques',
            base_color=(0.032, 0.034, 0.038, 1.0),
            metallic=0.02,
            roughness=0.56,
            specular=0.32,
        ),
        'trim_dark': make_pbr_material(
            'M_Plastiques_Sombre',
            base_color=(0.015, 0.016, 0.019, 1.0),
            metallic=0.0,
            roughness=0.80,
            specular=0.18,
        ),
        'chrome': make_pbr_material(
            'M_Chrome',
            base_color=(0.78, 0.80, 0.83, 1.0),
            metallic=0.92,
            roughness=0.16,
        ),
        'plate': make_pbr_material(
            'M_Plaques',
            base_color=(0.86, 0.85, 0.81, 1.0),
            metallic=0.04,
            roughness=0.38,
        ),
        'headlight': make_pbr_material(
            'M_Phares',
            base_color=(0.72, 0.76, 0.80, 1.0),
            metallic=0.52,
            roughness=0.08,
            coat_weight=1.0,
            coat_roughness=0.03,
            emission_color=(1.0, 0.96, 0.88, 1.0),
            emission_strength=0.14,
        ),
        'taillight': make_pbr_material(
            'M_Feux',
            base_color=(0.48, 0.012, 0.008, 1.0),
            metallic=0.08,
            roughness=0.12,
            coat_weight=0.95,
            coat_roughness=0.04,
            emission_color=(0.85, 0.03, 0.01, 1.0),
            emission_strength=0.16,
        ),
        'amber': make_pbr_material(
            'M_Clignotants',
            base_color=(0.82, 0.22, 0.006, 1.0),
            metallic=0.08,
            roughness=0.14,
            coat_weight=0.9,
            coat_roughness=0.05,
            emission_color=(0.95, 0.28, 0.01, 1.0),
            emission_strength=0.14,
        ),
        'reverse': make_pbr_material(
            'M_FeuRecul',
            base_color=(0.80, 0.82, 0.84, 1.0),
            metallic=0.15,
            roughness=0.10,
            coat_weight=0.9,
            coat_roughness=0.04,
        ),
        'taxi_sign_frame': make_pbr_material(
            'M_Lumignon_Cadre',
            base_color=(0.92, 0.92, 0.90, 1.0),
            metallic=0.0,
            roughness=0.25,
        ),
        'taxi_sign_glow': make_pbr_material(
            'M_Lumignon',
            base_color=(0.96, 0.30, 0.015, 1.0),
            metallic=0.0,
            roughness=0.22,
            emission_color=(1.0, 0.32, 0.02, 1.0),
            emission_strength=0.75,
        ),
        'tyre': make_pbr_material(
            'M_Pneu',
            base_color=(0.024, 0.025, 0.027, 1.0),
            metallic=0.0,
            roughness=0.78,
            specular=0.22,
        ),
        'steel_rim': make_pbr_material(
            'M_Jante_Tole',
            base_color=(0.66, 0.68, 0.71, 1.0),
            metallic=0.84,
            roughness=0.25,
            coat_weight=0.2,
            coat_roughness=0.18,
        ),
        'brake': make_pbr_material(
            'M_Frein',
            base_color=(0.04, 0.042, 0.045, 1.0),
            metallic=0.4,
            roughness=0.70,
        ),
    }


def apply_smooth_with_sharp_angle(obj, angle_deg=42.0):
    """Set smooth shading and mark edges sharper than angle_deg as sharp (Blender 5.x safe)."""
    mesh = obj.data
    if not isinstance(mesh, bpy.types.Mesh):
        return
    n_polys = len(mesh.polygons)
    if n_polys > 0:
        mesh.polygons.foreach_set('use_smooth', [True] * n_polys)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.edges.ensure_lookup_table()
    thresh = math.radians(angle_deg)
    for e in bm.edges:
        if len(e.link_faces) == 2:
            ang = e.calc_face_angle(0.0)
            if ang >= thresh:
                e.smooth = False
        elif len(e.link_faces) == 1:
            e.smooth = False
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def set_mesh_smooth(obj, smooth=True):
    mesh = obj.data
    if isinstance(mesh, bpy.types.Mesh) and len(mesh.polygons) > 0:
        mesh.polygons.foreach_set('use_smooth', [smooth] * len(mesh.polygons))
        mesh.update()


def create_mesh_object(name, bm, material, collection, parent=None, angle_deg=42.0):
    """Finalize a bmesh into a Blender object with recalculated normals."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    link_to_collection(obj, collection)
    if parent is not None:
        obj.parent = parent
    if material is not None:
        if isinstance(material, list):
            for m in material:
                obj.data.materials.append(m)
        else:
            obj.data.materials.append(material)

    if angle_deg is not None:
        apply_smooth_with_sharp_angle(obj, angle_deg=angle_deg)
    else:
        set_mesh_smooth(obj, True)
    return obj


def add_box_bm(bm, center, size, rot_euler=(0.0, 0.0, 0.0), bevel_width=0.0, bevel_segs=1, mat_idx=0):
    """Add an oriented box (optionally beveled) into an existing bmesh."""
    cx, cy, cz = center
    sx, sy, sz = size
    mat = (
        Matrix.Translation((cx, cy, cz))
        @ Euler(rot_euler, 'XYZ').to_matrix().to_4x4()
        @ Matrix.Diagonal((sx, sy, sz, 1.0))
    )
    ret = bmesh.ops.create_cube(bm, size=1.0, matrix=mat)
    verts = ret['verts']
    edges = list({e for v in verts for e in v.link_edges})
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat_idx
    if bevel_width > 0.0:
        bev = bmesh.ops.bevel(
            bm,
            geom=edges,
            offset=bevel_width,
            segments=bevel_segs,
            profile=0.5,
            affect='EDGES',
            clamp_overlap=True,
        )
        for f in bev.get('faces', []):
            f.material_index = mat_idx
    return verts


def add_cylinder_bm(bm, p0, p1, radius, segments=12, cap_ends=True, mat_idx=0):
    """Add a cylinder between points p0 and p1 in bmesh."""
    v0 = Vector(p0)
    v1 = Vector(p1)
    diff = v1 - v0
    length = diff.length
    if length < 1e-6:
        return []
    mid = (v0 + v1) * 0.5
    rot = Vector((0, 0, 1)).rotation_difference(diff.normalized()).to_matrix().to_4x4()
    mat = Matrix.Translation(mid) @ rot
    ret = bmesh.ops.create_cone(
        bm,
        cap_ends=cap_ends,
        cap_tris=False,
        segments=segments,
        radius1=radius,
        radius2=radius,
        depth=length,
        matrix=mat,
    )
    verts = ret['verts']
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_idx
    return verts


def add_lathe_ring_bm(bm, profile_rz, segments=24, mirror_x=1.0, mat_idx=0, close_profile=False):
    """Revolve a 2D profile [(axial_pos, radius), ...] around the X axis (for wheels)."""
    rings = []
    for axial, r in profile_rz:
        ring = []
        for i in range(segments):
            ang = 2.0 * math.pi * i / segments
            y = math.cos(ang) * r
            z = math.sin(ang) * r
            x = axial * mirror_x
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)

    n_prof = len(rings)
    limit = n_prof if close_profile else n_prof - 1
    for j in range(limit):
        r0 = rings[j]
        r1 = rings[(j + 1) % n_prof]
        for i in range(segments):
            ni = (i + 1) % segments
            verts = [r0[i], r0[ni], r1[ni], r1[i]] if mirror_x > 0 else [r0[i], r1[i], r1[ni], r0[ni]]
            try:
                f = bm.faces.new(verts)
                f.material_index = mat_idx
            except ValueError:
                pass
    return rings


def build_steel_wheel_mesh(
    name,
    radius=0.305,
    width=0.185,
    rim_ratio=0.58,
    segments=20,
    hole_count=12,
    mats=None,
):
    """Build a mobile-optimized realistic steel wheel (jante tôle + pneu, ~650 tris)."""
    bm = bmesh.new()
    hw = width * 0.5
    rim_r = radius * rim_ratio
    tread_hw = hw * 0.78
    sidewall_r = radius * 0.90

    # 1. Streamlined 8-point tire lathe profile (smooth shoulders, half the tris)
    tire_prof = [
        (-hw * 0.88, rim_r * 0.97),
        (-hw,        (rim_r + sidewall_r) * 0.52),
        (-hw * 0.95, sidewall_r),
        (-tread_hw,  radius),
        ( tread_hw,  radius),
        ( hw * 0.95, sidewall_r),
        ( hw,        (rim_r + sidewall_r) * 0.52),
        ( hw * 0.88, rim_r * 0.97),
    ]
    add_lathe_ring_bm(bm, tire_prof, segments=segments, mat_idx=0, close_profile=True)

    # 2. Steel rim outer lip + recessed dish + raised center hub (streamlined)
    rim_prof = [
        (-hw * 0.82, rim_r * 0.92),
        ( hw * 0.62, rim_r * 0.92),
        ( hw * 0.90, rim_r * 1.00),
        ( hw * 0.86, rim_r * 0.91),
        ( hw * 0.48, rim_r * 0.78),
        ( hw * 0.46, rim_r * 0.63),
        ( hw * 0.72, rim_r * 0.42),
        ( hw * 0.70, rim_r * 0.24),
        ( hw * 0.84, rim_r * 0.20),
    ]
    rings = add_lathe_ring_bm(bm, rim_prof, segments=segments, mat_idx=1, close_profile=False)
    center_v = bm.verts.new((hw * 0.86, 0.0, 0.0))
    last_ring = rings[-1]
    for i in range(segments):
        ni = (i + 1) % segments
        try:
            f = bm.faces.new([last_ring[i], last_ring[ni], center_v])
            f.material_index = 1
        except ValueError:
            pass

    back_ring = rings[0]
    back_c = bm.verts.new((-hw * 0.75, 0.0, 0.0))
    for i in range(segments):
        ni = (i + 1) % segments
        try:
            f = bm.faces.new([back_ring[ni], back_ring[i], back_c])
            f.material_index = 2
        except ValueError:
            pass

    # 3. 12 steel-rim circular dark ventilation holes (flat dark hexagons on the dish -> super low tri!)
    hole_orbit = rim_r * 0.71
    hole_rad = rim_r * 0.095
    hole_x = hw * 0.478
    for h in range(hole_count):
        ang = 2.0 * math.pi * h / hole_count
        hy = math.cos(ang) * hole_orbit
        hz = math.sin(ang) * hole_orbit
        hverts = []
        for k in range(6):
            ka = 2.0 * math.pi * k / 6.0
            hverts.append(bm.verts.new((hole_x, hy + math.cos(ka) * hole_rad, hz + math.sin(ka) * hole_rad)))
        f = bm.faces.new(hverts)
        f.material_index = 2

    # 4. 4 lug nuts around hub (low-poly hexagonal caps)
    lug_orbit = rim_r * 0.31
    for l in range(4):
        ang = 2.0 * math.pi * l / 4.0 + math.pi * 0.25
        ly = math.cos(ang) * lug_orbit
        lz = math.sin(ang) * lug_orbit
        add_cylinder_bm(
            bm, (hw * 0.69, ly, lz), (hw * 0.77, ly, lz), rim_r * 0.052, segments=6, cap_ends=True, mat_idx=1
        )

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    if mats:
        for m in mats:
            mesh.materials.append(m)

    n_polys = len(mesh.polygons)
    if n_polys > 0:
        mesh.polygons.foreach_set('use_smooth', [True] * n_polys)
    bm2 = bmesh.new()
    bm2.from_mesh(mesh)
    thresh = math.radians(40.0)
    for e in bm2.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) >= thresh:
            e.smooth = False
    bm2.to_mesh(mesh)
    bm2.free()
    mesh.update()
    return mesh


def merge_bms_with_mats(bm_list):
    """Merge a list of bmeshes into one bmesh, assigning material_index = list index."""
    bm_out = bmesh.new()
    for idx, bm_part in enumerate(bm_list):
        if len(bm_part.faces) == 0:
            bm_part.free()
            continue
        for f in bm_part.faces:
            f.material_index = idx
        tmp = bpy.data.meshes.new(f'_tmp_merge_{idx}')
        bm_part.to_mesh(tmp)
        bm_part.free()
        bm_out.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    return bm_out


def add_tube_path_bm(bm, points, radius, segments=8, closed=False, cap_ends=True, mat_idx=0):
    """Sweep a circular cross-section smoothly along a 3D polyline."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    if n < 2:
        return []
    rings = []
    prev_nrm = None
    for i in range(n):
        if closed:
            tang = (pts[(i + 1) % n] - pts[(i - 1) % n]).normalized()
        elif i == 0:
            tang = (pts[1] - pts[0]).normalized()
        elif i == n - 1:
            tang = (pts[-1] - pts[-2]).normalized()
        else:
            t0 = (pts[i] - pts[i - 1]).normalized()
            t1 = (pts[i + 1] - pts[i]).normalized()
            tang = (t0 + t1)
            tang = tang.normalized() if tang.length > 1e-5 else t1
        if prev_nrm is None:
            ref = Vector((0, 0, 1)) if abs(tang.z) < 0.9 else Vector((1, 0, 0))
            nrm = tang.cross(ref).normalized()
        else:
            nrm = prev_nrm - tang * prev_nrm.dot(tang)
            if nrm.length < 1e-5:
                ref = Vector((0, 0, 1)) if abs(tang.z) < 0.9 else Vector((1, 0, 0))
                nrm = tang.cross(ref).normalized()
            else:
                nrm.normalize()
        prev_nrm = nrm
        binrm = tang.cross(nrm).normalized()
        ring = []
        for s in range(segments):
            ang = 2.0 * math.pi * s / segments
            off = (nrm * math.cos(ang) + binrm * math.sin(ang)) * radius
            ring.append(bm.verts.new(pts[i] + off))
        rings.append(ring)

    limit = n if closed else n - 1
    for i in range(limit):
        r0 = rings[i]
        r1 = rings[(i + 1) % n]
        for s in range(segments):
            ns = (s + 1) % segments
            try:
                f = bm.faces.new([r0[s], r0[ns], r1[ns], r1[s]])
                f.material_index = mat_idx
            except ValueError:
                pass
    if cap_ends and not closed:
        try:
            f0 = bm.faces.new(list(reversed(rings[0])))
            f0.material_index = mat_idx
            f1 = bm.faces.new(rings[-1])
            f1.material_index = mat_idx
        except ValueError:
            pass
    return rings


def _finalize_wheel_mesh(bm, name, mats, sharp_deg=40.0):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    if mats:
        for m in mats:
            mesh.materials.append(m)
    if len(mesh.polygons) > 0:
        mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    bm2 = bmesh.new()
    bm2.from_mesh(mesh)
    thresh = math.radians(sharp_deg)
    for e in bm2.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0.0) >= thresh:
            e.smooth = False
    bm2.to_mesh(mesh)
    bm2.free()
    mesh.update()
    return mesh


def build_alloy_wheel_mesh(name, radius=0.335, width=0.225, rim_ratio=0.70, segments=20, spokes=10, mats=None):
    """Build a sporty modern multi-spoke alloy wheel (~720 tris). Mats: [tyre, rim, brake]."""
    bm = bmesh.new()
    hw = width * 0.5
    rim_r = radius * rim_ratio
    tread_hw = hw * 0.84
    sw_r = radius * 0.92

    tire_prof = [
        (-hw * 0.90, rim_r * 0.97), (-hw, (rim_r + sw_r) * 0.52), (-hw * 0.95, sw_r),
        (-tread_hw, radius), (tread_hw, radius),
        (hw * 0.95, sw_r), (hw, (rim_r + sw_r) * 0.52), (hw * 0.90, rim_r * 0.97),
    ]
    add_lathe_ring_bm(bm, tire_prof, segments=segments, mat_idx=0, close_profile=True)

    # Outer barrel + inner rim flange
    barrel_prof = [
        (-hw * 0.84, rim_r * 0.98), (-hw * 0.84, rim_r * 0.88),
        ( hw * 0.68, rim_r * 0.88), ( hw * 0.82, rim_r * 0.93),
        ( hw * 0.92, rim_r * 1.00), ( hw * 0.92, rim_r * 0.95),
        ( hw * 0.78, rim_r * 0.98),
    ]
    add_lathe_ring_bm(bm, barrel_prof, segments=segments, mat_idx=1, close_profile=False)

    # Center hub cap & brake disc rotor behind spokes
    add_cylinder_bm(bm, (hw * 0.35, 0, 0), (hw * 0.78, 0, 0), rim_r * 0.24, segments=12, mat_idx=1)
    add_cylinder_bm(bm, (-hw * 0.25, 0, 0), (hw * 0.28, 0, 0), rim_r * 0.72, segments=16, mat_idx=2)

    # Spokes radiating cleanly from center hub to outer barrel (exact 3D orientation!)
    for i in range(spokes):
        ang = 2.0 * math.pi * i / spokes
        p0 = Vector((hw * 0.68, math.cos(ang) * rim_r * 0.20, math.sin(ang) * rim_r * 0.20))
        p1 = Vector((hw * 0.83, math.cos(ang) * rim_r * 0.91, math.sin(ang) * rim_r * 0.91))
        mid = (p0 + p1) * 0.5
        radial = (p1 - p0)
        length = radial.length
        r_dir = radial.normalized()
        tang_dir = Vector((0.0, -math.sin(ang), math.cos(ang)))
        ax_dir = r_dir.cross(tang_dir).normalized()
        rot_m = Matrix((
            (ax_dir.x, r_dir.x, tang_dir.x, 0.0),
            (ax_dir.y, r_dir.y, tang_dir.y, 0.0),
            (ax_dir.z, r_dir.z, tang_dir.z, 0.0),
            (0.0,      0.0,     0.0,        1.0),
        ))
        mat = Matrix.Translation(mid) @ rot_m @ Matrix.Diagonal((0.022, length, rim_r * 0.115, 1.0))
        ret = bmesh.ops.create_cube(bm, size=1.0, matrix=mat)
        for f in {f for v in ret['verts'] for f in v.link_faces}:
            f.material_index = 1

    return _finalize_wheel_mesh(bm, name, mats)


def build_heavy_wheel_mesh(name, radius=0.49, width=0.28, rim_ratio=0.56, segments=20, mats=None):
    """Build a heavy commercial bus/truck wheel with deep dish & lug ring (~680 tris). Mats: [tyre, rim, brake]."""
    bm = bmesh.new()
    hw = width * 0.5
    rim_r = radius * rim_ratio
    tread_hw = hw * 0.84
    sw_r = radius * 0.91

    tire_prof = [
        (-hw * 0.90, rim_r * 0.97), (-hw, (rim_r + sw_r) * 0.52), (-hw * 0.95, sw_r),
        (-tread_hw, radius), (tread_hw, radius),
        (hw * 0.95, sw_r), (hw, (rim_r + sw_r) * 0.52), (hw * 0.90, rim_r * 0.97),
    ]
    add_lathe_ring_bm(bm, tire_prof, segments=segments, mat_idx=0, close_profile=True)

    rim_prof = [
        (-hw * 0.82, rim_r * 0.92), (hw * 0.35, rim_r * 0.92),
        (hw * 0.88, rim_r * 1.00), (hw * 0.88, rim_r * 0.90),
        (hw * 0.28, rim_r * 0.80), (hw * 0.25, rim_r * 0.45),
        (hw * 0.62, rim_r * 0.38), (hw * 0.68, rim_r * 0.26),
    ]
    rings = add_lathe_ring_bm(bm, rim_prof, segments=segments, mat_idx=1, close_profile=False)
    cv = bm.verts.new((hw * 0.70, 0, 0))
    for i in range(segments):
        ni = (i + 1) % segments
        f = bm.faces.new([rings[-1][i], rings[-1][ni], cv])
        f.material_index = 2
    for l in range(8):
        ang = 2.0 * math.pi * l / 8.0
        ly, lz = math.cos(ang) * rim_r * 0.56, math.sin(ang) * rim_r * 0.56
        add_cylinder_bm(bm, (hw * 0.25, ly, lz), (hw * 0.38, ly, lz), rim_r * 0.048, segments=6, mat_idx=2)

    return _finalize_wheel_mesh(bm, name, mats)


def build_spoked_moto_wheel_mesh(name, radius=0.315, width=0.095, rim_ratio=0.73, segments=22, spokes=14, mats=None):
    """Build a motorcycle spoked wheel (narrow tire + chrome rim + hub + crossed spokes). Mats: [tyre, rim, brake]."""
    bm = bmesh.new()
    hw = width * 0.5
    rim_r = radius * rim_ratio
    sw_r = radius * 0.90

    tire_prof = [
        (-hw * 0.80, rim_r * 0.98), (-hw, (rim_r + sw_r) * 0.55), (-hw * 0.85, sw_r),
        (-hw * 0.35, radius * 0.992), (0.0, radius), (hw * 0.35, radius * 0.992),
        (hw * 0.85, sw_r), (hw, (rim_r + sw_r) * 0.55), (hw * 0.80, rim_r * 0.98),
    ]
    add_lathe_ring_bm(bm, tire_prof, segments=segments, mat_idx=0, close_profile=True)

    rim_prof = [
        (-hw * 0.78, rim_r * 0.99), (-hw * 0.55, rim_r * 0.89),
        ( hw * 0.55, rim_r * 0.89), ( hw * 0.78, rim_r * 0.99),
        ( hw * 0.82, rim_r * 1.00), (-hw * 0.82, rim_r * 1.00),
    ]
    add_lathe_ring_bm(bm, rim_prof, segments=segments, mat_idx=1, close_profile=True)

    # Central wheel drum hub
    hub_r = rim_r * 0.28
    add_cylinder_bm(bm, (-hw * 1.05, 0, 0), (hw * 1.05, 0, 0), hub_r, segments=14, mat_idx=1)
    add_cylinder_bm(bm, (-hw * 1.18, 0, 0), (hw * 1.18, 0, 0), hub_r * 0.35, segments=10, mat_idx=2)

    # Wire spokes lacing both sides of hub to rim
    for i in range(spokes):
        ang0 = 2.0 * math.pi * i / spokes
        ang1 = ang0 + 0.22
        sx = 1.0 if (i % 2 == 0) else -1.0
        p0 = (hw * 0.72 * sx, math.cos(ang0) * hub_r * 0.85, math.sin(ang0) * hub_r * 0.85)
        p1 = (0.0, math.cos(ang1) * rim_r * 0.91, math.sin(ang1) * rim_r * 0.91)
        add_cylinder_bm(bm, p0, p1, 0.0042, segments=4, cap_ends=False, mat_idx=1)

    return _finalize_wheel_mesh(bm, name, mats)


def finalize_and_export_vehicle(root, out_dir, slug, fbx_name, lod1_ratio=0.40, samples=16, res=(768, 432)):
    """Save .blend, export LOD0 + LOD1 FBX, save stats JSON, and render 4-view studio sheet."""
    import json
    os.makedirs(out_dir, exist_ok=True)
    stats_lod0 = report_scene_stats(root)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, f'{slug}.blend'))
    export_unity_fbx(root, os.path.join(out_dir, f'{fbx_name}.fbx'))
    stats_lod1 = export_unity_lod1_fbx(root, os.path.join(out_dir, f'{fbx_name}_LOD1.fbx'), ratio=lod1_ratio)
    summary = {'lod0': stats_lod0, 'lod1': stats_lod1}
    with open(os.path.join(out_dir, f'{slug}_stats.json'), 'w', encoding='utf-8') as f:
        json.dump(summary, f, indent=2)
    print(f'=== {slug.upper()} STATS ===')
    print(json.dumps(summary, indent=2))
    renders = render_studio_sheet(root, out_dir, prefix=slug, samples=samples, res=res)
    print(f'RENDERED {slug}:', renders.get('sheet', ''))
    return summary, renders


def report_scene_stats(root_obj):
    """Compute triangle count, mesh count, material count and bounding box."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total_tris = 0
    total_verts = 0
    part_stats = []
    mat_names = set()
    min_co = Vector((1e9, 1e9, 1e9))
    max_co = Vector((-1e9, -1e9, -1e9))

    objs = [root_obj] + list(root_obj.children_recursive)
    for obj in objs:
        if obj.type != 'MESH':
            continue
        eval_obj = obj.evaluated_get(depsgraph)
        me = eval_obj.to_mesh()
        me.calc_loop_triangles()
        tris = len(me.loop_triangles)
        verts = len(me.vertices)
        total_tris += tris
        total_verts += verts
        part_stats.append((obj.name, tris, len(obj.data.materials)))
        for m in obj.data.materials:
            if m:
                mat_names.add(m.name)
        mat_w = obj.matrix_world
        for v in me.vertices:
            w = mat_w @ v.co
            min_co.x = min(min_co.x, w.x)
            min_co.y = min(min_co.y, w.y)
            min_co.z = min(min_co.z, w.z)
            max_co.x = max(max_co.x, w.x)
            max_co.y = max(max_co.y, w.y)
            max_co.z = max(max_co.z, w.z)
        eval_obj.to_mesh_clear()

    dims = max_co - min_co
    return {
        'total_tris': total_tris,
        'total_verts': total_verts,
        'materials': sorted(mat_names),
        'material_count': len(mat_names),
        'dimensions_m': (round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)),
        'min_z': round(min_co.z, 4),
        'parts': part_stats,
    }


def export_unity_fbx(root_obj, fbx_path):
    """Export vehicle hierarchy cleanly for Unity (Blender +Y -> Unity +Z Forward, Blender +Z -> Unity +Y Up)."""
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    objs = [root_obj] + list(root_obj.children_recursive)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root_obj

    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='Z',
        axis_up='Y',
        use_space_transform=True,
        bake_space_transform=True,
        object_types={'EMPTY', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        embed_textures=False,
        path_mode='AUTO',
    )


def export_unity_lod1_fbx(root_obj, fbx_path, ratio=0.40):
    """Build a decimated mobile LOD1 copy, export FBX, return stats, and remove temp copy."""
    lod_col = ensure_collection('_TempLOD1')
    lod_root = bpy.data.objects.new(root_obj.name + '_LOD1', None)
    link_to_collection(lod_root, lod_col)

    for child in root_obj.children:
        if child.type != 'MESH':
            continue
        # Skip interior in LOD1 (not visible past 25 m on mobile!)
        if child.name == 'Interieur':
            continue
        new_mesh = child.data.copy()
        new_obj = bpy.data.objects.new(child.name, new_mesh)
        link_to_collection(new_obj, lod_col)
        new_obj.parent = lod_root
        new_obj.location = child.location.copy()
        new_obj.rotation_euler = child.rotation_euler.copy()
        new_obj.scale = child.scale.copy()
        if len(new_mesh.polygons) > 120:
            mod = new_obj.modifiers.new('Decimate', 'DECIMATE')
            mod.decimate_type = 'COLLAPSE'
            mod.ratio = ratio
            bpy.context.view_layer.objects.active = new_obj
            new_obj.select_set(True)
            bpy.ops.object.modifier_apply(modifier='Decimate')
            new_obj.select_set(False)
            apply_smooth_with_sharp_angle(new_obj, angle_deg=44.0)

    lod_stats = report_scene_stats(lod_root)
    export_unity_fbx(lod_root, fbx_path)

    for o in list(lod_col.objects):
        me = o.data if o.type == 'MESH' else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(lod_col)
    return lod_stats


def render_studio_sheet(root_obj, output_dir, prefix="preview", samples=24, res=(800, 450)):
    """Render 4 neutral studio views (3/4 front, side, 3/4 rear, top)."""
    os.makedirs(output_dir, exist_ok=True)
    scene = bpy.context.scene

    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    try:
        scene.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    scene.cycles.max_bounces = 5
    scene.cycles.diffuse_bounces = 2
    scene.cycles.glossy_bounces = 2
    scene.cycles.transmission_bounces = 2
    scene.cycles.transparent_max_bounces = 4
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'AgX'
    try:
        scene.view_settings.look = 'AgX - Punchy'
    except Exception:
        pass
    scene.view_settings.exposure = -0.05

    studio_col = ensure_collection('_Studio')

    floor_bm = bmesh.new()
    bmesh.ops.create_grid(floor_bm, x_segments=1, y_segments=1, size=45.0)
    floor_mat = make_pbr_material('M_StudioFloor', base_color=(0.36, 0.36, 0.37, 1.0), roughness=0.90, specular=0.08)
    floor_obj = create_mesh_object('_StudioFloor', floor_bm, floor_mat, studio_col, angle_deg=None)
    floor_obj.location = (0.0, 0.0, -0.001)

    world = bpy.data.worlds.new('_StudioWorld')
    scene.world = world
    world.use_nodes = True
    wnt = world.node_tree
    wnt.nodes.clear()
    wout = wnt.nodes.new('ShaderNodeOutputWorld')
    bg = wnt.nodes.new('ShaderNodeBackground')
    bg.inputs['Color'].default_value = (0.44, 0.45, 0.47, 1.0)
    bg.inputs['Strength'].default_value = 0.90
    wnt.links.new(bg.outputs['Background'], wout.inputs['Surface'])

    def add_area_light(name, loc, target, energy, size_x, size_y, color=(1.0, 1.0, 1.0)):
        ldata = bpy.data.lights.new(name=name, type='AREA')
        ldata.energy = energy
        ldata.shape = 'RECTANGLE'
        ldata.size = size_x
        ldata.size_y = size_y
        ldata.color = color
        lobj = bpy.data.objects.new(name, ldata)
        link_to_collection(lobj, studio_col)
        lobj.location = Vector(loc)
        direction = Vector(target) - lobj.location
        lobj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        return lobj

    # Balanced studio lighting matching user's reference renders (soft shadows, vivid paint, dark reflective glass)
    add_area_light('_KeyLight',    (-5.5,  5.0, 5.5), (0.0, 0.0, 0.65), 850, 4.5, 3.5, (1.0, 0.99, 0.96))
    add_area_light('_FillLight',   ( 6.0,  2.0, 4.0), (0.0, 0.0, 0.65), 420, 5.0, 3.5, (0.94, 0.97, 1.0))
    add_area_light('_TopSoftbox',  ( 0.0,  0.0, 7.2), (0.0, 0.0, 0.50), 380, 3.0, 5.5, (1.0, 1.0, 1.0))
    add_area_light('_RimLight',    ( 4.0, -5.5, 3.8), (0.0, 0.0, 0.65), 420, 4.5, 3.0, (0.98, 0.99, 1.0))

    cam_data = bpy.data.cameras.new('_StudioCam')
    cam_obj = bpy.data.objects.new('_StudioCam', cam_data)
    link_to_collection(cam_obj, studio_col)
    scene.camera = cam_obj

    def point_camera(loc, target, lens=65.0, ortho=False, ortho_scale=5.6, roll_deg=0.0):
        cam_obj.location = Vector(loc)
        direction = Vector(target) - cam_obj.location
        up_axis = 'X' if abs(direction.normalized().z) > 0.99 else 'Y'
        rot_q = direction.to_track_quat('-Z', up_axis)
        cam_obj.rotation_euler = rot_q.to_euler()
        if roll_deg != 0.0:
            cam_obj.rotation_euler.rotate_axis('Z', math.radians(roll_deg))
        if ortho:
            cam_data.type = 'ORTHO'
            cam_data.ortho_scale = ortho_scale
        else:
            cam_data.type = 'PERSP'
            cam_data.lens = lens

    stats = report_scene_stats(root_obj)
    dx, dy, dz = stats['dimensions_m']
    max_len = max(dy, dx, 3.6)
    cz = dz * 0.46

    # Well-framed camera views (full vehicle + clean studio margin on all sides!)
    views = [
        ('front34', (-max_len * 1.18,  max_len * 1.42, cz + max_len * 0.34), (0.0, 0.0, cz), 58.0, False, 5.6, 0.0),
        ('side',    (-max_len * 2.10,  0.0,            cz + 0.04),           (0.0, 0.0, cz), 90.0, True,  max_len * 1.38, 0.0),
        ('rear34',  (-max_len * 1.18, -max_len * 1.42, cz + max_len * 0.34), (0.0, 0.0, cz), 58.0, False, 5.6, 0.0),
        ('top',     (0.0,              0.0,            max_len * 2.40),      (0.0, 0.0, cz), 90.0, True,  max_len * 1.42, -90.0),
    ]

    scene.render.resolution_x = res[0]
    scene.render.resolution_y = res[1]
    scene.render.resolution_percentage = 100

    rendered_paths = {}
    for vname, loc, tgt, lens, ortho, oscale, roll in views:
        point_camera(loc, tgt, lens=lens, ortho=ortho, ortho_scale=oscale, roll_deg=roll)
        out_path = os.path.join(output_dir, f'{prefix}_{vname}.png')
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
        rendered_paths[vname] = out_path

    for o in list(studio_col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(studio_col)
    scene.world = None

    # Composite 2x2 turnaround contact sheet using Blender's bundled numpy + Image API
    try:
        import numpy as np
        w, h = res
        sheet = np.zeros((h * 2, w * 2, 4), dtype=np.float32)
        order = [('rear34', 0, 0), ('top', 0, 1), ('front34', 1, 0), ('side', 1, 1)]
        for key, row_idx, col_idx in order:
            img = bpy.data.images.load(rendered_paths[key], check_existing=False)
            pix = np.empty(w * h * 4, dtype=np.float32)
            img.pixels.foreach_get(pix)
            arr = pix.reshape((h, w, 4))
            sheet[row_idx * h:(row_idx + 1) * h, col_idx * w:(col_idx + 1) * w, :] = arr
            bpy.data.images.remove(img)
        sheet_img = bpy.data.images.new(f'{prefix}_sheet', width=w * 2, height=h * 2, alpha=False)
        sheet_img.pixels.foreach_set(sheet.ravel())
        sheet_path = os.path.join(output_dir, f'{prefix}_sheet.png')
        sheet_img.filepath_raw = sheet_path
        sheet_img.file_format = 'PNG'
        sheet_img.save()
        bpy.data.images.remove(sheet_img)
        rendered_paths['sheet'] = sheet_path
    except Exception as exc:
        print('Sheet compose warning:', exc)

    return rendered_paths
