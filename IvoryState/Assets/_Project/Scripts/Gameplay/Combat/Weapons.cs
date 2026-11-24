namespace IvoryState.Gameplay.Combat
{
    /// <summary>
    /// Armes rangées dans le sac.
    /// <list type="bullet">
    /// <item><b>Armes blanches</b> (quincaillerie) : machette et couteau, pour racketter (voir <see cref="RobRules"/>).</item>
    /// <item><b>Armes à feu</b> (armurerie, avec un port d'arme) : pistolet, kalach et taser. On les équipe depuis
    /// le sac ; elles tirent des chargeurs achetés à part (voir <see cref="Firearms"/>).</item>
    /// </list>
    /// </summary>
    public static class Weapons
    {
        public const string Machete = "Machette";
        public const string Knife = "Couteau";
        public const long Price = 20000;

        /// <summary>Document délivré (pour l'instant) à l'armurerie : obligatoire pour acheter une arme à feu.</summary>
        public const string Permit = "Port d'arme";
        public const long PermitPrice = 500000;

        public static bool IsMelee(string itemName) => itemName == Machete || itemName == Knife;

        /// <summary>Arme blanche ou arme à feu.</summary>
        public static bool IsWeapon(string itemName) => IsMelee(itemName) || Firearms.ByName(itemName) != null;

        /// <summary>Chargeur ou cartouche d'une arme à feu.</summary>
        public static bool IsAmmo(string itemName) => Firearms.ByAmmo(itemName) != null;

        /// <summary>Objets qui vont toujours au sac à l'achat (jamais consommés sur place).</summary>
        public static bool GoesToBag(string itemName) => IsWeapon(itemName) || IsAmmo(itemName) || itemName == Permit;

        /// <summary>Il faut un port d'arme dans le sac pour acheter cet objet.</summary>
        public static bool RequiresPermit(string itemName) => Firearms.ByName(itemName) != null || IsAmmo(itemName);
    }

    /// <summary>Caractéristiques d'une arme à feu (identiques sur le serveur et les clients).</summary>
    public sealed class FirearmDef
    {
        public byte Id;
        /// <summary>Nom de l'objet dans le sac et en boutique.</summary>
        public string Name;
        /// <summary>Préfab dans <c>Resources/Weapons/</c>.</summary>
        public string Model;
        /// <summary>Nom de l'objet munitions (un objet = un chargeur plein).</summary>
        public string Ammo;
        public int MagazineSize;
        /// <summary>Santé retirée par balle (0 pour le taser, qui immobilise).</summary>
        public int Damage;
        public float Range;
        /// <summary>Délai minimal entre deux tirs (s).</summary>
        public float FireInterval;
        public float ReloadTime;
        /// <summary>Tir en rafale tant que le bouton est appuyé.</summary>
        public bool Automatic;
        /// <summary>Taser : la cible reste au sol <see cref="Firearms.StunSeconds"/> secondes.</summary>
        public bool Stun;
        public long Price;
        public long AmmoPrice;
        /// <summary>Longueur de l'arme (m) : bout du canon pour les traçantes.</summary>
        public float Length;
    }

    public static class Firearms
    {
        /// <summary>Durée au sol après un tir de taser (spécification : 10 s).</summary>
        public const float StunSeconds = 10f;

        public static readonly FirearmDef Pistol = new FirearmDef
        {
            Id = 1, Name = "Pistolet", Model = "glock17", Ammo = "Chargeur 9 mm", MagazineSize = 15,
            Damage = 25, Range = 35f, FireInterval = 0.35f, ReloadTime = 1.6f, Price = 3000000, AmmoPrice = 25000,
            Length = 0.186f,
        };

        public static readonly FirearmDef Rifle = new FirearmDef
        {
            Id = 2, Name = "Kalach", Model = "ak47", Ammo = "Chargeur 7,62", MagazineSize = 30,
            Damage = 30, Range = 60f, FireInterval = 0.12f, ReloadTime = 2.4f, Automatic = true,
            Price = 8000000, AmmoPrice = 60000, Length = 0.88f,
        };

        public static readonly FirearmDef Taser = new FirearmDef
        {
            Id = 3, Name = "Taser", Model = "taser", Ammo = "Cartouche taser", MagazineSize = 1,
            Damage = 0, Range = 6f, FireInterval = 1f, ReloadTime = 1.5f, Stun = true,
            Price = 400000, AmmoPrice = 10000, Length = 0.2f,
        };

        public static readonly FirearmDef[] All = { Pistol, Rifle, Taser };

        public static FirearmDef ById(int id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id)
                    return All[i];
            }
            return null;
        }

        public static FirearmDef ByName(string name)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Name == name)
                    return All[i];
            }
            return null;
        }

        public static FirearmDef ByAmmo(string ammo)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Ammo == ammo)
                    return All[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Règles du racket (vérifiées par le serveur) : l'agresseur a une arme dans son sac ;
    /// la victime est seule (aucun autre joueur tout près), désarmée et à pied ; personne n'est en zone
    /// protégée ni en soins. L'agresseur prend toutes les espèces de la victime (pas son compte Pingoin)
    /// et le racket est inscrit à son casier.
    /// </summary>
    public static class RobRules
    {
        /// <summary>Aucun autre joueur dans ce rayon autour de la victime (sinon il y a des témoins).</summary>
        public const float WitnessRadius = 12f;
        /// <summary>Une même victime ne peut pas être rackettée plus souvent (secondes).</summary>
        public const float VictimCooldown = 600f;
        /// <summary>Délai entre deux tentatives d'un même agresseur (secondes).</summary>
        public const float AttackerCooldown = 60f;
    }
}
