"""Télécharge les données brutes d'Abidjan utilisées par simplify_osm.py et build_layout.py.

- OpenStreetMap (API Overpass, licence ODbL) : grands axes, rues tertiaires, eau (lagune, baies, canal),
  trait de côte (océan), forêt du Banco, aéroport, lieux remarquables (monuments, hôpitaux, gares...),
  feux tricolores.
- Relief : SRTM 30 m via l'API publique OpenTopoData (une grille de points d'altitude).

Usage : python -I tools/citylayout/fetch_osm.py <dossier de sortie>
Les fichiers déjà présents ne sont pas retéléchargés (supprimer un fichier pour le rafraîchir).
"""
import json, os, sys, time, urllib.parse, urllib.request

out = sys.argv[1]
os.makedirs(out, exist_ok=True)

# Cadre réel (doit rester identique à simplify_osm.py) : de Port-Bouët (aéroport, côte) à Abobo (forêt du Banco).
LON_MIN, LON_MAX, LAT_MIN, LAT_MAX = -4.100, -3.895, 5.228, 5.445
BBOX = "%.4f,%.4f,%.4f,%.4f" % (LAT_MIN, LON_MIN, LAT_MAX, LON_MAX)
UA = {"User-Agent": "IvoryState-citylayout/1.0 (game map generator)"}
OVERPASS = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter"]

QUERIES = {
    "roads.json": 'way[highway~"^(motorway|trunk|primary|secondary|motorway_link|trunk_link|primary_link)$"](%s);out geom;',
    "tertiary.json": 'way[highway=tertiary](%s);out geom;',
    "water_full.json": '(way[natural=water](%s);relation[natural=water](%s);way[waterway=riverbank](%s);'
                       'relation[waterway=riverbank](%s);way[water~"^(lagoon|bay|canal|river)$"](%s);'
                       'relation[water~"^(lagoon|bay|canal|river)$"](%s);way[natural=bay](%s);relation[natural=bay](%s););out geom;',
    "coastline.json": 'way[natural=coastline](%s);out geom;',
    "landuse.json": '(way[landuse=forest](%s);relation[landuse=forest](%s);way[natural=wood](%s);relation[natural=wood](%s);'
                    'way[boundary=national_park](%s);relation[boundary=national_park](%s);'
                    'way[leisure=nature_reserve](%s);relation[leisure=nature_reserve](%s);'
                    'way[aeroway~"^(aerodrome|runway|taxiway|apron|terminal)$"](%s);'
                    'relation[aeroway=aerodrome](%s););out geom;',
    "places.json": '(nwr[leisure=stadium](%s);nwr[amenity=hospital](%s);nwr[amenity=place_of_worship][name](%s);'
                   'nwr[amenity~"^(townhall|police|marketplace|bus_station|theatre|courthouse)$"](%s);'
                   'nwr[office=government][name](%s);nwr[tourism~"^(hotel|attraction|museum)$"][name](%s);'
                   'nwr[historic][name](%s);nwr[landuse=military](%s);nwr[military][name](%s);nwr[man_made=lighthouse](%s);'
                   'nwr[aeroway=terminal](%s);nwr[building][name~"Pyramide|Tour|Palais|Cit"](%s);'
                   'node[place~"^(suburb|quarter|town|city|neighbourhood)$"](%s););out center tags;',
    # Feux tricolores réels : les carrefours du jeu qui en ont un près d'eux reçoivent des feux.
    "signals.json": 'node[highway=traffic_signals](%s);out;',
}


def overpass(query):
    data = urllib.parse.urlencode({"data": "[out:json][timeout:180];" + query}).encode()
    for attempt in range(5):
        try:
            req = urllib.request.Request(OVERPASS[attempt % len(OVERPASS)], data=data, headers=UA)
            with urllib.request.urlopen(req, timeout=240) as r:
                return r.read()
        except Exception as e:  # serveur chargé : on attend et on recommence
            print("  Overpass indisponible (%s), nouvel essai..." % e)
            time.sleep(20 * (attempt + 1))
    raise SystemExit("Overpass injoignable")


for name, q in QUERIES.items():
    path = os.path.join(out, name)
    if os.path.exists(path):
        print("déjà là :", name)
        continue
    print("OSM :", name)
    raw = overpass(q.replace("%s", BBOX))
    json.loads(raw)  # vérifie que c'est bien du JSON
    open(path, "wb").write(raw)
    print("  %d Ko" % (len(raw) // 1024))
    time.sleep(5)

# --- Relief : grille d'altitudes SRTM (pas de 0,0025°, environ 280 m réels, 35 m de jeu) ---
path = os.path.join(out, "elevation.json")
if not os.path.exists(path):
    STEP = 0.0025
    lons = [round(LON_MIN + i * STEP, 5) for i in range(int((LON_MAX - LON_MIN) / STEP) + 2)]
    lats = [round(LAT_MIN + j * STEP, 5) for j in range(int((LAT_MAX - LAT_MIN) / STEP) + 2)]
    pts = [(lat, lon) for lat in lats for lon in lons]
    heights = []
    print("Relief : %d points (%d x %d)" % (len(pts), len(lons), len(lats)))
    for k in range(0, len(pts), 100):
        chunk = pts[k:k + 100]
        url = "https://api.opentopodata.org/v1/srtm30m?locations=" + "|".join("%.5f,%.5f" % p for p in chunk)
        for attempt in range(6):
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
                    res = json.loads(r.read())
                if res.get("status") != "OK":
                    raise RuntimeError(res.get("error"))
                heights += [x["elevation"] if x["elevation"] is not None else 0.0 for x in res["results"]]
                break
            except Exception as e:
                print("  OpenTopoData : %s, nouvel essai..." % e)
                time.sleep(5 * (attempt + 1))
        else:
            raise SystemExit("OpenTopoData injoignable")
        time.sleep(1.1)
        if k % 1000 == 0:
            print("  %d / %d" % (k + len(chunk), len(pts)))
    json.dump({"source": "SRTM 30 m (NASA), via OpenTopoData", "lon0": LON_MIN, "lat0": LAT_MIN, "step": STEP,
               "nx": len(lons), "nz": len(lats), "h": heights}, open(path, "w"))
    print("Relief enregistré")
