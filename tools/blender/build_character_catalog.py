"""Master builder for Ivory State Modular Character Studio & Analysis Catalog.

Generates:
  1. tools/blender/IvoryCharacterStudio.blend
       - Interactive dressing-room scenes: Male_Wardrobe_Live & Female_Wardrobe_Live
       - Upgraded anatomical Male & Female basemeshes (mixamo_unity rigged)
       - 10 Male + 10 Female Face Presets (Shape Keys) & 10 West African Skin Tones
       - 10 Male + 10 Female 3D Hairstyles (rigged & face-synced)
       - 10 Male + 10 Female Wearable 3D Outfits (rigged & body-morph-synced)
       - 5 Male + 5 Female 3D Shoe Pairs + Complete Male & Female Accessories
       - Embedded live Blender N-Panel ('Ivory State' tab -> ivory_wardrobe_ui.py)
  2. docs/characters/catalog/ (8 high-resolution studio analysis PNGs):
       - 01_body_anatomy_upgraded.png
       - 02_male_faces_10.png
       - 03_female_faces_10.png
       - 04_male_hair_10.png
       - 05_female_hair_10.png
       - 06_male_outfits_10.png
       - 07_female_outfits_10.png
       - 08_shoes_and_accessories.png
"""

import math
import os
import sys
import bpy
import bmesh
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
BLEND_OUT = os.path.join(SCRIPT_DIR, "IvoryCharacterStudio.blend")
CATALOG_DIR = os.path.join(REPO_ROOT, "docs", "characters", "catalog")

from char_builder import (  # noqa: E402
    FEMALE_FACE_PRESETS,
    MALE_FACE_PRESETS,
    build_character_asset,
)
from char_hair import (  # noqa: E402
    FEMALE_HAIR_SPECS,
    MALE_HAIR_SPECS,
    build_female_hairstyles,
    build_male_hairstyles,
)
from char_wardrobe import (  # noqa: E402
    FEMALE_OUTFIT_SPECS,
    MALE_OUTFIT_SPECS,
    build_female_wardrobe,
    build_male_wardrobe,
)
from char_accessories import (  # noqa: E402
    FEMALE_ACCESSORY_SPECS,
    FEMALE_SHOE_SPECS,
    MALE_ACCESSORY_SPECS,
    MALE_SHOE_SPECS,
    build_accessories,
    build_footwear,
)
from char_materials import (  # noqa: E402
    DEFAULT_SKIN_INDEX,
    SKIN_TONE_PALETTE_SRGB,
    apply_skin_tone_to_material,
    clone_skin_material_with_tone,
    create_studio_grid_floor_material,
    create_studio_wall_material,
    create_ui_flat_material,
    srgb_rgba,
)


