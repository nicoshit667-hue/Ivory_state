using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace IvoryState.Core.Networking
{
    /// <summary>
    /// Limite le nombre d'images par seconde d'un processus headless (<c>-batchmode</c>).
    /// <para>
    /// En batchmode, Unity ignore <see cref="Application.targetFrameRate"/> : un serveur sans
    /// limiteur boucle à plusieurs centaines d'images/s et consomme du CPU pour rien (mesuré :
    /// 342 images/s pour un tick à 30 Hz). Ce composant dort en fin de frame pour tenir la cadence.
    /// </para>
    /// <para>Ajouté automatiquement par <see cref="NetworkBootstrap"/> en batchmode uniquement.</para>
    /// </summary>
    [DefaultExecutionOrder(int.MaxValue)] // dernier LateUpdate de la frame
    [DisallowMultipleComponent]
    public sealed class HeadlessFrameLimiter : MonoBehaviour
    {
        private readonly Stopwatch _frameTimer = new Stopwatch();
        private double _targetFrameSeconds = 1.0 / 60.0;

        public void SetTargetFrameRate(int framesPerSecond)
        {
            _targetFrameSeconds = 1.0 / Mathf.Max(1, framesPerSecond);
        }

        private void OnEnable()
        {
            _frameTimer.Restart();
        }

        private void LateUpdate()
        {
            // Temps de travail de la frame (depuis la fin du sommeil précédent).
            double remaining = _targetFrameSeconds - _frameTimer.Elapsed.TotalSeconds;
            if (remaining > 0.001)
                Thread.Sleep((int)(remaining * 1000.0));

            _frameTimer.Restart();
        }
    }
}
