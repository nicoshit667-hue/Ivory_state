"""Plan du jeu d'après le tracé OSM d'Abidjan : projection, réduction d'échelle, recousage et simplification.

Sortie : plan JSON (routes du jeu en mètres, eau, océan, forêts, aéroport, lieux nommés, altitudes) et
aperçu SVG. Les données brutes viennent de fetch_osm.py.
Usage : python -I simplify_osm.py <dossier osm> <sortie json> <sortie svg>
"""
import json, math, os, sys

src, out_json, out_svg = sys.argv[1], sys.argv[2], sys.argv[3]

# Cadre réel et échelle du jeu (1 m de jeu = SCALE m réels). Origine : le Plateau.
LON0, LAT0 = -4.0175, 5.3200
# De Yopougon à Bingerville (ouest-est), de la côte de Port-Bouët (aéroport) à Abobo et la forêt du Banco.
LON_MIN, LON_MAX, LAT_MIN, LAT_MAX = -4.100, -3.895, 5.228, 5.445
SCALE = 8.0
KX = 111320 * math.cos(math.radians(LAT0)) / SCALE
KZ = 110540 / SCALE

CLASS = {"motorway": 0, "trunk": 0, "primary": 1, "secondary": 2, "tertiary": 3,
         "motorway_link": 9, "trunk_link": 9, "primary_link": 9}
WIDTH = {0: 16, 1: 13, 2: 10, 3: 8}


def proj(p):
    return ((p["lon"] - LON0) * KX, (p["lat"] - LAT0) * KZ)


def inside(p):
    return LON_MIN <= p["lon"] <= LON_MAX and LAT_MIN <= p["lat"] <= LAT_MAX


def dp(pts, tol, keep_set=None):
    """Douglas-Peucker, en préservant les sommets de carrefour présents dans keep_set."""
    if len(pts) < 3:
        return pts
    if keep_set:
        for i in range(1, len(pts) - 1):
            if (round(pts[i][0], 1), round(pts[i][1], 1)) in keep_set:
                return dp(pts[:i + 1], tol, keep_set)[:-1] + dp(pts[i:], tol, keep_set)
    (ax, az), (bx, bz) = pts[0], pts[-1]
    dx, dz = bx - ax, bz - az
    n = math.hypot(dx, dz) or 1e-9
    best, idx = -1, 0
    for i in range(1, len(pts) - 1):
        px, pz = pts[i]
        d = abs(dz * (px - ax) - dx * (pz - az)) / n
        if d > best:
            best, idx = d, i
    if best <= tol:
        return [pts[0], pts[-1]]
    return dp(pts[:idx + 1], tol, keep_set)[:-1] + dp(pts[idx:], tol, keep_set)


def length(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))


def dedup_pts(pts, min_dist=0.3):
    if not pts:
        return pts
    out = [pts[0]]
    for p in pts[1:]:
        if math.hypot(p[0] - out[-1][0], p[1] - out[-1][1]) >= min_dist:
            out.append(p)
    if len(out) == 1 and len(pts) >= 2 and math.hypot(pts[-1][0] - out[0][0], pts[-1][1] - out[0][1]) > 0.05:
        out.append(pts[-1])
    return out


def nearest_on_polyline(px, pz, pts):
    best_d = 1e18
    best_q = pts[0]
    best_seg = 0
    for k in range(len(pts) - 1):
        ax, az = pts[k]
        bx, bz = pts[k + 1]
        dx, dz = bx - ax, bz - az
        L2 = dx * dx + dz * dz
        t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / L2))
        qx, qz = ax + t * dx, az + t * dz
        d = math.hypot(px - qx, pz - qz)
        if d < best_d:
            best_d = d
            best_q = (qx, qz)
            best_seg = k
    return best_d, best_q, best_seg


def insert_vertex(pts, q, tol=0.6):
    qx, qz = q
    for k, (x, z) in enumerate(pts):
        if math.hypot(x - qx, z - qz) <= tol:
            pts[k] = (qx, qz)
            return pts[k]
    _, best_q, seg = nearest_on_polyline(qx, qz, pts)
    pts.insert(seg + 1, best_q)
    return best_q


# --- 1. Eau : anneaux des multipolygones (lagune Ébrié) recousus bout à bout, puis simplifiés ---
def stitch_rings(segments):
    rings, segs = [], [list(s) for s in segments if len(s) >= 2]
    while segs:
        ring = segs.pop(0)
        changed = True
        while changed and ring[0] != ring[-1]:
            changed = False
            for i, s in enumerate(segs):
                if s[0] == ring[-1]:
                    ring += s[1:]
                elif s[-1] == ring[-1]:
                    ring += s[::-1][1:]
                elif s[-1] == ring[0]:
                    ring = s[:-1] + ring
                elif s[0] == ring[0]:
                    ring = s[::-1][:-1] + ring
                else:
                    continue
                segs.pop(i)
                changed = True
                break
        rings.append(ring)
    return rings


