"""Disposition de la ville du jeu d'après le plan simplifié d'Abidjan (abidjan_plan.json).

Entrée : tools/citylayout/abidjan_plan.json (grands axes, eau, océan, forêts, aéroport, lieux nommés et
altitudes, déjà en mètres du jeu, voir simplify_osm.py ; données OpenStreetMap sous licence ODbL, relief SRTM).
Sortie : IvoryState/Assets/_Project/Resources/CityLayout.json, lu par le générateur de ville (éditeur) et
par les catalogues du jeu, plus un aperçu SVG dans IvoryState/Builds.

Principe : les grands axes, la lagune, la côte, la forêt du Banco et l'aéroport viennent du vrai tracé.
Les monuments modélisés dans Blender puis les lieux particuliers (marchés, gares...) prennent leur
emplacement réel OpenStreetMap, ou le plus proche possible sans couper un grand axe (une petite rue
tertiaire qui les traverse s'arrête à leur bord). Chaque quartier reçoit ensuite des bâtiments le long de
ses rues. Le relief (SRTM) est lissé, limité en pente pour rester praticable en voiture, et aplani sous les
monuments et l'aéroport. Tout est déterministe.

Usage : python -I tools/citylayout/build_layout.py
"""
import json, math, os, re
from array import array

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT_JSON = os.path.join(ROOT, "IvoryState", "Assets", "_Project", "Resources", "CityLayout.json")
OUT_SVG = os.path.join(ROOT, "IvoryState", "Builds", "city_layout.svg")

plan = json.load(open(os.path.join(HERE, "abidjan_plan.json"), encoding="utf-8"))
SCALE = plan["scale"]
LON0, LAT0 = -4.0175, 5.3200
KX = 111320 * math.cos(math.radians(LAT0)) / SCALE
KZ = 110540 / SCALE


def geo(lon, lat):
    return ((lon - LON0) * KX, (lat - LAT0) * KZ)


# --- Cadre et trame de calcul ---
bx0, bz0, bx1, bz1 = plan["bounds"]
X0, Z0, X1, Z1 = bx0 + 10, bz0 + 10, bx1 - 10, bz1 - 10
RES = 2.0
NX, NZ = int((X1 - X0) / RES) + 1, int((Z1 - Z0) / RES) + 1
WATER, ROAD, CLEAR, TAKEN, SPECIAL, MAJOR, FOREST, AIRPORT, MCLEAR, PCLEAR = 1, 2, 4, 8, 16, 32, 64, 128, 256, 512
grid = array("H", bytes(2 * NX * NZ))


def cell(x, z):
    i, j = int((x - X0) / RES), int((z - Z0) / RES)
    if 0 <= i < NX and 0 <= j < NZ:
        return j * NX + i
    return -1


def flag(x, z, f):
    c = cell(x, z)
    return c < 0 or grid[c] & f != 0


def fill_rings(rings, f):
    """Remplissage pair-impair d'un groupe d'anneaux, ajouté (OU) aux cases."""
    edges = []
    for ring in rings:
        for k in range(len(ring)):
            a, b = ring[k], ring[(k + 1) % len(ring)]
            if a[1] != b[1]:
                edges.append((a[0], a[1], b[0], b[1]))
    if not edges:
        return
    zmin = min(min(e[1], e[3]) for e in edges)
    zmax = max(max(e[1], e[3]) for e in edges)
    for j in range(max(0, int((zmin - Z0) / RES) - 1), min(NZ, int((zmax - Z0) / RES) + 2)):
        z = Z0 + (j + 0.5) * RES
        xs = sorted(ax + (z - az) * (bx - ax) / (bz - az) for ax, az, bx, bz in edges if (az <= z) != (bz <= z))
        for k in range(0, len(xs) - 1, 2):
            i0 = max(0, int((xs[k] - X0) / RES))
            i1 = min(NX - 1, int((xs[k + 1] - X0) / RES))
            for i in range(i0, i1 + 1):
                grid[j * NX + i] |= f


# --- Eau (lagune et baies, puis océan), forêts, enceinte de l'aéroport ---
fill_rings(plan["water"], WATER)
fill_rings(plan.get("ocean", []), WATER)
for ring in plan.get("forest", []):
    fill_rings([ring], FOREST)
AIR = plan.get("airport", {})
for ring in AIR.get("fence", []):
    fill_rings([ring], AIRPORT)


# --- Petites eaux isolées (bassins, mares, restes du tracé) : retirées, sinon flaques et quais invisibles en ville ---
MIN_WATER = 2000.0  # m² de jeu (environ 13 ha réels)


def drop_small_water():
    n_cells = NX * NZ
    seen = bytearray(n_cells)
    min_cells = int(MIN_WATER / (RES * RES))
    dropped = cells = 0
    for start in range(n_cells):
        if seen[start] or not grid[start] & WATER:
            continue
        comp = [start]
        seen[start] = 1
        k = 0
        while k < len(comp):
            u = comp[k]
            k += 1
            i = u % NX
            for v in ((u - 1) if i > 0 else -1, (u + 1) if i < NX - 1 else -1, u - NX, u + NX):
                if 0 <= v < n_cells and not seen[v] and grid[v] & WATER:
                    seen[v] = 1
                    comp.append(v)
        # Un fossé ou un canal étroit (aucune case dont les 4 m alentour sont tous de l'eau) fait flaque aussi.
        core = 0
        if len(comp) >= min_cells:
            for u in comp:
                j, i = divmod(u, NX)
                if 2 <= i < NX - 2 and 2 <= j < NZ - 2 and all(grid[(j + b) * NX + i + a] & WATER for a in (-2, 0, 2) for b in (-2, 0, 2)):
                    core += 1
                    if core >= 40:
                        break
        if len(comp) < min_cells or core < 40:
            for u in comp:
                grid[u] &= 0xFFFF ^ WATER
            dropped += 1
            cells += len(comp)
    print("eaux isolées retirées :", dropped, "(%.0f m²)" % (cells * RES * RES))


drop_small_water()


# --- Réseau routier : vrais ronds-points, impasses raccordées, petits bouts pendants retirés ---
def seg_closest(px, pz, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    L2 = dx * dx + dz * dz
    t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - a[0]) * dx + (pz - a[1]) * dz) / L2))
    qx, qz = a[0] + t * dx, a[1] + t * dz
    return math.hypot(px - qx, pz - qz), qx, qz, t


def closest_on(px, pz, pts):
    """(distance, x, z, segment, t) du point de la polyligne le plus proche."""
    best = (1e18, 0.0, 0.0, 0, 0.0)
    for k in range(len(pts) - 1):
        d, qx, qz, t = seg_closest(px, pz, pts[k], pts[k + 1])
        if d < best[0]:
            best = (d, qx, qz, k, t)
    return best


def poly_len(pts):
    return sum(math.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1]) for k in range(len(pts) - 1))


def arc_at(pts, k, t):
    return sum(math.hypot(pts[q + 1][0] - pts[q][0], pts[q + 1][1] - pts[q][1]) for q in range(k)) + \
        t * math.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1])


def point_at(pts, s):
    for k in range(len(pts) - 1):
        L = math.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1])
        if s <= L or k == len(pts) - 2:
            t = 0.0 if L == 0 else max(0.0, min(1.0, s / L))
            return [pts[k][0] + (pts[k + 1][0] - pts[k][0]) * t, pts[k][1] + (pts[k + 1][1] - pts[k][1]) * t]
        s -= L
    return list(pts[-1])


def sub_poly(pts, s0, s1):
    """Morceau de polyligne entre les abscisses curvilignes s0 < s1."""
    out = [point_at(pts, s0)]
    acc = 0.0
    for k in range(len(pts) - 1):
        acc += math.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1])
        if s0 + 0.3 < acc < s1 - 0.3:
            out.append(list(pts[k + 1]))
    out.append(point_at(pts, s1))
    return [[round(x, 1), round(z, 1)] for x, z in out]


def bbox_r(r, pad):
    xs = [p[0] for p in r["points"]]
    zs = [p[1] for p in r["points"]]
    return (min(xs) - pad, min(zs) - pad, max(xs) + pad, max(zs) + pad)


def in_box(x, z, b):
    return b[0] <= x <= b[2] and b[1] <= z <= b[3]


