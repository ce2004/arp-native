using System;
using System.Collections.Generic;

namespace Arp
{
    /// <summary>
    /// The built-in sound set, generated in code rather than shipped as files
    /// so the whole application stays a single executable with no sounds
    /// folder beside it.
    ///
    /// Everything here is deliberately short and soft-edged. These fire while
    /// a screen reader is talking, so a cue that rings, clips or lingers gets
    /// in the way; each one fades in and out to avoid the click a raw sine
    /// start would produce.
    /// </summary>
    internal static class SoundLibrary
    {
        public const int Rate = 44100;
        public const string None = "None";

        /// <summary>Selectable names, in the order the settings combo shows them.</summary>
        public static readonly string[] Names =
        {
            None,
            "Rising Sweep",
            "Falling Sweep",
            "Low Double Beep",
            "High Double Beep",
            "Two Tone Up",
            "Two Tone Down",
            "Soft Chime",
            "Short Blip",
            "Triple Blip",
            "Low Thud",
            "Alert Warble",
        };

        private static readonly Dictionary<string, short[]> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();

        public static bool IsKnown(string name)
        {
            foreach (string n in Names)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static short[] Get(string name)
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, None, StringComparison.OrdinalIgnoreCase))
                return Array.Empty<short>();

            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                var rendered = Render(name);
                Cache[name] = rendered;
                return rendered;
            }
        }

        public static void Preload()
        {
            foreach (string n in Names) Get(n);
        }

        internal static short[] Render(string name) => name switch
        {
            // The first four reproduce the original cues bit for bit, including
            // the 400-sample fades, so existing installs sound unchanged.
            "Rising Sweep" => Sweep(440, 880, 200),
            "Falling Sweep" => Sweep(880, 440, 200),
            "Low Double Beep" => DoubleBeep(400),
            "High Double Beep" => DoubleBeep(800),

            "Two Tone Up" => TwoTone(523.25, 783.99),
            "Two Tone Down" => TwoTone(783.99, 523.25),
            "Soft Chime" => Chime(880, 1320, 380),
            "Short Blip" => Blip(1000, 60),
            "Triple Blip" => TripleBlip(1200),
            "Low Thud" => Thud(150, 170),
            "Alert Warble" => Warble(700, 900, 3, 60),
            _ => Array.Empty<short>(),
        };

        // ---- generators ----

        /// <summary>Linear frequency sweep with a 400-sample fade at each end.</summary>
        private static short[] Sweep(double fStart, double fEnd, int durationMs)
        {
            int n = (int)(Rate * (durationMs / 1000.0));
            var outp = new short[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                double freq = fStart + (fEnd - fStart) * ((double)i / n);
                phase += 2 * Math.PI * freq / Rate;
                outp[i] = (short)(int)(Math.Sin(phase) * Fade(i, n, 400) * 32767);
            }
            return outp;
        }

        private static short[] DoubleBeep(double freq, int durationMs = 80, int gapMs = 40)
        {
            int beat = (int)(Rate * (durationMs / 1000.0));
            int gap = (int)(Rate * (gapMs / 1000.0));
            var outp = new short[beat * 2 + gap];
            int p = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < beat; i++)
                {
                    double t = (double)i / Rate;
                    outp[p++] = (short)(int)(Math.Sin(2 * Math.PI * freq * t) * Fade(i, beat, 400) * 32767);
                }
                if (pass == 0) p += gap;
            }
            return outp;
        }

        /// <summary>Two discrete notes, a clearer "something changed" than a sweep.</summary>
        private static short[] TwoTone(double f1, double f2, int noteMs = 95, int gapMs = 25)
        {
            int note = (int)(Rate * (noteMs / 1000.0));
            int gap = (int)(Rate * (gapMs / 1000.0));
            var outp = new short[note * 2 + gap];
            int p = 0;
            foreach (double f in new[] { f1, f2 })
            {
                for (int i = 0; i < note; i++)
                {
                    double t = (double)i / Rate;
                    outp[p++] = (short)(int)(Math.Sin(2 * Math.PI * f * t) * Fade(i, note, 500) * 0.85 * 32767);
                }
                if (p < outp.Length && f == f1) p += gap;
            }
            return outp;
        }

        /// <summary>Two partials with an exponential decay, so it reads as a bell.</summary>
        private static short[] Chime(double f1, double f2, int durationMs)
        {
            int n = (int)(Rate * (durationMs / 1000.0));
            var outp = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double decay = Math.Exp(-t * 9.0);
                double v = (Math.Sin(2 * Math.PI * f1 * t) * 0.65 +
                            Math.Sin(2 * Math.PI * f2 * t) * 0.35) * decay;
                // Attack only; the decay handles the tail.
                if (i < 200) v *= i / 200.0;
                outp[i] = (short)(int)(v * 0.9 * 32767);
            }
            return outp;
        }

        private static short[] Blip(double freq, int durationMs)
        {
            int n = (int)(Rate * (durationMs / 1000.0));
            var outp = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                outp[i] = (short)(int)(Math.Sin(2 * Math.PI * freq * t) * Fade(i, n, 300) * 0.8 * 32767);
            }
            return outp;
        }

        private static short[] TripleBlip(double freq, int noteMs = 45, int gapMs = 35)
        {
            int note = (int)(Rate * (noteMs / 1000.0));
            int gap = (int)(Rate * (gapMs / 1000.0));
            var outp = new short[note * 3 + gap * 2];
            int p = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < note; i++)
                {
                    double t = (double)i / Rate;
                    outp[p++] = (short)(int)(Math.Sin(2 * Math.PI * freq * t) * Fade(i, note, 250) * 0.8 * 32767);
                }
                if (pass < 2) p += gap;
            }
            return outp;
        }

        /// <summary>A low, quickly damped tone; unobtrusive for frequent events.</summary>
        private static short[] Thud(double freq, int durationMs)
        {
            int n = (int)(Rate * (durationMs / 1000.0));
            var outp = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double decay = Math.Exp(-t * 16.0);
                double v = Math.Sin(2 * Math.PI * freq * t) * decay;
                if (i < 150) v *= i / 150.0;
                outp[i] = (short)(int)(v * 0.95 * 32767);
            }
            return outp;
        }

        /// <summary>Alternating pitches, which reads as attention-seeking without being harsh.</summary>
        private static short[] Warble(double fLow, double fHigh, int cycles, int stepMs)
        {
            int step = (int)(Rate * (stepMs / 1000.0));
            var outp = new short[step * cycles * 2];
            int p = 0;
            for (int c = 0; c < cycles; c++)
            {
                foreach (double f in new[] { fLow, fHigh })
                {
                    for (int i = 0; i < step; i++)
                    {
                        double t = (double)i / Rate;
                        outp[p++] = (short)(int)(Math.Sin(2 * Math.PI * f * t) * Fade(i, step, 200) * 0.8 * 32767);
                    }
                }
            }
            return outp;
        }

        /// <summary>Linear fade in and out over <paramref name="ramp"/> samples.</summary>
        private static double Fade(int i, int n, int ramp)
        {
            if (ramp <= 0 || n <= 0) return 1.0;
            if (i < ramp) return i / (double)ramp;
            if (i > n - ramp) return (n - i) / (double)ramp;
            return 1.0;
        }
    }
}
