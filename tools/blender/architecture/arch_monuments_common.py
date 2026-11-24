"""Shared architectural building helpers for grand institutional buildings
(Commissariat Central, Palais Presidentiel) compatible with arch_common.py
and Unity ArchitectureSetup.cs.

All dimensions are in meters (1 Blender unit = 1 meter):
- Blender -Y faces the street (Unity +Z)
- Blender +Z is up, ground at Z = 0.0 (Unity +Y)
- Blender +X maps to Unity -X
"""

import math
import os
import sys

import bpy
from mathutils import Vector

ARCH_DIR = os.path.dirname(os.path.abspath(__file__))
if ARCH_DIR not in sys.path:
    sys.path.insert(0, ARCH_DIR)

import arch_common as ac  # noqa: E402


EXTRA_MATERIAL_SPECS = {
    "Mat_Arch_Or_Ceremonie": ((0.86, 0.68, 0.22, 1.0), 0.90, 0.24),
    "Mat_Arch_Tapis_Rouge": ((0.58, 0.07, 0.09, 1.0), 0.0, 0.86),
    "Mat_Arch_Police_Bleu": ((0.07, 0.22, 0.50, 1.0), 0.0, 0.42),
    "Mat_Arch_Drapeau_Orange": ((0.95, 0.45, 0.04, 1.0), 0.0, 0.82),
    "Mat_Arch_Drapeau_Vert": ((0.04, 0.56, 0.25, 1.0), 0.0, 0.82),
    "Mat_Arch_Eau_Bassin": ((0.12, 0.46, 0.62, 0.78), 0.05, 0.08),
    "Mat_Arch_Lumiere_Ecran": ((0.22, 0.64, 0.92, 1.0), 0.05, 0.15),
    "Mat_Arch_Lumiere_Gyro_Bleu": ((0.12, 0.45, 1.00, 1.0), 0.0, 0.15),
    "Mat_Arch_Lumiere_Gyro_Rouge": ((1.00, 0.14, 0.14, 1.0), 0.0, 0.15),
    "Mat_Arch_Hopital_Teal": ((0.14, 0.56, 0.54, 1.0), 0.0, 0.45),
    "Mat_Arch_Hopital_Menthe": ((0.88, 0.95, 0.92, 1.0), 0.0, 0.78),
    "Mat_Arch_Croix_Rouge": ((0.86, 0.08, 0.08, 1.0), 0.0, 0.35),
    "Mat_Arch_Lumiere_Croix": ((0.95, 0.12, 0.12, 1.0), 0.0, 0.20),
    "Mat_Arch_Vitrail_Bleu": ((0.18, 0.42, 0.85, 0.58), 0.05, 0.10),
    "Mat_Arch_Vitrail_Or": ((0.90, 0.66, 0.16, 0.58), 0.05, 0.10),
    "Mat_Arch_Vitrail_Rubis": ((0.78, 0.14, 0.22, 0.58), 0.05, 0.10),
    "Mat_Arch_Mosquee_Bleu": ((0.08, 0.34, 0.72, 1.0), 0.05, 0.28),
    "Mat_Arch_Mosquee_Vert": ((0.09, 0.44, 0.28, 1.0), 0.0, 0.68),
    "Mat_Arch_Pierre_Ocre": ((0.82, 0.69, 0.48, 1.0), 0.0, 0.78),
    "Mat_Arch_Militaire_Kaki": ((0.27, 0.31, 0.22, 1.0), 0.12, 0.74),
    "Mat_Arch_Militaire_CamoVert": ((0.18, 0.28, 0.17, 1.0), 0.08, 0.78),
    "Mat_Arch_Militaire_Sable": ((0.76, 0.69, 0.55, 1.0), 0.0, 0.80),
    "Mat_Arch_Asphalte_Base": ((0.19, 0.20, 0.22, 1.0), 0.0, 0.88),
    "Mat_Arch_Marquage_Jaune": ((0.94, 0.76, 0.12, 1.0), 0.0, 0.55),
    "Mat_Arch_Toiture_Tole_Verte": ((0.16, 0.32, 0.22, 1.0), 0.45, 0.52),
}


def register_monument_materials():
    """Register additional institutional accent materials in arch_common."""
    for name, spec in EXTRA_MATERIAL_SPECS.items():
        if name not in ac.MATERIAL_SPECS:
            ac.MATERIAL_SPECS[name] = spec


def make_empty_root(name, collection_name="Architecture"):
    """Create an identity Empty root object in the requested collection."""
    col = ac._ensure_collection(collection_name)
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 1.0
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    col.objects.link(obj)
    return obj


def add_slab_with_cutouts(
    b,
    x0,
    x1,
    y0,
    y1,
    z0,
    z1,
    cutouts,
    mat_name,
    uv_scale=0.5,
    collider=True,
):
    """Build a horizontal slab over [x0, x1] x [y0, y1] excluding hole rectangles.

    Each cutout is (hx0, hx1, hy0, hy1). Sub-rectangles never overlap.
    """
    rects = [(x0, x1, y0, y1)]
    eps = 1e-4
    for hx0, hx1, hy0, hy1 in cutouts or []:
        next_rects = []
        for rx0, rx1, ry0, ry1 in rects:
            ix0 = max(rx0, hx0)
            ix1 = min(rx1, hx1)
            iy0 = max(ry0, hy0)
            iy1 = min(ry1, hy1)
            if ix1 <= ix0 + eps or iy1 <= iy0 + eps:
                next_rects.append((rx0, rx1, ry0, ry1))
                continue
            if iy0 > ry0 + eps:
                next_rects.append((rx0, rx1, ry0, iy0))
            if ry1 > iy1 + eps:
                next_rects.append((rx0, rx1, iy1, ry1))
            if ix0 > rx0 + eps:
                next_rects.append((rx0, ix0, iy0, iy1))
            if rx1 > ix1 + eps:
                next_rects.append((ix1, rx1, iy0, iy1))
        rects = next_rects

    for rx0, rx1, ry0, ry1 in rects:
        if rx1 - rx0 > eps and ry1 - ry0 > eps:
            b.add_box_bounds(
                rx0,
                rx1,
                ry0,
                ry1,
                z0,
                z1,
                mat_name,
                uv_scale=uv_scale,
                collider=collider,
            )


def add_door_leaf_builder(
    name,
    collection_name,
    pivot_world,
    leaf_bounds_local,
    mat_panel,
    mat_frame="Mat_Arch_Metal_Anthracite",
    mat_handle="Mat_Arch_Metal_Inox",
    glass_bounds_local=None,
    default_rot_deg=(0.0, 0.0, 0.0),
    offset_world=(0.0, 0.0, 0.0),
    is_sliding=False,
    slide_axis="X",
    open_note="",
    add_handles=True,
):
    """Create a movable door/gate ArchMeshBuilder pivoted at its hinge or rail."""
    px, py, pz = pivot_world
    ox, oy, oz = (
        px + offset_world[0],
        py + offset_world[1],
        pz + offset_world[2],
    )
    b = ac.ArchMeshBuilder(name, collection_name=collection_name, origin=(ox, oy, oz))

    x0, x1, y0, y1, z0, z1 = leaf_bounds_local
    sx, sy, sz = x1 - x0, y1 - y0, z1 - z0

    if glass_bounds_local is None:
        b.add_box_bounds(
            ox + x0, ox + x1, oy + y0, oy + y1, oz + z0, oz + z1,
            mat_panel, uv_scale=1.0, collider=True,
        )
    else:
        gx0, gx1, gy0, gy1, gz0, gz1 = glass_bounds_local
        b.add_box_bounds(
            ox + x0, ox + x1, oy + y0, oy + y1, oz + z0, oz + gz0,
            mat_panel, uv_scale=1.0, collider=False,
        )
        b.add_box_bounds(
            ox + x0, ox + x1, oy + y0, oy + y1, oz + gz1, oz + z1,
            mat_panel, uv_scale=1.0, collider=False,
        )
        b.add_box_bounds(
            ox + x0, ox + gx0, oy + y0, oy + y1, oz + gz0, oz + gz1,
            mat_frame, uv_scale=1.0, collider=False,
        )
        b.add_box_bounds(
            ox + gx1, ox + x1, oy + y0, oy + y1, oz + gz0, oz + gz1,
            mat_frame, uv_scale=1.0, collider=False,
        )
        b.add_box_bounds(
            ox + gx0, ox + gx1, oy + gy0, oy + gy1, oz + gz0, oz + gz1,
            "Mat_Arch_Vitre_Ext", uv_scale=1.0, collider=False,
        )
        b.add_collider_bounds(ox + x0, ox + x1, oy + y0, oy + y1, oz + z0, oz + z1)

    if add_handles:
        hz = oz + 1.02
        if sx >= sy:
            hx = ox + (x1 - 0.11 if abs(x0) <= abs(x1) else x0 + 0.11)
            b.add_box_center(hx, oy + y0 - 0.03, hz, 0.03, 0.04, 0.18, mat_handle)
            b.add_box_center(hx, oy + y1 + 0.03, hz, 0.03, 0.04, 0.18, mat_handle)
        else:
            hy = oy + (y1 - 0.11 if abs(y0) <= abs(y1) else y0 + 0.11)
            b.add_box_center(ox + x0 - 0.03, hy, hz, 0.04, 0.03, 0.18, mat_handle)
            b.add_box_center(ox + x1 + 0.03, hy, hz, 0.04, 0.03, 0.18, mat_handle)

    meta = {
        "name": name,
        "type": "sliding" if is_sliding else "hinged",
        "open_note": open_note,
        "colliders": b.colliders,
    }
    if is_sliding:
        if slide_axis == "X":
            meta["closed_offset_x"] = -round(offset_world[0], 4)
        else:
            meta["closed_offset_y"] = -round(offset_world[1], 4)
    else:
        meta["default_rot_deg"] = list(default_rot_deg)

    return b, meta, default_rot_deg