def ring_area(pts):
    return abs(sum(pts[k][0] * pts[(k + 1) % len(pts)][1] - pts[(k + 1) % len(pts)][0] * pts[k][1] for k in range(len(pts)))) / 2


def element_rings(e):
    """Anneaux (lon, lat) d'un chemin fermé ou d'une relation multipolygone (extérieurs puis intérieurs)."""
    if e["type"] == "way" and "geometry" in e:
        return [[(p["lon"], p["lat"]) for p in e["geometry"]]]
    groups = []
    for role in ("outer", "inner"):
        segs = [[(p["lon"], p["lat"]) for p in m["geometry"]] for m in e.get("members", []) if m.get("role") == role and "geometry" in m]
        groups += stitch_rings(segs)
    return groups


def game_ring(g, tol=2.0, min_area=0.0):
    if not any(LON_MIN - 0.02 <= lon <= LON_MAX + 0.02 and LAT_MIN - 0.02 <= lat <= LAT_MAX + 0.02 for lon, lat in g):
        return None
    pts = [proj({"lon": lon, "lat": lat}) for lon, lat in g]
    h = len(pts) // 2
    s = dp(pts[:h + 1], tol)[:-1] + dp(pts[h:], tol) if len(pts) > 4 else pts
    if len(s) < 3 or ring_area(s) < min_area:
        return None
    return [[round(x, 1), round(z, 1)] for x, z in s]


# Lagune, baies, canal et grands plans d'eau. Les bassins et mares de moins de 600 m² (jeu) sont ignorés.
BIG_WATER = ("lagoon", "bay", "canal", "river")
water = []
for e in json.load(open(src + "/water_full.json", encoding="utf-8"))["elements"]:
    tags = e.get("tags", {})
    if tags.get("water") in ("wastewater", "basin", "pond") and e["type"] == "way":
        continue
    big = tags.get("water") in BIG_WATER or tags.get("natural") == "bay"
    for g in element_rings(e):
        ring = game_ring(g, 2.0, 0.0 if big else 600.0)
        if ring:
            water.append(ring)

# Océan : le trait de côte (terre à gauche, mer à droite) recousu, puis fermé par le sud du cadre.
ocean = []
coast = [[(p["lon"], p["lat"]) for p in e["geometry"]] for e in json.load(open(src + "/coastline.json", encoding="utf-8"))["elements"] if "geometry" in e]
for chain in stitch_rings(coast):
    if chain[0] == chain[-1]:
        continue  # île fermée : ignorée (aucune au large dans le cadre)
    south = LAT_MIN - 0.03
    closed = chain + [(chain[-1][0], south), (chain[0][0], south)]
    ring = game_ring(closed, 2.5)
    if ring:
        ocean.append(ring)
print("océan :", len(ocean), "contour(s)")

# Grille d'eau rapide pour éviter de prolonger une route à travers la lagune.
_gx0, _gz0 = proj({"lon": LON_MIN, "lat": LAT_MIN})
_gx1, _gz1 = proj({"lon": LON_MAX, "lat": LAT_MAX})
WX0, WZ0, WRES = _gx0 - 60.0, _gz0 - 60.0, 4.0
WNX, WNZ = int((_gx1 - _gx0 + 120) / WRES) + 1, int((_gz1 - _gz0 + 120) / WRES) + 1
wgrid = bytearray(WNX * WNZ)


def fill_rings(rings, grid, value=1):
    """Remplissage pair-impair d'un groupe d'anneaux, ajouté (OU) à la grille."""
    edges = []
    for ring in rings:
        for k in range(len(ring)):
            a, b = ring[k], ring[(k + 1) % len(ring)]
            if a[1] != b[1]:
                edges.append((a[0], a[1], b[0], b[1]))
    for j in range(WNZ):
        z = WZ0 + (j + 0.5) * WRES
        xs = sorted(ax + (z - az) * (bx - ax) / (bz - az) for ax, az, bx, bz in edges if (az <= z) != (bz <= z))
        for k in range(0, len(xs) - 1, 2):
            i0 = max(0, int((xs[k] - WX0) / WRES))
            i1 = min(WNX - 1, int((xs[k + 1] - WX0) / WRES))
            for i in range(i0, i1 + 1):
                grid[j * WNX + i] = value


fill_rings(water, wgrid)
fill_rings(ocean, wgrid)


def is_water(x, z):
    i, j = int((x - WX0) / WRES), int((z - WZ0) / WRES)
    return 0 <= i < WNX and 0 <= j < WNZ and wgrid[j * WNX + i] != 0


