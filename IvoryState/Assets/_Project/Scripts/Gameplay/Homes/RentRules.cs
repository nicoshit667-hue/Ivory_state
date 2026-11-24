using System;

namespace IvoryState.Gameplay.Homes
{
    /// <summary>
    /// Loyer des logements. Échéance chaque fin de semaine réelle : le dimanche à 20 h (heure
    /// d'Abidjan = UTC). Un impayé laisse une semaine pour payer (ou obtenir une aide publique) ;
    /// à l'échéance suivante, le joueur perd son logement et devient SDF jusqu'à ce qu'il paie sa dette.
    /// </summary>
    public static class RentRules
    {
        /// <summary>Studio en colocation : 10 000 F par mois, soit 5 000 F par colocataire.</summary>
        public const long StudioRentPerRoommate = 5000;

        /// <summary>Aide publique : une fois tous les 14 jours au plus, plafonnée.</summary>
        public const long MaxPublicAid = 10000;
        public const long PublicAidCooldownSeconds = 14L * 24 * 3600;

        private const int DueHourUtc = 20;

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Première échéance (dimanche 20 h UTC) strictement après cet instant (secondes Unix).</summary>
        public static long NextDue(long unixSeconds)
        {
            DateTime t = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            DateTime due = t.Date.AddHours(DueHourUtc);
            int days = ((int)DayOfWeek.Sunday - (int)due.DayOfWeek + 7) % 7;
            due = due.AddDays(days);
            if (due <= t)
                due = due.AddDays(7);
            return new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        /// <summary>Date lisible, heure locale de l'appareil : « dim. 23/11 à 20:00 ».</summary>
        public static string Format(long unixSeconds)
        {
            DateTime local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().DateTime;
            string[] days = { "dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam." };
            return days[(int)local.DayOfWeek] + " " + local.ToString("dd/MM") + " à " + local.ToString("HH:mm");
        }
    }
}
