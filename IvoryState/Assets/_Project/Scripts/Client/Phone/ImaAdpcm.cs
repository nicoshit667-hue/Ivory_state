namespace IvoryState.Client.Phone
{
    /// <summary>
    /// Codec IMA ADPCM 4 bits (standard, domaine public) : 16 bits → 4 bits par échantillon, soit
    /// ~8 Ko/s à 16 kHz. Qualité téléphone correcte, coût CPU négligeable, aucune dépendance.
    /// <para>
    /// Chaque trame est autonome (en-tête : prédicteur 16 bits + index de pas) : une trame perdue sur
    /// le canal non fiable ne dégrade pas les suivantes. Aucune allocation.
    /// </para>
    /// </summary>
    public static class ImaAdpcm
    {
        public const int HeaderSize = 4;

        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88,
            97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660,
            4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818,
            18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };

        /// <summary>Taille encodée d'une trame de <paramref name="samples"/> échantillons.</summary>
        public static int EncodedSize(int samples) => HeaderSize + (samples + 1) / 2;

        /// <summary>Encode <paramref name="count"/> échantillons [-1, 1] ; renvoie le nombre d'octets écrits.</summary>
        public static int Encode(float[] samples, int count, byte[] output, ref int predictor, ref int index)
        {
            output[0] = (byte)(predictor & 0xFF);
            output[1] = (byte)((predictor >> 8) & 0xFF);
            output[2] = (byte)index;
            output[3] = 0;

            int o = HeaderSize;
            for (int i = 0; i < count; i++)
            {
                int sample = (int)(UnityEngine.Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
                int step = StepTable[index];
                int diff = sample - predictor;
                int code = 0;
                if (diff < 0) { code = 8; diff = -diff; }

                int delta = step >> 3;
                if (diff >= step) { code |= 4; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 2; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 1; delta += step; }

                predictor += (code & 8) != 0 ? -delta : delta;
                predictor = predictor < -32768 ? -32768 : predictor > 32767 ? 32767 : predictor;
                index += IndexTable[code];
                index = index < 0 ? 0 : index > 88 ? 88 : index;

                if ((i & 1) == 0)
                    output[o] = (byte)code;
                else
                    output[o++] |= (byte)(code << 4);
            }
            if ((count & 1) == 1)
                o++;
            return o;
        }

        /// <summary>Décode une trame ; renvoie le nombre d'échantillons écrits dans <paramref name="output"/>.</summary>
        public static int Decode(byte[] input, int offset, int length, float[] output)
        {
            if (length < HeaderSize)
                return 0;

            int predictor = (short)(input[offset] | (input[offset + 1] << 8));
            int index = input[offset + 2];
            if (index > 88) index = 88;

            int samples = 0;
            int max = output.Length;
            for (int b = offset + HeaderSize; b < offset + length && samples < max; b++)
            {
                for (int nibble = 0; nibble < 2 && samples < max; nibble++)
                {
                    int code = nibble == 0 ? input[b] & 0x0F : input[b] >> 4;
                    int step = StepTable[index];
                    int delta = step >> 3;
                    if ((code & 4) != 0) delta += step;
                    if ((code & 2) != 0) delta += step >> 1;
                    if ((code & 1) != 0) delta += step >> 2;
                    predictor += (code & 8) != 0 ? -delta : delta;
                    predictor = predictor < -32768 ? -32768 : predictor > 32767 ? 32767 : predictor;
                    index += IndexTable[code];
                    index = index < 0 ? 0 : index > 88 ? 88 : index;
                    output[samples++] = predictor / 32768f;
                }
            }
            return samples;
        }
    }
}
