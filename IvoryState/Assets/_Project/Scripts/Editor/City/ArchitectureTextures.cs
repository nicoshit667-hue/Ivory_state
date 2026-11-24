using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IvoryState.Editor.City
{
    /// <summary>
    /// Génère et importe les textures architecturales procédurales (albédo + normales OpenGL)
    /// pour les édifices civiques, religieux et hospitaliers d'Abidjan.
    /// </summary>
    public static class ArchitectureTextures
    {
        public const string Folder = "Assets/_Project/Art/Textures/Architecture";
        private const string VersionStampFile = "arch_textures_v2.stamp";
        private const int TexSize = 512;

        public static void EnsureGenerated(bool forceRegenerate = false)
        {
            EnsureFolder(Folder);
            string diskFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Folder));
            Directory.CreateDirectory(diskFolder);
            string stampPath = Path.Combine(diskFolder, VersionStampFile);

            bool anyWritten = false;
            bool needAll = forceRegenerate || !File.Exists(stampPath);

            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_BrushedGold", needAll, TexSize, 1.4f, SampleBrushedGold);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_TapisRouge", needAll, TexSize, 2.2f, SampleTapisRouge);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_WaterRipple", needAll, TexSize, 2.6f, SampleWaterRipple);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_ZelligeBlue", needAll, TexSize, 3.0f, SampleZelligeBlue);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PrayerCarpet", needAll, TexSize, 2.4f, SamplePrayerCarpet);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PierreOcre", needAll, TexSize, 2.8f, SamplePierreOcre);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_BrushedMetal", needAll, 256, 1.1f, SampleBrushedMetal, albedoToo: false);

            // Surfaces architecturales claires (enduits blancs/crème, marbre veiné, carrelages, terrasses, pavés, parquet, sports)
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_MurBlanc", needAll, TexSize, 0.45f, SampleMurBlanc);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_MurCreme", needAll, TexSize, 0.35f, SampleMurCreme);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_MarbreBlanc", needAll, TexSize, 0.55f, SampleMarbreBlanc);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_CarrelageClair", needAll, TexSize, 0.85f, SampleCarrelageClair);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_TerrasseClair", needAll, TexSize, 1.05f, SampleTerrasseClair);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PavesClairs", needAll, TexSize, 1.55f, SamplePavesClairs);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_ParquetChaud", needAll, TexSize, 0.90f, SampleParquetChaud);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PiscineMosaic", needAll, TexSize, 0.70f, SamplePiscineMosaic);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PelouseStade", needAll, TexSize, 0.55f, SamplePelouseStade);
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_PisteAthletisme", needAll, TexSize, 0.75f, SamplePisteAthletisme);

            // Vitraux (3 variantes chromatiques partageant le relief plombé)
            anyWritten |= BuildHeightPair(diskFolder, "T_Arch_Vitrail_Bleu", needAll, TexSize, 3.4f,
                (u, v) => SampleStainedGlass(u, v, new Color(0.14f, 0.38f, 0.88f), new Color(0.18f, 0.68f, 0.90f), new Color(0.92f, 0.72f, 0.22f)));
            anyWritten |= BuildSingleAlbedo(diskFolder, "T_Arch_Vitrail_Or_diff.png", needAll, TexSize,
                (u, v) => SampleStainedGlass(u, v, new Color(0.94f, 0.68f, 0.14f), new Color(0.98f, 0.84f, 0.36f), new Color(0.84f, 0.26f, 0.16f)).albedo);
            anyWritten |= BuildSingleAlbedo(diskFolder, "T_Arch_Vitrail_Rubis_diff.png", needAll, TexSize,
                (u, v) => SampleStainedGlass(u, v, new Color(0.82f, 0.12f, 0.22f), new Color(0.94f, 0.32f, 0.28f), new Color(0.92f, 0.70f, 0.18f)).albedo);

            // Masques émissifs (écrans PC/CCTV/médicaux + lentilles gyrophares)
            anyWritten |= BuildSingleAlbedo(diskFolder, "T_Arch_ScreenUI_diff.png", needAll, 256, SampleScreenUi);
            anyWritten |= BuildSingleAlbedo(diskFolder, "T_Arch_BeaconLens_diff.png", needAll, 256, SampleBeaconLens);

            if (needAll)
                File.WriteAllText(stampPath, "v2\n");

            if (anyWritten)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static Texture2D Albedo(string prefixOrFile, int aniso = 4)
        {
            string file = prefixOrFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? prefixOrFile
                : prefixOrFile + "_diff.png";
            return CityMaterials.LoadTexture(Folder + "/" + file, false, aniso, 1024);
        }

        public static Texture2D Normal(string prefixOrFile, int aniso = 4)
        {
            string file = prefixOrFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? prefixOrFile
                : prefixOrFile + "_nor.png";
            return CityMaterials.LoadTexture(Folder + "/" + file, true, aniso, 1024);
        }

        #region Génération PNG & normales

        private struct Sample
        {
            public Color albedo;
            public float height;

            public Sample(Color a, float h)
            {
                albedo = a;
                height = h;
            }
        }

        private static bool BuildHeightPair(string diskFolder, string prefix, bool force, int size, float bumpStrength,
            Func<float, float, Sample> sampler, bool albedoToo = true)
        {
            string diffPath = Path.Combine(diskFolder, prefix + "_diff.png");
            string norPath = Path.Combine(diskFolder, prefix + "_nor.png");
            bool needDiff = albedoToo && (force || !File.Exists(diffPath));
            bool needNor = force || !File.Exists(norPath);
            if (!needDiff && !needNor)
                return false;

            float[] heights = new float[size * size];
            Color32[] diffPixels = needDiff ? new Color32[size * size] : null;
            float inv = 1f / size;

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) * inv;
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * inv;
                    Sample s = sampler(u, v);
                    heights[row + x] = s.height;
                    if (diffPixels != null)
                        diffPixels[row + x] = ToColor32(s.albedo);
                }
            }

            if (needDiff)
                WritePng(diffPath, size, diffPixels);

            if (needNor)
            {
                Color32[] norPixels = ComputeNormalMap(heights, size, bumpStrength);
                WritePng(norPath, size, norPixels);
            }

            return true;
        }

        private static bool BuildSingleAlbedo(string diskFolder, string fileName, bool force, int size, Func<float, float, Color> sampler)
        {
            string path = Path.Combine(diskFolder, fileName);
            if (!force && File.Exists(path))
                return false;

            Color32[] pixels = new Color32[size * size];
            float inv = 1f / size;
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) * inv;
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * inv;
                    pixels[row + x] = ToColor32(sampler(u, v));
                }
            }
            WritePng(path, size, pixels);
            return true;
        }

        private static Color32[] ComputeNormalMap(float[] h, int size, float strength)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int ym = (y - 1 + size) % size;
                int yp = (y + 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int xm = (x - 1 + size) % size;
                    int xp = (x + 1) % size;

                    float tl = h[yp * size + xm];
                    float t  = h[yp * size + x];
                    float tr = h[yp * size + xp];
                    float l  = h[y * size + xm];
                    float r  = h[y * size + xp];
                    float bl = h[ym * size + xm];
                    float b  = h[ym * size + x];
                    float br = h[ym * size + xp];

                    float dx = (tr + 2f * r + br - tl - 2f * l - bl) * strength;
                    float dy = (tl + 2f * t + tr - bl - 2f * b - br) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255),
                        255);
                }
            }
            return pixels;
        }

        private static void WritePng(string path, int size, Color32[] pixels)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static Color32 ToColor32(Color c)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f), 0, 255));
        }

        #endregion

        #region Bruit cohérent périodique

        private static float Hash2D(int ix, int iy, int seed)
        {
            unchecked
            {
                uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + (uint)seed * 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float TileValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period;
            float y = v * period;
            int x0 = Mathf.FloorToInt(x) % period;
            int y0 = Mathf.FloorToInt(y) % period;
            if (x0 < 0) x0 += period;
            if (y0 < 0) y0 += period;
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;
            float fx = x - Mathf.Floor(x);
            float fy = y - Mathf.Floor(y);
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = Hash2D(x0, y0, seed);
            float b = Hash2D(x1, y0, seed);
            float c = Hash2D(x0, y1, seed);
            float d = Hash2D(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float FbmTile(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float norm = 0f;
            int p = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * TileValueNoise(u, v, p, seed + i * 97);
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        #endregion

        #region Échantillonneurs architecturaux

        private static Sample SampleBrushedGold(float u, float v)
        {
            float streak = TileValueNoise(u * 0.25f, v, 48, 11) * 0.6f + TileValueNoise(u, v, 96, 17) * 0.4f;
            float warm = FbmTile(u, v, 8, 3, 23);
            Color baseGold = new Color(0.89f, 0.71f, 0.24f);
            Color hiGold = new Color(0.96f, 0.80f, 0.34f);
            Color col = Color.Lerp(baseGold, hiGold, streak * 0.55f + warm * 0.25f);
            return new Sample(col, 0.5f + (streak - 0.5f) * 0.25f);
        }

        private static Sample SampleBrushedMetal(float u, float v)
        {
            float streak = TileValueNoise(u * 0.25f, v, 48, 31) * 0.7f + TileValueNoise(u, v, 64, 37) * 0.3f;
            float g = 0.5f + (streak - 0.5f) * 0.35f;
            return new Sample(new Color(g, g, g), g);
        }

        private static Sample SampleTapisRouge(float u, float v)
        {
            // Damas royal sans direction : treillis diagonal + médaillon central discret
            float tu = Mathf.Repeat(u * 4f, 1f) - 0.5f;
            float tv = Mathf.Repeat(v * 4f, 1f) - 0.5f;
            float d1 = Mathf.Abs(tu) + Mathf.Abs(tv);
            float d2 = Mathf.Sqrt(tu * tu + tv * tv);
            float damask = SmoothBand(d1, 0.44f, 0.045f) + 0.7f * SmoothBand(d2, 0.16f, 0.035f);

            float weave = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 128f) * Mathf.Sin(v * Mathf.PI * 128f);
            float noise = FbmTile(u, v, 16, 3, 41);

            Color crimson = new Color(0.56f, 0.06f, 0.08f);
            Color deepCrimson = new Color(0.42f, 0.04f, 0.06f);
            Color goldThread = new Color(0.74f, 0.48f, 0.14f);

            Color baseCol = Color.Lerp(deepCrimson, crimson, 0.45f + 0.4f * noise + 0.15f * weave);
            Color col = Color.Lerp(baseCol, goldThread, Mathf.Clamp01(damask) * 0.28f);
            float h = 0.42f + 0.22f * weave + 0.18f * noise + 0.18f * Mathf.Clamp01(damask);
            return new Sample(col, h);
        }

        private static Sample SampleWaterRipple(float u, float v)
        {
            float tau = Mathf.PI * 2f;
            float w1 = Mathf.Sin((u * 3f + v * 2f) * tau);
            float w2 = Mathf.Sin((u * -2f + v * 4f) * tau + 1.2f);
            float w3 = Mathf.Sin((u * 5f + v * 5f) * tau * 0.8f);
            float n = FbmTile(u, v, 8, 3, 53);
            float ripple = (w1 * 0.45f + w2 * 0.35f + w3 * 0.20f) * 0.5f + 0.5f;
            float caustic = Mathf.Pow(Mathf.Clamp01(ripple * 0.65f + n * 0.35f), 2.2f);

            Color deepLagoon = new Color(0.11f, 0.46f, 0.64f, 0.80f);
            Color foamHighlight = new Color(0.32f, 0.78f, 0.90f, 0.74f);
            Color col = Color.Lerp(deepLagoon, foamHighlight, caustic * 0.55f);
            return new Sample(col, ripple * 0.7f + n * 0.3f);
        }

        private static Sample SampleStainedGlass(float u, float v, Color cMain, Color cAccent, Color cHighlight)
        {
            const int cols = 4;
            const int rows = 8;
            float gx = u * cols;
            float gy = v * rows;
            int ix = Mathf.FloorToInt(gx) % cols;
            int iy = Mathf.FloorToInt(gy) % rows;
            float lx = gx - Mathf.Floor(gx);
            float ly = gy - Mathf.Floor(gy);

            // Cadrillage losange + bordures plombées
            float diag1 = Mathf.Abs((lx - 0.5f) + (ly - 0.5f));
            float diag2 = Mathf.Abs((lx - 0.5f) - (ly - 0.5f));
            float border = Mathf.Min(Mathf.Min(lx, 1f - lx), Mathf.Min(ly, 1f - ly));
            float diagDist = Mathf.Min(Mathf.Abs(diag1 - 0.5f), Mathf.Abs(diag2 - 0.5f));
            bool isLead = border < 0.045f || diagDist < 0.032f;

            float cellRnd = Hash2D(ix + (diag1 < 0.5f ? 17 : 0), iy + (diag2 < 0.5f ? 31 : 0), 67);
            float ripple = FbmTile(u, v, 16, 2, 71);
            Color glass = cellRnd > 0.78f ? cHighlight : (cellRnd > 0.38f ? cAccent : cMain);
            glass *= 0.85f + 0.25f * ripple;
            glass.a = 0.64f;

            if (isLead)
                return new Sample(new Color(0.10f, 0.11f, 0.13f, 0.96f), 0.92f);

            float bevel = Mathf.Clamp01(Mathf.Min(border, diagDist) * 4f);
            return new Sample(glass, 0.25f + 0.25f * bevel + 0.10f * ripple);
        }

        private static Sample SampleZelligeBlue(float u, float v)
        {
            const int cells = 6;
            float gx = u * cells;
            float gy = v * cells;
            int ix = Mathf.FloorToInt(gx);
            int iy = Mathf.FloorToInt(gy);
            float lx = (gx - ix) - 0.5f;
            float ly = (gy - iy) - 0.5f;
            float ax = Mathf.Abs(lx);
            float ay = Mathf.Abs(ly);

            // Étoile à 8 branches (deux carrés tournés à 45°)
            float s1 = Mathf.Max(ax, ay);
            float s2 = (ax + ay) * 0.7071f;
            float star = Mathf.Min(s1, s2);
            float dJoin = Mathf.Min(Mathf.Abs(star - 0.26f), Mathf.Min(Mathf.Abs(s1 - 0.44f), Mathf.Abs(s2 - 0.46f)));
            bool isGrout = dJoin < 0.022f || Mathf.Abs(lx) > 0.482f || Mathf.Abs(ly) > 0.482f;

            if (isGrout)
                return new Sample(new Color(0.84f, 0.82f, 0.77f), 0.10f);

            float glossNoise = FbmTile(u, v, 12, 2, 83);
            Color cobalt = new Color(0.08f, 0.30f, 0.74f);
            Color turquoise = new Color(0.10f, 0.58f, 0.76f);
            Color cream = new Color(0.93f, 0.91f, 0.85f);
            Color goldAccent = new Color(0.86f, 0.68f, 0.24f);

            Color col;
            if (star < 0.14f)
                col = goldAccent;
            else if (star < 0.26f)
                col = turquoise;
            else if ((ix + iy) % 2 == 0 && s1 < 0.44f)
                col = cobalt;
            else
                col = cream;

            col *= 0.92f + 0.12f * glossNoise;
            return new Sample(col, 0.82f + 0.08f * glossNoise);
        }

        private static Sample SamplePrayerCarpet(float u, float v)
        {
            // Lignes de prière horizontales (saff) + motifs géométriques discrets
            float rowPos = Mathf.Repeat(v * 2f, 1f);
            float cellU = Mathf.Repeat(u * 3f, 1f) - 0.5f;
            bool isLineBand = rowPos < 0.065f || rowPos > 0.965f;
            float arch = 0.72f - 0.45f * (cellU * cellU * 4f);
            bool isMihrabTrim = Mathf.Abs(rowPos - arch) < 0.024f;

            float pile = FbmTile(u, v, 32, 3, 97);
            float thread = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 160f) * Mathf.Sin(v * Mathf.PI * 160f);

            Color emerald = new Color(0.08f, 0.42f, 0.25f);
            Color deepGreen = new Color(0.05f, 0.32f, 0.19f);
            Color goldTrim = new Color(0.85f, 0.69f, 0.28f);

            Color baseCol = Color.Lerp(deepGreen, emerald, 0.4f + 0.45f * pile + 0.15f * thread);
            if (isLineBand)
                baseCol = Color.Lerp(baseCol, goldTrim, 0.82f);
            else if (isMihrabTrim)
                baseCol = Color.Lerp(baseCol, goldTrim, 0.65f);

            return new Sample(baseCol, 0.45f + 0.30f * thread + 0.25f * pile);
        }

        private static Sample SamplePierreOcre(float u, float v)
        {
            const int courses = 8;
            const int stonesPerRow = 4;
            float fy = v * courses;
            int row = Mathf.FloorToInt(fy) % courses;
            float offset = (row % 2) * 0.5f;
            float fx = u * stonesPerRow + offset;
            int col = Mathf.FloorToInt(fx) % stonesPerRow;

            float ly = fy - Mathf.Floor(fy);
            float lx = fx - Mathf.Floor(fx);
            bool mortar = ly < 0.045f || lx < 0.028f;

            float grain = FbmTile(u, v, 16, 4, 109);
            float tintVar = Hash2D(col, row, 113);

            if (mortar)
                return new Sample(new Color(0.66f, 0.56f, 0.41f), 0.12f);

            Color warmOcre = new Color(0.84f, 0.70f, 0.48f);
            Color paleSand = new Color(0.90f, 0.79f, 0.59f);
            Color c = Color.Lerp(warmOcre, paleSand, 0.35f * tintVar + 0.65f * grain);
            float bevel = Mathf.Clamp01(Mathf.Min(ly - 0.045f, lx - 0.028f) * 8f);
            return new Sample(c, 0.45f + 0.35f * bevel + 0.20f * grain);
        }

        private static Color SampleScreenUi(float u, float v)
        {
            // Interface radar / grille télémétrie lumineuse (NightLight _MainTex utilise canal R)
            bool outerFrame = u < 0.06f || u > 0.94f || v < 0.08f || v > 0.92f;
            if (outerFrame)
                return new Color(0.28f, 0.28f, 0.28f, 1f);

            float dx = u - 0.34f;
            float dy = v - 0.52f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ring = SmoothBand(r, 0.18f, 0.015f) + SmoothBand(r, 0.10f, 0.015f);
            float grid = (Mathf.Repeat(u * 12f, 1f) < 0.08f || Mathf.Repeat(v * 8f, 1f) < 0.08f) ? 0.35f : 0f;
            bool rightBars = u > 0.62f && u < 0.90f && Mathf.Repeat(v * 9f, 1f) > 0.45f && v > 0.18f && v < 0.84f;
            bool scan = Mathf.Abs(v - 0.52f) < 0.014f && u > 0.12f && u < 0.56f;

            float val = Mathf.Clamp01(0.18f + grid + ring * 0.75f + (rightBars ? 0.65f : 0f) + (scan ? 0.85f : 0f));
            return new Color(val, val, val, 1f);
        }

        private static Color SampleBeaconLens(float u, float v)
        {
            // Lentille de gyrophare striée Fresnel
            float stripes = 0.70f + 0.30f * Mathf.Sin(v * Mathf.PI * 16f);
            float ribs = 0.82f + 0.18f * Mathf.Cos(u * Mathf.PI * 24f);
            float val = Mathf.Clamp01(stripes * ribs);
            return new Color(val, val, val, 1f);
        }

        private static Sample SampleMurBlanc(float u, float v)
        {
            float grain = FbmTile(u, v, 8, 4, 211);
            float fine = TileValueNoise(u, v, 32, 223);
            Color warmLow = new Color(0.945f, 0.935f, 0.915f);
            Color crispHigh = new Color(0.988f, 0.982f, 0.968f);
            Color c = Color.Lerp(warmLow, crispHigh, 0.65f * grain + 0.35f * fine);
            return new Sample(c, 0.45f + 0.35f * grain + 0.20f * fine);
        }

        private static Sample SampleMurCreme(float u, float v)
        {
            float grain = FbmTile(u, v, 8, 4, 239);
            float fine = TileValueNoise(u, v, 24, 241);
            Color ivoryLow = new Color(0.948f, 0.926f, 0.882f);
            Color ivoryHigh = new Color(0.988f, 0.976f, 0.948f);
            Color c = Color.Lerp(ivoryLow, ivoryHigh, 0.70f * grain + 0.30f * fine);
            return new Sample(c, 0.50f + 0.30f * grain + 0.15f * fine);
        }

        private static Sample SampleMarbreBlanc(float u, float v)
        {
            const int tiles = 4;
            float fx = u * tiles;
            float fy = v * tiles;
            int col = Mathf.FloorToInt(fx) % tiles;
            int row = Mathf.FloorToInt(fy) % tiles;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (lx < 0.016f || ly < 0.016f)
                return new Sample(new Color(0.84f, 0.82f, 0.79f), 0.14f);

            float ox = Hash2D(col, row, 251) * 0.73f;
            float oy = Hash2D(col, row, 257) * 0.61f;
            float warp = FbmTile(u + ox, v + oy, 6, 3, 263);
            float cloud = FbmTile(u + ox, v + oy, 8, 3, 269);
            float veinDiag = Mathf.Repeat(lx * 0.65f + ly * 0.45f + 0.65f * warp, 1f);
            float vein = SmoothBand(veinDiag, 0.50f, 0.15f) * 0.65f;

            Color pearl = new Color(0.935f, 0.922f, 0.898f);
            Color pureIvory = new Color(0.978f, 0.972f, 0.958f);
            Color veinTone = new Color(0.845f, 0.822f, 0.785f);
            Color c = Color.Lerp(Color.Lerp(pearl, pureIvory, cloud), veinTone, vein);
            return new Sample(c, 0.58f + 0.18f * cloud - 0.10f * vein);
        }

        private static Sample SampleCarrelageClair(float u, float v)
        {
            const int tiles = 4;
            float fx = u * tiles;
            float fy = v * tiles;
            int col = Mathf.FloorToInt(fx) % tiles;
            int row = Mathf.FloorToInt(fy) % tiles;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (lx < 0.022f || ly < 0.022f)
                return new Sample(new Color(0.79f, 0.76f, 0.72f), 0.14f);

            float grain = FbmTile(u, v, 12, 3, 271);
            float tileVar = Hash2D(col, row, 277);
            Color warmStone = new Color(0.885f, 0.858f, 0.816f);
            Color lightCream = new Color(0.938f, 0.918f, 0.884f);
            Color c = Color.Lerp(warmStone, lightCream, 0.35f * tileVar + 0.65f * grain);
            float bevel = Mathf.Clamp01(Mathf.Min(lx - 0.022f, ly - 0.022f) * 14f);
            return new Sample(c, 0.48f + 0.34f * bevel + 0.14f * grain);
        }

        private static Sample SampleTerrasseClair(float u, float v)
        {
            const int rows = 4;
            const int cols = 2;
            float fy = v * rows;
            int row = Mathf.FloorToInt(fy) % rows;
            float fx = u * cols + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(fx) % cols;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (lx < 0.024f || ly < 0.028f)
                return new Sample(new Color(0.73f, 0.66f, 0.57f), 0.12f);

            float grain = FbmTile(u, v, 12, 4, 281);
            float tileVar = Hash2D(col, row, 283);
            Color travLow = new Color(0.848f, 0.772f, 0.672f);
            Color travHigh = new Color(0.922f, 0.860f, 0.770f);
            Color c = Color.Lerp(travLow, travHigh, 0.30f * tileVar + 0.70f * grain);
            float bevel = Mathf.Clamp01(Mathf.Min(lx - 0.024f, ly - 0.028f) * 12f);
            return new Sample(c, 0.46f + 0.32f * bevel + 0.18f * grain);
        }

        private static Sample SamplePavesClairs(float u, float v)
        {
            const int rows = 8;
            const int cols = 4;
            float fy = v * rows;
            int row = Mathf.FloorToInt(fy) % rows;
            float fx = u * cols + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(fx) % cols;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (lx < 0.032f || ly < 0.048f)
                return new Sample(new Color(0.65f, 0.63f, 0.59f), 0.10f);

            float grain = FbmTile(u, v, 16, 4, 293);
            float tileVar = Hash2D(col, row, 307);
            Color stoneA = new Color(0.805f, 0.785f, 0.752f);
            Color stoneB = new Color(0.888f, 0.870f, 0.840f);
            Color c = Color.Lerp(stoneA, stoneB, 0.45f * tileVar + 0.55f * grain);
            float bevel = Mathf.Clamp01(Mathf.Min(lx - 0.032f, ly - 0.048f) * 10f);
            return new Sample(c, 0.44f + 0.38f * bevel + 0.18f * grain);
        }

        private static Sample SampleParquetChaud(float u, float v)
        {
            const int planks = 8;
            float fy = v * planks;
            int row = Mathf.FloorToInt(fy) % planks;
            float fx = u * 2f + Hash2D(row, 0, 311);
            int col = Mathf.FloorToInt(fx) % 2;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (ly < 0.032f || lx < 0.012f)
                return new Sample(new Color(0.38f, 0.25f, 0.14f), 0.16f);

            float streak = FbmTile(u * 2f, v * 16f, 8, 3, 313 + row * 17);
            float plankVar = Hash2D(col, row, 317);
            Color irokoRich = new Color(0.66f, 0.44f, 0.25f);
            Color irokoHoney = new Color(0.83f, 0.62f, 0.39f);
            Color c = Color.Lerp(irokoRich, irokoHoney, 0.35f * plankVar + 0.65f * streak);
            return new Sample(c, 0.52f + 0.28f * streak);
        }

        private static Sample SamplePiscineMosaic(float u, float v)
        {
            const int tiles = 16;
            float fx = u * tiles;
            float fy = v * tiles;
            int col = Mathf.FloorToInt(fx) % tiles;
            int row = Mathf.FloorToInt(fy) % tiles;
            float lx = fx - Mathf.Floor(fx);
            float ly = fy - Mathf.Floor(fy);
            if (lx < 0.08f || ly < 0.08f)
                return new Sample(new Color(0.84f, 0.94f, 0.98f), 0.18f);

            float tVar = Hash2D(col, row, 331);
            Color deepAzure = new Color(0.08f, 0.50f, 0.80f);
            Color aqua = new Color(0.18f, 0.72f, 0.88f);
            Color c = Color.Lerp(deepAzure, aqua, tVar);
            return new Sample(c, 0.68f + 0.14f * tVar);
        }

        private static Sample SamplePelouseStade(float u, float v)
        {
            int stripe = Mathf.FloorToInt(u * 8f) % 2;
            float fine = FbmTile(u, v, 16, 4, 337);
            Color emerald = new Color(0.12f, 0.50f, 0.18f);
            Color jade = new Color(0.18f, 0.60f, 0.23f);
            Color baseC = stripe == 0 ? emerald : jade;
            Color c = Color.Lerp(baseC * 0.90f, baseC * 1.10f, fine);
            return new Sample(c, 0.42f + 0.45f * fine);
        }

        private static Sample SamplePisteAthletisme(float u, float v)
        {
            float fine = FbmTile(u, v, 24, 3, 347);
            float laneCoord = Mathf.Repeat(v * 6f, 1f);
            if (laneCoord < 0.045f)
                return new Sample(new Color(0.95f, 0.94f, 0.92f), 0.65f);

            Color tartanDark = new Color(0.68f, 0.22f, 0.17f);
            Color tartanBright = new Color(0.79f, 0.28f, 0.21f);
            return new Sample(Color.Lerp(tartanDark, tartanBright, fine), 0.45f + 0.40f * fine);
        }

        private static float SmoothBand(float value, float center, float halfWidth)
        {
            float d = Mathf.Abs(value - center);
            return d >= halfWidth ? 0f : 1f - d / halfWidth;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        #endregion
    }
}