# ---------------------------------------------------------------------------
# Embedded Live Blender N-Panel Customizer Script (stored inside .blend!)
# ---------------------------------------------------------------------------
EMBEDDED_UI_SCRIPT = r'''"""Ivory State Character Wardrobe & Customizer N-Panel (View3D -> Sidebar -> Ivory State)."""
import random
import bpy

SKIN_PALETTE_SRGB = [
    ("Obsidian",     (0.14, 0.085, 0.062)),
    ("Deep Ebony",   (0.18, 0.112, 0.078)),
    ("Espresso",     (0.23, 0.145, 0.100)),
    ("Mahogany",     (0.28, 0.180, 0.124)),
    ("Rich Umber",   (0.33, 0.215, 0.148)),
    ("Warm Cocoa",   (0.37, 0.248, 0.172)),
    ("Chestnut",     (0.41, 0.278, 0.194)),
    ("Warm Bronze",  (0.45, 0.308, 0.216)),
    ("Deep Caramel", (0.49, 0.340, 0.238)),
    ("Honey Amber",  (0.53, 0.372, 0.262)),
]

MALE_FACES = [
    ("NONE", "Base Hero", ""),
    ("Face_Preset_01", "M01 Kouadio", ""), ("Face_Preset_02", "M02 Traore", ""),
    ("Face_Preset_03", "M03 Konan", ""),   ("Face_Preset_04", "M04 Bamba", ""),
    ("Face_Preset_05", "M05 Yao", ""),     ("Face_Preset_06", "M06 Diallo", ""),
    ("Face_Preset_07", "M07 Kouassi", ""), ("Face_Preset_08", "M08 Soro", ""),
    ("Face_Preset_09", "M09 N'Guessan", ""), ("Face_Preset_10", "M10 Ouattara", ""),
]
FEMALE_FACES = [
    ("NONE", "Base Heroine", ""),
    ("Face_Preset_01", "F01 Aya", ""),      ("Face_Preset_02", "F02 Aminata", ""),
    ("Face_Preset_03", "F03 Fatou", ""),    ("Face_Preset_04", "F04 Adjoua", ""),
    ("Face_Preset_05", "F05 Mariam", ""),   ("Face_Preset_06", "F06 Aissata", ""),
    ("Face_Preset_07", "F07 Bintou", ""),   ("Face_Preset_08", "F08 Clarisse", ""),
    ("Face_Preset_09", "F09 Salimata", ""), ("Face_Preset_10", "F10 Kadiatou", ""),
]

MALE_HAIRS = [
    ("BUZZ", "00 Buzz Cut (Base)", ""),
    ("Male_Hair_01_LowFade", "01 Low Fade", ""),         ("Male_Hair_02_HighTopFade", "02 High-Top Fade", ""),
    ("Male_Hair_03_ShortAfroTaper", "03 Afro Taper", ""), ("Male_Hair_04_Waves360", "04 360 Waves", ""),
    ("Male_Hair_05_SpongeTwists", "05 Sponge Twists", ""), ("Male_Hair_06_Cornrows", "06 Cornrows", ""),
    ("Male_Hair_07_ShortLocs", "07 Short Locs", ""),     ("Male_Hair_08_HighTopLocs", "08 High-Top Locs", ""),
    ("Male_Hair_09_FullAfro", "09 Full Afro", ""),       ("Male_Hair_10_BurstFadeMohawk", "10 Burst Mohawk", ""),
]
FEMALE_HAIRS = [
    ("BUZZ", "00 Shaved (Base)", ""),
    ("Female_Hair_01_ShortTWA", "01 Short TWA", ""),            ("Female_Hair_02_NaturalAfroCrown", "02 Afro Crown", ""),
    ("Female_Hair_03_HighAfroPuff", "03 High Puff", ""),        ("Female_Hair_04_DoubleAfroPuffs", "04 Double Puffs", ""),
    ("Female_Hair_05_LongBoxBraids", "05 Long Box Braids", ""), ("Female_Hair_06_FulaniCornrows", "06 Fulani Braids", ""),
    ("Female_Hair_07_HighBraidedBun", "07 Braided Bun", ""),    ("Female_Hair_08_BraidedBob", "08 Braided Bob", ""),
    ("Female_Hair_09_GoddessLocs", "09 Goddess Locs", ""),      ("Female_Hair_10_CurlySidePart", "10 Curly Side-Part", ""),
]

MALE_OUTFITS = [
    ("BASE", "00 Boxer Briefs (Base)", ""),
    ("Male_Outfit_01_Abidjan_Streetwear", "01 Abidjan Streetwear", ""),
    ("Male_Outfit_02_Plateau_Business", "02 Plateau Business", ""),
    ("Male_Outfit_03_Cocody_SmartCasual", "03 Cocody Smart Polo", ""),
    ("Male_Outfit_04_Wax_Dashiki_Set", "04 Wax Dashiki Tunic", ""),
    ("Male_Outfit_05_Nightlife_Biker", "05 Marcory Biker Leather", ""),
    ("Male_Outfit_06_Lagune_Sport", "06 Lagune Sport Hoodie", ""),
    ("Male_Outfit_07_GrandBassam_Resort", "07 Grand-Bassam Linen", ""),
    ("Male_Outfit_08_Kita_Royal_Boubou", "08 Royal Kita Boubou", ""),
    ("Male_Outfit_09_Treichville_Denim", "09 Treichville Denim", ""),
    ("Male_Outfit_10_Gym_Elephants", "10 Elephants Gym Kit", ""),
]
FEMALE_OUTFITS = [
    ("BASE", "00 Sports Bra & Shorts (Base)", ""),
    ("Female_Outfit_01_Abidjan_Wax_Dress", "01 Abidjan Wax Dress", ""),
    ("Female_Outfit_02_Plateau_Executive", "02 Plateau Boss Blazer", ""),
    ("Female_Outfit_03_Cocody_Chic_Crop", "03 Cocody Chic Denim", ""),
    ("Female_Outfit_04_Kita_Royal_Gown", "04 Royal Kita Gown", ""),
    ("Female_Outfit_05_Lagune_Athleisure", "05 Lagune Athleisure", ""),
    ("Female_Outfit_06_GrandBassam_Resort", "06 Grand-Bassam Dress", ""),
    ("Female_Outfit_07_Marcory_Biker_Night", "07 Marcory Night Moto", ""),
    ("Female_Outfit_08_Wax_Pagne_Ensemble", "08 Wax Pagne & Blouse", ""),
    ("Female_Outfit_09_Street_Hoodie_Cargo", "09 Urban Hoodie Cargo", ""),
    ("Female_Outfit_10_Smart_Jumpsuit", "10 Emerald Jumpsuit", ""),
]

MALE_SHOES = [
    ("NONE", "00 Barefoot", ""),
    ("Male_Shoe_01_Urban_Sneakers", "01 Urban Sneakers", ""),
    ("Male_Shoe_02_Leather_Loafers", "02 Leather Loafers", ""),
    ("Male_Shoe_03_Abidjan_Slides", "03 Abidjan Slides", ""),
    ("Male_Shoe_04_Wheat_Boots", "04 Wheat Boots", ""),
    ("Male_Shoe_05_Sport_Runners", "05 Sport Runners", ""),
]
FEMALE_SHOES = [
    ("NONE", "00 Barefoot", ""),
    ("Female_Shoe_01_White_Sneakers", "01 White Sneakers", ""),
    ("Female_Shoe_02_Classic_Pumps", "02 Classic Pumps", ""),
    ("Female_Shoe_03_Strappy_Sandals", "03 Strappy Sandals", ""),
    ("Female_Shoe_04_Ankle_Boots", "04 Ankle Boots", ""),
    ("Female_Shoe_05_Fitness_Runners", "05 Fitness Runners", ""),
]

MALE_ACCS = [
    ("acc_1", "Male_Acc_01_Aviator_Glasses", "Aviator Sunglasses"),
    ("acc_2", "Male_Acc_02_Snapback_Cap", "Snapback Cap"),
    ("acc_3", "Male_Acc_03_Bucket_Hat", "Wax Bucket Hat"),
    ("acc_4", "Male_Acc_04_Luxury_Watch", "Gold Watch"),
    ("acc_5", "Male_Acc_05_Gold_Chain", "Cuban Gold Chain"),
    ("acc_6", "Male_Acc_06_Signet_Ring", "Gold Signet Ring"),
    ("acc_7", "Male_Acc_07_Crossbody_Bag", "Crossbody Sling Bag"),
]
FEMALE_ACCS = [
    ("acc_1", "Female_Acc_01_CatEye_Glasses", "Cat-Eye Sunglasses"),
    ("acc_2", "Female_Acc_02_Wax_Headwrap", "Royal Wax Headwrap"),
    ("acc_3", "Female_Acc_03_Hoop_Earrings", "Gold Hoop Earrings"),
    ("acc_4", "Female_Acc_04_Beaded_Necklace", "Collar Necklace"),
    ("acc_5", "Female_Acc_05_Luxury_Watch", "Rose-Gold Watch"),
    ("acc_6", "Female_Acc_06_Gold_Bangles", "Gold Bangles"),
    ("acc_7", "Female_Acc_07_Gold_Ring", "Gold Band Ring"),
    ("acc_8", "Female_Acc_08_Crossbody_Bag", "Leather Crossbody Bag"),
]


def _srgb_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _rgba(r, g, b):
    return (_srgb_lin(r), _srgb_lin(g), _srgb_lin(b), 1.0)


def _is_female_scene(scene):
    return "Female" in scene.name


def _set_obj_vis(obj, visible):
    if obj:
        obj.hide_viewport = not visible
        obj.hide_render = not visible


def _sync_character(scene):
    props = scene.ivory_wardrobe
    is_fem = _is_female_scene(scene)
    prefix = "Female" if is_fem else "Male"

    # 1. Skin tone
    mat = bpy.data.materials.get(f"Ivory_{prefix}_Skin")
    if mat and mat.use_nodes:
        idx = int(props.skin_tone)
        _, (sr, sg, sb) = SKIN_PALETTE_SRGB[idx]
        for n in mat.node_tree.nodes:
            if n.type == "GROUP" and n.node_tree and "MpfbSkinMasterColor" in n.node_tree.name:
                if "SkinColor" in n.inputs:
                    n.inputs["SkinColor"].default_value = _rgba(sr, sg, sb)
                if "LipsColor" in n.inputs:
                    n.inputs["LipsColor"].default_value = _rgba(sr * 0.76 + 0.045, sg * 0.56 + 0.020, sb * 0.56 + 0.020)
                if "EyelidColor" in n.inputs:
                    n.inputs["EyelidColor"].default_value = _rgba(sr * 0.70, sg * 0.66, sb * 0.64)

    # 2. Shape Keys (Face + Body Morphs) across ALL meshes parented to rig
    rig = bpy.data.objects.get(f"{prefix}_Rig")
    face_choice = props.female_face if is_fem else props.male_face
    sk_values = {}
    if not is_fem:
        sk_values["Body_Skinny"] = props.morph_slim
        sk_values["Body_Muscular"] = props.morph_muscular
        sk_values["Body_Heavy"] = props.morph_heavy
    else:
        sk_values["Body_Slender"] = props.morph_slim
        sk_values["Body_Muscular"] = props.morph_muscular
        sk_values["Body_Curvy_Full"] = props.morph_heavy
        sk_values["Bust_Adjust"] = props.morph_bust
        sk_values["Hip_Adjust"] = props.morph_hip
    for i in range(1, 11):
        fname = f"Face_Preset_{i:02d}"
        sk_values[fname] = 1.0 if face_choice == fname else 0.0

    if rig:
        for obj in bpy.data.objects:
            if obj.parent == rig and obj.type == "MESH" and obj.data.shape_keys:
                for kb in obj.data.shape_keys.key_blocks:
                    if kb.name in sk_values:
                        kb.value = sk_values[kb.name]

    # 3. Hair visibility
    hair_choice = props.female_hair if is_fem else props.male_hair
    scalp = bpy.data.objects.get(f"{prefix}_Scalp_BuzzCut")
    _set_obj_vis(scalp, hair_choice == "BUZZ")
    hair_col = bpy.data.collections.get(f"Hair_{prefix}")
    if hair_col:
        for obj in hair_col.objects:
            _set_obj_vis(obj, obj.name == hair_choice)

    # 4. Outfit visibility & auto-undergarment handling
    outfit_choice = props.female_outfit if is_fem else props.male_outfit
    under_names = [f"{prefix}_SportsBra", f"{prefix}_Shorts"] if is_fem else [f"{prefix}_BoxerBriefs"]
    for un in under_names:
        _set_obj_vis(bpy.data.objects.get(un), outfit_choice == "BASE")
    w_col = bpy.data.collections.get(f"Wardrobe_{prefix}")
    if w_col:
        for subc in w_col.children:
            active = (subc.name == outfit_choice)
            for obj in subc.objects:
                _set_obj_vis(obj, active)

    # 5. Footwear visibility
    shoe_choice = props.female_shoes if is_fem else props.male_shoes
    s_col = bpy.data.collections.get(f"Shoes_{prefix}")
    if s_col:
        for obj in s_col.objects:
            _set_obj_vis(obj, obj.name == shoe_choice)

    # 6. Accessories visibility
    acc_list = FEMALE_ACCS if is_fem else MALE_ACCS
    for prop_name, obj_name, _lbl in acc_list:
        _set_obj_vis(bpy.data.objects.get(obj_name), getattr(props, prop_name))


def _on_update(self, context):
    if context and context.scene:
        _sync_character(context.scene)


class IvoryWardrobeProps(bpy.types.PropertyGroup):
    skin_tone: bpy.props.EnumProperty(
        name="Skin Tone",
        items=[(str(i), f"{i+1:02d} {n}", "") for i, (n, _) in enumerate(SKIN_PALETTE_SRGB)],
        default="3", update=_on_update,
    )
    male_face: bpy.props.EnumProperty(name="Male Face", items=MALE_FACES, default="Face_Preset_01", update=_on_update)
    female_face: bpy.props.EnumProperty(name="Female Face", items=FEMALE_FACES, default="Face_Preset_01", update=_on_update)
    male_hair: bpy.props.EnumProperty(name="Male Hair", items=MALE_HAIRS, default="Male_Hair_03_ShortAfroTaper", update=_on_update)
    female_hair: bpy.props.EnumProperty(name="Female Hair", items=FEMALE_HAIRS, default="Female_Hair_05_LongBoxBraids", update=_on_update)
    male_outfit: bpy.props.EnumProperty(name="Male Outfit", items=MALE_OUTFITS, default="Male_Outfit_01_Abidjan_Streetwear", update=_on_update)
    female_outfit: bpy.props.EnumProperty(name="Female Outfit", items=FEMALE_OUTFITS, default="Female_Outfit_01_Abidjan_Wax_Dress", update=_on_update)
    male_shoes: bpy.props.EnumProperty(name="Male Shoes", items=MALE_SHOES, default="Male_Shoe_01_Urban_Sneakers", update=_on_update)
    female_shoes: bpy.props.EnumProperty(name="Female Shoes", items=FEMALE_SHOES, default="Female_Shoe_01_White_Sneakers", update=_on_update)

    morph_slim: bpy.props.FloatProperty(name="Skinny / Slender", min=0.0, max=1.0, default=0.0, update=_on_update)
    morph_muscular: bpy.props.FloatProperty(name="Muscular", min=0.0, max=1.0, default=0.0, update=_on_update)
    morph_heavy: bpy.props.FloatProperty(name="Heavy / Curvy", min=0.0, max=1.0, default=0.0, update=_on_update)
    morph_bust: bpy.props.FloatProperty(name="Bust Adjust", min=0.0, max=1.0, default=0.0, update=_on_update)
    morph_hip: bpy.props.FloatProperty(name="Hip Adjust", min=0.0, max=1.0, default=0.0, update=_on_update)

    acc_1: bpy.props.BoolProperty(name="Acc 1", default=False, update=_on_update)
    acc_2: bpy.props.BoolProperty(name="Acc 2", default=False, update=_on_update)
    acc_3: bpy.props.BoolProperty(name="Acc 3", default=True, update=_on_update)
    acc_4: bpy.props.BoolProperty(name="Acc 4", default=True, update=_on_update)
    acc_5: bpy.props.BoolProperty(name="Acc 5", default=False, update=_on_update)
    acc_6: bpy.props.BoolProperty(name="Acc 6", default=False, update=_on_update)
    acc_7: bpy.props.BoolProperty(name="Acc 7", default=False, update=_on_update)
    acc_8: bpy.props.BoolProperty(name="Acc 8", default=False, update=_on_update)


class IVORY_OT_switch_scene(bpy.types.Operator):
    bl_idname = "ivory.switch_scene"
    bl_label = "Switch Male / Female"
    target: bpy.props.StringProperty()

    def execute(self, context):
        sc = bpy.data.scenes.get(self.target)
        if sc:
            context.window.scene = sc
            _sync_character(sc)
        return {"FINISHED"}


class IVORY_OT_randomize(bpy.types.Operator):
    bl_idname = "ivory.randomize_look"
    bl_label = "Randomize Look"

    def execute(self, context):
        sc = context.scene
        p = sc.ivory_wardrobe
        is_fem = _is_female_scene(sc)
        p.skin_tone = str(random.randint(0, len(SKIN_PALETTE_SRGB) - 1))
        if is_fem:
            p.female_face = random.choice(FEMALE_FACES[1:])[0]
            p.female_hair = random.choice(FEMALE_HAIRS[1:])[0]
            p.female_outfit = random.choice(FEMALE_OUTFITS[1:])[0]
            p.female_shoes = random.choice(FEMALE_SHOES[1:])[0]
        else:
            p.male_face = random.choice(MALE_FACES[1:])[0]
            p.male_hair = random.choice(MALE_HAIRS[1:])[0]
            p.male_outfit = random.choice(MALE_OUTFITS[1:])[0]
            p.male_shoes = random.choice(MALE_SHOES[1:])[0]
        for k in range(1, 9):
            setattr(p, f"acc_{k}", random.random() < 0.30)
        _sync_character(sc)
        return {"FINISHED"}


class IVORY_OT_reset_base(bpy.types.Operator):
    bl_idname = "ivory.reset_basemesh"
    bl_label = "Bare Basemesh"

    def execute(self, context):
        sc = context.scene
        p = sc.ivory_wardrobe
        is_fem = _is_female_scene(sc)
        p.morph_slim = p.morph_muscular = p.morph_heavy = p.morph_bust = p.morph_hip = 0.0
        if is_fem:
            p.female_hair = "BUZZ"; p.female_outfit = "BASE"; p.female_shoes = "NONE"
        else:
            p.male_hair = "BUZZ"; p.male_outfit = "BASE"; p.male_shoes = "NONE"
        for k in range(1, 9):
            setattr(p, f"acc_{k}", False)
        _sync_character(sc)
        return {"FINISHED"}


class VIEW3D_PT_ivory_wardrobe(bpy.types.Panel):
    bl_label = "Ivory State Customizer"
    bl_idname = "VIEW3D_PT_ivory_wardrobe"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Ivory State"

    def draw(self, context):
        layout = self.layout
        sc = context.scene
        p = sc.ivory_wardrobe
        is_fem = _is_female_scene(sc)

        row = layout.row(align=True)
        op_m = row.operator("ivory.switch_scene", text="Male", depress=(not is_fem))
        op_m.target = "Male_Wardrobe_Live"
        op_f = row.operator("ivory.switch_scene", text="Female", depress=is_fem)
        op_f.target = "Female_Wardrobe_Live"

        row2 = layout.row(align=True)
        row2.operator("ivory.randomize_look", icon="SHADERFX")
        row2.operator("ivory.reset_basemesh", icon="LOOP_BACK")

        box = layout.box()
        box.label(text="Identity & Skin", icon="USER")
        box.prop(p, "female_face" if is_fem else "male_face", text="Face")
        box.prop(p, "skin_tone", text="Skin")

        box = layout.box()
        box.label(text="Wardrobe & Hair", icon="MOD_CLOTH")
        box.prop(p, "female_hair" if is_fem else "male_hair", text="Hair")
        box.prop(p, "female_outfit" if is_fem else "male_outfit", text="Outfit")
        box.prop(p, "female_shoes" if is_fem else "male_shoes", text="Shoes")

        box = layout.box()
        box.label(text="Accessories", icon="MONKEY")
        acc_list = FEMALE_ACCS if is_fem else MALE_ACCS
        for prop_name, _oname, lbl in acc_list:
            box.prop(p, prop_name, text=lbl)

        box = layout.box()
        box.label(text="Body Morphs", icon="ARMATURE_DATA")
        box.prop(p, "morph_slim", text="Slender" if is_fem else "Skinny", slider=True)
        box.prop(p, "morph_muscular", text="Muscular", slider=True)
        box.prop(p, "morph_heavy", text="Curvy / Full" if is_fem else "Heavy", slider=True)
        if is_fem:
            box.prop(p, "morph_bust", text="Bust Adjust", slider=True)
            box.prop(p, "morph_hip", text="Hip Adjust", slider=True)


_CLASSES = (IvoryWardrobeProps, IVORY_OT_switch_scene, IVORY_OT_randomize, IVORY_OT_reset_base, VIEW3D_PT_ivory_wardrobe)


def register():
    for cls in _CLASSES:
        try:
            bpy.utils.register_class(cls)
        except Exception:
            pass
    bpy.types.Scene.ivory_wardrobe = bpy.props.PointerProperty(type=IvoryWardrobeProps)


if __name__ == "__main__":
    register()
'''


