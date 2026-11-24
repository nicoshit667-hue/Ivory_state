using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IvoryState.Gameplay.Player
{
    /// <summary>
    /// Besoins du personnage, de 0 à 100 : <b>satiété</b> (faim), <b>hydratation</b> (soif) et <b>santé</b>.
    /// <list type="bullet">
    /// <item>Affamé ou assoiffé (jauge à 0) : on ne peut plus courir. Règle appliquée dans la simulation
    /// de mouvement (PlayerMotor), donc aussi par le serveur : impossible de la contourner.</item>
    /// <item>La santé baisse tant qu'on a très faim ou très soif, et remonte quand on est bien nourri et
    /// hydraté. À 0 : malaise (le serveur ramène le joueur à la clinique).</item>
    /// </list>
    /// Les trois jauges sont lisibles par les observateurs (1 octet chacune, rarement modifiées) : les
    /// autres clients prédisent correctement les déplacements de ce joueur. Les règles d'évolution
    /// vivent dans l'assembly Server (EconomyService).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNeeds : NetworkBehaviour
    {
        public const byte MaxSatiety = 100;
        public const byte MaxHydration = 100;
        public const byte MaxHealth = 100;

        private static readonly List<PlayerNeeds> s_serverInstances = new List<PlayerNeeds>(128);

        private readonly SyncVar<byte> _satiety = new SyncVar<byte>(MaxSatiety);
        private readonly SyncVar<byte> _hydration = new SyncVar<byte>(MaxHydration);
        private readonly SyncVar<byte> _health = new SyncVar<byte>(MaxHealth);

        public byte Satiety => _satiety.Value;
        public byte Hydration => _hydration.Value;
        public byte Health => _health.Value;

        /// <summary>Plus assez d'énergie pour courir (trop faim ou trop soif).</summary>
        public bool IsExhausted => _satiety.Value == 0 || _hydration.Value == 0;

        /// <summary>Joueurs actifs côté serveur (pour l'évolution périodique des jauges).</summary>
        public static IReadOnlyList<PlayerNeeds> ServerInstances => s_serverInstances;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_serverInstances.Clear();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            s_serverInstances.Add(this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            s_serverInstances.Remove(this);
        }

        /// <summary>Serveur : jauges restaurées depuis la sauvegarde.</summary>
        [Server]
        public void ServerSetSatiety(int value) => _satiety.Value = Clamp(value, MaxSatiety);

        [Server]
        public void ServerSetHydration(int value) => _hydration.Value = Clamp(value, MaxHydration);

        [Server]
        public void ServerSetHealth(int value) => _health.Value = Clamp(value, MaxHealth);

        [Server]
        public void ServerChangeSatiety(int delta) => _satiety.Value = Clamp(_satiety.Value + delta, MaxSatiety);

        [Server]
        public void ServerChangeHydration(int delta) => _hydration.Value = Clamp(_hydration.Value + delta, MaxHydration);

        [Server]
        public void ServerChangeHealth(int delta) => _health.Value = Clamp(_health.Value + delta, MaxHealth);

        private static byte Clamp(int value, byte max) => (byte)Mathf.Clamp(value, 0, max);
    }
}
