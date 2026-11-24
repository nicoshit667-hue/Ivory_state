<#
.SYNOPSIS
    Télécharge les assets libres (CC0) utilisés par Ivory State depuis Poly Haven.

.DESCRIPTION
    Textures PBR (couleur + relief, 1K JPEG) et objets 3D (FBX 1K + textures de couleur), dans
    IvoryState/Assets/_Project/ThirdParty/PolyHaven/. Licence CC0 : utilisation, modification et
    redistribution libres, sans attribution obligatoire (on crédite quand même dans docs/THIRD_PARTY.md).

    Résolution 1K : suffisante sur mobile et économe en mémoire et en taille d'APK.
    Idempotent : un fichier déjà présent n'est pas retéléchargé.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'IvoryState\Assets\_Project\ThirdParty\PolyHaven'

# Textures de surface (identifiant Poly Haven).
$textures = @(
    'asphalt_02',            # chaussée
    'concrete_pavement_02',  # trottoirs
    'herringbone_pavement',  # place du Plateau
    'painted_plaster_wall',  # façades peintes (teintées par quartier)
    'peeling_painted_wall',  # façades usées des quartiers populaires
    'painted_metal_shutter', # rideaux métalliques des boutiques (rez-de-chaussée)
    'corrugated_iron_02',    # toits en tôle
    'concrete',              # toits-terrasses en béton
    # Intérieurs (logements, hôpitaux)
    'floor_tiles_06',        # carrelage des studios et appartements
    'marble_tiles',          # sol des villas de luxe
    'dark_wood',             # meubles (lits, armoires, tables)
    'denim_fabric',          # draps
    'fabric_pattern_07',     # tissu à motifs (coussins, canapé, tapis « pagne »)
    'marble_mosaic_tiles',   # faïence des douches (mosaïque blanche)
    'leafy_grass',           # gazon des jardins
    'anti_skid_tiles',       # sol des hôpitaux
    'clay_roof_tiles_02',    # tuiles en terre cuite (toits des villas)
    'terracotta_floor_tiles',# dalles de terrasse et vérandas
    'laminate_floor_02',     # parquet des chambres
    'brick_wall_005'         # soubassements et murets en briques
)

# Objets 3D.
$models = @(
    'plastic_monobloc_chair_01', # chaise de maquis
    'plastic_crate_01',          # casier en plastique
    'plastic_jerrycan',          # bidon
    'trashbag',
    'metal_trash_can',
    'old_tyre',
    # Arbres Poly Haven écartés : 46 à 72 Mo et des centaines de milliers de triangles,
    # inutilisables sur mobile. Palmiers générés en code (CityGenerator).
    'round_wooden_table_01',     # table de maquis
    'barrel_03',
    'Barrel_02',                 # fût bleu / eau de quartier (~2,7k tris)
    'plastic_container',         # bac plastique léger (~4k tris)
    'cardboard_box_01',
    # Mobilier et électroménager des maisons (studios, F2, F3, villas)
    'Sofa_01',                   # canapé 3 places
    'ArmChair_01',               # fauteuil de salon
    'modern_coffee_table_01',    # table basse
    'Television_01',             # téléviseur écran plat
    'ClassicConsole_01',         # meuble TV / console
    'WoodenTable_01',            # table en bois (~950 tris)
    'SchoolDesk_01',             # bureau d'examen / étude (~4,1k tris)
    'dining_table',              # grande table à manger
    'painted_wooden_chair_01',   # chaise en bois (~720 tris)
    'painted_wooden_chair_02',   # chaise de salle à manger / terrasse (~1,2k tris)
    'painted_wooden_stool',      # tabouret en bois (~680 tris)
    'electric_stove',            # cuisinière avec four
    'vintage_microwave',         # micro-ondes
    'GothicBed_01',              # lit double en bois
    'ClassicNightstand_01',      # table de chevet
    'painted_wooden_nightstand', # chevet peint (~470 tris)
    'painted_wooden_cabinet',    # armoire / commode
    'painted_wooden_cabinet_02', # petit meuble peint (~970 tris)
    'Shelf_01',                  # étagère murale légère (~180 tris)
    'wooden_bookshelf_worn',     # bibliothèque / étagère
    'ceiling_fan',               # ventilateur de plafond
    'wall_clock',                # horloge murale
    'fancy_picture_frame_01',    # cadre / tableau mural
    # Plantes, jardins, marchés et accessoires urbains
    'potted_plant_04',           # plante en pot d'intérieur
    'ceramic_vase_01',           # pot / vase en céramique (~2,5k tris)
    'planter_box_01',            # jardinière en bois
    'shrub_sorrel_01',           # petit arbuste de jardin
    'painted_wooden_bench',      # banc de jardin et de parc (~630 tris)
    'small_lpg_tank',            # bouteille de gaz
    'hand_truck',                # diable de manutention (marchés, gares)
    'wooden_crate_01',           # caisse en bois (marchés, galeries de gbaka)
    'cement_bag',                # sac de ciment (~840 tris)
    'standing_chalkboard_01',    # ardoise / menu de rue (~2,4k tris)
    'football',                  # ballon de foot (terrains, cours)
    # Compléments : façades, jardins, boutiques, hôpitaux et ateliers
    'exterior_aircon_unit',      # climatiseur extérieur sur les façades
    'outdoor_table_chair_set_01',# salon de jardin / terrasse (villas, Hôtel Ivoire)
    'sofa_02',                   # canapé moderne d'angle / 2 places
    'sofa_03',                   # canapé de salon
    'ornate_mirror_01',          # miroir (salles de bain, chambres)
    'Chandelier_01',             # lustre (villas de luxe)
    'steel_frame_shelves_01',    # rayons métalliques (superettes, pharmacies)
    'wooden_display_shelves_01', # étagères en bois (boutiques, salons)
    'CashRegister_01',           # caisse enregistreuse
    'wheelchair_01',             # fauteuil roulant (hôpitaux)
    'medical_box',               # trousse médicale d'urgence (~3,7k tris)
    'medical_tape',              # ruban médical (~1,4k tris)
    'WetFloorSign_01',           # panneau sol glissant (~230 tris)
    'fire_alarm',                # boîtier alarme incendie (~2,7k tris)
    'office_notepads',           # dossiers / bloc-notes (~670 tris)
    'calathea_orbifolia_01',     # plante verte d'intérieur
    'shrub_02',                  # buisson / arbuste de cour
    'boombox',                   # poste radio (maquis, appartements)
    'classic_laptop',            # ordinateur portable (bureaux, villas)
    'rusted_wheel_rim_01',       # jante (marché black, garages)
    'tire_pump',                 # pompe à vélo / pneu (auto-école, ateliers)
    'tool_cart',                 # servante d'atelier (concessionnaires, garages)
    'portable_generator',        # groupe électrogène
    'utility_box_01',            # armoire électrique de rue
    'machete',                   # machette (quincailleries)
    'bananas'                    # régimes de bananes (marchés)
)