def add_barred_cell_door_builder(
    name,
    collection_name,
    pivot_world,
    axis,
    width,
    height,
    bar_mat="Mat_Arch_Metal_Anthracite",
    default_rot_deg=(0.0, 0.0, 0.0),
    open_note="Porte de cellule barreaudee",
):
    """Create a prison cell barred steel door pivoted on one jamb."""
    px, py, pz = pivot_world
    b = ac.ArchMeshBuilder(name, collection_name=collection_name, origin=(px, py, pz))
    t = 0.06
    n_bars = max(5, int(round(width / 0.14)))

    if axis == "Y":
        # Spans along +Y from 0 to width, thickness along X
        b.add_box_bounds(px - t / 2, px + t / 2, py, py + 0.05, pz, pz + height, bar_mat)
        b.add_box_bounds(px - t / 2, px + t / 2, py + width - 0.05, py + width, pz, pz + height, bar_mat)
        for rz in (0.08, height * 0.48, height - 0.08):
            b.add_box_bounds(px - t / 2, px + t / 2, py, py + width, pz + rz - 0.03, pz + rz + 0.03, bar_mat)
        for i in range(1, n_bars):
            by = py + (width * i) / n_bars
            b.add_box_center(px, by, pz + height * 0.5, 0.028, 0.028, height - 0.12, bar_mat)
        b.add_box_center(px - 0.04, py + width - 0.12, pz + 1.05, 0.03, 0.12, 0.24, "Mat_Arch_Metal_Inox")
        b.add_box_center(px + 0.04, py + width - 0.12, pz + 1.05, 0.03, 0.12, 0.24, "Mat_Arch_Metal_Inox")
        b.add_collider_bounds(px - t / 2, px + t / 2, py, py + width, pz, pz + height)
    else:
        # Spans along +X from 0 to width, thickness along Y
        b.add_box_bounds(px, px + 0.05, py - t / 2, py + t / 2, pz, pz + height, bar_mat)
        b.add_box_bounds(px + width - 0.05, px + width, py - t / 2, py + t / 2, pz, pz + height, bar_mat)
        for rz in (0.08, height * 0.48, height - 0.08):
            b.add_box_bounds(px, px + width, py - t / 2, py + t / 2, pz + rz - 0.03, pz + rz + 0.03, bar_mat)
        for i in range(1, n_bars):
            bx = px + (width * i) / n_bars
            b.add_box_center(bx, py, pz + height * 0.5, 0.028, 0.028, height - 0.12, bar_mat)
        b.add_box_center(px + width - 0.12, py - 0.04, pz + 1.05, 0.12, 0.03, 0.24, "Mat_Arch_Metal_Inox")
        b.add_box_center(px + width - 0.12, py + 0.04, pz + 1.05, 0.12, 0.03, 0.24, "Mat_Arch_Metal_Inox")
        b.add_collider_bounds(px, px + width, py - t / 2, py + t / 2, pz, pz + height)

    meta = {
        "name": name,
        "type": "hinged",
        "default_rot_deg": list(default_rot_deg),
        "open_note": open_note,
        "colliders": b.colliders,
    }
    return b, meta, default_rot_deg


def add_barred_partition(
    b,
    axis,
    fixed_center,
    span_start,
    span_end,
    z_bottom,
    z_top,
    door_s0,
    door_s1,
    door_z1,
    wall_mat="Mat_Arch_Beton_Brut",
    bar_mat="Mat_Arch_Metal_Anthracite",
    dwarf_wall_h=1.05,
):
    """Build a prison cell front partition: concrete dwarf wall + steel grille + open doorway."""
    wall_t = 0.22
    # Solid lower concrete dwarf wall on sides of the door
    if door_s0 > span_start + 0.05:
        b.add_wall_with_openings(
            axis, fixed_center, wall_t, span_start, door_s0, z_bottom, z_bottom + dwarf_wall_h,
            wall_mat, openings=None, collider=True,
        )
        # Upper steel grille above dwarf wall
        n_bars = max(3, int(round((door_s0 - span_start) / 0.15)))
        for i in range(n_bars + 1):
            s = span_start + (door_s0 - span_start) * i / n_bars
            if axis == "Y":
                b.add_box_center(fixed_center, s, (z_bottom + dwarf_wall_h + z_top) * 0.5, 0.032, 0.032, z_top - z_bottom - dwarf_wall_h, bar_mat)
            else:
                b.add_box_center(s, fixed_center, (z_bottom + dwarf_wall_h + z_top) * 0.5, 0.032, 0.032, z_top - z_bottom - dwarf_wall_h, bar_mat)
        for rz in (z_bottom + dwarf_wall_h + 0.04, (z_bottom + dwarf_wall_h + z_top) * 0.5, z_top - 0.06):
            if axis == "Y":
                b.add_box_bounds(fixed_center - 0.03, fixed_center + 0.03, span_start, door_s0, rz - 0.03, rz + 0.03, bar_mat)
            else:
                b.add_box_bounds(span_start, door_s0, fixed_center - 0.03, fixed_center + 0.03, rz - 0.03, rz + 0.03, bar_mat)
        if axis == "Y":
            b.add_collider_bounds(fixed_center - wall_t / 2, fixed_center + wall_t / 2, span_start, door_s0, z_bottom + dwarf_wall_h, z_top)
        else:
            b.add_collider_bounds(span_start, door_s0, fixed_center - wall_t / 2, fixed_center + wall_t / 2, z_bottom + dwarf_wall_h, z_top)

    if span_end > door_s1 + 0.05:
        b.add_wall_with_openings(
            axis, fixed_center, wall_t, door_s1, span_end, z_bottom, z_bottom + dwarf_wall_h,
            wall_mat, openings=None, collider=True,
        )
        n_bars = max(3, int(round((span_end - door_s1) / 0.15)))
        for i in range(n_bars + 1):
            s = door_s1 + (span_end - door_s1) * i / n_bars
            if axis == "Y":
                b.add_box_center(fixed_center, s, (z_bottom + dwarf_wall_h + z_top) * 0.5, 0.032, 0.032, z_top - z_bottom - dwarf_wall_h, bar_mat)
            else:
                b.add_box_center(s, fixed_center, (z_bottom + dwarf_wall_h + z_top) * 0.5, 0.032, 0.032, z_top - z_bottom - dwarf_wall_h, bar_mat)
        for rz in (z_bottom + dwarf_wall_h + 0.04, (z_bottom + dwarf_wall_h + z_top) * 0.5, z_top - 0.06):
            if axis == "Y":
                b.add_box_bounds(fixed_center - 0.03, fixed_center + 0.03, door_s1, span_end, rz - 0.03, rz + 0.03, bar_mat)
            else:
                b.add_box_bounds(door_s1, span_end, fixed_center - 0.03, fixed_center + 0.03, rz - 0.03, rz + 0.03, bar_mat)
        if axis == "Y":
            b.add_collider_bounds(fixed_center - wall_t / 2, fixed_center + wall_t / 2, door_s1, span_end, z_bottom + dwarf_wall_h, z_top)
        else:
            b.add_collider_bounds(door_s1, span_end, fixed_center - wall_t / 2, fixed_center + wall_t / 2, z_bottom + dwarf_wall_h, z_top)

    # Transom beam above the cell door
    if z_top > door_z1 + 0.05:
        b.add_wall_with_openings(
            axis, fixed_center, wall_t, door_s0, door_s1, door_z1, z_top,
            wall_mat, openings=None, collider=True,
        )


