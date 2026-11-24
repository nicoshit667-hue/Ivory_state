using System.Collections.Generic;
using IvoryState.Gameplay.Combat;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Homes;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Vehicles;
using UnityEngine;

namespace IvoryState.Server.Combat
{
    /// <summary>
    /// Hôpitaux (serveur uniquement).
    /// <list type="bullet">
    /// <item>K.O. (bagarre ou malaise) : le joueur se réveille dans un lit de l'hôpital le plus proche,
    /// paie 4 000 F de soins (ce qu'il a, s'il a moins), et sa santé remonte pendant une minute ; il ne
    /// peut pas sortir avant.</item>
    /// <item>Blessé qui entre de lui-même (cercle sous la croix rouge) : consultation à 2 000 F, mêmes soins.</item>
    /// <item>Cercle au pied de la porte de la salle : sortie, une fois guéri.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class HospitalService : MonoBehaviour
    {
        private const float CheckInterval = 0.2f;
        private const float EntranceRadius = 1.4f;

        public static HospitalService Instance { get; private set; }

        private readonly Dictionary<PlayerNeeds, bool> _armed = new Dictionary<PlayerNeeds, bool>();
        private readonly Dictionary<PlayerNeeds, float> _nextReminder = new Dictionary<PlayerNeeds, float>();
        private readonly List<PlayerNeeds> _stale = new List<PlayerNeeds>();
        private float _nextCheck, _nextHeal;
        private int _nextBed;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Hospitalise un joueur : réveil dans un lit, frais médicaux, une minute de soins.</summary>
        public void Admit(PlayerNeeds patient, long fee, string reason)
        {
            if (patient == null)
                return;
            if (patient.TryGetComponent(out PlayerDriving driving) && driving.IsInVehicle)
                driving.ServerExit();

            Vector3 from = patient.transform.position;
            if (PropertyCatalog.TryGetExterior(from, out Vector3 exterior))
                from = exterior;
            int hospital = HospitalCatalog.Nearest(from);
            int bed = _nextBed++ % HospitalCatalog.Beds;

            patient.ServerSetHealth(Mathf.Max(patient.Health, 10));
            patient.ServerSetSatiety(Mathf.Max(patient.Satiety, 30));
            patient.ServerSetHydration(Mathf.Max(patient.Hydration, 30));
            if (patient.TryGetComponent(out PlayerCombat combat))
                combat.ServerSetRecovering(RentRules.Now + (long)HospitalCatalog.RecoverySeconds);
            if (patient.TryGetComponent(out PlayerInteractor interactor))
                interactor.ServerTravel(HospitalCatalog.WakeUpPoint(hospital, bed), HospitalCatalog.Hospitals[hospital].Name);
            _armed[patient] = false;

            if (patient.TryGetComponent(out PlayerEconomy economy))
            {
                long pay = Mathf.Min((int)fee, (int)Mathf.Max(0, economy.Balance));
                if (pay > 0)
                    economy.ServerTrySpend(pay, "frais médicaux (" + reason + ")");
                economy.ServerNotify(reason + " : tu te réveilles au " + HospitalCatalog.Hospitals[hospital].Name +
                                     (pay > 0 ? ". Soins : " + Money.Format(pay) + "." : ". Soins offerts (tu n'avais plus d'argent).") +
                                     " Repos obligatoire : 1 minute.");
            }
        }

        private void Update()
        {
            float now = Time.time;
            if (now >= _nextHeal)
            {
                _nextHeal = now + 1f;
                HealPatients();
            }
            if (now < _nextCheck)
                return;
            _nextCheck = now + CheckInterval;

            var all = PlayerNeeds.ServerInstances;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerNeeds needs = all[i];
                if (needs == null || !needs.Owner.IsValid)
                    continue;
                if (needs.TryGetComponent(out PlayerDriving driving) && driving.IsInVehicle)
                    continue;
                CheckCircles(needs);
            }

            _stale.Clear();
            foreach (PlayerNeeds key in _armed.Keys)
            {
                if (key == null)
                    _stale.Add(key);
            }
            foreach (PlayerNeeds key in _stale)
            {
                _armed.Remove(key);
                _nextReminder.Remove(key);
            }
        }

        private static void HealPatients()
        {
            long now = RentRules.Now;
            var all = PlayerNeeds.ServerInstances;
            for (int i = 0; i < all.Count; i++)
            {
                PlayerNeeds needs = all[i];
                if (needs == null || !needs.TryGetComponent(out PlayerCombat combat) || combat.RecoveringUntil <= 0)
                    continue;
                needs.ServerChangeHealth(2);
                if (now >= combat.RecoveringUntil)
                {
                    combat.ServerSetRecovering(0);
                    needs.ServerSetHealth(PlayerNeeds.MaxHealth);
                    if (needs.TryGetComponent(out PlayerEconomy economy))
                        economy.ServerNotify("Tu es guéri ! Tu peux sortir de l'hôpital (cercle doré près de la porte).");
                }
            }
        }

        private void CheckCircles(PlayerNeeds needs)
        {
            Vector3 position = needs.transform.position;
            bool armed = !_armed.TryGetValue(needs, out bool value) || value;
            bool inCircle = false;

            int hospital = HospitalCatalog.HospitalAt(position);
            if (hospital >= 0)
            {
                Vector3 offset = position - HospitalCatalog.ExitCircle(hospital);
                offset.y = 0f;
                inCircle = offset.sqrMagnitude <= StudioLayout.ExitRadius * StudioLayout.ExitRadius;
                if (inCircle && armed)
                    TryLeave(needs, hospital);
            }
            else if (!StudioLayout.IsIndoors(position))
            {
                for (int i = 0; i < HospitalCatalog.Count; i++)
                {
                    Vector3 offset = position - HospitalCatalog.Hospitals[i].Entrance;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > EntranceRadius * EntranceRadius)
                        continue;
                    inCircle = true;
                    if (armed)
                        Consult(needs);
                    break;
                }
            }
            _armed[needs] = !inCircle;
        }

        private void TryLeave(PlayerNeeds needs, int hospital)
        {
            if (needs.TryGetComponent(out PlayerCombat combat) && combat.RecoveringUntil > RentRules.Now)
            {
                if (needs.TryGetComponent(out PlayerEconomy economy))
                    economy.ServerNotify("Encore " + (combat.RecoveringUntil - RentRules.Now) + " s de soins avant de pouvoir sortir.");
                return;
            }
            if (needs.TryGetComponent(out PlayerInteractor interactor))
                interactor.ServerTravel(HospitalCatalog.EntranceExit(hospital), HospitalCatalog.Hospitals[hospital].Name);
        }

        private void Consult(PlayerNeeds needs)
        {
            if (!needs.TryGetComponent(out PlayerEconomy economy))
                return;
            if (needs.Health >= PlayerNeeds.MaxHealth)
            {
                economy.ServerNotify("Tu es en pleine forme : l'hôpital accueille les blessés et les malades.");
                return;
            }
            Admit(needs, HospitalCatalog.ConsultationFee, "Consultation");
        }
    }
}
