using System;
using IvoryState.Gameplay.Social;
using UnityEngine;

namespace IvoryState.Client.Phone
{
    /// <summary>État du micro de proximité (bouton du HUD). Mémorisé sur l'appareil.</summary>
    public static class ProximityVoice
    {
        private const string PrefsKey = "ivory.proximityMic";
        private static bool? s_enabled;

        /// <summary>Micro de proximité actif : les joueurs autour de soi entendent.</summary>
        public static bool Enabled
        {
            get
            {
                if (s_enabled == null)
                    s_enabled = PlayerPrefs.GetInt(PrefsKey, 0) == 1;
                return s_enabled.Value;
            }
            set
            {
                s_enabled = value;
                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_enabled = null;
    }

    /// <summary>
    /// Haut-parleur 3D posé sur un joueur qui parle à proximité : la voix vient de son personnage et
    /// s'atténue avec la distance (silencieuse à <see cref="PlayerVoice.Range"/> m). Tampon anti-gigue
    /// de 120 ms, décodage ADPCM, lecture sur le thread audio sans allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProximitySpeaker : MonoBehaviour
    {
        private const int JitterSamples = VoiceChat.SampleRate * 120 / 1000;
        private const int RingSize = VoiceChat.SampleRate * 2;

        private AudioSource _source;
        private readonly float[] _ring = new float[RingSize];
        private readonly float[] _decoded = new float[VoiceChat.FrameSamples * 2];
        private readonly object _lock = new object();
        private int _write, _read, _count;
        private bool _buffering = true;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = AudioClip.Create("ProximityVoice", VoiceChat.SampleRate, 1, VoiceChat.SampleRate, true, OnAudioRead);
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = PlayerVoice.Range;
            _source.dopplerLevel = 0f;
            _source.playOnAwake = false;
            _source.Play();
        }

        public void Push(ArraySegment<byte> frame)
        {
            if (frame.Array == null)
                return;
            int samples = ImaAdpcm.Decode(frame.Array, frame.Offset, frame.Count, _decoded);
            lock (_lock)
            {
                for (int i = 0; i < samples; i++)
                {
                    if (_count == RingSize)
                    {
                        _read = (_read + 1) % RingSize;
                        _count--;
                    }
                    _ring[_write] = _decoded[i];
                    _write = (_write + 1) % RingSize;
                    _count++;
                }
            }
        }

        private void OnAudioRead(float[] data)
        {
            lock (_lock)
            {
                if (_buffering && _count >= JitterSamples)
                    _buffering = false;
                for (int i = 0; i < data.Length; i++)
                {
                    if (_buffering || _count == 0)
                    {
                        data[i] = 0f;
                        if (_count == 0)
                            _buffering = true;
                        continue;
                    }
                    data[i] = _ring[_read];
                    _read = (_read + 1) % RingSize;
                    _count--;
                }
            }
        }
    }
}
