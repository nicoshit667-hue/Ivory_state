"""
Build and export Building #2: Tour F (Cité Administrative — Commune du Plateau, Abidjan).

Architectural faithfulness (Pierre Fakhoury design):
- Footprint: 64.0 m x 64.0 m monumental parvis & podium (fits Plateau block (-70, 15) cleanly).
- Height: 388 m roof lantern / 412 m spire (1:1 monumental scale).
- Podium: Parallelepipedic base (54 m x 50 m, H = 18 m) with cantilevered street canopy,
  inclined steel V-deviation struts, and explorable grand ground-floor lobby + mezzanine.
- Shaft: Faceted symmetric "African mask" silhouette built with evolving polygonal loft sections,
  slanted glass curtain-wall planes, chamfered structural ribs, night-lit office bands, and 360-deg crown lantern.
- Exports:
  1. IvoryState/Assets/_Project/Art/Models/Architecture/Monuments/SM_Tour_F.fbx
  2. IvoryState/Assets/_Project/Art/Models/Architecture/Interieurs/SM_Tour_F_Interieur.fbx
  3. tools/blender/architecture/Tour_F.blend
  4. tools/blender/architecture/tour_f_manifest.json
"""

import math
import os
import sys
import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from arch_common import (  # noqa: E402
    ArchMeshBuilder,
    export_objects_fbx,
    render_preview_views,
    reset_scene,
    save_collider_manifest,
)


REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", "..", ".."))
EXT_FBX_PATH = os.path.join(
    REPO_ROOT,
    "IvoryState",
    "Assets",
    "_Project",
    "Art",
    "Models",
    "Architecture",
    "Monuments",
    "SM_Tour_F.fbx",
)
INT_FBX_PATH = os.path.join(
    REPO_ROOT,
    "IvoryState",
    "Assets",
    "_Project",
    "Art",
    "Models",
    "Architecture",
    "Interieurs",
    "SM_Tour_F_Interieur.fbx",
)
BLEND_PATH = os.path.join(SCRIPT_DIR, "Tour_F.blend")
MANIFEST_PATH = os.path.join(SCRIPT_DIR, "tour_f_manifest.json")


def make_empty_root(name, collection_name):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    col = bpy.data.collections.get(collection_name)
    if col is None:
        col = bpy.data.collections.new(collection_name)
        bpy.context.scene.collection.children.link(col)
    col.objects.link(empty)
    empty.location = (0.0, 0.0, 0.0)
    empty.scale = (1.0, 1.0, 1.0)
    return empty


def mask_ring_points(z, rx, ry, prow_y, cheek_x, chamfer_x, chamfer_y):
    """
    Build a symmetric 12-vertex African-mask polygon ring at elevation z.
    Vertex order goes CCW in XY (front -Y has prow ridge, flanks flare into cheekbones).
    """
    return [
        (0.0, -prow_y, z),               # 0: Front center nose/prow ridge
        (chamfer_x, -ry, z),             # 1: Front-right brow
        (rx, -chamfer_y, z),             # 2: Right front cheek
        (cheek_x, 0.0, z),               # 3: Right cheekbone peak
        (rx, chamfer_y, z),              # 4: Right rear cheek
        (chamfer_x, ry, z),              # 5: Rear-right brow
        (0.0, prow_y, z),                # 6: Rear center spine ridge
        (-chamfer_x, ry, z),             # 7: Rear-left brow
        (-rx, chamfer_y, z),             # 8: Left rear cheek
        (-cheek_x, 0.0, z),              # 9: Left cheekbone peak
        (-rx, -chamfer_y, z),            # 10: Left front cheek
        (-chamfer_x, -ry, z),            # 11: Front-left brow
    ]