function Get-File([string]$url, [string]$path) {
    if ((Test-Path $path) -and ((Get-Item $path).Length -gt 0)) { return }
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    & curl.exe -fsSL --retry 3 -A "ivory-tools/1.0" $url -o $path
    if ($LASTEXITCODE -ne 0) { throw "Échec du téléchargement : $url" }
}

function Get-PolyHavenJson([string]$id) {
    $raw = & curl.exe -fsSL --retry 3 -A "ivory-tools/1.0" "https://api.polyhaven.com/files/$id"
    if ($LASTEXITCODE -ne 0) { throw "Échec API Poly Haven : $id" }
    return ($raw | ConvertFrom-Json)
}

foreach ($id in $textures) {
    $dir = Join-Path $dest "Textures\$id"
    $diffPath = Join-Path $dir "${id}_diff_1k.jpg"
    $norPath = Join-Path $dir "${id}_nor_gl_1k.jpg"
    if ((Test-Path $diffPath) -and ((Get-Item $diffPath).Length -gt 0) -and (Test-Path $norPath) -and ((Get-Item $norPath).Length -gt 0)) {
        Write-Host "texture $id (déjà présent)"
        continue
    }
    $files = Get-PolyHavenJson $id
    # Certains tissus n'ont pas de « Diffuse » mais des variantes de couleur (col_1, col_01…) : la première.
    $diffuse = $files.Diffuse
    if (-not $diffuse) {
        $variant = $files.PSObject.Properties | Where-Object { $_.Name -match '^col' } | Sort-Object Name | Select-Object -First 1
        $diffuse = $variant.Value
    }
    Get-File $diffuse.'1k'.jpg.url $diffPath
    Get-File $files.nor_gl.'1k'.jpg.url $norPath
    Write-Host "texture $id"
}

foreach ($id in $models) {
    $dir = Join-Path $dest "Models\$id"
    $fbxPath = Join-Path $dir "${id}_1k.fbx"
    $texDir = Join-Path $dir "textures"
    if ((Test-Path $fbxPath) -and ((Get-Item $fbxPath).Length -gt 0) -and (Test-Path $texDir)) {
        Write-Host "modèle $id (déjà présent)"
        continue
    }
    $files = Get-PolyHavenJson $id
    $fbx = $files.fbx.'1k'.fbx
    Get-File $fbx.url $fbxPath
    # Seulement les textures de couleur (JPEG ou PNG). Les cartes EXR (relief, rugosité) sont lourdes,
    # et peu visibles sur des objets de décor vus sur un petit écran.
    foreach ($inc in $fbx.include.PSObject.Properties) {
        if ($inc.Name -match '_diff_1k\.(jpg|png)$') {
            Get-File $inc.Value.url (Join-Path $dir $inc.Name)
        }
    }
    Write-Host "modèle $id"
}

$size = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Total : {0:N1} Mo dans {1}" -f $size, $dest)