def segments_cross(a0, a1, b0, b1):
    dax, daz = a1[0] - a0[0], a1[1] - a0[1]
    dbx, dbz = b1[0] - b0[0], b1[1] - b0[1]
    den = dax * dbz - daz * dbx
    if abs(den) < 1e-9:
        return None
    t = ((b0[0] - a0[0]) * dbz - (b0[1] - a0[1]) * dbx) / den
    u = ((b0[0] - a0[0]) * daz - (b0[1] - a0[1]) * dax) / den
    if 0.0 <= t <= 1.0 and 0.0 <= u <= 1.0:
        return a0[0] + t * dax, a0[1] + t * daz, t, u
    return None


def wet_line(ax, az, bx, bz):
    n = max(1, int(math.hypot(bx - ax, bz - az) / 2))
    return any(flag(ax + (bx - ax) * q / n, az + (bz - az) * q / n, WATER) for q in range(1, n))


ROADS = plan["roads"]
for r in ROADS:
    r["points"] = [list(p) for p in r["points"]]

# 1. Ronds-points : l'anneau est une route circulaire (8 m), les rues qui y arrivent s'arrêtent sur l'anneau ;
#    l'îlot central est un terre-plein planté. Seulement là où au moins trois rues se rejoignent.
roundabouts = []
for cx, cz, rad, rb_name in plan.get("roundabouts", []):
    if flag(cx, cz, WATER):
        continue
    R = max(9.0, min(14.0, rad))
    reach = max(rad, R) + 6.0
    if any(math.hypot(cx - o["x"], cz - o["z"]) < o["R"] + R + 12.0 for o in roundabouts):
        continue
    new_list, arms, touched, cls_min = [], 0, False, 3
    for r in ROADS:
        pts = r["points"]
        if closest_on(cx, cz, pts)[0] > reach:
            new_list.append(r)
            continue
        touched = True
        cls_min = min(cls_min, r["class"])
        # Découpe de la polyligne hors du disque de rayon R (abscisses curvilignes des intervalles extérieurs).
        L = poly_len(pts)
        cuts, acc, inside_now = [], 0.0, math.hypot(pts[0][0] - cx, pts[0][1] - cz) < R
        start = None if inside_now else 0.0
        for k in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[k], pts[k + 1]
            dx, dz = bx - ax, bz - az
            sl = math.hypot(dx, dz)
            if sl < 1e-6:
                continue
            fx, fz = ax - cx, az - cz
            qa, qb, qc = dx * dx + dz * dz, 2 * (fx * dx + fz * dz), fx * fx + fz * fz - R * R
            disc = qb * qb - 4 * qa * qc
            if disc > 0:
                for t in sorted(((-qb - math.sqrt(disc)) / (2 * qa), (-qb + math.sqrt(disc)) / (2 * qa))):
                    if 0.0 < t < 1.0:
                        s = acc + t * sl
                        if inside_now:
                            start = s
                        else:
                            cuts.append((start, s, True))
                            start = None
                        inside_now = not inside_now
            acc += sl
        if not inside_now:
            cuts.append((start, L, False))
        pieces = []
        for idx_, (s0, s1, ends_on_ring) in enumerate(cuts):
            starts_on_ring = s0 > 0.01
            if s1 - s0 < 6.0:
                continue
            piece_pts = sub_poly(pts, s0, s1)
            # Une extrémité libre proche de l'anneau (sans le toucher) est prolongée jusqu'à lui.
            for end in (0, -1):
                on_ring = starts_on_ring if end == 0 else ends_on_ring
                ex, ez = piece_pts[end]
                de = math.hypot(ex - cx, ez - cz)
                original_end = (end == 0 and s0 <= 0.01) or (end == -1 and s1 >= L - 0.01)
                if on_ring:
                    arms += 1
                elif original_end and de <= reach:
                    rp = [round(cx + (ex - cx) / de * R, 1), round(cz + (ez - cz) / de * R, 1)]
                    if end == 0:
                        piece_pts.insert(0, rp)
                    else:
                        piece_pts.append(rp)
                    arms += 1
            pieces.append(dict(r, points=piece_pts))
        new_list.extend(pieces)
    if not touched or arms < 3:
        continue
    ROADS[:] = new_list
    ring = [[round(cx + R * math.cos(2 * math.pi * q / 28), 1), round(cz + R * math.sin(2 * math.pi * q / 28), 1)] for q in range(29)]
    ROADS.append({"class": max(1, min(2, cls_min)), "name": rb_name or "Rond-point", "width": 8, "points": ring, "ring": True})
    roundabouts.append({"x": round(cx, 1), "z": round(cz, 1), "R": R, "r": round(R - 4.6, 1), "name": rb_name})
print("ronds-points construits :", len(roundabouts))


def touches(roads, i):
    """Abscisses curvilignes des raccords de la rue i (autres rues qui la touchent ou la croisent)."""
    r = roads[i]
    pts = r["points"]
    out = []
    b = bbox_r(r, 12.0)
    for j, q in enumerate(roads):
        if j == i:
            continue
        qp = q["points"]
        qb = bbox_r(q, 2.0)
        if qb[0] > b[2] or qb[2] < b[0] or qb[1] > b[3] or qb[3] < b[1]:
            continue
        for end in (0, -1):
            # Extrémité de l'autre rue posée sur celle-ci.
            d, _, _, k, t = closest_on(qp[end][0], qp[end][1], pts)
            if d <= r["width"] / 2 + 1.0:
                out.append(arc_at(pts, k, t))
            # Extrémité de celle-ci posée sur l'autre.
            d2 = closest_on(pts[end][0], pts[end][1], qp)[0]
            if d2 <= q["width"] / 2 + 1.0:
                out.append(0.0 if end == 0 else poly_len(pts))
        for k in range(len(pts) - 1):
            for m in range(len(qp) - 1):
                hit = segments_cross(pts[k], pts[k + 1], qp[m], qp[m + 1])
                if hit:
                    out.append(arc_at(pts, k, hit[2]))
    return sorted(out)


def near_edge(x, z):
    return x < bx0 + 40 or x > bx1 - 40 or z < bz0 + 40 or z > bz1 - 40


# 2. Impasses : raccordées à la rue la plus proche devant elles (60 m au plus, sans traverser l'eau).
joined = 0
for i, r in enumerate(ROADS):
    pts = r["points"]
    if len(pts) < 2:
        continue
    for end in (0, -1):
        ex, ez = pts[end]
        if near_edge(ex, ez):
            continue
        free = True
        best = None
        if end == 0:
            tx, tz = pts[0][0] - pts[1][0], pts[0][1] - pts[1][1]
        else:
            tx, tz = pts[-1][0] - pts[-2][0], pts[-1][1] - pts[-2][1]
        tl = math.hypot(tx, tz) or 1.0
        tx, tz = tx / tl, tz / tl
        for j, q in enumerate(ROADS):
            if j == i:
                continue
            qb = bbox_r(q, 42.0)
            if not in_box(ex, ez, qb):
                continue
            d, qx, qz, k, t = closest_on(ex, ez, q["points"])
            if d <= q["width"] / 2 + 1.0:
                free = False
                break
            if d > 60.0:
                continue
            ahead = ((qx - ex) * tx + (qz - ez) * tz) / max(d, 1e-6)
            if d > 15.0 and ahead < 0.45:
                continue
            if wet_line(ex, ez, qx, qz):
                continue
            score = d * (1.6 - ahead)
            if best is None or score < best[0]:
                best = (score, j, qx, qz, k, t)
        if not free or best is None:
            continue
        _, j, qx, qz, k, t = best
        q = ROADS[j]["points"]
        if 0.01 < t < 0.99:
            q.insert(k + 1, [round(qx, 1), round(qz, 1)])
        target = [round(qx, 1), round(qz, 1)]
        if end == 0:
            pts.insert(0, target)
        else:
            pts.append(target)
        joined += 1
print("impasses raccordées :", joined)


# 3. Bouts pendants : au-delà du dernier raccord, un tronçon de moins de 70 m est retiré ;
#    une rue sans aucun raccord de moins de 200 m disparaît.
def prune(roads, max_spur):
    kept, cut = [], 0
    for i, r in enumerate(roads):
        pts = r["points"]
        if r.get("ring"):
            kept.append(r)
            continue
        L = poly_len(pts)
        ts = touches(roads, i)
        if not ts:
            if L >= 200.0:
                kept.append(r)
            else:
                cut += 1
            continue
        s0, s1 = 0.0, L
        if not near_edge(*pts[0]) and ts[0] > 0.5 and ts[0] < max_spur:
            s0 = ts[0]
        if not near_edge(*pts[-1]) and L - ts[-1] > 0.5 and L - ts[-1] < max_spur:
            s1 = ts[-1]
        if s1 - s0 < 8.0:
            cut += 1
            continue
        if s0 > 0.0 or s1 < L:
            cut += 1
            r = dict(r, points=sub_poly(pts, s0, s1))
        kept.append(r)
    return kept, cut