# --- 2. Lecture des voies OSM et fusion des ronds-points en carrefours francs ---
raw_ways = []
rb_ways = []
link_ways = []
for f in ("roads.json", "tertiary.json"):
    for e in json.load(open(src + "/" + f, encoding="utf-8"))["elements"]:
        tags = e.get("tags", {})
        hw = tags.get("highway")
        if hw not in CLASS or "geometry" not in e:
            continue
        name = tags.get("name", "")
        is_rb = tags.get("junction") == "roundabout" or "rond-point" in name.lower() or "rond point" in name.lower()
        oneway = tags.get("oneway") == "yes" or hw == "motorway" or is_rb
        run = []
        for p in e["geometry"]:
            if inside(p):
                run.append(proj(p))
            elif run:
                if len(run) >= 2:
                    item = [CLASS[hw], name, dedup_pts(run), oneway]
                    if len(item[2]) >= 2:
                        (rb_ways if is_rb else (link_ways if CLASS[hw] == 9 else raw_ways)).append(item)
                run = []
        if len(run) >= 2:
            item = [CLASS[hw], name, dedup_pts(run), oneway]
            if len(item[2]) >= 2:
                (rb_ways if is_rb else (link_ways if CLASS[hw] == 9 else raw_ways)).append(item)

# Regroupement des anneaux de ronds-points et rabattement sur leur centre.
rb_clusters = [list(pts) for _, _, pts, _ in rb_ways]
rb_boxes = [[min(x for x, _ in c), min(z for _, z in c), max(x for x, _ in c), max(z for _, z in c)] for c in rb_clusters]
changed = True
while changed:
    changed = False
    for i in range(len(rb_clusters)):
        bi = rb_boxes[i]
        for j in range(i + 1, len(rb_clusters)):
            bj = rb_boxes[j]
            if bi[2] + 3.5 < bj[0] or bj[2] + 3.5 < bi[0] or bi[3] + 3.5 < bj[1] or bj[3] + 3.5 < bi[1]:
                continue
            if any(math.hypot(x - u, z - v) < 3.5 for x, z in rb_clusters[i] for u, v in rb_clusters[j]):
                rb_clusters[i].extend(rb_clusters[j])
                bi[0], bi[1] = min(bi[0], bj[0]), min(bi[1], bj[1])
                bi[2], bi[3] = max(bi[2], bj[2]), max(bi[3], bj[3])
                rb_clusters.pop(j)
                rb_boxes.pop(j)
                changed = True
                break
        if changed:
            break

rb_hubs = []
roundabouts = []  # [x, z, rayon de l'anneau réel (m de jeu), nom] : refaits en vrais ronds-points par build_layout
for c in rb_clusters:
    cx = sum(x for x, _ in c) / len(c)
    cz = sum(z for _, z in c) / len(c)
    rad = max(math.hypot(x - cx, z - cz) for x, z in c)
    names = [n for _, n, pts, _ in rb_ways if n and any(math.hypot(x - cx, z - cz) <= rad + 3.0 for x, z in pts)]
    if len(c) >= 3:
        roundabouts.append([round(cx, 1), round(cz, 1), round(rad, 1), max(set(names), key=names.count) if names else ""])
    if rad <= 22.0:
        rb_hubs.append((cx, cz, rad + 2.6, c))

for w in raw_ways:
    pts = w[2]
    for k, (x, z) in enumerate(pts):
        for cx, cz, rmax, c in rb_hubs:
            if abs(x - cx) <= rmax and abs(z - cz) <= rmax and any(math.hypot(x - u, z - v) <= 2.5 for u, v in c):
                pts[k] = (cx, cz)
                break
    w[2] = dedup_pts(pts)
ways = [w for w in raw_ways if len(w[2]) >= 2]


# --- 3. Recousage des tronçons consécutifs d'une même rue ---
def end_tangent(pts, at_end):
    if at_end:
        dx, dz = pts[-1][0] - pts[-2][0], pts[-1][1] - pts[-2][1]
    else:
        dx, dz = pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]
    n = math.hypot(dx, dz) or 1e-9
    return dx / n, dz / n


