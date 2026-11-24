namespace IvoryState.Gameplay.Interaction
{
    /// <summary>
    /// Point d'interaction qui ouvre un menu de choix (produits d'une boutique, destinations d'une gare…).
    /// <para>
    /// Le client affiche les options lues sur <b>sa</b> copie de la scène ; il n'envoie au serveur que
    /// « point n° X, option n° i ». Le serveur relit l'option (prix, effets, destination) sur sa propre
    /// copie : un client modifié ne peut changer ni les prix ni les règles.
    /// </para>
    /// </summary>
    public abstract class MenuInteractable : WorldInteractable
    {
        /// <summary>Titre du menu (ex. « Superette de Tantie Awa »).</summary>
        public virtual string MenuTitle => DisplayName;

        /// <summary>Phrase d'accueil sous le titre (ex. « Bonjour, qu'est-ce qu'on te sert ? »).</summary>
        public abstract string MenuGreeting { get; }

        public abstract int OptionCount { get; }

        /// <summary>Libellé d'une option (ex. « Eau minérale 1,5 L »).</summary>
        public abstract string OptionLabel(int index);

        /// <summary>Prix en FCFA (0 = gratuit).</summary>
        public abstract long OptionPrice(int index);

        /// <summary>Précision sous le libellé (ex. « Soif -40 »), ou null.</summary>
        public virtual string OptionDetail(int index) => null;
    }
}
