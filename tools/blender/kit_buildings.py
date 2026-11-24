"""Blender modeling routines for real-world Abidjan residential houses, villas,
popular shopfront buildings, Marcory apartments, and Plateau curtain-wall towers.
All buildings face Blender -Y (which maps to Unity +Z street front) with base at Z = 0."""

import math
from kit_common import (
    KitBuilder,
    MAT_ACCENT_PAINT,
    MAT_CONCRETE,
    MAT_DARK_METAL,
    MAT_FACADE_TRIM,
    MAT_FACADE_WALL,
    MAT_GALV_STEEL,
    MAT_GLASS_NIGHT,
    MAT_ROOF_METAL,
    MAT_ROOF_TILE,
    MAT_WOOD,
)


def _perimeter_fence_gate(kb, width, front_y, wall_h=0.95, gate_w=3.2):
    """Adds a real-world Abidjan low compound wall (mur de cloture) with coping caps and driveway gate."""
    half = width * 0.5
    pillar_w = 0.36
    # Left & right low masonry walls flanking the central driveway gate
    side_len = max(0.8, (width - gate_w) * 0.5 - pillar_w)
    for sx in (-1.0, 1.0):
        # Wall segment
        wx = sx * (gate_w * 0.5 + pillar_w + side_len * 0.5)
        kb.add_box_base((wx, front_y, 0.0), (side_len, 0.22, wall_h), MAT_FACADE_WALL)
        kb.add_box((wx, front_y, wall_h + 0.03), (side_len + 0.06, 0.28, 0.06), MAT_FACADE_TRIM)
        # Corner end pillar
        cx = sx * (half - 0.16)
        kb.add_box_base((cx, front_y, 0.0), (0.32, 0.32, wall_h + 0.25), MAT_FACADE_TRIM)
        kb.add_box((cx, front_y, wall_h + 0.28), (0.38, 0.38, 0.06), MAT_FACADE_TRIM)
        # Gate pillar
        gx = sx * (gate_w * 0.5 + pillar_w * 0.5)
        kb.add_box_base((gx, front_y, 0.0), (pillar_w, pillar_w, 1.65), MAT_FACADE_TRIM)
        kb.add_bevel_box((gx, front_y, 1.71), (pillar_w + 0.08, pillar_w + 0.08, 0.10), 0.03, MAT_FACADE_TRIM)
    # Slatted metal sliding gate (slightly open on one side or closed with rails)
    kb.add_box((0.0, front_y, 0.12), (gate_w, 0.06, 0.06), MAT_DARK_METAL)
    kb.add_box((0.0, front_y, 1.42), (gate_w, 0.06, 0.06), MAT_DARK_METAL)
    bars = 9
    for i in range(bars):
        bx = -gate_w * 0.45 + (gate_w * 0.90) * (i / float(bars - 1))
        kb.add_box((bx, front_y, 0.77), (0.045, 0.045, 1.28), MAT_DARK_METAL)


def build_villa_coloniale(mats):
    """Cocody pitched-roof villa (14m x 14m nominal) with colonnaded veranda, shutters, AC & compound gate."""
    kb = KitBuilder("SM_Villa_Coloniale", mats)
    # Raised stone/concrete plinth + main living volume shifted back for front garden & veranda
    plinth_h = 0.32
    kb.add_box_base((0.0, 1.2, 0.0), (11.2, 8.6, plinth_h), MAT_CONCRETE, color=(0.74, 0.72, 0.68, 1.0))
    # Main body (h=3.35m above plinth)
    kb.add_box_base((0.0, 1.5, plinth_h), (11.0, 7.6, 3.35), MAT_FACADE_WALL)
    # Side wing (living extension)
    kb.add_box_base((3.2, -0.4, plinth_h), (4.2, 3.6, 3.10), MAT_FACADE_WALL)
    # Veranda deck + 3 entrance steps at front (-Y)
    kb.add_box_base((-1.8, -2.5, 0.0), (7.2, 1.9, plinth_h), MAT_CONCRETE)
    for st in range(3):
        kb.add_box_base((-1.8, -3.55 - st * 0.34, 0.0), (2.6, 0.36, plinth_h * (1.0 - (st + 1) * 0.28)), MAT_CONCRETE)
    # 4 colonnaded veranda pillars with bases & capitals
    for px in (-4.9, -3.0, -0.6, 1.3):
        kb.add_box_base((px, -3.22, plinth_h), (0.40, 0.40, 0.18), MAT_FACADE_TRIM)
        kb.add_box_base((px, -3.22, plinth_h + 0.18), (0.28, 0.28, 2.75), MAT_FACADE_TRIM)
        kb.add_box_base((px, -3.22, plinth_h + 2.93), (0.38, 0.38, 0.14), MAT_FACADE_TRIM)
    # Veranda beam & sloped lower porch roof
    kb.add_box((-1.8, -3.22, plinth_h + 3.12), (7.4, 0.32, 0.24), MAT_FACADE_TRIM)
    kb.add_box((-1.8, -2.55, plinth_h + 3.26), (7.5, 2.15, 0.14), MAT_ROOF_TILE,
               rot_euler=(math.radians(12.0), 0.0, 0.0))

    # Recessed front door & shuttered windows
    kb.add_door_unit((-1.8, -2.30, plinth_h), (0.0, -1.0, 0.0), width=1.45, height=2.35, glazed=True, pane_id=(0, 0))
    kb.add_window_unit((-3.8, -2.30, plinth_h + 1.55), (0.0, -1.0, 0.0), 1.45, 1.35, pane_id=(1, 0), has_shutters=True)
    kb.add_window_unit((3.2, -2.20, plinth_h + 1.55), (0.0, -1.0, 0.0), 1.85, 1.40, pane_id=(2, 0), has_hood=True, mullion_cols=2)
    # Side & rear windows
    for sy in (0.4, 3.6):
        kb.add_window_unit((-5.50, sy, plinth_h + 1.55), (-1.0, 0.0, 0.0), 1.35, 1.30, pane_id=(3, 0), has_shutters=True)
        kb.add_window_unit((5.50, sy, plinth_h + 1.55), (1.0, 0.0, 0.0), 1.35, 1.30, pane_id=(4, 0), has_shutters=True)
    kb.add_window_unit((-2.4, 5.30, plinth_h + 1.55), (0.0, 1.0, 0.0), 1.60, 1.30, pane_id=(5, 0), mullion_cols=2)
    kb.add_window_unit((2.4, 5.30, plinth_h + 1.55), (0.0, 1.0, 0.0), 1.60, 1.30, pane_id=(6, 0), mullion_cols=2)

    # Main hipped terracotta roof with eaves & side wing hip roof
    kb.add_hip_roof_with_eaves((0.0, 1.5, plinth_h + 3.45), (11.0, 7.6), 2.15, overhang=0.60, mat_roof=MAT_ROOF_TILE)
    kb.add_hip_roof_with_eaves((3.2, -0.4, plinth_h + 3.18), (4.2, 3.6), 1.25, overhang=0.45, mat_roof=MAT_ROOF_TILE)
    kb.add_ac_unit((5.50, 2.0, plinth_h + 2.35), (1.0, 0.0, 0.0))

    # Front compound wall & driveway gate
    _perimeter_fence_gate(kb, 13.4, -6.65, wall_h=0.90, gate_w=3.4)
    return kb


