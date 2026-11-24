# Personnages MPFB : sources et licences

Les avatars citoyens par défaut de ce dossier sont générés dans Blender via **MPFB2** (MakeHuman Plugin For Blender) à partir des assets système officiels de **MakeHuman Community** (`makehuman_system_assets`), publiés dans le domaine public sous licence **CC0 1.0** (septembre 2020 — Data Collection AB, Joel Palmius, Jonas Hauquier, <http://www.makehumancommunity.org>).

L'attribution dans `Téléphone > Réglages` n'est pas juridiquement requise par la licence CC0, mais MakeHuman / MPFB y est mentionné à titre de crédit.

## Modifications apportées (`tools/blender/mpfb/`)

- Génération paramétrique (`mpfb_generate.py`) avec rig `game_engine` compatible Unity Humanoid.
- Export et optimisation mobile (`mpfb_export.py`) : application des modificateurs, décimation (corps ~9 000 tris, vêtements ~3 200, cheveux ~2 500, chaussures ~1 000), correction chromatique de l'iris marron, fusion en un maillage unique `<Nom>_Mesh` en pose de repos et export FBX avec textures PNG.
- Configuration Unity (`CharacterSetup.cs`) : matériaux URP/Lit, découpe alpha sur cheveux/sourcils/cils, variantes chromatiques de tenues (`tenue1` à `tenue3`) et génération des préfabs `Assets/_Project/Prefabs/Characters/Avatars/PF_Avatar_*_MPFB_Default.prefab`.

## Modèles inclus

| ID | Préréglage & morphologie | Éléments équipés (`makehuman_system_assets`) | Source | Licence |
| :--- | :--- | :--- | :--- | :--- |
| `Male_MPFB_Default` | `young_african_male`, peau `male_generic` | `afro01`, `eyebrow001`, cils par défaut, yeux `brown`, `male_casualsuit02`, `shoes01` | MakeHuman Community / MPFB2 | CC0 1.0 |
| `Female_MPFB_Default` | `young_african_female`, peau `female_generic` | `braid01`, `eyebrow002`, cils par défaut, yeux `brown`, `female_casualsuit01`, `shoes02` | MakeHuman Community / MPFB2 | CC0 1.0 |