ROADS[:], pruned = prune(ROADS, 70.0)
print("bouts pendants retirés :", pruned)
plan["roads"] = ROADS


# --- Grands axes : chaussée (ROAD), emprise interdite aux blocs (CLEAR, MCLEAR pour les grands axes) ---
def stamp_capsule(a, b, radius, f):
    ax, az = a
    bx, bz = b
    i0 = int((min(ax, bx) - radius - X0) / RES) - 1
    i1 = int((max(ax, bx) + radius - X0) / RES) + 1
    j0 = int((min(az, bz) - radius - Z0) / RES) - 1
    j1 = int((max(az, bz) + radius - Z0) / RES) + 1
    dx, dz = bx - ax, bz - az
    L2 = dx * dx + dz * dz or 1e-9
    r2 = radius * radius
    for j in range(max(0, j0), min(NZ, j1 + 1)):
        z = Z0 + (j + 0.5) * RES
        for i in range(max(0, i0), min(NX, i1 + 1)):
            x = X0 + (i + 0.5) * RES
            t = max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / L2))
            ex, ez = ax + t * dx - x, az + t * dz - z
            if ex * ex + ez * ez <= r2:
                grid[j * NX + i] |= f


for r in plan["roads"]:
    pts = r["points"]
    for k in range(len(pts) - 1):
        stamp_capsule(pts[k], pts[k + 1], r["width"] / 2, ROAD | (MAJOR if r["class"] <= 2 else 0))
        stamp_capsule(pts[k], pts[k + 1], r["width"] / 2 + 3.0,
                      CLEAR | (MCLEAR if r["class"] <= 2 else 0) | (PCLEAR if r["class"] <= 1 else 0))
# Îlots des ronds-points : ni bâtiment, ni arbre, ni trottoir.
for rb in roundabouts:
    stamp_capsule((rb["x"], rb["z"]), (rb["x"], rb["z"]), rb["R"], CLEAR | MCLEAR | TAKEN)

# --- Ponts : morceaux de route au-dessus de l'eau (au moins 8 m) ---
roads_out = []
for r in plan["roads"]:
    pts = r["points"]
    samples = []  # (x, z, eau ?)
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        n = max(1, int(math.hypot(bx - ax, bz - az) / 2))
        for s in range(n):
            t = s / n
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            samples.append((x, z, flag(x, z, WATER)))
    samples.append((pts[-1][0], pts[-1][1], flag(pts[-1][0], pts[-1][1], WATER)))
    # Les courts passages sur l'eau (berges mal simplifiées) restent de la route ordinaire.
    wet = [s[2] for s in samples]
    k = 0
    while k < len(wet):
        if wet[k]:
            e = k
            while e < len(wet) and wet[e]:
                e += 1
            if e - k < 4:
                for q in range(k, e):
                    wet[q] = False
            k = e
        else:
            k += 1
    spans = []
    k = 0
    while k < len(wet):
        e = k
        while e < len(wet) and wet[e] == wet[k]:
            e += 1
        spans.append({"bridge": wet[k], "from": k, "to": e})
        k = e
    bridges = [[round(samples[sp["from"]][0], 1), round(samples[sp["from"]][1], 1),
                round(samples[min(sp["to"], len(samples) - 1)][0], 1), round(samples[min(sp["to"], len(samples) - 1)][1], 1)]
               for sp in spans if sp["bridge"]]
    roads_out.append({"class": r["class"], "name": r["name"], "width": r["width"], "points": pts, "bridges": bridges,
                      "ring": bool(r.get("ring"))})

# --- Quartiers : centre (emplacement réel), rayon et orientation dominante des rues voisines ---
DISTRICTS = [
    ("Plateau", -4.0185, 5.3215, 330), ("Adjamé", -4.0250, 5.3550, 420), ("Cocody", -3.9850, 5.3480, 560),
    ("Yopougon", -4.0750, 5.3380, 520), ("Attécoubé", -4.0430, 5.3320, 330), ("Treichville", -4.0100, 5.3000, 360),
    ("Marcory", -3.9800, 5.3000, 380), ("Koumassi", -3.9550, 5.2950, 380),
    # Extension : Abobo au nord (au-delà de la forêt du Banco), la Riviera à l'est de Cocody, Port-Bouët
    # sur le cordon littoral (Vridi, aéroport, plage).
    ("Abobo", -4.0200, 5.4250, 470), ("Riviera", -3.9560, 5.3700, 430), ("Port-Bouët", -3.9580, 5.2520, 430),
]
# Lots le long des rues : largeur et profondeur des bâtiments, part de lots bâtis, deuxième rangée.
STYLE = {
    "Plateau": ((12, 20), (12, 18), 0.95, True), "Adjamé": ((8, 13), (10, 16), 0.95, True),
    "Cocody": ((12, 18), (12, 18), 0.62, False), "Yopougon": ((7, 11), (8, 13), 0.9, True),
    "Attécoubé": ((7, 10), (8, 12), 0.85, True), "Treichville": ((8, 13), (10, 15), 0.93, True),
    "Marcory": ((10, 16), (12, 18), 0.85, True), "Koumassi": ((8, 12), (9, 14), 0.88, True),
    "Abobo": ((7, 11), (8, 13), 0.88, True), "Riviera": ((12, 18), (12, 18), 0.58, False),
    "Port-Bouët": ((8, 12), (9, 14), 0.8, True),
}
districts = []
for name, lon, lat, radius in DISTRICTS:
    sx, sz = geo(lon, lat)
    c4 = s4 = 0.0
    for r in plan["roads"]:
        pts = r["points"]
        for k in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[k], pts[k + 1]
            d = math.hypot((ax + bx) / 2 - sx, (az + bz) / 2 - sz)
            if d > 380:
                continue
            a = math.atan2(bz - az, bx - ax)
            w = math.hypot(bx - ax, bz - az) * (4 - r["class"]) / (1 + d / 120)
            c4 += w * math.cos(4 * a)
            s4 += w * math.sin(4 * a)
    districts.append({"name": name, "x": round(sx, 1), "z": round(sz, 1), "radius": radius,
                      "yaw": round(math.degrees(math.atan2(s4, c4) / 4), 2)})


def district_at(x, z):
    """Quartier le plus proche (pondéré par son rayon) ; None au-delà : abords non bâtis."""
    best, bd = None, 1e18
    for d in districts:
        dd = math.hypot(x - d["x"], z - d["z"]) / d["radius"]
        if dd < bd:
            best, bd = d, dd
    return best if bd <= 1.0 else None


def nearest_district(x, z):
    return district_at(x, z) or min(districts, key=lambda q: math.hypot(x - q["x"], z - q["z"]))


def rect_ok(cx, cz, ca, sa, w, d, forbid):
    """Rectangle (largeur w le long de (ca, sa), profondeur d) libre de ces drapeaux, hors de l'eau."""
    nw, nd = max(2, int(w / RES) + 1), max(2, int(d / RES) + 1)
    # Le centre et les coins d'abord : la plupart des refus tombent tout de suite.
    for u, v in ((0, 0), (-w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2), (w / 2, -d / 2)):
        c = cell(cx + u * ca - v * sa, cz + u * sa + v * ca)
        if c < 0 or grid[c] & (forbid | WATER):
            return False
    for a in range(nw + 1):
        u = -w / 2 + w * a / nw
        for b in range(nd + 1):
            v = -d / 2 + d * b / nd
            c = cell(cx + u * ca - v * sa, cz + u * sa + v * ca)
            if c < 0 or grid[c] & (forbid | WATER):
                return False
    return True


def take_rect(cx, cz, ca, sa, w, d, f=TAKEN):
    nw, nd = max(2, int(w / RES) * 2), max(2, int(d / RES) * 2)
    for a in range(nw + 1):
        u = -w / 2 + w * a / nw
        for b in range(nd + 1):
            v = -d / 2 + d * b / nd
            c = cell(cx + u * ca - v * sa, cz + u * sa + v * ca)
            if c >= 0:
                grid[c] |= f


