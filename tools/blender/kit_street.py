"""Blender modeling routines for real-world street lighting, power poles, traffic signals,
road infrastructure, storm drains, bus shelters, guardrails, and bridge structures."""

import math
from kit_common import (
    KitBuilder,
    MAT_ACCENT_PAINT,
    MAT_CONCRETE,
    MAT_DARK_METAL,
    MAT_GALV_STEEL,
    MAT_LAMP_LED,
    MAT_LAMP_SODIUM,
    MAT_ROOF_METAL,
    MAT_WOOD,
)


def _cobra_led_head(kb, center_xyz, yaw_deg=0.0):
    """Aerodynamic die-cast aluminum LED cobra-head luminaire oriented toward -Y (or rotated by yaw_deg)."""
    cx, cy, cz = center_xyz
    ry = math.radians(yaw_deg)
    dx, dy = math.sin(ry), -math.cos(ry)  # facing vector when yaw_deg=0 is (0, -1)
    # Main tapered luminaire shell (approx 0.38m wide x 0.86m long x 0.13m tall)
    hx = cx + dx * 0.25
    hy = cy + dy * 0.25
    kb.add_bevel_box((hx, hy, cz + 0.03), (0.36, 0.78, 0.11), 0.025, MAT_GALV_STEEL,
                     rot_euler=(0.0, 0.0, ry), segments=2)
    # Upper cooling fins
    for off in (-0.10, 0.0, 0.10):
        fx = cx + dx * 0.14 + math.cos(ry) * off
        fy = cy + dy * 0.14 + math.sin(ry) * off
        kb.add_box((fx, fy, cz + 0.09), (0.025, 0.42, 0.035), MAT_DARK_METAL, rot_euler=(0.0, 0.0, ry))
    # Mounting neck collar
    kb.add_box((cx - dx * 0.12, cy - dy * 0.12, cz + 0.01), (0.14, 0.22, 0.10), MAT_DARK_METAL,
               rot_euler=(0.0, 0.0, ry))
    # Recessed LED optical diffuser facing downward
    lx = cx + dx * 0.30
    ly = cy + dy * 0.30
    kb.add_box((lx, ly, cz - 0.022), (0.26, 0.46, 0.025), MAT_LAMP_LED, rot_euler=(0.0, 0.0, ry))


def build_street_lamp_single(mats):
    kb = KitBuilder("SM_StreetLamp_Single", mats)
    # Concrete base collar + cast-iron anchor flange with 4 bolts
    kb.add_cylinder((0.0, 0.0, 0.0), 0.22, 0.20, 0.16, 8, MAT_CONCRETE, smooth_sides=False)
    kb.add_box_base((0.0, 0.0, 0.16), (0.30, 0.30, 0.04), MAT_DARK_METAL)
    for sx in (-0.11, 0.11):
        for sy in (-0.11, 0.11):
            kb.add_cylinder((sx, sy, 0.20), 0.018, 0.018, 0.04, 6, MAT_GALV_STEEL, smooth_sides=False)
    # Stepped lower sleeve + inspection hatch
    kb.add_cylinder((0.0, 0.0, 0.20), 0.135, 0.115, 1.15, 10, MAT_GALV_STEEL)
    kb.add_box((0.0, -0.12, 0.78), (0.11, 0.03, 0.36), MAT_DARK_METAL)
    # Upper tapered galvanized mast up to 6.85m
    kb.add_cylinder((0.0, 0.0, 1.35), 0.095, 0.068, 5.45, 10, MAT_GALV_STEEL)
    # Swept curved gooseneck bracket arm reaching (0, -1.80, 7.52)
    arm_pts = [
        (0.0, 0.0, 6.72),
        (0.0, -0.18, 7.05),
        (0.0, -0.52, 7.34),
        (0.0, -1.00, 7.49),
        (0.0, -1.48, 7.52),
    ]
    kb.add_pipe(arm_pts, [0.062, 0.056, 0.050, 0.046, 0.042], 8, MAT_GALV_STEEL)
    # Stiffening gusset strut under elbow
    kb.add_pipe([(0.0, 0.0, 6.42), (0.0, -0.62, 7.22)], [0.024, 0.022], 6, MAT_GALV_STEEL)
    _cobra_led_head(kb, (0.0, -1.50, 7.52), yaw_deg=0.0)
    return kb


