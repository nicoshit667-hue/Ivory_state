"""Materials and procedural PBR shaders for Ivory State modular characters:
- Upgraded West African skin shader (MPFB2 v2 + Ambient Occlusion cavity depth + micro-pore detail)
- Realistic eyes, eyelashes, hair-stroke feathered eyebrows, coily/braided Afro hair
- Procedural West African Wax / Ankara / Kita pagne fabrics, denim, cotton, fleece, leather, wool, metals
- Studio backdrop, metric grid floor, and HUD materials (Blender 5.3 / EEVEE compatible).
"""

import bpy


def srgb_to_linear(c):
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def srgb_rgba(r, g, b, a=1.0):
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), a)


# 10 authentic West African / Ivorian skin tones (deep obsidian -> warm golden-amber brown)
SKIN_TONE_PALETTE_SRGB = [
    ("Obsidian",      (0.14, 0.085, 0.062)),
    ("Deep Ebony",    (0.18, 0.112, 0.078)),
    ("Espresso",      (0.23, 0.145, 0.100)),
    ("Mahogany",      (0.28, 0.180, 0.124)),  # Default base hero tone
    ("Rich Umber",    (0.33, 0.215, 0.148)),
    ("Warm Cocoa",    (0.37, 0.248, 0.172)),
    ("Chestnut",      (0.41, 0.278, 0.194)),
    ("Warm Bronze",   (0.45, 0.308, 0.216)),
    ("Deep Caramel",  (0.49, 0.340, 0.238)),
    ("Honey Amber",   (0.53, 0.372, 0.262)),
]

DEFAULT_SKIN_INDEX = 3  # Mahogany


def _inject_skin_ao_and_microrelief(nt):
    """Splice Ambient Occlusion cavity shading into MpfbSkinMasterColor -> body/face/ears for rich anatomy."""
    if nt.nodes.get("Ivory_Skin_AO_Mix"):
        return
    colorgroup = nt.nodes.get("colorgroup")
    if not colorgroup:
        return

    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.name = "Ivory_Skin_AO"
    ao.samples = 8
    ao.inputs["Distance"].default_value = 0.095
    ao.location = (180, 1250)

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.name = "Ivory_Skin_AO_Ramp"
    ramp.location = (340, 1250)
    ramp.color_ramp.elements[0].position = 0.18
    ramp.color_ramp.elements[0].color = (0.30, 0.22, 0.18, 1.0)  # Warm deep umber cavity
    ramp.color_ramp.elements[1].position = 0.94
    ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    nt.links.new(ao.outputs["AO"], ramp.inputs["Fac"])

    mix = nt.nodes.new("ShaderNodeMix")
    mix.name = "Ivory_Skin_AO_Mix"
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 0.78
    mix.location = (520, 1250)

    nt.links.new(colorgroup.outputs["SkinColor"], mix.inputs["A"])
    nt.links.new(ramp.outputs["Color"], mix.inputs["B"])

    for target_name in ("bodygroup", "facegroup", "earsgroup"):
        tnode = nt.nodes.get(target_name)
        if tnode and "SkinColor" in tnode.inputs:
            nt.links.new(mix.outputs["Result"], tnode.inputs["SkinColor"])


def create_ivory_skin_material(name, basemesh_obj, tone_index=DEFAULT_SKIN_INDEX):
    """Create and configure MPFB2 v2 procedural skin tuned for sculpted realistic West African skin."""
    from bl_ext.blender_org.mpfb.services import MaterialService

    mat = MaterialService.create_v2_skin_material(name, basemesh_obj)
    _inject_skin_ao_and_microrelief(mat.node_tree)
    apply_skin_tone_to_material(mat, tone_index)
    return mat