class Rng:
    """Générateur déterministe simple (indépendant de la version de Python)."""
    def __init__(self, seed):
        self.s = seed & 0xFFFFFFFF

    def next(self):
        self.s = (1103515245 * self.s + 12345) & 0x7FFFFFFF
        return self.s / 0x7FFFFFFF

    def range(self, a, b):
        return a + (b - a) * self.next()


rng = Rng(20261118)
SIDEWALK = 3.0
blocks, buildings, anchors, landmarks, parkings = [], [], [], [], []
lots = []  # (x, z, yaw, w, d, coupe les rues secondaires ?) : emprises réservées (monuments, lieux particuliers)

# --- Emplacements réels : lieux nommés OpenStreetMap (le plus proche du repère approximatif) ---
PLACES = plan.get("places", [])


def real(regex, lon, lat, max_dist=260.0):
    fx, fz = geo(lon, lat)
    if regex:
        pat = re.compile(regex)
        cands = [(math.hypot(p[2] - fx, p[3] - fz), p) for p in PLACES if pat.search(p[0])]
        cands = [c for c in cands if c[0] <= max_dist]
        if cands:
            _, p = min(cands, key=lambda c: c[0])
            return p[2], p[3], p[0]
    return fx, fz, ""


def search_lot(tx, tz, w, d, yaw0, forbid, reach, step=4):
    """Rectangle w x d libre le plus proche de (tx, tz) : spirale, deux orientations de la trame du quartier."""
    for radius in range(0, reach, step):
        steps = max(1, int(2 * math.pi * radius / step))
        for s in range(steps):
            a = 2 * math.pi * s / steps
            cx, cz = tx + radius * math.cos(a), tz + radius * math.sin(a)
            for yaw in (yaw0, yaw0 + 90.0):
                ya = math.radians(yaw)
                if rect_ok(cx, cz, math.cos(ya), math.sin(ya), w, d, forbid):
                    return cx, cz, yaw
    return None


def nearest_road_dir(cx, cz, max_cls=3):
    best, bdir = 1e18, (0.0, 1.0)
    for r in roads_out:
        if r["class"] > max_cls:
            continue
        pts = r["points"]
        for k in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[k], pts[k + 1]
            dx, dz = bx - ax, bz - az
            L2 = dx * dx + dz * dz or 1e-9
            t = max(0.0, min(1.0, ((cx - ax) * dx + (cz - az) * dz) / L2))
            qx, qz = ax + t * dx - cx, az + t * dz - cz
            dd = qx * qx + qz * qz
            if dd < best:
                best, bdir = dd, (qx, qz)
    n = math.hypot(*bdir) or 1.0
    return bdir[0] / n, bdir[1] / n


# --- Monuments modélisés dans Blender (taille réelle, lots des manifestes tools/blender/architecture) ---
# (clé du prefab, nom, motif OSM, longitude et latitude approximatives, largeur, profondeur)
LANDMARKS = [
    ("PF_Stade_FHB", "Stade Félix Houphouët-Boigny", r"^Stade F[ée]lix", -4.0190, 5.3235, 160, 200),
    ("PF_Base_Militaire_Yopougon", "Camp militaire de Yopougon", r"^Camp militaire$", -4.0647, 5.3216, 220, 240),
    ("PF_Palais_Presidentiel", "Palais présidentiel", r"Palais [Pp]r[ée]sidentiel|Pr[ée]sidence de la R", -4.0180, 5.3195, 100, 92),
    ("PF_Hopital_CHU", "CHU de Cocody", r"CHU de Cocody|Hospitalier Universitaire de Cocody", -3.9930, 5.3460, 74, 66),
    ("PF_Cathedrale_Saint_Paul", "Cathédrale Saint-Paul", r"Cath[ée]drale Saint[- ]Paul", -4.0240, 5.3285, 64, 72),
    ("PF_Grande_Mosquee", "Grande Mosquée du Plateau", r"Grande Mosqu[ée]e du Plateau|Mosqu[ée]e du Plateau", -4.0170, 5.3265, 68, 74),
    ("PF_Mairie_Plateau", "Mairie du Plateau", r"Mairie du Plateau|Mairie de la commune du Plateau", -4.0190, 5.3245, 66, 58),
    ("PF_Commissariat_Central", "Préfecture de police", r"Pr[ée]fecture de [Pp]olice|Commissariat [Cc]entral", -4.0230, 5.3260, 60, 56),
    ("PF_Tour_F", "Cité administrative (Tour F)", r"^Tour F$", -4.0222, 5.3356, 64, 64),
    ("PF_Duplex_Cocody_01", "Duplex de Cocody", None, -3.9846, 5.3470, 30, 36),
]
LOT_FORBID = MCLEAR | TAKEN | AIRPORT | FOREST | SPECIAL
PARKING_FREE = ("PF_Duplex_Cocody_01", "PF_Base_Militaire_Yopougon")  # parkings intérieurs déjà modélisés
MONUMENT_FORBID = PCLEAR | TAKEN | AIRPORT | FOREST | SPECIAL
for key, name, regex, lon, lat, w, d in sorted(LANDMARKS, key=lambda m: m[5] * m[6]):
    tx, tz, osm = real(regex, lon, lat)
    dist = nearest_district(tx, tz)
    found = search_lot(tx, tz, w + 4, d + 4, dist["yaw"], MONUMENT_FORBID, 700)
    if not found:
        print("  monument introuvable :", name)
        continue
    cx, cz, yaw = found
    ya = math.radians(yaw)
    ca, sa = math.cos(ya), math.sin(ya)
    # La façade (+profondeur) regarde la rue la plus proche.
    rx, rz = nearest_road_dir(cx, cz)
    vx, vz = -sa, ca
    if vx * rx + vz * rz < 0:
        vx, vz = -vx, -vz
    take_rect(cx, cz, ca, sa, w + 6, d + 6, TAKEN | SPECIAL)
    lots.append((cx, cz, yaw, w + 2, d + 2, True))
    # Parking extérieur des bâtiments utilisables : sur un côté libre du lot (ni rue, ni eau), au même niveau.
    if key not in PARKING_FREE:
        best = None
        for sign_u, sign_v in ((-1, 0), (1, 0), (0, -1)):
            if sign_v:
                pw, pd = min(w, 36.0), 16.0
                px, pz = cx + vx * sign_v * (d / 2 + 5 + pd / 2), cz + vz * sign_v * (d / 2 + 5 + pd / 2)
                pa = yaw
            else:
                pw, pd = min(d, 36.0), 16.0
                px, pz = cx + ca * sign_u * (w / 2 + 5 + pd / 2), cz + sa * sign_u * (w / 2 + 5 + pd / 2)
                pa = yaw + 90.0
            # Au plus près de ce côté du lot (petite rue tertiaire coupée si besoin, jamais un grand axe).
            spot = search_lot(px, pz, pw + 2, pd + 2, pa, MCLEAR | TAKEN | AIRPORT | FOREST | SPECIAL, 70)
            if spot:
                dist_ = math.hypot(spot[0] - cx, spot[1] - cz)
                if best is None or dist_ < best[0]:
                    best = (dist_, spot[0], spot[1], spot[2], pw, pd)
        if best:
            _, px, pz, pa, pw, pd = best
            pr = math.radians(pa)
            take_rect(px, pz, math.cos(pr), math.sin(pr), pw + 2, pd + 2, TAKEN | SPECIAL)
            lots.append((px, pz, pa, pw, pd, False))
            parkings.append({"key": key, "name": "Parking — " + name, "x": round(px, 1), "z": round(pz, 1),
                             "yaw": round(pa, 2), "w": pw, "d": pd})
        else:
            print("  pas de place pour le parking :", name)
    landmarks.append({"key": key, "name": name, "osm": osm, "district": dist["name"], "x": round(cx, 1), "z": round(cz, 1),
                      "yaw": round(yaw, 2), "w": w, "d": d, "fx": round(vx, 4), "fz": round(vz, 4),
                      "offset": round(math.hypot(cx - tx, cz - tz), 1)})