def build_street_lamp_double(mats):
    kb = KitBuilder("SM_StreetLamp_Double", mats)
    kb.add_cylinder((0.0, 0.0, 0.0), 0.24, 0.21, 0.18, 8, MAT_CONCRETE, smooth_sides=False)
    kb.add_box_base((0.0, 0.0, 0.18), (0.34, 0.34, 0.04), MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 0.22), 0.145, 0.12, 1.25, 10, MAT_GALV_STEEL)
    kb.add_box((0.0, -0.13, 0.84), (0.11, 0.03, 0.38), MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 1.47), 0.102, 0.072, 5.65, 10, MAT_GALV_STEEL)
    # Central twin-arm collar + decorative top cap
    kb.add_cylinder((0.0, 0.0, 6.95), 0.11, 0.11, 0.26, 10, MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 7.21), 0.09, 0.03, 0.14, 10, MAT_DARK_METAL)
    for sign, yaw in ((-1.0, 0.0), (1.0, 180.0)):
        arm_pts = [
            (0.0, sign * 0.05, 7.05),
            (0.0, sign * 0.36, 7.35),
            (0.0, sign * 0.85, 7.55),
            (0.0, sign * 1.40, 7.60),
        ]
        kb.add_pipe(arm_pts, [0.054, 0.048, 0.044, 0.040], 8, MAT_GALV_STEEL)
        kb.add_pipe([(0.0, 0.0, 6.62), (0.0, sign * 0.56, 7.34)], [0.022, 0.020], 6, MAT_GALV_STEEL)
        _cobra_led_head(kb, (0.0, sign * 1.42, 7.60), yaw_deg=yaw)
    return kb


def _build_power_pole_base(name, mats, with_lamp=True):
    kb = KitBuilder(name, mats)
    # Concrete protection collar at base + tapered treated pole up to 8.5m
    kb.add_cylinder((0.0, 0.0, 0.0), 0.20, 0.17, 0.48, 8, MAT_CONCRETE, smooth_sides=False)
    kb.add_cylinder((0.0, 0.0, 0.48), 0.135, 0.092, 8.05, 8, MAT_WOOD, color=(0.55, 0.42, 0.30, 1.0))
    # Pole cap
    kb.add_cylinder((0.0, 0.0, 8.53), 0.095, 0.05, 0.08, 8, MAT_GALV_STEEL)
    # Galvanized steel cross-arm at 7.90m (width 1.65m along X) + diagonal V-braces
    kb.add_box((0.0, 0.14, 7.90), (1.66, 0.08, 0.09), MAT_GALV_STEEL)
    kb.add_pipe([(-0.48, 0.14, 7.90), (0.0, 0.12, 7.40)], 0.018, 6, MAT_GALV_STEEL)
    kb.add_pipe([(0.48, 0.14, 7.90), (0.0, 0.12, 7.40)], 0.018, 6, MAT_GALV_STEEL)
    # 3 ceramic pin insulators at x = -0.70, 0.0, +0.70
    for ix in (-0.70, 0.0, 0.70):
        kb.add_cylinder((ix, 0.14, 7.94), 0.022, 0.022, 0.07, 6, MAT_GALV_STEEL)
        kb.add_cylinder((ix, 0.14, 8.01), 0.055, 0.036, 0.09, 8, MAT_CONCRETE, color=(0.82, 0.85, 0.88, 1.0))
    # Pole-mounted metal meter/junction box + conduit pipe
    kb.add_box((0.0, 0.18, 3.10), (0.28, 0.16, 0.40), MAT_GALV_STEEL)
    kb.add_pipe([(0.0, 0.15, 3.30), (0.0, 0.13, 6.30)], 0.018, 6, MAT_DARK_METAL)

    if with_lamp:
        # Curved tubular bracket arm at 6.15m extending toward -Y (0.9m) + sodium luminaire
        kb.add_box((0.0, 0.0, 6.18), (0.22, 0.24, 0.14), MAT_DARK_METAL)
        kb.add_pipe([(0.0, -0.08, 6.20), (0.0, -0.45, 6.40), (0.0, -0.82, 6.45)], [0.032, 0.030, 0.028], 8, MAT_GALV_STEEL)
        kb.add_bevel_box((0.0, -0.94, 6.44), (0.26, 0.48, 0.11), 0.02, MAT_GALV_STEEL, segments=1)
        kb.add_cylinder((0.0, -0.98, 6.32), 0.12, 0.08, 0.07, 8, MAT_LAMP_SODIUM)
    return kb