def stitch_ways(way_list, match_oneway=True, tol=0.9, min_dot=0.45):
    items = [[w[0], w[1], list(w[2]), w[3]] for w in way_list if len(w[2]) >= 2]
    alive = [True] * len(items)
    cell_size = max(2.0, tol * 2.0)

    def bkey(p):
        return (int(math.floor(p[0] / cell_size)), int(math.floor(p[1] / cell_size)))

    while True:
        buckets = {}
        for idx, ok in enumerate(alive):
            if not ok:
                continue
            pts = items[idx][2]
            for pt in (pts[0], pts[-1]):
                bx, bz = bkey(pt)
                for dx in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        buckets.setdefault((bx + dx, bz + dz), []).append(idx)

        candidates_list = []
        seen_pairs = set()
        for idx, ok in enumerate(alive):
            if not ok:
                continue
            c1, n1, p1, ow1 = items[idx]
            candidates = set(buckets.get(bkey(p1[0]), ())) | set(buckets.get(bkey(p1[-1]), ()))
            for j in candidates:
                if j <= idx or not alive[j]:
                    continue
                pair = (idx, j)
                if pair in seen_pairs:
                    continue
                seen_pairs.add(pair)
                c2, n2, p2, ow2 = items[j]
                if match_oneway and ow1 != ow2:
                    continue
                same_name = bool(n1 and n2 and n1 == n2)
                if c1 != c2 and not (same_name and abs(c1 - c2) <= 1):
                    continue
                if match_oneway and ow1:
                    orientations = [(p1, p2), (p2, p1)]
                else:
                    orientations = [(p1, p2), (p1, p2[::-1]), (p1[::-1], p2), (p1[::-1], p2[::-1])]
                best_mode = None
                for a, b in orientations:
                    d = math.hypot(a[-1][0] - b[0][0], a[-1][1] - b[0][1])
                    if d > tol:
                        continue
                    tx1, tz1 = end_tangent(a, True)
                    tx2, tz2 = end_tangent(b, False)
                    dot = tx1 * tx2 + tz1 * tz2
                    if dot < min_dot:
                        continue
                    score = dot + (2.0 if same_name else 0.0) + (0.5 if c1 == c2 else 0.0) - d * 0.2
                    if best_mode is None or score > best_mode[0]:
                        best_mode = (score, idx, j, a, b)
                if best_mode is not None:
                    candidates_list.append(best_mode)
        if not candidates_list:
            break
        candidates_list.sort(key=lambda x: -x[0])
        used = set()
        merged_any = False
        for _, i, j, a, b in candidates_list:
            if i in used or j in used:
                continue
            used.add(i)
            used.add(j)
            c1, n1, p1, ow1 = items[i]
            c2, n2, p2, ow2 = items[j]
            merged_pts = dedup_pts(a + b[1:])
            cls = c1 if length(p1) >= length(p2) else c2
            if c1 != c2:
                cls = min(c1, c2)
            name = n1 if (n1 and (not n2 or len(p1) >= len(p2))) else (n2 or n1)
            items[i] = [cls, name, merged_pts, ow1 and ow2]
            alive[j] = False
            merged_any = True
        if not merged_any:
            break
    return [items[i] for i in range(len(items)) if alive[i] and len(items[i][2]) >= 2]


ways = stitch_ways(ways, match_oneway=True, tol=1.0, min_dot=0.45)


# --- 4. Chaussées doubles : fusion d'une des deux voies et report des connexions sur la voie gardée ---
def sample_polyline(pts, step=3.5):
    out = []
    for k in range(len(pts) - 1):
        ax, az = pts[k]
        bx, bz = pts[k + 1]
        L = math.hypot(bx - ax, bz - az)
        if L < 1e-6:
            continue
        tx, tz = (bx - ax) / L, (bz - az) / L
        n = max(1, int(L / step))
        for s in range(n):
            t = s / n
            out.append((ax + (bx - ax) * t, az + (bz - az) * t, tx, tz))
    tx, tz = end_tangent(pts, True)
    out.append((pts[-1][0], pts[-1][1], tx, tz))
    return out


def bbox_of(pts):
    xs = [x for x, _ in pts]
    zs = [z for _, z in pts]
    return (min(xs), min(zs), max(xs), max(zs))


def bbox_overlap(b1, b2, margin):
    return not (b1[2] + margin < b2[0] or b2[2] + margin < b1[0] or b1[3] + margin < b2[1] or b2[3] + margin < b1[1])


def pt_in_bbox(px, pz, b, margin):
    return b[0] - margin <= px <= b[2] + margin and b[1] - margin <= pz <= b[3] + margin