# --- Lieux particuliers : une grande dalle de 64 m au plus près du vrai emplacement ---
# (Cathédrale, Grande Mosquée et stade FHB sont des monuments Blender, ci-dessus.)
SPECIALS = [
    ("Plaza", "Place du Plateau", None, -4.0195, 5.3205),
    ("Pyramide", "La Pyramide", r"Pyramide", -4.0170, 5.3240),
    ("GrandMarche", "Grand Marché d'Adjamé", r"[Mm]arch[ée] d'Adjam|Forum des [Mm]arch", -4.0230, 5.3530),
    ("GareRoutiere", "Gare routière d'Adjamé", r"Gare [Rr]outi[èe]re d'Adjam|Gare UTB|Gare d'Adjam", -4.0300, 5.3580),
    ("Mosquee", "Mosquée d'Adjamé", r"Mosqu[ée]e.*Adjam", -4.0200, 5.3600),
    ("HotelIvoire", "Hôtel Ivoire", r"H[ôo]tel Ivoire", -3.9975, 5.3330),
    ("ParcBasket", "Jardin de Cocody", None, -3.9850, 5.3550),
    ("Gare", "Gare de Cocody", None, -3.9750, 5.3600),
    ("Foot", "Terrain de Yopougon", None, -4.0700, 5.3300),
    ("Basket", "Playground de Yopougon", None, -4.0800, 5.3450),
    ("Gare", "Gare de Yopougon", r"Gare de Yopougon|Gare.*Siporex", -4.0650, 5.3500),
    ("Eglise", "Église Saint-André de Yopougon", r"Saint[- ]Andr[ée]", -4.0850, 5.3350),
    ("PalaisCulture", "Palais de la Culture", r"Palais de la Culture", -4.0150, 5.3050),
    ("MarcheCouvert", "Grand Marché de Treichville", r"March[ée] de Treichville", -4.0136, 5.3089),
    ("Foot", "Palais des Sports de Treichville", r"^Palais des Sports$", -4.0055, 5.2981),
    ("Mosquee", "Mosquée de Treichville", r"Mosqu[ée]e.*Treichville", -4.0130, 5.2930),
    ("Gare", "Gare Sud (Treichville)", r"Gare[- ][Ss]ud", -4.0185, 5.3147),
    ("ParcBasket", "Parc de Marcory", None, -3.9800, 5.3030),
    ("Gare", "Gare de Marcory", r"^Gare Marcory$", -3.9839, 5.3089),
    ("Eglise", "Paroisse Sainte-Thérèse de Marcory", r"Sainte[- ]Th[ée]r[èe]se", -3.9750, 5.2920),
    # Abobo
    ("Gare", "Abobo Gare", None, -4.0200, 5.4270),
    ("GrandMarche", "Grand marché d'Abobo", r"[Mm]arch[ée] de [Gg]ros d'Abobo", -4.0101, 5.4177),
    ("Mosquee", "Grande Mosquée Salam d'Abobo", r"Mosqu[ée]e.*Abobo", -4.0201, 5.4219),
    ("Foot", "Stade d'Abobo", r"Stade.*Abobo", -4.0140, 5.4290),
    ("Eglise", "Paroisse d'Abobo", r"(Paroisse|[ÉE]glise).*Abobo", -4.0270, 5.4140),
    # Riviera
    ("ParcBasket", "Parc de la Riviera", None, -3.9600, 5.3650),
    ("Gare", "Gare de la Riviera", r"Gare.*Riviera", -3.9500, 5.3720),
    # Port-Bouët
    ("Gare", "Gare de Port-Bouët", None, -3.9700, 5.2560),
    ("MarcheCouvert", "Marché de Port-Bouët", r"^March[ée] de Port[- ]Bou", -3.9637, 5.2580),
    ("Mosquee", "Mosquée de Port-Bouët", r"Mosqu[ée]e.*Port[- ]Bou", -3.9610, 5.2490),
    ("Foot", "Terrain de Vridi", r"Vridi", -3.9950, 5.2560),
]
BIG = 64.0
for kind, name, regex, lon, lat in SPECIALS:
    tx, tz, osm = real(regex, lon, lat)
    d = nearest_district(tx, tz)
    found = search_lot(tx, tz, BIG + 2, BIG + 2, d["yaw"], LOT_FORBID, 360)
    if not found:
        print("  introuvable :", name)
        continue
    ya = math.radians(found[2])
    take_rect(found[0], found[1], math.cos(ya), math.sin(ya), BIG + 4, BIG + 4, TAKEN | SPECIAL)
    lots.append((found[0], found[1], found[2], BIG, BIG, False))
    blocks.append({"x": round(found[0], 1), "z": round(found[1], 1), "yaw": round(found[2], 2), "size": BIG,
                   "district": d["name"], "special": kind, "name": name, "osm": osm,
                   "offset": round(math.hypot(found[0] - tx, found[1] - tz), 1)})

# --- Si une petite rue tertiaire traverse un monument ou un lieu particulier, elle s'arrête à son bord ---


def in_lot(x, z, cls):
    for lx, lz, lyaw, lw, ld, cut2 in lots:
        if cls == 2 and not cut2:
            continue
        ya = math.radians(lyaw)
        dx, dz = x - lx, z - lz
        u, v = dx * math.cos(ya) + dz * math.sin(ya), -dx * math.sin(ya) + dz * math.cos(ya)
        if abs(u) <= lw / 2 - 0.5 and abs(v) <= ld / 2 - 0.5:
            return True
    return False


def piece(r, run):
    """Morceau de rue gardé : ses ponts restent ceux qui tombent sur lui."""
    pts = [run[0]] + run[3:-1:3] + [run[-1]]
    keep = [br for br in r["bridges"] if any(math.hypot(br[0] - x, br[1] - z) < 2.5 for x, z in run)
            and any(math.hypot(br[2] - x, br[3] - z) < 2.5 for x, z in run)]
    return dict(r, points=pts, bridges=keep)


clipped = []
for r in roads_out:
    if r["class"] <= 1 or r.get("ring"):
        clipped.append(r)
        continue
    pts = r["points"]
    samples = []
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        n = max(1, int(math.hypot(bx - ax, bz - az) / 2))
        for q in range(n):
            samples.append((ax + (bx - ax) * q / n, az + (bz - az) * q / n))
    samples.append(tuple(pts[-1]))
    inside = [in_lot(x, z, r["class"]) for x, z in samples]
    if not any(inside):
        clipped.append(r)
        continue
    run = []
    for (x, z), hit in zip(samples, inside):
        if not hit:
            run.append([round(x, 1), round(z, 1)])
        elif run:
            if len(run) >= 9:
                clipped.append(piece(r, run))
            run = []
    if len(run) >= 9:
        clipped.append(piece(r, run))
roads_out = clipped


# --- Carrefours (après toutes les retouches des rues) : branches, feux tricolores, impasses ---
roads_out, pruned2 = prune(roads_out, 40.0)
SIGNALS = plan.get("signals", [])


def on_bridge(r, x, z):
    for br in r["bridges"]:
        if seg_closest(x, z, (br[0], br[1]), (br[2], br[3]))[0] <= r["width"] / 2 + 2.0:
            return True
    return False


def find_junctions(roads):
    found = []  # [x, z, {indices}]

    def add(x, z, members):
        for jn in found:
            if math.hypot(jn[0] - x, jn[1] - z) < 7.0:
                jn[2].update(members)
                return
        found.append([x, z, set(members)])

    boxes = [bbox_r(r, 2.0) for r in roads]
    for i, r in enumerate(roads):
        pts = r["points"]
        for j in range(len(roads)):
            if j == i:
                continue
            bi, bj = boxes[i], boxes[j]
            if bj[0] > bi[2] or bj[2] < bi[0] or bj[1] > bi[3] or bj[3] < bi[1]:
                continue
            q = roads[j]
            for end in (0, -1):
                d, qx, qz, _, _ = closest_on(pts[end][0], pts[end][1], q["points"])
                if d <= q["width"] / 2 + 1.0 and not on_bridge(q, qx, qz) and not on_bridge(r, pts[end][0], pts[end][1]):
                    add(qx, qz, (i, j))
            if j < i:
                continue
            qp = q["points"]
            for k in range(len(pts) - 1):
                for m in range(len(qp) - 1):
                    hit = segments_cross(pts[k], pts[k + 1], qp[m], qp[m + 1])
                    if hit and not on_bridge(r, hit[0], hit[1]) and not on_bridge(q, hit[0], hit[1]) and not flag(hit[0], hit[1], WATER):
                        add(hit[0], hit[1], (i, j))
    out = []
    for x, z, members in found:
        arms = []
        for i in members:
            r = roads[i]
            pts = r["points"]
            d, _, _, k, t = closest_on(x, z, pts)
            if d > r["width"] / 2 + 3.0:
                continue
            s = arc_at(pts, k, t)
            L = poly_len(pts)
            for target in ((s - 8.0) if s > 3.0 else None, (s + 8.0) if L - s > 3.0 else None):
                if target is None:
                    continue
                px, pz = point_at(pts, max(0.0, min(L, target)))
                dx, dz = px - x, pz - z
                n = math.hypot(dx, dz)
                if n < 1.0:
                    continue
                dx, dz = dx / n, dz / n
                same = next((a for a in arms if a[0] * dx + a[1] * dz > 0.94), None)
                if same:
                    if r["width"] > same[2]:
                        same[2], same[3] = r["width"], r["class"]
                    continue
                arms.append([dx, dz, r["width"], r["class"], bool(r.get("ring"))])
        if len(arms) < 3:
            continue
        ring = any(roads[i].get("ring") for i in members)
        radius = max(a[2] for a in arms) / 2 + 1.5
        near_signal = any(math.hypot(sx - x, sz - z) <= 16.0 for sx, sz in SIGNALS)
        majors = sum(1 for a in arms if a[3] <= 1)
        signal = (not ring) and any(a[3] <= 2 for a in arms) and (near_signal if SIGNALS else majors >= 2)
        out.append({"x": round(x, 1), "z": round(z, 1), "r": round(radius, 1), "s": 1 if signal else 0, "ring": 1 if ring else 0,
                    "a": [round(v, 3) for a in arms for v in (a[0], a[1], a[2], a[3])], "members": members})
    return out


