using System;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using IvoryState.Core.Diagnostics;
using UnityEngine;

namespace IvoryState.Core.Networking
{
    /// <summary>
    /// Réglages réseau contre les saccades et retours en arrière pendant les déplacements.
    /// <para>
    /// Cause mesurée (serveur local sur un portable à 2 cœurs, jeu lancé à côté) : le serveur se bloquait
    /// 100 à 400 ms (souvent plusieurs secondes). FishNet jette alors les ticks au-delà de 3 par image : le
    /// serveur tombait à 26 ticks/s au lieu de 30, la file d'entrées des joueurs débordait (15 entrées),
    /// des entrées étaient perdues, et le client était ramené en arrière.
    /// </para>
    /// <list type="bullet">
    /// <item><b>Serveur Windows</b> : priorité haute et minuterie système à 1 ms (sinon <c>Thread.Sleep</c>
    /// du limiteur d'images dort par pas de 15,6 ms et se réveille en retard quand le PC est chargé).</item>
    /// <item><b>Serveur</b> : rattrape jusqu'à 15 ticks par image (0,5 s) au lieu de 3, et garde 1 s
    /// d'entrées par joueur au lieu de 0,5 s.</item>
    /// <item><b>Client</b> : FishNet espaçait les recalages (4-5 par seconde) sous 50 images/s, donc
    /// toujours sur téléphone (30 images/s) et souvent sur PC portable : les corrections devenaient rares et
    /// grosses (à-coups, caméra qui tremble). On ne les espace plus qu'en dessous de 20 images/s.</item>
    /// </list>
    /// </summary>
    public static class NetworkTuning
    {
        private const string LogTag = "Network";

        private const byte ServerMaximumFrameTicks = 15;
        private const byte ServerMaximumReplicates = 30;
        private const ushort ClientMinimumReconcileFramerate = 20;

        public static void ApplyServer(NetworkManager manager)
        {
            if (manager == null)
                return;
            TimeManager time = manager.TimeManager;
            if (time != null)
                SetField(time, "_maximumFrameTicks", ServerMaximumFrameTicks);

            PredictionManager prediction = manager.GetComponent<PredictionManager>();
            if (prediction != null)
                prediction.SetMaximumServerReplicates(ServerMaximumReplicates);

            RaiseServerProcessPriority();
        }

        public static void ApplyClient(NetworkManager manager)
        {
            if (manager == null)
                return;
            PredictionManager prediction = manager.GetComponent<PredictionManager>();
            if (prediction != null)
                SetField(prediction, "_minimumClientReconcileFramerate", ClientMinimumReconcileFramerate);
        }

        /// <summary>Le serveur passe devant le jeu et les autres programmes quand le PC est saturé.</summary>
        private static void RaiseServerProcessPriority()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
                TimerResolution.Begin();
                GameLog.Info(LogTag, "Serveur : priorité haute, minuterie 1 ms.");
            }
            catch (Exception e)
            {
                GameLog.Warn(LogTag, "Priorité du serveur non modifiée : " + e.Message);
            }
#endif
        }

        /// <summary>Écrit un réglage privé de FishNet (pas d'accesseur public) ; journalise si le champ a disparu.</summary>
        private static void SetField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(T))
            {
                GameLog.Warn(LogTag, "Réglage FishNet introuvable : " + target.GetType().Name + "." + name + " (version de FishNet changée ?)");
                return;
            }
            field.SetValue(target, value);
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>Minuterie Windows à 1 ms tant que le serveur tourne (rendue à la fermeture).</summary>
        private static class TimerResolution
        {
            private static bool s_active;

            [System.Runtime.InteropServices.DllImport("winmm.dll")]
            private static extern uint timeBeginPeriod(uint period);

            [System.Runtime.InteropServices.DllImport("winmm.dll")]
            private static extern uint timeEndPeriod(uint period);

            public static void Begin()
            {
                if (s_active)
                    return;
                s_active = timeBeginPeriod(1) == 0;
                if (s_active)
                    Application.quitting += End;
            }

            private static void End()
            {
                if (!s_active)
                    return;
                timeEndPeriod(1);
                s_active = false;
            }
        }
#endif
    }
}