def clip_parallel_spans(ways_in):
    """Fusionne les chaussées doubles à sens unique et écarte les ruelles tertiaires entièrement collées à un grand axe."""
    def priority_key(w):
        cls, name, pts, oneway = w
        dx, dz = pts[-1][0] - pts[0][0], pts[-1][1] - pts[0][1]
        canonical = 1 if (not oneway or (dx * 0.85 + dz * 0.52) > 0) else 0
        return (cls, -canonical, -length(pts))

    ordered = sorted(ways_in, key=priority_key)
    accepted = []
    acc_boxes = []
    transfers = []

    for w in ordered:
        cls, name, pts, oneway = w
        samples = sample_polyline(pts, 3.5)
        if len(samples) < 2:
            continue
        bw = bbox_of(pts)
        n_s = len(samples)
        shadow_target = [None] * n_s

        for idx_a, o in enumerate(accepted):
            o_cls, o_name, o_pts, o_ow = o
            same_name = bool(name and o_name and name == o_name)
            if oneway and o_ow and abs(o_cls - cls) <= 1:
                tol = 15.0 if same_name else 9.5
            elif cls == 3 and o_cls <= 2:
                tol = 16.0
            else:
                continue
            if not bbox_overlap(bw, acc_boxes[idx_a], tol):
                continue
            for k, (sx, sz, tx, tz) in enumerate(samples):
                if shadow_target[k] is not None or not pt_in_bbox(sx, sz, acc_boxes[idx_a], tol):
                    continue
                d, _, seg = nearest_on_polyline(sx, sz, o_pts)
                if d <= tol:
                    obx, obz = o_pts[seg + 1][0] - o_pts[seg][0], o_pts[seg + 1][1] - o_pts[seg][1]
                    ol = math.hypot(obx, obz) or 1e-9
                    dot = (tx * obx + tz * obz) / ol
                    if (oneway and o_ow and dot < -0.45) or (cls == 3 and o_cls <= 2 and abs(dot) >= 0.82):
                        shadow_target[k] = o

        # Ignore les courts contacts aux carrefours (< 8 échantillons = 28 m) : seule une vraie
        # section parallèle continue d'au moins 28 m compte comme chaussée doublonnée.
        k = 0
        while k < n_s:
            if shadow_target[k] is not None:
                e = k
                while e < n_s and shadow_target[e] is not None:
                    e += 1
                if e - k < 8 and n_s >= 8:
                    for q in range(k, e):
                        shadow_target[q] = None
                k = e
            else:
                k += 1

        # Lissage : un court passage non masqué (< 6 échantillons = 21 m) entre deux passages masqués est masqué aussi.
        k = 0
        while k < n_s:
            if shadow_target[k] is None:
                e = k
                while e < n_s and shadow_target[e] is None:
                    e += 1
                if e - k < 6 and (k > 0 or e < n_s) and any(t is not None for t in shadow_target):
                    fill = shadow_target[k - 1] if k > 0 else shadow_target[e]
                    for q in range(k, e):
                        shadow_target[q] = fill
                k = e
            else:
                k += 1

        hits = sum(1 for t in shadow_target if t is not None)
        ratio = hits / n_s
        if hits == 0:
            accepted.append(w)
            acc_boxes.append(bw)
            continue

        # Une rue bidirectionnelle (ou ruelle tertiaire) n'est jamais découpée en morceaux :
        # elle n'est écartée que si elle longe entièrement (>= 65 %) un grand axe.
        if not oneway:
            if ratio >= 0.65:
                first_t = next(t for t in shadow_target if t is not None)
                transfers.append((pts, first_t))
            else:
                accepted.append(w)
                acc_boxes.append(bw)
            continue

        # Enregistre les voies cibles pour reporter les carrefours.
        seen_t = set()
        for t in shadow_target:
            if t is not None and id(t) not in seen_t:
                seen_t.add(id(t))
                transfers.append((pts, t))

        # Conserve toute plage libre d'au moins 30 m (ex. : pont ou prolongement au-delà de la section doublonnée).
        k = 0
        while k < n_s:
            if shadow_target[k] is None:
                e = k
                while e < n_s and shadow_target[e] is None:
                    e += 1
                span_pts = [(samples[q][0], samples[q][1]) for q in range(k, e)]
                if k == 0 and e == n_s:
                    accepted.append(w)
                    acc_boxes.append(bw)
                elif length(span_pts) >= 30.0:
                    if k > 0 and shadow_target[k - 1] is not None:
                        t_o = shadow_target[k - 1]
                        d_o, q_o, _ = nearest_on_polyline(span_pts[0][0], span_pts[0][1], t_o[2])
                        if d_o <= 18.0:
                            q_ins = insert_vertex(t_o[2], q_o, tol=0.8)
                            span_pts.insert(0, q_ins)
                    if e < n_s and shadow_target[e] is not None:
                        t_o = shadow_target[e]
                        d_o, q_o, _ = nearest_on_polyline(span_pts[-1][0], span_pts[-1][1], t_o[2])
                        if d_o <= 18.0:
                            q_ins = insert_vertex(t_o[2], q_o, tol=0.8)
                            span_pts.append(q_ins)
                    span_pts = dedup_pts(span_pts)
                    if len(span_pts) >= 2:
                        accepted.append([cls, name, span_pts, oneway])
                        acc_boxes.append(bbox_of(span_pts))
                k = e
            else:
                k += 1

    # Report de tous les carrefours et extrémités attachés à une portion supprimée vers la chaussée gardée.
    for drop_pts, w_keep in transfers:
        if len(drop_pts) < 2:
            continue
        b_drop = bbox_of(drop_pts)
        keep_pts = w_keep[2]
        for r in accepted:
            if r is w_keep:
                continue
            rpts = r[2]
            for idx in (0, -1):
                px, pz = rpts[idx]
                if not pt_in_bbox(px, pz, b_drop, 3.5):
                    continue
                d_drop, _, _ = nearest_on_polyline(px, pz, drop_pts)
                if d_drop <= 3.0:
                    d_keep, q, _ = nearest_on_polyline(px, pz, keep_pts)
                    if d_keep <= 19.0:
                        q_ins = insert_vertex(keep_pts, q, tol=0.8)
                        rpts[idx] = q_ins
            r[2] = dedup_pts(rpts)

    return [w for w in accepted if len(w[2]) >= 2]