def build_villa_duplex(mats):
    """Cocody 2-storey R+1 duplex villa (14m x 14m nominal) with upper balcony, sunshades & gate."""
    kb = KitBuilder("SM_Villa_Duplex", mats)
    plinth_h = 0.28
    # Ground floor main volume & carport porch on left
    kb.add_box_base((0.0, 1.0, 0.0), (11.4, 9.2, plinth_h), MAT_CONCRETE)
    kb.add_box_base((0.8, 1.2, plinth_h), (9.8, 8.6, 3.15), MAT_FACADE_WALL)
    # Carport/entrance roof slab on left front supported by 2 pillars
    kb.add_box_base((-4.6, -2.8, plinth_h), (0.35, 0.35, 3.05), MAT_FACADE_TRIM)
    kb.add_box_base((-4.6, 0.4, plinth_h), (0.35, 0.35, 3.05), MAT_FACADE_TRIM)
    kb.add_box((-3.2, -1.2, plinth_h + 3.10), (3.4, 4.0, 0.22), MAT_FACADE_TRIM)
    # Front steps
    for st in range(2):
        kb.add_box_base((0.8, -3.30 - st * 0.32, 0.0), (2.4, 0.34, plinth_h * (1.0 - (st + 1) * 0.35)), MAT_CONCRETE)

    # Upper floor (R+1) slightly stepped
    z2 = plinth_h + 3.15
    kb.add_box_base((0.2, 1.2, z2), (11.0, 8.4, 3.05), MAT_FACADE_WALL)
    # Belt cornice
    kb.add_box((0.2, 1.2, z2), (11.2, 8.6, 0.16), MAT_FACADE_TRIM)
    # Cantilevered upper balcony at street front
    kb.add_balcony((1.4, -3.0, z2), 5.6, 1.35)

    # Ground openings
    kb.add_door_unit((0.8, -3.10, plinth_h), (0.0, -1.0, 0.0), width=1.4, height=2.3, glazed=True, pane_id=(0, 0))
    kb.add_window_unit((3.8, -3.10, plinth_h + 1.50), (0.0, -1.0, 0.0), 2.1, 1.45, pane_id=(1, 0), has_hood=True, mullion_cols=2)
    # Upper openings (balcony sliding bay + side windows with concrete casquette hoods)
    kb.add_window_unit((1.4, -3.00, z2 + 1.35), (0.0, -1.0, 0.0), 2.8, 2.15, pane_id=(2, 1), has_sill=False, mullion_cols=3)
    kb.add_window_unit((-3.2, -3.00, z2 + 1.50), (0.0, -1.0, 0.0), 1.6, 1.35, pane_id=(3, 1), has_hood=True, mullion_cols=2)
    for sy in (-0.6, 2.8):
        kb.add_window_unit((5.70, sy, plinth_h + 1.50), (1.0, 0.0, 0.0), 1.4, 1.30, pane_id=(4, 0), has_hood=True)
        kb.add_window_unit((5.70, sy, z2 + 1.50), (1.0, 0.0, 0.0), 1.4, 1.30, pane_id=(5, 1), has_hood=True)
        kb.add_window_unit((-5.30, sy, z2 + 1.50), (-1.0, 0.0, 0.0), 1.4, 1.30, pane_id=(6, 1), has_hood=True)
    kb.add_ac_unit((5.70, 1.1, z2 + 2.25), (1.0, 0.0, 0.0))

    # Hipped tiled roof
    kb.add_hip_roof_with_eaves((0.2, 1.2, z2 + 3.15), (11.0, 8.4), 2.1, overhang=0.55, mat_roof=MAT_ROOF_TILE)
    _perimeter_fence_gate(kb, 13.4, -6.65, wall_h=0.95, gate_w=3.4)
    return kb