def apply_skin_tone_to_material(mat, tone_index=DEFAULT_SKIN_INDEX):
    """Tune MpfbSkinMasterColor and body/face/lips/ears/nails groups for realistic dark skin."""
    from bl_ext.blender_org.mpfb.services import NodeService

    idx = max(0, min(len(SKIN_TONE_PALETTE_SRGB) - 1, tone_index))
    _, (sr, sg, sb) = SKIN_TONE_PALETTE_SRGB[idx]
    skin_lin = srgb_rgba(sr, sg, sb, 1.0)
    lips_lin = srgb_rgba(sr * 0.76 + 0.045, sg * 0.56 + 0.020, sb * 0.56 + 0.020, 1.0)
    eyelid_lin = srgb_rgba(sr * 0.70, sg * 0.66, sb * 0.64, 1.0)
    navel_lin = srgb_rgba(sr * 0.58, sg * 0.54, sb * 0.52, 1.0)
    aureola_lin = srgb_rgba(sr * 0.55, sg * 0.50, sb * 0.46, 1.0)
    nail_lin = srgb_rgba(min(1.0, sr * 1.15 + 0.14), min(1.0, sg * 1.12 + 0.11), min(1.0, sb * 1.12 + 0.10), 1.0)
    vein_lin = srgb_rgba(sr * 0.85, sg * 0.82, sb * 0.82, 1.0)

    nt = mat.node_tree
    mc = NodeService.find_first_group_node_by_tree_name(nt, "MpfbSkinMasterColor")
    if mc:
        vals = {
            "DiffuseTextureStrength": 0.0,
            "SkinColor": skin_lin,
            "SkinOverride": 1.0,
            "LipsColor": lips_lin,
            "LipsOverride": 1.0,
            "EyelidColor": eyelid_lin,
            "EyelidOverride": 1.0,
            "NavelCenterColor": navel_lin,
            "NavelCenterOverride": 1.0,
            "AureolaeColor": aureola_lin,
            "AurolaeOverride": 1.0,
            "FingernailsColor": nail_lin,
            "FingernailsOverride": 1.0,
            "ToenailsColor": nail_lin,
            "ToenailsOverride": 1.0,
            "SpotColor": srgb_rgba(sr * 0.62, sg * 0.52, sb * 0.44, 1.0),
            "SpotOverride": 1.0,
            "VeinColor": vein_lin,
        }
        for k, v in vals.items():
            if k in mc.inputs:
                mc.inputs[k].default_value = v

    # Balanced natural velvety satin-matte skin specular & fine micro-pores (identical across zones => zero neck seam)
    group_tweaks = {
        "bodygroup": {
            "Roughness": 0.62,
            "VeinStrength": 0.0,
            "SpotStrength": 0.06,
            "ColorVariationStrength": 0.08,
            "ColorVariationScaleMultiplier": 32.0,
            "UnevennessBumpStrength": 0.018,
            "UnevennessScaleMultiplier": 220.0,
            "DermalBumpStrength": 0.032,
            "DermalScaleMultiplier": 230.0,
            "SSSWeight": 0.14,
            "SSSRadiusMultiplyer": 0.58,
            "SSSRadiusX": 1.0,
            "SSSRadiusY": 0.34,
            "SSSRadiusZ": 0.14,
            "NavelColorStrength": 0.82,
            "NavelBumpStrength": 0.22,
        },
        "facegroup": {
            "Roughness": 0.62,
            "VeinStrength": 0.0,
            "SpotStrength": 0.05,
            "ColorVariationStrength": 0.08,
            "ColorVariationScaleMultiplier": 36.0,
            "UnevennessBumpStrength": 0.014,
            "UnevennessScaleMultiplier": 260.0,
            "DermalBumpStrength": 0.026,
            "DermalScaleMultiplier": 270.0,
            "SSSWeight": 0.14,
            "SSSRadiusMultiplyer": 0.58,
            "SSSRadiusX": 1.0,
            "SSSRadiusY": 0.34,
            "SSSRadiusZ": 0.14,
        },
        "earsgroup": {
            "Roughness": 0.62,
            "VeinStrength": 0.0,
            "SpotStrength": 0.04,
            "ColorVariationStrength": 0.06,
            "UnevennessBumpStrength": 0.012,
            "DermalBumpStrength": 0.020,
            "SSSWeight": 0.22,
        },
        "lipsgroup": {
            "Roughness": 0.34,
            "SSSWeight": 0.22,
        },
        "aureolaegroup": {
            "Roughness": 0.50,
        },
        "fingernailsgroup": {
            "Roughness": 0.24,
        },
        "toenailsgroup": {
            "Roughness": 0.28,
        },
    }

    for node_name, settings in group_tweaks.items():
        node = nt.nodes.get(node_name)
        if not node:
            continue
        for sock_name, val in settings.items():
            if sock_name in node.inputs:
                node.inputs[sock_name].default_value = val