kept = clip_parallel_spans(ways)

# Recousage après fusion des chaussées doubles (raccorde les passages 2x1 voie <-> 2x2 voies).
kept = stitch_ways(kept, match_oneway=False, tol=1.5, min_dot=0.40)
# Deuxième passe de nettoyage parallèle au cas où le recousage a révélé un doublon.
kept = clip_parallel_spans(kept)
kept = stitch_ways(kept, match_oneway=False, tol=1.5, min_dot=0.40)

# Raccordement des grands axes séparés par une bretelle (*_link) d'échangeur.
link_ways = stitch_ways(link_ways, match_oneway=True, tol=1.2, min_dot=0.25)
bboxes = {id(r): bbox_of(r[2]) for r in kept}
for r in kept:
    if r[0] > 1:
        continue
    for idx in (0, -1):
        px, pz = r[2][idx]
        if any(o is not r and o[0] <= 2 and pt_in_bbox(px, pz, bboxes[id(o)], 2.0) and nearest_on_polyline(px, pz, o[2])[0] <= 1.5 for o in kept):
            continue
        for lw in link_ways:
            lpts = lw[2]
            for l_start, l_end in ((lpts[0], lpts[-1]), (lpts[-1], lpts[0])):
                if math.hypot(px - l_start[0], pz - l_start[1]) <= 3.5 and length(lpts) <= 65.0:
                    for o in kept:
                        if o is r or o[0] > 2 or not pt_in_bbox(l_end[0], l_end[1], bboxes[id(o)], 5.0):
                            continue
                        d_o, q_o, _ = nearest_on_polyline(l_end[0], l_end[1], o[2])
                        if d_o <= 4.0:
                            q_ins = insert_vertex(o[2], q_o, tol=0.8)
                            bboxes[id(o)] = bbox_of(o[2])
                            r[2][idx] = q_ins
                            bboxes[id(r)] = bbox_of(r[2])
                            break

# Re-recousage des grands axes désormais joints.
kept = stitch_ways(kept, match_oneway=False, tol=1.5, min_dot=0.35)

# Filtrage par longueur sur les rues ENTIÈRES recousues.
kept = [w for w in kept if length(w[2]) >= (8.0 if w[0] <= 2 else 22.0)]


# --- 5. Fermeture des quasi-jonctions (extrémités pendantes proches d'une autre rue) ---
bboxes = {id(r): bbox_of(r[2]) for r in kept}
for r in kept:
    pts = r[2]
    for idx in (0, -1):
        px, pz = pts[idx]
        already = False
        for o in kept:
            if o is r or not pt_in_bbox(px, pz, bboxes[id(o)], 2.0):
                continue
            d_o, q_o, _ = nearest_on_polyline(px, pz, o[2])
            if d_o <= 1.2:
                q_ins = insert_vertex(o[2], q_o, tol=0.8)
                bboxes[id(o)] = bbox_of(o[2])
                pts[idx] = q_ins
                already = True
                break
        if already:
            continue
        tx, tz = end_tangent(pts, idx == -1)
        if idx == 0:
            tx, tz = -tx, -tz
        max_snap = 24.0 if r[0] <= 1 else 16.0
        best_d = max_snap
        best_target = None
        for o in kept:
            if o is r or not pt_in_bbox(px, pz, bboxes[id(o)], max_snap):
                continue
            d_o, q_o, _ = nearest_on_polyline(px, pz, o[2])
            if d_o < best_d:
                vx, vz = q_o[0] - px, q_o[1] - pz
                vl = math.hypot(vx, vz) or 1e-9
                if d_o > 9.0 and (vx * tx + vz * tz) / vl < -0.25:
                    continue
                if any(is_water(px + (q_o[0] - px) * k / 4, pz + (q_o[1] - pz) * k / 4) for k in range(1, 5)):
                    continue
                best_d = d_o
                best_target = (o, q_o)
        if best_target is not None:
            o, q_o = best_target
            q_ins = insert_vertex(o[2], q_o, tol=0.8)
            bboxes[id(o)] = bbox_of(o[2])
            if best_d <= 4.0:
                pts[idx] = q_ins
            elif idx == -1:
                pts.append(q_ins)
            else:
                pts.insert(0, q_ins)
    r[2] = dedup_pts(pts)
    bboxes[id(r)] = bbox_of(r[2])

# Dernier recousage si deux impasses face à face se sont rejointes.
kept = stitch_ways(kept, match_oneway=False, tol=1.2, min_dot=0.45)