# ---------------------------------------------------------------------------
# Studio & Gallery Rendering Utilities
# ---------------------------------------------------------------------------
def _setup_world(scene, bg_srgb=(0.22, 0.23, 0.25), strength=0.40):
    world = bpy.data.worlds.new(f"World_{scene.name}")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = srgb_rgba(*bg_srgb, 1.0)
        bg.inputs["Strength"].default_value = strength


def _add_area_light(col, name, loc, target, energy, size, color_srgb=(1.0, 1.0, 1.0)):
    ld = bpy.data.lights.new(name, "AREA")
    ld.energy = energy
    ld.size = size
    ld.color = srgb_rgba(*color_srgb, 1.0)[:3]
    lo = bpy.data.objects.new(name, ld)
    lo.location = loc
    d = (Vector(target) - Vector(loc)).normalized()
    lo.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    col.objects.link(lo)
    return lo


def _add_camera(col, name, loc, target, lens=85.0, ortho=False, ortho_scale=2.0):
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
    col.objects.link(co)
    return co


def _ui_quad(col, name, center_xyz, w, h, mat):
    bm = bmesh.new()
    hw, hh = w * 0.5, h * 0.5
    cx, cy, cz = center_xyz
    bm.faces.new((
        bm.verts.new((cx - hw, cy, cz - hh)),
        bm.verts.new((cx + hw, cy, cz - hh)),
        bm.verts.new((cx + hw, cy, cz + hh)),
        bm.verts.new((cx - hw, cy, cz + hh)),
    ))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def _ui_text(col, name, text, loc, size, mat, align_x="CENTER", align_y="CENTER"):
    curve = bpy.data.curves.new(name, type="FONT")
    curve.body = text
    curve.size = size
    curve.align_x = align_x
    curve.align_y = align_y
    obj = bpy.data.objects.new(name, curve)
    obj.location = loc
    obj.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def _clone_static_mesh(src_obj, col, parent_emp, tag, face_sk=None, skin_mat_override=None, crop_z=None, crop_x=None):
    """Create a lightweight static display copy of a mesh for 2x5 studio gallery rendering."""
    dup = src_obj.copy()
    dup.data = src_obj.data.copy()
    dup.name = f"{src_obj.name}_{tag}"
    dup.hide_viewport = False
    dup.hide_render = False

    for m in list(dup.modifiers):
        if m.type == "ARMATURE":
            dup.modifiers.remove(m)
        elif m.type == "SUBSURF":
            m.levels = 1
            m.render_levels = 1

    if dup.data.shape_keys:
        if face_sk and face_sk in dup.data.shape_keys.key_blocks:
            kb_target = dup.data.shape_keys.key_blocks[face_sk]
            coords = [v.co[:] for v in kb_target.data]
            for kb in reversed(list(dup.data.shape_keys.key_blocks)):
                dup.shape_key_remove(kb)
            for vi, co in enumerate(coords):
                dup.data.vertices[vi].co = co
        else:
            for kb in dup.data.shape_keys.key_blocks:
                if kb.name != "Basis":
                    kb.value = 0.0

    if crop_z is not None:
        bm = bmesh.new()
        bm.from_mesh(dup.data)
        del_v = [v for v in bm.verts if v.co.z < crop_z or (crop_x is not None and abs(v.co.x) > crop_x)]
        bmesh.ops.delete(bm, geom=del_v, context="VERTS")
        bm.to_mesh(dup.data)
        bm.free()

    if skin_mat_override is not None:
        dup.data.materials.clear()
        dup.data.materials.append(skin_mat_override)

    dup.parent = parent_emp
    dup.matrix_parent_inverse = Matrix.Identity(4)
    col.objects.link(dup)
    return dup