def add_colonnade_pillar(
    b,
    cx,
    cy,
    z0,
    z1,
    radius=0.45,
    base_mat="Mat_Arch_Parement_Pierre",
    shaft_mat="Mat_Arch_MurExt_Blanc",
    cap_mat="Mat_Arch_Or_Ceremonie",
    segments=16,
):
    """Add a classical/modernist monument column with base plinth and capital."""
    b.add_box_center(cx, cy, z0 + 0.22, radius * 2.5, radius * 2.5, 0.44, base_mat, collider=True)
    b.add_cylinder(cx, cy, z0 + 0.44, z1 - 0.38, radius, segments, shaft_mat)
    b.add_collider_center(cx, cy, (z0 + z1) * 0.5, radius * 2.0, radius * 2.0, z1 - z0)
    b.add_box_center(cx, cy, z1 - 0.24, radius * 2.35, radius * 2.35, 0.28, cap_mat)
    b.add_box_center(cx, cy, z1 - 0.05, radius * 2.6, radius * 2.6, 0.10, base_mat)


def add_flagpole_ivorian(b, cx, cy, z0, height=11.0, flag_dir="+X", police_flag=False):
    """Add a stainless flagpole with stone plinth and Ivorian (or police) flag."""
    b.add_box_center(cx, cy, z0 + 0.25, 0.90, 0.90, 0.50, "Mat_Arch_Parement_Pierre", collider=True)
    b.add_cylinder(cx, cy, z0 + 0.50, z0 + height, 0.075, 10, "Mat_Arch_Metal_Inox")
    b.add_cone(cx, cy, z0 + height, z0 + height + 0.22, 0.12, 0.02, 10, "Mat_Arch_Or_Ceremonie")

    fh = 1.50
    fw = 2.40
    fz0 = z0 + height - fh - 0.25
    fz1 = z0 + height - 0.25
    sign = 1.0 if flag_dir == "+X" else -1.0
    if police_flag:
        b.add_box_bounds(
            min(cx, cx + sign * fw), max(cx, cx + sign * fw),
            cy - 0.02, cy + 0.02, fz0, fz1, "Mat_Arch_Police_Bleu",
        )
        b.add_box_bounds(
            min(cx + sign * 0.6, cx + sign * 1.8), max(cx + sign * 0.6, cx + sign * 1.8),
            cy - 0.03, cy + 0.03, fz0 + 0.35, fz1 - 0.35, "Mat_Arch_Or_Ceremonie",
        )
    else:
        stripes = [
            "Mat_Arch_Drapeau_Orange",
            "Mat_Arch_MurExt_Blanc",
            "Mat_Arch_Drapeau_Vert",
        ]
        sw = fw / 3.0
        for idx, mat in enumerate(stripes):
            s0 = cx + sign * (idx * sw)
            s1 = cx + sign * ((idx + 1) * sw)
            b.add_box_bounds(min(s0, s1), max(s0, s1), cy - 0.02, cy + 0.02, fz0, fz1, mat)


def add_palm_tree(b, cx, cy, z0, height=6.5):
    """Add a stylized Abidjan royal palm tree with concrete planter ring."""
    b.add_cylinder(cx, cy, z0, z0 + 0.35, 0.95, 12, "Mat_Arch_Parement_Pierre")
    b.add_cylinder(cx, cy, z0 + 0.05, z0 + 0.38, 0.80, 12, "Mat_Arch_Gazon")
    b.add_cone(cx, cy, z0 + 0.35, z0 + height, 0.26, 0.14, 10, "Mat_Arch_Bois_Exterieur")
    b.add_collider_center(cx, cy, z0 + height * 0.5, 0.55, 0.55, height)
    crown_z = z0 + height
    for k in range(7):
        ang = k * (2.0 * math.pi / 7.0)
        dx = math.cos(ang) * 1.45
        dy = math.sin(ang) * 1.45
        b.add_box_center(
            cx + dx * 0.55, cy + dy * 0.55, crown_z + 0.18,
            1.65, 0.42, 0.10, "Mat_Arch_Gazon",
            rot_euler=(0.0, math.radians(-18.0), ang),
        )


def add_streetlamp(b, cx, cy, z0, height=5.2):
    """Add a twin-arm institutional courtyard light pole."""
    b.add_box_center(cx, cy, z0 + 0.18, 0.46, 0.46, 0.36, "Mat_Arch_Parement_Pierre", collider=True)
    b.add_cylinder(cx, cy, z0 + 0.36, z0 + height, 0.08, 10, "Mat_Arch_Metal_Anthracite")
    b.add_box_center(cx, cy, z0 + height - 0.12, 1.35, 0.12, 0.10, "Mat_Arch_Metal_Anthracite")
    for sx in (-0.58, 0.58):
        b.add_box_center(cx + sx, cy, z0 + height - 0.24, 0.32, 0.28, 0.14, "Mat_Arch_Lumiere_Nuit")


def add_office_desk(b, cx, cy, z0, facing="-Y", width=1.65, depth=0.80, executive=False, with_monitor=True):
    """Add an office desk with chair, optional executive return, and monitors."""
    desk_h = 0.76
    mat_top = "Mat_Arch_Bois_Interieur" if executive else "Mat_Arch_Plan_Travail"
    mat_leg = "Mat_Arch_Or_Ceremonie" if executive else "Mat_Arch_Metal_Anthracite"

    if facing in ("-Y", "+Y"):
        sx, sy = width, depth
        fy = -1.0 if facing == "-Y" else 1.0
        fx = 0.0
    else:
        sx, sy = depth, width
        fx = -1.0 if facing == "-X" else 1.0
        fy = 0.0

    # Desk tabletop + modesty panel + pedestals
    b.add_box_center(cx, cy, z0 + desk_h - 0.03, sx, sy, 0.06, mat_top, collider=True)
    for px in (-sx * 0.42, sx * 0.42):
        for py in (-sy * 0.36, sy * 0.36):
            b.add_box_center(cx + px, cy + py, z0 + (desk_h - 0.06) * 0.5, 0.07, 0.07, desk_h - 0.06, mat_leg)
    # Drawer pedestal
    if facing in ("-Y", "+Y"):
        b.add_box_center(cx + sx * 0.30, cy, z0 + 0.34, 0.44, sy * 0.85, 0.68, "Mat_Arch_Bois_Interieur")
    else:
        b.add_box_center(cx, cy + sy * 0.30, z0 + 0.34, sx * 0.85, 0.44, 0.68, "Mat_Arch_Bois_Interieur")

    # Chair behind desk (opposite facing)
    ch_x = cx - fx * (depth * 0.5 + 0.42)
    ch_y = cy - fy * (depth * 0.5 + 0.42)
    b.add_box_center(ch_x, ch_y, z0 + 0.46, 0.52, 0.52, 0.08, "Mat_Arch_Tissu_Mobilier")
    b.add_box_center(ch_x, ch_y, z0 + 0.22, 0.10, 0.10, 0.44, "Mat_Arch_Metal_Inox")
    b.add_box_center(ch_x, ch_y, z0 + 0.03, 0.54, 0.54, 0.06, "Mat_Arch_Metal_Anthracite")
    # Backrest on far side of chair
    b.add_box_center(ch_x - fx * 0.23, ch_y - fy * 0.23, z0 + 0.80, 0.08 if fx else 0.48, 0.48 if fx else 0.08, 0.62, "Mat_Arch_Tissu_Mobilier")

    if with_monitor:
        mx = cx + fx * 0.12
        my = cy + fy * 0.12
        b.add_box_center(mx, my, z0 + desk_h + 0.12, 0.12, 0.12, 0.24, "Mat_Arch_Metal_Anthracite")
        if facing in ("-Y", "+Y"):
            b.add_box_center(mx, my, z0 + desk_h + 0.32, 0.62, 0.05, 0.36, "Mat_Arch_Metal_Anthracite")
            b.add_box_center(mx, my - fy * 0.03, z0 + desk_h + 0.32, 0.56, 0.02, 0.30, "Mat_Arch_Lumiere_Ecran")
        else:
            b.add_box_center(mx, my, z0 + desk_h + 0.32, 0.05, 0.62, 0.36, "Mat_Arch_Metal_Anthracite")
            b.add_box_center(mx - fx * 0.03, my, z0 + desk_h + 0.32, 0.02, 0.56, 0.30, "Mat_Arch_Lumiere_Ecran")