# Suppression des petits îlots routiers isolés du réseau principal.
adj = [set() for _ in range(len(kept))]
for i in range(len(kept)):
    pi = kept[i][2]
    bxi0, bzi0 = min(x for x, _ in pi) - 3, min(z for _, z in pi) - 3
    bxi1, bzi1 = max(x for x, _ in pi) + 3, max(z for _, z in pi) + 3
    for j in range(i + 1, len(kept)):
        pj = kept[j][2]
        if max(x for x, _ in pj) < bxi0 or min(x for x, _ in pj) > bxi1:
            continue
        if max(z for _, z in pj) < bzi0 or min(z for _, z in pj) > bzi1:
            continue
        if any(nearest_on_polyline(x, z, pj)[0] <= 2.0 for x, z in pi) or any(nearest_on_polyline(x, z, pi)[0] <= 2.0 for x, z in pj):
            adj[i].add(j)
            adj[j].add(i)

visited = [False] * len(kept)
connected_kept = []
for i in range(len(kept)):
    if visited[i]:
        continue
    comp = []
    stack = [i]
    visited[i] = True
    while stack:
        u = stack.pop()
        comp.append(u)
        for v in adj[u]:
            if not visited[v]:
                visited[v] = True
                stack.append(v)
    comp_len = sum(length(kept[u][2]) for u in comp)
    if comp_len >= 120.0 or any(kept[u][0] <= 1 for u in comp):
        for u in comp:
            connected_kept.append(kept[u])
kept = connected_kept

# Repérage des sommets de carrefour à préserver lors de la simplification Douglas-Peucker.
vertex_counts = {}
for _, _, pts, _ in kept:
    for x, z in pts:
        key = (round(x, 1), round(z, 1))
        vertex_counts[key] = vertex_counts.get(key, 0) + 1
junction_nodes = {k for k, v in vertex_counts.items() if v >= 2}

roads = []
for cls, name, pts, _ in kept:
    s = dp(pts, 2.2, junction_nodes)
    if length(s) < (8 if cls <= 2 else 20):
        continue
    roads.append({"class": cls, "name": name, "width": WIDTH[cls], "points": [[round(x, 1), round(z, 1)] for x, z in s]})

xs = [x for r in roads for x, _ in r["points"]]
zs = [z for r in roads for _, z in r["points"]]
# --- 6. Forêts (parc national du Banco, forêts classées), aéroport, lieux nommés, altitudes ---
forest = []
airport = {"fence": [], "runways": [], "taxiways": [], "aprons": [], "terminals": []}
for e in json.load(open(src + "/landuse.json", encoding="utf-8"))["elements"]:
    tags = e.get("tags", {})
    aero = tags.get("aeroway")
    if aero in ("runway", "taxiway"):
        if "geometry" not in e or (aero == "runway" and "Héliport" in tags.get("name", "")):
            continue
        pts = [proj(p) for p in e["geometry"]]
        if not any(inside(p) for p in e["geometry"]):
            continue
        pts = dp(dedup_pts(pts), 1.0)
        if length(pts) < (40 if aero == "runway" else 6):
            continue
        (airport["runways"] if aero == "runway" else airport["taxiways"]).append({"name": tags.get("ref", ""), "points": [[round(x, 1), round(z, 1)] for x, z in pts]})
        continue
    for g in element_rings(e):
        if aero == "aerodrome":
            ring = game_ring(g, 1.5)
            if ring:
                airport["fence"].append(ring)
        elif aero == "apron":
            ring = game_ring(g, 1.0, 40.0)
            if ring:
                airport["aprons"].append(ring)
        elif aero == "terminal":
            ring = game_ring(g, 0.5, 20.0)
            if ring:
                airport["terminals"].append({"name": tags.get("name", ""), "ring": ring})
        elif aero is None:
            # Forêts : grands massifs seulement (au moins 0,5 ha de jeu, soit 32 ha réels).
            ring = game_ring(g, 3.0, 5000.0)
            if ring:
                forest.append(ring)
print("forêts :", len(forest), "; aéroport :", {k: len(v) for k, v in airport.items()})

places = []
for e in (json.load(open(src + "/places.json", encoding="utf-8"))["elements"] if os.path.exists(src + "/places.json") else []):
    tags = e.get("tags", {})
    c = e.get("center") or ({"lat": e["lat"], "lon": e["lon"]} if "lat" in e else None)
    if not c or not tags.get("name") or not inside(c):
        continue
    kind = (tags.get("amenity") or tags.get("leisure") or tags.get("tourism") or tags.get("historic") or tags.get("place")
            or tags.get("aeroway") or tags.get("building") or tags.get("highway") or tags.get("man_made") or "")
    x, z = proj(c)
    places.append([tags["name"], kind, round(x, 1), round(z, 1)])
places.sort()