def _cleanup_temp_scene(scene):
    """Remove temporary gallery scene and its exclusive objects so the saved .blend stays clean and light."""
    objs = list(scene.objects)
    bpy.data.scenes.remove(scene)
    for o in objs:
        if o.users == 0:
            data = o.data
            bpy.data.objects.remove(o)
            if data and data.users == 0:
                if isinstance(data, bpy.types.Mesh):
                    bpy.data.meshes.remove(data)
                elif isinstance(data, bpy.types.Curve):
                    bpy.data.curves.remove(data)
                elif isinstance(data, bpy.types.Light):
                    bpy.data.lights.remove(data)
                elif isinstance(data, bpy.types.Camera):
                    bpy.data.cameras.remove(data)


def _configure_fast_eevee(scene, res_x, res_y, samples=16):
    scene.render.engine = "BLENDER_EEVEE"
    if hasattr(scene.eevee, "taa_render_samples"):
        scene.eevee.taa_render_samples = samples
    if hasattr(scene.eevee, "use_raytracing"):
        scene.eevee.use_raytracing = False
    if hasattr(scene.eevee, "use_shadows"):
        scene.eevee.use_shadows = True
    scene.render.resolution_x = res_x
    scene.render.resolution_y = res_y


# ---------------------------------------------------------------------------
# 8 High-Resolution Analysis Showcase Renders
# ---------------------------------------------------------------------------
def _render_01_body_anatomy(male_info, female_info):
    sc = bpy.data.scenes.new("__Gal_01_Body")
    _setup_world(sc, bg_srgb=(0.24, 0.25, 0.27), strength=0.40)
    col = sc.collection

    bm_f = bmesh.new()
    bmesh.ops.create_grid(bm_f, x_segments=1, y_segments=1, size=8.5, matrix=Matrix.Translation((0, 0.3, 0)))
    mf = bpy.data.meshes.new("__AnatFloor"); bm_f.to_mesh(mf); bm_f.free()
    fo = bpy.data.objects.new("__AnatFloor", mf); col.objects.link(fo)
    fo.data.materials.append(create_studio_grid_floor_material())

    bm_w = bmesh.new()
    bmesh.ops.create_grid(bm_w, x_segments=1, y_segments=1, size=8.5, matrix=Matrix.Translation((0, 1.4, 1.8)) @ Matrix.Rotation(math.radians(90), 4, "X"))
    mw = bpy.data.meshes.new("__AnatWall"); bm_w.to_mesh(mw); bm_w.free()
    wo = bpy.data.objects.new("__AnatWall", mw); col.objects.link(wo)
    wo.data.materials.append(create_studio_wall_material())

    _add_area_light(col, "KeyL", (3.2, -3.5, 3.0), (0.0, 0.0, 1.0), 480.0, 1.8, (1.0, 0.96, 0.92))
    _add_area_light(col, "FillL", (-3.5, -2.8, 1.5), (0.0, 0.0, 0.95), 110.0, 3.4, (0.84, 0.90, 1.0))
    _add_area_light(col, "RimL", (0.0, 1.25, 2.5), (0.0, 0.0, 1.0), 260.0, 4.0, (0.98, 0.98, 1.0))

    mat_banner = create_ui_flat_material("CatBanner", (0.12, 0.13, 0.15), 1.0, 0.95)
    mat_white = create_ui_flat_material("CatWhite", (0.95, 0.96, 0.97), 1.0, 1.0)
    mat_gold = create_ui_flat_material("CatGold", (0.92, 0.72, 0.25), 1.0, 1.0)

    placements = [
        (male_info,   "MALE FRONT",   -2.42, 0.0),
        (male_info,   "MALE SIDE",    -1.45, 90.0),
        (male_info,   "MALE BACK",    -0.62, 180.0),
        (female_info, "FEMALE FRONT",  0.66, 0.0),
        (female_info, "FEMALE SIDE",   1.55, 90.0),
        (female_info, "FEMALE BACK",   2.34, 180.0),
    ]
    for idx, (info, lbl, tx, rz) in enumerate(placements):
        emp = bpy.data.objects.new(f"Anat_{idx}", None)
        emp.location = (tx, 0.0, 0.0)
        emp.rotation_euler = (0.0, 0.0, math.radians(rz))
        col.objects.link(emp)
        parts = [info["body"], info["eyes"], info["lashes"], info["brows"], info["scalp"]] + info["clothes"]
        for src in parts:
            _clone_static_mesh(src, col, emp, f"a{idx}")
        _ui_text(col, f"Lbl_{idx}", lbl, (tx, -0.25, 1.98), 0.056, mat_white)

    _ui_quad(col, "TopBar", (0.0, -0.22, 2.34), 7.6, 0.30, mat_banner)
    _ui_text(col, "Header", "IVORY STATE - 01 UPGRADED ANATOMY BASEMESHES (MALE & FEMALE)", (-3.05, -0.24, 2.32), 0.074, mat_gold, align_x="LEFT")

    # Gentle elevated perspective telephoto shows crisp studio grid floor cleanly!
    cam = _add_camera(col, "CamAnat", (0.0, -11.4, 1.24), (0.0, 0.0, 0.96), lens=64.0)
    sc.camera = cam
    _configure_fast_eevee(sc, 2000, 1080, samples=16)
    sc.render.filepath = os.path.join(CATALOG_DIR, "01_body_anatomy_upgraded.png")
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    print(f"[CATALOG] Rendered {sc.render.filepath}", flush=True)
    _cleanup_temp_scene(sc)


def _render_faces_sheet(info, filename, title_text):
    prefix = info["prefix"]
    sc = bpy.data.scenes.new(f"__Gal_Faces_{prefix}")
    _setup_world(sc, bg_srgb=(0.18, 0.19, 0.21), strength=0.38)
    col = sc.collection

    mat_bg = create_ui_flat_material("CatBG", (0.14, 0.15, 0.17), 1.0, 0.95)
    mat_card = create_ui_flat_material("CatCard", (0.22, 0.23, 0.26), 1.0, 0.92)
    mat_gold = create_ui_flat_material("CatGold", (0.92, 0.72, 0.25), 1.0, 1.0)
    mat_white = create_ui_flat_material("CatWhite", (0.95, 0.96, 0.97), 1.0, 1.0)

    cell_w, cell_h = 0.44, 0.48
    head_z = info["height"] - 0.115
    crop_z = info["height"] - 0.305

    _ui_quad(col, "SheetBG", (0.0, 0.30, 0.04), 3.4, 1.8, mat_bg)
    _ui_text(col, "Title", title_text, (-1.08, 0.12, 0.56), 0.044, mat_gold, align_x="LEFT")

    for idx, (sk_name, label, tone_idx, _t) in enumerate(info["face_presets"]):
        row, ci = idx // 5, idx % 5
        cx = (ci - 2.0) * cell_w
        cz = 0.24 if row == 0 else -0.25
        _ui_quad(col, f"Card_{idx}", (cx, 0.22, cz - 0.01), cell_w - 0.035, cell_h - 0.045, mat_card)

        emp = bpy.data.objects.new(f"FaceBust_{idx}", None)
        emp.location = (cx, 0.0, cz + 0.015 - head_z)
        emp.rotation_euler = (0.0, 0.0, math.radians(6.0))
        col.objects.link(emp)

        skin_c = clone_skin_material_with_tone(info["skin_mat"], f"GalSkin_{prefix}_{idx}", tone_idx)
        _clone_static_mesh(info["body"], col, emp, f"fb{idx}", face_sk=sk_name, skin_mat_override=skin_c, crop_z=crop_z, crop_x=0.175)
        for src in (info["eyes"], info["lashes"], info["brows"], info["scalp"]):
            _clone_static_mesh(src, col, emp, f"fb{idx}", face_sk=sk_name)

        _ui_text(col, f"Lbl_{idx}", label, (cx, -0.14, cz - 0.195), 0.028, mat_white)

    _add_area_light(col, "Key", (1.8, -2.0, 1.2), (0.0, 0.0, 0.0), 270.0, 2.2, (1.0, 0.96, 0.92))
    _add_area_light(col, "Fill", (-2.0, -1.8, 0.2), (0.0, 0.0, 0.0), 75.0, 2.6, (0.84, 0.90, 1.0))
    _add_area_light(col, "Rim", (0.0, 0.55, 0.9), (0.0, 0.0, 0.0), 145.0, 2.8, (0.98, 0.98, 1.0))

    cam = _add_camera(col, "CamFaces", (0.0, -3.5, 0.03), (0.0, 0.0, 0.03), ortho=True, ortho_scale=2.36)
    sc.camera = cam
    _configure_fast_eevee(sc, 2000, 1000, samples=16)
    sc.render.filepath = os.path.join(CATALOG_DIR, filename)
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    print(f"[CATALOG] Rendered {sc.render.filepath}", flush=True)
    _cleanup_temp_scene(sc)


