# Véhicules : sources et licences

Tous les modèles viennent de Sketchfab, sous licence **Creative Commons Attribution 4.0 (CC BY 4.0)** :
usage commercial autorisé, **auteur à créditer** (fait dans Téléphone > Réglages > crédits).

**Modifications apportées** (à signaler selon la licence) : véhicules séparés des packs, remis droits et posés au sol,
mis à l'échelle réelle en mètres, textures réduites en 1K et regroupées (couleur, relief, métal/brillance) ;
moto : marquages de marque effacés (« TT », « DS », « 200 »).

| Modèles (dossier `Models/`) | Œuvre | Auteur | Source |
|---|---|---|---|
| `compact`, `coupe`, `hatchback`, `minivan`, `offroad`, `pickup`, `sedan`, `sport`, `suv`, `wagon` | Generic passenger car pack | Comrade1280 | https://sketchfab.com/3d-models/generic-passenger-car-pack-20f9af9b8a404d5cb022ac6fe87f21f5 |
| `ambulance`, `citybus`, `fire_truck`, `garbage_truck`, `police_sedan`, `postvan`, `rdservtruck`, `school_bus`, `taxi`, `towtruck` | Generic civil service vehicles pack | Comrade1280 | https://sketchfab.com/3d-models/generic-civil-service-vehicles-pack-8ff2a13f30914932a70c7950cfa58465 |
| `supercar` | Generic Supercar "Cheetah" | mk2design | https://sketchfab.com/3d-models/generic-supercar-cheetah-f5420af7b30c4e8c874be33cc58724f2 |
| `trottinette` | Electric Scooter | Gest.lt | https://sketchfab.com/3d-models/electric-scooter-f3785071e9244d83ba311b7da48a1085 |
| `moto` | Akt Tt Ds 200 repainted | e-restrepo1114 (EmanuelRestrepoVelez) | https://sketchfab.com/3d-models/akt-tt-ds-200-repainted-c32f7b87090d4ce4b10c76e844dc86cd |
| `camion_militaire` | Ural 4320 | Brout (davidbroutian) | https://sketchfab.com/3d-models/ural-4320-f953c51a5dbc4a15949f4dcc0905c4e8 |

Notes :
- Électric Scooter porte la mention « NoAI » de Sketchfab : ne pas l'utiliser pour entraîner ou alimenter une IA générative.
- En jeu, noms génériques (pas « Ural », pas « AKT ») : les formes restent inspirées de vrais véhicules.
- Préfabs : `Prefabs/Vehicles/Models/PF_Vehicle_<id>.prefab`, construits par `VehicleModelBuilder`
  (menu Ivory State > Véhicules). Avant vers +Z, pivot au sol ; pas encore branchés sur la conduite.
- Archives d'origine et étapes de conversion : `_downloads/` (hors dépôt).
