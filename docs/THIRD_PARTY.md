# Ressources tierces

Toutes les ressources ci-dessous peuvent être utilisées, modifiées (par exemple dans Blender) et redistribuées dans un jeu commercial.

| Ressource | Source | Licence | Emplacement |
|---|---|---|---|
| Textures PBR : asphalte, béton, pavés, enduits, rideau métallique, tôle, tuiles en terre cuite (`clay_roof_tiles_02`), tomettes (`terracotta_floor_tiles`), parquet (`laminate_floor_02`), brique (`brick_wall_005`) ; intérieurs : carrelage, marbre, bois sombre, denim, tissu à motifs, mosaïque, gazon, sol antidérapant | [Poly Haven](https://polyhaven.com) | CC0 (domaine public) | `IvoryState/Assets/_Project/ThirdParty/PolyHaven/Textures` |
| Objets : mobilier de rue et maquis (chaises, tables, casiers, bidons, fûts, pneus, lampadaires, poteaux, coffrets, bancs), salon/chambre/cuisine (canapés, fauteuils, lit, chevets, tables, consoles TV, armoire, bibliothèque, four, micro-ondes, ventilo/lustre, horloge, cadres, miroir, ordinateur, boombox), jardin/terrasse (bacs à fleurs, plantes, arbustes, salons de jardin), boutiques/hôpital/quincaillerie (étagères, caisse enregistreuse, fauteuil roulant, néons, caddies, bouteilles de gaz, groupe électrogène, sacs de ciment, outils) | [Poly Haven](https://polyhaven.com) | CC0 (domaine public) | `IvoryState/Assets/_Project/ThirdParty/PolyHaven/Models` |
| Personnages : 7 avatars adultes rigués (squelette Bip01) et animations attente, marche, course | [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox) | MIT, © 2020 Microsoft (avis de copyright à conserver) | `IvoryState/Assets/_Project/ThirdParty/Rocketbox` |
| FishNet (réseau) | [FirstGearGames](https://github.com/FirstGearGames/FishNet) | Licence FishNet (usage commercial autorisé) | paquet UPM |
| Nakama (backend) | [Heroic Labs](https://heroiclabs.com) | Apache 2.0 | image Docker |
| Tracé de la ville d'Abidjan, de Port-Bouët à Abobo (grands axes, lagune Ébrié, océan, forêt du Banco, aéroport, emplacement des monuments), simplifié et réduit au 1/8 | [OpenStreetMap](https://www.openstreetmap.org/copyright) | ODbL 1.0, © les contributeurs d'OpenStreetMap (mention obligatoire dans les crédits du jeu) | `tools/citylayout/abidjan_plan.json`, `IvoryState/Assets/_Project/Resources/CityLayout.json` (récupération : `tools/citylayout/fetch_osm.py`) |
| Relief (altitudes) | [SRTM 30 m](https://www.earthdata.nasa.gov/data/instruments/srtm) (NASA), via l'API publique [OpenTopoData](https://www.opentopodata.org) | Domaine public (données de la NASA) | grille d'altitudes de `abidjan_plan.json` et `CityLayout.json` |

Le CC0 n'impose aucune attribution, mais on crédite quand même Poly Haven. Les données OpenStreetMap
imposent la mention « © les contributeurs d'OpenStreetMap » dans le jeu (écran des crédits) ; la base
dérivée (`abidjan_plan.json`, `CityLayout.json`) reste sous ODbL.

## Téléchargement

Les fichiers Poly Haven sont versionnés dans le dépôt. Pour les retélécharger ou en ajouter, modifier la liste dans `tools/fetch-assets.ps1`, puis lancer :

```
powershell -File tools/fetch-assets.ps1
```

Les textures sont en résolution 1K, qui suffit sur un téléphone.

Les personnages Rocketbox se téléchargent avec un script à part :

```
powershell -File tools/fetch-characters.ps1
```

Ce script réduit les textures d'origine (TGA de 2048 px) en JPEG ou PNG de 1024 px.

Rocketbox ne propose pas de femme noire adulte, et deux des hommes retenus ont le teint clair. Pour ces cinq avatars (Female_Adult_03, 12 et 15, Male_Adult_04 et 12), le script détecte les tons chair et assombrit la peau en gardant l'ombrage. Les vêtements ne changent pas. La licence MIT autorise cette modification ; l'avis de copyright de Microsoft doit accompagner les fichiers redistribués.

## Ressources générées par le code (aucune licence tierce)

- Taxi : maillage produit par `TaxiModelBuilder`.
- Façades : le shader `IvoryState/Facade` calcule les fenêtres, les rideaux et les lumières de nuit.
- Eau de la lagune : shader `IvoryState/Water`.
- Lumières de nuit : shader `IvoryState/NightLight`.
- Cocotiers : maillages et texture des palmes produits par `PalmBuilder`.
- Bâtiments, balcons, auvents, réservoirs, lampadaires, poteaux et fils : `CityGenerator` et `CityDecor`.