def _render_hair_sheet(info, hair_dict, hair_specs, filename, title_text):
    prefix = info["prefix"]
    is_fem = (info["gender"] == "female")
    sc = bpy.data.scenes.new(f"__Gal_Hair_{prefix}")
    _setup_world(sc, bg_srgb=(0.20, 0.21, 0.23), strength=0.42)
    col = sc.collection

    mat_bg = create_ui_flat_material("CatBG", (0.14, 0.15, 0.17), 1.0, 0.95)
    mat_card = create_ui_flat_material("CatCard", (0.24, 0.25, 0.28), 1.0, 0.92)
    mat_capbar = create_ui_flat_material("CatCapBar", (0.12, 0.13, 0.15), 1.0, 0.95)
    mat_gold = create_ui_flat_material("CatGold", (0.92, 0.72, 0.25), 1.0, 1.0)
    mat_white = create_ui_flat_material("CatWhite", (0.95, 0.96, 0.97), 1.0, 1.0)

    cell_w, cell_h = 0.56, 0.62
    head_z = info["height"] - 0.140
    # Crop cleanly at clavicle/upper-chest (and keep female sports bra so busts look studio-clean!)
    crop_z = info["height"] - 0.375

    _ui_quad(col, "SheetBG", (0.0, 0.45, 0.04), 4.2, 2.2, mat_bg)
    _ui_text(col, "Title", title_text, (-1.36, 0.20, 0.70), 0.052, mat_gold, align_x="LEFT")

    for idx, (hkey, label) in enumerate(hair_specs):
        row, ci = idx // 5, idx % 5
        cx = (ci - 2.0) * cell_w
        cz = 0.30 if row == 0 else -0.33
        _ui_quad(col, f"Card_{idx}", (cx, 0.32, cz - 0.01), cell_w - 0.04, cell_h - 0.05, mat_card)

        emp = bpy.data.objects.new(f"HairBust_{idx}", None)
        emp.location = (cx, 0.0, cz + 0.025 - head_z)
        emp.rotation_euler = (0.0, 0.0, math.radians(20.0))
        col.objects.link(emp)

        sk_name, _flbl, tone_idx, _t = info["face_presets"][idx]
        skin_c = clone_skin_material_with_tone(info["skin_mat"], f"GalSkinH_{prefix}_{idx}", tone_idx)
        _clone_static_mesh(info["body"], col, emp, f"hb{idx}", face_sk=sk_name, skin_mat_override=skin_c, crop_z=crop_z, crop_x=0.22)
        if is_fem and info["clothes"]:
            _clone_static_mesh(info["clothes"][0], col, emp, f"hb{idx}", crop_z=crop_z)
        for src in (info["eyes"], info["lashes"], info["brows"]):
            _clone_static_mesh(src, col, emp, f"hb{idx}", face_sk=sk_name)
        _clone_static_mesh(hair_dict[hkey], col, emp, f"hb{idx}", face_sk=sk_name)

        # Clean dark caption footer strip at bottom of card (never overlaps skin/hair!)
        _ui_quad(col, f"CapBar_{idx}", (cx, -0.22, cz - 0.262), cell_w - 0.04, 0.068, mat_capbar)
        _ui_text(col, f"Lbl_{idx}", label, (cx, -0.24, cz - 0.264), 0.032, mat_white)

    _add_area_light(col, "Key", (2.2, -2.4, 1.6), (0.0, 0.0, 0.0), 340.0, 2.6, (1.0, 0.96, 0.92))
    _add_area_light(col, "Fill", (-2.4, -2.0, 0.4), (0.0, 0.0, 0.0), 95.0, 2.8, (0.86, 0.92, 1.0))
    _add_area_light(col, "Rim", (0.0, 0.85, 1.4), (0.0, 0.0, 0.0), 220.0, 3.2, (0.98, 0.98, 1.0))

    cam = _add_camera(col, "CamHair", (0.0, -4.2, 0.03), (0.0, 0.0, 0.03), ortho=True, ortho_scale=2.96)
    sc.camera = cam
    _configure_fast_eevee(sc, 2000, 1000, samples=16)
    sc.render.filepath = os.path.join(CATALOG_DIR, filename)
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    print(f"[CATALOG] Rendered {sc.render.filepath}", flush=True)
    _cleanup_temp_scene(sc)


def _render_outfits_sheet(info, outfits_dict, outfit_specs, hair_dict, hair_specs, shoes_dict, shoe_specs, filename, title_text):
    prefix = info["prefix"]
    sc = bpy.data.scenes.new(f"__Gal_Outfits_{prefix}")
    _setup_world(sc, bg_srgb=(0.24, 0.25, 0.27), strength=0.42)
    col = sc.collection

    mat_bg = create_ui_flat_material("CatBG", (0.16, 0.17, 0.19), 1.0, 0.95)
    mat_card = create_ui_flat_material("CatCardO", (0.28, 0.29, 0.32), 1.0, 0.90)
    mat_gold = create_ui_flat_material("CatGold", (0.92, 0.72, 0.25), 1.0, 1.0)
    mat_white = create_ui_flat_material("CatWhite", (0.95, 0.96, 0.97), 1.0, 1.0)

    col_dx = 1.50

    _ui_quad(col, "SheetBG", (0.0, 0.70, 0.15), 9.6, 6.2, mat_bg)
    _ui_text(col, "Title", title_text, (-3.65, 0.35, 2.38), 0.145, mat_gold, align_x="LEFT")

    for idx, (okey, label) in enumerate(outfit_specs):
        row, ci = idx // 5, idx % 5
        cx = (ci - 2.0) * col_dx
        z_floor = 0.22 if row == 0 else -2.02
        _ui_quad(col, f"Card_{idx}", (cx, 0.45, z_floor + 0.92), col_dx - 0.10, 2.06, mat_card)

        emp = bpy.data.objects.new(f"OutfitFig_{idx}", None)
        emp.location = (cx, 0.0, z_floor + 0.06)
        emp.rotation_euler = (0.0, 0.0, math.radians(12.0))
        col.objects.link(emp)

        sk_name, _flbl, tone_idx, _t = info["face_presets"][idx]
        skin_c = clone_skin_material_with_tone(info["skin_mat"], f"GalSkinO_{prefix}_{idx}", tone_idx)
        _clone_static_mesh(info["body"], col, emp, f"ob{idx}", face_sk=sk_name, skin_mat_override=skin_c)
        for src in (info["eyes"], info["lashes"], info["brows"]):
            _clone_static_mesh(src, col, emp, f"ob{idx}", face_sk=sk_name)

        hkey = hair_specs[idx][0]
        _clone_static_mesh(hair_dict[hkey], col, emp, f"ob{idx}", face_sk=sk_name)

        skey = shoe_specs[idx % len(shoe_specs)][0]
        _clone_static_mesh(shoes_dict[skey], col, emp, f"ob{idx}")

        _ocol, o_meshes = outfits_dict[okey]
        for om in o_meshes:
            _clone_static_mesh(om, col, emp, f"ob{idx}")

        _ui_text(col, f"Lbl_{idx}", label, (cx, -0.30, z_floor - 0.05), 0.095, mat_white)

    _add_area_light(col, "Key", (4.5, -5.5, 4.2), (0.0, 0.0, 0.1), 1100.0, 5.5, (1.0, 0.97, 0.93))
    _add_area_light(col, "Fill", (-4.8, -4.5, 0.8), (0.0, 0.0, 0.1), 320.0, 6.0, (0.86, 0.92, 1.0))
    _add_area_light(col, "Rim", (0.0, 2.2, 3.8), (0.0, 0.0, 0.1), 520.0, 7.0, (0.98, 0.98, 1.0))

    cam = _add_camera(col, "CamOutfits", (0.0, -9.5, 0.14), (0.0, 0.0, 0.14), ortho=True, ortho_scale=7.95)
    sc.camera = cam
    _configure_fast_eevee(sc, 2100, 1300, samples=16)
    sc.render.filepath = os.path.join(CATALOG_DIR, filename)
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    print(f"[CATALOG] Rendered {sc.render.filepath}", flush=True)
    _cleanup_temp_scene(sc)


