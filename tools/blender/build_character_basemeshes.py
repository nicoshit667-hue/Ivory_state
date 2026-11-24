"""Builds the Ivory State Male & Female West African modular character basemeshes
and full Customization Blueprint Turnaround scenes in Blender 5.3.

Outputs:
  - tools/blender/IvoryCharacterBasemeshes.blend
      Scenes:
        * Male_Blueprint
        * Female_Blueprint
        * Basemesh_Masters
  - docs/characters/male_basemesh_blueprint.png
  - docs/characters/female_basemesh_blueprint.png

Run headless:
  "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --python tools/blender/build_character_basemeshes.py
"""

import math
import os
import sys
import tempfile
import bpy
import bmesh
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
BLEND_OUT = os.path.join(SCRIPT_DIR, "IvoryCharacterBasemeshes.blend")
RENDER_DIR = os.path.join(REPO_ROOT, "docs", "characters")

from char_builder import build_character_asset  # noqa: E402
from char_materials import (  # noqa: E402
    DEFAULT_SKIN_INDEX,
    SKIN_TONE_PALETTE_SRGB,
    apply_skin_tone_to_material,
    create_image_card_material,
    create_studio_grid_floor_material,
    create_studio_wall_material,
    create_ui_flat_material,
    srgb_rgba,
)


def _set_shapekey_state(info, active_names):
    """Set only the named shape keys (dict name->weight) to weight on body and all synced parts."""
    all_objs = [info["body"], info["eyes"], info["brows"], info["scalp"]] + info["clothes"]
    for obj in all_objs:
        if not obj.data.shape_keys:
            continue
        for kb in obj.data.shape_keys.key_blocks:
            if kb.name == "Basis":
                continue
            kb.value = active_names.get(kb.name, 0.0)


def _setup_world(scene, bg_srgb=(0.30, 0.31, 0.32), strength=0.85):
    world = bpy.data.worlds.new(f"World_{scene.name}")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = srgb_rgba(*bg_srgb, 1.0)
        bg.inputs["Strength"].default_value = strength


def _add_area_light(collection, name, loc, target, energy, size, color_srgb=(1.0, 1.0, 1.0)):
    ld = bpy.data.lights.new(name, "AREA")
    ld.energy = energy
    ld.size = size
    ld.color = srgb_rgba(*color_srgb, 1.0)[:3]
    lo = bpy.data.objects.new(name, ld)
    lo.location = loc
    d = (Vector(target) - Vector(loc)).normalized()
    lo.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    collection.objects.link(lo)
    return lo


def _add_camera(collection, name, loc, target, lens=90.0, ortho=False, ortho_scale=2.0):
    cd = bpy.data.cameras.new(name)
    if ortho:
        cd.type = "ORTHO"
        cd.ortho_scale = ortho_scale
    else:
        cd.lens = lens
    cd.clip_start = 0.05
    cd.clip_end = 100.0
    co = bpy.data.objects.new(name, cd)
    co.location = loc
    d = (Vector(target) - Vector(loc)).normalized()
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    collection.objects.link(co)
    return co


