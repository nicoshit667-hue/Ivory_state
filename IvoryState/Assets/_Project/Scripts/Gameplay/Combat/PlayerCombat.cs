using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using IvoryState.Gameplay.Player;
using UnityEngine;

namespace IvoryState.Gameplay.Combat
{
    /// <summary>
    /// Bagarre à mains nues. Le client vise le joueur le plus proche devant lui et envoie son numéro ;
    /// le serveur revérifie tout (distance, cadence, zone protégée, soins en cours) et applique les
    /// dégâts (service de combat). À 0 de santé : K.O., réveil à l'hôpital le plus proche.
    /// Mettre quelqu'un K.O. est un crime inscrit au casier (effacé au bout d'un jour réel).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class PlayerCombat : NetworkBehaviour
    {
        public const float Reach = 2.2f;
        public const float Cooldown = 0.7f;

        /// <summary>Fin des soins à l'hôpital (secondes Unix, 0 = pas hospitalisé). Propriétaire seulement.</summary>
        private readonly SyncVar<long> _recoveringUntil = new SyncVar<long>(0,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly));

        /// <summary>Crimes en cours au casier (visible de tous : la police pourra l'utiliser).</summary>
        private readonly SyncVar<byte> _crimes = new SyncVar<byte>(0);

        public long RecoveringUntil => _recoveringUntil.Value;
        public int Crimes => _crimes.Value;

        public static PlayerCombat Local { get; private set; }

        /// <summary>Serveur : un coup demandé (attaquant, cible).</summary>
        public static Action<PlayerCombat, PlayerCombat> ServerPunchHandler { get; set; }

        /// <summary>Client : j'ai reçu un coup (pseudo de l'attaquant, K.O. ?).</summary>
        public static event Action<string, bool> LocalHit;

        /// <summary>Serveur : un racket demandé (agresseur, victime). Voir <see cref="RobRules"/>.</summary>
        public static Action<PlayerCombat, PlayerCombat> ServerRobHandler { get; set; }

        /// <summary>Client : on vient de me racketter (pseudo de l'agresseur, espèces prises).</summary>
        public static event Action<string, long> LocalRobbed;

        private float _nextClientRob;
        private float _nextServerRob;

        private float _nextClientPunch;
        private float _nextServerPunch;
        private PlayerMotor _motor;
        private float _lungeTime = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            ServerPunchHandler = null;
            LocalHit = null;
            ServerRobHandler = null;
            LocalRobbed = null;
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
                Local = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            StopFirearmClient();
            if (Local == this)
                Local = null;
        }

        /// <summary>Client : joueur visable (le plus proche, devant moi, à portée).</summary>
        public PlayerCombat FindTarget()
        {
            PlayerCombat best = null;
            float bestSqr = Reach * Reach;
            Vector3 position = transform.position;
            Transform view = _motor != null ? _motor.GraphicalTransform : transform;
            var players = PlayerNetworkIdentity.ClientInstances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerNetworkIdentity other = players[i];
                if (other == null || other.gameObject == gameObject || !other.TryGetComponent(out PlayerCombat combat))
                    continue;
                if (other.TryGetComponent(out Vehicles.PlayerDriving driving) && driving.IsInVehicle)
                    continue;
                Vector3 offset = other.transform.position - position;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                // Devant moi (à 70° près) ou collé à moi.
                if (sqr > bestSqr || (sqr > 0.8f && Vector3.Angle(view.forward, offset) > 70f))
                    continue;
                best = combat;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>Client : frapper le joueur visé.</summary>
        public void Punch()
        {
            if (!IsOwner || Time.unscaledTime < _nextClientPunch)
                return;
            PlayerCombat target = FindTarget();
            if (target == null)
                return;
            _nextClientPunch = Time.unscaledTime + Cooldown;
            ServerPunch(target);
        }

        [ServerRpc]
        private void ServerPunch(PlayerCombat target)
        {
            if (Time.unscaledTime < _nextServerPunch || target == null || target == this)
                return;
            _nextServerPunch = Time.unscaledTime + Cooldown * 0.9f;
            ServerPunchHandler?.Invoke(this, target);
        }

        /// <summary>Client : racketter le joueur visé avec la machette ou le couteau du sac (le serveur décide).</summary>
        public void Rob()
        {
            if (!IsOwner || Time.unscaledTime < _nextClientRob)
                return;
            PlayerCombat target = FindTarget();
            if (target == null)
                return;
            _nextClientRob = Time.unscaledTime + 2f;
            ServerRob(target);
        }

        [ServerRpc]
        private void ServerRob(PlayerCombat target)
        {
            if (Time.unscaledTime < _nextServerRob || target == null || target == this)
                return;
            _nextServerRob = Time.unscaledTime + 1.5f;
            ServerRobHandler?.Invoke(this, target);
        }

        /// <summary>Serveur : le geste de menace chez tout le monde, et l'avertissement de la victime.</summary>
        [Server]
        public void ServerPlayRob(PlayerCombat victim, string attackerName, long amount)
        {
            ObserversPunch();
            if (victim != null && victim.Owner.IsValid)
                victim.TargetRobbed(victim.Owner, attackerName, amount);
        }

        [TargetRpc]
        private void TargetRobbed(FishNet.Connection.NetworkConnection connection, string attackerName, long amount)
        {
            LocalRobbed?.Invoke(attackerName, amount);
        }

        [Server]
        public void ServerSetRecovering(long until) => _recoveringUntil.Value = until;

        [Server]
        public void ServerSetCrimes(int crimes) => _crimes.Value = (byte)Mathf.Clamp(crimes, 0, 255);

        /// <summary>Serveur : animation du coup chez tout le monde, et avertissement de la victime.</summary>
        [Server]
        public void ServerPlayPunch(PlayerCombat victim, string attackerName, bool knockout)
        {
            ObserversPunch();
            if (victim != null && victim.Owner.IsValid)
                victim.TargetHit(victim.Owner, attackerName, knockout);
        }

        [ObserversRpc]
        private void ObserversPunch()
        {
            _lungeTime = 0f;
        }

        [TargetRpc]
        private void TargetHit(FishNet.Connection.NetworkConnection connection, string attackerName, bool knockout)
        {
            LocalHit?.Invoke(attackerName, knockout);
        }

        // Coup de poing visible : le modèle de l'attaquant s'élance en avant puis revient (0,25 s).
        // On déplace l'enfant du visuel lissé (position locale fixée, rien ne s'accumule).
        private Transform _model;
        private Vector3 _modelRest;

        private void UpdateLunge()
        {
            if (_lungeTime < 0f || _motor == null)
                return;
            if (_model == null)
            {
                Transform graphics = _motor.GraphicalTransform;
                if (graphics == transform || graphics.childCount == 0)
                {
                    _lungeTime = -1f;
                    return;
                }
                _model = graphics.GetChild(0);
                _modelRest = _model.localPosition;
            }
            _lungeTime += Time.deltaTime;
            float t = Mathf.Clamp01(_lungeTime / 0.25f);
            _model.localPosition = _modelRest + Vector3.forward * (Mathf.Sin(t * Mathf.PI) * 0.35f);
            if (t >= 1f)
            {
                _model.localPosition = _modelRest;
                _lungeTime = -1f;
            }
        }
    }
}