def add_conference_table(
    b,
    cx,
    cy,
    z0,
    length=7.0,
    width=2.4,
    axis="X",
    num_chairs_side=6,
    table_mat="Mat_Arch_Bois_Interieur",
    chair_mat="Mat_Arch_Tissu_Mobilier",
    hollow_center=False,
):
    """Add a large conference / council table surrounded by chairs."""
    th = 0.78
    if axis == "X":
        sx, sy = length, width
    else:
        sx, sy = width, length

    if hollow_center and length >= 5.0 and width >= 3.2:
        rim = 0.95
        b.add_box_center(cx, cy + sy * 0.5 - rim * 0.5, z0 + th - 0.04, sx, rim, 0.08, table_mat, collider=True)
        b.add_box_center(cx, cy - sy * 0.5 + rim * 0.5, z0 + th - 0.04, sx, rim, 0.08, table_mat, collider=True)
        b.add_box_center(cx - sx * 0.5 + rim * 0.5, cy, z0 + th - 0.04, rim, sy - 2 * rim, 0.08, table_mat, collider=True)
        b.add_box_center(cx + sx * 0.5 - rim * 0.5, cy, z0 + th - 0.04, rim, sy - 2 * rim, 0.08, table_mat, collider=True)
        for px in (-sx * 0.38, 0.0, sx * 0.38):
            for py in (-sy * 0.40, sy * 0.40):
                b.add_box_center(cx + px, cy + py, z0 + (th - 0.08) * 0.5, 0.25, 0.25, th - 0.08, table_mat)
    else:
        b.add_box_center(cx, cy, z0 + th - 0.04, sx, sy, 0.08, table_mat, collider=True)
        n_legs = max(2, int(round(length / 2.6)))
        for i in range(n_legs):
            t = -0.38 * length + (0.76 * length * i / max(1, n_legs - 1))
            lx = cx + (t if axis == "X" else 0.0)
            ly = cy + (t if axis == "Y" else 0.0)
            b.add_box_center(lx, ly, z0 + (th - 0.08) * 0.5, 0.35 if axis == "X" else sy * 0.55, sy * 0.55 if axis == "X" else 0.35, th - 0.08, "Mat_Arch_Metal_Anthracite")

    # Side chairs
    span = length - 1.0
    for i in range(num_chairs_side):
        pos = -span * 0.5 + (span * i / max(1, num_chairs_side - 1))
        for side in (-1.0, 1.0):
            if axis == "X":
                chx = cx + pos
                chy = cy + side * (sy * 0.5 + 0.42)
                bx, by = 0.0, side * 0.22
            else:
                chx = cx + side * (sx * 0.5 + 0.42)
                chy = cy + pos
                bx, by = side * 0.22, 0.0
            b.add_box_center(chx, chy, z0 + 0.46, 0.50, 0.50, 0.08, chair_mat)
            b.add_box_center(chx, chy, z0 + 0.22, 0.08, 0.08, 0.44, "Mat_Arch_Metal_Inox")
            b.add_box_center(chx + bx, chy + by, z0 + 0.78, 0.08 if bx else 0.48, 0.48 if bx else 0.08, 0.58, chair_mat)

    # Head / foot chairs
    for side in (-1.0, 1.0):
        if axis == "X":
            chx, chy = cx + side * (sx * 0.5 + 0.48), cy
            bx, by = side * 0.24, 0.0
        else:
            chx, chy = cx, cy + side * (sy * 0.5 + 0.48)
            bx, by = 0.0, side * 0.24
        b.add_box_center(chx, chy, z0 + 0.48, 0.56, 0.56, 0.09, chair_mat)
        b.add_box_center(chx, chy, z0 + 0.23, 0.10, 0.10, 0.46, "Mat_Arch_Or_Ceremonie")
        b.add_box_center(chx + bx, chy + by, z0 + 0.88, 0.09 if bx else 0.54, 0.54 if bx else 0.09, 0.74, chair_mat)


def add_reception_or_caisse_counter(
    b,
    cx,
    cy,
    z0,
    length=5.4,
    axis="X",
    facing="-Y",
    num_windows=3,
    armored_glass=True,
):
    """Build a multi-station reception or secure cashier (caisse) counter."""
    ch = 1.08
    depth = 0.85
    sx = length if axis == "X" else depth
    sy = depth if axis == "X" else length
    b.add_box_center(cx, cy, z0 + ch * 0.5, sx, sy, ch, "Mat_Arch_Bois_Interieur", collider=True)
    b.add_box_center(cx, cy, z0 + ch + 0.03, sx + 0.08, sy + 0.08, 0.06, "Mat_Arch_Plan_Travail", collider=True)

    # Bulletproof glass bays + metal frames for caisse / security reception
    if armored_glass:
        gh = 1.15
        bay_w = length / num_windows
        for i in range(num_windows + 1):
            s = -length * 0.5 + i * bay_w
            px = cx + (s if axis == "X" else 0.0)
            py = cy + (s if axis == "Y" else 0.0)
            b.add_box_center(px, py, z0 + ch + gh * 0.5, 0.08, 0.08, gh, "Mat_Arch_Metal_Inox")
        b.add_box_center(
            cx, cy, z0 + ch + gh + 0.04,
            length if axis == "X" else 0.10, 0.10 if axis == "X" else length, 0.08,
            "Mat_Arch_Metal_Anthracite",
        )
        # Glass pane leaving 0.15 m pass-through slot above counter
        b.add_box_center(
            cx, cy, z0 + ch + 0.16 + (gh - 0.16) * 0.5,
            (length - 0.12) if axis == "X" else 0.03, 0.03 if axis == "X" else (length - 0.12),
            gh - 0.16, "Mat_Arch_Vitre_Ext",
        )

    # Monitors inside stations
    step = length / num_windows
    for i in range(num_windows):
        s = -length * 0.5 + (i + 0.5) * step
        off = 0.24 if facing in ("+Y", "+X") else -0.24
        mx = cx + (s if axis == "X" else -off)
        my = cy + (-off if axis == "X" else s)
        b.add_box_center(
            mx, my, z0 + ch + 0.24,
            0.46 if axis == "X" else 0.04, 0.04 if axis == "X" else 0.46, 0.28,
            "Mat_Arch_Lumiere_Ecran",
        )


def add_safe_vault(b, cx, cy, z0, facing="-Y", size=(1.5, 1.2, 2.0)):
    """Add a heavy armored safe / treasury vault."""
    sx, sy, sz = size
    b.add_box_center(cx, cy, z0 + sz * 0.5, sx, sy, sz, "Mat_Arch_Metal_Anthracite", collider=True)
    # Vault door face + wheel handle
    fx = 0.0 if facing in ("-Y", "+Y") else (-1.0 if facing == "-X" else 1.0)
    fy = (-1.0 if facing == "-Y" else 1.0) if facing in ("-Y", "+Y") else 0.0
    b.add_box_center(
        cx + fx * (sx * 0.5 + 0.03),
        cy + fy * (sy * 0.5 + 0.03),
        z0 + sz * 0.5,
        0.06 if fx else sx * 0.82,
        sx * 0.82 if fx else 0.06,
        sz * 0.84,
        "Mat_Arch_Metal_Inox",
    )
    b.add_box_center(
        cx + fx * (sx * 0.5 + 0.09),
        cy + fy * (sy * 0.5 + 0.09),
        z0 + sz * 0.5,
        0.08 if fx else 0.34,
        0.34 if fx else 0.08,
        0.34,
        "Mat_Arch_Or_Ceremonie",
    )


def add_prison_cell_interior(b, x0, x1, y0, y1, z0, bunk_side="-Y", double_bunk=False):
    """Furnish a prison cell with concrete bunk(s), mattress, and stainless toilet/sink."""
    # Bunk along chosen wall
    if bunk_side == "-Y":
        bx0, bx1, by0, by1 = x0 + 0.20, x0 + 2.25, y0 + 0.12, y0 + 0.98
        tx, ty = x1 - 0.55, y0 + 0.45
    elif bunk_side == "+Y":
        bx0, bx1, by0, by1 = x0 + 0.20, x0 + 2.25, y1 - 0.98, y1 - 0.12
        tx, ty = x1 - 0.55, y1 - 0.45
    elif bunk_side == "-X":
        bx0, bx1, by0, by1 = x0 + 0.12, x0 + 0.98, y0 + 0.20, y0 + 2.25
        tx, ty = x0 + 0.45, y1 - 0.55
    else:
        bx0, bx1, by0, by1 = x1 - 0.98, x1 - 0.12, y0 + 0.20, y0 + 2.25
        tx, ty = x1 - 0.45, y1 - 0.55

    # Concrete plinth + mattress
    b.add_box_bounds(bx0, bx1, by0, by1, z0, z0 + 0.44, "Mat_Arch_Beton_Brut", collider=True)
    b.add_box_bounds(bx0 + 0.04, bx1 - 0.04, by0 + 0.04, by1 - 0.04, z0 + 0.44, z0 + 0.58, "Mat_Arch_Tissu_Mobilier")
    if double_bunk:
        b.add_box_bounds(bx0, bx1, by0, by1, z0 + 1.48, z0 + 1.56, "Mat_Arch_Metal_Anthracite", collider=True)
        b.add_box_bounds(bx0 + 0.04, bx1 - 0.04, by0 + 0.04, by1 - 0.04, z0 + 1.56, z0 + 1.68, "Mat_Arch_Tissu_Mobilier")

    # Anti-vandal stainless toilet + sink block
    b.add_box_center(tx, ty, z0 + 0.22, 0.46, 0.54, 0.44, "Mat_Arch_Metal_Inox", collider=True)
    b.add_box_center(tx, ty, z0 + 0.46, 0.36, 0.40, 0.04, "Mat_Arch_Ceramique_SDB")
    b.add_box_center(tx + 0.05, ty + 0.35, z0 + 0.85, 0.42, 0.34, 0.20, "Mat_Arch_Metal_Inox")


