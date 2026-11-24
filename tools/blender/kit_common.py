"""Shared Blender 5.x bmesh helpers, materials, UV/vertex-color utilities, and FBX exporter
for the Ivory State city architectural and street-furniture asset kit."""

import math
import os
import bpy
import bmesh
from mathutils import Matrix, Vector, Euler


# Material slot names mapped 1-to-1 in Unity by BlenderCityKit.cs
MAT_FACADE_WALL = "Mat_FacadeWall"
MAT_FACADE_TRIM = "Mat_FacadeTrim"
MAT_GLASS_NIGHT = "Mat_GlassNight"
MAT_ROOF_TILE = "Mat_RoofTile"
MAT_ROOF_METAL = "Mat_RoofMetal"
MAT_DARK_METAL = "Mat_DarkMetal"
MAT_GALV_STEEL = "Mat_GalvSteel"
MAT_CONCRETE = "Mat_Concrete"
MAT_WOOD = "Mat_Wood"
MAT_ACCENT_PAINT = "Mat_AccentPaint"
MAT_LAMP_LED = "Mat_LampLED"
MAT_LAMP_SODIUM = "Mat_LampSodium"


MATERIAL_SPECS = {
    MAT_FACADE_WALL: ((0.84, 0.77, 0.66, 1.0), 0.0, 0.78, (0.0, 0.0, 0.0), 0.0),
    MAT_FACADE_TRIM: ((0.90, 0.88, 0.84, 1.0), 0.0, 0.65, (0.0, 0.0, 0.0), 0.0),
    MAT_GLASS_NIGHT: ((0.14, 0.24, 0.32, 1.0), 0.35, 0.12, (0.95, 0.72, 0.35), 0.25),
    MAT_ROOF_TILE: ((0.64, 0.28, 0.17, 1.0), 0.0, 0.72, (0.0, 0.0, 0.0), 0.0),
    MAT_ROOF_METAL: ((0.48, 0.50, 0.53, 1.0), 0.35, 0.55, (0.0, 0.0, 0.0), 0.0),
    MAT_DARK_METAL: ((0.14, 0.15, 0.17, 1.0), 0.78, 0.38, (0.0, 0.0, 0.0), 0.0),
    MAT_GALV_STEEL: ((0.62, 0.65, 0.68, 1.0), 0.85, 0.42, (0.0, 0.0, 0.0), 0.0),
    MAT_CONCRETE: ((0.66, 0.65, 0.62, 1.0), 0.0, 0.86, (0.0, 0.0, 0.0), 0.0),
    MAT_WOOD: ((0.42, 0.26, 0.14, 1.0), 0.0, 0.68, (0.0, 0.0, 0.0), 0.0),
    MAT_ACCENT_PAINT: ((0.88, 0.55, 0.16, 1.0), 0.05, 0.55, (0.0, 0.0, 0.0), 0.0),
    MAT_LAMP_LED: ((1.0, 0.95, 0.85, 1.0), 0.0, 0.20, (1.0, 0.94, 0.80), 4.0),
    MAT_LAMP_SODIUM: ((1.0, 0.66, 0.26, 1.0), 0.0, 0.25, (1.0, 0.60, 0.20), 4.0),
}