def build_villa_moderne(mats):
    """Contemporary flat-roof Cocody villa (14m x 14m) inspired by modern architectural villas."""
    kb = KitBuilder("SM_Villa_Moderne", mats)
    plinth_h = 0.25
    # Elevated terrace deck & lawn border
    kb.add_box_base((0.0, 0.6, 0.0), (12.2, 10.6, plinth_h), MAT_CONCRETE, color=(0.82, 0.80, 0.76, 1.0))
    # Main lower horizontal wing + taller architectural living pavilion
    kb.add_box_base((-1.6, 1.5, plinth_h), (8.6, 8.2, 3.65), MAT_FACADE_WALL)
    kb.add_box_base((3.5, 0.8, plinth_h), (4.6, 9.4, 4.25), MAT_FACADE_WALL)
    # Stone / wood architectural accent cladding pier on front facade
    kb.add_box_base((1.15, 0.55, plinth_h), (1.65, 9.6, 4.25), MAT_CONCRETE, color=(0.76, 0.72, 0.66, 1.0))
    kb.add_box_base((-5.4, -2.5, plinth_h), (1.4, 0.35, 3.55), MAT_WOOD, color=(0.48, 0.30, 0.16, 1.0))

    # Cantilevered flat roofs with dark metal parapet coping (acrotere) & wood soffits
    for (cx, cy, cz, sx, sy) in (
        (-1.6, 1.1, plinth_h + 3.65, 9.8, 9.8),
        (3.5, 0.5, plinth_h + 4.25, 5.6, 10.8),
    ):
        # Warm wood soffit under-eaves
        kb.add_box((cx, cy, cz - 0.02), (sx - 0.08, sy - 0.08, 0.05), MAT_WOOD, color=(0.52, 0.34, 0.18, 1.0))
        # White fascia slab + dark metal top coping
        kb.add_box((cx, cy, cz + 0.14), (sx, sy, 0.28), MAT_FACADE_TRIM)
        kb.add_box((cx, cy, cz + 0.30), (sx + 0.06, sy + 0.06, 0.07), MAT_DARK_METAL)

    # Timber pergola canopy over front-left outdoor lounge terrace
    kb.add_box_base((-5.6, -4.4, plinth_h), (0.14, 0.14, 3.15), MAT_DARK_METAL)
    kb.add_box_base((-1.2, -4.4, plinth_h), (0.14, 0.14, 3.15), MAT_DARK_METAL)
    kb.add_box((-3.4, -4.4, plinth_h + 3.15), (4.55, 0.14, 0.14), MAT_DARK_METAL)
    for sx in (-5.5, -1.3):
        kb.add_box((sx, -3.4, plinth_h + 3.15), (0.12, 2.1, 0.14), MAT_DARK_METAL)
    for i in range(9):
        py = -4.3 + i * 0.24
        kb.add_box((-3.4, py, plinth_h + 3.22), (4.35, 0.06, 0.10), MAT_WOOD, color=(0.56, 0.36, 0.18, 1.0))

    # Floor-to-ceiling sliding glass bays & vertical brise-soleil louvers
    kb.add_window_unit((-3.2, -2.60, plinth_h + 1.35), (0.0, -1.0, 0.0), 3.2, 2.45,
                       frame_mat=MAT_DARK_METAL, pane_id=(0, 1), has_sill=False, mullion_cols=3)
    kb.add_window_unit((3.5, -3.90, plinth_h + 1.50), (0.0, -1.0, 0.0), 3.0, 2.70,
                       frame_mat=MAT_DARK_METAL, pane_id=(1, 1), has_sill=False, mullion_cols=3)
    kb.add_door_unit((1.15, -4.25, plinth_h), (0.0, -1.0, 0.0), width=1.35, height=2.5,
                     frame_mat=MAT_DARK_METAL, door_color=(0.30, 0.18, 0.10, 1.0))
    # Vertical brise-soleil slats along west/east glass
    for i in range(5):
        lx = 2.25 + i * 0.18
        kb.add_box((lx, -4.05, plinth_h + 1.55), (0.04, 0.18, 2.75), MAT_WOOD, color=(0.48, 0.30, 0.16, 1.0))
    # Side & rear bays
    kb.add_window_unit((5.80, 0.8, plinth_h + 1.65), (1.0, 0.0, 0.0), 2.8, 2.2, frame_mat=MAT_DARK_METAL, pane_id=(2, 1), mullion_cols=2)
    kb.add_window_unit((-5.90, 1.5, plinth_h + 1.65), (-1.0, 0.0, 0.0), 2.4, 1.9, frame_mat=MAT_DARK_METAL, pane_id=(3, 1), mullion_cols=2)

    # Low front architectural planter + sleek modern wall & gate
    kb.add_box_base((-3.4, -4.7, plinth_h), (4.2, 0.55, 0.45), MAT_CONCRETE)
    _perimeter_fence_gate(kb, 13.4, -6.65, wall_h=0.90, gate_w=3.6)
    return kb