junctions = find_junctions(roads_out)
# Carrefours rapprochés (chaussées doubles, bretelles) : un seul jeu de feux, au plus grand.
lit = []
for jn in sorted(junctions, key=lambda j: (-len(j["a"]), -max(j["a"][2::4]))):
    if jn["s"] and any(math.hypot(o["x"] - jn["x"], o["z"] - jn["z"]) < 24.0 for o in lit):
        jn["s"] = 0
    elif jn["s"]:
        lit.append(jn)
for r in roads_out:
    r["de"] = [1, 1]
for jn in junctions:
    for i in jn["members"]:
        pts = roads_out[i]["points"]
        for e, end in enumerate((0, -1)):
            if math.hypot(pts[end][0] - jn["x"], pts[end][1] - jn["z"]) <= roads_out[i]["width"] / 2 + jn["r"] + 2.0:
                roads_out[i]["de"][e] = 0
for i, r in enumerate(roads_out):
    pts = r["points"]
    for e, end in enumerate((0, -1)):
        if r.get("ring") or near_edge(*pts[end]):
            r["de"][e] = 0
        elif r["de"][e]:
            # Bout posé sur une autre rue sans former un vrai carrefour (raccord en continuité) : pas une impasse.
            for j, q in enumerate(roads_out):
                if j != i and closest_on(pts[end][0], pts[end][1], q["points"])[0] <= q["width"] / 2 + 1.0:
                    r["de"][e] = 0
                    break
for jn in junctions:
    del jn["members"]
print("carrefours : %d dont %d à feux ; impasses : %d ; bouts retirés : %d" % (
    len(junctions), sum(j["s"] for j in junctions), sum(sum(r["de"]) for r in roads_out), pruned2))

# --- Bâtiments le long des rues (les grands axes d'abord), une ou deux rangées ---
BUILD_FORBID = CLEAR | TAKEN | FOREST | AIRPORT
for r in sorted(roads_out, key=lambda r: r["class"]):
    pts = r["points"]
    front = r["width"] / 2 + SIDEWALK + 0.4
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 10:
            continue
        ca, sa = (bx - ax) / L, (bz - az) / L
        for side in (1, -1):
            nx, nz = -sa * side, ca * side
            s = 3.0
            while s < L - 3.0:
                px, pz = ax + ca * s, az + sa * s
                d = district_at(px + nx * 15, pz + nz * 15)
                if d is None:
                    s += 12.0
                    continue
                (w0, w1), (d0, d1), coverage, second = STYLE[d["name"]]
                w = rng.range(w0, w1)
                if s + w > L - 2.0:
                    break
                depth = rng.range(d0, d1)
                cx, cz = px + ca * w / 2 + nx * (front + depth / 2), pz + sa * w / 2 + nz * (front + depth / 2)
                built = rng.next() < coverage and rect_ok(cx, cz, ca, sa, w - 2.6, depth - 2.2, BUILD_FORBID)
                if built:
                    take_rect(cx, cz, ca, sa, w - 0.6, depth)
                    yaw = round(math.degrees(math.atan2(sa, ca)), 2)
                    buildings.append({"x": round(cx, 1), "z": round(cz, 1), "yaw": yaw, "w": round(w - 0.6, 1),
                                      "d": round(depth, 1), "r": round(rng.next(), 3), "district": d["name"],
                                      "fx": round(nx, 3), "fz": round(nz, 3), "row": 1})
                    # Repère sur le trottoir devant la façade (points d'interaction, étals, lampadaires).
                    anchors.append({"x": round(px + ca * w / 2 + nx * (r["width"] / 2 + SIDEWALK * 0.5), 1),
                                    "z": round(pz + sa * w / 2 + nz * (r["width"] / 2 + SIDEWALK * 0.5), 1),
                                    "fx": round(-nx, 3), "fz": round(-nz, 3), "district": d["name"], "road": r["class"]})
                    if second:
                        depth2 = rng.range(d0, d1)
                        off = front + depth + 2.0 + depth2 / 2
                        c2x, c2z = px + ca * w / 2 + nx * off, pz + sa * w / 2 + nz * off
                        if rng.next() < coverage and rect_ok(c2x, c2z, ca, sa, w - 2.6, depth2 - 2.2, BUILD_FORBID):
                            take_rect(c2x, c2z, ca, sa, w - 0.6, depth2)
                            buildings.append({"x": round(c2x, 1), "z": round(c2z, 1), "yaw": yaw, "w": round(w - 0.6, 1),
                                              "d": round(depth2, 1), "r": round(rng.next(), 3), "district": d["name"],
                                              "fx": round(nx, 3), "fz": round(nz, 3), "row": 2})
                s += w


# --- Trottoirs : bandes de 3 m de chaque côté des rues, coupées aux carrefours, aux ponts et à l'eau ---
def dist_to_road(px, pz, pts):
    best = 1e18
    for k in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[k], pts[k + 1]
        dx, dz = bx - ax, bz - az
        L2 = dx * dx + dz * dz
        t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / L2))
        d = math.hypot(px - (ax + t * dx), pz - (az + t * dz))
        if d < best:
            best = d
    return best


road_bboxes = []
for r in roads_out:
    xs = [p[0] for p in r["points"]]
    zs = [p[1] for p in r["points"]]
    pad = r["width"] / 2 + SIDEWALK / 2 + 1.0
    road_bboxes.append((min(xs) - pad, min(zs) - pad, max(xs) + pad, max(zs) + pad, r["width"] / 2 + SIDEWALK / 2 + 0.55, r["points"]))


def sidewalk_clear(px, pz, self_idx, self_pts, self_min_d):
    c = cell(px, pz)
    if c < 0 or grid[c] & (WATER | SPECIAL | AIRPORT):
        return False
    if dist_to_road(px, pz, self_pts) < self_min_d:
        return False
    for j, (bx0_, bz0_, bx1_, bz1_, req_d, rpts) in enumerate(road_bboxes):
        if j == self_idx or px < bx0_ or px > bx1_ or pz < bz0_ or pz > bz1_:
            continue
        if dist_to_road(px, pz, rpts) < req_d:
            return False
    return True


sidewalks = []
for r_idx, r in enumerate(roads_out):
    pts = r["points"]
    off = r["width"] / 2 + SIDEWALK / 2
    self_min = off - 0.25
    # Pas de trottoir le long des voies rapides hors des quartiers (autoroute du Nord, VGE...).
    # Anneau d'un rond-point (tracé dans le sens trigonométrique) : trottoir côté extérieur seulement.
    for side in ((-1,) if r.get("ring") else (1, -1)):
        run = []
        for k in range(len(pts) - 1):
            (ax, az), (bx, bz) = pts[k], pts[k + 1]
            L = math.hypot(bx - ax, bz - az)
            if L < 0.5:
                continue
            ca, sa = (bx - ax) / L, (bz - az) / L
            nx, nz = -sa * side, ca * side
            n = max(1, int(L / 2))
            for q in range(n + 1):
                t = L * q / n
                x, z = ax + ca * t + nx * off, az + sa * t + nz * off
                ok = sidewalk_clear(x, z, r_idx, pts, self_min) and not (r["class"] == 0 and district_at(x, z) is None)
                if ok:
                    run.append((round(x, 1), round(z, 1)))
                elif run:
                    if len(run) >= 3:
                        sidewalks.append(run)
                    run = []
        if len(run) >= 3:
            sidewalks.append(run)


