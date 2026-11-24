"""Génère l'homme et la femme par défaut d'Ivory State avec MPFB2, puis enregistre un .blend par personnage."""
import bpy, sys, os, json, copy
import addon_utils

addon_utils.enable("bl_ext.blender_org.mpfb", default_set=True)
from bl_ext.blender_org.mpfb.services import HumanService, TargetService

out_dir = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out_dir, exist_ok=True)

CHARACTERS = {
    "Male_MPFB_Default": dict(
        pheno=dict(gender=1.0, age=0.5, muscle=0.6, weight=0.5, height=0.55, proportions=0.6),
        skin="young_african_male/young_african_male.mhmat",
        proxy="male_generic/male_generic.proxy",
        hair="afro01/afro01.mhclo",
        eyebrows="eyebrow001/eyebrow001.mhclo",
        clothes=["male_casualsuit02/male_casualsuit02.mhclo", "shoes01/shoes01.mhclo"],
    ),
    "Female_MPFB_Default": dict(
        pheno=dict(gender=0.0, age=0.5, muscle=0.5, weight=0.5, height=0.5, proportions=0.6),
        skin="young_african_female/young_african_female.mhmat",
        proxy="female_generic/female_generic.proxy",
        hair="braid01/braid01.mhclo",
        eyebrows="eyebrow002/eyebrow002.mhclo",
        clothes=["female_casualsuit01/female_casualsuit01.mhclo", "shoes02/shoes02.mhclo"],
    ),
}

default_pheno = TargetService.get_default_macro_info_dict()
print("DEFAULT_PHENO", json.dumps(default_pheno))

only = sys.argv[sys.argv.index("--") + 2:]
for name, c in CHARACTERS.items():
    if only and name not in only:
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    addon_utils.enable("bl_ext.blender_org.mpfb", default_set=True)

    pheno = copy.deepcopy(default_pheno)
    pheno.update(c["pheno"])
    pheno["race"] = {"african": 1.0, "asian": 0.0, "caucasian": 0.0}

    info = HumanService._create_default_human_info_dict()
    info.update({
        "name": name,
        "phenotype": pheno,
        "rig": "game_engine",
        "proxy": c["proxy"],
        "eyes": "low-poly/low-poly.mhclo",
        "eyebrows": c["eyebrows"],
        "eyelashes": "eyelashes01/eyelashes01.mhclo",
        "hair": c["hair"],
        "teeth": "",
        "tongue": "",
        "clothes": c["clothes"],
        "skin_mhmat": c["skin"],
        "skin_material_type": "MAKESKIN",
        "eyes_material_type": "MAKESKIN",
        # Yeux marron foncé (la matière par défaut donnait un iris rougeâtre).
        "alternative_materials": {"1cc97a30-85a1-42e6-a9f7-b3e753732baa": "materials/brown.mhmat"},
    })
    settings = HumanService.get_default_deserialization_settings()
    settings["subdiv_levels"] = 0
    settings["override_rig"] = "game_engine"

    basemesh = HumanService.deserialize_from_dict(info, settings)
    print("CREATED", name, basemesh.name)

    for ob in bpy.data.objects:
        line = f"  OBJ {ob.name} type={ob.type} parent={ob.parent.name if ob.parent else None} hide_render={ob.hide_render}"
        if ob.type == "MESH":
            dg = bpy.context.evaluated_depsgraph_get()
            ev = ob.evaluated_get(dg).to_mesh()
            ev.calc_loop_triangles()
            line += f" tris(eval)={len(ev.loop_triangles)} mats={[m.name for m in ob.data.materials if m]} mods={[m.type for m in ob.modifiers]}"
            ob.evaluated_get(dg).to_mesh_clear()
        if ob.type == "ARMATURE":
            line += f" bones={len(ob.data.bones)}"
        print(line)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, name + ".blend"))
    print("SAVED", name)