def build_maison_populaire_a(mats):
    """Abidjan popular 1-storey veranda house (8.5m x 10.5m) with corrugated gable roof & rafters."""
    kb = KitBuilder("SM_Maison_Populaire_A", mats)
    plinth_h = 0.24
    # Concrete plinth + front veranda deck + 2 steps
    kb.add_box_base((0.0, 0.2, 0.0), (7.8, 8.8, plinth_h), MAT_CONCRETE)
    kb.add_box_base((0.0, -4.45, 0.0), (2.2, 0.65, plinth_h * 0.55), MAT_CONCRETE)
    # Main house walls shifted slightly back (+1.05) leaving covered veranda (-4.2 to -2.5)
    kb.add_box_base((0.0, 1.05, plinth_h), (7.6, 6.9, 2.95), MAT_FACADE_WALL)
    # Veranda low side parapet walls (muret) & 3 concrete veranda columns
    for sx in (-1.0, 1.0):
        kb.add_box_base((sx * 2.45, -4.02, plinth_h), (2.5, 0.20, 0.88), MAT_FACADE_WALL)
        kb.add_box((sx * 2.45, -4.02, plinth_h + 0.90), (2.56, 0.24, 0.05), MAT_FACADE_TRIM)
    for px in (-3.65, -1.15, 1.15, 3.65):
        kb.add_box_base((px, -4.02, plinth_h), (0.24, 0.24, 2.95), MAT_FACADE_TRIM)
    # Veranda top lintel beam
    kb.add_box((0.0, -4.02, plinth_h + 2.86), (7.65, 0.26, 0.22), MAT_FACADE_TRIM)

    # Door + shuttered front & side windows
    kb.add_door_unit((0.0, -2.40, plinth_h), (0.0, -1.0, 0.0), width=1.05, height=2.15,
                     door_color=(0.36, 0.22, 0.12, 1.0))
    for sx in (-2.2, 2.2):
        kb.add_window_unit((sx, -2.40, plinth_h + 1.45), (0.0, -1.0, 0.0), 1.25, 1.15,
                           pane_id=(0 if sx < 0 else 1, 0), has_shutters=True)
    for sy in (-0.4, 2.4):
        kb.add_window_unit((-3.80, sy, plinth_h + 1.45), (-1.0, 0.0, 0.0), 1.15, 1.10,
                           pane_id=(2, 0), has_hood=True)
        kb.add_window_unit((3.80, sy, plinth_h + 1.45), (1.0, 0.0, 0.0), 1.15, 1.10,
                           pane_id=(3, 0), has_hood=True)

    # Corrugated metal gable roof spanning main body + veranda with overhanging eaves & fascia
    kb.add_gable_roof((0.0, 0.15, plinth_h + 2.95), (7.6, 8.7), 1.45, overhang=0.55,
                       ridge_along_x=True, mat_roof=MAT_ROOF_METAL)
    return kb


def build_maison_populaire_b(mats):
    """Abidjan street-front shop house (8.5m x 10.5m): recessed shutter bays, awning, signboard & polytank."""
    kb = KitBuilder("SM_Maison_Populaire_B", mats)
    # Main building body + concrete step along street
    kb.add_box_base((0.0, 0.4, 0.0), (8.0, 9.2, 3.25), MAT_FACADE_WALL)
    kb.add_box_base((0.0, -4.45, 0.0), (8.0, 0.55, 0.14), MAT_CONCRETE)
    # Street pilasters dividing 2 recessed shop bays
    for px in (-3.85, 0.0, 3.85):
        kb.add_box_base((px, -4.22, 0.0), (0.34, 0.24, 3.25), MAT_FACADE_TRIM)
    # Recessed roll-up shutters & open boutique door
    kb.add_box_base((-1.92, -4.20, 0.14), (3.45, 0.10, 2.40), MAT_ROOF_METAL, color=(0.24, 0.44, 0.62, 1.0))
    kb.add_door_unit((1.92, -4.20, 0.14), (0.0, -1.0, 0.0), width=1.65, height=2.35, glazed=True, pane_id=(0, 0))
    # Side windows with hoods
    for sy in (-1.2, 2.0):
        kb.add_window_unit((-4.00, sy, 1.65), (-1.0, 0.0, 0.0), 1.15, 1.10, pane_id=(1, 0), has_hood=True)
        kb.add_window_unit((4.00, sy, 1.65), (1.0, 0.0, 0.0), 1.15, 1.10, pane_id=(2, 0), has_hood=True)

    # Corrugated street awning supported by diagonal steel struts + painted shop signboard
    kb.add_box((0.0, -4.92, 2.74), (7.9, 1.45, 0.06), MAT_ROOF_METAL, color=(0.56, 0.30, 0.20, 1.0),
               rot_euler=(math.radians(-12.0), 0.0, 0.0))
    for sx in (-3.2, 0.0, 3.2):
        kb.add_pipe([(sx, -4.20, 2.25), (sx, -5.25, 2.66)], 0.025, 6, MAT_DARK_METAL)
    kb.add_box((0.0, -4.32, 3.05), (4.6, 0.14, 0.55), MAT_ACCENT_PAINT, color=(0.94, 0.72, 0.12, 1.0))

    # Flat roof slab with parapet wall (acrotere) + elevated polytank water tank
    for cx, cy, sx, sy in (
        (0.0, -4.08, 8.2, 0.24),
        (0.0, 4.88, 8.2, 0.24),
        (-3.98, 0.4, 0.24, 9.2),
        (3.98, 0.4, 0.24, 9.2),
    ):
        kb.add_box_base((cx, cy, 3.25), (sx, sy, 0.45), MAT_FACADE_WALL)
        kb.add_box((cx, cy, 3.72), (sx + 0.05, sy + 0.05, 0.06), MAT_FACADE_TRIM)
    kb.add_box((0.0, 0.4, 3.22), (8.2, 9.4, 0.14), MAT_CONCRETE)
    kb.add_polytank_stand((-1.8, 1.8, 3.29))
    return kb


def build_floor_ground_shop(mats):
    """Modular ground floor commercial arcade (h=3.3m, ref 10m x 12m) for multi-storey buildings."""
    kb = KitBuilder("SM_Floor_GroundShop", mats)
    kb.add_box_base((0.0, 0.0, 0.0), (10.0, 12.0, 3.30), MAT_FACADE_WALL)
    kb.add_box_base((0.0, -6.28, 0.0), (10.0, 0.56, 0.12), MAT_CONCRETE)
    # Front concrete pilasters separating 3 bays (2 shops + side residential entry)
    for px in (-4.82, -1.4, 2.0, 4.82):
        kb.add_box_base((px, -6.02, 0.0), (0.36, 0.24, 3.30), MAT_FACADE_TRIM)
    # 2 shop shutter/glass bays + residential entry door
    kb.add_box_base((-3.11, -6.00, 0.12), (3.00, 0.10, 2.55), MAT_ROOF_METAL, color=(0.22, 0.42, 0.62, 1.0))
    kb.add_window_unit((0.30, -6.00, 1.42), (0.0, -1.0, 0.0), 2.80, 2.40, frame_mat=MAT_DARK_METAL,
                       pane_id=(0, 0), has_sill=False, mullion_cols=2)
    kb.add_door_unit((3.40, -6.00, 0.12), (0.0, -1.0, 0.0), width=1.30, height=2.45,
                     frame_mat=MAT_DARK_METAL, door_color=(0.30, 0.20, 0.14, 1.0))
    # Shopfront awning + tie-rods + signboard
    kb.add_box((-1.4, -6.78, 2.86), (6.9, 1.50, 0.06), MAT_ACCENT_PAINT, color=(0.75, 0.22, 0.16, 1.0),
               rot_euler=(math.radians(-10.0), 0.0, 0.0))
    for sx in (-4.5, -1.4, 1.7):
        kb.add_pipe([(sx, -6.00, 2.35), (sx, -7.15, 2.77)], 0.024, 6, MAT_DARK_METAL)
    kb.add_box((-1.4, -6.12, 3.08), (4.8, 0.14, 0.48), MAT_ACCENT_PAINT, color=(0.95, 0.78, 0.14, 1.0))
    # Side windows
    for sy in (-2.5, 2.5):
        kb.add_window_unit((-5.00, sy, 1.75), (-1.0, 0.0, 0.0), 1.4, 1.25, pane_id=(1, 0), has_hood=True)
        kb.add_window_unit((5.00, sy, 1.75), (1.0, 0.0, 0.0), 1.4, 1.25, pane_id=(2, 0), has_hood=True)
    return kb


def build_floor_ground_res(mats):
    """Modular Marcory residential ground floor (h=3.2m, ref 12m x 14.5m) with portico lobby."""
    kb = KitBuilder("SM_Floor_GroundRes", mats)
    kb.add_box_base((0.0, 0.0, 0.0), (12.0, 14.5, 0.30), MAT_CONCRETE)
    kb.add_box_base((0.0, 0.0, 0.30), (12.0, 14.5, 2.90), MAT_FACADE_WALL)
    # Projecting entrance portico + canopy hood + steps
    for sx in (-1.45, 1.45):
        kb.add_box_base((sx, -7.85, 0.0), (0.36, 0.36, 2.85), MAT_FACADE_TRIM)
    kb.add_box((0.0, -7.65, 2.88), (3.6, 1.4, 0.22), MAT_FACADE_TRIM)
    kb.add_box_base((0.0, -7.65, 0.0), (3.2, 1.2, 0.28), MAT_CONCRETE)
    kb.add_door_unit((0.0, -7.25, 0.30), (0.0, -1.0, 0.0), width=1.8, height=2.45, frame_mat=MAT_DARK_METAL, glazed=True, pane_id=(0, 0))
    # Flanking residential windows with sills & hoods
    for sx in (-3.8, 3.8):
        kb.add_window_unit((sx, -7.25, 1.75), (0.0, -1.0, 0.0), 2.1, 1.4, pane_id=(1 if sx < 0 else 2, 0), has_hood=True, mullion_cols=2)
    for sy in (-3.2, 0.6, 4.2):
        kb.add_window_unit((-6.00, sy, 1.75), (-1.0, 0.0, 0.0), 1.6, 1.3, pane_id=(3, 0), has_hood=True)
        kb.add_window_unit((6.00, sy, 1.75), (1.0, 0.0, 0.0), 1.6, 1.3, pane_id=(4, 0), has_hood=True)
    return kb