def _create_silhouette_outline_material(name, line_srgb=(0.86, 0.88, 0.90), fill_srgb=(0.18, 0.19, 0.21)):
    """Fresnel contour material used for real 3D Body Morph outline cards."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")

    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.42

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.36
    ramp.color_ramp.elements[0].color = srgb_rgba(*fill_srgb, 1.0)
    ramp.color_ramp.elements[1].position = 0.56
    ramp.color_ramp.elements[1].color = srgb_rgba(*line_srgb, 1.0)

    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs["Strength"].default_value = 1.0

    nt.links.new(lw.outputs["Facing"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], emis.inputs["Color"])
    nt.links.new(emis.outputs["Emission"], out.inputs["Surface"])
    return mat


def _render_stage1_thumbnails(scene, info, gender, thumb_dir):
    """Render genuine 3D Face Preset portraits & Body Morph outline silhouettes from the built character."""
    is_female = (gender.lower() == "female")
    prefix = "Female" if is_female else "Male"
    os.makedirs(thumb_dir, exist_ok=True)

    # Ensure clean world & portrait lighting in staging scene
    _setup_world(scene, bg_srgb=(0.24, 0.25, 0.27), strength=0.9)
    lcol = bpy.data.collections.new(f"__TmpThumb_{prefix}")
    scene.collection.children.link(lcol)

    h = info["height"]
    head_z = h - 0.115
    _add_area_light(lcol, f"ThumbKey_{prefix}", (0.7, -1.3, head_z + 0.4), (0.0, 0.0, head_z), 38.0, 1.0, (1.0, 0.96, 0.92))
    _add_area_light(lcol, f"ThumbFill_{prefix}", (-0.8, -1.2, head_z), (0.0, 0.0, head_z), 20.0, 1.2, (0.88, 0.92, 1.0))
    _add_area_light(lcol, f"ThumbRim_{prefix}", (0.0, 0.9, head_z + 0.3), (0.0, 0.0, head_z), 38.0, 0.9, (0.98, 0.98, 1.0))

    # Dark backdrop behind head for crisp portrait thumbnails (built via bmesh, no operator context needed)
    bm_bg = bmesh.new()
    bmesh.ops.create_grid(
        bm_bg, x_segments=1, y_segments=1, size=0.8,
        matrix=Matrix.Translation((0.0, 0.35, head_z)) @ Matrix.Rotation(math.radians(90.0), 4, "X")
    )
    m_bg = bpy.data.meshes.new(f"__TmpBG_{prefix}")
    bm_bg.to_mesh(m_bg)
    bm_bg.free()
    bg_plane = bpy.data.objects.new(f"__TmpBG_{prefix}", m_bg)
    lcol.objects.link(bg_plane)
    bg_plane.data.materials.append(create_ui_flat_material(f"ThumbBG_{prefix}", (0.20, 0.21, 0.23), 1.0, 0.85))

    face_cam = _add_camera(lcol, f"ThumbFaceCam_{prefix}", (0.0, -0.78, head_z - 0.005), (0.0, -0.04, head_z - 0.005), lens=85.0)
    scene.camera = face_cam
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 256
    scene.render.resolution_y = 256

    # Render 6 diverse Face Presets across distinct West African skin tones
    tone_indices = [2, 0, 3, 1, 4, 2]
    face_paths = []
    for i in range(6):
        sk_name = f"Face_Preset_0{i + 1}"
        _set_shapekey_state(info, {sk_name: 1.0})
        apply_skin_tone_to_material(info["skin_mat"], tone_indices[i])
        out_p = os.path.join(thumb_dir, f"{prefix.lower()}_face_0{i + 1}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        face_paths.append(out_p)

    # Restore default skin & face
    _set_shapekey_state(info, {})
    apply_skin_tone_to_material(info["skin_mat"], DEFAULT_SKIN_INDEX)

    # Render Body Morph outline silhouettes (temporarily swap materials on body & clothes to outline shader)
    outline_mat_std = _create_silhouette_outline_material(f"OutlineStd_{prefix}", (0.85, 0.87, 0.90), (0.16, 0.17, 0.19))
    outline_mat_red = _create_silhouette_outline_material(f"OutlineRed_{prefix}", (0.90, 0.32, 0.32), (0.16, 0.17, 0.19))

    saved_mats = {}
    target_objs = [info["body"], info["scalp"]] + info["clothes"]
    for obj in target_objs:
        saved_mats[obj.name] = [m for m in obj.data.materials]
        obj.data.materials.clear()
        obj.data.materials.append(outline_mat_std)
    info["eyes"].hide_render = True
    info["brows"].hide_render = True
    bg_plane.location = (0.0, 0.45, h * 0.5)
    bg_plane.scale = (3.0, 3.0, 3.0)

    morph_cam = _add_camera(lcol, f"ThumbMorphCam_{prefix}", (0.0, -3.5, h * 0.50), (0.0, 0.0, h * 0.50), ortho=True, ortho_scale=h * 1.12)
    scene.camera = morph_cam
    scene.render.resolution_x = 220
    scene.render.resolution_y = 330

    if not is_female:
        morph_defs = [
            ("skinny", {"Body_Skinny": 1.0}, outline_mat_std),
            ("muscular", {"Body_Muscular": 1.0}, outline_mat_std),
            ("heavy", {"Body_Heavy": 1.0}, outline_mat_std),
        ]
    else:
        morph_defs = [
            ("slender", {"Body_Slender": 1.0}, outline_mat_std),
            ("muscular", {"Body_Muscular": 1.0}, outline_mat_std),
            ("curvy", {"Body_Curvy_Full": 1.0}, outline_mat_std),
            ("bh_slender", {"Body_Slender": 0.65}, outline_mat_red),
            ("bh_bust", {"Bust_Adjust": 1.0, "Hip_Adjust": 0.4}, outline_mat_red),
            ("bh_hip", {"Body_Curvy_Full": 0.6, "Hip_Adjust": 1.0}, outline_mat_red),
        ]

    morph_paths = []
    for tag, sk_map, omat in morph_defs:
        _set_shapekey_state(info, sk_map)
        for obj in target_objs:
            for slot_i in range(len(obj.data.materials)):
                obj.data.materials[slot_i] = omat
        out_p = os.path.join(thumb_dir, f"{prefix.lower()}_morph_{tag}.png")
        scene.render.filepath = out_p
        bpy.ops.render.render(write_still=True)
        morph_paths.append(out_p)

    # Restore original materials & visibility
    _set_shapekey_state(info, {})
    for obj in target_objs:
        obj.data.materials.clear()
        for m in saved_mats[obj.name]:
            obj.data.materials.append(m)
    info["eyes"].hide_render = False
    info["brows"].hide_render = False

    # Clean temporary thumb objects
    for obj in list(lcol.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(lcol)
    return face_paths, morph_paths


def _create_ui_quad(col, name, center_xyz, width, height, mat, border_mat=None, border_pad=0.004):
    """Create a vertical UI card plane facing -Y in the XZ plane with exact UV mapping."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    hw, hh = width * 0.5, height * 0.5
    cx, cy, cz = center_xyz
    v0 = bm.verts.new((cx - hw, cy, cz - hh))
    v1 = bm.verts.new((cx + hw, cy, cz - hh))
    v2 = bm.verts.new((cx + hw, cy, cz + hh))
    v3 = bm.verts.new((cx - hw, cy, cz + hh))
    f = bm.faces.new((v0, v1, v2, v3))
    uvs = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
    for i, loop in enumerate(f.loops):
        loop[uv].uv = uvs[i]

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    col.objects.link(obj)
    obj.data.materials.append(mat)

    if border_mat is not None:
        _create_ui_quad(col, f"{name}_Border", (cx, cy + 0.0015, cz), width + border_pad * 2, height + border_pad * 2, border_mat)
    return obj