def add_interrogation_set(b, cx, cy, z0):
    """Add bolted interrogation table and 3 chairs."""
    b.add_box_center(cx, cy, z0 + 0.74, 1.35, 0.85, 0.06, "Mat_Arch_Metal_Inox", collider=True)
    b.add_box_center(cx, cy, z0 + 0.36, 0.22, 0.22, 0.72, "Mat_Arch_Metal_Anthracite")
    for dy, sx in ((-0.75, 0.0), (0.75, -0.32), (0.75, 0.32)):
        b.add_box_center(cx + sx, cy + dy, z0 + 0.45, 0.46, 0.46, 0.06, "Mat_Arch_Metal_Anthracite")
        b.add_box_center(cx + sx, cy + dy, z0 + 0.22, 0.08, 0.08, 0.44, "Mat_Arch_Metal_Anthracite")
        b.add_box_center(cx + sx, cy + dy + (0.20 if dy > 0 else -0.20), z0 + 0.72, 0.44, 0.06, 0.48, "Mat_Arch_Metal_Anthracite")


def add_locker_bank(b, cx, cy, z0, count=5, axis="X", mat="Mat_Arch_Police_Bleu"):
    """Add a bank of steel staff lockers."""
    w = 0.55
    total = count * w
    if axis == "X":
        b.add_box_center(cx, cy, z0 + 1.00, total, 0.55, 2.00, mat, collider=True)
        for i in range(count):
            lx = cx - total * 0.5 + (i + 0.5) * w
            b.add_box_center(lx, cy - 0.28, z0 + 1.00, w - 0.05, 0.02, 1.88, "Mat_Arch_Metal_Inox")
    else:
        b.add_box_center(cx, cy, z0 + 1.00, 0.55, total, 2.00, mat, collider=True)
        for i in range(count):
            ly = cy - total * 0.5 + (i + 0.5) * w
            b.add_box_center(cx - 0.28, ly, z0 + 1.00, 0.02, w - 0.05, 1.88, "Mat_Arch_Metal_Inox")


def add_armory_rack(b, cx, cy, z0, length=2.8, axis="X"):
    """Add a secure weapon and tactical gear storage rack."""
    if axis == "X":
        b.add_box_center(cx, cy, z0 + 1.05, length, 0.55, 2.10, "Mat_Arch_Metal_Anthracite", collider=True)
        for s in (0.45, 1.05, 1.65):
            b.add_box_center(cx, cy, z0 + s, length - 0.08, 0.52, 0.05, "Mat_Arch_Metal_Inox")
    else:
        b.add_box_center(cx, cy, z0 + 1.05, 0.55, length, 2.10, "Mat_Arch_Metal_Anthracite", collider=True)
        for s in (0.45, 1.05, 1.65):
            b.add_box_center(cx, cy, z0 + s, 0.52, length - 0.08, 0.05, "Mat_Arch_Metal_Inox")


def add_cctv_wall(b, cx, cy, z0, axis="X", width=5.0, rows=2, cols=4, facing_sign=-1.0):
    """Add a multi-monitor CCTV / command video wall."""
    b.add_box_center(
        cx, cy, z0 + 1.85,
        width if axis == "X" else 0.14, 0.14 if axis == "X" else width, 2.20,
        "Mat_Arch_Metal_Anthracite", collider=True,
    )
    mw = (width - 0.40) / cols
    mh = 1.90 / rows
    off = 0.08 * (1.0 if facing_sign >= 0 else -1.0)
    for r in range(rows):
        for c in range(cols):
            s = -width * 0.5 + 0.20 + (c + 0.5) * mw
            mz = z0 + 0.90 + (r + 0.5) * mh
            if axis == "X":
                b.add_box_center(cx + s, cy + off, mz, mw - 0.10, 0.04, mh - 0.10, "Mat_Arch_Lumiere_Ecran")
            else:
                b.add_box_center(cx + off, cy + s, mz, 0.04, mw - 0.10, mh - 0.10, "Mat_Arch_Lumiere_Ecran")


def add_sofa_lounge(b, cx, cy, z0, fabric_mat="Mat_Arch_Tissu_Mobilier"):
    """Add a ceremonial / VIP salon seating group with marble coffee table."""
    b.add_box_center(cx, cy, z0 + 0.22, 1.40, 0.85, 0.44, "Mat_Arch_Sol_Marbre", collider=True)
    # Main sofa (-Y side facing +Y)
    b.add_box_center(cx, cy - 1.35, z0 + 0.24, 2.40, 0.90, 0.42, fabric_mat, collider=True)
    b.add_box_center(cx, cy - 1.72, z0 + 0.58, 2.40, 0.18, 0.55, fabric_mat)
    # Two armchairs flanking
    for sx in (-1.55, 1.55):
        b.add_box_center(cx + sx, cy, z0 + 0.24, 0.90, 0.95, 0.42, fabric_mat, collider=True)
        b.add_box_center(cx + sx + (0.36 if sx > 0 else -0.36), cy, z0 + 0.58, 0.18, 0.95, 0.55, fabric_mat)


def add_chandelier(b, cx, cy, z_ceil, drop=1.45, radius=1.25):
    """Add a monumental gilded palace chandelier with glowing light rings."""
    b.add_cylinder(cx, cy, z_ceil - drop, z_ceil, 0.05, 8, "Mat_Arch_Or_Ceremonie")
    for tier, (dz, r) in enumerate(((drop * 0.55, radius * 0.65), (drop * 0.92, radius))):
        zc = z_ceil - dz
        n_arms = 8 if tier == 0 else 12
        b.add_cylinder(cx, cy, zc - 0.05, zc + 0.05, r, 16, "Mat_Arch_Or_Ceremonie")
        for i in range(n_arms):
            ang = i * (2.0 * math.pi / n_arms)
            lx = cx + math.cos(ang) * r
            ly = cy + math.sin(ang) * r
            b.add_box_center(lx, ly, zc + 0.14, 0.12, 0.12, 0.20, "Mat_Arch_Lumiere_Int_Nuit")


def add_podium_lectern(b, cx, cy, z0, facing="-Y"):
    """Add a presidential / briefing lectern with microphones and gold seal."""
    b.add_box_center(cx, cy, z0 + 0.08, 1.15, 0.85, 0.16, "Mat_Arch_Bois_Interieur", collider=True)
    b.add_box_center(cx, cy, z0 + 0.72, 0.78, 0.58, 1.12, "Mat_Arch_Bois_Interieur", collider=True)
    b.add_box_center(cx, cy, z0 + 1.30, 0.86, 0.66, 0.08, "Mat_Arch_Bois_Interieur")
    fy = -0.30 if facing == "-Y" else 0.30
    b.add_box_center(cx, cy + fy, z0 + 0.86, 0.38, 0.04, 0.38, "Mat_Arch_Or_Ceremonie")
    for mx in (-0.16, 0.16):
        b.add_box_center(cx + mx, cy + fy * 0.6, z0 + 1.42, 0.03, 0.03, 0.20, "Mat_Arch_Metal_Inox")


def add_helipad_marking(b, cx, cy, z_roof, radius=6.2):
    """Add a rooftop helipad landing zone with perimeter lights and 'H'."""
    b.add_cylinder(cx, cy, z_roof, z_roof + 0.04, radius, 24, "Mat_Arch_MurExt_Anthracite")
    b.add_cylinder(cx, cy, z_roof + 0.04, z_roof + 0.06, radius - 0.35, 24, "Mat_Arch_Toiture_Terrasse")
    # 'H' marking in crisp white/yellow-gold
    b.add_box_center(cx - 1.4, cy, z_roof + 0.07, 0.55, 4.4, 0.03, "Mat_Arch_Or_Ceremonie")
    b.add_box_center(cx + 1.4, cy, z_roof + 0.07, 0.55, 4.4, 0.03, "Mat_Arch_Or_Ceremonie")
    b.add_box_center(cx, cy, z_roof + 0.07, 2.8, 0.55, 0.03, "Mat_Arch_Or_Ceremonie")
    for i in range(8):
        ang = i * (2.0 * math.pi / 8.0)
        b.add_box_center(
            cx + math.cos(ang) * (radius + 0.25),
            cy + math.sin(ang) * (radius + 0.25),
            z_roof + 0.10, 0.24, 0.24, 0.16, "Mat_Arch_Lumiere_Nuit",
        )


