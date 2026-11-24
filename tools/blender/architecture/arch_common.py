"""Shared architectural bmesh, UV, material, collider-manifest, and FBX export helpers for Ivory State."""

import json
import math
import os
import bpy
import bmesh
from mathutils import Matrix, Vector


# Canonical material slot definitions mapped in Unity by ArchitectureSetup.
MATERIAL_SPECS = {
    "Mat_Arch_MurExt_Blanc": ((0.97, 0.96, 0.93, 1.0), 0.0, 0.85),
    "Mat_Arch_MurExt_Anthracite": ((0.20, 0.22, 0.25, 1.0), 0.0, 0.78),
    "Mat_Arch_Parement_Pierre": ((0.85, 0.79, 0.69, 1.0), 0.0, 0.78),
    "Mat_Arch_Beton_Brut": ((0.72, 0.71, 0.69, 1.0), 0.0, 0.80),
    "Mat_Arch_Paves_Cour": ((0.82, 0.80, 0.77, 1.0), 0.0, 0.82),
    "Mat_Arch_Gazon": ((0.30, 0.56, 0.24, 1.0), 0.0, 0.90),
    "Mat_Arch_Toiture_Terrasse": ((0.52, 0.50, 0.48, 1.0), 0.0, 0.84),
    "Mat_Arch_Metal_Anthracite": ((0.16, 0.18, 0.21, 1.0), 0.78, 0.45),
    "Mat_Arch_Metal_Inox": ((0.82, 0.84, 0.86, 1.0), 0.88, 0.22),
    "Mat_Arch_Bois_Exterieur": ((0.68, 0.49, 0.31, 1.0), 0.0, 0.60),
    "Mat_Arch_Vitre_Ext": ((0.36, 0.54, 0.64, 0.28), 0.12, 0.08),
    "Mat_Arch_Vitre_GratteCiel": ((0.22, 0.40, 0.54, 1.0), 0.35, 0.10),
    "Mat_Arch_Fenetre_Nuit": ((0.96, 0.84, 0.60, 1.0), 0.0, 0.25),
    "Mat_Arch_Lumiere_Nuit": ((1.00, 0.86, 0.62, 1.0), 0.0, 0.30),
    "Mat_Arch_Lumiere_Int_Nuit": ((1.00, 0.91, 0.74, 1.0), 0.0, 0.30),
    "Mat_Arch_MurInt_Creme": ((0.98, 0.97, 0.94, 1.0), 0.0, 0.88),
    "Mat_Arch_MurInt_Accent": ((0.83, 0.79, 0.73, 1.0), 0.0, 0.84),
    "Mat_Arch_Sol_Marbre": ((0.94, 0.92, 0.88, 1.0), 0.0, 0.32),
    "Mat_Arch_Sol_Carrelage": ((0.90, 0.88, 0.85, 1.0), 0.0, 0.52),
    "Mat_Arch_Sol_Terrasse": ((0.86, 0.78, 0.68, 1.0), 0.0, 0.70),
    "Mat_Arch_Sol_Parquet": ((0.74, 0.56, 0.39, 1.0), 0.0, 0.50),
    "Mat_Arch_Bois_Interieur": ((0.62, 0.43, 0.27, 1.0), 0.0, 0.58),
    "Mat_Arch_Tissu_Mobilier": ((0.38, 0.44, 0.50, 1.0), 0.0, 0.88),
    "Mat_Arch_Plan_Travail": ((0.22, 0.24, 0.27, 1.0), 0.0, 0.28),
    "Mat_Arch_Ceramique_SDB": ((0.97, 0.97, 0.98, 1.0), 0.0, 0.20),
    "Mat_Arch_Cuve_Bleue": ((0.14, 0.42, 0.76, 1.0), 0.0, 0.42),
    "Mat_Arch_Eau_Piscine": ((0.18, 0.72, 0.86, 0.72), 0.05, 0.06),
    "Mat_Arch_Piscine_Fond": ((0.14, 0.58, 0.74, 1.0), 0.0, 0.50),
    "Mat_Arch_Vitrail_Bleu": ((0.22, 0.46, 0.82, 0.72), 0.05, 0.12),
    "Mat_Arch_Vitrail_Or": ((0.92, 0.72, 0.24, 0.72), 0.05, 0.12),
    "Mat_Arch_Pelouse_Stade": ((0.22, 0.58, 0.22, 1.0), 0.0, 0.88),
    "Mat_Arch_Piste_Athletisme": ((0.72, 0.25, 0.19, 1.0), 0.0, 0.75),
    "Mat_Arch_Siege_Orange": ((0.92, 0.42, 0.08, 1.0), 0.0, 0.65),
    "Mat_Arch_Siege_Vert": ((0.10, 0.54, 0.28, 1.0), 0.0, 0.65),
    "Mat_Arch_Siege_Blanc": ((0.94, 0.94, 0.94, 1.0), 0.0, 0.65),
}