def _render_08_shoes_and_accessories(male_info, female_info, m_shoes, f_shoes, m_accs, f_accs, m_hairs, f_hairs, m_outfits, f_outfits):
    """High-resolution close-up catalog of ALL 10 Shoe Pairs (top rows) & ALL Accessories worn on characters (bottom row)."""
    sc = bpy.data.scenes.new("__Gal_08_Shoes_Accs")
    _setup_world(sc, bg_srgb=(0.20, 0.21, 0.23), strength=0.45)
    col = sc.collection

    mat_bg = create_ui_flat_material("CatBG", (0.14, 0.15, 0.17), 1.0, 0.95)
    mat_card = create_ui_flat_material("CatCardS", (0.25, 0.26, 0.29), 1.0, 0.92)
    mat_capbar = create_ui_flat_material("CatCapBarS", (0.12, 0.13, 0.15), 1.0, 0.95)
    mat_gold = create_ui_flat_material("CatGold", (0.92, 0.72, 0.25), 1.0, 1.0)
    mat_white = create_ui_flat_material("CatWhite", (0.95, 0.96, 0.97), 1.0, 1.0)

    _ui_quad(col, "SheetBG", (0.0, 0.80, 0.0), 6.2, 4.0, mat_bg)
    _ui_text(col, "Header", "IVORY STATE - 08 FOOTWEAR (5 MALE + 5 FEMALE) & MODULAR ACCESSORIES", (-2.25, 0.25, 1.34), 0.088, mat_gold, align_x="LEFT")

    # ROW 1: 5 Male Shoes cleanly centered inside cards with dedicated caption bar below
    _ui_text(col, "SubMShoe", "MALE FOOTWEAR", (-2.25, 0.22, 1.16), 0.056, mat_white, align_x="LEFT")
    for i, (skey, lbl) in enumerate(MALE_SHOE_SPECS):
        cx = (i - 2.0) * 0.92
        cz = 0.84
        _ui_quad(col, f"MSCard_{i}", (cx, 0.45, cz + 0.06), 0.84, 0.50, mat_card)
        emp = bpy.data.objects.new(f"MShoeDisp_{i}", None)
        emp.location = (cx, 0.0, cz - 0.03)
        emp.rotation_euler = (math.radians(14.0), 0.0, math.radians(48.0))
        emp.scale = (1.16, 1.16, 1.16)
        col.objects.link(emp)
        _clone_static_mesh(m_shoes[skey], col, emp, f"ms{i}")
        _ui_quad(col, f"MSBar_{i}", (cx, -0.35, cz - 0.145), 0.84, 0.09, mat_capbar)
        _ui_text(col, f"MSLbl_{i}", lbl, (cx, -0.38, cz - 0.148), 0.046, mat_white)

    # ROW 2: 5 Female Shoes cleanly centered inside cards with dedicated caption bar below
    _ui_text(col, "SubFShoe", "FEMALE FOOTWEAR", (-2.25, 0.22, 0.54), 0.056, mat_white, align_x="LEFT")
    for i, (skey, lbl) in enumerate(FEMALE_SHOE_SPECS):
        cx = (i - 2.0) * 0.92
        cz = 0.22
        _ui_quad(col, f"FSCard_{i}", (cx, 0.45, cz + 0.06), 0.84, 0.50, mat_card)
        emp = bpy.data.objects.new(f"FShoeDisp_{i}", None)
        emp.location = (cx, 0.0, cz - 0.03)
        emp.rotation_euler = (math.radians(14.0), 0.0, math.radians(48.0))
        emp.scale = (1.16, 1.16, 1.16)
        col.objects.link(emp)
        _clone_static_mesh(f_shoes[skey], col, emp, f"fs{i}")
        _ui_quad(col, f"FSBar_{i}", (cx, -0.35, cz - 0.145), 0.84, 0.09, mat_capbar)
        _ui_text(col, f"FSLbl_{i}", lbl, (cx, -0.38, cz - 0.148), 0.046, mat_white)

    # ROW 3: 4 Zoomed-in Upper-Body Accessorized Showcase Vignettes (2 Male + 2 Female)
    _ui_text(col, "SubAcc", "WEARABLE ACCESSORIES (GLASSES, CAPS, HEADWRAP, WATCHES, JEWELRY, BAGS)", (-2.25, 0.22, -0.09), 0.054, mat_white, align_x="LEFT")
    vignettes = [
        (male_info,   "MALE: Cap + Aviators + Chain + Watch + Ring",  -1.72, 16.0, None,
         m_outfits["01_Abidjan_Streetwear"][1], [m_accs["01_Aviator_Glasses"], m_accs["02_Snapback_Cap"], m_accs["04_Luxury_Watch"], m_accs["05_Gold_Chain"], m_accs["06_Signet_Ring"]]),
        (male_info,   "MALE: Wax Bucket Hat + Sling Bag + Watch",     -0.58, -16.0, None,
         m_outfits["07_GrandBassam_Resort"][1], [m_accs["03_Bucket_Hat"], m_accs["04_Luxury_Watch"], m_accs["07_Crossbody_Bag"]]),
        (female_info, "FEMALE: Royal Headwrap + Hoops + Necklace",     0.58, 16.0, None,
         f_outfits["08_Wax_Pagne_Ensemble"][1], [f_accs["02_Wax_Headwrap"], f_accs["03_Hoop_Earrings"], f_accs["04_Beaded_Necklace"], f_accs["06_Gold_Bangles"]]),
        (female_info, "FEMALE: Cat-Eye + Watch + Ring + Leather Bag",  1.72, -16.0, f_hairs["07_HighBraidedBun"],
         f_outfits["03_Cocody_Chic_Crop"][1], [f_accs["01_CatEye_Glasses"], f_accs["03_Hoop_Earrings"], f_accs["05_Luxury_Watch"], f_accs["07_Gold_Ring"], f_accs["08_Crossbody_Bag"]]),
    ]
    for vi, (info, vlbl, vx, vyaw, hair_obj, outfit_meshes, acc_objs) in enumerate(vignettes):
        _ui_quad(col, f"VCard_{vi}", (vx, 0.45, -0.74), 1.06, 1.18, mat_card)
        emp = bpy.data.objects.new(f"AccVig_{vi}", None)
        scale_v = 0.96
        crop_z = info["height"] * 0.44
        emp.location = (vx, 0.0, -0.72 - (info["height"] * 0.72) * scale_v)
        emp.rotation_euler = (0.0, 0.0, math.radians(vyaw))
        emp.scale = (scale_v, scale_v, scale_v)
        col.objects.link(emp)

        _clone_static_mesh(info["body"], col, emp, f"av{vi}", crop_z=crop_z)
        for src in (info["eyes"], info["lashes"], info["brows"]):
            _clone_static_mesh(src, col, emp, f"av{vi}")
        if hair_obj is not None:
            _clone_static_mesh(hair_obj, col, emp, f"av{vi}")
        for om in outfit_meshes:
            _clone_static_mesh(om, col, emp, f"av{vi}", crop_z=crop_z)
        for ao in acc_objs:
            if ao is not None:
                _clone_static_mesh(ao, col, emp, f"av{vi}")
        _ui_quad(col, f"VBar_{vi}", (vx, -0.35, -1.27), 1.06, 0.09, mat_capbar)
        _ui_text(col, f"VLbl_{vi}", vlbl, (vx, -0.38, -1.273), 0.036, mat_white)

    _add_area_light(col, "Key", (3.2, -4.0, 3.0), (0.0, 0.0, 0.0), 650.0, 4.0, (1.0, 0.97, 0.93))
    _add_area_light(col, "Fill", (-3.5, -3.5, 0.5), (0.0, 0.0, 0.0), 210.0, 4.5, (0.86, 0.92, 1.0))
    _add_area_light(col, "Rim", (0.0, 1.8, 2.5), (0.0, 0.0, 0.0), 340.0, 5.0, (0.98, 0.98, 1.0))

    cam = _add_camera(col, "CamShoesAcc", (0.0, -7.2, 0.02), (0.0, 0.0, 0.02), ortho=True, ortho_scale=4.85)
    sc.camera = cam
    _configure_fast_eevee(sc, 2100, 1300, samples=16)
    sc.render.filepath = os.path.join(CATALOG_DIR, "08_shoes_and_accessories.png")
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    print(f"[CATALOG] Rendered {sc.render.filepath}", flush=True)
    _cleanup_temp_scene(sc)


