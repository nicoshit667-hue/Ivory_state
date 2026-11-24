using FishNet.Object;
using UnityEngine;

namespace IvoryState.Core.Networking
{
    /// <summary>
    /// Configuration réseau partagée par le client et le serveur.
    /// Les arguments de ligne de commande (<see cref="LaunchArguments"/>) ont priorité.
    /// </summary>
    [CreateAssetMenu(menuName = "Ivory State/Network Config", fileName = "NetworkConfig")]
    public sealed class NetworkConfig : ScriptableObject
    {
        [Header("Connexion")]
        [Tooltip("Adresse du serveur utilisée par défaut par les clients.")]
        [SerializeField] private string _defaultAddress = "127.0.0.1";

        [Tooltip("Port UDP du serveur de jeu.")]
        [SerializeField] private ushort _port = 7770;

        [Header("Serveur")]
        [Tooltip("Nombre maximum de joueurs connectés sur une instance de la ville.")]
        [SerializeField, Range(1, 200)] private int _maxPlayers = 100;

        [Tooltip("Fréquence de simulation réseau (ticks/s). 30 est un bon compromis bande passante / réactivité sur mobile.")]
        [SerializeField, Range(10, 60)] private ushort _tickRate = 30;

        [Tooltip("Nombre maximum de joueurs visibles par client (les plus proches). Protège la bande passante " +
                 "mobile dans les foules. 0 = pas de plafond (seule la grille d'intérêt s'applique).")]
        [SerializeField, Range(0, 200)] private int _maxVisiblePlayers = 28;

        [Header("Sauvegarde (Nakama)")]
        [Tooltip("Port HTTP de Nakama, sur la même machine que le serveur de jeu.")]
        [SerializeField] private ushort _nakamaPort = 7350;

        [Tooltip("Clé serveur Nakama, publique par conception et embarquée dans l'app. Doit correspondre à " +
                 "NAKAMA_SERVER_KEY dans backend/.env. NE JAMAIS mettre ici la clé http (secrète, serveur de jeu uniquement).")]
        [SerializeField] private string _nakamaServerKey = "ivory-dev-key";

        [Header("Client mobile")]
        [Tooltip("Images/s visées sur mobile. 30 économise la batterie, 60 est plus fluide.")]
        [SerializeField, Range(30, 120)] private int _clientTargetFrameRate = 60;

        [Header("Éditeur")]
        [Tooltip("Mode lancé automatiquement en Play Mode. None = choix via le panneau de dev.")]
        [SerializeField] private LaunchMode _editorLaunchMode = LaunchMode.None;

        [Header("Joueur")]
        [Tooltip("Prefab réseau instancié par le serveur pour chaque joueur connecté.")]
        [SerializeField] private NetworkObject _playerPrefab;

        [Tooltip("Prefab réseau des taxis, apparus par le serveur sur chaque TaxiStand de la scène.")]
        [SerializeField] private NetworkObject _vehiclePrefab;

        [Tooltip("Centre de la zone d'apparition des joueurs.")]
        [SerializeField] private Vector3 _spawnCenter = new Vector3(0f, 1f, 0f);

        [Tooltip("Rayon de dispersion pour éviter que les joueurs apparaissent les uns dans les autres.")]
        [SerializeField, Min(0f)] private float _spawnRadius = 6f;

        public string DefaultAddress => _defaultAddress;
        public ushort Port => _port;
        public int MaxPlayers => _maxPlayers;
        public ushort TickRate => _tickRate;
        public int MaxVisiblePlayers => _maxVisiblePlayers;
        public ushort NakamaPort => _nakamaPort;
        public string NakamaServerKey => _nakamaServerKey;
        public int ClientTargetFrameRate => _clientTargetFrameRate;
        public LaunchMode EditorLaunchMode => _editorLaunchMode;
        public NetworkObject PlayerPrefab => _playerPrefab;
        public NetworkObject VehiclePrefab => _vehiclePrefab;
        public Vector3 SpawnCenter => _spawnCenter;
        public float SpawnRadius => _spawnRadius;
    }
}