def reset_scene():
    """Start from a clean metric scene (1 Blender unit = 1 meter)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene


def get_material(name):
    """Create or retrieve a named Blender material matching MATERIAL_SPECS."""
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    rgba, metallic, roughness = MATERIAL_SPECS.get(
        name, ((0.75, 0.75, 0.75, 1.0), 0.0, 0.7)
    )
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if rgba[3] < 0.99 and "Alpha" in bsdf.inputs:
            bsdf.inputs["Alpha"].default_value = rgba[3]
        if "Lumiere" in name and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = rgba
            bsdf.inputs["Emission Strength"].default_value = 1.5
    mat.diffuse_color = rgba
    return mat


def _ensure_collection(name, parent=None):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        target = parent if parent is not None else bpy.context.scene.collection
        target.children.link(col)
    return col


def blender_euler_to_unity(rot_euler):
    """Convert Blender Euler XYZ (radians) to Unity Euler degrees (X_u=-X_b, Y_u=Z_b, Z_u=-Y_b)."""
    return [
        round(math.degrees(rot_euler[0]), 3),
        round(math.degrees(-rot_euler[2]), 3),
        round(math.degrees(rot_euler[1]), 3),
    ]


def blender_box_to_unity(cx, cy, cz, sx, sy, sz):
    """Convert a Blender box center/size into Unity space (X_u=-X_b, Y_u=Z_b, Z_u=-Y_b)."""
    return {
        "center": [-round(cx, 4), round(cz, 4), round(-cy, 4)],
        "size": [round(sx, 4), round(sz, 4), round(sy, 4)],
    }


class ArchMeshBuilder:
    """Builds a single clean mesh with metric box-projected UVs and optional collider boxes."""

    def __init__(self, name, collection_name="Architecture", origin=(0.0, 0.0, 0.0)):
        self.name = name
        self.collection_name = collection_name
        self.origin = Vector(origin)
        self.bm = bmesh.new()
        self.uv_layer = self.bm.loops.layers.uv.new("UVMap")
        self.mat_indices = {}
        self.materials = []
        self.colliders = []

    def _mat_index(self, mat_name):
        idx = self.mat_indices.get(mat_name)
        if idx is None:
            idx = len(self.materials)
            self.mat_indices[mat_name] = idx
            self.materials.append(get_material(mat_name))
        return idx

    def _assign_box_uv_and_mat(self, faces, mat_idx, uv_scale=1.0):
        for face in faces:
            face.material_index = mat_idx
            face.smooth = False
            n = face.normal
            ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
            for loop in face.loops:
                p = loop.vert.co + self.origin
                if az >= ax and az >= ay:
                    u, v = p.x, p.y
                elif ax >= ay:
                    u, v = p.y, p.z
                else:
                    u, v = p.x, p.z
                loop[self.uv_layer].uv = (u * uv_scale, v * uv_scale)

    def add_collider_bounds(self, x0, x1, y0, y1, z0, z1):
        """Record an invisible Unity BoxCollider matching world bounds [x0..x1] x [y0..y1] x [z0..z1]."""
        if x1 <= x0 + 1e-5 or y1 <= y0 + 1e-5 or z1 <= z0 + 1e-5:
            return
        local_cx = 0.5 * (x0 + x1) - self.origin.x
        local_cy = 0.5 * (y0 + y1) - self.origin.y
        local_cz = 0.5 * (z0 + z1) - self.origin.z
        self.colliders.append(
            blender_box_to_unity(local_cx, local_cy, local_cz, x1 - x0, y1 - y0, z1 - z0)
        )

    def add_collider_center(self, cx, cy, cz, sx, sy, sz, rot_euler=(0.0, 0.0, 0.0)):
        """Record an invisible oriented Unity BoxCollider."""
        local_cx = cx - self.origin.x
        local_cy = cy - self.origin.y
        local_cz = cz - self.origin.z
        c_entry = blender_box_to_unity(local_cx, local_cy, local_cz, sx, sy, sz)
        if any(abs(a) > 1e-6 for a in rot_euler):
            c_entry["euler"] = blender_euler_to_unity(rot_euler)
        self.colliders.append(c_entry)

    def add_box_bounds(
        self,
        x0,
        x1,
        y0,
        y1,
        z0,
        z1,
        mat_name,
        uv_scale=1.0,
        collider=False,
        collider_pad=0.0,
    ):
        """Add an axis-aligned box spanning [x0..x1] x [y0..y1] x [z0..z1] in world coordinates."""
        if x1 <= x0 + 1e-5 or y1 <= y0 + 1e-5 or z1 <= z0 + 1e-5:
            return
        cx = 0.5 * (x0 + x1)
        cy = 0.5 * (y0 + y1)
        cz = 0.5 * (z0 + z1)
        sx = x1 - x0
        sy = y1 - y0
        sz = z1 - z0
        self.add_box_center(
            cx,
            cy,
            cz,
            sx,
            sy,
            sz,
            mat_name,
            uv_scale=uv_scale,
            collider=collider,
            collider_pad=collider_pad,
        )

    def add_box_center(
        self,
        cx,
        cy,
        cz,
        sx,
        sy,
        sz,
        mat_name,
        rot_euler=(0.0, 0.0, 0.0),
        uv_scale=1.0,
        collider=False,
        collider_pad=0.0,
    ):
        """Add an oriented box centered at world (cx, cy, cz)."""
        local_center = Vector((cx, cy, cz)) - self.origin
        rot = Matrix.Identity(4)
        if any(abs(a) > 1e-6 for a in rot_euler):
            rot = (
                Matrix.Rotation(rot_euler[2], 4, "Z")
                @ Matrix.Rotation(rot_euler[1], 4, "Y")
                @ Matrix.Rotation(rot_euler[0], 4, "X")
            )
        scale_mat = Matrix.Diagonal((sx, sy, sz, 1.0))
        xform = Matrix.Translation(local_center) @ rot @ scale_mat
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xform)
        verts = ret["verts"]
        faces = list({f for v in verts for f in v.link_faces})
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._assign_box_uv_and_mat(faces, self._mat_index(mat_name), uv_scale)

        if collider:
            c_entry = blender_box_to_unity(
                local_center.x,
                local_center.y,
                local_center.z,
                sx + collider_pad * 2.0,
                sy + collider_pad * 2.0,
                sz + collider_pad * 2.0,
            )
            if any(abs(a) > 1e-6 for a in rot_euler):
                c_entry["euler"] = blender_euler_to_unity(rot_euler)
            self.colliders.append(c_entry)

    def add_cylinder(
        self,
        cx,
        cy,
        z0,
        z1,
        radius,
        segments,
        mat_name,
        rot_euler=(0.0, 0.0, 0.0),
        uv_scale=1.0,
    ):
        """Add a capped cylinder (used for columns, trees, tank, lamps)."""
        height = z1 - z0
        if height <= 1e-5 or radius <= 1e-5:
            return
        local_center = Vector((cx, cy, 0.5 * (z0 + z1))) - self.origin
        rot = Matrix.Identity(4)
        if any(abs(a) > 1e-6 for a in rot_euler):
            rot = (
                Matrix.Rotation(rot_euler[2], 4, "Z")
                @ Matrix.Rotation(rot_euler[1], 4, "Y")
                @ Matrix.Rotation(rot_euler[0], 4, "X")
            )
        xform = Matrix.Translation(local_center) @ rot
        ret = bmesh.ops.create_cone(
            self.bm,
            cap_ends=True,
            cap_tris=False,
            segments=segments,
            radius1=radius,
            radius2=radius,
            depth=height,
            matrix=xform,
        )
        verts = ret["verts"]
        faces = list({f for v in verts for f in v.link_faces})
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._assign_box_uv_and_mat(faces, self._mat_index(mat_name), uv_scale)

    def add_cone(self, cx, cy, z0, z1, r_bottom, r_top, segments, mat_name):
        """Add a frustum/cone for palm trunks, foliage crowns, or lamps."""
        height = z1 - z0
        if height <= 1e-5:
            return
        local_center = Vector((cx, cy, 0.5 * (z0 + z1))) - self.origin
        xform = Matrix.Translation(local_center)
        ret = bmesh.ops.create_cone(
            self.bm,
            cap_ends=True,
            cap_tris=False,
            segments=segments,
            radius1=r_bottom,
            radius2=r_top,
            depth=height,
            matrix=xform,
        )
        verts = ret["verts"]
        faces = list({f for v in verts for f in v.link_faces})
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._assign_box_uv_and_mat(faces, self._mat_index(mat_name), 1.0)

    def add_loft_section(
        self,
        pts_bottom,
        pts_top,
        mat_name,
        uv_scale=1.0,
        cap_bottom=True,
        cap_top=True,
    ):
        """Loft a closed polygon ring between 3D loops `pts_bottom` and `pts_top`."""
        n = len(pts_bottom)
        if n < 3 or len(pts_top) != n:
            return
        vb = [self.bm.verts.new(Vector(p) - self.origin) for p in pts_bottom]
        vt = [self.bm.verts.new(Vector(p) - self.origin) for p in pts_top]
        faces = []
        for i in range(n):
            j = (i + 1) % n
            try:
                faces.append(self.bm.faces.new((vb[i], vb[j], vt[j], vt[i])))
            except ValueError:
                pass
        if cap_bottom:
            try:
                faces.append(self.bm.faces.new(list(reversed(vb))))
            except ValueError:
                pass
        if cap_top:
            try:
                faces.append(self.bm.faces.new(vt))
            except ValueError:
                pass
        if faces:
            bmesh.ops.recalc_face_normals(self.bm, faces=faces)
            self._assign_box_uv_and_mat(faces, self._mat_index(mat_name), uv_scale)

    def add_beam(self, p0, p1, width, thickness, mat_name, uv_scale=1.0, collider=False):
        """Add a rectangular structural beam, strut, or stay cable from 3D point p0 to p1."""
        v0 = Vector(p0)
        v1 = Vector(p1)
        delta = v1 - v0
        length = delta.length
        if length <= 1e-5:
            return
        mid = 0.5 * (v0 + v1)
        direction = delta / length
        up_ref = Vector((0.0, 0.0, 1.0)) if abs(direction.z) < 0.95 else Vector((1.0, 0.0, 0.0))
        right = direction.cross(up_ref).normalized()
        up_perp = right.cross(direction).normalized()
        # Local X = right (width), Y = direction (length), Z = up_perp (thickness)
        rot_mat = Matrix((
            (right.x, direction.x, up_perp.x, 0.0),
            (right.y, direction.y, up_perp.y, 0.0),
            (right.z, direction.z, up_perp.z, 0.0),
            (0.0, 0.0, 0.0, 1.0),
        ))
        rot_euler = rot_mat.to_euler("XYZ")
        self.add_box_center(
            mid.x,
            mid.y,
            mid.z,
            width,
            length,
            thickness,
            mat_name,
            rot_euler=(rot_euler.x, rot_euler.y, rot_euler.z),
            uv_scale=uv_scale,
            collider=collider,
        )

    def add_wall_with_openings(
        self,
        axis,
        fixed_center,
        thickness,
        span_start,
        span_end,
        z_bottom,
        z_top,
        mat_name,
        openings=None,
        frame_mat=None,
        glass_mat=None,
        collider=True,
    ):
        """
        Build a clean perforated wall along X (axis='X') or Y (axis='Y') without booleans.

        Each item in `openings` is a dict:
          {
            's0': float, 's1': float, 'z0': float, 'z1': float,
            'kind': 'window'|'door'|'open',
            'frame_depth': float (optional),
            'has_glass': bool (optional)
          }
        """
        openings = sorted(openings or [], key=lambda o: o["s0"])
        half_t = thickness * 0.5
        f0 = fixed_center - half_t
        f1 = fixed_center + half_t

        def emit_panel(a0, a1, zb, zt, panel_mat, emit_col):
            if a1 <= a0 + 1e-4 or zt <= zb + 1e-4:
                return
            if axis == "X":
                self.add_box_bounds(
                    a0, a1, f0, f1, zb, zt, panel_mat, collider=emit_col
                )
            else:
                self.add_box_bounds(
                    f0, f1, a0, a1, zb, zt, panel_mat, collider=emit_col
                )

        cursor = span_start
        for op in openings:
            s0 = max(span_start, op["s0"])
            s1 = min(span_end, op["s1"])
            oz0 = max(z_bottom, op["z0"])
            oz1 = min(z_top, op["z1"])

            # Solid wall pier before this opening
            if s0 > cursor + 1e-4:
                emit_panel(cursor, s0, z_bottom, z_top, mat_name, collider)

            # Sill below opening
            if oz0 > z_bottom + 1e-4:
                emit_panel(s0, s1, z_bottom, oz0, mat_name, collider)

            # Lintel above opening
            if z_top > oz1 + 1e-4:
                emit_panel(s0, s1, oz1, z_top, mat_name, collider)

            # Window/door frame & glazing (no collider on glass so camera/lights behave cleanly)
            kind = op.get("kind", "open")
            if kind in ("window", "door") and frame_mat:
                f_thick = min(thickness + 0.03, op.get("frame_depth", 0.08))
                fw = 0.05
                ff0 = fixed_center - f_thick * 0.5
                ff1 = fixed_center + f_thick * 0.5

                def emit_frame(u0, u1, v0, v1, mname):
                    if axis == "X":
                        self.add_box_bounds(u0, u1, ff0, ff1, v0, v1, mname)
                    else:
                        self.add_box_bounds(ff0, ff1, u0, u1, v0, v1, mname)

                # Left, right, top jambs
                emit_frame(s0, min(s1, s0 + fw), oz0, oz1, frame_mat)
                emit_frame(max(s0, s1 - fw), s1, oz0, oz1, frame_mat)
                emit_frame(s0, s1, max(oz0, oz1 - fw), oz1, frame_mat)
                if kind == "window":
                    emit_frame(s0, s1, oz0, min(oz1, oz0 + fw), frame_mat)
                    # Center mullion for wide windows
                    if s1 - s0 >= 1.4:
                        mid = 0.5 * (s0 + s1)
                        emit_frame(mid - 0.025, mid + 0.025, oz0 + fw, oz1 - fw, frame_mat)
                    if op.get("has_glass", True) and glass_mat:
                        gf0 = fixed_center - 0.012
                        gf1 = fixed_center + 0.012
                        if axis == "X":
                            self.add_box_bounds(
                                s0 + fw, s1 - fw, gf0, gf1, oz0 + fw, oz1 - fw, glass_mat
                            )
                        else:
                            self.add_box_bounds(
                                gf0, gf1, s0 + fw, s1 - fw, oz0 + fw, oz1 - fw, glass_mat
                            )

            cursor = max(cursor, s1)

        if span_end > cursor + 1e-4:
            emit_panel(cursor, span_end, z_bottom, z_top, mat_name, collider)

    def add_stair_flight(
        self,
        axis,
        lateral_center,
        width,
        start_pos,
        end_pos,
        z_start,
        z_end,
        num_steps,
        tread_mat,
        riser_mat,
    ):
        """
        Add a solid walkable stair flight with visual treads/stringer + ONE smooth inclined ramp collider.
        axis='Y' means flight runs along Y (from start_pos to end_pos); lateral is X.
        """
        if num_steps < 2:
            return
        dx = end_pos - start_pos
        dz = z_end - z_start
        step_run = abs(dx) / num_steps
        step_rise = dz / num_steps
        direction = 1.0 if dx >= 0 else -1.0
        half_w = width * 0.5

        for i in range(num_steps):
            p0 = start_pos + direction * i * step_run
            p1 = start_pos + direction * (i + 1) * step_run
            a0, a1 = min(p0, p1), max(p0, p1)
            zt = z_start + (i + 1) * step_rise
            zb = max(z_start, z_start + i * step_rise - 0.14)
            if axis == "Y":
                self.add_box_bounds(
                    lateral_center - half_w,
                    lateral_center + half_w,
                    a0,
                    a1,
                    zb,
                    zt - 0.03,
                    riser_mat,
                )
                self.add_box_bounds(
                    lateral_center - half_w,
                    lateral_center + half_w,
                    a0,
                    a1,
                    zt - 0.03,
                    zt,
                    tread_mat,
                )
            else:
                self.add_box_bounds(
                    a0,
                    a1,
                    lateral_center - half_w,
                    lateral_center + half_w,
                    zb,
                    zt - 0.03,
                    riser_mat,
                )
                self.add_box_bounds(
                    a0,
                    a1,
                    lateral_center - half_w,
                    lateral_center + half_w,
                    zt - 0.03,
                    zt,
                    tread_mat,
                )

        # Single smooth inclined collider matching the top noses of steps (collider-only, no extra mesh)
        horiz = abs(dx)
        slope_len = math.hypot(horiz, dz)
        pitch = math.atan2(dz, horiz)
        mid_along = 0.5 * (start_pos + end_pos)
        mid_z = 0.5 * (z_start + z_end)
        ramp_thick = 0.18
        if axis == "Y":
            angle_x = pitch if direction > 0 else -pitch
            ny_off = (ramp_thick * 0.5) * math.sin(angle_x)
            nz_off = -(ramp_thick * 0.5) * math.cos(angle_x)
            self.add_collider_center(
                lateral_center,
                mid_along + ny_off,
                mid_z + nz_off,
                width,
                slope_len,
                ramp_thick,
                rot_euler=(angle_x, 0.0, 0.0),
            )
            # Visible central steel spine stringers under steps
            for sx_off in (-width * 0.30, width * 0.30):
                self.add_box_center(
                    lateral_center + sx_off,
                    mid_along + ny_off * 1.35,
                    mid_z + nz_off * 1.35 - 0.04,
                    0.10,
                    slope_len - 0.12,
                    0.14,
                    "Mat_Arch_Metal_Anthracite",
                    rot_euler=(angle_x, 0.0, 0.0),
                )
        else:
            angle_y = -pitch if direction > 0 else pitch
            nx_off = -(ramp_thick * 0.5) * math.sin(angle_y)
            nz_off = -(ramp_thick * 0.5) * math.cos(angle_y)
            self.add_collider_center(
                mid_along + nx_off,
                lateral_center,
                mid_z + nz_off,
                slope_len,
                width,
                ramp_thick,
                rot_euler=(0.0, angle_y, 0.0),
            )
            for sy_off in (-width * 0.30, width * 0.30):
                self.add_box_center(
                    mid_along + nx_off * 1.35,
                    lateral_center + sy_off,
                    mid_z + nz_off * 1.35 - 0.04,
                    slope_len - 0.12,
                    0.10,
                    0.14,
                    "Mat_Arch_Metal_Anthracite",
                    rot_euler=(0.0, angle_y, 0.0),
                )

    def add_glass_railing(
        self,
        axis,
        fixed_center,
        span_start,
        span_end,
        z_base,
        height=1.02,
        post_mat="Mat_Arch_Metal_Inox",
        glass_mat="Mat_Arch_Vitre_Ext",
        collider=True,
    ):
        """Add tempered glass + stainless handrail balcony/mezzanine railing."""
        length = span_end - span_start
        if length <= 0.1:
            return
        z_top = z_base + height
        post_count = max(2, int(math.ceil(length / 1.45)) + 1)
        for i in range(post_count):
            t = i / (post_count - 1)
            pos = span_start + t * length
            if axis == "X":
                self.add_box_bounds(
                    pos - 0.025,
                    pos + 0.025,
                    fixed_center - 0.025,
                    fixed_center + 0.025,
                    z_base,
                    z_top,
                    post_mat,
                )
            else:
                self.add_box_bounds(
                    fixed_center - 0.025,
                    fixed_center + 0.025,
                    pos - 0.025,
                    pos + 0.025,
                    z_base,
                    z_top,
                    post_mat,
                )

        # Top handrail + glass panel
        if axis == "X":
            self.add_box_bounds(
                span_start,
                span_end,
                fixed_center - 0.03,
                fixed_center + 0.03,
                z_top - 0.04,
                z_top + 0.01,
                post_mat,
            )
            self.add_box_bounds(
                span_start + 0.03,
                span_end - 0.03,
                fixed_center - 0.01,
                fixed_center + 0.01,
                z_base + 0.06,
                z_top - 0.05,
                glass_mat,
            )
            if collider:
                self.add_collider_bounds(
                    span_start, span_end, fixed_center - 0.04, fixed_center + 0.04, z_base, z_top
                )
        else:
            self.add_box_bounds(
                fixed_center - 0.03,
                fixed_center + 0.03,
                span_start,
                span_end,
                z_top - 0.04,
                z_top + 0.01,
                post_mat,
            )
            self.add_box_bounds(
                fixed_center - 0.01,
                fixed_center + 0.01,
                span_start + 0.03,
                span_end - 0.03,
                z_base + 0.06,
                z_top - 0.05,
                glass_mat,
            )
            if collider:
                self.add_collider_bounds(
                    fixed_center - 0.04, fixed_center + 0.04, span_start, span_end, z_base, z_top
                )

    def add_ac_split_outdoor(self, cx, cy, cz, facing_axis="+X"):
        """Add realistic outdoor split air-conditioner unit mounted on facade."""
        w, d, h = 0.86, 0.32, 0.58
        if facing_axis in ("+X", "-X"):
            sx, sy = d, w
            fan_x = cx + (d * 0.51 if facing_axis == "+X" else -d * 0.51)
            self.add_box_center(cx, cy, cz, sx, sy, h, "Mat_Arch_Beton_Brut")
            self.add_box_center(
                fan_x, cy - 0.14, cz, 0.02, 0.42, 0.42, "Mat_Arch_Metal_Anthracite"
            )
            # Brackets tucked under chassis
            self.add_box_center(cx, cy - 0.28, cz - h * 0.5 - 0.03, sx * 0.92, 0.04, 0.05, "Mat_Arch_Metal_Anthracite")
            self.add_box_center(cx, cy + 0.28, cz - h * 0.5 - 0.03, sx * 0.92, 0.04, 0.05, "Mat_Arch_Metal_Anthracite")
        else:
            sx, sy = w, d
            fan_y = cy + (d * 0.51 if facing_axis == "+Y" else -d * 0.51)
            self.add_box_center(cx, cy, cz, sx, sy, h, "Mat_Arch_Beton_Brut")
            self.add_box_center(
                cx - 0.14, fan_y, cz, 0.42, 0.02, 0.42, "Mat_Arch_Metal_Anthracite"
            )

    def build_object(self, parent=None, default_rot_deg=(0.0, 0.0, 0.0)):
        """Finalize bmesh into a clean Blender Object with transforms applied."""
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for mat in self.materials:
            mesh.materials.append(mat)
        mesh.validate(clean_customdata=False)

        obj = bpy.data.objects.new(self.name, mesh)
        col = _ensure_collection(self.collection_name)
        col.objects.link(obj)
        obj.location = self.origin
        obj.rotation_euler = (
            math.radians(default_rot_deg[0]),
            math.radians(default_rot_deg[1]),
            math.radians(default_rot_deg[2]),
        )
        obj.scale = (1.0, 1.0, 1.0)
        if parent is not None:
            obj.parent = parent
        return obj


def export_objects_fbx(objects, filepath):
    """Export selected object hierarchy to Unity-compatible FBX (1 unit = 1 meter, Scale = 1)."""
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    if objects:
        bpy.context.view_layer.objects.active = objects[0]

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
        use_mesh_modifiers=True,
        embed_textures=False,
        add_leaf_bones=False,
    )


def save_collider_manifest(filepath, builders_meta):
    """Write Unity-space collider + interactive door/gate manifest JSON alongside scripts."""
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(builders_meta, f, indent=2)


def render_preview_views(output_dir, prefix, cam_targets):
    """Render quick diagnostic views using EEVEE/Workbench for visual inspection."""
    os.makedirs(output_dir, exist_ok=True)
    scene = bpy.context.scene
    # Use Workbench or EEVEE Next depending on Blender 5.3 availability
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue

    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"

    # Ensure world sky + sun light exist
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    if bg is not None:
        bg.inputs["Color"].default_value = (0.58, 0.72, 0.88, 1.0)
        bg.inputs["Strength"].default_value = 0.85

    sun_obj = bpy.data.objects.get("PreviewSun")
    if sun_obj is None:
        sun_data = bpy.data.lights.new("PreviewSun", type="SUN")
        sun_data.energy = 3.2
        sun_obj = bpy.data.objects.new("PreviewSun", sun_data)
        scene.collection.objects.link(sun_obj)
        sun_obj.rotation_euler = (math.radians(48), math.radians(15), math.radians(-38))

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 30.0
    cam_data.clip_end = 1000.0
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    rendered = []
    for tag, loc, target in cam_targets:
        cam_obj.location = Vector(loc)
        direction = Vector(target) - Vector(loc)
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        out_path = os.path.join(output_dir, f"{prefix}_{tag}.png")
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
        rendered.append(out_path)

    # Remove temporary preview camera & sun before saving .blend or exporting
    bpy.data.objects.remove(cam_obj, do_unlink=True)
    bpy.data.cameras.remove(cam_data, do_unlink=True)
    bpy.data.objects.remove(sun_obj, do_unlink=True)
    return rendered