def clone_skin_material_with_tone(src_mat, new_name, tone_index):
    """Clone an Ivory skin material with its own node tree and apply a specific skin tone index."""
    existing = bpy.data.materials.get(new_name)
    if existing:
        apply_skin_tone_to_material(existing, tone_index)
        return existing
    mat = src_mat.copy()
    mat.name = new_name
    apply_skin_tone_to_material(mat, tone_index)
    return mat


def create_eye_material(name="Ivory_Eye_DarkBrown"):
    """Procedural realistic eyeball material with rich dark brown iris, limbal ring & glossy wet cornea."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.03
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = 0.95
        bsdf.inputs["Coat Roughness"].default_value = 0.015

    tex_coord = nt.nodes.new("ShaderNodeTexCoord")
    sub = nt.nodes.new("ShaderNodeVectorMath")
    sub.operation = "SUBTRACT"
    sub.inputs[1].default_value = (0.5, 0.5, 0.0)
    nt.links.new(tex_coord.outputs["UV"], sub.inputs[0])

    length = nt.nodes.new("ShaderNodeVectorMath")
    length.operation = "LENGTH"
    nt.links.new(sub.outputs["Vector"], length.inputs[0])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 55.0
    noise.inputs["Detail"].default_value = 8.0
    nt.links.new(tex_coord.outputs["UV"], noise.inputs["Vector"])

    mix_r = nt.nodes.new("ShaderNodeMath")
    mix_r.operation = "MULTIPLY_ADD"
    mix_r.inputs[1].default_value = 0.018
    nt.links.new(noise.outputs["Fac"], mix_r.inputs[0])
    nt.links.new(length.outputs["Value"], mix_r.inputs[2])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.13
    cr.elements[0].color = (0.003, 0.002, 0.002, 1.0)
    e1 = cr.elements.new(0.15)
    e1.color = srgb_rgba(0.14, 0.072, 0.035, 1.0)
    e2 = cr.elements.new(0.30)
    e2.color = srgb_rgba(0.075, 0.038, 0.020, 1.0)
    e3 = cr.elements.new(0.34)
    e3.color = srgb_rgba(0.028, 0.016, 0.012, 1.0)
    cr.elements[-1].position = 0.37
    cr.elements[-1].color = srgb_rgba(0.76, 0.74, 0.70, 1.0)

    nt.links.new(mix_r.outputs["Value"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_buzzcut_material(name="Ivory_Scalp_BuzzCut"):
    """Procedural natural coily buzz-cut/fade shader with vertex alpha attribute 'fade_alpha'."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    if hasattr(mat, "blend_method"):
        mat.blend_method = "HASHED"
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"

    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix_sh = nt.nodes.new("ShaderNodeMixShader")
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.84
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.22

    tc = nt.nodes.new("ShaderNodeTexCoord")
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 480.0
    nt.links.new(tc.outputs["Object"], vor.inputs["Vector"])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 260.0
    noise.inputs["Detail"].default_value = 6.0
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])

    add_h = nt.nodes.new("ShaderNodeMath")
    add_h.operation = "ADD"
    nt.links.new(vor.outputs["Distance"], add_h.inputs[0])
    nt.links.new(noise.outputs["Fac"], add_h.inputs[1])

    col_ramp = nt.nodes.new("ShaderNodeValToRGB")
    col_ramp.color_ramp.elements[0].position = 0.25
    col_ramp.color_ramp.elements[0].color = srgb_rgba(0.055, 0.045, 0.040, 1.0)
    col_ramp.color_ramp.elements[1].position = 1.15
    col_ramp.color_ramp.elements[1].color = srgb_rgba(0.125, 0.105, 0.095, 1.0)
    nt.links.new(add_h.outputs["Value"], col_ramp.inputs["Fac"])
    nt.links.new(col_ramp.outputs["Color"], bsdf.inputs["Base Color"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.55
    bump.inputs["Distance"].default_value = 0.0016
    nt.links.new(add_h.outputs["Value"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "fade_alpha"
    mix_a = nt.nodes.new("ShaderNodeMath")
    mix_a.operation = "POWER"
    mix_a.inputs[1].default_value = 1.1
    nt.links.new(attr.outputs["Fac"], mix_a.inputs[0])

    nt.links.new(mix_a.outputs["Value"], mix_sh.inputs["Fac"])
    nt.links.new(transp.outputs["BSDF"], mix_sh.inputs[1])
    nt.links.new(bsdf.outputs["BSDF"], mix_sh.inputs[2])
    nt.links.new(mix_sh.outputs["Shader"], out.inputs["Surface"])
    return mat


def create_afro_hair_material(name="Ivory_Hair_Coily", style="coily", tint_srgb=(0.022, 0.018, 0.016)):
    """Procedural realistic jet-black/dark-espresso Afro hair shader supporting coily, braided, locs, and wavy hair."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.70 if style == "coily" else (0.54 if style == "braid" else 0.56)
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.28 if style == "braid" else 0.16
    if "Sheen Weight" in bsdf.inputs:
        # Keep sheen very low on dark hair so EEVEE never creates dusty grey halo
        bsdf.inputs["Sheen Weight"].default_value = 0.02
        bsdf.inputs["Sheen Roughness"].default_value = 0.60

    tc = nt.nodes.new("ShaderNodeTexCoord")
    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 6
    ao.inputs["Distance"].default_value = 0.045

    if style == "braid":
        # Diagonal braid chevron ridges along wave texture
        wave = nt.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "DIAGONAL"
        wave.wave_profile = "SIN"
        wave.inputs["Scale"].default_value = 42.0
        wave.inputs["Distortion"].default_value = 2.4
        wave.inputs["Detail"].default_value = 4.0
        nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
        height_sock = wave.outputs["Fac"]
        bump_str = 0.65
    elif style == "waves":
        # 360 waves ripple pattern
        wave = nt.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "RINGS"
        wave.rings_direction = "Z"
        wave.inputs["Scale"].default_value = 26.0
        wave.inputs["Distortion"].default_value = 3.2
        wave.inputs["Detail"].default_value = 4.0
        nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
        height_sock = wave.outputs["Fac"]
        bump_str = 0.50
    else:
        # Tight natural Afro coils (Voronoi + Noise)
        vor = nt.nodes.new("ShaderNodeTexVoronoi")
        vor.inputs["Scale"].default_value = 180.0
        nt.links.new(tc.outputs["Object"], vor.inputs["Vector"])
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 120.0
        noise.inputs["Detail"].default_value = 7.0
        nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
        add = nt.nodes.new("ShaderNodeMath")
        add.operation = "ADD"
        nt.links.new(vor.outputs["Distance"], add.inputs[0])
        nt.links.new(noise.outputs["Fac"], add.inputs[1])
        height_sock = add.outputs["Value"]
        bump_str = 0.72

    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = bump_str
    bump.inputs["Distance"].default_value = 0.0025
    nt.links.new(height_sock, bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # Color darkened in AO crevices for lush depth
    mix_col = nt.nodes.new("ShaderNodeMix")
    mix_col.data_type = "RGBA"
    mix_col.inputs["A"].default_value = srgb_rgba(tint_srgb[0] * 0.45, tint_srgb[1] * 0.45, tint_srgb[2] * 0.45, 1.0)
    mix_col.inputs["B"].default_value = srgb_rgba(tint_srgb[0] * 1.45, tint_srgb[1] * 1.40, tint_srgb[2] * 1.35, 1.0)
    nt.links.new(ao.outputs["AO"], mix_col.inputs["Factor"])
    nt.links.new(mix_col.outputs["Result"], bsdf.inputs["Base Color"])

    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_eyebrow_material(name="Ivory_Eyebrows_Dark"):
    """Feathered fine hair-stroke eyebrow shader using UV elliptical alpha fade."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    if hasattr(mat, "blend_method"):
        mat.blend_method = "HASHED"
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"

    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix_sh = nt.nodes.new("ShaderNodeMixShader")
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = srgb_rgba(0.048, 0.036, 0.030, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.78

    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["UV"], sep.inputs["Vector"])

    # Soft elliptical alpha mask across brow UV (u in [0,1], v in [0,1])
    sub_u = nt.nodes.new("ShaderNodeMath"); sub_u.operation = "SUBTRACT"; sub_u.inputs[1].default_value = 0.5
    nt.links.new(sep.outputs["X"], sub_u.inputs[0])
    mul_u = nt.nodes.new("ShaderNodeMath"); mul_u.operation = "MULTIPLY"
    nt.links.new(sub_u.outputs["Value"], mul_u.inputs[0]); nt.links.new(sub_u.outputs["Value"], mul_u.inputs[1])

    sub_v = nt.nodes.new("ShaderNodeMath"); sub_v.operation = "SUBTRACT"; sub_v.inputs[1].default_value = 0.5
    nt.links.new(sep.outputs["Y"], sub_v.inputs[0])
    mul_v = nt.nodes.new("ShaderNodeMath"); mul_v.operation = "MULTIPLY"
    nt.links.new(sub_v.outputs["Value"], mul_v.inputs[0]); nt.links.new(sub_v.outputs["Value"], mul_v.inputs[1])

    add_r = nt.nodes.new("ShaderNodeMath"); add_r.operation = "ADD"
    nt.links.new(mul_u.outputs["Value"], add_r.inputs[0]); nt.links.new(mul_v.outputs["Value"], add_r.inputs[1])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.04
    ramp.color_ramp.elements[0].color = (0.92, 0.92, 0.92, 1.0)
    ramp.color_ramp.elements[1].position = 0.24
    ramp.color_ramp.elements[1].color = (0.0, 0.0, 0.0, 1.0)
    nt.links.new(add_r.outputs["Value"], ramp.inputs["Fac"])

    # Fine angled hair-stroke density modulation along UV
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "DIAGONAL"
    wave.inputs["Scale"].default_value = 34.0
    wave.inputs["Distortion"].default_value = 2.0
    nt.links.new(tc.outputs["UV"], wave.inputs["Vector"])

    stroke_ramp = nt.nodes.new("ShaderNodeValToRGB")
    stroke_ramp.color_ramp.elements[0].position = 0.20
    stroke_ramp.color_ramp.elements[0].color = (0.62, 0.62, 0.62, 1.0)
    stroke_ramp.color_ramp.elements[1].position = 0.78
    stroke_ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    nt.links.new(wave.outputs["Fac"], stroke_ramp.inputs["Fac"])

    mul_alpha = nt.nodes.new("ShaderNodeMath")
    mul_alpha.operation = "MULTIPLY"
    nt.links.new(ramp.outputs["Color"], mul_alpha.inputs[0])
    nt.links.new(stroke_ramp.outputs["Color"], mul_alpha.inputs[1])

    nt.links.new(mul_alpha.outputs["Value"], mix_sh.inputs["Fac"])
    nt.links.new(transp.outputs["BSDF"], mix_sh.inputs[1])
    nt.links.new(bsdf.outputs["BSDF"], mix_sh.inputs[2])
    nt.links.new(mix_sh.outputs["Shader"], out.inputs["Surface"])
    return mat


def create_eyelash_material(name="Ivory_Eyelashes_Dark"):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = srgb_rgba(0.030, 0.024, 0.022, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.65
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_undergarment_material(name="Ivory_Undergarment_DarkGrey", is_band=False):
    """Matte cotton/elastane athletic fabric shader matching reference dark grey undergarments."""
    return create_fabric_material(
        name,
        srgb_color=(0.20, 0.21, 0.22) if is_band else (0.27, 0.28, 0.29),
        roughness=0.78 if is_band else 0.86,
        weave_scale=420.0,
        bump_strength=0.08,
    )


def create_fabric_material(name, srgb_color, roughness=0.82, metallic=0.0, sheen=0.30, weave_scale=360.0, bump_strength=0.10):
    """General PBR clothing fabric material with woven texture bump & AO depth."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if "Sheen Weight" in bsdf.inputs:
        bsdf.inputs["Sheen Weight"].default_value = sheen
        bsdf.inputs["Sheen Roughness"].default_value = 0.55

    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 6
    ao.inputs["Distance"].default_value = 0.08
    mix_ao = nt.nodes.new("ShaderNodeMix")
    mix_ao.data_type = "RGBA"
    mix_ao.blend_type = "MULTIPLY"
    mix_ao.inputs["Factor"].default_value = 0.55
    mix_ao.inputs["A"].default_value = srgb_rgba(*srgb_color, 1.0)
    nt.links.new(ao.outputs["Color"], mix_ao.inputs["B"])
    nt.links.new(mix_ao.outputs["Result"], bsdf.inputs["Base Color"])

    tc = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = weave_scale
    noise.inputs["Detail"].default_value = 5.0
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = bump_strength
    bump.inputs["Distance"].default_value = 0.0015
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_wax_pagne_material(name="Ivory_Wax_Ankara", palette="abidjan"):
    """Authentic procedural West African Wax / Ankara / Kita geometric printed cotton fabric."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.78
    if "Sheen Weight" in bsdf.inputs:
        bsdf.inputs["Sheen Weight"].default_value = 0.25

    palettes = {
        "abidjan": [
            (0.08, 0.14, 0.34),  # Deep indigo
            (0.90, 0.44, 0.08),  # Côte d'Ivoire vibrant orange
            (0.96, 0.78, 0.18),  # Sun gold
            (0.09, 0.44, 0.28),  # Emerald green
        ],
        "kita": [
            (0.10, 0.08, 0.07),  # Charcoal black
            (0.86, 0.66, 0.14),  # Royal Kita gold
            (0.72, 0.14, 0.12),  # Deep crimson
            (0.10, 0.46, 0.26),  # Forest emerald
        ],
        "terracotta": [
            (0.24, 0.11, 0.09),  # Deep cacao
            (0.80, 0.34, 0.15),  # Terracotta
            (0.94, 0.86, 0.68),  # Cream
            (0.18, 0.44, 0.48),  # Lagoon teal
        ],
    }
    cols = palettes.get(palette, palettes["abidjan"])

    tc = nt.nodes.new("ShaderNodeTexCoord")

    # Geometric diamond / chevron Ankara pattern combining Voronoi + Wave bands
    wave1 = nt.nodes.new("ShaderNodeTexWave")
    wave1.wave_type = "BANDS"
    wave1.bands_direction = "DIAGONAL"
    wave1.wave_profile = "SAW"
    wave1.inputs["Scale"].default_value = 9.5
    wave1.inputs["Distortion"].default_value = 1.8
    nt.links.new(tc.outputs["Object"], wave1.inputs["Vector"])

    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 18.0
    nt.links.new(tc.outputs["Object"], vor.inputs["Vector"])

    mix_pat = nt.nodes.new("ShaderNodeMath")
    mix_pat.operation = "MULTIPLY_ADD"
    mix_pat.inputs[1].default_value = 0.65
    nt.links.new(wave1.outputs["Fac"], mix_pat.inputs[0])
    nt.links.new(vor.outputs["Distance"], mix_pat.inputs[2])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    cr = ramp.color_ramp
    cr.elements[0].position = 0.00
    cr.elements[0].color = srgb_rgba(*cols[0], 1.0)
    e1 = cr.elements.new(0.28)
    e1.color = srgb_rgba(*cols[1], 1.0)
    e2 = cr.elements.new(0.56)
    e2.color = srgb_rgba(*cols[2], 1.0)
    cr.elements[-1].position = 0.80
    cr.elements[-1].color = srgb_rgba(*cols[3], 1.0)

    nt.links.new(mix_pat.outputs["Value"], ramp.inputs["Fac"])

    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 6
    ao.inputs["Distance"].default_value = 0.08
    mix_ao = nt.nodes.new("ShaderNodeMix")
    mix_ao.data_type = "RGBA"
    mix_ao.blend_type = "MULTIPLY"
    mix_ao.inputs["Factor"].default_value = 0.48
    nt.links.new(ramp.outputs["Color"], mix_ao.inputs["A"])
    nt.links.new(ao.outputs["Color"], mix_ao.inputs["B"])
    nt.links.new(mix_ao.outputs["Result"], bsdf.inputs["Base Color"])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 420.0
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.09
    bump.inputs["Distance"].default_value = 0.0012
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_metal_material(name, srgb_color=(0.88, 0.72, 0.28), roughness=0.22):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = srgb_rgba(*srgb_color, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.96
    bsdf.inputs["Roughness"].default_value = roughness
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_leather_material(name, srgb_color=(0.12, 0.11, 0.11), roughness=0.38):
    return create_fabric_material(name, srgb_color, roughness=roughness, metallic=0.02, sheen=0.10, weave_scale=240.0, bump_strength=0.14)


def create_glass_lens_material(name="Ivory_Sunglass_Lens", srgb_color=(0.06, 0.07, 0.09)):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = srgb_rgba(*srgb_color, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.25
    bsdf.inputs["Roughness"].default_value = 0.05
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = 1.0
        bsdf.inputs["Coat Roughness"].default_value = 0.02
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_ui_flat_material(name, srgb_color, alpha=1.0, emission_strength=0.85):
    """Crisp flat UI / HUD material (readable in viewport and camera render)."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    if alpha < 1.0:
        if hasattr(mat, "blend_method"):
            mat.blend_method = "BLEND"
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "BLENDED"
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    lin = srgb_rgba(*srgb_color, 1.0)
    if alpha < 1.0:
        mix_sh = nt.nodes.new("ShaderNodeMixShader")
        transp = nt.nodes.new("ShaderNodeBsdfTransparent")
        emis = nt.nodes.new("ShaderNodeEmission")
        emis.inputs["Color"].default_value = lin
        emis.inputs["Strength"].default_value = emission_strength
        mix_sh.inputs["Fac"].default_value = alpha
        nt.links.new(transp.outputs["BSDF"], mix_sh.inputs[1])
        nt.links.new(emis.outputs["Emission"], mix_sh.inputs[2])
        nt.links.new(mix_sh.outputs["Shader"], out.inputs["Surface"])
    else:
        emis = nt.nodes.new("ShaderNodeEmission")
        emis.inputs["Color"].default_value = lin
        emis.inputs["Strength"].default_value = emission_strength
        nt.links.new(emis.outputs["Emission"], out.inputs["Surface"])
    return mat


def create_image_card_material(name, image_path, alpha=1.0):
    """Material displaying a packed thumbnail PNG cleanly without lighting washout."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    if alpha < 1.0 and hasattr(mat, "blend_method"):
        mat.blend_method = "BLEND"
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")

    img = bpy.data.images.load(image_path, check_existing=True)
    img.pack()

    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.extension = "CLIP"

    emis = nt.nodes.new("ShaderNodeEmission")
    emis.inputs["Strength"].default_value = 1.0
    nt.links.new(tex.outputs["Color"], emis.inputs["Color"])
    nt.links.new(emis.outputs["Emission"], out.inputs["Surface"])
    return mat


def create_studio_grid_floor_material(name="Ivory_Studio_GridFloor"):
    """Neutral grey studio floor with crisp 0.25m metric grid lines."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.88
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.16

    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs["Vector"])

    def grid_axis_mask(coord_sock, period, line_half_width):
        div = nt.nodes.new("ShaderNodeMath"); div.operation = "DIVIDE"; div.inputs[1].default_value = period
        nt.links.new(coord_sock, div.inputs[0])
        rnd = nt.nodes.new("ShaderNodeMath"); rnd.operation = "ROUND"
        nt.links.new(div.outputs["Value"], rnd.inputs[0])
        sub = nt.nodes.new("ShaderNodeMath"); sub.operation = "SUBTRACT"
        nt.links.new(div.outputs["Value"], sub.inputs[0]); nt.links.new(rnd.outputs["Value"], sub.inputs[1])
        ab = nt.nodes.new("ShaderNodeMath"); ab.operation = "ABSOLUTE"
        nt.links.new(sub.outputs["Value"], ab.inputs[0])
        mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = period
        nt.links.new(ab.outputs["Value"], mul.inputs[0])
        lt = nt.nodes.new("ShaderNodeMath"); lt.operation = "LESS_THAN"; lt.inputs[1].default_value = line_half_width
        nt.links.new(mul.outputs["Value"], lt.inputs[0])
        return lt.outputs["Value"]

    mx = grid_axis_mask(sep.outputs["X"], 0.25, 0.004)
    my = grid_axis_mask(sep.outputs["Y"], 0.25, 0.004)
    grid_mask = nt.nodes.new("ShaderNodeMath"); grid_mask.operation = "MAXIMUM"
    nt.links.new(mx, grid_mask.inputs[0]); nt.links.new(my, grid_mask.inputs[1])

    mix_col = nt.nodes.new("ShaderNodeMix"); mix_col.data_type = "RGBA"
    mix_col.inputs["A"].default_value = srgb_rgba(0.43, 0.44, 0.46, 1.0)
    mix_col.inputs["B"].default_value = srgb_rgba(0.30, 0.31, 0.33, 1.0)
    nt.links.new(grid_mask.outputs["Value"], mix_col.inputs["Factor"])
    nt.links.new(mix_col.outputs["Result"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def create_studio_wall_material(name="Ivory_Studio_Backdrop"):
    """Neutral grey backdrop wall with faint horizontal measurement lines every 0.15m."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.92
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.08

    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs["Vector"])

    div = nt.nodes.new("ShaderNodeMath"); div.operation = "DIVIDE"; div.inputs[1].default_value = 0.15
    nt.links.new(sep.outputs["Z"], div.inputs[0])
    rnd = nt.nodes.new("ShaderNodeMath"); rnd.operation = "ROUND"
    nt.links.new(div.outputs["Value"], rnd.inputs[0])
    sub = nt.nodes.new("ShaderNodeMath"); sub.operation = "SUBTRACT"
    nt.links.new(div.outputs["Value"], sub.inputs[0]); nt.links.new(rnd.outputs["Value"], sub.inputs[1])
    ab = nt.nodes.new("ShaderNodeMath"); ab.operation = "ABSOLUTE"
    nt.links.new(sub.outputs["Value"], ab.inputs[0])
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 0.15
    nt.links.new(ab.outputs["Value"], mul.inputs[0])
    lt = nt.nodes.new("ShaderNodeMath"); lt.operation = "LESS_THAN"; lt.inputs[1].default_value = 0.0025
    nt.links.new(mul.outputs["Value"], lt.inputs[0])

    mix_col = nt.nodes.new("ShaderNodeMix"); mix_col.data_type = "RGBA"
    mix_col.inputs["A"].default_value = srgb_rgba(0.45, 0.46, 0.48, 1.0)
    mix_col.inputs["B"].default_value = srgb_rgba(0.54, 0.55, 0.57, 1.0)
    nt.links.new(lt.outputs["Value"], mix_col.inputs["Factor"])
    nt.links.new(mix_col.outputs["Result"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat
