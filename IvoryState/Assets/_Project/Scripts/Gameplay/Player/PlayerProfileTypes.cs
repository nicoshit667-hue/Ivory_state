using System;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>Statut / métier officiel affiché sur la carte d'identité du joueur.</summary>
    public enum PlayerJobStatus : byte
    {
        Civil = 0,
        Medecin = 1,
        Police = 2,
        Militaire = 3,
        Mairie = 4,
        Gouvernement = 5,
    }

    /// <summary>Masque des sections du profil masquées aux autres joueurs (1 bit par section).</summary>
    [Flags]
    public enum ProfilePrivacyFlags
    {
        None = 0,
        Residence = 1 << 0,
        Properties = 1 << 1,
        Vehicles = 1 << 2,
        Commerces = 1 << 3,
        All = Residence | Properties | Vehicles | Commerces,
    }

    public struct ProfileStatusVisual
    {
        public PlayerJobStatus Status;
        public string Label;
        public string RoleSummary;
        public string IconKey;
        public string BackgroundKey;
        public Color AccentColor;
        public Color PillBgColor;
    }

    /// <summary>
    /// Métadonnées partagées des statuts/métiers du profil (libellés, icônes dédiées, fonds dynamiques
    /// de carte d'identité et couleurs d'accent).
    /// </summary>
    public static class PlayerProfileMetaCatalog
    {
        public static readonly ProfileStatusVisual[] Statuses =
        {
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Civil,
                Label = "Civil",
                RoleSummary = "Citoyen d'Abidjan",
                IconKey = "icon_civil",
                BackgroundKey = "bg_civil_slate",
                AccentColor = new Color(0.28f, 0.72f, 0.96f, 1f),
                PillBgColor = new Color(0.14f, 0.34f, 0.56f, 0.92f),
            },
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Medecin,
                Label = "Médecin",
                RoleSummary = "Corps médical & CHU",
                IconKey = "icon_medecin",
                BackgroundKey = "bg_medecin_emerald_ecg",
                AccentColor = new Color(0.20f, 0.86f, 0.58f, 1f),
                PillBgColor = new Color(0.06f, 0.42f, 0.28f, 0.94f),
            },
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Police,
                Label = "Police",
                RoleSummary = "Forces de sécurité nationale",
                IconKey = "icon_police",
                BackgroundKey = "bg_police_tactical",
                AccentColor = new Color(0.38f, 0.68f, 1f, 1f),
                PillBgColor = new Color(0.10f, 0.26f, 0.54f, 0.94f),
            },
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Militaire,
                Label = "Militaire",
                RoleSummary = "Forces armées & défense",
                IconKey = "icon_militaire",
                BackgroundKey = "bg_militaire_camo",
                AccentColor = new Color(0.68f, 0.84f, 0.32f, 1f),
                PillBgColor = new Color(0.24f, 0.30f, 0.14f, 0.94f),
            },
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Mairie,
                Label = "Mairie",
                RoleSummary = "Administration municipale",
                IconKey = "icon_mairie",
                BackgroundKey = "bg_mairie_navy",
                AccentColor = new Color(0.30f, 0.82f, 0.98f, 1f),
                PillBgColor = new Color(0.10f, 0.28f, 0.48f, 0.94f),
            },
            new ProfileStatusVisual
            {
                Status = PlayerJobStatus.Gouvernement,
                Label = "Gouvernement",
                RoleSummary = "Haute autorité de l'État",
                IconKey = "icon_gouvernement",
                BackgroundKey = "bg_gouvernement_obsidian",
                AccentColor = new Color(0.96f, 0.76f, 0.22f, 1f),
                PillBgColor = new Color(0.40f, 0.29f, 0.06f, 0.94f),
            },
        };

        public static bool IsValid(byte raw) => raw < Statuses.Length;

        public static ProfileStatusVisual Get(PlayerJobStatus status)
        {
            int idx = (int)status;
            if (idx < 0 || idx >= Statuses.Length)
                idx = 0;
            return Statuses[idx];
        }
    }
}
