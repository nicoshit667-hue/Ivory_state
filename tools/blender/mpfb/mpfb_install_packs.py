import bpy, sys, os, zipfile, glob
import addon_utils

addon_utils.enable("bl_ext.blender_org.mpfb", default_set=True)
from bl_ext.blender_org.mpfb.services import LocationService, AssetService

packs = sys.argv[sys.argv.index("--") + 1:]
data_dir = LocationService.get_user_data()
print("USER_DATA", data_dir)

for p in packs:
    check = AssetService.check_asset_pack_zip(p)
    if check is not None:
        print("CHECK_PROBLEM", p, check)
        res = AssetService.fix_and_extract_asset_pack_zip(p, data_dir)
        print("FIX_RESULT", p, res)
    else:
        with zipfile.ZipFile(p, "r") as z:
            z.extractall(data_dir)
        print("INSTALLED", os.path.basename(p))

AssetService.update_all_asset_lists()

for sub in ("skins", "eyes", "eyebrows", "eyelashes", "hair", "proxymeshes", "clothes", "teeth", "tongue"):
    d = os.path.join(data_dir, sub)
    items = sorted(os.listdir(d)) if os.path.isdir(d) else []
    print("ASSETS", sub, len(items), items)
