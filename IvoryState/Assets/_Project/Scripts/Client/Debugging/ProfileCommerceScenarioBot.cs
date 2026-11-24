#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Economy;
using IvoryState.Gameplay.Interaction;
using IvoryState.Gameplay.Player;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Bot de test bout-en-bout pour Profil (statut/métier, compteur de vues, masquage serveur),
    /// Commerces (présence dans la scène + annuaire réseau) et Taxi (commande + annulation).
    /// <list type="bullet">
    /// <item><c>-e2eProfileHost</c> : passe Médecin, masque Véhicules &amp; Commerces, teste Taxi &amp; annuaire Commerces.</item>
    /// <item><c>-e2eProfileViewer &lt;pseudo&gt;</c> : consulte la fiche de l'hôte, vérifie statut, vues ≥ 1 et rédaction serveur.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ProfileCommerceScenarioBot : MonoBehaviour
    {
        private const string LogTag = "E2E-PROFILE";

        private bool _isHost;
        private string _targetPseudo;
        private float _nextTick;
        private int _targetObjectId = -1;
        private bool _hostConfigured;
        private bool _hostVerifiedSelf;
        private bool _hostTestedCommerceDir;
        private bool _hostTestedTaxi;
        private bool _done;
        private int _attempts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool isHost = Array.IndexOf(args, "-e2eProfileHost") >= 0;
            int viewerIdx = Array.IndexOf(args, "-e2eProfileViewer");
            if (!isHost && viewerIdx < 0)
                return;

            var go = new GameObject(nameof(ProfileCommerceScenarioBot));
            DontDestroyOnLoad(go);
            var bot = go.AddComponent<ProfileCommerceScenarioBot>();
            bot._isHost = isHost;
            bot._targetPseudo = viewerIdx >= 0 && viewerIdx + 1 < args.Length ? args[viewerIdx + 1] : null;
        }

        private void OnEnable()
        {
            PlayerPhone.LocalProfileDirectoryReceived += OnProfileDirectory;
            PlayerPhone.LocalProfileReceived += OnProfileReceived;
            CommerceRegistryClient.RegistryUpdated += OnCommerceRegistry;
        }

        private void OnDisable()
        {
            PlayerPhone.LocalProfileDirectoryReceived -= OnProfileDirectory;
            PlayerPhone.LocalProfileReceived -= OnProfileReceived;
            CommerceRegistryClient.RegistryUpdated -= OnCommerceRegistry;
        }

        private void Update()
        {
            if (_done || Time.time < _nextTick)
                return;
            _nextTick = Time.time + 1.5f;

            PlayerNetworkIdentity id = PlayerNetworkIdentity.Local;
            PlayerPhone phone = PlayerPhone.Local;
            PlayerEconomy eco = PlayerEconomy.Local;
            PlayerServices services = PlayerServices.Local;
            if (id == null || phone == null || eco == null || services == null)
                return;

            if (_isHost)
            {
                RunHostTick(id, phone, eco, services);
                return;
            }

            if (_targetObjectId < 0)
            {
                phone.RequestProfileDirectory();
                return;
            }

            _attempts++;
            if (_attempts > 25)
            {
                Fail("délai dépassé en attendant la validation du profil de " + _targetPseudo);
                return;
            }
            phone.RequestProfile(_targetObjectId);
        }

        private void RunHostTick(PlayerNetworkIdentity id, PlayerPhone phone, PlayerEconomy eco, PlayerServices services)
        {
            if (!_hostVerifiedSelf)
            {
                id.RequestSetJobStatus(PlayerJobStatus.Medecin);
                id.RequestSetProfilePrivacy((int)(ProfilePrivacyFlags.Vehicles | ProfilePrivacyFlags.Commerces));
            }

            if (!_hostConfigured)
            {
                _hostConfigured = true;
                eco.RequestCommerceDirectory();
                Vector3 farCommerce = CommerceCatalog.GetWorldPosition(CommerceCatalog.All[CommerceCatalog.All.Length - 1]);
                services.CallTaxi(farCommerce);
                GameLog.Info(LogTag, "Hôte configuré (Médecin, masque Véhicules+Commerces, appel Taxi).");
                return;
            }

            if (!_hostTestedTaxi)
            {
                if (services.Taxi != PlayerServices.TaxiState.None)
                {
                    services.CancelTaxi();
                    _hostTestedTaxi = true;
                }
                else
                {
                    Vector3 farCommerce = CommerceCatalog.GetWorldPosition(CommerceCatalog.All[CommerceCatalog.All.Length - 1]);
                    services.CallTaxi(farCommerce);
                }
            }

            phone.RequestProfile(-1);
        }

        private void OnCommerceRegistry()
        {
            if (!_isHost || _hostTestedCommerceDir)
                return;
            int found = 0;
            for (int i = 0; i < CommerceCatalog.All.Length; i++)
            {
                if (CommerceRegistryClient.TryGet(CommerceCatalog.All[i].Id, out _))
                    found++;
            }
            if (found == CommerceCatalog.Count)
            {
                _hostTestedCommerceDir = true;
                GameLog.Info(LogTag, "Annuaire commerces reçu (" + found + "/" + CommerceCatalog.Count + ").");
            }
        }

        private void OnProfileDirectory(int[] ids, string[] names, int[] levels, bool[] homeless)
        {
            if (_isHost || _done || _targetObjectId >= 0 || string.IsNullOrEmpty(_targetPseudo))
                return;

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], _targetPseudo, StringComparison.OrdinalIgnoreCase))
                {
                    _targetObjectId = ids[i];
                    GameLog.Info(LogTag, "Cible trouvée dans l'annuaire : " + _targetPseudo + " (id=" + _targetObjectId + ").");
                    PlayerPhone.Local?.RequestProfile(_targetObjectId);
                    return;
                }
            }
        }

        private void OnProfileReceived(PlayerProfileSnapshot s)
        {
            if (_done || s == null || !s.Found)
                return;

            if (_isHost)
            {
                if (!s.IsSelf)
                    return;
                int expectedMask = (int)(ProfilePrivacyFlags.Vehicles | ProfilePrivacyFlags.Commerces);
                if (!_hostVerifiedSelf && s.JobStatus == PlayerJobStatus.Medecin && s.PrivacyMask == expectedMask)
                {
                    _hostVerifiedSelf = true;
                    GameLog.Info(LogTag, "HOST_PRÊT : statut=Médecin, masque=" + s.PrivacyMask + ".");
                }
                return;
            }

            if (s.IsSelf || s.TargetObjectId != _targetObjectId)
                return;

            int expected = (int)(ProfilePrivacyFlags.Vehicles | ProfilePrivacyFlags.Commerces);
            if (s.JobStatus != PlayerJobStatus.Medecin || (s.PrivacyMask & expected) != expected)
                return; // attend que le client hôte ait appliqué ses SyncVars

            if (s.ProfileViews < 1)
            {
                Fail("compteur de vues non incrémenté (" + s.ProfileViews + ").");
                return;
            }
            if (!string.IsNullOrEmpty(s.CarsCsv) || s.HasLicense || s.CommercesCount != 0 || !string.IsNullOrEmpty(s.CommercesSummary))
            {
                Fail("fuite de données masquées côté serveur (CarsCsv/License/Commerces non vides).");
                return;
            }

            int commercePoints = CountSceneCommercePoints();
            if (commercePoints < CommerceCatalog.Count)
            {
                Fail("CommercePoints manquants en scène (" + commercePoints + "/" + CommerceCatalog.Count + ").");
                return;
            }

            _done = true;
            GameLog.Info(LogTag, "RÉUSSI : statut=" + s.JobStatus + ", vues=" + s.ProfileViews +
                                 ", masque=" + s.PrivacyMask + ", commercesScene=" + commercePoints + ".");
            Invoke(nameof(Quit), 1f);
        }

        private static int CountSceneCommercePoints()
        {
            var seen = new HashSet<int>();
            var all = WorldInteractable.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is CommercePoint cp)
                    seen.Add(cp.CommerceId);
            }
            return seen.Count;
        }

        private void Fail(string reason)
        {
            if (_done)
                return;
            _done = true;
            GameLog.Error(LogTag, "ÉCHEC : " + reason);
            Invoke(nameof(Quit), 1f);
        }

        private void Quit() => Application.Quit();
    }
}
#endif
