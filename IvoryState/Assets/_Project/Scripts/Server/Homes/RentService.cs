using IvoryState.Gameplay.Accounts;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Server.Backend;
using UnityEngine;

namespace IvoryState.Server.Homes
{
    /// <summary>
    /// Loyers (serveur uniquement). Chaque dimanche à 20 h, le loyer est prélevé sur le Pingoin du
    /// joueur (à sa prochaine connexion s'il était absent : les semaines manquées sont rattrapées).
    /// <list type="number">
    /// <item>Solde suffisant : loyer payé.</item>
    /// <item>Sinon le loyer devient une dette ; le joueur a jusqu'à l'échéance suivante pour la payer
    /// (téléphone → Logement) ou obtenir une aide publique.</item>
    /// <item>Dette toujours là à l'échéance suivante : il perd son studio et devient SDF (affiché
    /// au-dessus de sa tête). Quand il paie sa dette, il retrouve un studio en colocation.</item>
    /// </list>
    /// Les joueurs non liés à un compte (bots, mode hors-ligne) ne paient rien.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RentService : MonoBehaviour
    {
        private const float CheckInterval = 20f;
        private const int MaxCatchUpWeeks = 12;

        private float _nextCheck;

        private void OnEnable()
        {
            PlayerHome.ServerActionHandler = HandleAction;
        }

        private void OnDisable()
        {
            if (PlayerHome.ServerActionHandler == (System.Action<PlayerHome, PlayerHome.HomeAction>)HandleAction)
                PlayerHome.ServerActionHandler = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + CheckInterval;

            long now = RentRules.Now;
            var homes = PlayerHome.ServerInstances;
            for (int i = 0; i < homes.Count; i++)
            {
                PlayerHome home = homes[i];
                if (home != null && home.ServerRentLoaded && IsLinked(home))
                    Process(home, now);
            }
        }

        private static bool IsLinked(PlayerHome home) =>
            home.TryGetComponent(out PlayerAccount account) && account.ServerUserId != null;

        private void Process(PlayerHome home, long now)
        {
            if (!home.TryGetComponent(out PlayerEconomy economy))
                return;

            // Premier passage : la première semaine est offerte.
            if (home.ServerPaidUntil <= 0)
            {
                home.ServerSetRent(RentRules.NextDue(now), home.RentDebt);
                return;
            }

            long paidUntil = home.ServerPaidUntil;
            long debt = home.RentDebt;
            long due = RentRules.NextDue(paidUntil);
            int weeks = 0;
            bool changed = false;
            while (now >= due && weeks < MaxCatchUpWeeks)
            {
                weeks++;
                changed = true;
                if (home.IsHomeless)
                {
                    paidUntil = due; // pas de logement, pas de loyer
                }
                else if (debt > 0)
                {
                    // Une semaine de délai écoulée sans payer : à la rue.
                    paidUntil = due;
                    Evict(home, economy);
                }
                else
                {
                    // Studio en colocation + bien loué (loyer) ou acheté (impôt de 0,5 %).
                    long rent = WeeklyCost(home);
                    if (rent <= 0)
                    {
                        // rien à payer cette semaine
                    }
                    else if (economy.ServerTrySpend(rent, "loyer et charges du logement"))
                    {
                        economy.ServerNotify("Loyer de la semaine payé : " + Money.Format(rent) + ".");
                    }
                    else
                    {
                        debt += rent;
                        economy.ServerNotify("Loyer impayé : " + Money.Format(rent) + " de dette. Paie avant " +
                                             RentRules.Format(RentRules.NextDue(due)) +
                                             " (téléphone → Logement) ou demande une aide publique, sinon tu seras à la rue.");
                    }
                    paidUntil = due;
                }
                due = RentRules.NextDue(paidUntil);
            }

            if (changed)
                home.ServerSetRent(paidUntil, debt);
        }

        /// <summary>Ce que le joueur paie chaque semaine pour ses logements.</summary>
        public static long WeeklyCost(PlayerHome home)
        {
            long cost = home.HasHome ? RentRules.StudioRentPerRoommate : 0;
            for (int slot = 0; slot < PlayerHome.MaxProperties; slot++)
            {
                int id = home.PropertyAt(slot);
                if (PropertyCatalog.IsValid(id) && home.OwnsPropertyAt(slot))
                    cost += PropertyCatalog.WeeklyCost(PropertyCatalog.Get(id));
            }
            return cost;
        }

        private void Evict(PlayerHome home, PlayerEconomy economy)
        {
            // Bail d'un appartement loué résilié (une maison achetée n'est pas saisie).
            bool lostRented = PropertyService.Instance != null && PropertyService.Instance.ServerEvictRented(home);

            if (home.HasHome)
            {
                // Dehors s'il est chez lui à ce moment-là.
                if (StudioLayout.StudioAt(home.transform.position) != 0 && home.TryGetComponent(out PlayerInteractor interactor) &&
                    HomeEntrance.Residence != null)
                    interactor.ServerTravel(HomeEntrance.Residence.ExitPosition, "Expulsé du studio");
                if (home.TryGetComponent(out PlayerAccount account) && TryGetComponent(out AccountService accounts))
                    accounts.ServerReleaseHome(account);
                home.ServerSetHome(0, string.Empty);
            }

            if (home.HasProperty)
            {
                economy.ServerNotify("Impôt impayé : ta dette s'accumule (" + Money.Format(home.RentDebt) + "). Paie-la dans Logement.");
                return;
            }
            home.ServerSetHomeless(true);
            economy.ServerNotify("Loyer toujours impayé : tu as perdu " + (lostRented ? "ton logement" : "ton studio") +
                                 ". Tu es SDF tant que ta dette n'est pas payée (" + Money.Format(home.RentDebt) + ").");
        }

        private void HandleAction(PlayerHome home, PlayerHome.HomeAction action)
        {
            if (home == null || !home.ServerRentLoaded || !home.TryGetComponent(out PlayerEconomy economy))
                return;

            switch (action)
            {
                case PlayerHome.HomeAction.PayDebt:
                    PayDebt(home, economy);
                    break;
                case PlayerHome.HomeAction.PublicAid:
                    PublicAid(home, economy);
                    break;
            }
        }

        private void PayDebt(PlayerHome home, PlayerEconomy economy)
        {
            long debt = home.RentDebt;
            if (debt <= 0)
            {
                economy.ServerNotify("Tu n'as aucune dette de loyer.");
                return;
            }
            if (!economy.ServerTrySpend(debt, "dette de loyer"))
            {
                economy.ServerNotify("Pas assez d'argent pour payer ta dette (" + Money.Format(debt) + "). Travaille ou demande une aide publique.");
                return;
            }

            home.ServerSetRent(home.ServerPaidUntil, 0);
            if (!home.IsHomeless)
            {
                economy.ServerNotify("Dette payée : " + Money.Format(debt) + ". Tu gardes ton studio.");
                return;
            }

            // Ancien SDF : un nouveau studio en colocation.
            home.ServerSetHomeless(false);
            economy.ServerNotify("Dette payée : tu n'es plus SDF. Recherche d'un studio…");
            if (home.TryGetComponent(out PlayerAccount account) && TryGetComponent(out AccountService accounts))
            {
                accounts.ServerAssignHome(account, (studio, roommate) =>
                {
                    if (home == null)
                        return;
                    home.ServerSetHome(studio, roommate);
                    home.ServerSetRent(RentRules.Now, 0);
                    economy.ServerNotify(studio > 0
                        ? "Te revoilà colocataire : studio n° " + studio + " à la Résidence Les Flamboyants."
                        : "La résidence est pleine pour l'instant : réessaie plus tard.");
                });
            }
        }

        private void PublicAid(PlayerHome home, PlayerEconomy economy)
        {
            long debt = home.RentDebt;
            long now = RentRules.Now;
            if (debt <= 0)
            {
                economy.ServerNotify("Aide refusée : tu n'as pas de dette de loyer.");
                return;
            }
            if (economy.Balance >= debt)
            {
                economy.ServerNotify("Aide refusée : tu as assez d'argent pour payer ta dette.");
                return;
            }
            if (home.ServerAidAt > 0 && now - home.ServerAidAt < RentRules.PublicAidCooldownSeconds)
            {
                economy.ServerNotify("Aide refusée : tu en as déjà reçu une. Prochaine demande possible le " +
                                     RentRules.Format(home.ServerAidAt + RentRules.PublicAidCooldownSeconds) + ".");
                return;
            }

            long aid = Mathf.Min((int)(debt - economy.Balance), (int)RentRules.MaxPublicAid);
            aid = (aid + 99) / 100 * 100;
            economy.ServerAdd(aid, "aide publique au logement");
            home.ServerMarkAid(now);
            economy.ServerNotify("Aide publique acceptée : +" + Money.Format(aid) + ". Paie ta dette dans Logement.");
        }
    }
}