def ensure_materials():
    """Create or fetch the shared kit materials in Blender."""
    mats = {}
    for name, (base, metallic, rough, emis_col, emis_str) in MATERIAL_SPECS.items():
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name=name)
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.inputs["Base Color"].default_value = base
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = rough
        if emis_str > 0.0:
            bsdf.inputs["Emission Color"].default_value = (*emis_col, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emis_str
        # Blend vertex color slightly in Blender preview for AccentPaint/FacadeWall
        if name in (MAT_FACADE_WALL, MAT_ACCENT_PAINT, MAT_ROOF_TILE, MAT_ROOF_METAL):
            vc = nt.nodes.new("ShaderNodeVertexColor")
            vc.layer_name = "Col"
            mul = nt.nodes.new("ShaderNodeMix")
            mul.data_type = "RGBA"
            mul.blend_type = "MULTIPLY"
            mul.inputs["Factor"].default_value = 1.0
            mul.inputs["A"].default_value = base
            nt.links.new(vc.outputs["Color"], mul.inputs["B"])
            nt.links.new(mul.outputs["Result"], bsdf.inputs["Base Color"])
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        mats[name] = mat
    return mats


def ensure_collection(name, parent=None):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(col)
    return col


class KitBuilder:
    """Builds a single clean multi-material mesh with metric UV0, UV1 window ids, and vertex colors."""

    def __init__(self, name, materials):
        self.name = name
        self.materials = materials
        self.bm = bmesh.new()
        self.uv0 = self.bm.loops.layers.uv.new("UVMap")
        self.uv1 = self.bm.loops.layers.uv.new("UV1")
        self.col = self.bm.loops.layers.color.new("Col")
        self.slot_names = []
        self.slot_index = {}

    def _mat_idx(self, mat_name):
        if mat_name not in self.slot_index:
            self.slot_index[mat_name] = len(self.slot_names)
            self.slot_names.append(mat_name)
        return self.slot_index[mat_name]

    def _assign_face(self, face, mat_idx, color=(1.0, 1.0, 1.0, 1.0), smooth=False,
                     uv1_rect=None, uv_scale=1.0, custom_uv0=None):
        face.material_index = mat_idx
        face.smooth = smooth
        n = face.normal
        # Choose dominant axis for crisp metric box-mapped UV0
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for i, loop in enumerate(face.loops):
            v = loop.vert.co
            if custom_uv0 is not None:
                u, w = custom_uv0[i]
            elif az >= ax and az >= ay:
                u, w = v.x * uv_scale, v.y * uv_scale
            elif ax >= ay:
                u, w = v.y * uv_scale, v.z * uv_scale
            else:
                u, w = v.x * uv_scale, v.z * uv_scale
            loop[self.uv0].uv = (u, w)
            if uv1_rect is not None:
                # uv1_rect = (u0, v0, u1, v1) inside a cell or across the pane
                # We assign based on corner index in quad when possible
                loop[self.uv1].uv = uv1_rect[i % len(uv1_rect)]
            else:
                loop[self.uv1].uv = (-1.0, -1.0)
            loop[self.col] = color

    def add_box(self, center, size, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                rot_euler=(0.0, 0.0, 0.0), skip_bottom=False, skip_top=False,
                uv_scale=1.0):
        """Adds an oriented box centered at `center` with dimensions `size=(sx, sy, sz)`."""
        sx, sy, sz = size
        if sx <= 1e-5 or sy <= 1e-5 or sz <= 1e-5:
            return
        mat_idx = self._mat_idx(mat_name)
        rot = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        xf = Matrix.Translation(Vector(center)) @ rot @ Matrix.Diagonal((sx, sy, sz, 1.0))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xf)
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        local_up = rot.to_3x3() @ Vector((0.0, 0.0, 1.0))
        to_remove = []
        for f in faces:
            dot_up = f.normal.dot(local_up)
            if skip_bottom and dot_up < -0.9:
                to_remove.append(f)
            elif skip_top and dot_up > 0.9:
                to_remove.append(f)
            else:
                self._assign_face(f, mat_idx, color=color, smooth=False, uv_scale=uv_scale)
        if to_remove:
            bmesh.ops.delete(self.bm, geom=to_remove, context="FACES")

    def add_box_base(self, base_center, size, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                     rot_euler=(0.0, 0.0, 0.0), skip_bottom=True, skip_top=False, uv_scale=1.0):
        cx, cy, cz = base_center
        rot = Euler(rot_euler, "XYZ").to_matrix()
        offset = rot @ Vector((0.0, 0.0, size[2] * 0.5))
        self.add_box((cx + offset.x, cy + offset.y, cz + offset.z), size, mat_name,
                     color=color, rot_euler=rot_euler, skip_bottom=skip_bottom,
                     skip_top=skip_top, uv_scale=uv_scale)

    def add_bevel_box(self, center, size, bevel, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                      rot_euler=(0.0, 0.0, 0.0), segments=1, uv_scale=1.0):
        """Adds a crisp chamfered/beveled box for architectural copings, plinths, and luminaires."""
        sx, sy, sz = size
        mat_idx = self._mat_idx(mat_name)
        rot = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        xf = Matrix.Translation(Vector(center)) @ rot @ Matrix.Diagonal((sx, sy, sz, 1.0))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xf)
        verts = set(ret["verts"])
        edges = [e for e in self.bm.edges if all(v in verts for v in e.verts)]
        b_ret = bmesh.ops.bevel(
            self.bm, geom=edges, offset=min(bevel, min(sx, sy, sz) * 0.35),
            offset_type="OFFSET", segments=segments, profile=0.5, affect="EDGES"
        )
        new_verts = verts | set(b_ret["verts"])
        faces = [f for f in self.bm.faces if all(v in new_verts for v in f.verts)]
        for f in faces:
            self._assign_face(f, mat_idx, color=color, smooth=segments > 1, uv_scale=uv_scale)

    def add_cylinder(self, base_center, radius_bottom, radius_top, height, segments, mat_name,
                     color=(1.0, 1.0, 1.0, 1.0), rot_euler=(0.0, 0.0, 0.0),
                     cap_bottom=False, cap_top=True, smooth_sides=True, uv_scale=1.0):
        """Adds a tapered cylinder starting at base_center extending along +Z (rotated by rot_euler)."""
        mat_idx = self._mat_idx(mat_name)
        rot = Euler(rot_euler, "XYZ").to_matrix().to_4x4()
        offset = rot.to_3x3() @ Vector((0.0, 0.0, height * 0.5))
        xf = Matrix.Translation(Vector(base_center) + offset) @ rot
        ret = bmesh.ops.create_cone(
            self.bm, cap_ends=True, cap_tris=False, segments=segments,
            radius1=radius_bottom, radius2=radius_top, depth=height, matrix=xf
        )
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        local_up = rot.to_3x3() @ Vector((0.0, 0.0, 1.0))
        to_remove = []
        for f in faces:
            dot_up = f.normal.dot(local_up)
            is_cap = abs(dot_up) > 0.85
            if is_cap and dot_up < -0.85 and not cap_bottom:
                to_remove.append(f)
            elif is_cap and dot_up > 0.85 and not cap_top:
                to_remove.append(f)
            else:
                self._assign_face(f, mat_idx, color=color, smooth=(smooth_sides and not is_cap), uv_scale=uv_scale)
        if to_remove:
            bmesh.ops.delete(self.bm, geom=to_remove, context="FACES")

    def add_pipe(self, points, radii, segments, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                 cap_start=False, cap_end=True, smooth=True):
        """Sweeps a circular (or tapered) tube along a 3D polyline `points`."""
        if len(points) < 2:
            return
        if isinstance(radii, (int, float)):
            radii = [float(radii)] * len(points)
        mat_idx = self._mat_idx(mat_name)
        pts = [Vector(p) for p in points]
        rings = []
        prev_up = None
        for i, p in enumerate(pts):
            if i == 0:
                tangent = (pts[1] - pts[0]).normalized()
            elif i == len(pts) - 1:
                tangent = (pts[-1] - pts[-2]).normalized()
            else:
                t0 = (pts[i] - pts[i - 1]).normalized()
                t1 = (pts[i + 1] - pts[i]).normalized()
                tangent = (t0 + t1)
                tangent = tangent.normalized() if tangent.length > 1e-4 else t1
            ref = Vector((0, 0, 1)) if abs(tangent.z) < 0.92 else Vector((1, 0, 0))
            if prev_up is None:
                side = tangent.cross(ref).normalized()
            else:
                side = (prev_up - tangent * prev_up.dot(tangent))
                side = side.normalized() if side.length > 1e-4 else tangent.cross(ref).normalized()
            up = side.cross(tangent).normalized()
            prev_up = side
            r = radii[i]
            ring = []
            for s in range(segments):
                a = 2.0 * math.pi * s / segments
                pos = p + (side * math.cos(a) + up * math.sin(a)) * r
                ring.append(self.bm.verts.new(pos))
            rings.append(ring)

        for i in range(len(rings) - 1):
            r0, r1 = rings[i], rings[i + 1]
            for s in range(segments):
                sn = (s + 1) % segments
                f = self.bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))
                self._assign_face(f, mat_idx, color=color, smooth=smooth)

        if cap_start:
            f = self.bm.faces.new(list(reversed(rings[0])))
            self._assign_face(f, mat_idx, color=color, smooth=False)
        if cap_end:
            f = self.bm.faces.new(rings[-1])
            self._assign_face(f, mat_idx, color=color, smooth=False)

    def add_quad(self, p0, p1, p2, p3, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                 smooth=False, uv1_rect=None, custom_uv0=None, expected_normal=None):
        mat_idx = self._mat_idx(mat_name)
        verts = [self.bm.verts.new(Vector(p)) for p in (p0, p1, p2, p3)]
        f = self.bm.faces.new(verts)
        if expected_normal is not None:
            f.normal_update()
            if f.normal.dot(Vector(expected_normal)) < 0.0:
                f.normal_flip()
                f.normal_update()
        self._assign_face(f, mat_idx, color=color, smooth=smooth, uv1_rect=uv1_rect, custom_uv0=custom_uv0)
        return f

    def add_tri(self, p0, p1, p2, mat_name, color=(1.0, 1.0, 1.0, 1.0), smooth=False):
        mat_idx = self._mat_idx(mat_name)
        verts = [self.bm.verts.new(Vector(p)) for p in (p0, p1, p2)]
        f = self.bm.faces.new(verts)
        self._assign_face(f, mat_idx, color=color, smooth=smooth)
        return f

    def add_extruded_y_profile(self, profile_xz, y0, y1, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                               cap_ends=True, smooth=False):
        """Extrudes a closed 2D (x, z) polygon along Y from y0 to y1 (useful for W-beams, curbs, barriers)."""
        mat_idx = self._mat_idx(mat_name)
        ring0 = [self.bm.verts.new(Vector((x, y0, z))) for x, z in profile_xz]
        ring1 = [self.bm.verts.new(Vector((x, y1, z))) for x, z in profile_xz]
        n = len(profile_xz)
        for i in range(n):
            ni = (i + 1) % n
            f = self.bm.faces.new((ring0[i], ring1[i], ring1[ni], ring0[ni]))
            self._assign_face(f, mat_idx, color=color, smooth=smooth)
        if cap_ends:
            f0 = self.bm.faces.new(list(reversed(ring0)))
            self._assign_face(f0, mat_idx, color=color, smooth=False)
            f1 = self.bm.faces.new(ring1)
            self._assign_face(f1, mat_idx, color=color, smooth=False)

    def add_extruded_x_profile(self, profile_yz, x0, x1, mat_name, color=(1.0, 1.0, 1.0, 1.0),
                               cap_ends=True, smooth=False):
        """Extrudes a closed 2D (y, z) polygon along X from x0 to x1 (for guardrails, railings)."""
        mat_idx = self._mat_idx(mat_name)
        ring0 = [self.bm.verts.new(Vector((x0, y, z))) for y, z in profile_yz]
        ring1 = [self.bm.verts.new(Vector((x1, y, z))) for y, z in profile_yz]
        n = len(profile_yz)
        for i in range(n):
            ni = (i + 1) % n
            f = self.bm.faces.new((ring0[i], ring0[ni], ring1[ni], ring1[i]))
            self._assign_face(f, mat_idx, color=color, smooth=smooth)
        if cap_ends:
            f0 = self.bm.faces.new(list(reversed(ring0)))
            self._assign_face(f0, mat_idx, color=color, smooth=False)
            f1 = self.bm.faces.new(ring1)
            self._assign_face(f1, mat_idx, color=color, smooth=False)

    def add_window_unit(self, center, normal_dir, width, height, frame_mat=MAT_FACADE_TRIM,
                        glass_mat=MAT_GLASS_NIGHT, sill_mat=MAT_FACADE_TRIM,
                        pane_id=(0, 1), has_sill=True, has_hood=False,
                        has_shutters=False, shutter_color=(0.38, 0.27, 0.17, 1.0),
                        mullion_cols=1):
        """Adds a realistic 3D window mounted proud on the wall surface: protruding outer frame,
        slightly recessed reflective/night-lit glass pane (front of wall plane), sill, hood & shutters."""
        n = Vector(normal_dir).normalized()
        up = Vector((0.0, 0.0, 1.0))
        right = up.cross(n).normalized()
        c = Vector(center)
        frame_depth = 0.09
        frame_t = 0.055

        # Outer 4 frame members projecting +0.075m from wall plane so glass sits cleanly inside
        frame_c = c + n * (frame_depth * 0.45)
        for s in (-1.0, 1.0):
            p = frame_c + right * s * (width * 0.5 - frame_t * 0.5)
            self._add_oriented_box(p, right, n, up, (frame_t, frame_depth, height), frame_mat)
        for s in (-1.0, 1.0):
            p = frame_c + up * s * (height * 0.5 - frame_t * 0.5)
            self._add_oriented_box(p, right, n, up, (width - frame_t * 2, frame_depth, frame_t), frame_mat)

        # Optional vertical mullion
        if mullion_cols >= 2:
            for k in range(1, mullion_cols):
                frac = -0.5 + k / float(mullion_cols)
                p = frame_c + right * (frac * width)
                self._add_oriented_box(p, right, n, up, (0.04, frame_depth * 0.85, height - frame_t * 2), frame_mat)

        # Recessed glass pane sitting +0.018m in front of wall plane (zero Z-fighting, cleanly framed)
        gw = (width - frame_t * 2) * 0.5
        gh = (height - frame_t * 2) * 0.5
        gc = c + n * 0.018
        px, py = pane_id
        uv1_quad = [
            (px + 0.08, py + 0.08),
            (px + 0.92, py + 0.08),
            (px + 0.92, py + 0.92),
            (px + 0.08, py + 0.92),
        ]
        # Winding: cross(right, up) = +n so the front face always points outward along +n.
        p0 = gc - right * gw - up * gh
        p1 = gc + right * gw - up * gh
        p2 = gc + right * gw + up * gh
        p3 = gc - right * gw + up * gh
        self.add_quad(p0, p1, p2, p3, glass_mat, uv1_rect=uv1_quad, expected_normal=n)

        if has_sill:
            sill_p = c - up * (height * 0.5 + 0.03) + n * 0.075
            self._add_oriented_box(sill_p, right, n, up, (width + 0.14, 0.18, 0.06), sill_mat)

        if has_hood:
            hood_p = c + up * (height * 0.5 + 0.04) + n * 0.14
            self._add_oriented_box(hood_p, right, n, up, (width + 0.18, 0.30, 0.07), sill_mat)

        if has_shutters:
            sw = width * 0.42
            for s in (-1.0, 1.0):
                sp = c + right * s * (width * 0.5 + sw * 0.5 + 0.02) + n * 0.045
                self._add_oriented_box(sp, right, n, up, (sw, 0.045, height * 0.96), MAT_WOOD, color=shutter_color)

    def add_door_unit(self, base_center, normal_dir, width=1.1, height=2.2,
                      frame_mat=MAT_FACADE_TRIM, door_mat=MAT_WOOD,
                      door_color=(0.35, 0.22, 0.12, 1.0), glazed=False, pane_id=(0, 0)):
        """Adds a realistic entry door proud of the wall plane with frame, panel/glass, and handle."""
        n = Vector(normal_dir).normalized()
        up = Vector((0.0, 0.0, 1.0))
        right = up.cross(n).normalized()
        c = Vector(base_center) + up * (height * 0.5)
        frame_t = 0.075
        depth = 0.10
        frame_c = c + n * (depth * 0.45)

        for s in (-1.0, 1.0):
            self._add_oriented_box(frame_c + right * s * (width * 0.5 - frame_t * 0.5), right, n, up, (frame_t, depth, height), frame_mat)
        self._add_oriented_box(frame_c + up * (height * 0.5 - frame_t * 0.5), right, n, up, (width, depth, frame_t), frame_mat)

        if glazed:
            gw = (width - frame_t * 2) * 0.5
            gh = (height - frame_t) * 0.5
            gc = c + n * 0.022
            px, py = pane_id
            uv1_quad = [(px + 0.08, py + 0.08), (px + 0.92, py + 0.08), (px + 0.92, py + 0.92), (px + 0.08, py + 0.92)]
            self.add_quad(gc - right * gw - up * gh, gc + right * gw - up * gh,
                          gc + right * gw + up * gh, gc - right * gw + up * gh,
                          MAT_GLASS_NIGHT, uv1_rect=uv1_quad, expected_normal=n)
            self._add_oriented_box(c + n * 0.028, right, n, up, (0.05, 0.03, height - frame_t), frame_mat)
        else:
            self._add_oriented_box(c + n * 0.025, right, n, up, (width - frame_t * 2, 0.04, height - frame_t), door_mat, color=door_color)
        # Door handle
        self._add_oriented_box(c - up * 0.05 + right * (width * 0.30) + n * 0.055, right, n, up, (0.04, 0.04, 0.18), MAT_GALV_STEEL)

    def _add_oriented_box(self, center, right, forward, up, size, mat_name, color=(1.0, 1.0, 1.0, 1.0)):
        sx, sy, sz = size
        mat_idx = self._mat_idx(mat_name)
        basis = Matrix((
            (right.x, forward.x, up.x, 0.0),
            (right.y, forward.y, up.y, 0.0),
            (right.z, forward.z, up.z, 0.0),
            (0.0, 0.0, 0.0, 1.0),
        ))
        if basis.determinant() < 0.0:
            basis = Matrix((
                (right.x, -forward.x, up.x, 0.0),
                (right.y, -forward.y, up.y, 0.0),
                (right.z, -forward.z, up.z, 0.0),
                (0.0, 0.0, 0.0, 1.0),
            ))
        xf = Matrix.Translation(Vector(center)) @ basis @ Matrix.Diagonal((sx, sy, sz, 1.0))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xf)
        verts = set(ret["verts"])
        faces = [f for f in self.bm.faces if all(v in verts for v in f.verts)]
        for f in faces:
            self._assign_face(f, mat_idx, color=color, smooth=False)

    def add_hip_roof_with_eaves(self, base_center, size_xy, roof_height, overhang=0.55,
                                mat_roof=MAT_ROOF_TILE, mat_fascia=MAT_FACADE_TRIM,
                                mat_soffit=MAT_WOOD, color=(1.0, 1.0, 1.0, 1.0),
                                fascia_h=0.14):
        """Adds an architectural hipped roof with soffit overhang, fascia trim, and ridge cap."""
        cx, cy, cz = base_center
        wx, wy = size_xy
        hx = wx * 0.5 + overhang
        hy = wy * 0.5 + overhang

        # Fascia rim & soffit plate at base
        self.add_box((cx, cy, cz - fascia_h * 0.35), (hx * 2.0, hy * 2.0, fascia_h), mat_fascia,
                     color=(0.92, 0.90, 0.86, 1.0))
        # Soffit under-eaves
        self.add_quad(
            (cx - hx, cy + hy, cz - fascia_h * 0.85),
            (cx + hx, cy + hy, cz - fascia_h * 0.85),
            (cx + hx, cy - hy, cz - fascia_h * 0.85),
            (cx - hx, cy - hy, cz - fascia_h * 0.85),
            mat_soffit, color=(0.55, 0.40, 0.25, 1.0)
        )

        # Pitched hip planes
        z0 = cz + fascia_h * 0.15
        zt = z0 + roof_height
        p00 = (cx - hx, cy - hy, z0)
        p10 = (cx + hx, cy - hy, z0)
        p11 = (cx + hx, cy + hy, z0)
        p01 = (cx - hx, cy + hy, z0)

        if hx >= hy:
            rx = max(0.25, hx - hy * 0.85)
            r0 = (cx - rx, cy, zt)
            r1 = (cx + rx, cy, zt)
            self.add_quad(p00, p10, r1, r0, mat_roof, color=color)
            self.add_quad(p11, p01, r0, r1, mat_roof, color=color)
            self.add_tri(p01, p00, r0, mat_roof, color=color)
            self.add_tri(p10, p11, r1, mat_roof, color=color)
            # Ridge cap
            self.add_box((cx, cy, zt + 0.03), (rx * 2.0 + 0.12, 0.18, 0.09), mat_roof, color=color)
        else:
            ry = max(0.25, hy - hx * 0.85)
            r0 = (cx, cy - ry, zt)
            r1 = (cx, cy + ry, zt)
            self.add_quad(p01, p00, r0, r1, mat_roof, color=color)
            self.add_quad(p10, p11, r1, r0, mat_roof, color=color)
            self.add_tri(p00, p10, r0, mat_roof, color=color)
            self.add_tri(p11, p01, r1, mat_roof, color=color)
            self.add_box((cx, cy, zt + 0.03), (0.18, ry * 2.0 + 0.12, 0.09), mat_roof, color=color)

    def add_gable_roof(self, base_center, size_xy, roof_height, overhang=0.55,
                       ridge_along_x=True, mat_roof=MAT_ROOF_METAL,
                       mat_gable=MAT_FACADE_WALL, mat_fascia=MAT_WOOD,
                       color=(1.0, 1.0, 1.0, 1.0)):
        """Adds a 2-slope pitched gable roof with overhang, gable triangles, fascia & ridge cap."""
        cx, cy, cz = base_center
        wx, wy = size_xy
        hx = wx * 0.5 + (overhang if ridge_along_x else 0.0)
        hy = wy * 0.5 + (0.0 if ridge_along_x else overhang)
        eave_x = wx * 0.5 + overhang
        eave_y = wy * 0.5 + overhang
        z0 = cz
        zt = cz + roof_height

        if ridge_along_x:
            # Gable walls at +- wx*0.5
            for s in (-1.0, 1.0):
                gx = cx + s * (wx * 0.5)
                if s > 0:
                    self.add_tri((gx, cy - wy * 0.5, z0), (gx, cy + wy * 0.5, z0), (gx, cy, zt), mat_gable)
                else:
                    self.add_tri((gx, cy + wy * 0.5, z0), (gx, cy - wy * 0.5, z0), (gx, cy, zt), mat_gable)
            # Roof slopes extending to eave_x, eave_y
            self.add_quad((cx - eave_x, cy - eave_y, z0 - 0.08), (cx + eave_x, cy - eave_y, z0 - 0.08),
                          (cx + eave_x, cy, zt + 0.04), (cx - eave_x, cy, zt + 0.04), mat_roof, color=color)
            self.add_quad((cx + eave_x, cy + eave_y, z0 - 0.08), (cx - eave_x, cy + eave_y, z0 - 0.08),
                          (cx - eave_x, cy, zt + 0.04), (cx + eave_x, cy, zt + 0.04), mat_roof, color=color)
            self.add_box((cx, cy, zt + 0.06), (eave_x * 2.0 + 0.08, 0.20, 0.09), mat_roof, color=color)
            # Fascia beams along eaves
            for s in (-1.0, 1.0):
                self.add_box((cx, cy + s * eave_y, z0 - 0.08), (eave_x * 2.0, 0.07, 0.14), mat_fascia)
        else:
            for s in (-1.0, 1.0):
                gy = cy + s * (wy * 0.5)
                if s > 0:
                    self.add_tri((cx + wx * 0.5, gy, z0), (cx - wx * 0.5, gy, z0), (cx, gy, zt), mat_gable)
                else:
                    self.add_tri((cx - wx * 0.5, gy, z0), (cx + wx * 0.5, gy, z0), (cx, gy, zt), mat_gable)
            self.add_quad((cx - eave_x, cy + eave_y, z0 - 0.08), (cx - eave_x, cy - eave_y, z0 - 0.08),
                          (cx, cy - eave_y, zt + 0.04), (cx, cy + eave_y, zt + 0.04), mat_roof, color=color)
            self.add_quad((cx + eave_x, cy - eave_y, z0 - 0.08), (cx + eave_x, cy + eave_y, z0 - 0.08),
                          (cx, cy + eave_y, zt + 0.04), (cx, cy - eave_y, zt + 0.04), mat_roof, color=color)
            self.add_box((cx, cy, zt + 0.06), (0.20, eave_y * 2.0 + 0.08, 0.09), mat_roof, color=color)
            for s in (-1.0, 1.0):
                self.add_box((cx + s * eave_x, cy, z0 - 0.08), (0.07, eave_y * 2.0, 0.14), mat_fascia)

    def add_balcony(self, center, width, depth, mat_slab=MAT_FACADE_TRIM, mat_rail=MAT_DARK_METAL):
        """Adds a cantilevered balcony slab extending along -Y (street side) with posts and horizontal/vertical railings."""
        cx, cy, cz = center
        slab_h = 0.14
        # Slab
        self.add_box((cx, cy - depth * 0.5, cz - slab_h * 0.5), (width, depth, slab_h), mat_slab)
        # Side & front rails
        rail_h = 0.98
        top_z = cz + rail_h
        # Front top rail
        self.add_box((cx, cy - depth + 0.04, top_z), (width, 0.05, 0.05), mat_rail)
        self.add_box((cx, cy - depth + 0.04, cz + 0.12), (width, 0.04, 0.04), mat_rail)
        # Side rails
        for sx in (-1.0, 1.0):
            px = cx + sx * (width * 0.5 - 0.03)
            self.add_box((px, cy - depth * 0.5, top_z), (0.05, depth, 0.05), mat_rail)
            self.add_box((px, cy - depth * 0.5, cz + 0.12), (0.04, depth, 0.04), mat_rail)
        # Vertical balusters
        posts = max(4, int(width / 0.45))
        for i in range(posts + 1):
            px = cx - (width * 0.5 - 0.04) + (width - 0.08) * (i / float(posts))
            self.add_box((px, cy - depth + 0.04, cz + rail_h * 0.5), (0.035, 0.035, rail_h), mat_rail)
        for j in range(1, 3):
            py = cy - depth * (j / 3.0)
            for sx in (-1.0, 1.0):
                self.add_box((cx + sx * (width * 0.5 - 0.03), py, cz + rail_h * 0.5), (0.035, 0.035, rail_h), mat_rail)

    def add_polytank_stand(self, base_center):
        """Adds an iconic West African black polyethylene water reservoir on a 4-leg steel stand."""
        cx, cy, cz = base_center
        stand_h = 0.65
        leg_off = 0.40
        for sx in (-1.0, 1.0):
            for sy in (-1.0, 1.0):
                self.add_box_base((cx + sx * leg_off, cy + sy * leg_off, cz), (0.06, 0.06, stand_h), MAT_GALV_STEEL)
        # Platform frame
        self.add_box((cx, cy, cz + stand_h), (0.95, 0.95, 0.06), MAT_GALV_STEEL)
        # Ribbed black tank
        tank_z = cz + stand_h + 0.03
        self.add_cylinder((cx, cy, tank_z), 0.52, 0.52, 1.05, 10, MAT_DARK_METAL, color=(0.06, 0.06, 0.07, 1.0))
        self.add_cylinder((cx, cy, tank_z + 1.05), 0.52, 0.44, 0.14, 10, MAT_DARK_METAL, color=(0.06, 0.06, 0.07, 1.0))
        # Lid
        self.add_cylinder((cx, cy, tank_z + 1.19), 0.22, 0.22, 0.08, 8, MAT_DARK_METAL, color=(0.08, 0.08, 0.09, 1.0))

    def add_ac_unit(self, wall_center, normal_dir):
        """Adds an exterior split-system AC condenser box with fan grille and wall brackets."""
        n = Vector(normal_dir).normalized()
        up = Vector((0.0, 0.0, 1.0))
        right = n.cross(up).normalized()
        c = Vector(wall_center) + n * 0.18
        self._add_oriented_box(c, right, n, up, (0.78, 0.30, 0.52), MAT_FACADE_TRIM, color=(0.86, 0.86, 0.84, 1.0))
        # Dark fan grille circle / square inset
        self._add_oriented_box(c - right * 0.14 + n * 0.155, right, n, up, (0.38, 0.02, 0.38), MAT_DARK_METAL)
        # Wall brackets
        for s in (-1.0, 1.0):
            self._add_oriented_box(c + right * s * 0.26 - up * 0.29 - n * 0.02, right, n, up, (0.04, 0.34, 0.05), MAT_GALV_STEEL)

    def build_object(self, collection, location=(0.0, 0.0, 0.0)):
        """Finalizes the bmesh into a Blender Object, attaches materials, and links to collection."""
        closed_faces = [f for f in self.bm.faces if all(e.is_manifold for e in f.edges)]
        if closed_faces:
            bmesh.ops.recalc_face_normals(self.bm, faces=closed_faces)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        for mat_name in self.slot_names:
            mesh.materials.append(self.materials[mat_name])

        mesh.uv_layers.active = mesh.uv_layers["UVMap"]
        if "Col" in mesh.color_attributes:
            mesh.color_attributes.active_color = mesh.color_attributes["Col"]

        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = location
        collection.objects.link(obj)
        return obj, len(mesh.polygons)


def export_fbx(obj, out_dir):
    """Exports a single object to Unity-ready FBX with Y-up / -Z-forward baked transforms."""
    os.makedirs(out_dir, exist_ok=True)
    filepath = os.path.join(out_dir, f"{obj.name}.fbx")
    saved_loc = obj.location.copy()
    obj.location = (0.0, 0.0, 0.0)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        use_selection=True,
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z",
        axis_up="Y",
        use_space_transform=True,
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        colors_type="SRGB",
        prioritize_active_color=True,
        use_mesh_modifiers=True,
        embed_textures=False,
    )
    obj.location = saved_loc
    return filepath