# Sculptural profile stations: (z, rx, ry, prow_y, cheek_x, chamfer_x, chamfer_y)
TOUR_F_PROFILE = [
    (18.0,  15.5, 11.5, 14.5, 19.5,  8.5, 6.2),
    (52.0,  17.8, 13.0, 16.8, 22.0, 10.0, 7.2),
    (96.0,  19.2, 14.2, 18.4, 24.0, 11.2, 8.0),
    (148.0, 18.4, 13.6, 17.6, 23.0, 10.6, 7.5),
    (204.0, 19.8, 14.8, 19.2, 24.8, 11.6, 8.4),
    (258.0, 17.6, 13.2, 17.4, 21.8, 10.2, 7.2),
    (312.0, 14.8, 11.2, 14.6, 18.2,  8.4, 6.0),
    (358.0, 12.2,  9.2, 12.0, 15.0,  6.8, 4.8),
]


def build_exterior():
    """Build Tour F monumental parvis, podium with canopy & V-struts, faceted tower shaft, and 412m crown/spire."""
    col_name = "Tour_F_Exterieur"
    ext_podium = ArchMeshBuilder("TourF_Podium", collection_name=col_name)
    ext_tower = ArchMeshBuilder("TourF_Tower_Facade", collection_name=col_name)
    ext_doors = []
    door_metas = []

    # -------------------------------------------------------------------------
    # 1. PARVIS & PODIUM BASE (64 m x 64 m -> X [-32, +32], Y [-32, +32])
    # -------------------------------------------------------------------------
    # 3 stepped granite plaza slabs around the perimeter
    ext_podium.add_box_bounds(-32.0, 32.0, -32.0, 32.0, 0.0, 0.16, "Mat_Arch_Paves_Cour", uv_scale=0.35, collider=True)
    ext_podium.add_box_bounds(-30.6, 30.6, -30.6, 30.6, 0.16, 0.32, "Mat_Arch_Beton_Brut", uv_scale=0.35, collider=True)
    ext_podium.add_box_bounds(-29.2, 29.2, -29.2, 29.2, 0.32, 0.48, "Mat_Arch_Sol_Terrasse", uv_scale=0.35, collider=True)

    # Plaza decorative planters & flagpoles along front street edge
    for px in (-26.0, -18.0, 18.0, 26.0):
        ext_podium.add_box_bounds(px - 2.6, px + 2.6, -28.4, -25.4, 0.48, 0.96, "Mat_Arch_Parement_Pierre", collider=True)
        ext_podium.add_box_bounds(px - 2.2, px + 2.2, -28.0, -25.8, 0.96, 1.05, "Mat_Arch_Gazon")
        ext_podium.add_cylinder(px, -26.9, 1.05, 8.50, 0.08, 8, "Mat_Arch_Metal_Inox")

    # Podium Envelope (X [-26.0, +26.0], Y [-22.0, +24.0], Z = 0.48..18.0 m)
    # Ground Lobby Level (Z = 0.48..10.20): grand glazed curtain walls with open double portals front & rear
    # Front Podium Lobby Glass Wall (Y = -21.85) with wide 6.0m open entry portal
    ext_podium.add_wall_with_openings(
        "X", -21.85, 0.30, -26.0, 26.0, 0.48, 10.20, "Mat_Arch_Parement_Pierre",
        openings=[
            {"s0": -23.0, "s1": -7.5, "z0": 0.48, "z1": 9.20, "kind": "window"},
            {"s0": -3.2, "s1": 3.2, "z0": 0.48, "z1": 4.40, "kind": "door"},
            {"s0": 7.5, "s1": 23.0, "z0": 0.48, "z1": 9.20, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    # Rear Podium Wall (Y = +23.85)
    ext_podium.add_wall_with_openings(
        "X", 23.85, 0.30, -26.0, 26.0, 0.48, 10.20, "Mat_Arch_Parement_Pierre",
        openings=[
            {"s0": -22.0, "s1": -7.5, "z0": 1.20, "z1": 9.00, "kind": "window"},
            {"s0": -2.5, "s1": 2.5, "z0": 0.48, "z1": 4.20, "kind": "door"},
            {"s0": 7.5, "s1": 22.0, "z0": 1.20, "z1": 9.00, "kind": "window"},
        ],
        frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
    )
    # Side Podium Walls (X = -25.85, X = +25.85)
    for sx in (-25.85, 25.85):
        ext_podium.add_wall_with_openings(
            "Y", sx, 0.30, -21.70, 23.70, 0.48, 10.20, "Mat_Arch_Parement_Pierre",
            openings=[
                {"s0": -18.0, "s1": -4.0, "z0": 0.48, "z1": 9.00, "kind": "window"},
                {"s0": 4.0, "s1": 18.0, "z0": 0.48, "z1": 9.00, "kind": "window"},
            ],
            frame_mat="Mat_Arch_Metal_Anthracite", glass_mat="Mat_Arch_Vitre_Ext",
        )

    # Upper Podium Technical / Auditorium Block (Z = 10.20..18.00) + Overhanging Entrance Canopy (auvent)
    ext_podium.add_box_bounds(-26.0, 26.0, -22.0, 24.0, 10.20, 10.70, "Mat_Arch_Beton_Brut", collider=True)
    ext_podium.add_box_bounds(-26.0, 26.0, -22.0, 24.0, 10.70, 17.60, "Mat_Arch_Parement_Pierre", collider=True)
    ext_podium.add_box_bounds(-26.3, 26.3, -22.3, 24.3, 17.60, 18.10, "Mat_Arch_Metal_Anthracite", collider=True)
    # Podium facade architectural vertical fins & auditorium glass ribbon
    for fx in range(-24, 26, 4):
        ext_podium.add_box_bounds(fx - 0.14, fx + 0.14, -22.35, -22.00, 10.70, 17.60, "Mat_Arch_Metal_Anthracite")
    ext_podium.add_box_bounds(-23.0, 23.0, -22.05, -21.95, 11.80, 16.20, "Mat_Arch_Vitre_GratteCiel")

    # Monumental Cantilever Canopy (Auvent) over Front Parvis + V-Shaped Steel Deviation Columns
    ext_podium.add_box_bounds(-27.5, 27.5, -28.5, -22.0, 10.20, 11.20, "Mat_Arch_Metal_Anthracite")
    ext_podium.add_box_bounds(-27.0, 27.0, -28.0, -22.0, 10.12, 10.20, "Mat_Arch_Bois_Exterieur")
    ext_podium.add_box_bounds(-25.0, 25.0, -27.8, -27.5, 10.08, 10.12, "Mat_Arch_Lumiere_Nuit")
    for vx in (-16.0, -6.0, 6.0, 16.0):
        ext_podium.add_beam((vx, -25.6, 0.48), (vx - 2.2, -23.0, 10.20), 0.55, 0.55, "Mat_Arch_Metal_Anthracite", collider=True)
        ext_podium.add_beam((vx, -25.6, 0.48), (vx + 2.2, -23.0, 10.20), 0.55, 0.55, "Mat_Arch_Metal_Anthracite", collider=True)

    # Separate Automatic Sliding Glass Entrance Doors (Left & Right leaves slid open 2.2m each -> 4.4m clear!)
    for dname, ox, slide_dx in [("Door_TourF_Entry_L", -3.15, -1.60), ("Door_TourF_Entry_R", 1.55, 1.60)]:
        db = ArchMeshBuilder(dname, collection_name=col_name, origin=(ox + slide_dx, -21.85, 0.48))
        px = ox + slide_dx
        db.add_box_bounds(px, px + 1.60, -21.90, -21.80, 0.48, 3.85, "Mat_Arch_Vitre_Ext", collider=True)
        db.add_box_bounds(px, px + 1.60, -21.91, -21.79, 0.48, 0.58, "Mat_Arch_Metal_Inox")
        db.add_box_bounds(px, px + 1.60, -21.91, -21.79, 3.75, 3.85, "Mat_Arch_Metal_Inox")
        ext_doors.append(db.build_object())
        door_metas.append({"name": dname, "type": "sliding", "colliders": db.colliders})

    # -------------------------------------------------------------------------
    # 2. FACETED "AFRICAN MASK" SKYSCRAPER SHAFT (Z = 18.0..358.0 m)
    # -------------------------------------------------------------------------
    for idx in range(len(TOUR_F_PROFILE) - 1):
        p0 = TOUR_F_PROFILE[idx]
        p1 = TOUR_F_PROFILE[idx + 1]
        ring0 = mask_ring_points(*p0)
        ring1 = mask_ring_points(*p1)
        # Main faceted double-skin reflective curtain wall
        ext_tower.add_loft_section(ring0, ring1, "Mat_Arch_Vitre_GratteCiel", uv_scale=0.1, cap_bottom=(idx == 0), cap_top=False)

        # Structural diamond ribs along each of the 12 mask facet edges
        for k in range(len(ring0)):
            rib_mat = "Mat_Arch_Metal_Inox" if k in (0, 3, 6, 9) else "Mat_Arch_Metal_Anthracite"
            ext_tower.add_beam(ring0[k], ring1[k], 0.45, 0.45, rib_mat)

        # Horizontal structural floor rings & NightLight illuminated office bands every ~12m
        z0, z1 = p0[0], p1[0]
        steps = max(2, int(round((z1 - z0) / 14.0)))
        for s in range(1, steps + 1):
            t = s / (steps + 1)
            interp = tuple(p0[j] * (1.0 - t) + p1[j] * t for j in range(len(p0)))
            band_z = interp[0]
            # Slightly proud glowing office band + spandrel mullion
            r_lit_b = mask_ring_points(band_z - 0.9, *(v * 1.012 for v in interp[1:]))
            r_lit_t = mask_ring_points(band_z + 0.9, *(v * 1.012 for v in interp[1:]))
            ext_tower.add_loft_section(r_lit_b, r_lit_t, "Mat_Arch_Fenetre_Nuit", uv_scale=0.1, cap_bottom=False, cap_top=False)
            r_sp_b = mask_ring_points(band_z + 0.9, *(v * 1.016 for v in interp[1:]))
            r_sp_t = mask_ring_points(band_z + 1.4, *(v * 1.016 for v in interp[1:]))
            ext_tower.add_loft_section(r_sp_b, r_sp_t, "Mat_Arch_Metal_Anthracite", uv_scale=0.1, cap_bottom=False, cap_top=False)

    # Diagonal "Mask Eye/Cheek" Architectural Accent Blades (symmetric across mid-height)
    r1 = mask_ring_points(*TOUR_F_PROFILE[1])
    r3 = mask_ring_points(*TOUR_F_PROFILE[3])
    r5 = mask_ring_points(*TOUR_F_PROFILE[5])
    for side in (1, -1):
        i_brow = 1 if side > 0 else 11
        i_cheek = 3 if side > 0 else 9
        ext_tower.add_beam(r1[0], r3[i_brow], 0.55, 0.55, "Mat_Arch_Metal_Inox")
        ext_tower.add_beam(r3[i_brow], r5[0], 0.55, 0.55, "Mat_Arch_Metal_Inox")
        ext_tower.add_beam(r1[i_brow], r3[i_cheek], 0.50, 0.50, "Mat_Arch_Metal_Inox")
        ext_tower.add_beam(r3[i_cheek], r5[i_brow], 0.50, 0.50, "Mat_Arch_Metal_Inox")

    # -------------------------------------------------------------------------
    # 3. 360-DEG PANORAMIC CROWN LANTERN (Z = 358..388 m) & SPIRE (Z = 388..412 m)
    # -------------------------------------------------------------------------
    crown_bot = mask_ring_points(*TOUR_F_PROFILE[-1])
    crown_mid = mask_ring_points(374.0, 10.2, 7.8, 10.0, 12.2, 5.6, 4.0)
    crown_top = mask_ring_points(388.0, 5.8, 4.4, 5.8, 7.0, 3.2, 2.4)
    ext_tower.add_loft_section(crown_bot, crown_mid, "Mat_Arch_Fenetre_Nuit", uv_scale=0.1, cap_bottom=False, cap_top=False)
    ext_tower.add_loft_section(crown_mid, crown_top, "Mat_Arch_Vitre_GratteCiel", uv_scale=0.1, cap_bottom=False, cap_top=True)
    for k in range(len(crown_bot)):
        ext_tower.add_beam(crown_bot[k], crown_mid[k], 0.45, 0.45, "Mat_Arch_Metal_Inox")
        ext_tower.add_beam(crown_mid[k], crown_top[k], 0.35, 0.35, "Mat_Arch_Metal_Inox")

    # Illuminated crown beacon ring + central spire up to 412 m
    ext_tower.add_cylinder(0.0, 0.0, 387.5, 389.2, 3.2, 12, "Mat_Arch_Lumiere_Nuit")
    ext_tower.add_cone(0.0, 0.0, 389.2, 411.0, 1.35, 0.12, 10, "Mat_Arch_Metal_Inox")
    ext_tower.add_cylinder(0.0, 0.0, 410.8, 412.2, 0.28, 8, "Mat_Arch_Lumiere_Nuit")

    # Tower Shaft Collider (solid core 46m x 36m from Z=18..388)
    ext_tower.add_collider_bounds(-21.0, 21.0, -16.0, 16.0, 18.0, 388.0)

    root = make_empty_root("Tour_F_Ext", collection_name=col_name)
    o_pod = ext_podium.build_object(parent=root)
    o_tow = ext_tower.build_object(parent=root)
    for d in ext_doors:
        d.parent = root

    return root, [root, o_pod, o_tow] + ext_doors, ext_podium.colliders + ext_tower.colliders, door_metas


def build_interior():
    """Build explorable monumental Ground Lobby + Mezzanine + Elevator Core inside Tour F podium."""
    col_name = "Tour_F_Interieur"
    int_lobby = ArchMeshBuilder("TourF_Lobby_Int", collection_name=col_name)

    # Polished marble lobby floor (Z = 0.46..0.52)
    int_lobby.add_box_bounds(-25.6, 25.6, -21.6, 23.6, 0.46, 0.52, "Mat_Arch_Sol_Marbre", uv_scale=0.35, collider=True)

    # Central monumental elevator core (X [-9.0, +9.0], Y [-2.0, +10.0], Z = 0.52..10.20)
    int_lobby.add_box_bounds(-9.0, 9.0, -2.0, 10.0, 0.52, 10.20, "Mat_Arch_Parement_Pierre", collider=True)
    # Brushed steel elevator doors along front & sides of core
    for ex in (-6.0, -2.0, 2.0, 6.0):
        int_lobby.add_box_bounds(ex - 0.85, ex + 0.85, -2.06, -1.98, 0.52, 3.20, "Mat_Arch_Metal_Inox")
        int_lobby.add_box_bounds(ex - 0.95, ex + 0.95, -2.08, -1.96, 3.20, 3.45, "Mat_Arch_Metal_Anthracite")

    # Grand Reception Desk facing front doors (Y = -12.5, X [-6.5, +6.5])
    int_lobby.add_box_bounds(-6.5, 6.5, -13.2, -11.6, 0.52, 1.65, "Mat_Arch_Bois_Interieur", collider=True)
    int_lobby.add_box_bounds(-6.6, 6.6, -13.3, -11.5, 1.65, 1.74, "Mat_Arch_Plan_Travail")

    # Interior Structural Columns
    for cx in (-18.0, -10.5, 10.5, 18.0):
        for cy in (-14.5, -4.5, 6.0, 16.0):
            int_lobby.add_cylinder(cx, cy, 0.52, 10.20, 0.55, 10, "Mat_Arch_Parement_Pierre")
            int_lobby.add_collider_bounds(cx - 0.55, cx + 0.55, cy - 0.55, cy + 0.55, 0.52, 10.20)

    # Rear Lobby Mezzanine Gallery (Y [11.5, 23.5], Z = 5.10..5.40) + Twin Grand Staircases!
    int_lobby.add_box_bounds(-25.5, 25.5, 11.5, 23.5, 5.10, 5.40, "Mat_Arch_Sol_Marbre", uv_scale=0.35, collider=True)
    # Front railing on mezzanine (leaving left & right stair openings open)
    int_lobby.add_glass_railing("X", 11.55, -14.0, 14.0, 5.40, height=1.10)
    int_lobby.add_glass_railing("X", 11.55, -25.4, -18.0, 5.40, height=1.10)
    int_lobby.add_glass_railing("X", 11.55, 18.0, 25.4, 5.40, height=1.10)

    # Left & Right Lobby Staircases (X = -16.0 and X = +16.0, climbing Y: 1.5 -> 11.5, Z: 0.52 -> 5.40 -> slope 26 deg!)
    for sx in (-16.0, 16.0):
        int_lobby.add_stair_flight(
            "Y", sx, 3.6, 1.5, 11.5, 0.52, 5.40, 24, "Mat_Arch_Sol_Marbre", "Mat_Arch_Metal_Anthracite"
        )

    # Warm Ceiling Recessed Light Strips inside Lobby
    for lx in (-16.0, 0.0, 16.0):
        for ly in (-16.0, -7.5):
            int_lobby.add_box_bounds(lx - 2.4, lx + 2.4, ly - 0.5, ly + 0.5, 10.16, 10.20, "Mat_Arch_Lumiere_Int_Nuit")

    root = make_empty_root("Tour_F_Int", collection_name=col_name)
    o_lobby = int_lobby.build_object(parent=root)
    return root, [root, o_lobby], int_lobby.colliders, []


def main():
    reset_scene()
    ext_root, ext_objects, ext_cols, ext_doors = build_exterior()
    int_root, int_objects, int_cols, int_doors = build_interior()

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    export_objects_fbx(ext_objects, EXT_FBX_PATH)
    export_objects_fbx(int_objects, INT_FBX_PATH)

    manifest = {
        "name": "Tour_F",
        "lot_meters": [64.0, 64.0],
        "exterior_fbx": "Assets/_Project/Art/Models/Architecture/Monuments/SM_Tour_F.fbx",
        "interior_fbx": "Assets/_Project/Art/Models/Architecture/Interieurs/SM_Tour_F_Interieur.fbx",
        "exterior_colliders": ext_cols,
        "interior_colliders": int_cols,
        "exterior_doors": ext_doors,
        "interior_doors": int_doors,
    }
    save_collider_manifest(MANIFEST_PATH, manifest)

    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1 :]
        if args:
            preview_dir = args[0]
            render_preview_views(
                preview_dir,
                "tour_f",
                [
                    ("skyline_3q", (-190.0, -240.0, 165.0), (0.0, 0.0, 185.0)),
                    ("podium_front", (-35.0, -68.0, 18.0), (0.0, -10.0, 12.0)),
                    ("lobby_int", (-10.0, -19.0, 3.8), (0.0, 4.0, 3.6)),
                ],
            )

    total_tris = sum(
        sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
        for o in ext_objects + int_objects
        if o.type == "MESH"
    )
    print(f"[SUMMARY] Tour F total tris={total_tris}, ext_colliders={len(ext_cols)}, int_colliders={len(int_cols)}")


if __name__ == "__main__":
    main()
