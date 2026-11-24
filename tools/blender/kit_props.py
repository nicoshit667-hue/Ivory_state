"""Blender modeling routines for focal street structures & entrance gates:
JobBoard (Depot Express), FoodKiosk, PropertyGate, HospitalGate."""

import math
from kit_common import (
    KitBuilder,
    MAT_ACCENT_PAINT,
    MAT_CONCRETE,
    MAT_DARK_METAL,
    MAT_FACADE_TRIM,
    MAT_FACADE_WALL,
    MAT_GALV_STEEL,
    MAT_ROOF_METAL,
    MAT_WOOD,
)


def build_job_board(mats):
    """Depot Express urban noticeboard with concrete footings, steel posts, shelter roof & pinned flyers."""
    kb = KitBuilder("SM_JobBoard", mats)
    # Concrete footing blocks + steel base flanges
    for px in (-1.28, 1.28):
        kb.add_bevel_box((px, 0.0, 0.11), (0.34, 0.34, 0.22), 0.02, MAT_CONCRETE)
        kb.add_cylinder((px, 0.0, 0.22), 0.06, 0.06, 2.38, 8, MAT_DARK_METAL)
    # Framed noticeboard cabinet (double-sided)
    kb.add_bevel_box((0.0, 0.0, 1.56), (2.48, 0.12, 1.32), 0.015, MAT_ACCENT_PAINT, color=(0.18, 0.42, 0.88, 1.0))
    # Outer metallic perimeter frame + orange Express header bar
    kb.add_box((0.0, 0.0, 2.24), (2.68, 0.18, 0.10), MAT_DARK_METAL)
    kb.add_box((0.0, 0.0, 0.88), (2.68, 0.18, 0.09), MAT_DARK_METAL)
    for sx in (-1.28, 1.28):
        kb.add_box((sx, 0.0, 1.56), (0.10, 0.18, 1.44), MAT_DARK_METAL)
    kb.add_box((0.0, 0.0, 2.39), (2.25, 0.16, 0.22), MAT_ACCENT_PAINT, color=(0.95, 0.56, 0.10, 1.0))
    # Protective sloped corrugated metal shelter canopy overhead
    kb.add_box((0.0, -0.05, 2.58), (2.92, 0.72, 0.06), MAT_ROOF_METAL, rot_euler=(math.radians(-8.0), 0.0, 0.0))
    # Pinned job sheets on front & back
    sheets = [
        (-0.85, 1.80, (0.96, 0.94, 0.88, 1.0)),
        (-0.30, 1.38, (0.98, 0.88, 0.42, 1.0)),
        (0.25, 1.78, (0.96, 0.94, 0.88, 1.0)),
        (0.80, 1.40, (0.98, 0.88, 0.42, 1.0)),
        (-0.60, 1.32, (0.96, 0.94, 0.88, 1.0)),
        (0.55, 1.82, (0.96, 0.94, 0.88, 1.0)),
    ]
    for fx, fz, col in sheets:
        kb.add_box((fx, -0.068, fz), (0.28, 0.015, 0.36), MAT_ACCENT_PAINT, color=col)
        kb.add_box((-fx, 0.068, fz), (0.28, 0.015, 0.36), MAT_ACCENT_PAINT, color=col)
    return kb


def build_food_kiosk(mats):
    """Abidjan roadside food stall / kiosk (origin at counter center, front faces -Y)."""
    kb = KitBuilder("SM_FoodKiosk", mats)
    # Wooden counter base + overhanging serving plank
    kb.add_box_base((0.0, 0.0, 0.0), (2.35, 0.72, 0.96), MAT_WOOD, color=(0.45, 0.29, 0.16, 1.0))
    kb.add_bevel_box((0.0, -0.04, 1.00), (2.48, 0.86, 0.07), 0.015, MAT_WOOD, color=(0.56, 0.38, 0.22, 1.0))
    # Rear half-wall partition
    kb.add_box_base((0.0, 1.05, 0.0), (2.35, 0.12, 2.30), MAT_WOOD, color=(0.58, 0.46, 0.32, 1.0))
    # 4 corner steel posts with diagonal knee-braces
    for px in (-1.18, 1.18):
        for py in (-0.36, 1.05):
            kb.add_box_base((px, py, 0.0), (0.07, 0.07, 2.45 if py > 0 else 2.22), MAT_DARK_METAL)
        # Side diagonal roof brace
        kb.add_pipe([(px, -0.36, 1.82), (px, 0.15, 2.25)], 0.02, 6, MAT_DARK_METAL)
    # Sloped striped/painted corrugated awning roof (tilted down toward -Y front)
    kb.add_box((0.0, 0.25, 2.38), (2.82, 2.15, 0.06), MAT_ACCENT_PAINT, color=(0.88, 0.26, 0.14, 1.0),
               rot_euler=(math.radians(9.0), 0.0, 0.0))
    # Front menu fascia board attached cleanly under front eave
    kb.add_box((0.0, -0.78, 2.12), (2.42, 0.05, 0.24), MAT_ACCENT_PAINT, color=(0.95, 0.72, 0.14, 1.0))
    return kb