# ---------------------------------------------------------------------------
# Live Interactive Dressing-Room Scene Setup
# ---------------------------------------------------------------------------
def _setup_live_dressing_scene(scene, char_col, prefix):
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    _setup_world(scene, bg_srgb=(0.24, 0.25, 0.27), strength=0.45)

    st_col = bpy.data.collections.new(f"Studio_{prefix}")
    scene.collection.children.link(st_col)
    if char_col.name not in scene.collection.children:
        scene.collection.children.link(char_col)

    bm_f = bmesh.new()
    bmesh.ops.create_grid(bm_f, x_segments=1, y_segments=1, size=4.5, matrix=Matrix.Translation((0, 0, 0)))
    mf = bpy.data.meshes.new(f"LiveFloor_{prefix}"); bm_f.to_mesh(mf); bm_f.free()
    fo = bpy.data.objects.new(f"LiveFloor_{prefix}", mf); st_col.objects.link(fo)
    fo.data.materials.append(create_studio_grid_floor_material())

    _add_area_light(st_col, f"LiveKey_{prefix}", (2.2, -2.8, 2.5), (0.0, 0.0, 0.95), 260.0, 1.8, (1.0, 0.97, 0.93))
    _add_area_light(st_col, f"LiveFill_{prefix}", (-2.4, -2.2, 1.4), (0.0, 0.0, 0.95), 75.0, 2.4, (0.85, 0.91, 1.0))
    _add_area_light(st_col, f"LiveRim_{prefix}", (0.0, 1.4, 2.2), (0.0, 0.0, 1.05), 180.0, 2.2, (0.98, 0.98, 1.0))
    cam = _add_camera(st_col, f"LiveCam_{prefix}", (0.0, -3.4, 0.96), (0.0, 0.0, 0.92), lens=65.0)
    scene.camera = cam


def _embed_and_sync_ui(male_scene, female_scene):
    txt = bpy.data.texts.get("ivory_wardrobe_ui.py") or bpy.data.texts.new("ivory_wardrobe_ui.py")
    txt.from_string(EMBEDDED_UI_SCRIPT)
    txt.use_module = True
    ns = {"__name__": "__main__"}
    exec(EMBEDDED_UI_SCRIPT, ns)
    if male_scene:
        ns["_sync_character"](male_scene)
    if female_scene:
        ns["_sync_character"](female_scene)
    return ns


def _reconstruct_single_info(gender):
    is_fem = (gender == "female")
    prefix = "Female" if is_fem else "Male"
    body = bpy.data.objects[f"{prefix}_Basemesh"]
    rig = bpy.data.objects[f"{prefix}_Rig"]
    max_z = max(v.co.z for v in body.data.vertices)
    eyes = bpy.data.objects[f"{prefix}_Eyes"]
    ec = [v.co for v in eyes.data.vertices]
    l_pts = [p for p in ec if p.x >= 0.0]
    r_pts = [p for p in ec if p.x < 0.0]
    l_eye = sum(l_pts, Vector((0, 0, 0))) / max(1, len(l_pts))
    r_eye = sum(r_pts, Vector((0, 0, 0))) / max(1, len(r_pts))
    hb = rig.data.bones.get("mixamorig:Head")
    head_c = (rig.matrix_world @ hb.head_local) if hb else Vector((0, 0, max_z - 0.14))
    clothes = [bpy.data.objects[f"{prefix}_SportsBra"], bpy.data.objects[f"{prefix}_Shorts"]] if is_fem else [bpy.data.objects[f"{prefix}_BoxerBriefs"]]
    return {
        "gender": gender,
        "prefix": prefix,
        "rig": rig,
        "body": body,
        "skin_mat": bpy.data.materials[f"Ivory_{prefix}_Skin"],
        "eyes": eyes,
        "lashes": bpy.data.objects[f"{prefix}_Eyelashes"],
        "brows": bpy.data.objects[f"{prefix}_Eyebrows"],
        "scalp": bpy.data.objects[f"{prefix}_Scalp_BuzzCut"],
        "clothes": clothes,
        "height": float(max_z),
        "head_center": head_c,
        "l_eye": l_eye,
        "r_eye": r_eye,
        "face_presets": FEMALE_FACE_PRESETS if is_fem else MALE_FACE_PRESETS,
    }


