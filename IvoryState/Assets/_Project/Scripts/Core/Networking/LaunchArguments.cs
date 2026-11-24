using System;
using System.Globalization;

namespace IvoryState.Core.Networking
{
    /// <summary>
    /// Arguments de ligne de commande, lus une seule fois au démarrage.
    /// <example>
    /// IvoryStateServer.exe -batchmode -nographics -server -port 7770 -maxPlayers 100
    /// IvoryState.exe -client -address 192.168.1.10 -port 7770
    /// IvoryStateServer.exe -server -serverBots 96 -botSpread 180   (test de charge)
    /// </example>
    /// Les valeurs absentes restent à leur valeur par défaut (None / null / 0) :
    /// c'est alors la <see cref="NetworkConfig"/> qui s'applique.
    /// </summary>
    public readonly struct LaunchArguments
    {
        public readonly LaunchMode Mode;
        public readonly string Address;
        public readonly ushort Port;
        public readonly int MaxPlayers;

        /// <summary>
        /// -bot : le client se déplace tout seul (builds de dev uniquement).
        /// Sert aux tests automatisés et aux tests de charge.
        /// </summary>
        public readonly bool Bot;

        /// <summary>
        /// -serverBots N : le serveur simule N personnages sans client (tests de charge).
        /// Même PlayerMotor que les vrais joueurs, donc même coût CPU et même trafic vers les clients.
        /// </summary>
        public readonly int ServerBots;

        /// <summary>
        /// -botSpread R : rayon (m) de la zone où errent les bots. 0 = foule serrée autour
        /// du point d'apparition (pire cas : tout le monde voit tout le monde).
        /// </summary>
        public readonly float BotSpread;

        /// <summary>
        /// -maxVisible N : remplace le plafond de joueurs visibles de la NetworkConfig
        /// (0 = pas de plafond). -1 si absent. Sert à comparer les réglages en test de charge.
        /// </summary>
        public readonly int MaxVisiblePlayers;

        /// <summary>-ghostPlayers : joueurs et véhicules se traversent (ancien mode, pour les tests de charge entassés).</summary>
        public readonly bool GhostPlayers;

        private LaunchArguments(LaunchMode mode, string address, ushort port, int maxPlayers,
            bool bot, int serverBots, float botSpread, int maxVisiblePlayers, bool ghostPlayers)
        {
            GhostPlayers = ghostPlayers;
            MaxVisiblePlayers = maxVisiblePlayers;
            Mode = mode;
            Address = address;
            Port = port;
            MaxPlayers = maxPlayers;
            Bot = bot;
            ServerBots = serverBots;
            BotSpread = botSpread;
        }

        public static LaunchArguments FromEnvironment()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {
                // Certaines plateformes mobiles ne fournissent pas de ligne de commande.
                args = Array.Empty<string>();
            }

            return Parse(args);
        }

        public static LaunchArguments Parse(string[] args)
        {
            LaunchMode mode = LaunchMode.None;
            string address = null;
            ushort port = 0;
            int maxPlayers = 0;
            bool bot = false;
            int serverBots = 0;
            float botSpread = 0f;
            int maxVisible = -1;
            bool ghostPlayers = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;

                if (Is(arg, "-server")) mode = LaunchMode.Server;
                else if (Is(arg, "-client")) mode = LaunchMode.Client;
                else if (Is(arg, "-host")) mode = LaunchMode.Host;
                else if (Is(arg, "-bot")) bot = true;
                else if (Is(arg, "-ghostPlayers")) ghostPlayers = true;
                else if (Is(arg, "-address") && next != null) { address = next; i++; }
                else if (Is(arg, "-port") && ushort.TryParse(next, out ushort p)) { port = p; i++; }
                else if (Is(arg, "-maxPlayers") && int.TryParse(next, out int m)) { maxPlayers = m; i++; }
                else if (Is(arg, "-maxVisible") && int.TryParse(next, out int v)) { maxVisible = v < 0 ? -1 : v; i++; }
                else if (Is(arg, "-serverBots") && int.TryParse(next, out int b)) { serverBots = Math.Max(0, b); i++; }
                else if (Is(arg, "-botSpread") &&
                         float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                {
                    botSpread = Math.Max(0f, s);
                    i++;
                }
            }

            return new LaunchArguments(mode, address, port, maxPlayers, bot, serverBots, botSpread, maxVisible, ghostPlayers);
        }

        private static bool Is(string arg, string expected)
        {
            return string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
