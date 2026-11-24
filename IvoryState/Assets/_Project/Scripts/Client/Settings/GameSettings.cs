using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IvoryState.Client.Settings
{
    /// <summary>Préréglages de qualité graphique.</summary>
    public enum QualityPreset : byte
    {
        /// <summary>Très bas : téléphones entrée de gamme (Android 8, 2 Go de RAM).</summary>
        VeryLow = 0,
        /// <summary>Équilibré : réglage par défaut, bon compromis visuel / fluidité.</summary>
        Balanced = 1,
        /// <summary>Élevé : téléphones haut de gamme, tablettes, PC.</summary>
        High = 2,
        /// <summary>Réglages manuels.</summary>
        Custom = 255,
    }

    /// <summary>Identifiants des touches et éléments tactiles repositionnables à l'écran.</summary>
    public enum HudControlId : byte
    {
        // À pied
        Joystick = 0,
        Jump = 1,
        Punch = 2,
        Mic = 3,
        Rob = 4,
        Interact = 5,
        BottomDock = 6,

        // En véhicule
        SteerLeft = 7,
        SteerRight = 8,
        GazPedal = 9,
        BrakeButton = 10,
        KlaxonButton = 11,
        PharesButton = 12,
        Speedometer = 13,
    }

    /// <summary>Position normalisée (0..1) et échelle (0,7..1,5) d'une touche tactile personnalisée.</summary>
    public struct HudControlLayout
    {
        public bool Customized;
        public float NormX;
        public float NormY;
        public float Scale;
    }

    /// <summary>
    /// Réglages de jeu centralisés, sauvegardés dans <c>PlayerPrefs</c>. Source unique de vérité
    /// pour la résolution, la distance d'affichage, les ombres, le FPS cible, le nombre de
    /// passants et les préférences de contrôle. Les consommateurs lisent les propriétés statiques ;
    /// toute modification passe par les méthodes <c>Set*()</c>, puis <see cref="Apply"/> les
    /// projette sur les API Unity (URP, caméra, brume, audio…).
    /// <para>
    /// Brume synchronisée : le début et la fin de la brume suivent la distance d'affichage,
    /// de sorte que les objets fondent progressivement au lieu d'apparaître d'un coup ou de
    /// disparaître derrière un mur de brouillard.
    /// </para>
    /// </summary>
    public static class GameSettings
    {
        // ────────────── Événements ──────────────

        /// <summary>Levé après chaque <see cref="Apply"/> réussi.</summary>
        public static event Action Changed;

        // ────────────── Qualité graphique ──────────────

        public static QualityPreset Preset { get; private set; } = QualityPreset.Balanced;

        /// <summary>Résolution de rendu (0,5 = moitié, 1,0 = native). Par défaut 0,8.</summary>
        public static float RenderScale { get; private set; } = 0.8f;

        /// <summary>Distance d'affichage en mètres (120–600). Pilote farClip, brume et LOD.</summary>
        public static float DrawDistance { get; private set; } = 300f;

        /// <summary>0 = ombres désactivées, 1 = basses, 2 = normales.</summary>
        public static int ShadowMode { get; private set; } = 1;

        /// <summary>Effets visuels : bloom, anti-aliasing FXAA.</summary>
        public static bool PostProcessing { get; private set; }

        /// <summary>Limite de FPS (30, 45, 60, 90, 120).</summary>
        public static int TargetFps { get; private set; } = 60;

        /// <summary>Nombre de passants d'ambiance (0, 6, 12, 20, 30).</summary>
        public static int PedestrianCount { get; private set; } = 12;

        // ────────────── Général & Contrôles ──────────────

        /// <summary>Affiche les pseudos au-dessus des joueurs.</summary>
        public static bool ShowNameTags { get; private set; } = true;

        /// <summary>Sensibilité de la caméra (0,5–2,0).</summary>
        public static float CameraSensitivity { get; private set; } = 1f;

        /// <summary>Joystick fixe (vrai) ou flottant sous le pouce (faux).</summary>
        public static bool FixedJoystick { get; private set; }

        /// <summary>Échelle du HUD (0,8–1,3).</summary>
        public static float HudScale { get; private set; } = 1f;

        private static readonly Dictionary<HudControlId, HudControlLayout> s_hudLayouts =
            new Dictionary<HudControlId, HudControlLayout>();

        /// <summary>Nombre de touches actuellement personnalisées.</summary>
        public static int CustomHudControlCount => s_hudLayouts.Count;

        // ────────────── Audio ──────────────

        /// <summary>Volume général (0–1).</summary>
        public static float MasterVolume { get; private set; } = 1f;

        /// <summary>Index de la sonnerie du téléphone (0 = par défaut).</summary>
        public static int RingtoneIndex { get; private set; }

        // ────────────── Propriétés dérivées ──────────────

        /// <summary>Far clip extérieur : un peu au-delà de la brume pour éviter le tranchage net.</summary>
        public static float OutdoorFarClip => DrawDistance + 30f;

        /// <summary>Début de la brume linéaire.</summary>
        public static float FogStart => Mathf.Max(20f, DrawDistance * 0.15f);

        /// <summary>Fin de la brume (objets totalement masqués).</summary>
        public static float FogEnd => DrawDistance * 0.93f;

        /// <summary>LOD bias ajusté à la distance d'affichage.</summary>
        public static float LodBias => DrawDistance > 300f ? 1.5f : (DrawDistance > 180f ? 1f : 0.7f);

        // ────────────── Persistance ──────────────

        private const string P = "GS_";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoLoad()
        {
            Load();
        }

        /// <summary>Charge les réglages sauvegardés ou applique <see cref="QualityPreset.Balanced"/>.</summary>
        public static void Load()
        {
            if (!PlayerPrefs.HasKey(P + "v"))
            {
                ApplyPreset(QualityPreset.Balanced);
                return;
            }

            Preset          = (QualityPreset)PlayerPrefs.GetInt(P + "preset", 1);
            RenderScale     = PlayerPrefs.GetFloat(P + "rs", 0.8f);
            DrawDistance     = PlayerPrefs.GetFloat(P + "dd", 300f);
            ShadowMode      = PlayerPrefs.GetInt(P + "shd", 1);
            PostProcessing  = PlayerPrefs.GetInt(P + "pp", 0) == 1;
            TargetFps       = PlayerPrefs.GetInt(P + "fps", 60);
            PedestrianCount = PlayerPrefs.GetInt(P + "ped", 12);
            CameraSensitivity = PlayerPrefs.GetFloat(P + "sens", 1f);
            FixedJoystick   = PlayerPrefs.GetInt(P + "fj", 0) == 1;
            HudScale        = PlayerPrefs.GetFloat(P + "hs", 1f);
            ShowNameTags    = PlayerPrefs.GetInt(P + "nt", 1) == 1;
            MasterVolume    = PlayerPrefs.GetFloat(P + "vol", 1f);
            RingtoneIndex   = PlayerPrefs.GetInt(P + "ring", 0);
            DeserializeHudLayouts(PlayerPrefs.GetString(P + "hud_layout", string.Empty));
            Apply();
        }

        /// <summary>Sauvegarde tous les réglages dans <c>PlayerPrefs</c>.</summary>
        public static void Save()
        {
            PlayerPrefs.SetInt(P + "v", 1);
            PlayerPrefs.SetInt(P + "preset", (int)Preset);
            PlayerPrefs.SetFloat(P + "rs", RenderScale);
            PlayerPrefs.SetFloat(P + "dd", DrawDistance);
            PlayerPrefs.SetInt(P + "shd", ShadowMode);
            PlayerPrefs.SetInt(P + "pp", PostProcessing ? 1 : 0);
            PlayerPrefs.SetInt(P + "fps", TargetFps);
            PlayerPrefs.SetInt(P + "ped", PedestrianCount);
            PlayerPrefs.SetFloat(P + "sens", CameraSensitivity);
            PlayerPrefs.SetInt(P + "fj", FixedJoystick ? 1 : 0);
            PlayerPrefs.SetFloat(P + "hs", HudScale);
            PlayerPrefs.SetInt(P + "nt", ShowNameTags ? 1 : 0);
            PlayerPrefs.SetFloat(P + "vol", MasterVolume);
            PlayerPrefs.SetInt(P + "ring", RingtoneIndex);
            PlayerPrefs.SetString(P + "hud_layout", SerializeHudLayouts());
            PlayerPrefs.Save();
        }

        // ────────────── Setters ──────────────

        private static bool _inPreset;
        private static void MaybeCustom() { if (!_inPreset) Preset = QualityPreset.Custom; }

        public static void SetRenderScale(float v)      { RenderScale = Mathf.Clamp(v, 0.5f, 1f); MaybeCustom(); }
        public static void SetDrawDistance(float v)      { DrawDistance = Mathf.Clamp(v, 120f, 600f); MaybeCustom(); }
        public static void SetShadowMode(int v)          { ShadowMode = Mathf.Clamp(v, 0, 2); MaybeCustom(); }
        public static void SetPostProcessing(bool v)     { PostProcessing = v; MaybeCustom(); }
        public static void SetTargetFps(int v)           { TargetFps = Mathf.Clamp(v, 30, 120); MaybeCustom(); }
        public static void SetPedestrianCount(int v)     { PedestrianCount = Mathf.Clamp(v, 0, 30); MaybeCustom(); }
        public static void SetCameraSensitivity(float v) { CameraSensitivity = Mathf.Clamp(v, 0.5f, 2f); }
        public static void SetFixedJoystick(bool v)      { FixedJoystick = v; }
        public static void SetHudScale(float v)          { HudScale = Mathf.Clamp(v, 0.8f, 1.3f); }
        public static void SetShowNameTags(bool v)       { ShowNameTags = v; }
        public static void SetMasterVolume(float v)      { MasterVolume = Mathf.Clamp01(v); }
        public static void SetRingtoneIndex(int v)       { RingtoneIndex = Mathf.Max(0, v); }

        // ────────────── Disposition personnalisée des touches ──────────────

        public static bool TryGetHudLayout(HudControlId id, out HudControlLayout layout)
            => s_hudLayouts.TryGetValue(id, out layout);

        public static void SetHudLayout(HudControlId id, float normX, float normY, float scale)
        {
            s_hudLayouts[id] = new HudControlLayout
            {
                Customized = true,
                NormX = Mathf.Clamp(normX, 0.04f, 0.96f),
                NormY = Mathf.Clamp(normY, 0.04f, 0.96f),
                Scale = Mathf.Clamp(scale, 0.7f, 1.5f),
            };
        }

        public static void ClearHudLayout(HudControlId id)
        {
            s_hudLayouts.Remove(id);
        }

        public static void ResetHudLayout(bool vehicleOnly = false, bool onFootOnly = false)
        {
            if (!vehicleOnly && !onFootOnly)
            {
                s_hudLayouts.Clear();
            }
            else
            {
                var toRemove = new List<HudControlId>();
                foreach (HudControlId id in s_hudLayouts.Keys)
                {
                    bool isVehicle = id >= HudControlId.SteerLeft;
                    if ((vehicleOnly && isVehicle) || (onFootOnly && !isVehicle))
                        toRemove.Add(id);
                }
                for (int i = 0; i < toRemove.Count; i++)
                    s_hudLayouts.Remove(toRemove[i]);
            }
            Apply();
            Save();
        }

        public static void ResetAllSettings()
        {
            ShowNameTags = true;
            CameraSensitivity = 1f;
            FixedJoystick = false;
            HudScale = 1f;
            MasterVolume = 1f;
            RingtoneIndex = 0;
            s_hudLayouts.Clear();
            ApplyPreset(QualityPreset.Balanced);
        }

        /// <summary>Applique la position et l'échelle personnalisées (ou la position par défaut) à un contrôle HUD.</summary>
        public static void ApplyRectLayout(RectTransform rect, RectTransform canvasRect, HudControlId id, Vector2 defaultAnchoredPos)
        {
            if (rect == null)
                return;

            if (!s_hudLayouts.TryGetValue(id, out HudControlLayout layout) || !layout.Customized)
            {
                rect.anchoredPosition = defaultAnchoredPos;
                rect.localScale = Vector3.one;
                return;
            }

            float s = Mathf.Clamp(layout.Scale, 0.7f, 1.5f);
            rect.localScale = new Vector3(s, s, 1f);
            if (canvasRect == null)
                return;

            float cw = canvasRect.rect.width;
            float ch = canvasRect.rect.height;
            if (cw <= 1f || ch <= 1f)
                return;

            Vector2 anchorCenter = (rect.anchorMin + rect.anchorMax) * 0.5f;
            Vector2 targetInCanvas = new Vector2((layout.NormX - anchorCenter.x) * cw, (layout.NormY - anchorCenter.y) * ch);
            Vector2 size = rect.rect.size;
            Vector2 pivotOffset = new Vector2((rect.pivot.x - 0.5f) * size.x * s, (rect.pivot.y - 0.5f) * size.y * s);
            rect.anchoredPosition = targetInCanvas + pivotOffset;
        }

        /// <summary>Position normalisée (0..1) par défaut d'un contrôle selon les dimensions courantes du canvas.</summary>
        public static Vector2 GetDefaultNormPosition(HudControlId id, float cw, float ch)
        {
            if (cw <= 1f) cw = 1920f;
            if (ch <= 1f) ch = 1080f;

            return id switch
            {
                HudControlId.Joystick    => new Vector2(0.125f, 0.22f),
                HudControlId.Jump        => AnchorToNorm(1f, 0f, -112f, 216f, cw, ch),
                HudControlId.Punch       => AnchorToNorm(1f, 0f, -276f, 128f, cw, ch),
                HudControlId.Mic         => AnchorToNorm(1f, 0f, -276f, 305f, cw, ch),
                HudControlId.Rob         => AnchorToNorm(1f, 0f, -570f, 360f, cw, ch),
                HudControlId.Interact    => AnchorToNorm(0.5f, 0f, 0f, 134f + 36f, cw, ch),
                HudControlId.BottomDock  => AnchorToNorm(0.5f, 0f, 0f, 24f + 48f, cw, ch),
                HudControlId.SteerLeft   => AnchorToNorm(0f, 0f, 165f, 160f, cw, ch),
                HudControlId.SteerRight  => AnchorToNorm(0f, 0f, 365f, 160f, cw, ch),
                HudControlId.GazPedal    => AnchorToNorm(1f, 0f, -102f, 168f, cw, ch),
                HudControlId.BrakeButton => AnchorToNorm(1f, 0f, -262f, 120f, cw, ch),
                HudControlId.KlaxonButton => AnchorToNorm(1f, 0f, -262f, 294f, cw, ch),
                HudControlId.PharesButton => AnchorToNorm(1f, 0f, -102f, 412f, cw, ch),
                HudControlId.Speedometer => AnchorToNorm(1f, 0f, -490f, 154f, cw, ch),
                _ => new Vector2(0.5f, 0.5f),
            };
        }

        private static Vector2 AnchorToNorm(float ax, float ay, float px, float py, float cw, float ch)
        {
            return new Vector2(Mathf.Clamp(ax + px / cw, 0.04f, 0.96f), Mathf.Clamp(ay + py / ch, 0.04f, 0.96f));
        }

        private static string SerializeHudLayouts()
        {
            if (s_hudLayouts.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (KeyValuePair<HudControlId, HudControlLayout> kv in s_hudLayouts)
            {
                if (!kv.Value.Customized)
                    continue;
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append((int)kv.Key).Append(':')
                  .Append(kv.Value.NormX.ToString("F4", CultureInfo.InvariantCulture)).Append(':')
                  .Append(kv.Value.NormY.ToString("F4", CultureInfo.InvariantCulture)).Append(':')
                  .Append(kv.Value.Scale.ToString("F3", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static void DeserializeHudLayouts(string raw)
        {
            s_hudLayouts.Clear();
            if (string.IsNullOrEmpty(raw))
                return;

            string[] entries = raw.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split(':');
                if (parts.Length != 4)
                    continue;
                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idInt) &&
                    idInt >= 0 && idInt <= 255 &&
                    Enum.IsDefined(typeof(HudControlId), (byte)idInt) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float nx) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ny) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float sc))
                {
                    SetHudLayout((HudControlId)idInt, nx, ny, sc);
                }
            }
        }

        /// <summary>Applique un préréglage complet, sauvegarde et projette.</summary>
        public static void ApplyPreset(QualityPreset preset)
        {
            _inPreset = true;
            Preset = preset;
            switch (preset)
            {
                case QualityPreset.VeryLow:
                    RenderScale = 0.6f;  DrawDistance = 150f; ShadowMode = 0;
                    PostProcessing = false; TargetFps = 30; PedestrianCount = 0;
                    break;
                case QualityPreset.High:
                    RenderScale = 1f;    DrawDistance = 450f; ShadowMode = 2;
                    PostProcessing = true;  TargetFps = 60; PedestrianCount = 20;
                    break;
                default: // Balanced
                    Preset = QualityPreset.Balanced;
                    RenderScale = 0.8f;  DrawDistance = 300f; ShadowMode = 1;
                    PostProcessing = false; TargetFps = 60; PedestrianCount = 12;
                    break;
            }
            _inPreset = false;
            Apply();
            Save();
        }

        // ────────────── Projection ──────────────

        /// <summary>
        /// Projette tous les réglages sur les API Unity : URP <c>renderScale</c>, caméra,
        /// brume, ombres, FPS, audio. Appelé une seule fois après un lot de modifications.
        /// </summary>
        public static void Apply()
        {
            // --- URP ---
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.renderScale = RenderScale;
                urp.shadowDistance = ShadowMode switch { 0 => 0f, 1 => 30f, _ => 60f };
            }

            // --- Caméra ---
            ApplyToCamera(Camera.main);

            // --- Brume synchronisée avec la distance d'affichage ---
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            // --- LOD ---
            QualitySettings.lodBias = LodBias;

            // --- FPS ---
            Application.targetFrameRate = TargetFps;

            // --- Audio ---
            AudioListener.volume = MasterVolume;

            Changed?.Invoke();
        }

        /// <summary>
        /// Applique les réglages dépendant de la caméra. Appelé par <see cref="Apply"/> et par
        /// les systèmes qui créent ou configurent la caméra après le chargement initial.
        /// </summary>
        public static void ApplyToCamera(Camera cam)
        {
            if (cam == null)
                return;

            cam.farClipPlane = OutdoorFarClip;

            // Culling par couche : les petits objets disparaissent plus tôt que les bâtiments.
            float[] cull = new float[32];
            cull[Core.GameLayers.Player]      = DrawDistance * 0.6f;
            cull[Core.GameLayers.Vehicle]     = DrawDistance * 0.8f;
            cull[Core.GameLayers.VehicleBody] = DrawDistance * 0.8f;
            cam.layerCullDistances = cull;

            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                data.renderPostProcessing = PostProcessing;
                data.antialiasing = PostProcessing
                    ? AntialiasingMode.FastApproximateAntialiasing
                    : AntialiasingMode.None;
            }
        }
    }
}