def build_property_gate(mats):
    """Architectural entrance archway & wrought-iron gate for apartments & villas (front -Y)."""
    kb = KitBuilder("SM_PropertyGate", mats)
    # Left & right stepped masonry pillars with pyramidal capstones
    for sx in (-1.68, 1.68):
        kb.add_bevel_box((sx, 0.0, 0.20), (0.62, 0.62, 0.40), 0.03, MAT_CONCRETE)
        kb.add_box_base((sx, 0.0, 0.40), (0.44, 0.44, 2.65), MAT_FACADE_WALL)
        kb.add_bevel_box((sx, 0.0, 3.12), (0.56, 0.56, 0.16), 0.04, MAT_FACADE_TRIM)
    # Overhead arch beam + framed signboard header
    kb.add_bevel_box((0.0, 0.0, 3.28), (3.95, 0.54, 0.34), 0.03, MAT_FACADE_TRIM)
    kb.add_box((0.0, -0.22, 3.68), (2.85, 0.10, 0.54), MAT_ACCENT_PAINT, color=(0.95, 0.74, 0.18, 1.0))
    # Wrought-iron double gate leaves slightly open inward (+Y) so players walk smoothly through center
    for sx in (-1.0, 1.0):
        hinge_x = sx * 1.44
        leaf_len = 0.85
        # Angled slightly inward (26 deg) so both wrought-iron leaves are clearly visible
        ang = math.radians(sx * -26.0)
        cx = hinge_x - sx * math.cos(ang) * (leaf_len * 0.5)
        cy = math.sin(abs(ang)) * (leaf_len * 0.5)
        kb.add_box((cx, cy, 0.28), (leaf_len, 0.05, 0.05), MAT_DARK_METAL, rot_euler=(0, 0, ang))
        kb.add_box((cx, cy, 1.72), (leaf_len, 0.05, 0.05), MAT_DARK_METAL, rot_euler=(0, 0, ang))
        for k in range(5):
            t = (k / 4.0) - 0.5
            px = cx + math.cos(ang) * t * (leaf_len * 0.85)
            py = cy + math.sin(ang) * t * (leaf_len * 0.85)
            kb.add_box((px, py, 1.00), (0.035, 0.035, 1.48), MAT_DARK_METAL, rot_euler=(0, 0, ang))
    return kb


def build_hospital_gate(mats):
    """CHU Hospital entrance portico with ambulance canopy & 3D red cross emblem (front -Y)."""
    kb = KitBuilder("SM_HospitalGate", mats)
    for sx in (-1.85, 1.85):
        kb.add_bevel_box((sx, 0.0, 0.24), (0.72, 0.72, 0.48), 0.03, MAT_DARK_METAL, color=(0.24, 0.36, 0.42, 1.0))
        kb.add_box_base((sx, 0.0, 0.48), (0.50, 0.50, 3.10), MAT_FACADE_TRIM, color=(0.95, 0.96, 0.96, 1.0))
    # Canopy + header cornice
    kb.add_bevel_box((0.0, -0.35, 3.38), (4.55, 1.55, 0.16), 0.02, MAT_DARK_METAL, color=(0.24, 0.36, 0.42, 1.0))
    kb.add_bevel_box((0.0, 0.0, 3.66), (4.45, 0.62, 0.48), 0.03, MAT_FACADE_TRIM, color=(0.95, 0.96, 0.96, 1.0))
    # Back panel & 3D red cross emblem
    kb.add_bevel_box((0.0, -0.10, 4.35), (1.42, 0.16, 1.42), 0.02, MAT_FACADE_TRIM, color=(0.95, 0.96, 0.96, 1.0))
    kb.add_box((0.0, -0.20, 4.35), (0.34, 0.08, 1.05), MAT_ACCENT_PAINT, color=(0.86, 0.08, 0.08, 1.0))
    kb.add_box((0.0, -0.20, 4.35), (1.05, 0.08, 0.34), MAT_ACCENT_PAINT, color=(0.86, 0.08, 0.08, 1.0))
    return kb


ALL_PROP_BUILDERS = [
    build_job_board,
    build_food_kiosk,
    build_property_gate,
    build_hospital_gate,
]
