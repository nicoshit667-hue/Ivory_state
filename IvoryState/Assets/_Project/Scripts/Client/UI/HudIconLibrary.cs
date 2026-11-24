using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Client.UI
{
    /// <summary>
    /// Bibliothèque centrale des icônes UI d'<i>Ivory State</i>.
    /// <para>
    /// Toutes les icônes importées par l'utilisateur dans <c>Assets/_Project/Resources/UI/Icons/</c>
    /// (monnaies, jauges vitales, actions à pied, barre inférieure, applications du téléphone, logo
    /// et armes futures) sont chargées directement en tant que <see cref="Sprite"/> configurés en
    /// <c>Sprite (2D and UI) / Single</c>. Seuls les gabarits de contours et éléments sans PNG dédié
    /// restent inchangés en attente de leurs futurs assets.
    /// </summary>
    public static class HudIconLibrary
    {
        private const string IconsResourceRoot = "UI/Icons/";

        // --- Contours géométriques de boutons (en attente de sprites PNG dédiés) ---
        public static Sprite Circle { get; private set; }
        public static Sprite Ring { get; private set; }
        public static Sprite ThinRing { get; private set; }
        public static Sprite RoundedBox { get; private set; }
        public static Sprite Pill { get; private set; }

        // --- Éléments sans icône PNG dédiée pour le moment (mode véhicule & sortie) ---
        public static Sprite ExitDoor { get; private set; }
        public static Sprite SteerArrow { get; private set; }
        public static Sprite Horn { get; private set; }
        public static Sprite Headlight { get; private set; }
        public static Sprite FuelPump { get; private set; }
        public static Sprite Wrench { get; private set; }
        public static Sprite PedalGrooves { get; private set; }

        // --- Monnaies PNG (HUD haut-gauche & garde-robe) ---
        public static Sprite Eco { get; private set; }
        public static Sprite PingoinCoin { get; private set; }
        public static Sprite Cash { get; private set; }

        // --- Jauges vitales PNG (HUD haut-droite) ---
        public static Sprite Health { get; private set; }
        public static Sprite Hunger { get; private set; }
        public static Sprite Thirst { get; private set; }

        // --- Touches d'action & menus PNG ---
        public static Sprite Microphone { get; private set; }
        public static Sprite Jump { get; private set; }
        public static Sprite Fight { get; private set; }
        public static Sprite Smartphone { get; private set; }
        public static Sprite Backpack { get; private set; }
        public static Sprite AnimationEmote { get; private set; }
        public static Sprite ParameterGear { get; private set; }

        // --- Identité & Téléphone PNG ---
        public static Sprite IvoryLogo { get; private set; }
        public static Sprite PhoneFrame { get; private set; }

        // --- 12 Applications du Téléphone PNG ---
        public static Sprite AppContacts { get; private set; }
        public static Sprite AppMessages { get; private set; }
        public static Sprite AppWassup { get; private set; }
        public static Sprite AppPingoin { get; private set; }
        public static Sprite AppEmploi { get; private set; }
        public static Sprite AppLogement { get; private set; }
        public static Sprite AppImmobilier { get; private set; }
        public static Sprite AppDjassa { get; private set; }
        public static Sprite AppLoto { get; private set; }
        public static Sprite AppGarage { get; private set; }
        public static Sprite AppParametres { get; private set; }
        public static Sprite AppLogout { get; private set; }
        public static Sprite AppProfil { get; private set; }
        public static Sprite AppTaxi { get; private set; }
        public static Sprite AppCommerces { get; private set; }

        // --- Fallback SDF pour les icônes Profil si la base n'est pas encore synchronisée ---
        private static Sprite s_fallbackEyeOpen;
        private static Sprite s_fallbackEyeClosed;
        private static Sprite s_fallbackLock;
        private static Sprite s_fallbackCommerce;
        private static UiIconDatabase s_database;

        // --- Chemins réservés aux futures icônes d'armes (non affichées sur le HUD actif) ---
        public const string WeaponCrosshairPath = IconsResourceRoot + "Weapons/crossair_icone";
        public const string WeaponReloadPath = IconsResourceRoot + "Weapons/reload_icone";
        public const string WeaponFirePath = IconsResourceRoot + "Weapons/fire_icone";

        public static Sprite LoadWeaponAimSprite() => Resources.Load<Sprite>(WeaponCrosshairPath);
        public static Sprite LoadWeaponReloadSprite() => Resources.Load<Sprite>(WeaponReloadPath);
        public static Sprite LoadWeaponFireSprite() => Resources.Load<Sprite>(WeaponFirePath);

        private static bool s_initialized;

        public static void EnsureInitialized()
        {
            if (s_initialized && Eco != null && Circle != null)
                return;
            s_initialized = true;
            s_database = Resources.Load<UiIconDatabase>("UI/UiIconDatabase");

            // 1. Chargement exclusif des sprites PNG utilisateur (Texture Type: Sprite 2D and UI, Single)
            IvoryLogo = LoadIcon("aceed5b4-ba84-472c-9b6d-090d5dcedf08");
            PhoneFrame = LoadIcon("phone_icone");

            Eco = LoadIcon("eco_icone");
            PingoinCoin = LoadIcon("pingoin_icone");
            Cash = LoadIcon("Cash_icone");

            Health = LoadIcon("santé_icone");
            Hunger = LoadIcon("faim_icone");
            Thirst = LoadIcon("soif icone");

            Microphone = LoadIcon("microphone_icone");
            Jump = LoadIcon("jump_icone");
            Fight = LoadIcon("fight_icone");
            Smartphone = LoadIcon("smartphone_icone");
            Backpack = LoadIcon("bag_icone");
            AnimationEmote = LoadIcon("animation icone");
            ParameterGear = LoadIcon("parameter_icone");

            AppContacts = LoadIcon("contact_icone");
            AppMessages = LoadIcon("message_icone");
            AppWassup = LoadIcon("wassup_icone");
            AppPingoin = LoadIcon("pingoin_icone");
            AppEmploi = LoadIcon("emploi_icone");
            AppLogement = LoadIcon("logement_icone");
            AppImmobilier = LoadIcon("immobilier_icone");
            AppDjassa = LoadIcon("djassa_icone");
            AppLoto = LoadIcon("loto_icone");
            AppGarage = LoadIcon("garage_icone");
            AppParametres = LoadIcon("parametre_app_icone");
            AppLogout = LoadIcon("log out_icone");
            AppProfil = BuildProfilAppSprite();
            AppTaxi = s_database != null && s_database.TaxiAppIcon != null
                ? s_database.TaxiAppIcon
                : (Resources.Load<Sprite>(IconsResourceRoot + "taxi") ?? BuildTaxiAppSprite());

            s_fallbackEyeOpen = BuildEyeSprite(closed: false);
            s_fallbackEyeClosed = BuildEyeSprite(closed: true);
            s_fallbackLock = BuildLockSprite();
            s_fallbackCommerce = BuildCommerceFallbackSprite();
            AppCommerces = s_database != null && s_database.CommerceAppIcon != null
                ? s_database.CommerceAppIcon
                : s_fallbackCommerce;

            // 2. Contours géométriques de boutons & éléments véhicule non dotés de PNG pour l'instant
            Circle = BuildSdf(128, (p) => SdfCircle(p, new Vector2(64f, 64f), 60f));
            Ring = BuildSdf(128, (p) => Mathf.Abs(SdfCircle(p, new Vector2(64f, 64f), 57f)) - 3.2f);
            ThinRing = BuildSdf(128, (p) => Mathf.Abs(SdfCircle(p, new Vector2(64f, 64f), 59f)) - 1.8f);
            RoundedBox = BuildSlicedRounded(64, 16f);
            Pill = BuildSlicedRounded(64, 28f);

            ExitDoor = BuildSdf(96, (p) =>
            {
                float frameOuter = SdfRoundBox(p, new Vector2(36f, 48f), new Vector2(18f, 28f), 4f);
                float frameInner = SdfRoundBox(p, new Vector2(38f, 48f), new Vector2(13f, 22f), 2f);
                float frame = Mathf.Max(frameOuter, -frameInner);
                float arrowGap = SdfRoundBox(p, new Vector2(54f, 48f), new Vector2(10f, 10f), 1f);
                frame = Mathf.Max(frame, -arrowGap);
                float shaft = SdfCapsule(p, new Vector2(40f, 48f), new Vector2(72f, 48f), 4f);
                float head1 = SdfCapsule(p, new Vector2(62f, 60f), new Vector2(75f, 48f), 4f);
                float head2 = SdfCapsule(p, new Vector2(62f, 36f), new Vector2(75f, 48f), 4f);
                return Mathf.Min(frame, Mathf.Min(shaft, Mathf.Min(head1, head2)));
            });

            SteerArrow = BuildSdf(96, (p) =>
                SdfTriangle(p, new Vector2(48f, 76f), new Vector2(20f, 24f), new Vector2(76f, 24f)) - 2f);

            Horn = BuildSdf(96, (p) =>
            {
                float tube = SdfCapsule(p, new Vector2(18f, 48f), new Vector2(48f, 48f), 4.5f);
                float bell = SdfTriangle(p, new Vector2(36f, 48f), new Vector2(60f, 66f), new Vector2(60f, 30f)) - 2f;
                float bulb = SdfCircle(p, new Vector2(18f, 48f), 7.5f);
                float handle = SdfCapsule(p, new Vector2(34f, 48f), new Vector2(34f, 34f), 3.5f);
                float horn = Mathf.Min(Mathf.Min(tube, bell), Mathf.Min(bulb, handle));

                Vector2 wc = new Vector2(56f, 48f);
                float ang = Mathf.Atan2(p.y - wc.y, p.x - wc.x);
                float inCone = Mathf.Abs(ang) - 0.65f;
                float w1 = Mathf.Max(Mathf.Abs(SdfCircle(p, wc, 14f)) - 2.2f, inCone);
                float w2 = Mathf.Max(Mathf.Abs(SdfCircle(p, wc, 23f)) - 2.2f, inCone);
                return Mathf.Min(horn, Mathf.Min(w1, w2));
            });

            Headlight = BuildSdf(96, (p) =>
            {
                Vector2 lc = new Vector2(52f, 48f);
                float dome = Mathf.Max(SdfCircle(p, lc, 18f), lc.x - p.x - 2f);
                float domeHollow = Mathf.Max(SdfCircle(p, lc, 13f), lc.x - p.x + 2f);
                dome = Mathf.Max(dome, -domeHollow);

                float r1 = SdfCapsule(p, new Vector2(20f, 55f), new Vector2(38f, 59f), 2.4f);
                float r2 = SdfCapsule(p, new Vector2(20f, 44f), new Vector2(38f, 48f), 2.4f);
                float r3 = SdfCapsule(p, new Vector2(20f, 33f), new Vector2(38f, 37f), 2.4f);
                return Mathf.Min(dome, Mathf.Min(r1, Mathf.Min(r2, r3)));
            });

            FuelPump = BuildSdf(64, (p) =>
            {
                float body = SdfRoundBox(p, new Vector2(28f, 30f), new Vector2(12f, 18f), 3f);
                float window = SdfRoundBox(p, new Vector2(28f, 38f), new Vector2(8f, 6f), 1.5f);
                float pump = Mathf.Max(body, -window);
                float hose = SdfCapsule(p, new Vector2(40f, 24f), new Vector2(48f, 42f), 2.2f);
                return Mathf.Min(pump, hose);
            });

            Wrench = BuildSdf(64, (p) =>
            {
                float shaft = SdfCapsule(p, new Vector2(16f, 16f), new Vector2(46f, 46f), 4.2f);
                float head = SdfCircle(p, new Vector2(46f, 46f), 10f);
                float jaw = SdfCircle(p, new Vector2(51f, 51f), 5.5f);
                return Mathf.Max(Mathf.Min(shaft, head), -jaw);
            });

            PedalGrooves = BuildSdf(128, (p) =>
            {
                float grooves = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float y = 34f + i * 20f;
                    grooves = Mathf.Min(grooves, SdfRoundBox(p, new Vector2(64f, y), new Vector2(32f, 3.5f), 2f));
                }
                return grooves;
            });
        }

        /// <summary>Génère une icône d'application « Profil » (carte indigo/bleu + silhouette blanche).</summary>
        private static Sprite BuildProfilAppSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            Vector2 center = new Vector2(64f, 64f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float cardD = SdfRoundBox(p, center, new Vector2(56f, 56f), 24f);
                    float cardA = Mathf.Clamp01(0.75f - cardD * 0.9f);
                    if (cardA <= 0.001f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float t = y / (float)size;
                    Color bg = Color.Lerp(new Color(0.08f, 0.28f, 0.62f, 1f), new Color(0.18f, 0.58f, 0.88f, 1f), t);

                    // Anneau décoratif subtil
                    float ringD = Mathf.Abs(SdfCircle(p, center, 44f)) - 1.5f;
                    float ringA = Mathf.Clamp01(0.75f - ringD * 0.9f) * 0.30f;

                    // Silhouette : tête + épaules (dans l'espace du bas Y=0)
                    float headD = SdfCircle(p, new Vector2(64f, 80f), 16f);
                    float torsoD = SdfRoundBox(p, new Vector2(64f, 40f), new Vector2(28f, 17f), 14f);
                    // Couper le bas du torse proprement
                    torsoD = Mathf.Max(torsoD, 25f - p.y);
                    float fgD = Mathf.Min(headD, torsoD);
                    float fgA = Mathf.Clamp01(0.75f - fgD * 0.9f);

                    Color c = Color.Lerp(bg, Color.white, ringA);
                    c = Color.Lerp(c, Color.white, fgA);
                    c.a = cardA;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Génère une icône d'application « Taxi » (carte ambre Abidjan + taxi stylisé).</summary>
        private static Sprite BuildTaxiAppSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            Vector2 center = new Vector2(64f, 64f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float cardD = SdfRoundBox(p, center, new Vector2(56f, 56f), 24f);
                    float cardA = Mathf.Clamp01(0.75f - cardD * 0.9f);
                    if (cardA <= 0.001f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float t = y / (float)size;
                    Color bg = Color.Lerp(new Color(0.72f, 0.36f, 0.02f, 1f), new Color(0.98f, 0.78f, 0.08f, 1f), t);

                    // Voiture vue de profil/face stylisée + enseigne TAXI sur le toit
                    float roofSignD = SdfRoundBox(p, new Vector2(64f, 84f), new Vector2(11f, 4.5f), 2f);
                    float cabinD = SdfRoundBox(p, new Vector2(64f, 69f), new Vector2(22f, 11f), 7f);
                    float bodyD = SdfRoundBox(p, new Vector2(64f, 51f), new Vector2(36f, 11f), 6f);
                    float wheelLD = SdfCircle(p, new Vector2(42f, 38f), 7.5f);
                    float wheelRD = SdfCircle(p, new Vector2(86f, 38f), 7.5f);

                    float silhouetteD = Mathf.Min(roofSignD, Mathf.Min(cabinD, bodyD));
                    float wheelsD = Mathf.Min(wheelLD, wheelRD);
                    float windowD = SdfRoundBox(p, new Vector2(64f, 69f), new Vector2(15f, 6.5f), 3f);

                    Color dark = new Color(0.11f, 0.11f, 0.13f, 1f);
                    float bodyA = Mathf.Clamp01(0.75f - silhouetteD * 0.9f);
                    float wheelA = Mathf.Clamp01(0.75f - wheelsD * 0.9f);
                    float winA = Mathf.Clamp01(0.75f - windowD * 0.9f);

                    // Damier noir sur la portière
                    bool checker = p.y >= 47f && p.y <= 53f && p.x >= 38f && p.x <= 90f && (((int)((p.x - 38f) / 6.5f)) % 2 == 0);

                    Color fg = new Color(0.14f, 0.12f, 0.08f, 1f);
                    if (checker)
                        fg = new Color(0.99f, 0.86f, 0.25f, 1f);
                    Color c = Color.Lerp(bg, fg, bodyA);
                    c = Color.Lerp(c, new Color(0.99f, 0.88f, 0.35f, 1f), winA);
                    c = Color.Lerp(c, dark, wheelA);
                    c.a = cardA;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite LoadIcon(string assetName)
        {
            Sprite sprite = Resources.Load<Sprite>(IconsResourceRoot + assetName);
            if (sprite == null)
                Debug.LogWarning("[HudIconLibrary] Sprite introuvable dans Resources/" + IconsResourceRoot + assetName);
            return sprite;
        }

        #region Contours et formes sans PNG dédié

        private delegate float SdfFunc(Vector2 p);

        private static Sprite BuildSdf(int size, SdfFunc sdf)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = sdf(new Vector2(x + 0.5f, y + 0.5f));
                    float a = Mathf.Clamp01(0.75f - d * 0.85f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite BuildSlicedRounded(int size, float cornerRadius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            Vector2 center = new Vector2(half, half);
            Vector2 extents = new Vector2(half - 2f, half - 2f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = SdfRoundBox(new Vector2(x + 0.5f, y + 0.5f), center, extents, cornerRadius);
                    float a = Mathf.Clamp01(0.75f - d * 0.9f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            int b = Mathf.Clamp(Mathf.RoundToInt(cornerRadius), 8, size / 2 - 2);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static float SdfCircle(Vector2 p, Vector2 c, float r) => Vector2.Distance(p, c) - r;

        private static float SdfRoundBox(Vector2 p, Vector2 c, Vector2 b, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - (b - new Vector2(r, r));
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float SdfSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(0.0001f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude;
        }

        private static float SdfCapsule(Vector2 p, Vector2 a, Vector2 b, float r) => SdfSegment(p, a, b) - r;

        private static float SdfTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(Vector2.Min(
                new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        public static Sprite GetConsumableSprite(string nameOrIconKey)
        {
            EnsureInitialized();
            if (s_database == null || string.IsNullOrEmpty(nameOrIconKey))
                return Backpack;

            if (s_database.TryGetConsumable(nameOrIconKey, out Sprite direct))
                return direct;

            string key = ConsumableCatalog.ResolveIconKey(nameOrIconKey);
            if (!string.IsNullOrEmpty(key) && s_database.TryGetConsumable(key, out Sprite resolved))
                return resolved;

            return Backpack;
        }

        public static Sprite GetProfileIcon(string key)
        {
            EnsureInitialized();
            if (s_database != null && s_database.TryGetProfileIcon(key, out Sprite sprite))
                return sprite;

            return key switch
            {
                "icon_eye_open" => s_fallbackEyeOpen,
                "icon_eye_closed" => s_fallbackEyeClosed,
                "icon_lock" => s_fallbackLock,
                "icon_commerce" => s_fallbackCommerce,
                "icon_taxi" => AppTaxi,
                "icon_voiture" => AppGarage,
                "icon_maison" => AppImmobilier,
                "icon_residence_pin" => AppLogement,
                _ => Circle,
            };
        }

        public static Sprite GetProfileBackground(PlayerJobStatus status)
        {
            EnsureInitialized();
            if (s_database == null)
                return null;
            ProfileStatusVisual meta = PlayerProfileMetaCatalog.Get(status);
            return s_database.TryGetProfileBackground(meta.BackgroundKey, out Sprite sprite) ? sprite : null;
        }

        private static Sprite BuildEyeSprite(bool closed)
        {
            return BuildSdf(96, p =>
            {
                Vector2 c = new Vector2(48f, 48f);
                float upper = SdfCircle(p, new Vector2(48f, 20f), 42f);
                float lower = SdfCircle(p, new Vector2(48f, 76f), 42f);
                float almond = Mathf.Max(upper, lower);
                float outline = Mathf.Abs(almond) - 3.2f;
                float pupil = SdfCircle(p, c, 10f);
                float d = Mathf.Min(outline, pupil);
                if (closed)
                {
                    float slash = SdfCapsule(p, new Vector2(18f, 18f), new Vector2(78f, 78f), 4f);
                    d = Mathf.Min(d, slash);
                }
                return d;
            });
        }

        private static Sprite BuildLockSprite()
        {
            return BuildSdf(96, p =>
            {
                float body = SdfRoundBox(p, new Vector2(48f, 34f), new Vector2(24f, 18f), 6f);
                float shackleOuter = Mathf.Max(SdfCircle(p, new Vector2(48f, 54f), 17f), 50f - p.y);
                float shackleInner = SdfCircle(p, new Vector2(48f, 54f), 11f);
                float shackle = Mathf.Max(shackleOuter, -shackleInner);
                float legs = Mathf.Min(
                    SdfCapsule(p, new Vector2(34f, 44f), new Vector2(34f, 55f), 3f),
                    SdfCapsule(p, new Vector2(62f, 44f), new Vector2(62f, 55f), 3f));
                float keyhole = Mathf.Min(
                    SdfCircle(p, new Vector2(48f, 37f), 4.5f),
                    SdfCapsule(p, new Vector2(48f, 27f), new Vector2(48f, 36f), 2.5f));
                return Mathf.Max(Mathf.Min(body, Mathf.Min(shackle, legs)), -keyhole);
            });
        }

        private static Sprite BuildCommerceFallbackSprite()
        {
            return BuildSdf(96, p =>
            {
                float roof = SdfRoundBox(p, new Vector2(48f, 66f), new Vector2(30f, 10f), 4f);
                float walls = SdfRoundBox(p, new Vector2(48f, 36f), new Vector2(25f, 18f), 3f);
                float door = SdfRoundBox(p, new Vector2(40f, 30f), new Vector2(6f, 11f), 1.5f);
                float win = SdfRoundBox(p, new Vector2(58f, 36f), new Vector2(7f, 6f), 1.5f);
                return Mathf.Max(Mathf.Min(roof, walls), -Mathf.Min(door, win));
            });
        }

        #endregion
    }
}
