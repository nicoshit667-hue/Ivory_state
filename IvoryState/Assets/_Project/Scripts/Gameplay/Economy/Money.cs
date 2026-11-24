using System.Text;

namespace IvoryState.Gameplay.Economy
{
    /// <summary>
    /// Monnaie du jeu : le franc CFA (XOF), toujours en entiers (pas de centimes).
    /// Stocké en <c>long</c> : aucune erreur d'arrondi, aucune limite en pratique.
    /// </summary>
    public static class Money
    {
        /// <summary>Solde de départ d'un nouveau joueur.</summary>
        public const long StartingBalance = 5000;

        private static readonly StringBuilder s_builder = new StringBuilder(24);

        /// <summary>
        /// « 12 500 FCFA ». Espaces simples comme séparateurs de milliers : la police par défaut
        /// n'a pas l'espace fine insécable utilisée par la culture fr-FR.
        /// </summary>
        public static string Format(long amount)
        {
            s_builder.Clear();
            if (amount < 0)
            {
                s_builder.Append('-');
                amount = -amount;
            }

            string digits = amount.ToString();
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0)
                    s_builder.Append(' ');
                s_builder.Append(digits[i]);
            }

            s_builder.Append(" FCFA");
            return s_builder.ToString();
        }
    }
}
