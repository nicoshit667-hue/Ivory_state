using UnityEngine;

namespace IvoryState.Core
{
    /// <summary>
    /// Couches physiques du jeu. Les noms sont aussi déclarés dans le TagManager par l'outil de setup,
    /// mais le code ne dépend que des index : pas de LayerMask.NameToLayer à l'exécution.
    /// </summary>
    public static class GameLayers
    {
        /// <summary>Personnages des joueurs (et bots / PNJ qui partagent le même prefab).</summary>
        public const int Player = 8;
        public const string PlayerName = "Player";

        public const int PlayerMask = 1 << Player;

        /// <summary>Véhicules : capsule de déplacement (CharacterController).</summary>
        public const int Vehicle = 9;
        public const string VehicleName = "Vehicle";
        public const int VehicleMask = 1 << Vehicle;

        /// <summary>
        /// Carrosserie des véhicules : boîte aux dimensions réelles de la voiture. La capsule de
        /// déplacement (ronde) ne couvrirait pas l'avant et l'arrière d'une berline de 4,4 m.
        /// </summary>
        public const int VehicleBody = 10;
        public const string VehicleBodyName = "VehicleBody";
        public const int VehicleBodyMask = 1 << VehicleBody;

        /// <summary>Décor statique uniquement : sol, bâtiments, quais… (ni joueurs, ni véhicules).</summary>
        public const int WorldMask = ~(PlayerMask | VehicleMask | VehicleBodyMask);

        /// <summary>Tout ce qu'une voiture ne doit pas traverser (décor, joueurs, autres carrosseries).</summary>
        public const int VehicleBlockingMask = ~VehicleMask;

        /// <summary>
        /// Rien ne se traverse : joueurs, voitures et décor sont solides.
        /// <para>
        /// Contrepartie réseau : avec la prédiction, une bousculade avec un joueur distant (connu avec un
        /// peu de retard) peut provoquer une petite correction. C'est le comportement attendu d'une ville
        /// « solide ». L'argument <c>-ghostPlayers</c> rétablit l'ancien mode (joueurs et véhicules se
        /// traversent) pour les tests de charge à 100 joueurs entassés.
        /// </para>
        /// Appliqué au démarrage, identiquement sur le client et le serveur.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureCollisions()
        {
            bool ghosts = Networking.LaunchArguments.FromEnvironment().GhostPlayers;
            Physics.IgnoreLayerCollision(Player, Player, ghosts);
            Physics.IgnoreLayerCollision(Vehicle, Player, ghosts);
            Physics.IgnoreLayerCollision(Vehicle, Vehicle, ghosts);
            Physics.IgnoreLayerCollision(VehicleBody, Player, ghosts);

            // La capsule d'une voiture ne heurte jamais les carrosseries : ni la sienne (même objet),
            // ni celles des autres (gérées par le balayage de VehicleMotor, plus précis).
            Physics.IgnoreLayerCollision(Vehicle, VehicleBody, true);
        }
    }
}
