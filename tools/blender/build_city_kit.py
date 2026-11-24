"""Builds the complete Ivory State architectural & street-furniture Blender kit,
saves IvoryCityKit.blend, exports Unity-ready FBXs, and renders showcase previews.

Run headless:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/build_city_kit.py -- [--render]
"""

import math
import os
import sys
import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from kit_common import ensure_collection, ensure_materials, export_fbx  # noqa: E402
from kit_street import ALL_STREET_BUILDERS  # noqa: E402
from kit_buildings import ALL_BUILDING_BUILDERS  # noqa: E402
from kit_props import ALL_PROP_BUILDERS  # noqa: E402


REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
FBX_OUT_DIR = os.path.join(REPO_ROOT, "IvoryState", "Assets", "_Project", "Art", "Models", "City")
BLEND_OUT = os.path.join(SCRIPT_DIR, "IvoryCityKit.blend")


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def add_lighting_for_preview():
    col = ensure_collection("00_Showcase_Env")
    # Warm golden-hour sun
    sun_data = bpy.data.lights.new(name="Sun_Abidjan", type="SUN")
    sun_data.energy = 3.2
    sun_data.color = (1.0, 0.90, 0.74)
    sun = bpy.data.objects.new("Sun_Abidjan", sun_data)
    sun.rotation_euler = (math.radians(52), math.radians(-22), math.radians(-35))
    col.objects.link(sun)

    # Sky world
    world = bpy.data.worlds.new("AbidjanSky")
    bpy.context.scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.48, 0.62, 0.80, 1.0)
        bg.inputs["Strength"].default_value = 0.7


def render_preview(out_dir, name, cam_loc, target):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1120
    scene.render.resolution_y = 640
    scene.render.filepath = os.path.join(out_dir, f"{name}.png")

    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = 35.0
    cam_data.clip_end = 500.0
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = cam_loc
    from mathutils import Vector
    direction = (Vector(target) - Vector(cam_loc)).normalized()
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(cam)
    scene.camera = cam
    bpy.ops.render.render(write_still=True)


def main():
    do_render = "--render" in sys.argv
    render_dir = None
    for idx, arg in enumerate(sys.argv):
        if arg == "--render-dir" and idx + 1 < len(sys.argv):
            render_dir = sys.argv[idx + 1]

    reset_scene()
    mats = ensure_materials()
    add_lighting_for_preview()

    groups = [
        ("01_Street_Lighting", [b for b in ALL_STREET_BUILDERS[:5]], 0.0, 6.0),
        ("02_Road_Infrastructure", [b for b in ALL_STREET_BUILDERS[5:]], 14.0, 7.0),
        ("03_Cocody_Villas", [b for b in ALL_BUILDING_BUILDERS[:3]], 34.0, 18.0),
        ("04_Popular_And_Residential", [b for b in ALL_BUILDING_BUILDERS[3:12]], 56.0, 16.0),
        ("05_Plateau_Towers", [b for b in ALL_BUILDING_BUILDERS[12:]], 78.0, 18.0),
        ("06_Hero_Props", [b for b in ALL_PROP_BUILDERS], 98.0, 7.5),
    ]

    total_tris = 0
    exported = []
    for col_name, builders, y_row, spacing in groups:
        col = ensure_collection(col_name)
        start_x = -0.5 * (len(builders) - 1) * spacing
        for i, builder_fn in enumerate(builders):
            kb = builder_fn(mats)
            x = start_x + i * spacing
            obj, face_count = kb.build_object(col, location=(x, y_row, 0.0))
            tri_count = sum(max(1, len(p.vertices) - 2) for p in obj.data.polygons)
            total_tris += tri_count
            fbx_path = export_fbx(obj, FBX_OUT_DIR)
            exported.append((obj.name, tri_count, os.path.basename(fbx_path)))
            print(f"[KIT] {obj.name:26s} -> {tri_count:4d} tris | {fbx_path}")

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print(f"[KIT] Saved {BLEND_OUT} ({len(exported)} models, {total_tris} total tris)")

    if do_render and render_dir:
        os.makedirs(render_dir, exist_ok=True)
        render_preview(render_dir, "kit_street", (-14.0, -11.0, 6.0), (0.0, 7.0, 2.5))
        render_preview(render_dir, "kit_villas", (-12.0, 14.0, 9.0), (0.0, 34.0, 2.5))
        render_preview(render_dir, "kit_buildings", (-28.0, 32.0, 14.0), (-6.0, 66.0, 3.0))
        render_preview(render_dir, "kit_props", (-6.0, 88.0, 3.8), (0.0, 98.0, 1.8))


if __name__ == "__main__":
    main()
