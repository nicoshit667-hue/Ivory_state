import bpy, sys, math
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene

meshes = [o for o in scene.objects if o.type == "MESH" and o.visible_get()]
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
zmin = min(p.z for p in pts); zmax = max(p.z for p in pts)
h = zmax - zmin
print("HEIGHT_M", round(h, 3))

sun = bpy.data.lights.new("Key", "SUN"); sun.energy = 3.5; sun.angle = math.radians(8)
so = bpy.data.objects.new("Key", sun); so.rotation_euler = (math.radians(55), 0, math.radians(-35))
scene.collection.objects.link(so)
fill = bpy.data.lights.new("Fill", "AREA"); fill.energy = 250; fill.size = 3
fo = bpy.data.objects.new("Fill", fill); fo.location = (2.5, -2.5, 1.6)
fo.rotation_euler = (math.radians(70), 0, math.radians(45)); scene.collection.objects.link(fo)
world = bpy.data.worlds.new("W"); scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (0.62, 0.66, 0.72, 1); bg.inputs["Strength"].default_value = 0.6

cam = bpy.data.cameras.new("Cam"); cam.lens = 70
co = bpy.data.objects.new("Cam", cam); scene.collection.objects.link(co); scene.camera = co
r = scene.render
r.resolution_x, r.resolution_y = 900, 1200
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        r.engine = eng; break
    except TypeError:
        pass

views = {"full": (0, zmin + h * 0.5, 4.6), "full34": (35, zmin + h * 0.5, 4.6), "face": (15, zmin + h * 0.92, 1.1)}
for name, (ang, tz, dist) in views.items():
    a = math.radians(ang)
    target = Vector((0, 0, tz))
    co.location = target + Vector((dist * math.sin(a), -dist * math.cos(a), 0.05 * dist))
    co.rotation_euler = (target - co.location).to_track_quat("-Z", "Y").to_euler()
    r.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("RENDERED", r.filepath)