def stage_male_base_hair():
    """Sub-stage A1 (~45s): Build Male basemesh + 10 faces + 10 hairstyles & save."""
    os.makedirs(CATALOG_DIR, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    male_scene = bpy.context.scene
    male_scene.name = "Male_Wardrobe_Live"
    col_male = bpy.data.collections.new("Male_Character")
    male_scene.collection.children.link(col_male)
    male_info = build_character_asset("male", col_male)
    build_male_hairstyles(male_info, col_male)
    _setup_live_dressing_scene(male_scene, col_male, "Male")
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    return {"status": "male_base_hair_done", "height": male_info["height"]}


def stage_male_wardrobe_accs():
    """Sub-stage A2 (~45s): Add Male 10 outfits + 5 shoes + 7 accessories & save."""
    male_scene = bpy.data.scenes["Male_Wardrobe_Live"]
    bpy.context.window.scene = male_scene
    col_male = bpy.data.collections["Male_Character"]
    male_info = _reconstruct_single_info("male")
    build_male_wardrobe(male_info, col_male)
    build_footwear(male_info, col_male)
    build_accessories(male_info, col_male)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    return {"status": "male_wardrobe_accs_done"}


def stage_female_base_hair():
    """Sub-stage B1 (~45s): Add Female basemesh + 10 faces + 10 hairstyles & save."""
    female_scene = bpy.data.scenes.get("Female_Wardrobe_Live") or bpy.data.scenes.new("Female_Wardrobe_Live")
    col_female = bpy.data.collections.new("Female_Character")
    female_scene.collection.children.link(col_female)
    bpy.context.window.scene = female_scene
    female_info = build_character_asset("female", col_female)
    build_female_hairstyles(female_info, col_female)
    _setup_live_dressing_scene(female_scene, col_female, "Female")
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    return {"status": "female_base_hair_done", "height": female_info["height"]}


def stage_female_wardrobe_accs():
    """Sub-stage B2 (~45s): Add Female 10 outfits + 5 shoes + 8 accs + live UI & save."""
    male_scene = bpy.data.scenes["Male_Wardrobe_Live"]
    female_scene = bpy.data.scenes["Female_Wardrobe_Live"]
    bpy.context.window.scene = female_scene
    col_female = bpy.data.collections["Female_Character"]
    female_info = _reconstruct_single_info("female")
    build_female_wardrobe(female_info, col_female)
    build_footwear(female_info, col_female)
    build_accessories(female_info, col_female)
    _embed_and_sync_ui(male_scene, female_scene)
    bpy.context.window.scene = male_scene
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    return {"status": "female_wardrobe_accs_done", "blend": BLEND_OUT}


def build_stage_male():
    stage_male_base_hair()
    return stage_male_wardrobe_accs()


def build_stage_female():
    stage_female_base_hair()
    return stage_female_wardrobe_accs()


def _reconstruct_studio_refs():
    """Rebuild dict handles from already-loaded IvoryCharacterStudio.blend in milliseconds."""
    def load_gender(gender, face_presets, hair_specs, outfit_specs, shoe_specs, acc_specs):
        is_fem = (gender == "female")
        prefix = "Female" if is_fem else "Male"
        body = bpy.data.objects[f"{prefix}_Basemesh"]
        max_z = max(v.co.z for v in body.data.vertices)
        clothes = [bpy.data.objects[f"{prefix}_SportsBra"], bpy.data.objects[f"{prefix}_Shorts"]] if is_fem else [bpy.data.objects[f"{prefix}_BoxerBriefs"]]
        info = {
            "gender": gender,
            "prefix": prefix,
            "rig": bpy.data.objects[f"{prefix}_Rig"],
            "body": body,
            "skin_mat": bpy.data.materials[f"Ivory_{prefix}_Skin"],
            "eyes": bpy.data.objects[f"{prefix}_Eyes"],
            "lashes": bpy.data.objects[f"{prefix}_Eyelashes"],
            "brows": bpy.data.objects[f"{prefix}_Eyebrows"],
            "scalp": bpy.data.objects[f"{prefix}_Scalp_BuzzCut"],
            "clothes": clothes,
            "height": float(max_z),
            "face_presets": face_presets,
        }
        hairs = {k: bpy.data.objects[f"{prefix}_Hair_{k}"] for k, _ in hair_specs}
        outfits = {}
        for k, _ in outfit_specs:
            c = bpy.data.collections[f"{prefix}_Outfit_{k}"]
            outfits[k] = (c, list(c.objects))
        shoes = {k: bpy.data.objects[f"{prefix}_Shoe_{k}"] for k, _ in shoe_specs}
        accs = {k: bpy.data.objects.get(f"{prefix}_Acc_{k}") for k, _ in acc_specs}
        return info, hairs, outfits, shoes, accs

    m = load_gender("male", MALE_FACE_PRESETS, MALE_HAIR_SPECS, MALE_OUTFIT_SPECS, MALE_SHOE_SPECS, MALE_ACCESSORY_SPECS)
    f = load_gender("female", FEMALE_FACE_PRESETS, FEMALE_HAIR_SPECS, FEMALE_OUTFIT_SPECS, FEMALE_SHOE_SPECS, FEMALE_ACCESSORY_SPECS)
    return m, f


def render_catalog_sheets(sheet_ids):
    """Render any subset of sheets [1..8] directly from the already-loaded IvoryCharacterStudio.blend."""
    os.makedirs(CATALOG_DIR, exist_ok=True)
    (male_info, m_hairs, m_outfits, m_shoes, m_accs), (female_info, f_hairs, f_outfits, f_shoes, f_accs) = _reconstruct_studio_refs()
    rendered = []
    if 1 in sheet_ids:
        _render_01_body_anatomy(male_info, female_info)
        rendered.append("01_body_anatomy_upgraded.png")
    if 2 in sheet_ids:
        _render_faces_sheet(male_info, "02_male_faces_10.png", "IVORY STATE - 02 MALE FACES (10 WEST AFRICAN PRESETS)")
        rendered.append("02_male_faces_10.png")
    if 3 in sheet_ids:
        _render_faces_sheet(female_info, "03_female_faces_10.png", "IVORY STATE - 03 FEMALE FACES (10 WEST AFRICAN PRESETS)")
        rendered.append("03_female_faces_10.png")
    if 4 in sheet_ids:
        _render_hair_sheet(male_info, m_hairs, MALE_HAIR_SPECS, "04_male_hair_10.png", "IVORY STATE - 04 MALE HAIRSTYLES (10 MODULAR 3D STYLES)")
        rendered.append("04_male_hair_10.png")
    if 5 in sheet_ids:
        _render_hair_sheet(female_info, f_hairs, FEMALE_HAIR_SPECS, "05_female_hair_10.png", "IVORY STATE - 05 FEMALE HAIRSTYLES (10 MODULAR 3D STYLES)")
        rendered.append("05_female_hair_10.png")
    if 6 in sheet_ids:
        _render_outfits_sheet(male_info, m_outfits, MALE_OUTFIT_SPECS, m_hairs, MALE_HAIR_SPECS, m_shoes, MALE_SHOE_SPECS, "06_male_outfits_10.png", "IVORY STATE - 06 MALE WARDROBE (10 COMPLETE WEARABLE OUTFITS)")
        rendered.append("06_male_outfits_10.png")
    if 7 in sheet_ids:
        _render_outfits_sheet(female_info, f_outfits, FEMALE_OUTFIT_SPECS, f_hairs, FEMALE_HAIR_SPECS, f_shoes, FEMALE_SHOE_SPECS, "07_female_outfits_10.png", "IVORY STATE - 07 FEMALE WARDROBE (10 COMPLETE WEARABLE OUTFITS)")
        rendered.append("07_female_outfits_10.png")
    if 8 in sheet_ids:
        _render_08_shoes_and_accessories(male_info, female_info, m_shoes, f_shoes, m_accs, f_accs, m_hairs, f_hairs, m_outfits, f_outfits)
        rendered.append("08_shoes_and_accessories.png")
    return {"rendered": rendered, "catalog_dir": CATALOG_DIR}


def main():
    import traceback
    log_path = os.path.join(SCRIPT_DIR, "catalog_build.log")
    with open(log_path, "w", encoding="utf-8") as lf:
        def log(msg):
            print(msg, flush=True)
            lf.write(msg + "\n")
            lf.flush()
        try:
            os.makedirs(CATALOG_DIR, exist_ok=True)
            bpy.ops.wm.read_factory_settings(use_empty=True)

            male_scene = bpy.context.scene
            male_scene.name = "Male_Wardrobe_Live"
            female_scene = bpy.data.scenes.new("Female_Wardrobe_Live")

            col_male = bpy.data.collections.new("Male_Character")
            col_female = bpy.data.collections.new("Female_Character")
            male_scene.collection.children.link(col_male)
            female_scene.collection.children.link(col_female)

            log("[STUDIO] 1/5 Building upgraded Male & Female basemeshes + 10+10 Face Presets...")
            bpy.context.window.scene = male_scene
            male_info = build_character_asset("male", col_male)
            bpy.context.window.scene = female_scene
            female_info = build_character_asset("female", col_female)

            log("[STUDIO] 2/5 Generating 10 Male + 10 Female 3D Hairstyles...")
            bpy.context.window.scene = male_scene
            m_hairs, _ = build_male_hairstyles(male_info, col_male)
            bpy.context.window.scene = female_scene
            f_hairs, _ = build_female_hairstyles(female_info, col_female)

            log("[STUDIO] 3/5 Tailoring 10 Male + 10 Female Wearable 3D Outfits...")
            bpy.context.window.scene = male_scene
            m_outfits, _ = build_male_wardrobe(male_info, col_male)
            bpy.context.window.scene = female_scene
            f_outfits, _ = build_female_wardrobe(female_info, col_female)

            log("[STUDIO] 4/5 Crafting Footwear (5+5) & Modular Accessories...")
            bpy.context.window.scene = male_scene
            m_shoes, _ = build_footwear(male_info, col_male)
            m_accs, _ = build_accessories(male_info, col_male)
            bpy.context.window.scene = female_scene
            f_shoes, _ = build_footwear(female_info, col_female)
            f_accs, _ = build_accessories(female_info, col_female)

            _setup_live_dressing_scene(male_scene, col_male, "Male")
            _setup_live_dressing_scene(female_scene, col_female, "Female")
            _embed_and_sync_ui(male_scene, female_scene)

            bpy.context.window.scene = male_scene
            bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
            log(f"[STUDIO] Saved interactive studio checkpoint to {BLEND_OUT}")

            log("[STUDIO] 5/5 Rendering all 8 polished analysis catalog images...")
            _render_01_body_anatomy(male_info, female_info)
            log("  -> 01_body_anatomy_upgraded.png OK")
            _render_faces_sheet(male_info, "02_male_faces_10.png", "IVORY STATE - 02 MALE FACES (10 WEST AFRICAN PRESETS)")
            log("  -> 02_male_faces_10.png OK")
            _render_faces_sheet(female_info, "03_female_faces_10.png", "IVORY STATE - 03 FEMALE FACES (10 WEST AFRICAN PRESETS)")
            log("  -> 03_female_faces_10.png OK")
            _render_hair_sheet(male_info, m_hairs, MALE_HAIR_SPECS, "04_male_hair_10.png", "IVORY STATE - 04 MALE HAIRSTYLES (10 MODULAR 3D STYLES)")
            log("  -> 04_male_hair_10.png OK")
            _render_hair_sheet(female_info, f_hairs, FEMALE_HAIR_SPECS, "05_female_hair_10.png", "IVORY STATE - 05 FEMALE HAIRSTYLES (10 MODULAR 3D STYLES)")
            log("  -> 05_female_hair_10.png OK")
            _render_outfits_sheet(male_info, m_outfits, MALE_OUTFIT_SPECS, m_hairs, MALE_HAIR_SPECS, m_shoes, MALE_SHOE_SPECS, "06_male_outfits_10.png", "IVORY STATE - 06 MALE WARDROBE (10 COMPLETE WEARABLE OUTFITS)")
            log("  -> 06_male_outfits_10.png OK")
            _render_outfits_sheet(female_info, f_outfits, FEMALE_OUTFIT_SPECS, f_hairs, FEMALE_HAIR_SPECS, f_shoes, FEMALE_SHOE_SPECS, "07_female_outfits_10.png", "IVORY STATE - 07 FEMALE WARDROBE (10 COMPLETE WEARABLE OUTFITS)")
            log("  -> 07_female_outfits_10.png OK")
            _render_08_shoes_and_accessories(male_info, female_info, m_shoes, f_shoes, m_accs, f_accs, m_hairs, f_hairs, m_outfits, f_outfits)
            log("  -> 08_shoes_and_accessories.png OK")

            bpy.context.window.scene = male_scene
            bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
            log(f"[STUDIO] ALL DONE! {BLEND_OUT}")
        except Exception:
            err = traceback.format_exc()
            log("[STUDIO][ERROR]\n" + err)
            raise


if __name__ == "__main__":
    main()