def build_floor_mid_popular(mats):
    """Modular upper floor for popular districts (h=3.0m, ref 10m x 12m) with balcony & casquette hoods."""
    kb = KitBuilder("SM_Floor_MidPopular", mats)
    kb.add_box_base((0.0, 0.0, 0.0), (10.0, 12.0, 3.0), MAT_FACADE_WALL, skip_bottom=True)
    # Concrete floor belt course
    kb.add_box((0.0, 0.0, 0.06), (10.16, 12.16, 0.14), MAT_FACADE_TRIM)
    # Front balcony + sliding doors + flanking shuttered windows
    kb.add_balcony((-1.2, -6.0, 0.08), 5.0, 1.05)
    kb.add_window_unit((-1.2, -6.00, 1.35), (0.0, -1.0, 0.0), 2.2, 2.05, pane_id=(0, 1), has_sill=False, mullion_cols=2)
    kb.add_window_unit((2.9, -6.00, 1.55), (0.0, -1.0, 0.0), 1.5, 1.3, pane_id=(1, 1), has_hood=True, has_shutters=True)
    # Side & rear windows
    for sy in (-2.8, 1.2, 4.2):
        kb.add_window_unit((-5.00, sy, 1.55), (-1.0, 0.0, 0.0), 1.35, 1.25, pane_id=(2, 1), has_hood=True)
        kb.add_window_unit((5.00, sy, 1.55), (1.0, 0.0, 0.0), 1.35, 1.25, pane_id=(3, 1), has_hood=True)
    for sx in (-2.6, 2.6):
        kb.add_window_unit((sx, 6.00, 1.55), (0.0, 1.0, 0.0), 1.4, 1.25, pane_id=(4, 1), has_hood=True)
    return kb


def build_floor_mid_marcory(mats):
    """Modular upper floor for Marcory apartments (h=3.0m, ref 12m x 14.5m) with twin balconies & AC."""
    kb = KitBuilder("SM_Floor_MidMarcory", mats)
    kb.add_box_base((0.0, 0.0, 0.0), (12.0, 14.5, 3.0), MAT_FACADE_WALL, skip_bottom=True)
    kb.add_box((0.0, 0.0, 0.07), (12.22, 14.72, 0.16), MAT_FACADE_TRIM)
    # Twin front balconies with sliding glass doors + central stairwell glazed strip
    for sx in (-3.6, 3.6):
        kb.add_balcony((sx, -7.25, 0.08), 4.0, 1.15)
        kb.add_window_unit((sx, -7.25, 1.35), (0.0, -1.0, 0.0), 2.3, 2.1, frame_mat=MAT_DARK_METAL,
                           pane_id=(0 if sx < 0 else 1, 1), has_sill=False, mullion_cols=2)
    kb.add_window_unit((0.0, -7.25, 1.50), (0.0, -1.0, 0.0), 1.3, 2.2, frame_mat=MAT_DARK_METAL,
                       pane_id=(2, 1), has_sill=False)
    for sy in (-3.5, 0.5, 4.5):
        kb.add_window_unit((-6.00, sy, 1.55), (-1.0, 0.0, 0.0), 1.7, 1.35, pane_id=(3, 1), has_hood=True, mullion_cols=2)
        kb.add_window_unit((6.00, sy, 1.55), (1.0, 0.0, 0.0), 1.7, 1.35, pane_id=(4, 1), has_hood=True, mullion_cols=2)
    for sx in (-3.2, 3.2):
        kb.add_window_unit((sx, 7.25, 1.55), (0.0, 1.0, 0.0), 1.7, 1.35, pane_id=(5, 1), has_hood=True, mullion_cols=2)
    kb.add_ac_unit((6.00, -1.6, 2.15), (1.0, 0.0, 0.0))
    return kb


def build_roof_parapet(mats):
    """Flat roof slab + perimeter parapet coping (acrotere), stairwell bulkhead & elevated polytank."""
    kb = KitBuilder("SM_Roof_Parapet", mats)
    wx, wy = 10.0, 12.0
    # Overhanging concrete roof cornice slab
    kb.add_box((0.0, 0.0, 0.10), (wx + 0.45, wy + 0.45, 0.22), MAT_FACADE_TRIM)
    # 4 perimeter parapet walls + coping caps
    p_h = 0.55
    for cx, cy, sx, sy in (
        (0.0, -wy * 0.5, wx + 0.2, 0.24),
        (0.0, wy * 0.5, wx + 0.2, 0.24),
        (-wx * 0.5, 0.0, 0.24, wy + 0.2),
        (wx * 0.5, 0.0, 0.24, wy + 0.2),
    ):
        kb.add_box_base((cx, cy, 0.21), (sx, sy, p_h), MAT_FACADE_WALL)
        kb.add_box((cx, cy, 0.21 + p_h + 0.03), (sx + 0.06, sy + 0.06, 0.07), MAT_FACADE_TRIM)
    # Roof stairwell access bulkhead (cage d'escalier) + elevated polytank
    kb.add_box_base((2.1, 2.6, 0.21), (2.4, 2.8, 2.15), MAT_FACADE_WALL)
    kb.add_box((2.1, 2.6, 2.40), (2.65, 3.05, 0.14), MAT_FACADE_TRIM)
    kb.add_door_unit((2.1, 1.20, 0.21), (0.0, -1.0, 0.0), width=0.95, height=1.95, door_mat=MAT_DARK_METAL)
    kb.add_polytank_stand((-2.2, 2.2, 0.21))
    return kb