# Feux tricolores réels (nœuds highway=traffic_signals).
signals = []
if os.path.exists(src + "/signals.json"):
    for e in json.load(open(src + "/signals.json", encoding="utf-8"))["elements"]:
        if "lat" in e and inside(e):
            x, z = proj(e)
            signals.append([round(x, 1), round(z, 1)])
print("ronds-points :", len(roundabouts), "; feux tricolores :", len(signals))

elev = json.load(open(src + "/elevation.json", encoding="utf-8"))
ex0, ez0 = proj({"lon": elev["lon0"], "lat": elev["lat0"]})
elevation = {"source": elev["source"], "x0": round(ex0, 2), "z0": round(ez0, 2),
             "dx": round(elev["step"] * KX, 4), "dz": round(elev["step"] * KZ, 4),
             "nx": elev["nx"], "nz": elev["nz"], "h": [round(v, 1) for v in elev["h"]]}

plan = {"source": "OpenStreetMap (ODbL) - Abidjan, simplifie ; relief SRTM", "scale": SCALE,
        "frame": [LON_MIN, LAT_MIN, LON_MAX, LAT_MAX],
        "bounds": [round(_gx0, 1), round(_gz0, 1), round(_gx1, 1), round(_gz1, 1)],
        "roads": roads, "water": water, "ocean": ocean, "forest": forest, "airport": airport,
        "places": places, "roundabouts": roundabouts, "signals": signals, "elevation": elevation}
json.dump(plan, open(out_json, "w", encoding="utf-8"), ensure_ascii=False)

# Aperçu SVG.
x0, z0, x1, z1 = plan["bounds"]
W = 2000
H = int(W * (z1 - z0) / (x1 - x0))
k = W / (x1 - x0)


def sp(x, z):
    return "%.1f,%.1f" % ((x - x0) * k, H - (z - z0) * k)


svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">', '<rect width="100%" height="100%" fill="#ece5d3"/>']
svg.append('<path fill="#7fae6b" d="%s"/>' % " ".join("M" + " L".join(sp(*p) for p in poly) + " Z" for poly in forest))
svg.append('<path fill="#8cc2e3" stroke="#4a9bd1" stroke-width="1.5" fill-rule="evenodd" d="%s"/>'
           % " ".join("M" + " L".join(sp(*p) for p in poly) + " Z" for poly in water))
svg.append('<path fill="#8cc2e3" d="%s"/>' % " ".join("M" + " L".join(sp(*p) for p in poly) + " Z" for poly in ocean))
for rw in airport["runways"] + airport["taxiways"]:
    svg.append('<polyline points="%s" fill="none" stroke="#444" stroke-width="%.1f"/>' % (" ".join(sp(*p) for p in rw["points"]), (24 if rw in airport["runways"] else 8) * k))
colors = {0: "#b03a2e", 1: "#d35400", 2: "#555", 3: "#999"}
for r in sorted(roads, key=lambda r: -r["class"]):
    svg.append('<polyline points="%s" fill="none" stroke="%s" stroke-width="%.1f" stroke-linecap="round" stroke-linejoin="round"/>'
               % (" ".join(sp(*p) for p in r["points"]), colors[r["class"]], r["width"] * k))
for label, lon, lat in [("Plateau", -4.018, 5.322), ("Adjamé", -4.025, 5.355), ("Cocody", -3.985, 5.350), ("Yopougon", -4.075, 5.340),
                        ("Treichville", -4.010, 5.300), ("Marcory", -3.980, 5.300), ("Attécoubé", -4.043, 5.333), ("Koumassi", -3.955, 5.295),
                        ("Port-Bouët", -3.950, 5.250), ("Abobo", -4.020, 5.420), ("Banco", -4.055, 5.390), ("Riviera", -3.955, 5.370)]:
    x, z = proj({"lon": lon, "lat": lat})
    svg.append(f'<text x="{(x - x0) * k:.0f}" y="{H - (z - z0) * k:.0f}" font-size="26" font-weight="bold" fill="#1d3557" stroke="#fff" stroke-width="4" paint-order="stroke">{label}</text>')
bx, bz = x0 + 20, z0 + 20
svg.append(f'<line x1="{(bx - x0) * k:.0f}" y1="{H - (bz - z0) * k:.0f}" x2="{(bx + 200 - x0) * k:.0f}" y2="{H - (bz - z0) * k:.0f}" stroke="#000" stroke-width="5"/>')
svg.append(f'<text x="{(bx - x0) * k:.0f}" y="{H - (bz - z0) * k - 10:.0f}" font-size="20">200 m (jeu)</text>')
svg.append("</svg>")
open(out_svg, "w", encoding="utf-8").write("\n".join(svg))
print(len(roads), "routes ;", len(water), "contours d'eau ; emprise %.0f x %.0f m" % (x1 - x0, z1 - z0),
      "; longueur totale %.1f km" % (sum(length(r["points"]) for r in roads) / 1000))

