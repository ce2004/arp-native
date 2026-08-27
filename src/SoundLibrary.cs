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

        /// <summary>
        /// Selectable names, in the order the settings combo shows them:
        /// the original cues first, then melodies, notification tones, alerts
        /// and finally the short subtle ones.
        /// </summary>
        public static readonly string[] Names =
        {
            None,

            // The original four, unchanged.
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

            // Melodies
            "Major Triad Up",
            "Major Triad Down",
            "Minor Triad Up",
            "Minor Triad Down",
            "Perfect Fifth",
            "Octave Leap",
            "Fanfare",
            "Little Fanfare",
            "Pentatonic Run Up",
            "Pentatonic Run Down",
            "Question",
            "Answer",
            "Music Box",
            "Lullaby",
            "Waltz",
            "Skip Step",
            "Cascade",
            "Staircase",

            // Notification tones
            "Gentle Ping",
            "Bright Ping",
            "Soft Pop",
            "Bubble",
            "Marimba",
            "Wood Block",
            "Glass Tap",
            "Crystal",
            "Bell Ding",
            "Doorbell",
            "Elevator Chime",
            "Submarine Ping",
            "Radar Blip",
            "Harp Pluck",
            "Kalimba",
            "Celesta",

            // Alerts
            "Gentle Alert",
            "Urgent Alert",
            "Siren Sweep",
            "Warning Trill",
            "Error Low",
            "Error Double",
            "Attention Rise",
            "Attention Fall",
            "Klaxon",
            "Buzz",

            // Short and subtle
            "Tick",
            "Tock",
            "Soft Click",
            "Whoosh Up",
            "Whoosh Down",
            "Air Puff",
            "Heartbeat",
            "Pulse",
            "Drip",
            "Ripple",
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

            // ---- melodies ----
            "Major Triad Up" => Melody(new[] { C5, E5, G5 }, 110, 0, Voice.Soft),
            "Major Triad Down" => Melody(new[] { G5, E5, C5 }, 110, 0, Voice.Soft),
            "Minor Triad Up" => Melody(new[] { C5, Eb5, G5 }, 110, 0, Voice.Soft),
            "Minor Triad Down" => Melody(new[] { G5, Eb5, C5 }, 110, 0, Voice.Soft),
            "Perfect Fifth" => Melody(new[] { C5, G5 }, 140, 10, Voice.Soft),
            "Octave Leap" => Melody(new[] { C5, C6 }, 130, 10, Voice.Soft),
            "Fanfare" => Melody(new[] { C5, E5, G5, C6 }, 95, 0, Voice.Bright),
            "Little Fanfare" => Melody(new[] { G5, G5, C6 }, 90, 25, Voice.Bright),
            "Pentatonic Run Up" => Melody(new[] { C5, D5, E5, G5, A5 }, 70, 0, Voice.Pluck),
            "Pentatonic Run Down" => Melody(new[] { A5, G5, E5, D5, C5 }, 70, 0, Voice.Pluck),
            "Question" => Melody(new[] { E5, A5 }, 130, 20, Voice.Soft),
            "Answer" => Melody(new[] { A5, E5 }, 130, 20, Voice.Soft),
            "Music Box" => Melody(new[] { C6, E6, G5, C6 }, 95, 15, Voice.Bell),
            "Lullaby" => Melody(new[] { G5, E5, C5 }, 200, 30, Voice.Bell),
            "Waltz" => Melody(new[] { C5, G5, G5 }, 105, 20, Voice.Soft),
            "Skip Step" => Melody(new[] { C5, E5, D5, G5 }, 85, 10, Voice.Pluck),
            "Cascade" => Melody(new[] { C6, A5, G5, E5, C5 }, 65, 0, Voice.Bell),
            "Staircase" => Melody(new[] { C5, D5, E5, F5, G5 }, 65, 0, Voice.Soft),

            // ---- notification tones ----
            "Gentle Ping" => Struck(A5, 420, 7, 0.55),
            "Bright Ping" => Struck(E6, 340, 9, 0.5),
            "Soft Pop" => Pop(420, 70),
            "Bubble" => Bubble(),
            "Marimba" => Struck(C5, 300, 14, 0.35, 3.0),
            "Wood Block" => WoodBlock(),
            "Glass Tap" => Struck(D6, 260, 16, 0.3, 4.2),
            "Crystal" => Bells(new[] { C6, E6, G6 }, 520, 6),
            "Bell Ding" => Bells(new[] { A5, 2 * A5 }, 500, 5),
            "Doorbell" => Melody(new[] { E5, C5 }, 260, 20, Voice.Bell),
            "Elevator Chime" => Melody(new[] { G5, C6 }, 220, 20, Voice.Bell),
            "Submarine Ping" => Struck(F5, 700, 4, 0.45, 2.5),
            "Radar Blip" => Blip(1600, 45),
            "Harp Pluck" => Struck(G5, 380, 10, 0.4, 2.2),
            "Kalimba" => Struck(E5, 340, 11, 0.4, 5.0),
            "Celesta" => Bells(new[] { G5, C6, E6 }, 460, 7),

            // ---- alerts ----
            "Gentle Alert" => Melody(new[] { A5, A5 }, 110, 45, Voice.Soft),
            "Urgent Alert" => Melody(new[] { A5, A5, A5 }, 80, 35, Voice.Bright),
            "Siren Sweep" => Siren(500, 1100, 2, 130),
            "Warning Trill" => Warble(880, 1046.5, 4, 45),
            "Error Low" => Struck(196, 380, 8, 0.7, 1.6),
            "Error Double" => Melody(new[] { 196.0, 174.61 }, 170, 25, Voice.Low),
            "Attention Rise" => Melody(new[] { C5, E5, G5, C6 }, 60, 0, Voice.Bright),
            "Attention Fall" => Melody(new[] { C6, G5, E5, C5 }, 60, 0, Voice.Bright),
            "Klaxon" => Siren(320, 520, 3, 110),
            "Buzz" => Buzz(160, 220),

            // ---- short and subtle ----
            "Tick" => Pop(1800, 22),
            "Tock" => Pop(900, 28),
            "Soft Click" => Pop(1200, 16),
            "Whoosh Up" => Whoosh(true),
            "Whoosh Down" => Whoosh(false),
            "Air Puff" => AirPuff(),
            "Heartbeat" => Heartbeat(),
            "Pulse" => Melody(new[] { 330.0, 330.0 }, 70, 60, Voice.Low),
            "Drip" => Drip(),
            "Ripple" => Melody(new[] { A5, C6, E6, C6 }, 55, 0, Voice.Bell),

            _ => Array.Empty<short>(),
        };

        // ---- note frequencies, equal temperament with A4 = 440 ----
        private const double C5 = 523.25, D5 = 587.33, Eb5 = 622.25, E5 = 659.25;
        private const double F5 = 698.46, G5 = 783.99, A5 = 880.00;
        private const double C6 = 1046.50, D6 = 1174.66, E6 = 1318.51, G6 = 1567.98;

        /// <summary>Timbre of a melody note.</summary>
        private enum Voice
        {
            Soft,   // rounded sine, gentle
            Bright, // a little second harmonic
            Pluck,  // fast decay, string-like
            Bell,   // struck, long decay, inharmonic partial
            Low,    // dark and short
        }

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

        // ------------------------------------------------------------------
        // Synthesis toolkit for the larger library.
        //
        // Everything is mixed into a floating point canvas and normalised once
        // at the end, so notes can overlap and ring into each other without any
        // risk of clipping, and every cue lands at a consistent loudness.
        // ------------------------------------------------------------------

        private sealed class Canvas
        {
            private readonly double[] _data;
            private uint _noiseState = 0x13579BDF; // fixed seed: cues must be reproducible

            public Canvas(int totalMs) => _data = new double[Math.Max(1, Ms(totalMs))];

            private static int Ms(int ms) => (int)(Rate * (ms / 1000.0));

            /// <summary>
            /// One exponentially decaying sine partial. A short attack ramp
            /// avoids the click a sine starting at full amplitude produces.
            /// </summary>
            public void Partial(int startMs, int lenMs, double freq, double amp, double decay)
            {
                int start = Ms(startMs), len = Ms(lenMs);
                int attack = Math.Min(Ms(3), Math.Max(1, len / 8));

                for (int i = 0; i < len; i++)
                {
                    int at = start + i;
                    if (at < 0 || at >= _data.Length) continue;

                    double t = i / (double)Rate;
                    double env = Math.Exp(-t * decay);
                    if (i < attack) env *= i / (double)attack;
                    // Taper the last stretch so a truncated tail cannot click.
                    int fromEnd = len - i;
                    if (fromEnd < attack) env *= fromEnd / (double)attack;

                    _data[at] += Math.Sin(2 * Math.PI * freq * t) * amp * env;
                }
            }

            /// <summary>A frequency sweep, used for whooshes and sirens.</summary>
            public void Sweep(int startMs, int lenMs, double from, double to, double amp, double decay = 0)
            {
                int start = Ms(startMs), len = Ms(lenMs);
                int attack = Math.Min(Ms(4), Math.Max(1, len / 8));
                double phase = 0;

                for (int i = 0; i < len; i++)
                {
                    int at = start + i;
                    double freq = from + (to - from) * (i / (double)len);
                    phase += 2 * Math.PI * freq / Rate;
                    if (at < 0 || at >= _data.Length) continue;

                    double env = decay > 0 ? Math.Exp(-(i / (double)Rate) * decay) : 1.0;
                    if (i < attack) env *= i / (double)attack;
                    int fromEnd = len - i;
                    if (fromEnd < attack) env *= fromEnd / (double)attack;

                    _data[at] += Math.Sin(phase) * amp * env;
                }
            }

            /// <summary>
            /// Filtered noise. The one-pole low pass turns white noise into
            /// something closer to breath or air, which is what the whooshes
            /// and puffs need.
            /// </summary>
            public void Noise(int startMs, int lenMs, double amp, double decay, double smoothing)
            {
                int start = Ms(startMs), len = Ms(lenMs);
                int attack = Math.Min(Ms(5), Math.Max(1, len / 6));
                double filtered = 0;

                for (int i = 0; i < len; i++)
                {
                    // Deterministic pseudo-random, so a cue sounds the same
                    // every time it is played and every time it is tested.
                    _noiseState = _noiseState * 1664525u + 1013904223u;
                    double white = ((_noiseState >> 8) & 0xFFFF) / 32768.0 - 1.0;
                    filtered += (white - filtered) * smoothing;

                    int at = start + i;
                    if (at < 0 || at >= _data.Length) continue;

                    double env = Math.Exp(-(i / (double)Rate) * decay);
                    if (i < attack) env *= i / (double)attack;
                    int fromEnd = len - i;
                    if (fromEnd < attack) env *= fromEnd / (double)attack;

                    _data[at] += filtered * amp * env;
                }
            }

            /// <summary>
            /// Normalises to a fixed peak and fades both ends, so every cue in
            /// the library is a comparable loudness, never clips, and always
            /// starts and ends at silence.
            /// </summary>
            public short[] Build(double peak = 0.82, int fadeMs = 4)
            {
                double max = 0;
                foreach (double v in _data) { double a = Math.Abs(v); if (a > max) max = a; }
                if (max <= 0) return Array.Empty<short>();

                double gain = peak / max;
                int fade = Math.Max(1, Ms(fadeMs));
                var outp = new short[_data.Length];

                for (int i = 0; i < _data.Length; i++)
                {
                    double v = _data[i] * gain * Fade(i, _data.Length, fade);
                    outp[i] = (short)(int)(Math.Clamp(v, -1.0, 1.0) * 32767);
                }
                return outp;
            }
        }

        /// <summary>Partial ratios and decay rate that give each voice its character.</summary>
        private static (double[] Ratios, double[] Amps, double Decay, int TailMs) VoiceSpec(Voice v) => v switch
        {
            Voice.Soft => (new[] { 1.0, 2.0 }, new[] { 1.0, 0.08 }, 4.5, 90),
            Voice.Bright => (new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 0.35, 0.12 }, 5.0, 90),
            Voice.Pluck => (new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 0.25, 0.08 }, 12.0, 140),
            // 2.76 and 5.4 are the classic inharmonic bell partials.
            Voice.Bell => (new[] { 1.0, 2.76, 5.4 }, new[] { 1.0, 0.30, 0.12 }, 6.0, 260),
            _ => (new[] { 0.5, 1.0 }, new[] { 0.5, 1.0 }, 9.0, 90),
        };

        /// <summary>A sequence of notes, each ringing on past the next.</summary>
        private static short[] Melody(double[] notes, int noteMs, int gapMs, Voice voice)
        {
            var (ratios, amps, decay, tail) = VoiceSpec(voice);
            int step = noteMs + gapMs;
            int total = Math.Min(1000, notes.Length * step + tail);

            var c = new Canvas(total);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = n * step;
                int len = Math.Min(noteMs + tail, total - start);
                if (len <= 0) break;
                for (int p = 0; p < ratios.Length; p++)
                    c.Partial(start, len, notes[n] * ratios[p], amps[p], decay);
            }
            return c.Build();
        }

        /// <summary>A single struck note with an optional inharmonic partial.</summary>
        private static short[] Struck(double freq, int durationMs, double decay, double amp, double partialRatio = 0)
        {
            var c = new Canvas(durationMs);
            c.Partial(0, durationMs, freq, amp, decay);
            if (partialRatio > 0) c.Partial(0, durationMs, freq * partialRatio, amp * 0.3, decay * 1.6);
            return c.Build();
        }

        /// <summary>Several notes struck together, which reads as a chime.</summary>
        private static short[] Bells(double[] freqs, int durationMs, double decay)
        {
            var c = new Canvas(durationMs);
            for (int i = 0; i < freqs.Length; i++)
            {
                // A few milliseconds of stagger stops it sounding synthetic.
                c.Partial(i * 12, durationMs - i * 12, freqs[i], 1.0 - i * 0.2, decay);
                c.Partial(i * 12, durationMs - i * 12, freqs[i] * 2.76, 0.18, decay * 1.5);
            }
            return c.Build();
        }

        /// <summary>A very short blip with a fast decay: clicks, ticks, pops.</summary>
        private static short[] Pop(double freq, int durationMs)
        {
            var c = new Canvas(durationMs);
            c.Partial(0, durationMs, freq, 1.0, 40);
            c.Partial(0, durationMs, freq * 2, 0.3, 60);
            return c.Build();
        }

        private static short[] Bubble()
        {
            // Rising pitch under a quick decay is what makes it read as a bubble.
            var c = new Canvas(150);
            c.Sweep(0, 140, 350, 900, 1.0, 14);
            return c.Build();
        }

        private static short[] WoodBlock()
        {
            var c = new Canvas(120);
            c.Partial(0, 110, 1200, 1.0, 45);
            c.Partial(0, 60, 2400, 0.35, 70);
            c.Noise(0, 25, 0.5, 90, 0.55);
            return c.Build();
        }

        private static short[] Siren(double low, double high, int cycles, int stepMs)
        {
            int total = Math.Min(1000, cycles * stepMs * 2);
            var c = new Canvas(total);
            for (int i = 0; i < cycles; i++)
            {
                c.Sweep(i * stepMs * 2, stepMs, low, high, 0.9);
                c.Sweep(i * stepMs * 2 + stepMs, stepMs, high, low, 0.9);
            }
            return c.Build();
        }

        private static short[] Buzz(double freq, int durationMs)
        {
            // Stacked harmonics at equal weight give the harsh edge a buzz needs.
            var c = new Canvas(durationMs);
            for (int h = 1; h <= 6; h++)
                c.Partial(0, durationMs, freq * h, 1.0 / h, 2.0);
            return c.Build();
        }

        private static short[] Whoosh(bool up)
        {
            var c = new Canvas(320);
            c.Noise(0, 300, 1.0, up ? 1.5 : 4.5, up ? 0.10 : 0.35);
            c.Sweep(0, 300, up ? 220 : 900, up ? 900 : 220, 0.25, 2.5);
            return c.Build();
        }

        private static short[] AirPuff()
        {
            var c = new Canvas(140);
            c.Noise(0, 130, 1.0, 22, 0.25);
            return c.Build();
        }

        private static short[] Heartbeat()
        {
            var c = new Canvas(560);
            c.Partial(0, 170, 62, 1.0, 13);
            c.Partial(0, 90, 124, 0.25, 20);
            c.Partial(230, 200, 55, 0.8, 12);
            c.Partial(230, 90, 110, 0.2, 20);
            return c.Build();
        }

        private static short[] Drip()
        {
            // Pitch falling fast under a quick decay is the classic water drop.
            var c = new Canvas(200);
            c.Sweep(0, 90, 1500, 700, 1.0, 22);
            c.Partial(70, 120, 700, 0.5, 16);
            return c.Build();
        }
    }
}