def build_roof_hip_metal(mats):
    """Overhanging corrugated metal hip roof cap (ref 10m x 12m)."""
    kb = KitBuilder("SM_Roof_HipMetal", mats)
    kb.add_box((0.0, 0.0, 0.08), (10.3, 12.3, 0.16), MAT_FACADE_TRIM)
    kb.add_hip_roof_with_eaves((0.0, 0.0, 0.18), (10.0, 12.0), 1.45, overhang=0.50, mat_roof=MAT_ROOF_METAL)
    return kb


def build_roof_hip_tile(mats):
    """Overhanging terracotta tile hip roof cap (ref 12m x 14.5m)."""
    kb = KitBuilder("SM_Roof_HipTile", mats)
    kb.add_box((0.0, 0.0, 0.08), (12.3, 14.8, 0.16), MAT_FACADE_TRIM)
    kb.add_hip_roof_with_eaves((0.0, 0.0, 0.18), (12.0, 14.5), 1.85, overhang=0.55, mat_roof=MAT_ROOF_TILE)
    return kb


def build_tower_podium(mats):
    """Plateau tower double-height colonnade podium (h=6.0m, ref 14.5m x 14.5m)."""
    kb = KitBuilder("SM_Tower_Podium", mats)
    w, d, h = 14.5, 14.5, 6.0
    # Granite podium plinth + dark structural core + 4 recessed triple-bay curtain-wall glazing faces
    kb.add_box_base((0.0, 0.0, 0.0), (w, d, 0.30), MAT_CONCRETE)
    cw, cd = w - 2.0, d - 2.0
    kb.add_box_base((0.0, 0.0, 0.30), (cw, cd, h - 0.30), MAT_DARK_METAL, skip_top=True)
    gy0, gy1 = 0.34, h - 0.50
    gx = w * 0.5 - 0.90
    gz = d * 0.5 - 0.90
    for bay in range(3):
        b0 = -gx + bay * (2.0 * gx / 3.0) + 0.06
        b1 = -gx + (bay + 1) * (2.0 * gx / 3.0) - 0.06
        uv1 = [(bay + 0.08, 0.08), (bay + 0.92, 0.08), (bay + 0.92, 0.92), (bay + 0.08, 0.92)]
        kb.add_quad((b0, -gz, gy0), (b1, -gz, gy0), (b1, -gz, gy1), (b0, -gz, gy1), MAT_GLASS_NIGHT, uv1_rect=uv1, expected_normal=(0, -1, 0))
        kb.add_quad((b1, gz, gy0), (b0, gz, gy0), (b0, gz, gy1), (b1, gz, gy1), MAT_GLASS_NIGHT, uv1_rect=uv1, expected_normal=(0, 1, 0))
        kb.add_quad((-gx, b1, gy0), (-gx, b0, gy0), (-gx, b0, gy1), (-gx, b1, gy1), MAT_GLASS_NIGHT, uv1_rect=uv1, expected_normal=(-1, 0, 0))
        kb.add_quad((gx, b0, gy0), (gx, b1, gy0), (gx, b1, gy1), (gx, b0, gy1), MAT_GLASS_NIGHT, uv1_rect=uv1, expected_normal=(1, 0, 0))
    # 4 corner & perimeter colonnade structural columns
    for sx in (-6.7, -2.3, 2.3, 6.7):
        for sy in (-6.7, 6.7):
            kb.add_box_base((sx, sy, 0.30), (0.85, 0.85, h - 0.30), MAT_FACADE_TRIM)
    for sy in (-2.3, 2.3):
        for sx in (-6.7, 6.7):
            kb.add_box_base((sx, sy, 0.30), (0.85, 0.85, h - 0.30), MAT_FACADE_TRIM)
    # Top podium cornice slab + grand cantilevered aluminum entrance canopy
    kb.add_box((0.0, 0.0, h - 0.25), (w + 0.3, d + 0.3, 0.50), MAT_FACADE_TRIM)
    kb.add_box((0.0, -7.55, 3.60), (6.4, 2.4, 0.22), MAT_DARK_METAL)
    kb.add_box((0.0, -7.55, 3.74), (6.5, 2.5, 0.08), MAT_GALV_STEEL)
    kb.add_box_base((0.0, -7.55, 0.0), (5.8, 1.8, 0.28), MAT_CONCRETE)
    # Curtain mullions on recessed lobby glazing
    for mx in (-4.4, 0.0, 4.4):
        kb.add_box_base((mx, -6.38, 0.30), (0.14, 0.14, h - 0.60), MAT_DARK_METAL)
        kb.add_box_base((mx, 6.38, 0.30), (0.14, 0.14, h - 0.60), MAT_DARK_METAL)
    return kb