def thin(run, tol=0.22):
    """Simplification Douglas-Peucker fine : garde les courbes exactes sans couper sur la chaussée."""
    if len(run) < 3:
        return run
    (ax, az), (bx, bz) = run[0], run[-1]
    dx, dz = bx - ax, bz - az
    n = math.hypot(dx, dz) or 1e-9
    best, idx = -1.0, 0
    for i in range(1, len(run) - 1):
        px, pz = run[i]
        d = abs(dz * (px - ax) - dx * (pz - az)) / n
        if d > best:
            best, idx = d, i
    if best <= tol:
        return [run[0], run[-1]]
    return thin(run[:idx + 1], tol)[:-1] + thin(run[idx:], tol)


sidewalks = [{"points": thin(s)} for s in sidewalks]

# --- Arbres des forêts (Banco, forêts classées) : trame de 11 m légèrement décalée ---
trees = []
TREE_STEP = 11.0
tz_ = Z0 + TREE_STEP / 2
row = 0
while tz_ < Z1:
    tx_ = X0 + TREE_STEP / 2 + (TREE_STEP / 2 if row % 2 else 0)
    while tx_ < X1:
        x, z = tx_ + rng.range(-3.5, 3.5), tz_ + rng.range(-3.5, 3.5)
        c = cell(x, z)
        if c >= 0 and grid[c] & FOREST and not grid[c] & (WATER | CLEAR | ROAD | TAKEN | AIRPORT):
            trees.append(round(x, 1))
            trees.append(round(z, 1))
        tx_ += TREE_STEP
    tz_ += TREE_STEP
    row += 1


# --- Masques (cases de 4 m pour l'eau, 8 m pour la forêt), lignes codées en plages ---
def mask_runs(res, test):
    nx_, nz_ = int((X1 - X0) / res), int((Z1 - Z0) / res)
    out = []
    for j in range(nz_):
        z = Z0 + (j + 0.5) * res
        start = None
        for i in range(nx_ + 1):
            on = i < nx_ and test(X0 + (i + 0.5) * res, z)
            if on and start is None:
                start = i
            elif not on and start is not None:
                out += [j, start, i - start]
                start = None
    return {"x": round(X0, 1), "z": round(Z0, 1), "cell": res, "nx": nx_, "nz": nz_, "runs": out}


water_mask = mask_runs(4.0, lambda x, z: flag(x, z, WATER) and not flag(x, z, ROAD))
forest_mask = mask_runs(8.0, lambda x, z: flag(x, z, FOREST) and not flag(x, z, WATER))

# --- Relief : altitudes SRTM lissées, réduites (x VSCALE), pente limitée, aplanies sous les monuments ---
ELEV = plan["elevation"]
VSCALE = 0.2          # 1/8 (comme l'horizontale) x 1,6 : les collines restent lisibles
SEA = 4.0             # altitude SRTM de la lagune (m) ramenée à 0
GRADE = 0.12          # pente maximale (12 %) : praticable en voiture et à pied
HCELL = 8.0
enx, enz = ELEV["nx"], ELEV["nz"]
eh = ELEV["h"]
# Médiane 3 x 3 : retire les pics isolés (tours du Plateau, grands arbres).
med = [0.0] * (enx * enz)
for j in range(enz):
    for i in range(enx):
        vals = sorted(eh[jj * enx + ii] for jj in range(max(0, j - 1), min(enz, j + 2)) for ii in range(max(0, i - 1), min(enx, i + 2)))
        med[j * enx + i] = vals[len(vals) // 2]


def elevation(x, z):
    fx = min(max((x - ELEV["x0"]) / ELEV["dx"], 0.0), enx - 1.001)
    fz = min(max((z - ELEV["z0"]) / ELEV["dz"], 0.0), enz - 1.001)
    i, j = int(fx), int(fz)
    tx, tz = fx - i, fz - j
    a, b = med[j * enx + i], med[j * enx + i + 1]
    c, d = med[(j + 1) * enx + i], med[(j + 1) * enx + i + 1]
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz


hnx, hnz = int((X1 - X0) / HCELL) + 2, int((Z1 - Z0) / HCELL) + 2
H = [0.0] * (hnx * hnz)
for j in range(hnz):
    for i in range(hnx):
        H[j * hnx + i] = max(0.0, elevation(X0 + i * HCELL, Z0 + j * HCELL) - SEA) * VSCALE


def box_blur(vals, radius):
    tmp = [0.0] * len(vals)
    for j in range(hnz):
        row_ = j * hnx
        for i in range(hnx):
            i0, i1 = max(0, i - radius), min(hnx - 1, i + radius)
            tmp[row_ + i] = sum(vals[row_ + i0:row_ + i1 + 1]) / (i1 - i0 + 1)
    out = [0.0] * len(vals)
    for j in range(hnz):
        j0, j1 = max(0, j - radius), min(hnz - 1, j + radius)
        for i in range(hnx):
            out[j * hnx + i] = sum(tmp[jj * hnx + i] for jj in range(j0, j1 + 1)) / (j1 - j0 + 1)
    return out


H = box_blur(box_blur(H, 2), 2)
fixed = bytearray(hnx * hnz)
# Eau : 0 (le fond de la lagune est creusé par le générateur d'après le masque).
for j in range(hnz):
    for i in range(hnx):
        if flag(X0 + i * HCELL, Z0 + j * HCELL, WATER) and not flag(X0 + i * HCELL, Z0 + j * HCELL, ROAD):
            H[j * hnx + i] = 0.0
            fixed[j * hnx + i] = 1


def flatten(cells):
    if not cells:
        return
    mean = sum(H[c] for c in cells) / len(cells)
    for c in cells:
        H[c] = mean
        fixed[c] = 1


def rect_cells(cx, cz, yaw, w, d, margin=4.0):
    ya = math.radians(yaw)
    ca, sa = math.cos(ya), math.sin(ya)
    r = math.hypot(w, d) / 2 + margin
    out = []
    for j in range(max(0, int((cz - r - Z0) / HCELL)), min(hnz, int((cz + r - Z0) / HCELL) + 2)):
        for i in range(max(0, int((cx - r - X0) / HCELL)), min(hnx, int((cx + r - X0) / HCELL) + 2)):
            dx, dz = X0 + i * HCELL - cx, Z0 + j * HCELL - cz
            u, v = dx * ca + dz * sa, -dx * sa + dz * ca
            if abs(u) <= w / 2 + margin and abs(v) <= d / 2 + margin and not fixed[j * hnx + i]:
                out.append(j * hnx + i)
    return out


for lx, lz, lyaw, lw, ld, _ in lots:
    flatten(rect_cells(lx, lz, lyaw, lw, ld))
flatten([j * hnx + i for j in range(hnz) for i in range(hnx)
         if not fixed[j * hnx + i] and flag(X0 + i * HCELL, Z0 + j * HCELL, AIRPORT)])

STEP = GRADE * HCELL
DIAG = STEP * math.sqrt(2)
FWD = ((-1, 0, STEP), (-1, -1, DIAG), (0, -1, STEP), (1, -1, DIAG))
BWD = ((1, 0, STEP), (1, 1, DIAG), (0, 1, STEP), (-1, 1, DIAG))


def envelope(lower):
    """Pente limitée : abaisse les cases trop hautes (lower) ou relève les trop basses, en 2 balayages."""
    for neigh, order in ((FWD, range(hnz)), (BWD, range(hnz - 1, -1, -1))):
        for j in order:
            irange = range(hnx) if neigh is FWD else range(hnx - 1, -1, -1)
            for i in irange:
                c = j * hnx + i
                if fixed[c]:
                    continue
                v = H[c]
                for di, dj, cost in neigh:
                    ii, jj = i + di, j + dj
                    if 0 <= ii < hnx and 0 <= jj < hnz:
                        n = H[jj * hnx + ii]
                        if lower:
                            if n + cost < v:
                                v = n + cost
                        elif n - cost > v:
                            v = n - cost
                H[c] = v


for _ in range(2):
    envelope(True)
for _ in range(2):
    envelope(False)
envelope(True)
height = {"x": round(X0, 1), "z": round(Z0, 1), "cell": HCELL, "nx": hnx, "nz": hnz,
          "h": [int(round(v * 10)) for v in H]}
print("relief : %.1f m au plus haut (jeu), pente maxi %d %%" % (max(H), GRADE * 100))


# Format lisible par JsonUtility (Unity) : pas de tableaux imbriqués, coordonnées aplaties.
def flat(points):
    return [round(v, 1) for p in points for v in p]


roads_json = [{"cls": r["class"], "name": r["name"], "width": r["width"], "p": flat(r["points"]),
               "b": [v for br in r["bridges"] for v in br], "de": r["de"], "ring": r.get("ring", False)} for r in roads_out]
sidewalks_json = [{"p": flat(s["points"])} for s in sidewalks]
airport_json = {
    "fence": [{"p": flat(r)} for r in AIR.get("fence", [])],
    "runways": [{"name": rw["name"], "width": 24.0, "p": flat(rw["points"])} for rw in AIR.get("runways", [])],
    "taxiways": [{"name": tw["name"], "width": 9.0, "p": flat(tw["points"])} for tw in AIR.get("taxiways", [])],
    "aprons": [{"p": flat(r)} for r in AIR.get("aprons", [])],
    "terminals": [{"name": t["name"], "p": flat(t["ring"])} for t in AIR.get("terminals", [])],
}

layout = {
    "source": "OpenStreetMap (ODbL), Abidjan, simplifié et réduit au 1/%d ; relief SRTM (NASA)" % SCALE,
    "bounds": [round(X0, 1), round(Z0, 1), round(X1, 1), round(Z1, 1)],
    "districts": districts,
    "roads": roads_json,
    "junctions": junctions,
    "roundabouts": [{"x": rb["x"], "z": rb["z"], "r": rb["r"], "name": rb["name"]} for rb in roundabouts],
    "sidewalkWidth": SIDEWALK,
    "sidewalks": sidewalks_json,
    "blocks": blocks,
    "landmarks": landmarks,
    "parkings": parkings,
    "buildings": buildings,
    "anchors": anchors,
    "trees": trees,
    "airport": airport_json,
    "waterMask": water_mask,
    "forestMask": forest_mask,
    "height": height,
}
os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
json.dump(layout, open(OUT_JSON, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))

# --- Aperçu ---
W = 2000
Hpx = int(W * (Z1 - Z0) / (X1 - X0))
k = W / (X1 - X0)


def sp(x, z):
    return "%.1f,%.1f" % ((x - X0) * k, Hpx - (z - Z0) * k)


colors = {"Plateau": "#8fa7c2", "Adjamé": "#d29a6a", "Cocody": "#eadbb5", "Yopougon": "#ddc38a", "Attécoubé": "#cdb27a",
          "Treichville": "#d08a5c", "Marcory": "#bdbdb5", "Koumassi": "#c7a383", "Abobo": "#c99b74", "Riviera": "#efe3c4",
          "Port-Bouët": "#d6b48c"}
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{Hpx}" viewBox="0 0 {W} {Hpx}">', '<rect width="100%" height="100%" fill="#c9d6a3"/>']
# Relief : ombrage par paliers de 2 m.
for j in range(0, hnz, 2):
    for i in range(0, hnx, 2):
        v = H[j * hnx + i]
        if v >= 2.0:
            g = max(90, 210 - int(v * 6))
            svg.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="rgb(%d,%d,%d)" opacity="0.35"/>' % (
                (i * HCELL - HCELL) * k, Hpx - (j * HCELL + HCELL) * k, 2 * HCELL * k, 2 * HCELL * k, g, g - 10, g - 40))
