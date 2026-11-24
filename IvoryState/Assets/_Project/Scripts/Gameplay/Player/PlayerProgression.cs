using IvoryState.Gameplay.Economy;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Calcul des niveaux, paliers d'XP et titres de progression des joueurs.
    /// <para>
    /// Formule progressive simple : passer du niveau <c>L</c> à <c>L + 1</c> demande
    /// <c>100 + (L - 1) * 50</c> points d'expérience.
    /// </para>
    /// </summary>
    public static class PlayerProgression
    {
        public const int MaxLevel = 99;

        /// <summary>XP nécessaire pour passer du niveau <paramref name="level"/> au suivant.</summary>
        public static int XpForNextLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 1, MaxLevel);
            return 100 + (clamped - 1) * 50;
        }

        /// <summary>Calcule le niveau et la progression dans le niveau courant à partir du total d'XP.</summary>
        public static void GetLevelInfo(int totalXp, out int level, out int currentLevelXp, out int nextLevelXp)
        {
            int remaining = Mathf.Max(0, totalXp);
            level = 1;
            while (level < MaxLevel)
            {
                int needed = XpForNextLevel(level);
                if (remaining < needed)
                {
                    currentLevelXp = remaining;
                    nextLevelXp = needed;
                    return;
                }
                remaining -= needed;
                level++;
            }

            currentLevelXp = XpForNextLevel(MaxLevel);
            nextLevelXp = currentLevelXp;
        }

        /// <summary>Niveau actuel correspondant au total d'XP.</summary>
        public static int GetLevel(int totalXp)
        {
            GetLevelInfo(totalXp, out int level, out _, out _);
            return level;
        }

        /// <summary>Titre honorifique affiché sur la carte de profil.</summary>
        public static string GetRankTitle(int level)
        {
            if (level >= 25) return "Magnat d'Ivory";
            if (level >= 15) return "Notable d'Abidjan";
            if (level >= 10) return "Entrepreneur";
            if (level >= 6) return "Citadin confirmé";
            if (level >= 3) return "Débrouillard";
            return "Nouveau venu";
        }

        /// <summary>
        /// Estime une XP de départ pour les comptes déjà existants qui n'avaient pas encore de champ <c>xp</c> sauvegardé.
        /// </summary>
        public static int ComputeLegacyBaseXp(long totalMoney, int carCount, bool hasLicense, int ownedProperties, int ownedOutfitsMask)
        {
            int xp = 0;
            xp += Mathf.Max(0, carCount) * 100;
            if (hasLicense) xp += 50;
            xp += Mathf.Max(0, ownedProperties) * 120;

            int extraOutfits = 0;
            for (int i = 1; i < PlayerNetworkIdentity.OutfitCount; i++)
            {
                if ((ownedOutfitsMask & (1 << i)) != 0)
                    extraOutfits++;
            }
            xp += extraOutfits * 30;

            long surplus = totalMoney - (Money.StartingBalance + PlayerEconomy.StartingCash);
            if (surplus > 0)
                xp += (int)Mathf.Clamp(surplus / 25000L, 0L, 2000L) * 5;

            return Mathf.Max(0, xp);
        }
    }
}