def build_tower_floor2(mats):
    """Plateau tower 2-storey curtain-wall block (h=6.0m, ref 14.5m x 14.5m) with 3D mullions & spandrels."""
    kb = KitBuilder("SM_Tower_Floor2", mats)
    w, d, h = 14.5, 14.5, 6.0
    # Solid recessed structural core behind curtain glass (keeps manifold & blocks view-through)
    kb.add_box_base((0.0, 0.0, 0.0), (w - 0.90, d - 0.90, h), MAT_DARK_METAL)
    # Corner concrete/stone pilasters
    for sx in (-6.65, 6.65):
        for sy in (-6.65, 6.65):
            kb.add_box_base((sx, sy, 0.0), (1.20, 1.20, h), MAT_FACADE_WALL)
    # Recessed curtain-wall glass panels proud of core (-d*0.47 -> -6.815, core is at -6.80)
    core_w = w - 0.50
    for tier in range(2):
        z0 = tier * 3.0 + 0.35
        z1 = z0 + 2.35
        for bay in range(3):
            b0 = -core_w * 0.44 + bay * (core_w * 0.88 / 3.0)
            b1 = b0 + (core_w * 0.88 / 3.0) - 0.08
            uv1 = [(bay + 0.08, tier + 0.08), (bay + 0.92, tier + 0.08), (bay + 0.92, tier + 0.92), (bay + 0.08, tier + 0.92)]
            # Front (-Y): CCW from outside = (b0,z0)->(b1,z0)->(b1,z1)->(b0,z1)
            kb.add_quad((b0, -d * 0.475, z0), (b1, -d * 0.475, z0), (b1, -d * 0.475, z1), (b0, -d * 0.475, z1),
                        MAT_GLASS_NIGHT, uv1_rect=uv1)
            # Back (+Y): CCW from outside = (b1,z0)->(b0,z0)->(b0,z1)->(b1,z1)
            kb.add_quad((b1, d * 0.475, z0), (b0, d * 0.475, z0), (b0, d * 0.475, z1), (b1, d * 0.475, z1),
                        MAT_GLASS_NIGHT, uv1_rect=uv1)
            # Left (-X): CCW from outside = (b1,z0)->(b0,z0)->(b0,z1)->(b1,z1)
            kb.add_quad((-w * 0.475, b1, z0), (-w * 0.475, b0, z0), (-w * 0.475, b0, z1), (-w * 0.475, b1, z1),
                        MAT_GLASS_NIGHT, uv1_rect=uv1)
            # Right (+X): CCW from outside = (b0,z0)->(b1,z0)->(b1,z1)->(b0,z1)
            kb.add_quad((w * 0.475, b0, z0), (w * 0.475, b1, z0), (w * 0.475, b1, z1), (w * 0.475, b0, z1),
                        MAT_GLASS_NIGHT, uv1_rect=uv1)

    # Protruding horizontal spandrel beams (at floor 0 & floor 1) + vertical aluminum mullions
    for tz in (0.18, 3.02):
        kb.add_box((0.0, 0.0, tz), (w - 0.25, d - 0.25, 0.42), MAT_FACADE_WALL)
    for mp in (-4.4, -2.2, 0.0, 2.2, 4.4):
        for sy in (-d * 0.485, d * 0.485):
            kb.add_box_base((mp, sy, 0.0), (0.16, 0.26, h), MAT_GALV_STEEL)
        for sx in (-w * 0.485, w * 0.485):
            kb.add_box_base((sx, mp, 0.0), (0.26, 0.16, h), MAT_GALV_STEEL)
    return kb


def build_tower_crown(mats):
    """Plateau tower architectural setback crown, HVAC chillers, coping & spire mast."""
    kb = KitBuilder("SM_Tower_Crown", mats)
    w, d = 14.5, 14.5
    # Main tower roof parapet & cornice
    kb.add_box((0.0, 0.0, 0.22), (w + 0.25, d + 0.25, 0.44), MAT_FACADE_TRIM)
    # Setback mechanical penthouse
    pw, pd, ph = w * 0.68, d * 0.68, 4.2
    kb.add_box_base((0.0, 0.0, 0.44), (pw, pd, ph), MAT_FACADE_WALL)
    # Louvered dark acoustic screens on 4 penthouse sides
    kb.add_box((0.0, -pd * 0.5 - 0.03, 2.6), (pw * 0.65, 0.08, 2.0), MAT_DARK_METAL)
    kb.add_box((0.0, pd * 0.5 + 0.03, 2.6), (pw * 0.65, 0.08, 2.0), MAT_DARK_METAL)
    kb.add_box((-pw * 0.5 - 0.03, 0.0, 2.6), (0.08, pd * 0.65, 2.0), MAT_DARK_METAL)
    kb.add_box((pw * 0.5 + 0.03, 0.0, 2.6), (0.08, pd * 0.65, 2.0), MAT_DARK_METAL)
    # Architectural crown fins + coping
    kb.add_box((0.0, 0.0, 0.44 + ph + 0.14), (pw + 0.4, pd + 0.4, 0.28), MAT_FACADE_TRIM)
    # HVAC chiller boxes + slender spire
    kb.add_box_base((-1.8, -1.2, 0.44 + ph + 0.28), (2.2, 1.4, 1.1), MAT_GALV_STEEL)
    kb.add_box_base((1.8, 1.2, 0.44 + ph + 0.28), (2.2, 1.4, 1.1), MAT_GALV_STEEL)
    kb.add_cylinder((0.0, 0.0, 0.44 + ph + 0.28), 0.22, 0.05, 6.5, 8, MAT_GALV_STEEL)
    return kb


ALL_BUILDING_BUILDERS = [
    build_villa_coloniale,
    build_villa_duplex,
    build_villa_moderne,
    build_maison_populaire_a,
    build_maison_populaire_b,
    build_floor_ground_shop,
    build_floor_ground_res,
    build_floor_mid_popular,
    build_floor_mid_marcory,
    build_roof_parapet,
    build_roof_hip_metal,
    build_roof_hip_tile,
    build_tower_podium,
    build_tower_floor2,
    build_tower_crown,
]
