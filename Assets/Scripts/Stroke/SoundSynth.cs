using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// The sounds of the lake, synthesised (REQ-0002-FUNC, REQ-0004-FUNC): the blade going in
    /// and coming out, lapping water, wind, water running under the hull, and birdsong.
    ///
    /// Synthesised rather than recorded so there is nothing to license and no binary audio
    /// in LFS, and so every sound can be tuned in code. Each call renders mono samples at
    /// <see cref="SampleRate"/>, from a seed, so a sound is identical every run and variants
    /// come from different seeds. Loops are cross-faded end into start, so they repeat
    /// without a click. Engine-free, like the rest of this assembly, so it is tested under
    /// make test (SoundSynthTests).
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 48000;

        /// <summary>
        /// The catch: a low "blup" as the blade enters, a splash, and a scatter of droplets.
        /// </summary>
        public static float[] Catch(int seed)
        {
            var random = new Random(seed);
            var n = Samples(0.5f);
            var output = new float[n];

            // Blup: a falling tone, over in a few tens of milliseconds.
            var phase = 0d;
            var start = 150f + (float)random.NextDouble() * 40f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var f = 80f + (start - 80f) * (float)Math.Exp(-t / 0.045f);
                phase += 2d * Math.PI * f / SampleRate;
                output[i] += (float)Math.Sin(phase) * 0.55f * Envelope(t, 0.004f, 0.07f);
            }

            // Splash: bright noise, fast attack, quick decay.
            var splash = BandPass(Noise(n, random), 2100f + (float)random.NextDouble() * 500f, 0.8f);
            for (var i = 0; i < n; i++) output[i] += splash[i] * 1.6f * Envelope(i / (float)SampleRate, 0.005f, 0.11f);

            Droplets(output, random, 6, 0.06f, 0.38f, 0.22f);
            return Normalise(output, 0.9f);
        }

        /// <summary>The finish: the water swirling off the blade as it comes out, then drips.</summary>
        public static float[] Finish(int seed)
        {
            var random = new Random(seed);
            var n = Samples(0.65f);
            var swirl = BandPass(Noise(n, random), 850f + (float)random.NextDouble() * 250f, 1.1f);
            var output = new float[n];
            for (var i = 0; i < n; i++) output[i] = swirl[i] * 1.4f * Envelope(i / (float)SampleRate, 0.06f, 0.16f);
            Droplets(output, random, 4, 0.2f, 0.55f, 0.16f);
            return Normalise(output, 0.8f);
        }

        /// <summary>Water lapping at a shore or hull: gentle waves arriving every second or so. Loops.</summary>
        public static float[] Lapping(float seconds, int seed)
        {
            var random = new Random(seed);
            var n = Samples(seconds) + Samples(LoopFade);
            var water = LowPass(BandPass(Noise(n, random), 480f, 0.6f), 1400f);
            var level = new float[n];
            for (var i = 0; i < n; i++) level[i] = 0.18f;
            for (var t = 0f; t < n / (float)SampleRate; t += 0.7f + (float)random.NextDouble() * 0.9f)
            {
                var strength = 0.4f + (float)random.NextDouble() * 0.6f;
                var begin = (int)(t * SampleRate);
                for (var i = begin; i < n; i++)
                {
                    var e = Envelope((i - begin) / (float)SampleRate, 0.08f, 0.35f);
                    if (e < 0.001f && i > begin + SampleRate / 10) break;
                    level[i] += e * strength;
                }
            }
            for (var i = 0; i < n; i++) water[i] *= level[i];
            return Normalise(Loop(water), 0.7f);
        }

        /// <summary>A soft breeze: low noise swelling slowly. Loops.</summary>
        public static float[] Wind(float seconds, int seed)
        {
            var random = new Random(seed);
            var n = Samples(seconds) + Samples(LoopFade);
            var wind = LowPass(LowPass(Noise(n, random), 320f), 320f);
            var p1 = random.NextDouble() * 6.28;
            var p2 = random.NextDouble() * 6.28;
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)SampleRate;
                wind[i] *= (float)(0.55 + 0.3 * Math.Sin(2 * Math.PI * 0.07 * t + p1) + 0.15 * Math.Sin(2 * Math.PI * 0.19 * t + p2));
            }
            return Normalise(Loop(wind), 0.6f);
        }

        /// <summary>Water running along the hull: steady, slightly fluttering. Loops; its level follows boat speed.</summary>
        public static float[] HullRush(float seconds, int seed)
        {
            var random = new Random(seed);
            var n = Samples(seconds) + Samples(LoopFade);
            var rush = BandPass(Noise(n, random), 1300f, 0.5f);
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)SampleRate;
                rush[i] *= (float)(0.8 + 0.2 * Math.Sin(2 * Math.PI * 3.1 * t) * Math.Sin(2 * Math.PI * 0.37 * t));
            }
            return Normalise(Loop(rush), 0.6f);
        }

        /// <summary>Number of bird species <see cref="Bird"/> knows.</summary>
        public const int BirdSpecies = 4;

        /// <summary>
        /// A birdcall: 0 a two-note whistle, 1 a rising trill, 2 a warble, 3 a soft dove coo.
        /// The seed varies pitch and timing within the species.
        /// </summary>
        public static float[] Bird(int species, int seed)
        {
            var random = new Random(seed);
            var pitch = 0.9f + (float)random.NextDouble() * 0.2f;
            switch (((species % BirdSpecies) + BirdSpecies) % BirdSpecies)
            {
                case 0:
                {
                    var output = new float[Samples(0.5f)];
                    Tone(output, 0.02f, 0.17f, 2900f * pitch, 2950f * pitch, 0.8f, 28f);
                    Tone(output, 0.24f, 0.2f, 2400f * pitch, 2300f * pitch, 0.7f, 28f);
                    return Normalise(output, 0.8f);
                }
                case 1:
                {
                    var output = new float[Samples(0.75f)];
                    var chirps = 7 + random.Next(4);
                    for (var c = 0; c < chirps; c++)
                        Tone(output, 0.02f + c * 0.07f, 0.045f, 2500f * pitch, 3500f * pitch, 0.6f + c * 0.04f, 0f);
                    return Normalise(output, 0.75f);
                }
                case 2:
                {
                    var output = new float[Samples(0.8f)];
                    var phase = 0d;
                    for (var i = 0; i < output.Length; i++)
                    {
                        var t = i / (float)SampleRate;
                        var f = pitch * (2100f + 650f * (float)Math.Sin(2 * Math.PI * 8.5 * t) + 300f * t);
                        phase += 2d * Math.PI * f / SampleRate;
                        var env = Envelope(t, 0.05f, 0.25f) * (float)Math.Min(1.0, t / 0.05);
                        output[i] = (float)(Math.Sin(phase) + 0.2 * Math.Sin(2 * phase)) * env;
                    }
                    return Normalise(output, 0.7f);
                }
                default:
                {
                    var output = new float[Samples(1.3f)];
                    Tone(output, 0.02f, 0.35f, 620f * pitch, 560f * pitch, 0.8f, 0f);
                    Tone(output, 0.45f, 0.25f, 600f * pitch, 540f * pitch, 0.6f, 0f);
                    Tone(output, 0.8f, 0.4f, 610f * pitch, 520f * pitch, 0.7f, 0f);
                    return Normalise(output, 0.6f);
                }
            }
        }

        // ---- building blocks -------------------------------------------------------------

        /// <summary>Seconds of cross-fade added to a loop so its end runs into its start.</summary>
        public const float LoopFade = 0.5f;

        static int Samples(float seconds) => (int)(seconds * SampleRate);

        /// <summary>Linear attack, then exponential decay with time constant <paramref name="decay"/>.</summary>
        static float Envelope(float t, float attack, float decay) =>
            t < attack ? t / attack : (float)Math.Exp(-(t - attack) / decay);

        static float[] Noise(int n, Random random)
        {
            var noise = new float[n];
            for (var i = 0; i < n; i++) noise[i] = (float)(random.NextDouble() * 2d - 1d);
            return noise;
        }

        /// <summary>A pure tone with a pitch glide and optional vibrato, added into <paramref name="output"/>.</summary>
        static void Tone(float[] output, float start, float length, float from, float to, float amplitude, float vibrato)
        {
            var begin = Samples(start);
            var count = Samples(length);
            var phase = 0d;
            for (var k = 0; k < count && begin + k < output.Length; k++)
            {
                var t = k / (float)SampleRate;
                var u = t / length;
                var f = from + (to - from) * u + vibrato * (float)Math.Sin(2 * Math.PI * 30 * t);
                phase += 2d * Math.PI * f / SampleRate;
                // Rounded on and off, so notes do not click.
                var env = (float)Math.Sin(Math.PI * Math.Min(1f, u)) * amplitude;
                output[begin + k] += (float)(Math.Sin(phase) + 0.15 * Math.Sin(2 * phase)) * env;
            }
        }

        /// <summary>Short rising blips scattered through a window: water droplets.</summary>
        static void Droplets(float[] output, Random random, int count, float from, float to, float amplitude)
        {
            for (var d = 0; d < count; d++)
            {
                var start = from + (float)random.NextDouble() * (to - from);
                var f = 900f + (float)random.NextDouble() * 1600f;
                var begin = Samples(start);
                var phase = 0d;
                for (var k = 0; k < Samples(0.03f) && begin + k < output.Length; k++)
                {
                    var t = k / (float)SampleRate;
                    phase += 2d * Math.PI * f * (1f + t * 12f) / SampleRate;
                    output[begin + k] += (float)Math.Sin(phase) * amplitude * (0.5f + (float)random.NextDouble() * 0.5f) * Envelope(t, 0.001f, 0.009f);
                }
            }
        }

        /// <summary>Band-pass biquad (RBJ cookbook, constant peak gain).</summary>
        static float[] BandPass(float[] input, float centre, float q)
        {
            var w = 2d * Math.PI * centre / SampleRate;
            var alpha = Math.Sin(w) / (2d * q);
            var a0 = 1d + alpha;
            double b0 = alpha / a0, b2 = -alpha / a0, a1 = -2d * Math.Cos(w) / a0, a2 = (1d - alpha) / a0;
            var output = new float[input.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < input.Length; i++)
            {
                var y = b0 * input[i] + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = input[i]; y2 = y1; y1 = y;
                output[i] = (float)y;
            }
            return output;
        }

        static float[] LowPass(float[] input, float cutoff)
        {
            var a = 1f - (float)Math.Exp(-2d * Math.PI * cutoff / SampleRate);
            var output = new float[input.Length];
            var y = 0f;
            for (var i = 0; i < input.Length; i++) output[i] = y += a * (input[i] - y);
            return output;
        }

        /// <summary>
        /// Folds the last <see cref="LoopFade"/> seconds into the start, so the buffer's end
        /// runs straight on into its beginning.
        /// </summary>
        static float[] Loop(float[] input)
        {
            var fade = Samples(LoopFade);
            var n = input.Length - fade;
            var output = new float[n];
            for (var i = 0; i < n; i++)
            {
                if (i < fade)
                {
                    var w = i / (float)fade;
                    output[i] = input[i] * w + input[n + i] * (1f - w);
                }
                else
                {
                    output[i] = input[i];
                }
            }
            return output;
        }

        static float[] Normalise(float[] samples, float peak)
        {
            var max = 0f;
            foreach (var s in samples) max = Math.Max(max, Math.Abs(s));
            if (max < 1e-6f) return samples;
            var gain = peak / max;
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
            return samples;
        }
    }
}