def build_power_pole(mats):
    return _build_power_pole_base("SM_PowerPole", mats, with_lamp=True)


def build_power_pole_nolamp(mats):
    return _build_power_pole_base("SM_PowerPole_NoLamp", mats, with_lamp=False)


def build_traffic_light(mats):
    """Realistic street-corner traffic signal pole with primary (-Y) and secondary (-X) hooded heads."""
    kb = KitBuilder("SM_TrafficLight", mats)
    kb.add_cylinder((0.0, 0.0, 0.0), 0.20, 0.18, 0.14, 8, MAT_CONCRETE, smooth_sides=False)
    kb.add_box_base((0.0, 0.0, 0.14), (0.28, 0.28, 0.03), MAT_DARK_METAL)
    # Tapered dark steel pole with reflective yellow safety collar
    kb.add_cylinder((0.0, 0.0, 0.17), 0.085, 0.075, 0.90, 10, MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 1.07), 0.076, 0.074, 0.35, 10, MAT_ACCENT_PAINT, color=(0.95, 0.78, 0.08, 1.0))
    kb.add_cylinder((0.0, 0.0, 1.42), 0.074, 0.062, 2.38, 10, MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 3.80), 0.070, 0.040, 0.08, 10, MAT_DARK_METAL)
    # Pedestrian push-button box
    kb.add_box((0.0, -0.11, 1.15), (0.11, 0.08, 0.18), MAT_ACCENT_PAINT, color=(0.95, 0.78, 0.08, 1.0))

    def _signal_head(center, yaw_rad):
        cx, cy, cz = center
        # Mounting bracket arms
        dx, dy = math.sin(yaw_rad), -math.cos(yaw_rad)
        kb.add_box((cx - dx * 0.14, cy - dy * 0.14, cz + 0.34), (0.06, 0.26, 0.06), MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
        kb.add_box((cx - dx * 0.14, cy - dy * 0.14, cz - 0.34), (0.06, 0.26, 0.06), MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
        # Contrast backplate + main housing
        kb.add_box((cx - dx * 0.04, cy - dy * 0.04, cz), (0.44, 0.03, 1.06), MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
        kb.add_bevel_box((cx, cy, cz), (0.30, 0.24, 0.94), 0.02, MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
        # 3 visor hoods (top, middle, bottom)
        for kz in (0.30, 0.0, -0.30):
            vx = cx + dx * 0.18
            vy = cy + dy * 0.18
            vz = cz + kz + 0.045
            # Top & side sun visor hood
            kb.add_box((vx, vy, vz + 0.065), (0.21, 0.16, 0.025), MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
            for sx in (-0.10, 0.10):
                rx = vx + math.cos(yaw_rad) * sx
                ry = vy + math.sin(yaw_rad) * sx
                kb.add_box((rx, ry, vz - 0.015), (0.022, 0.14, 0.16), MAT_DARK_METAL, rot_euler=(0, 0, yaw_rad))
            # Dark unlit lens backing inside hood
            kb.add_box((cx + dx * 0.125, cy + dy * 0.125, cz + kz), (0.18, 0.02, 0.18), MAT_DARK_METAL,
                       color=(0.06, 0.06, 0.07, 1.0), rot_euler=(0, 0, yaw_rad))

    _signal_head((0.0, -0.26, 2.90), 0.0)
    _signal_head((-0.26, 0.0, 2.90), math.pi * 0.5)
    return kb


def build_storm_drain(mats):
    """Abidjan roadside concrete storm drain apron & cast-iron slotted grate along curb (-Y faces road)."""
    kb = KitBuilder("SM_StormDrain", mats)
    # Concrete frame apron flush at street edge (1.40m x 0.56m x 0.14m)
    kb.add_box((0.0, -0.26, -0.02), (1.42, 0.54, 0.08), MAT_CONCRETE)
    # Recessed dark throat / drain pit underneath
    kb.add_box((0.0, -0.24, 0.01), (1.10, 0.36, 0.03), MAT_DARK_METAL, color=(0.05, 0.05, 0.05, 1.0))
    # Cast-iron perimeter grate frame + 6 slotted bars
    kb.add_box((0.0, -0.08, 0.03), (1.14, 0.05, 0.03), MAT_DARK_METAL)
    kb.add_box((0.0, -0.40, 0.03), (1.14, 0.05, 0.03), MAT_DARK_METAL)
    for i in range(7):
        bx = -0.51 + i * 0.17
        kb.add_box((bx, -0.24, 0.035), (0.06, 0.32, 0.03), MAT_DARK_METAL)
    # Curb inlet concrete lintel & throat opening
    kb.add_box((0.0, 0.08, 0.12), (1.42, 0.18, 0.10), MAT_CONCRETE)
    return kb


def build_bus_shelter(mats):
    """Real Abidjan SOTRA/Gbaka shelter: steel truss posts, arched roof, windscreen & slatted bench (-Y faces road)."""
    kb = KitBuilder("SM_BusShelter", mats)
    # Concrete pad anchor
    kb.add_box_base((0.0, 0.0, 0.0), (4.0, 1.75, 0.05), MAT_CONCRETE)
    # 4 steel posts with base flanges
    for px in (-1.75, 1.75):
        for py in (-0.65, 0.65):
            kb.add_box_base((px, py, 0.05), (0.16, 0.16, 0.03), MAT_DARK_METAL)
            kb.add_cylinder((px, py, 0.08), 0.042, 0.042, 2.38, 8, MAT_GALV_STEEL)
    # Sloped/curved corrugated roof canopy + purlins + fascia
    kb.add_box((0.0, 0.0, 2.50), (4.10, 1.85, 0.07), MAT_ROOF_METAL, color=(0.15, 0.45, 0.32, 1.0),
               rot_euler=(math.radians(-6.0), 0.0, 0.0))
    kb.add_box((0.0, -0.90, 2.41), (4.12, 0.06, 0.14), MAT_ACCENT_PAINT, color=(0.95, 0.60, 0.10, 1.0))
    for px in (-1.75, 0.0, 1.75):
        kb.add_box((px, 0.0, 2.42), (0.07, 1.65, 0.08), MAT_DARK_METAL, rot_euler=(math.radians(-6.0), 0.0, 0.0))
    # Rear framed windscreen panel + side ad light-box totem
    kb.add_box((0.0, 0.65, 1.32), (3.42, 0.04, 1.95), MAT_DARK_METAL, color=(0.18, 0.24, 0.28, 1.0))
    kb.add_box((1.75, 0.0, 1.32), (0.14, 1.25, 2.05), MAT_DARK_METAL)
    kb.add_box((1.75, 0.0, 1.32), (0.16, 1.05, 1.75), MAT_ACCENT_PAINT, color=(0.92, 0.88, 0.78, 1.0))
    # Slatted wooden bench on steel brackets
    for lx in (-1.1, 0.0, 1.1):
        kb.add_box_base((lx, 0.28, 0.05), (0.06, 0.44, 0.40), MAT_DARK_METAL)
    for sy in (0.15, 0.30, 0.44):
        kb.add_box((0.0, sy, 0.47), (2.70, 0.11, 0.045), MAT_WOOD, color=(0.50, 0.32, 0.18, 1.0))
    kb.add_box((0.0, 0.54, 0.76), (2.70, 0.045, 0.26), MAT_WOOD, color=(0.50, 0.32, 0.18, 1.0))
    return kb


def _build_road_sign(name, mats, shape="octagon", face_color=(0.82, 0.12, 0.12, 1.0)):
    kb = KitBuilder(name, mats)
    kb.add_cylinder((0.0, 0.0, 0.0), 0.14, 0.12, 0.08, 8, MAT_CONCRETE, smooth_sides=False)
    kb.add_cylinder((0.0, 0.0, 0.08), 0.032, 0.032, 2.45, 8, MAT_GALV_STEEL)
    # Clamps
    for cz in (2.22, 2.46):
        kb.add_box((0.0, -0.02, cz), (0.09, 0.08, 0.04), MAT_DARK_METAL)
    if shape == "octagon":
        kb.add_cylinder((0.0, -0.055, 2.35), 0.34, 0.34, 0.025, 8, MAT_GALV_STEEL,
                        rot_euler=(math.pi * 0.5, math.pi / 8.0, 0.0), cap_bottom=True, smooth_sides=False)
        kb.add_cylinder((0.0, -0.068, 2.35), 0.30, 0.30, 0.012, 8, MAT_ACCENT_PAINT, color=face_color,
                        rot_euler=(math.pi * 0.5, math.pi / 8.0, 0.0), cap_bottom=True, smooth_sides=False)
    elif shape == "triangle":
        # Warning sign: triangular plate
        p0, p1, p2 = (-0.34, -0.06, 2.12), (0.34, -0.06, 2.12), (0.0, -0.06, 2.70)
        kb.add_tri(p0, p1, p2, MAT_ACCENT_PAINT, color=face_color)
        kb.add_tri((0.0, -0.045, 2.70), (0.34, -0.045, 2.12), (-0.34, -0.045, 2.12), MAT_GALV_STEEL)
        kb.add_box((0.0, -0.052, 2.12), (0.68, 0.02, 0.04), MAT_ACCENT_PAINT, color=(0.82, 0.12, 0.12, 1.0))
    else:
        kb.add_cylinder((0.0, -0.055, 2.35), 0.32, 0.32, 0.025, 12, MAT_GALV_STEEL,
                        rot_euler=(math.pi * 0.5, 0.0, 0.0), cap_bottom=True, smooth_sides=False)
        kb.add_cylinder((0.0, -0.068, 2.35), 0.28, 0.28, 0.012, 12, MAT_ACCENT_PAINT, color=face_color,
                        rot_euler=(math.pi * 0.5, 0.0, 0.0), cap_bottom=True, smooth_sides=False)
    return kb


def build_road_sign_stop(mats):
    return _build_road_sign("SM_RoadSign_Stop", mats, shape="octagon", face_color=(0.82, 0.12, 0.12, 1.0))


def build_road_sign_pedestrian(mats):
    return _build_road_sign("SM_RoadSign_Pedestrian", mats, shape="triangle", face_color=(0.96, 0.88, 0.18, 1.0))


def build_road_sign_speed(mats):
    return _build_road_sign("SM_RoadSign_Speed", mats, shape="circle", face_color=(0.94, 0.94, 0.92, 1.0))


def build_guardrail(mats):
    """4m W-beam galvanized highway guardrail segment along X (-2m..+2m), facing -Y."""
    kb = KitBuilder("SM_Guardrail", mats)
    # Posts at x = -1.0 and +1.0
    for px in (-1.0, 1.0):
        kb.add_box_base((px, 0.06, 0.0), (0.10, 0.08, 0.76), MAT_DARK_METAL)
        kb.add_box((px, -0.02, 0.58), (0.10, 0.10, 0.26), MAT_DARK_METAL)
    # W-beam corrugated cross-section extruded along X from -2.0 to +2.0
    profile_yz = [
        (-0.07, 0.43), (-0.12, 0.47), (-0.12, 0.53), (-0.07, 0.57),
        (-0.12, 0.61), (-0.12, 0.67), (-0.07, 0.71),
        (-0.05, 0.71), (-0.09, 0.66), (-0.09, 0.62), (-0.05, 0.57),
        (-0.09, 0.52), (-0.09, 0.48), (-0.05, 0.43),
    ]
    kb.add_extruded_x_profile(profile_yz, -2.0, 2.0, MAT_GALV_STEEL)
    return kb


def build_bridge_pier(mats):
    """Reinforced concrete bridge pier cutwater buttresses (avant-becs) visible on both bridge flanks."""
    kb = KitBuilder("SM_BridgePier", mats)
    # Submerged & waterline concrete pile-cap beam spanning underneath (-2.5m..-1.05m)
    kb.add_bevel_box((0.0, 0.0, -1.78), (15.4, 2.3, 1.45), 0.14, MAT_CONCRETE, color=(0.56, 0.56, 0.54, 1.0))
    # Flank cutwater pier columns rising outside bridge deck (+-6.8m..+-7.5m) from water to deck edge
    for sx in (-7.2, 7.2):
        kb.add_cylinder((sx, 0.0, -2.10), 0.85, 0.72, 2.15, 10, MAT_CONCRETE, color=(0.64, 0.63, 0.60, 1.0))
        # Pier cap pedestal at deck/railing level
        kb.add_bevel_box((sx, 0.0, 0.10), (1.55, 1.85, 0.28), 0.05, MAT_CONCRETE, color=(0.74, 0.72, 0.68, 1.0))
    return kb


def build_bridge_railing(mats):
    """4m bridge New Jersey concrete safety parapet + steel stanchions & double pedestrian rails along X."""
    kb = KitBuilder("SM_BridgeRailing", mats)
    # Sloped New Jersey concrete parapet along X (-2..+2), inner road edge at -Y
    profile_yz = [
        (-0.24, 0.0), (0.20, 0.0), (0.20, 0.68), (0.04, 0.68),
        (-0.08, 0.22), (-0.24, 0.14),
    ]
    kb.add_extruded_x_profile(profile_yz, -2.0, 2.0, MAT_CONCRETE, color=(0.76, 0.74, 0.70, 1.0))
    # Steel stanchions & double horizontal tubular rails up to 1.15m
    for px in (-1.33, 0.0, 1.33):
        kb.add_box_base((px, 0.12, 0.68), (0.07, 0.07, 0.47), MAT_GALV_STEEL)
    kb.add_box((0.0, 0.12, 0.90), (4.0, 0.045, 0.045), MAT_GALV_STEEL)
    kb.add_box((0.0, 0.12, 1.15), (4.0, 0.065, 0.055), MAT_GALV_STEEL)
    return kb


def build_bollard(mats):
    kb = KitBuilder("SM_Bollard", mats)
    kb.add_cylinder((0.0, 0.0, 0.0), 0.10, 0.085, 0.12, 8, MAT_CONCRETE)
    kb.add_cylinder((0.0, 0.0, 0.12), 0.075, 0.070, 0.58, 10, MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 0.70), 0.072, 0.072, 0.11, 10, MAT_ACCENT_PAINT, color=(0.95, 0.78, 0.10, 1.0))
    kb.add_cylinder((0.0, 0.0, 0.81), 0.070, 0.025, 0.06, 10, MAT_DARK_METAL)
    return kb


def build_street_bin(mats):
    kb = KitBuilder("SM_StreetBin", mats)
    kb.add_cylinder((0.0, 0.0, 0.0), 0.26, 0.26, 0.05, 10, MAT_CONCRETE)
    kb.add_cylinder((0.0, 0.0, 0.05), 0.22, 0.24, 0.68, 10, MAT_DARK_METAL)
    # Green/orange accent collar & rain hood cap
    kb.add_cylinder((0.0, 0.0, 0.64), 0.245, 0.245, 0.09, 10, MAT_ACCENT_PAINT, color=(0.18, 0.56, 0.30, 1.0))
    for sx in (-0.21, 0.21):
        kb.add_box((sx, 0.0, 0.80), (0.04, 0.05, 0.18), MAT_DARK_METAL)
    kb.add_cylinder((0.0, 0.0, 0.88), 0.27, 0.12, 0.10, 10, MAT_DARK_METAL)
    return kb


ALL_STREET_BUILDERS = [
    build_street_lamp_single,
    build_street_lamp_double,
    build_power_pole,
    build_power_pole_nolamp,
    build_traffic_light,
    build_storm_drain,
    build_bus_shelter,
    build_road_sign_stop,
    build_road_sign_pedestrian,
    build_road_sign_speed,
    build_guardrail,
    build_bridge_pier,
    build_bridge_railing,
    build_bollard,
    build_street_bin,
]
