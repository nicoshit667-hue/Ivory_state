"""Prépare un personnage MPFB pour Unity mobile et l'exporte en FBX + textures.

Usage : blender --background perso.blend --python mpfb_export.py -- <dossier_sortie>
Sortie : <dossier>/<Nom>.fbx et <dossier>/Textures/<partie>_color.png / _normal.png
"""
import bpy, sys, os, shutil, re

out_dir = sys.argv[sys.argv.index("--") + 1]
tex_dir = os.path.join(out_dir, "Textures")
os.makedirs(tex_dir, exist_ok=True)

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
name = arm.name
print("CHARACTER", name)

# Budget de triangles par partie (mobile : ~20 000 au total).
BUDGET = {"body": 9000, "hair": 2500, "clothes": 3200, "shoes": 1000}


def part_of(ob):
    n = ob.name.lower()
    if n.endswith(".body"):
        return "basemesh"
    if "generic" in n or "proxy" in n or re.search(r"male\d|female\d|_muscle_", n):
        return "body"
    if "eyebrow" in n:
        return "eyebrows"
    if "eyelash" in n:
        return "eyelashes"
    if "low-poly" in n or "high-poly" in n:
        return "eyes"
    if "shoes" in n:
        return "shoes"
    if any(h in n for h in ("short0", "afro", "bob0", "braid", "long0", "ponytail")):
        return "hair"
    return "clothes"


bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None

meshes = []
for ob in list(bpy.data.objects):
    if ob.type != "MESH":
        continue
    part = part_of(ob)
    if part == "basemesh":
        bpy.data.objects.remove(ob, do_unlink=True)
        continue
    meshes.append((ob, part))

view_layer = bpy.context.view_layer


def tris(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


for ob, part in meshes:
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.hide_set(False); ob.hide_viewport = False
    ob.select_set(True)
    view_layer.objects.active = ob
    # Masques (corps caché sous les vêtements) appliqués : ces faces ne sont jamais vues.
    for mod in list(ob.modifiers):
        if mod.type == "MASK":
            bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        elif mod.type not in ("ARMATURE",):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    before = tris(ob)
    target = BUDGET.get(part)
    if target and before > target:
        dec = ob.modifiers.new("Decimate", "DECIMATE")
        dec.decimate_type = "COLLAPSE"
        dec.ratio = target / before
        dec.use_collapse_triangulate = True
        bpy.ops.object.modifier_move_to_index(modifier=dec.name, index=0)
        bpy.ops.object.modifier_apply(modifier=dec.name)
    print(f"PART {ob.name} -> {part}: {before} -> {tris(ob)} tris")


# Matériaux : un nom simple par partie, textures copiées à côté du FBX.
def images_of(mat):
    color = normal = None
    if not mat or not mat.node_tree:
        return None, None
    for n in mat.node_tree.nodes:
        if n.type != "TEX_IMAGE" or not n.image:
            continue
        fn = os.path.basename(n.image.filepath or n.image.name).lower()
        if "normal" in fn:
            normal = n.image
        elif "_ao" in fn or "bump" in fn or "rough" in fn or "spec" in fn:
            continue
        elif color is None:
            color = n.image
    return color, normal


used = {}
for ob, part in meshes:
    for slot in ob.material_slots:
        mat = slot.material
        if mat is None:
            continue
        key = part
        i = 2
        while key in used and used[key] is not mat:
            key = f"{part}{i}"; i += 1
        used[key] = mat
        color, normal = images_of(mat)
        if part == "eyes" and color is not None:
            # L'iris « brown » de MakeHuman tire sur le rouge : ramené à un marron foncé.
            import numpy as np
            px = np.empty(len(color.pixels), dtype=np.float32)
            color.pixels.foreach_get(px)
            px = px.reshape(-1, 4)
            r, g, b = px[:, 0], px[:, 1], px[:, 2]
            reddish = (r > g * 1.25) & (r > b * 1.25)
            lum = 0.3 * r + 0.59 * g + 0.11 * b
            px[reddish, 0] = lum[reddish] * 0.95
            px[reddish, 1] = lum[reddish] * 0.62
            px[reddish, 2] = lum[reddish] * 0.40
            color.pixels.foreach_set(px.ravel())
            color.update()
            print("EYES_RECOLORED", int(reddish.sum()), "pixels")
        for img, suffix in ((color, "color"), (normal, "normal")):
            if img is None:
                continue
            src = bpy.path.abspath(img.filepath)
            dst = os.path.join(tex_dir, f"{key}_{suffix}.png")
            if os.path.exists(src) and src.lower().endswith(".png") and not img.is_dirty:
                shutil.copyfile(src, dst)
            else:
                # Texture modifiée en mémoire (teinte des cheveux) ou autre format : écrite en PNG.
                img.file_format = "PNG"
                img.save(filepath=dst)
            print(f"TEX {key}_{suffix}.png <- {os.path.basename(src)} {tuple(img.size)}")
        mat.name = key

# Un seul maillage (un seul SkinnedMeshRenderer côté Unity).
for o in bpy.context.selected_objects:
    o.select_set(False)
for ob, _ in meshes:
    ob.select_set(True)
view_layer.objects.active = meshes[0][0]
bpy.ops.object.join()
body = view_layer.objects.active
body.name = name + "_Mesh"
body.data.name = name + "_Mesh"
print("TOTAL_TRIS", tris(body), "MATERIALS", [m.name for m in body.data.materials])

# Pose de repos, pas d'animation dans le FBX.
arm.data.pose_position = "REST"
for b in arm.pose.bones:
    b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.rotation_euler = (0, 0, 0); b.scale = (1, 1, 1)
arm.animation_data_clear()

for o in bpy.context.selected_objects:
    o.select_set(False)
arm.select_set(True); body.select_set(True)
view_layer.objects.active = arm
fbx = os.path.join(out_dir, name + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    use_mesh_modifiers=True, mesh_smooth_type="FACE", use_armature_deform_only=True,
    add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
    primary_bone_axis="Y", secondary_bone_axis="X")
print("EXPORTED", fbx, os.path.getsize(fbx))
print("BONES", [b.name for b in arm.data.bones if b.use_deform][:60])