def add_security_portal_scanner(b, cx, cy, z0):
    """Add an entry metal-detector arch and adjacent baggage scanner."""
    b.add_box_center(cx - 0.52, cy, z0 + 1.12, 0.14, 0.72, 2.24, "Mat_Arch_Metal_Anthracite", collider=True)
    b.add_box_center(cx + 0.52, cy, z0 + 1.12, 0.14, 0.72, 2.24, "Mat_Arch_Metal_Anthracite", collider=True)
    b.add_box_center(cx, cy, z0 + 2.20, 1.18, 0.72, 0.18, "Mat_Arch_Metal_Anthracite")
    b.add_box_center(cx, cy - 0.36, z0 + 2.20, 0.70, 0.04, 0.08, "Mat_Arch_Lumiere_Ecran")
    # Baggage X-ray tunnel next to arch
    b.add_box_center(cx + 1.65, cy, z0 + 0.55, 0.90, 2.20, 0.65, "Mat_Arch_Metal_Anthracite", collider=True)
    b.add_box_center(cx + 1.65, cy, z0 + 1.15, 0.92, 1.00, 0.75, "Mat_Arch_Police_Bleu", collider=True)


def add_bathroom_fixtures(b, x0, x1, y0, y1, z0, num_stalls=3):
    """Add toilet stalls and washbasin counter inside a restroom zone."""
    span = y1 - y0
    stall_w = min(1.25, (span - 0.4) / max(1, num_stalls))
    for i in range(num_stalls):
        sy = y0 + 0.2 + i * stall_w
        b.add_box_bounds(x0 + 0.05, x0 + 1.45, sy, sy + 0.05, z0, z0 + 2.10, "Mat_Arch_Plan_Travail", collider=True)
        b.add_box_center(x0 + 0.42, sy + stall_w * 0.5, z0 + 0.22, 0.44, 0.38, 0.44, "Mat_Arch_Ceramique_SDB")
        b.add_box_center(x0 + 0.16, sy + stall_w * 0.5, z0 + 0.55, 0.18, 0.36, 0.42, "Mat_Arch_Ceramique_SDB")
    # Sink counter opposite
    b.add_box_bounds(x1 - 0.65, x1 - 0.05, y0 + 0.30, y1 - 0.30, z0 + 0.78, z0 + 0.86, "Mat_Arch_Plan_Travail", collider=True)
    for i in range(num_stalls):
        sy = y0 + 0.6 + i * ((span - 1.2) / max(1, num_stalls))
        b.add_box_center(x1 - 0.35, sy, z0 + 0.89, 0.42, 0.48, 0.12, "Mat_Arch_Ceramique_SDB")
        b.add_box_center(x1 - 0.06, sy, z0 + 1.45, 0.04, 0.60, 0.85, "Mat_Arch_Metal_Inox")


def add_window_mullions(
    b,
    axis,
    fixed_center,
    s0,
    s1,
    z0,
    z1,
    num_bays=3,
    mat_name="Mat_Arch_Metal_Anthracite",
    horizontal_bar=True,
):
    """Add vertical (and optional horizontal transom) window mullions inside an opening."""
    span = s1 - s0
    if num_bays >= 2:
        for i in range(1, num_bays):
            s = s0 + span * i / num_bays
            if axis == "X":
                b.add_box_center(s, fixed_center, (z0 + z1) * 0.5, 0.07, 0.14, z1 - z0, mat_name)
            else:
                b.add_box_center(fixed_center, s, (z0 + z1) * 0.5, 0.14, 0.07, z1 - z0, mat_name)
    if horizontal_bar and (z1 - z0) >= 1.8:
        zt = z1 - 0.55
        if axis == "X":
            b.add_box_bounds(s0, s1, fixed_center - 0.07, fixed_center + 0.07, zt - 0.04, zt + 0.04, mat_name)
        else:
            b.add_box_bounds(fixed_center - 0.07, fixed_center + 0.07, s0, s1, zt - 0.04, zt + 0.04, mat_name)


def add_ceiling_lights_grid(b, x0, x1, y0, y1, z_ceil, nx=2, ny=2, size=(1.1, 0.55)):
    """Place recessed ceiling LED fixtures evenly inside a room rectangle."""
    for ix in range(nx):
        px = x0 + (x1 - x0) * (ix + 0.5) / nx
        for iy in range(ny):
            py = y0 + (y1 - y0) * (iy + 0.5) / ny
            b.add_box_center(px, py, z_ceil - 0.03, size[0], size[1], 0.05, "Mat_Arch_Lumiere_Int_Nuit")


def add_dome(
    b,
    cx,
    cy,
    z0,
    radius,
    height,
    segments=24,
    rings=8,
    mat_name="Mat_Arch_Mosquee_Bleu",
    pointed=True,
    collider=True,
    open_bottom=False,
    inner_mat=None,
):
    """Build a UV-mapped hemispherical or slightly pointed dome in bmesh."""
    bm = b.bm
    ox, oy, oz = b.origin.x, b.origin.y, b.origin.z

    def _build_shell(rad_scale, h_scale, mname, flip=False):
        ring_verts = []
        for r in range(rings):
            t = r / float(rings)
            ang_v = t * (math.pi * 0.5)
            rad = (radius * rad_scale) * math.cos(ang_v) * (1.0 - 0.12 * t if pointed else 1.0)
            vz = z0 + (height * h_scale) * math.sin(ang_v)
            ring = []
            for s in range(segments):
                ang = s * (2.0 * math.pi / segments)
                vx = cx + math.cos(ang) * rad
                vy = cy + math.sin(ang) * rad
                ring.append(bm.verts.new((vx - ox, vy - oy, vz - oz)))
            ring_verts.append(ring)

        v_apex = bm.verts.new((cx - ox, cy - oy, z0 + height * h_scale - oz))
        faces = []
        for r in range(rings - 1):
            r0, r1 = ring_verts[r], ring_verts[r + 1]
            for s in range(segments):
                sn = (s + 1) % segments
                quad = (r0[s], r0[sn], r1[sn], r1[s])
                faces.append(bm.faces.new(quad[::-1] if flip else quad))
        top_ring = ring_verts[-1]
        for s in range(segments):
            sn = (s + 1) % segments
            tri = (top_ring[s], top_ring[sn], v_apex)
            faces.append(bm.faces.new(tri[::-1] if flip else tri))
        if not open_bottom and not flip:
            v_base = bm.verts.new((cx - ox, cy - oy, z0 - oz))
            bot_ring = ring_verts[0]
            for s in range(segments):
                sn = (s + 1) % segments
                faces.append(bm.faces.new((bot_ring[sn], bot_ring[s], v_base)))
        b._assign_box_uv_and_mat(faces, b._mat_index(mname), uv_scale=0.5)

    _build_shell(1.0, 1.0, mat_name, flip=False)
    if open_bottom and inner_mat:
        _build_shell(0.95, 0.95, inner_mat, flip=True)

    if collider:
        b.add_collider_center(cx, cy, z0 + height * 0.5, radius * 1.8, radius * 1.8, height)


def add_hollow_drum(b, cx, cy, z0, z1, r_inner, r_outer, segments=20, mat_name="Mat_Arch_Pierre_Ocre"):
    """Build an open cylindrical/polygonal dome drum ring using tangent segments."""
    thick = max(0.15, r_outer - r_inner)
    r_mid = (r_inner + r_outer) * 0.5
    seg_w = 2.0 * r_outer * math.tan(math.pi / segments) + 0.12
    for i in range(segments):
        ang = i * (2.0 * math.pi / segments)
        px = cx + math.cos(ang) * r_mid
        py = cy + math.sin(ang) * r_mid
        b.add_box_center(px, py, (z0 + z1) * 0.5, thick, seg_w, z1 - z0, mat_name, rot_euler=(0.0, 0.0, ang), collider=True)


def add_beam_between(b, p0, p1, radius, mat_name, segments=8):
    """Build a cylindrical beam, pylon, or cable-stay between two 3D points."""
    bm = b.bm
    v0 = Vector(p0) - b.origin
    v1 = Vector(p1) - b.origin
    diff = v1 - v0
    if diff.length < 1e-4:
        return
    z_ax = diff.normalized()
    up = Vector((0.0, 0.0, 1.0)) if abs(z_ax.z) < 0.99 else Vector((1.0, 0.0, 0.0))
    x_ax = up.cross(z_ax).normalized()
    y_ax = z_ax.cross(x_ax).normalized()

    ring0, ring1 = [], []
    for s in range(segments):
        ang = s * (2.0 * math.pi / segments)
        offset = (x_ax * math.cos(ang) + y_ax * math.sin(ang)) * radius
        ring0.append(bm.verts.new(v0 + offset))
        ring1.append(bm.verts.new(v1 + offset))
    faces = [bm.faces.new(ring0[::-1]), bm.faces.new(ring1)]
    for s in range(segments):
        sn = (s + 1) % segments
        faces.append(bm.faces.new((ring0[s], ring0[sn], ring1[sn], ring1[s])))
    b._assign_box_uv_and_mat(faces, b._mat_index(mat_name), uv_scale=1.0)


