using System;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Client.Phone
{
    /// <summary>
    /// Voix des appels téléphoniques, côté client : micro → trames de 40 ms en ADPCM 16 kHz → serveur
    /// → correspondant → haut-parleur.
    /// <list type="bullet">
    /// <item>Le micro n'est ouvert que pendant une communication (permission demandée au premier appel).</item>
    /// <item>Porte de bruit : rien n'est envoyé dans les silences (économie de données mobiles), avec
    /// 300 ms de traîne pour ne pas couper les fins de mots.</item>
    /// <item>Lecture : tampon anti-gigue de 120 ms ; une trame manquante est remplacée par du silence.</item>
    /// </list>
    /// Aucune allocation pendant l'appel : tampons préalloués, lecture sur le thread audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChat : MonoBehaviour
    {
        public const int SampleRate = 16000;
        public const int FrameSamples = SampleRate / 25; // 40 ms

        private const float GateThreshold = 0.012f; // RMS au-dessous duquel on considère le silence
        private const float GateHangover = 0.3f;
        private const int JitterSamples = SampleRate * 120 / 1000;
        private const int RingSize = SampleRate * 2;

        /// <summary>Micro coupé par le joueur (bouton de l'interface).</summary>
        public static bool Muted { get; set; }

        /// <summary>Niveau du micro (0 à 1), pour l'indicateur de l'interface.</summary>
        public static float InputLevel { get; private set; }

        // --- Capture ---
        private AudioClip _micClip;
        private string _micDevice;
        private int _micRate;
        private int _readPosition;
        private readonly float[] _micFrame = new float[FrameSamples];
        private float[] _micRaw;
        private readonly byte[] _encoded = new byte[ImaAdpcm.EncodedSize(FrameSamples)];
        private int _predictor, _stepIndex;
        private float _lastVoiceTime = -10f;

        // --- Lecture ---
        private AudioSource _speaker;
        private readonly float[] _ring = new float[RingSize];
        private readonly float[] _decoded = new float[FrameSamples * 2];
        private readonly object _ringLock = new object();
        private int _ringWrite, _ringRead, _ringCount;
        private bool _buffering = true;

        private bool _inCall;
#pragma warning disable CS0414 // utilisé seulement sur Android
        private bool _permissionAsked;
#pragma warning restore CS0414

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            _speaker = gameObject.AddComponent<AudioSource>();
            _speaker.clip = AudioClip.Create("PhoneVoice", SampleRate, 1, SampleRate, true, OnAudioRead);
            _speaker.loop = true;
            _speaker.spatialBlend = 0f; // voix « dans l'oreille », pas dans la rue
            _speaker.playOnAwake = false;
        }

        private void OnEnable()
        {
            PlayerPhone.LocalCallStateChanged += OnCallState;
            PlayerPhone.LocalVoiceReceived += OnVoice;
            PlayerVoice.FrameHeard += OnProximityFrame;
        }

        private void OnDisable()
        {
            PlayerPhone.LocalCallStateChanged -= OnCallState;
            PlayerPhone.LocalVoiceReceived -= OnVoice;
            PlayerVoice.FrameHeard -= OnProximityFrame;
            StopCall();
            StopMicrophone();
        }

        /// <summary>Voix de proximité entendue : jouée en 3D depuis le personnage qui parle.</summary>
        private static void OnProximityFrame(PlayerVoice speaker, ArraySegment<byte> frame)
        {
            if (speaker == null)
                return;
            if (!speaker.TryGetComponent(out ProximitySpeaker output))
                output = speaker.gameObject.AddComponent<ProximitySpeaker>();
            output.Push(frame);
        }

        /// <summary>Micro ouvert : en appel, ou micro de proximité activé (bouton du HUD).</summary>
        private bool WantsMicrophone => _inCall || (ProximityVoice.Enabled && PlayerVoice.Local != null);

        private void OnCallState(CallState state, int peer, string peerName, string reason)
        {
            if (state == CallState.Connected)
                StartCall();
            else
                StopCall();
        }

        #region Appel

        private void StartCall()
        {
            if (_inCall)
                return;
            _inCall = true;
            _permissionAsked = false;
            ClearRing();
            _speaker.Play();
        }

        private void StopCall()
        {
            if (!_inCall)
                return;
            _inCall = false;
            if (_speaker != null)
                _speaker.Stop();
        }

        private void StopMicrophone()
        {
            if (_micClip != null)
            {
                Microphone.End(_micDevice);
                _micClip = null;
            }
            InputLevel = 0f;
        }

        private void StartMicrophone()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                // Demande une seule fois par appel ; Update revérifie ensuite sans redemander.
                if (!_permissionAsked)
                {
                    _permissionAsked = true;
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                }
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
                return;

            _micDevice = null; // micro par défaut
            Microphone.GetDeviceCaps(_micDevice, out int min, out int max);
            _micRate = (min == 0 && max == 0) ? SampleRate : Mathf.Clamp(SampleRate, min, max);
            _micRaw = new float[Mathf.CeilToInt(FrameSamples * (_micRate / (float)SampleRate)) + 1];
            _micClip = Microphone.Start(_micDevice, true, 1, _micRate);
            _readPosition = 0;
            _predictor = 0;
            _stepIndex = 0;
        }

        #endregion

        #region Capture

        private void Update()
        {
            if (!WantsMicrophone)
            {
                if (_micClip != null)
                    StopMicrophone();
                _permissionAsked = false;
                return;
            }

            if (_micClip == null)
            {
                StartMicrophone(); // première ouverture, ou permission accordée entre-temps
                return;
            }

            PlayerPhone phone = PlayerPhone.Local;
            int position = Microphone.GetPosition(_micDevice);
            int length = _micClip.samples;
            int needed = _micRaw.Length - 1;

            // Lit toutes les trames complètes disponibles depuis la dernière lecture.
            while (((position - _readPosition + length) % length) >= needed)
            {
                _micClip.GetData(_micRaw, _readPosition);
                _readPosition = (_readPosition + needed) % length;
                Resample(_micRaw, needed, _micFrame);
                SendFrame(phone);
            }
        }

        private static void Resample(float[] source, int sourceCount, float[] target)
        {
            if (sourceCount == target.Length)
            {
                Array.Copy(source, target, target.Length);
                return;
            }
            float ratio = (sourceCount - 1) / (float)(target.Length - 1);
            for (int i = 0; i < target.Length; i++)
            {
                float position = i * ratio;
                int index = (int)position;
                float t = position - index;
                target[i] = Mathf.Lerp(source[index], source[Mathf.Min(index + 1, sourceCount - 1)], t);
            }
        }

        private void SendFrame(PlayerPhone phone)
        {
            float energy = 0f;
            for (int i = 0; i < FrameSamples; i++)
                energy += _micFrame[i] * _micFrame[i];
            float rms = Mathf.Sqrt(energy / FrameSamples);
            InputLevel = Mathf.Clamp01(rms * 8f);

            if (rms > GateThreshold)
                _lastVoiceTime = Time.unscaledTime;
            bool speaking = Time.unscaledTime - _lastVoiceTime < GateHangover;
            if (Muted || !speaking)
                return;

            // En appel : au correspondant seulement. Sinon : aux joueurs proches (micro de proximité).
            int size = ImaAdpcm.Encode(_micFrame, FrameSamples, _encoded, ref _predictor, ref _stepIndex);
            if (_inCall)
            {
                if (phone != null)
                    phone.SendVoice(new ArraySegment<byte>(_encoded, 0, size));
            }
            else if (ProximityVoice.Enabled && PlayerVoice.Local != null)
            {
                PlayerVoice.Local.SendFrame(new ArraySegment<byte>(_encoded, 0, size));
            }
        }

        #endregion

        #region Lecture

        private void OnVoice(ArraySegment<byte> frame)
        {
            if (!_inCall || frame.Array == null)
                return;

            int samples = ImaAdpcm.Decode(frame.Array, frame.Offset, frame.Count, _decoded);
            lock (_ringLock)
            {
                for (int i = 0; i < samples; i++)
                {
                    if (_ringCount == RingSize)
                    {
                        // Trop de retard accumulé : on jette le plus ancien pour rester en temps réel.
                        _ringRead = (_ringRead + 1) % RingSize;
                        _ringCount--;
                    }
                    _ring[_ringWrite] = _decoded[i];
                    _ringWrite = (_ringWrite + 1) % RingSize;
                    _ringCount++;
                }
            }
        }

        /// <summary>Thread audio : remplit le haut-parleur depuis le tampon (silence si vide).</summary>
        private void OnAudioRead(float[] data)
        {
            lock (_ringLock)
            {
                if (_buffering && _ringCount >= JitterSamples)
                    _buffering = false;

                for (int i = 0; i < data.Length; i++)
                {
                    if (_buffering || _ringCount == 0)
                    {
                        data[i] = 0f;
                        if (_ringCount == 0)
                            _buffering = true; // reconstitue un petit tampon avant de reprendre
                        continue;
                    }
                    data[i] = _ring[_ringRead];
                    _ringRead = (_ringRead + 1) % RingSize;
                    _ringCount--;
                }
            }
        }

        private void ClearRing()
        {
            lock (_ringLock)
            {
                _ringWrite = _ringRead = _ringCount = 0;
                _buffering = true;
            }
        }

        #endregion
    }
}
