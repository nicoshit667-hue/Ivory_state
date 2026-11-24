using System.Text;

namespace IvoryState.Gameplay.Social
{
    /// <summary>
    /// Règles des pseudos, appliquées par le serveur (le client les applique aussi pour un retour immédiat).
    /// 3 à 16 caractères : lettres (accents compris), chiffres, espace, tiret, tiret bas.
    /// Les espaces multiples sont compactés.
    /// </summary>
    public static class PlayerNames
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        private static readonly StringBuilder s_builder = new StringBuilder(MaxLength);

        /// <summary>
        /// Pseudo de compte : lettres uniquement (accents compris), 3 à 16, sans espace. Renvoie le
        /// pseudo tel quel s'il est valide, sinon null (pas de correction silencieuse à l'inscription).
        /// </summary>
        public static string ValidateAccountPseudo(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            string name = raw.Trim();
            if (name.Length < MinLength || name.Length > MaxLength)
                return null;
            foreach (char c in name)
            {
                if (!char.IsLetter(c))
                    return null;
            }
            return name;
        }

        /// <summary>Renvoie le pseudo nettoyé, ou null s'il est invalide.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            s_builder.Clear();
            bool lastWasSpace = true;
            for (int i = 0; i < raw.Length && s_builder.Length < MaxLength; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                        s_builder.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    s_builder.Append(c);
                    lastWasSpace = false;
                }
            }

            string name = s_builder.ToString().Trim();
            return name.Length >= MinLength ? name : null;
        }
    }
}