def add_sloped_roof_slab(b, x0, x1, y0, y1, z_y0, z_y1, thickness, mat_name, collider=True):
    """Build a roof slab or amphitheater deck sloping linearly along Y."""
    bm = b.bm
    ox, oy, oz = b.origin.x, b.origin.y, b.origin.z
    v000 = bm.verts.new((x0 - ox, y0 - oy, z_y0 - oz))
    v100 = bm.verts.new((x1 - ox, y0 - oy, z_y0 - oz))
    v110 = bm.verts.new((x1 - ox, y1 - oy, z_y1 - oz))
    v010 = bm.verts.new((x0 - ox, y1 - oy, z_y1 - oz))
    v001 = bm.verts.new((x0 - ox, y0 - oy, z_y0 + thickness - oz))
    v101 = bm.verts.new((x1 - ox, y0 - oy, z_y0 + thickness - oz))
    v111 = bm.verts.new((x1 - ox, y1 - oy, z_y1 + thickness - oz))
    v011 = bm.verts.new((x0 - ox, y1 - oy, z_y1 + thickness - oz))

    faces = [
        bm.faces.new((v001, v101, v111, v011)),
        bm.faces.new((v000, v010, v110, v100)),
        bm.faces.new((v000, v100, v101, v001)),
        bm.faces.new((v010, v011, v111, v110)),
        bm.faces.new((v000, v001, v011, v010)),
        bm.faces.new((v100, v110, v111, v101)),
    ]
    b._assign_box_uv_and_mat(faces, b._mat_index(mat_name), uv_scale=0.35)
    if collider:
        dy = y1 - y0
        dz = z_y1 - z_y0
        slope_len = math.hypot(dy, dz)
        rx = math.atan2(dz, dy)
        b.add_collider_center(
            (x0 + x1) * 0.5, (y0 + y1) * 0.5, (z_y0 + z_y1 + thickness) * 0.5,
            x1 - x0, slope_len, thickness, rot_euler=(rx, 0.0, 0.0),
        )


def add_crescent_finial(b, cx, cy, z0, scale=1.0, mat_name="Mat_Arch_Or_Ceremonie"):
    """Add a gilded Islamic finial spire and stylized crescent."""
    b.add_cylinder(cx, cy, z0, z0 + 2.4 * scale, 0.08 * scale, 10, mat_name)
    for sz, sr in ((0.7, 0.24), (1.35, 0.18), (1.85, 0.14)):
        b.add_box_center(cx, cy, z0 + sz * scale, sr * 2 * scale, sr * 2 * scale, sr * 1.8 * scale, mat_name)
    # Stylized crescent at top
    b.add_box_center(cx, cy, z0 + 2.35 * scale, 0.85 * scale, 0.10 * scale, 0.18 * scale, mat_name)
    b.add_box_center(cx - 0.36 * scale, cy, z0 + 2.65 * scale, 0.14 * scale, 0.10 * scale, 0.58 * scale, mat_name)
    b.add_box_center(cx + 0.36 * scale, cy, z0 + 2.65 * scale, 0.14 * scale, 0.10 * scale, 0.58 * scale, mat_name)


def add_cross_emblem(b, cx, cy, z0, width=1.6, height=2.6, thickness=0.22, axis="X", mat_name="Mat_Arch_Or_Ceremonie"):
    """Add a 3D architectural cross (for church, cathedral, or hospital red cross)."""
    if axis == "X":
        b.add_box_center(cx, cy, z0 + height * 0.5, thickness, thickness, height, mat_name)
        b.add_box_center(cx, cy, z0 + height * 0.65, width, thickness, thickness, mat_name)
    else:
        b.add_box_center(cx, cy, z0 + height * 0.5, thickness, thickness, height, mat_name)
        b.add_box_center(cx, cy, z0 + height * 0.65, thickness, width, thickness, mat_name)


def add_hospital_bed(b, cx, cy, z0, axis="Y", with_monitor=True):
    """Add a medical hospital bed with teal blanket, pillow, and vitals monitor."""
    sx, sy = (1.05, 2.15) if axis == "Y" else (2.15, 1.05)
    b.add_box_center(cx, cy, z0 + 0.42, sx, sy, 0.18, "Mat_Arch_Metal_Inox", collider=True)
    for dx in (-sx * 0.42, sx * 0.42):
        for dy in (-sy * 0.42, sy * 0.42):
            b.add_box_center(cx + dx, cy + dy, z0 + 0.17, 0.07, 0.07, 0.34, "Mat_Arch_Metal_Anthracite")
    # Mattress & teal blanket
    b.add_box_center(cx, cy, z0 + 0.58, sx * 0.92, sy * 0.92, 0.16, "Mat_Arch_MurExt_Blanc")
    if axis == "Y":
        b.add_box_center(cx, cy + 0.25, z0 + 0.60, sx * 0.94, sy * 0.58, 0.17, "Mat_Arch_Hopital_Teal")
        b.add_box_center(cx, cy - 0.72, z0 + 0.70, 0.58, 0.34, 0.10, "Mat_Arch_Ceramique_SDB")
        b.add_box_center(cx, cy - sy * 0.49, z0 + 0.78, sx, 0.06, 0.58, "Mat_Arch_Metal_Inox")
    else:
        b.add_box_center(cx + 0.25, cy, z0 + 0.60, sx * 0.58, sy * 0.94, 0.17, "Mat_Arch_Hopital_Teal")
        b.add_box_center(cx - 0.72, cy, z0 + 0.70, 0.34, 0.58, 0.10, "Mat_Arch_Ceramique_SDB")
        b.add_box_center(cx - sx * 0.49, cy, z0 + 0.78, 0.06, sy, 0.58, "Mat_Arch_Metal_Inox")
    if with_monitor:
        mx = cx + (0.78 if axis == "Y" else -0.60)
        my = cy + (-0.60 if axis == "Y" else 0.78)
        b.add_box_center(mx, my, z0 + 0.65, 0.44, 0.44, 1.30, "Mat_Arch_Metal_Inox", collider=True)
        b.add_box_center(mx, my, z0 + 1.46, 0.42, 0.12, 0.30, "Mat_Arch_Lumiere_Ecran")


def add_operating_table(b, cx, cy, z0):
    """Add a surgical operating table, overhead surgical lamp, and instrument cart."""
    b.add_box_center(cx, cy, z0 + 0.45, 0.65, 0.95, 0.86, "Mat_Arch_Metal_Inox", collider=True)
    b.add_box_center(cx, cy, z0 + 0.92, 0.72, 2.15, 0.12, "Mat_Arch_Hopital_Teal", collider=True)
    # Overhead surgical lamp dome + glowing ring
    b.add_cylinder(cx, cy, z0 + 2.45, z0 + 3.40, 0.06, 8, "Mat_Arch_Metal_Inox")
    b.add_cylinder(cx, cy, z0 + 2.32, z0 + 2.45, 0.65, 16, "Mat_Arch_Ceramique_SDB")
    b.add_cylinder(cx, cy, z0 + 2.28, z0 + 2.33, 0.52, 16, "Mat_Arch_Lumiere_Int_Nuit")
    # Anesthesia & vitals tower
    b.add_box_center(cx - 1.25, cy - 0.55, z0 + 0.75, 0.65, 0.55, 1.45, "Mat_Arch_Metal_Inox", collider=True)
    b.add_box_center(cx - 1.25, cy - 0.25, z0 + 1.65, 0.55, 0.08, 0.36, "Mat_Arch_Lumiere_Ecran")


def add_ct_mri_scanner(b, cx, cy, z0):
    """Add a medical CT / MRI imaging gantry and sliding patient table."""
    b.add_box_center(cx, cy + 0.65, z0 + 1.15, 2.30, 0.95, 2.30, "Mat_Arch_Ceramique_SDB", collider=True)
    b.add_cylinder(cx, cy + 0.65, z0 + 0.25, z0 + 1.65, 0.52, 16, "Mat_Arch_Metal_Anthracite", rot_euler=(1.5708, 0.0, 0.0))
    b.add_box_center(cx, cy + 0.16, z0 + 1.95, 1.40, 0.05, 0.16, "Mat_Arch_Hopital_Teal")
    b.add_box_center(cx, cy - 0.85, z0 + 0.45, 0.72, 2.20, 0.82, "Mat_Arch_Ceramique_SDB", collider=True)
    b.add_box_center(cx, cy - 0.85, z0 + 0.90, 0.64, 2.10, 0.08, "Mat_Arch_Hopital_Teal")