svg.append('<path fill="#5f8f4e" d="%s"/>' % " ".join("M" + " L".join(sp(*p) for p in ring) + " Z" for ring in plan.get("forest", [])))
svg.append('<path fill="#8cc2e3" fill-rule="evenodd" d="%s"/>' % " ".join("M" + " L".join(sp(*p) for p in ring) + " Z" for ring in plan["water"]))
svg.append('<path fill="#8cc2e3" d="%s"/>' % " ".join("M" + " L".join(sp(*p) for p in ring) + " Z" for ring in plan.get("ocean", [])))
for rw in AIR.get("runways", []) + AIR.get("taxiways", []):
    svg.append('<polyline points="%s" fill="none" stroke="#555" stroke-width="%.1f"/>' % (" ".join(sp(*p) for p in rw["points"]), (24 if rw in AIR.get("runways", []) else 9) * k))
for s in sidewalks:
    svg.append('<polyline points="%s" fill="none" stroke="#d8d2c4" stroke-width="%.1f"/>' % (" ".join(sp(*p) for p in s["points"]), SIDEWALK * k))
for b in buildings:
    yaw = math.radians(b["yaw"])
    ca, sa = math.cos(yaw), math.sin(yaw)
    poly = [(b["x"] + u * ca - v * sa, b["z"] + u * sa + v * ca) for u, v in
            [(-b["w"] / 2, -b["d"] / 2), (b["w"] / 2, -b["d"] / 2), (b["w"] / 2, b["d"] / 2), (-b["w"] / 2, b["d"] / 2)]]
    svg.append('<polygon points="%s" fill="%s" stroke="#5c5346" stroke-width="0.4"/>' % (" ".join(sp(*p) for p in poly), colors[b["district"]]))
for lx, lz, lyaw, lw, ld, _ in lots:
    yaw = math.radians(lyaw)
    ca, sa = math.cos(yaw), math.sin(yaw)
    poly = [(lx + u * ca - v * sa, lz + u * sa + v * ca) for u, v in [(-lw / 2, -ld / 2), (lw / 2, -ld / 2), (lw / 2, ld / 2), (-lw / 2, ld / 2)]]
    svg.append('<polygon points="%s" fill="#f4f1e8" stroke="#7a1f1f" stroke-width="0.8"/>' % " ".join(sp(*p) for p in poly))
for r in roads_out:
    svg.append('<polyline points="%s" fill="none" stroke="#3b3b3b" stroke-width="%.1f" stroke-linecap="round" stroke-linejoin="round"/>'
               % (" ".join(sp(*p) for p in r["points"]), r["width"] * k))
    for br in r["bridges"]:
        svg.append('<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="#e9c46a" stroke-width="%.1f"/>' % (*sp(br[0], br[1]).split(","), *sp(br[2], br[3]).split(","), r["width"] * k * 0.6))
for item in blocks + landmarks:
    x, y = sp(item["x"], item["z"]).split(",")
    svg.append(f'<text x="{x}" y="{y}" font-size="10" text-anchor="middle" fill="#7a1f1f" stroke="#fff" stroke-width="3" paint-order="stroke">{item["name"]}</text>')
for d in districts:
    x, y = sp(d["x"], d["z"]).split(",")
    svg.append(f'<text x="{x}" y="{float(y) - 30:.0f}" font-size="26" font-weight="bold" text-anchor="middle" fill="#1d3557" stroke="#fff" stroke-width="5" paint-order="stroke">{d["name"]}</text>')
x0p, y0p = sp(X0 + 20, Z0 + 20).split(",")
x1p, _ = sp(X0 + 220, Z0 + 20).split(",")
svg.append(f'<line x1="{x0p}" y1="{y0p}" x2="{x1p}" y2="{y0p}" stroke="#000" stroke-width="5"/><text x="{x0p}" y="{float(y0p) - 10:.0f}" font-size="20">200 m</text>')
svg.append("</svg>")
os.makedirs(os.path.dirname(OUT_SVG), exist_ok=True)
open(OUT_SVG, "w", encoding="utf-8").write("\n".join(svg))
print("%d parkings" % len(parkings))
print("%d lieux, %d monuments, %d bâtiments, %d repères, %d trottoirs, %d ponts, %d arbres ; carte %.0f x %.0f m" % (
    len(blocks), len(landmarks), len(buildings), len(anchors), len(sidewalks),
    sum(len(r["bridges"]) for r in roads_out), len(trees) // 2, X1 - X0, Z1 - Z0))
for lm in landmarks + blocks:
    print("  %-36s %-12s décalage %5.0f m (OSM : %s)" % (lm["name"], lm["district"], lm["offset"], lm["osm"] or "-"))
print("orientations :", ", ".join("%s %.0f°" % (d["name"], d["yaw"]) for d in districts))
