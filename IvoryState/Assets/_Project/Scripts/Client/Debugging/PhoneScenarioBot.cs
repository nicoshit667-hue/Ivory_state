#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using IvoryState.Client.Phone;
using IvoryState.Core.Diagnostics;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Client.Debugging
{
    /// <summary>
    /// Test de bout en bout du téléphone (builds de dev, clients headless sans micro) :
    /// <list type="bullet">
    /// <item><c>-e2ePhoneAnswer</c> : décroche tout appel entrant, compte les trames de voix reçues et
    /// les décode ; à la fin de l'appel, écrit <c>[E2E-PHONE] RÉUSSI</c> si la voix est bien arrivée.</item>
    /// <item><c>-e2ePhoneCall Pseudo</c> : demande l'annuaire, appelle ce joueur, envoie 2 s de voix
    /// synthétique (un « la » à 440 Hz encodé en ADPCM) une fois en ligne, puis raccroche.</item>
    /// </list>
    /// </summary>
    internal sealed class PhoneScenarioBot : MonoBehaviour
    {
        private const string LogTag = "E2E-PHONE";
        private const float VoiceSeconds = 2f;

        private string _target;
        private bool _answer;
        private float _nextDirectory;
        private bool _called;
        private float _connectedAt = -1f;
        private float _nextFrameAt;
        private int _framesSent;
        private int _framesReceived;
        private float _peakReceived;
        private bool _wasConnected;

        private readonly float[] _tone = new float[VoiceChat.FrameSamples];
        private readonly float[] _decoded = new float[VoiceChat.FrameSamples * 2];
        private readonly byte[] _encoded = new byte[ImaAdpcm.EncodedSize(VoiceChat.FrameSamples)];
        private int _predictor, _stepIndex;
        private double _phase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int call = Array.IndexOf(args, "-e2ePhoneCall");
            bool answer = Array.IndexOf(args, "-e2ePhoneAnswer") >= 0;
            if (call < 0 && !answer)
                return;

            var go = new GameObject(nameof(PhoneScenarioBot));
            DontDestroyOnLoad(go);
            var bot = go.AddComponent<PhoneScenarioBot>();
            bot._answer = answer;
            bot._target = call >= 0 && call + 1 < args.Length ? args[call + 1] : null;
        }

        private void OnEnable()
        {
            PlayerPhone.LocalDirectoryReceived += OnDirectory;
            PlayerPhone.LocalCallStateChanged += OnState;
            PlayerPhone.LocalVoiceReceived += OnVoice;
            PlayerFriends.LocalFriendRequest += OnFriendRequest;
        }

        private void OnDisable()
        {
            PlayerPhone.LocalDirectoryReceived -= OnDirectory;
            PlayerPhone.LocalCallStateChanged -= OnState;
            PlayerPhone.LocalVoiceReceived -= OnVoice;
            PlayerFriends.LocalFriendRequest -= OnFriendRequest;
        }

        // On n'appelle que ses amis : l'appelé accepte toute demande d'ami.
        private void OnFriendRequest(int fromObjectId, string fromName)
        {
            if (!_answer)
                return;
            GameLog.Info(LogTag, "demande d'ami de " + fromName + " acceptée.");
            PlayerFriends.Local?.Respond(fromObjectId, true);
        }

        private bool _requested;
        private float _callAt = -1f;
        private int _retries;

        private void Update()
        {
            PlayerPhone phone = PlayerPhone.Local;
            if (phone == null)
                return;

            // Appelant : annuaire toutes les 3 s jusqu'à trouver la cible.
            if (_target != null && !_called && Time.time >= _nextDirectory)
            {
                _nextDirectory = Time.time + 3f;
                phone.RequestDirectory();
            }

            // En ligne : envoie une trame toutes les 40 ms pendant 2 s, puis raccroche.
            if (_target != null && _connectedAt >= 0f && Time.time >= _nextFrameAt)
            {
                if (Time.time - _connectedAt > VoiceSeconds)
                {
                    GameLog.Info(LogTag, _framesSent + " trames de voix envoyées, on raccroche.");
                    _connectedAt = -1f;
                    phone.HangUp();
                    return;
                }
                _nextFrameAt = Time.time + 0.04f;
                for (int i = 0; i < _tone.Length; i++)
                {
                    _tone[i] = 0.5f * (float)Math.Sin(_phase);
                    _phase += 2.0 * Math.PI * 440.0 / VoiceChat.SampleRate;
                }
                int size = ImaAdpcm.Encode(_tone, _tone.Length, _encoded, ref _predictor, ref _stepIndex);
                phone.SendVoice(new ArraySegment<byte>(_encoded, 0, size));
                _framesSent++;
            }
        }

        private void OnDirectory(int[] ids, string[] names)
        {
            if (_target == null || _called)
                return;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != _target)
                    continue;
                // D'abord devenir amis (les deux bots apparaissent côte à côte), puis appeler 3 s plus tard.
                if (!_requested)
                {
                    _requested = true;
                    _callAt = Time.time + 3f;
                    GameLog.Info(LogTag, "demande d'ami à " + _target + ".");
                    PlayerFriends.Local?.SendRequest(ids[i]);
                    return;
                }
                if (Time.time < _callAt)
                    return;
                _called = true;
                GameLog.Info(LogTag, "appel de " + _target + ".");
                PlayerPhone.Local.Call(ids[i]);
                return;
            }
        }

        private void OnState(CallState state, int peer, string peerName, string reason)
        {
            GameLog.Info(LogTag, "état " + state + (peerName.Length > 0 ? " avec " + peerName : "") +
                                 (reason.Length > 0 ? " (" + reason + ")" : ""));

            if (state == CallState.Ringing && _answer)
                PlayerPhone.Local.Answer(true);

            // Appel refusé parce que l'amitié n'est pas encore enregistrée (serveur chargé) : on réessaie.
            if (state == CallState.Idle && !_wasConnected && _target != null && reason.Contains("amis") && _retries < 5)
            {
                _retries++;
                _called = false;
                _callAt = Time.time + 3f;
                GameLog.Info(LogTag, "pas encore amis, nouvel essai dans 3 s (" + _retries + "/5).");
            }

            if (state == CallState.Connected)
            {
                _wasConnected = true;
                if (_target != null)
                    _connectedAt = Time.time;
            }

            if (state == CallState.Idle && _wasConnected && _answer)
            {
                // ~50 trames attendues ; on tolère des pertes (canal non fiable) et on vérifie que le son
                // décodé n'est pas du silence.
                bool ok = _framesReceived >= 25 && _peakReceived > 0.2f;
                GameLog.Info(LogTag, (ok ? "RÉUSSI" : "ÉCHEC") + " : " + _framesReceived + " trames reçues, crête " +
                                     _peakReceived.ToString("0.00") + ".");
                // Le journal du lecteur est mis en mémoire tampon : on quitte pour qu'il soit écrit tout de suite.
                Invoke(nameof(Quit), 1f);
            }
        }

        private void Quit() => Application.Quit();

        private void OnVoice(ArraySegment<byte> frame)
        {
            _framesReceived++;
            int samples = ImaAdpcm.Decode(frame.Array, frame.Offset, frame.Count, _decoded);
            for (int i = 0; i < samples; i++)
                _peakReceived = Mathf.Max(_peakReceived, Mathf.Abs(_decoded[i]));
        }
    }
}
#endif