def add_pharmacy_shelving(b, cx, cy, z0, length=3.2, axis="X"):
    """Add multi-tier hospital pharmacy / medical lab shelving."""
    sx, sy = (length, 0.50) if axis == "X" else (0.50, length)
    b.add_box_center(cx, cy, z0 + 1.10, sx, sy, 2.20, "Mat_Arch_Ceramique_SDB", collider=True)
    for sz in (0.45, 0.95, 1.45, 1.95):
        b.add_box_center(cx, cy, z0 + sz, sx + 0.03, sy + 0.03, 0.05, "Mat_Arch_Hopital_Teal")


def add_church_pew(b, cx, cy, z0, length=4.6, axis="X"):
    """Add a wooden church pew bench with backrest and kneeler."""
    sx, sy = (length, 0.55) if axis == "X" else (0.55, length)
    b.add_box_center(cx, cy, z0 + 0.45, sx, sy, 0.07, "Mat_Arch_Bois_Interieur", collider=True)
    if axis == "X":
        b.add_box_center(cx, cy - 0.24, z0 + 0.75, sx, 0.07, 0.58, "Mat_Arch_Bois_Interieur")
        for lx in (-sx * 0.46, 0.0, sx * 0.46):
            b.add_box_center(cx + lx, cy, z0 + 0.22, 0.08, sy, 0.44, "Mat_Arch_Bois_Interieur")
    else:
        b.add_box_center(cx - 0.24, cy, z0 + 0.75, 0.07, sy, 0.58, "Mat_Arch_Bois_Interieur")
        for ly in (-sy * 0.46, 0.0, sy * 0.46):
            b.add_box_center(cx, cy + ly, z0 + 0.22, sx, 0.08, 0.44, "Mat_Arch_Bois_Interieur")


def add_altar_and_sanctuary(b, cx, cy, z0):
    """Add raised sanctuary platform, marble altar, ambo lectern, and tall gold cross."""
    for s in range(3):
        b.add_box_center(cx, cy + 0.8 - s * 0.3, z0 + (s + 1) * 0.16 * 0.5, 11.0 - s * 1.2, 6.4 - s * 0.8, (s + 1) * 0.16, "Mat_Arch_Sol_Marbre", collider=True)
    z_top = z0 + 0.48
    b.add_box_center(cx, cy, z_top + 0.52, 3.2, 1.35, 1.04, "Mat_Arch_Sol_Marbre", collider=True)
    b.add_box_center(cx, cy - 0.69, z_top + 0.52, 1.2, 0.04, 0.55, "Mat_Arch_Or_Ceremonie")
    b.add_box_center(cx, cy, z_top + 1.06, 3.35, 1.45, 0.06, "Mat_Arch_Tapis_Rouge")
    add_podium_lectern(b, cx - 3.2, cy - 1.2, z_top, facing="-Y")
    add_cross_emblem(b, cx, cy + 2.6, z_top + 0.8, width=2.4, height=4.4, thickness=0.24, axis="X", mat_name="Mat_Arch_Or_Ceremonie")


def add_ablution_station(b, cx, cy, z0, length=4.8, axis="X", stations=5):
    """Add a tiled wudu ablution bench, drainage trough, and stainless faucets."""
    sx, sy = (length, 0.95) if axis == "X" else (0.95, length)
    b.add_box_center(cx, cy, z0 + 0.22, sx, sy * 0.45, 0.44, "Mat_Arch_Parement_Pierre", collider=True)
    off = 0.42
    if axis == "X":
        b.add_box_center(cx, cy + off, z0 + 0.55, sx, 0.18, 1.10, "Mat_Arch_Sol_Carrelage", collider=True)
        for i in range(stations):
            fx = cx - length * 0.5 + length * (i + 0.5) / stations
            b.add_box_center(fx, cy + off - 0.15, z0 + 0.62, 0.05, 0.16, 0.05, "Mat_Arch_Metal_Inox")
            b.add_box_center(fx, cy - 0.28, z0 + 0.22, 0.42, 0.36, 0.45, "Mat_Arch_Sol_Marbre")
    else:
        b.add_box_center(cx + off, cy, z0 + 0.55, 0.18, sy, 1.10, "Mat_Arch_Sol_Carrelage", collider=True)
        for i in range(stations):
            fy = cy - length * 0.5 + length * (i + 0.5) / stations
            b.add_box_center(cx + off - 0.15, fy, z0 + 0.62, 0.16, 0.05, 0.05, "Mat_Arch_Metal_Inox")
            b.add_box_center(cx - 0.28, fy, z0 + 0.22, 0.36, 0.42, 0.45, "Mat_Arch_Sol_Marbre")


def add_mihrab_and_minbar(b, cx, cy, z0):
    """Add a gilded Qibla Mihrab niche frame and stepped Minbar pulpit."""
    # Mihrab frame protruding slightly from +Y Qibla wall
    b.add_box_center(cx - 1.35, cy - 0.25, z0 + 2.2, 0.45, 0.50, 4.4, "Mat_Arch_Or_Ceremonie", collider=True)
    b.add_box_center(cx + 1.35, cy - 0.25, z0 + 2.2, 0.45, 0.50, 4.4, "Mat_Arch_Or_Ceremonie", collider=True)
    b.add_box_center(cx, cy - 0.25, z0 + 4.3, 3.15, 0.50, 0.70, "Mat_Arch_Or_Ceremonie", collider=True)
    b.add_box_center(cx, cy - 0.06, z0 + 2.0, 2.25, 0.12, 3.8, "Mat_Arch_Mosquee_Bleu")
    # Stepped Minbar pulpit to the right (+X)
    mx = cx + 3.4
    for s in range(6):
        sh = (s + 1) * 0.34
        b.add_box_center(mx, cy - 2.4 + s * 0.42, z0 + sh * 0.5, 1.20, 0.42, sh, "Mat_Arch_Bois_Interieur", collider=True)
    b.add_box_center(mx, cy - 0.25, z0 + 3.10, 1.30, 1.10, 0.20, "Mat_Arch_Or_Ceremonie")


def render_monument_previews(output_dir, prefix, cam_specs, interior_point_lights=None):
    """Render exterior, cutaway, and interior previews in Blender 5.3 headless mode.

    cam_specs: list of (tag, loc, target, hide_names_list_or_None)
    """
    os.makedirs(output_dir, exist_ok=True)
    scene = bpy.context.scene

    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue

    if hasattr(scene, "eevee") and hasattr(scene.eevee, "taa_render_samples"):
        scene.eevee.taa_render_samples = 16

    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"

    world = scene.world
    if world is None:
        world = bpy.data.worlds.new("MonumentPreviewWorld")
        scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.58, 0.72, 0.88, 1.0)
        bg.inputs["Strength"].default_value = 0.85

    sun_data = bpy.data.lights.new("MonumentPreviewSun", type="SUN")
    sun_data.energy = 3.2
    sun_obj = bpy.data.objects.new("MonumentPreviewSun", sun_data)
    sun_obj.rotation_euler = (math.radians(48.0), math.radians(15.0), math.radians(-38.0))
    scene.collection.objects.link(sun_obj)

    temp_lights = [sun_obj]
    for idx, (lx, ly, lz, energy, radius) in enumerate(interior_point_lights or []):
        ldata = bpy.data.lights.new(f"TmpIntLight_{idx}", type="POINT")
        ldata.energy = energy
        ldata.shadow_soft_size = radius
        ldata.use_shadow = False
        ldata.color = (1.0, 0.95, 0.86)
        lobj = bpy.data.objects.new(f"TmpIntLight_{idx}", ldata)
        lobj.location = (lx, ly, lz)
        scene.collection.objects.link(lobj)
        temp_lights.append(lobj)

    cam_data = bpy.data.cameras.new("MonumentPreviewCam")
    cam_data.lens = 24.0
    cam_data.clip_start = 0.15
    cam_data.clip_end = 1500.0
    cam_obj = bpy.data.objects.new("MonumentPreviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    for tag, loc, target, hide_names in cam_specs:
        hidden_restore = []
        for obj_name in hide_names or []:
            obj = bpy.data.objects.get(obj_name)
            if obj is not None:
                hidden_restore.append((obj, obj.hide_render, obj.hide_viewport))
                obj.hide_render = True
                obj.hide_viewport = True

        cam_obj.location = Vector(loc)
        direction = Vector(target) - Vector(loc)
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(output_dir, f"{prefix}_{tag}.png")
        bpy.ops.render.render(write_still=True)

        for obj, hr, hv in hidden_restore:
            obj.hide_render = hr
            obj.hide_viewport = hv

    bpy.data.objects.remove(cam_obj, do_unlink=True)
    bpy.data.cameras.remove(cam_data)
    for lobj in temp_lights:
        ldata = lobj.data
        bpy.data.objects.remove(lobj, do_unlink=True)
        bpy.data.lights.remove(ldata)