def _create_ui_text(col, name, text_str, loc_xyz, size, mat, align_x="LEFT", align_y="CENTER"):
    """Create crisp upright 3D text facing -Y in the studio scene."""
    curve = bpy.data.curves.new(name, type="FONT")
    curve.body = text_str
    curve.size = size
    curve.align_x = align_x
    curve.align_y = align_y
    curve.space_line = 1.15
    obj = bpy.data.objects.new(name, curve)
    obj.location = loc_xyz
    obj.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def _create_pointer_callout(col, name, targets_xyz, label_xyz, title_text, sub_text, line_mat, dot_mat, text_mat, align="LEFT"):
    """Create thin white 3D pointer line(s) from dot(s) on the basemesh to a crisp technical callout label."""
    lx, ly, lz = label_xyz
    # Place text
    _create_ui_text(col, f"{name}_Title", title_text, (lx, ly, lz + 0.022), 0.040, text_mat, align_x=align, align_y="BOTTOM")
    _create_ui_text(col, f"{name}_Sub", sub_text, (lx, ly, lz + 0.012), 0.030, text_mat, align_x=align, align_y="TOP")

    # Line endpoint next to text
    if align == "LEFT":
        end_pt = Vector((lx - 0.020, ly, lz + 0.015))
    elif align == "RIGHT":
        end_pt = Vector((lx + 0.020, ly, lz + 0.015))
    else:
        end_pt = Vector((lx, ly, lz + 0.055))

    for idx, t_xyz in enumerate(targets_xyz):
        p0 = Vector(t_xyz)
        # Dot at target
        bm_d = bmesh.new()
        bmesh.ops.create_icosphere(bm_d, subdivisions=2, radius=0.0085)
        for v in bm_d.verts:
            v.co += p0
        dm = bpy.data.meshes.new(f"{name}_Dot_{idx}")
        bm_d.to_mesh(dm)
        bm_d.free()
        dobj = bpy.data.objects.new(f"{name}_Dot_{idx}", dm)
        col.objects.link(dobj)
        dobj.data.materials.append(dot_mat)

        # Thin tube line from p0 to end_pt
        bm_l = bmesh.new()
        vec = end_pt - p0
        length = vec.length
        if length > 1e-4:
            mid = (p0 + end_pt) * 0.5
            quat = Vector((0.0, 0.0, 1.0)).rotation_difference(vec.normalized())
            xf = Matrix.Translation(mid) @ quat.to_matrix().to_4x4()
            bmesh.ops.create_cone(bm_l, cap_ends=True, segments=8, radius1=0.0024, radius2=0.0024, depth=length, matrix=xf)
        lm = bpy.data.meshes.new(f"{name}_Line_{idx}")
        bm_l.to_mesh(lm)
        bm_l.free()
        lobj = bpy.data.objects.new(f"{name}_Line_{idx}", lm)
        col.objects.link(lobj)
        lobj.data.materials.append(line_mat)


def _build_blueprint_scene(scene_name, master_col, info, gender, face_paths, morph_paths):
    """Assemble a complete Ivory State Male/Female Blueprint Turnaround Studio Scene."""
    is_female = (gender.lower() == "female")
    prefix = "Female" if is_female else "Male"

    scene = bpy.data.scenes.new(scene_name)
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    _setup_world(scene, bg_srgb=(0.35, 0.36, 0.37), strength=0.65)

    # Collections
    col_turn = bpy.data.collections.new(f"01_Turnaround_{prefix}")
    col_hud = bpy.data.collections.new(f"02_HUD_{prefix}")
    col_studio = bpy.data.collections.new(f"03_Studio_{prefix}")
    for c in (col_turn, col_hud, col_studio):
        scene.collection.children.link(c)

    # 1. Studio Grid Floor & Horizontal Measurement Backdrop Wall
    bm_f = bmesh.new()
    bmesh.ops.create_grid(bm_f, x_segments=1, y_segments=1, size=5.5, matrix=Matrix.Translation((0.0, 0.2, 0.0)))
    mf = bpy.data.meshes.new(f"Studio_Floor_{prefix}")
    bm_f.to_mesh(mf)
    bm_f.free()
    floor_obj = bpy.data.objects.new(f"Studio_Floor_{prefix}", mf)
    col_studio.objects.link(floor_obj)
    floor_obj.data.materials.append(create_studio_grid_floor_material())

    bm_w = bmesh.new()
    rot_w = Matrix.Translation((0.0, 1.35, 1.6)) @ Matrix.Rotation(math.radians(90.0), 4, "X")
    bmesh.ops.create_grid(bm_w, x_segments=1, y_segments=1, size=5.5, matrix=rot_w)
    mw = bpy.data.meshes.new(f"Studio_Backdrop_{prefix}")
    bm_w.to_mesh(mw)
    bm_w.free()
    wall_obj = bpy.data.objects.new(f"Studio_Backdrop_{prefix}", mw)
    col_studio.objects.link(wall_obj)
    wall_obj.data.materials.append(create_studio_wall_material())

    # 2. 3-Point Studio Lighting & Blueprint Camera
    _add_area_light(col_studio, f"Key_{prefix}", (2.4, -3.8, 2.7), (0.2, 0.0, 0.95), 240.0, 2.6, (1.0, 0.97, 0.94))
    _add_area_light(col_studio, f"Fill_{prefix}", (-2.8, -3.2, 1.6), (0.2, 0.0, 0.95), 115.0, 3.2, (0.88, 0.92, 1.0))
    _add_area_light(col_studio, f"Rim_{prefix}", (0.3, 1.15, 2.5), (0.3, 0.0, 1.05), 145.0, 2.8, (0.98, 0.98, 1.0))

    cam = _add_camera(col_studio, f"Cam_{prefix}_Blueprint", (0.0, -10.55, 1.04), (0.0, 0.0, 1.02), lens=88.0)
    scene.camera = cam

    # 3. Side-by-side Turnaround Collection Instances (FRONT, SIDE facing right, BACK)
    x_front, x_side, x_back = -0.48, 0.52, 1.34
    turn_specs = [
        ("FRONT", x_front, 0.0),
        ("SIDE",  x_side,  90.0),
        ("BACK",  x_back,  180.0),
    ]
    for label, tx, rz in turn_specs:
        inst = bpy.data.objects.new(f"{prefix}_{label}", None)
        inst.instance_type = "COLLECTION"
        inst.instance_collection = master_col
        inst.location = (tx, 0.0, 0.0)
        inst.rotation_euler = (0.0, 0.0, math.radians(rz))
        col_turn.objects.link(inst)

    # 4. Blueprint Header Banner & View Labels
    mat_banner = create_ui_flat_material("UI_BannerDark", (0.14, 0.15, 0.16), 0.96, 0.95)
    mat_panel = create_ui_flat_material("UI_PanelSlate", (0.20, 0.21, 0.23), 0.90, 0.92)
    mat_border = create_ui_flat_material("UI_PanelBorder", (0.38, 0.40, 0.42), 1.0, 0.95)
    mat_gold = create_ui_flat_material("UI_GoldActive", (0.85, 0.66, 0.24), 1.0, 1.0)
    mat_white = create_ui_flat_material("UI_TextWhite", (0.94, 0.95, 0.96), 1.0, 1.0)
    mat_muted = create_ui_flat_material("UI_TextMuted", (0.76, 0.78, 0.80), 1.0, 0.95)

    # Top header bar flush across upper edge of frame
    _create_ui_quad(col_hud, f"{prefix}_TopBar", (0.0, -0.25, 2.34), 5.0, 0.34, mat_banner)
    header_txt = f"IVORY STATE - {prefix.upper()} BASEMESH - CUSTOMIZATION BLUEPRINT"
    _create_ui_text(col_hud, f"{prefix}_HeaderTitle", header_txt, (-2.02, -0.27, 2.31), 0.076, mat_white, align_x="LEFT", align_y="CENTER")

    h = info["height"]
    label_z = max(1.94, h + 0.14)
    for label, tx, _ in turn_specs:
        _create_ui_text(col_hud, f"{prefix}_View_{label}", label, (tx, -0.26, label_z), 0.064, mat_white, align_x="CENTER", align_y="CENTER")

    # Right-side height markers cleanly to the right of BACK hand
    for cm in (0, 30, 60, 90, 120, 150, 180):
        z_m = cm / 100.0
        _create_ui_text(col_hud, f"{prefix}_Tick_{cm}", f"{cm} cm", (2.06, 1.32, z_m + 0.012), 0.028, mat_muted, align_x="RIGHT", align_y="BOTTOM")

    # 5. Left 'CUSTOMIZATION SLOTS' HUD Panel
    px_center = -1.62
    panel_y = -0.24
    panel_w = 0.76
    panel_h = 1.80 if is_female else 1.38
    panel_z = 1.03 if is_female else 1.24
    _create_ui_quad(col_hud, f"{prefix}_SlotsPanel", (px_center, panel_y, panel_z), panel_w, panel_h, mat_panel, border_mat=mat_border, border_pad=0.005)

    top_z = panel_z + panel_h * 0.5
    _create_ui_text(col_hud, f"{prefix}_SlotsHeader", "CUSTOMIZATION SLOTS", (px_center - panel_w * 0.45, panel_y - 0.01, top_z - 0.065), 0.044, mat_white, align_x="LEFT", align_y="CENTER")

    # FACE PRESETS (2 rows x 3 cols)
    _create_ui_text(col_hud, f"{prefix}_FaceLabel", "FACE PRESETS", (px_center - panel_w * 0.45, panel_y - 0.01, top_z - 0.145), 0.036, mat_white, align_x="LEFT", align_y="CENTER")
    box_s = 0.195
    gap_x = 0.025
    gap_y = 0.025
    start_x = px_center - (box_s + gap_x)
    row0_z = top_z - 0.285
    for i, fpath in enumerate(face_paths):
        row = i // 3
        col_i = i % 3
        cx = start_x + col_i * (box_s + gap_x)
        cz = row0_z - row * (box_s + gap_y)
        fmat = create_image_card_material(f"{prefix}_FaceThumb_{i + 1}", fpath)
        bmat = mat_gold if i == 0 else mat_border
        _create_ui_quad(col_hud, f"{prefix}_FaceCard_{i + 1}", (cx, panel_y - 0.012, cz), box_s, box_s, fmat, border_mat=bmat, border_pad=0.005)

    # SKIN TONES (8-swatch gradient bar)
    skin_label_z = row0_z - box_s - 0.165
    _create_ui_text(col_hud, f"{prefix}_SkinLabel", "SKIN TONES", (px_center - panel_w * 0.45, panel_y - 0.01, skin_label_z), 0.036, mat_white, align_x="LEFT", align_y="CENTER")
    sw_total_w = 0.68
    sw_w = sw_total_w / len(SKIN_TONE_PALETTE_SRGB)
    sw_h = 0.115
    sw_z = skin_label_z - 0.095
    for idx, (tname, srgb) in enumerate(SKIN_TONE_PALETTE_SRGB):
        sx = (px_center - sw_total_w * 0.5) + (idx + 0.5) * sw_w
        smat = create_ui_flat_material(f"UI_SkinSwatch_{idx}", srgb, 1.0, 0.95)
        _create_ui_quad(col_hud, f"{prefix}_Swatch_{idx}", (sx, panel_y - 0.012, sw_z), sw_w - 0.002, sw_h, smat)
    # Border around full swatch bar
    _create_ui_quad(col_hud, f"{prefix}_SwatchFrame", (px_center, panel_y - 0.010, sw_z), sw_total_w + 0.008, sw_h + 0.008, mat_border)

    # BODY MORPHS (Row 1: 3 cards; plus Row 2 for Female Bust & Hip!)
    morph_label_z = sw_z - 0.115
    _create_ui_text(col_hud, f"{prefix}_MorphLabel", "BODY MORPHS", (px_center - panel_w * 0.45, panel_y - 0.01, morph_label_z), 0.036, mat_white, align_x="LEFT", align_y="CENTER")
    mw_card = 0.195
    mh_card = 0.285
    m_row1_z = morph_label_z - 0.185
    for idx in range(3):
        cx = start_x + idx * (mw_card + gap_x)
        mmat = create_image_card_material(f"{prefix}_MorphCard_{idx + 1}", morph_paths[idx])
        _create_ui_quad(col_hud, f"{prefix}_MorphCard_{idx + 1}", (cx, panel_y - 0.012, m_row1_z), mw_card, mh_card, mmat, border_mat=mat_border, border_pad=0.004)

    morph_caption = "Slender / Muscular / Curvy/Full" if is_female else "Skinny / Muscular / Heavy"
    _create_ui_text(col_hud, f"{prefix}_MorphCaption", morph_caption, (px_center, panel_y - 0.014, m_row1_z - mh_card * 0.5 - 0.032), 0.031, mat_white, align_x="CENTER", align_y="CENTER")

    if is_female and len(morph_paths) >= 6:
        m_row2_z = m_row1_z - mh_card - 0.115
        for idx in range(3):
            cx = start_x + idx * (mw_card + gap_x)
            mmat = create_image_card_material(f"{prefix}_BustHipCard_{idx + 1}", morph_paths[3 + idx])
            _create_ui_quad(col_hud, f"{prefix}_BustHipCard_{idx + 1}", (cx, panel_y - 0.012, m_row2_z), mw_card, mh_card, mmat, border_mat=mat_border, border_pad=0.004)
        _create_ui_text(col_hud, f"{prefix}_BustHipCaption", "Bust & Hip", (px_center, panel_y - 0.014, m_row2_z - mh_card * 0.5 - 0.032), 0.031, mat_white, align_x="CENTER", align_y="CENTER")

    # 6. 3D Pointer Lines & Technical Attachment Labels (non-crossing, anchored to actual sockets!)
    def socket_on_instance(slot_name, inst_x, rz_deg):
        emp = info["sockets"][slot_name]
        local_pos = emp.matrix_world.translation.copy()
        rot_m = Matrix.Rotation(math.radians(rz_deg), 3, "Z")
        return Vector((inst_x, 0.0, 0.0)) + (rot_m @ local_pos)

    head_pt = Vector((x_front + 0.075, -0.04, h - 0.095))
    wrist_pt = socket_on_instance("WRIST_L", x_front, 0.0)
    finger_pt = socket_on_instance("FINGER_L", x_front, 0.0)
    hips_pt = socket_on_instance("TORSO_LEGS", 0.0, 0.0)
    torso_side_pt = Vector((x_side + 0.11, -0.04, hips_pt.z - 0.02))
    torso_back_pt = Vector((x_back - 0.145, -0.04, hips_pt.z - 0.02))
    foot_side_pt = Vector((x_side + 0.105, -0.02, 0.032))
    foot_back_pt = Vector((x_back - 0.110, -0.02, 0.055))

    _create_pointer_callout(
        col_hud, f"{prefix}_Ptr_HEAD", [head_pt],
        (x_front + 0.26, -0.15, h - 0.08), "HEAD", "(Hair/Hats/Masks)",
        mat_white, mat_white, mat_white, align="LEFT"
    )
    _create_pointer_callout(
        col_hud, f"{prefix}_Ptr_WRIST", [wrist_pt],
        (0.17, -0.15, wrist_pt.z + 0.16), "WRIST", "(Watch)",
        mat_white, mat_white, mat_white, align="CENTER"
    )
    _create_pointer_callout(
        col_hud, f"{prefix}_Ptr_FINGER", [finger_pt],
        (0.17, -0.15, finger_pt.z - 0.15), "FINGER", "(Ring)",
        mat_white, mat_white, mat_white, align="CENTER"
    )
    _create_pointer_callout(
        col_hud, f"{prefix}_Ptr_TORSO", [torso_side_pt, torso_back_pt],
        ((x_side + x_back) * 0.5, -0.15, hips_pt.z - 0.30), "TORSO/LEGS", "(Complete Outfits)",
        mat_white, mat_white, mat_white, align="CENTER"
    )
    _create_pointer_callout(
        col_hud, f"{prefix}_Ptr_FEET", [foot_side_pt, foot_back_pt],
        ((x_side + x_back) * 0.5, -0.15, 0.085), "FEET", "(Footwear)",
        mat_white, mat_white, mat_white, align="CENTER"
    )

    return scene


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    master_scene = bpy.context.scene
    master_scene.name = "Basemesh_Masters"
    master_scene.unit_settings.system = "METRIC"
    master_scene.unit_settings.scale_length = 1.0

    # Build Male and Female Master Collections inside Basemesh_Masters
    col_male = bpy.data.collections.new("Male_Master")
    col_female = bpy.data.collections.new("Female_Master")
    master_scene.collection.children.link(col_male)
    master_scene.collection.children.link(col_female)

    print("[IVORY] Building West African Male Basemesh...", flush=True)
    male_info = build_character_asset("male", col_male)
    print(f"[IVORY] Male built: height={male_info['height']:.3f}m, shapekeys={len(male_info['body'].data.shape_keys.key_blocks)}", flush=True)

    print("[IVORY] Building West African Female Basemesh...", flush=True)
    female_info = build_character_asset("female", col_female)
    print(f"[IVORY] Female built: height={female_info['height']:.3f}m, shapekeys={len(female_info['body'].data.shape_keys.key_blocks)}", flush=True)

    # Temporarily isolate each master during Stage 1 thumbnail rendering
    thumb_dir = os.path.join(tempfile.gettempdir(), "ivory_char_thumbs")

    # Hide Female while rendering Male thumbs
    master_scene.view_layers[0].layer_collection.children["Female_Master"].exclude = True
    male_faces, male_morphs = _render_stage1_thumbnails(master_scene, male_info, "male", thumb_dir)
    master_scene.view_layers[0].layer_collection.children["Female_Master"].exclude = False

    # Hide Male while rendering Female thumbs
    master_scene.view_layers[0].layer_collection.children["Male_Master"].exclude = True
    female_faces, female_morphs = _render_stage1_thumbnails(master_scene, female_info, "female", thumb_dir)
    master_scene.view_layers[0].layer_collection.children["Male_Master"].exclude = False

    # Build both Blueprint Studio Scenes (Collection Instances reference origin (0,0,0) in master collections)
    print("[IVORY] Assembling Male_Blueprint & Female_Blueprint scenes...", flush=True)
    male_scene = _build_blueprint_scene("Male_Blueprint", col_male, male_info, "male", male_faces, male_morphs)
    female_scene = _build_blueprint_scene("Female_Blueprint", col_female, female_info, "female", female_faces, female_morphs)

    # In Basemesh_Masters scene, offset ONLY the viewport preview or leave at origin?
    # Note: Rig locations MUST stay at (0,0,0) inside Male_Master/Female_Master so Collection Instances align!
    # We can put a nice light & camera in Basemesh_Masters and toggle visibility or view via scenes.
    _setup_world(master_scene, bg_srgb=(0.28, 0.29, 0.30), strength=0.85)
    _add_area_light(master_scene.collection, "MasterKey", (2.0, -3.0, 2.4), (0.0, 0.0, 0.9), 180.0, 2.2)
    _add_area_light(master_scene.collection, "MasterFill", (-2.2, -2.5, 1.5), (0.0, 0.0, 0.9), 90.0, 2.5)
    mcam = _add_camera(master_scene.collection, "MasterCam", (0.0, -4.0, 0.95), (0.0, 0.0, 0.90), lens=65.0)
    master_scene.camera = mcam

    # Set Male_Blueprint as active window scene before saving
    bpy.context.window.scene = male_scene

    os.makedirs(RENDER_DIR, exist_ok=True)
    male_out = os.path.join(RENDER_DIR, "male_basemesh_blueprint.png")
    female_out = os.path.join(RENDER_DIR, "female_basemesh_blueprint.png")

    for sc, out_path in [(male_scene, male_out), (female_scene, female_out)]:
        bpy.context.window.scene = sc
        sc.render.engine = "BLENDER_EEVEE"
        sc.render.resolution_x = 1920
        sc.render.resolution_y = 1280
        sc.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
        print(f"[IVORY] Rendered {out_path}", flush=True)

    # Save master .blend file with packed thumbnail images
    bpy.context.window.scene = male_scene
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print(f"[IVORY] Saved {BLEND_OUT}", flush=True)


if __name__ == "__main__":
    main()
