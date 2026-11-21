using System;
using System.Collections.Generic;
using System.Linq;

namespace MmdStudio.Poc
{
    // D-01: tempo, beats, bars and section energy from the scene audio by local signal processing
    // (decision 16: no external services or ML models). Assumes a steady tempo (dance tracks).
    //  1. Onset envelope at 100 Hz: spectral flux of log magnitudes (Hann window ~46 ms) averaged
    //     per log-spaced band so a kick counts as much as a broadband hi-hat; local mean removed.
    //  2. Tempo: comb score over 60-200 BPM in 0.05 BPM steps (envelope products at 1 and 2
    //     periods, linear interpolation), weighted towards 120 BPM; outside 90-180 BPM the double /
    //     half tempo wins when its own comb score is close (dance tracks sit in 90-180).
    //  3. Phase: the beat grid offset with the most onset energy; between it and the half-beat
    //     offset, the one with more kick-band energy; then each predicted beat is moved to its
    //     local onset peak and a least-squares line through them refines period and phase.
    //  4. Bars: 4/4, downbeat = the beat position (of 4) with the most low-frequency onset energy;
    //     section energy = RMS per bar scaled to 0..1 by the 10th/90th percentiles.
    public sealed class MmdBeatInfo
    {
        public double Bpm, PeriodSeconds, FirstBeatSeconds, DurationSeconds;
        public double[] Beats = new double[0];     // seconds
        public double[] Bars = new double[0];      // bar starts (seconds)
        public float[] BarEnergy = new float[0];   // 0..1 per bar
        public float Confidence;                   // best comb score relative to the mean
    }

    public static class MmdBeatAnalyzer
    {
        public const int EnvelopeRate = 100;

        // Mono mix of an AudioClip's samples (interleaved) at `rate`.
        public static MmdBeatInfo Analyze(float[] interleaved, int channels, int rate)
        {
            if (rate < 8000 || channels < 1) throw new ArgumentOutOfRangeException(nameof(rate));
            var frames = interleaved.Length / channels;
            if (frames < rate * 4) throw new ArgumentException("박자를 찾으려면 4초 이상의 오디오가 필요합니다.");
            var mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                float sum = 0; for (var c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            var (envelope, low, latency) = Envelope(mono, rate);
            var (bpm, confidence) = Tempo(envelope);
            var period = 60.0 * EnvelopeRate / bpm;
            var phase = Phase(envelope, period);
            // Kicks land on the beat: between the grid and the half-beat grid, keep the one with more kick energy.
            double KickScore(double offset) { double score = 0; for (var t = offset; t < low.Length - 1; t += period) score += Sample(low, t); return score; }
            if (KickScore(phase + period / 2) > KickScore(phase) * 1.1) phase = (phase + period / 2) % period;
            (period, phase) = Refine(envelope, period, phase);
            var duration = frames / (double)rate;
            var beats = new List<double>();
            // Envelope frames start at the analysis window; the onset sits about half a window later.
            for (var t = phase; t / EnvelopeRate + latency < duration; t += period) if (t >= 0) beats.Add(t / EnvelopeRate + latency);
            // Downbeat: the position (of 4) with the most low-band onset energy.
            var best = 0; double bestScore = double.MinValue;
            for (var offset = 0; offset < 4; offset++)
            {
                double score = 0;
                for (var k = offset; k < beats.Count; k += 4) score += Sample(low, (beats[k] - latency) * EnvelopeRate);
                if (score > bestScore) { bestScore = score; best = offset; }
            }
            var bars = new List<double>();
            for (var k = best; k < beats.Count; k += 4) bars.Add(beats[k]);
            // The (partial) bar before the first full one starts on a beat only if that beat exists.
            if (best > 0 && beats[best] - 4 * period / EnvelopeRate >= 0) bars.Insert(0, beats[best] - 4 * period / EnvelopeRate);
            var energy = BarEnergy(mono, rate, bars, duration);
            return new MmdBeatInfo
            {
                Bpm = 60.0 * EnvelopeRate / period, PeriodSeconds = period / EnvelopeRate, FirstBeatSeconds = beats.Count > 0 ? beats[0] : 0,
                DurationSeconds = duration, Beats = beats.ToArray(), Bars = bars.ToArray(), BarEnergy = energy, Confidence = confidence,
            };
        }

        private static (float[] envelope, float[] low, double latency) Envelope(float[] mono, int rate)
        {
            var hop = rate / EnvelopeRate;
            var size = 1; while (size < rate * .046) size <<= 1;
            var count = Math.Max(1, (mono.Length - size) / hop + 1);
            var window = new float[size];
            for (var i = 0; i < size; i++) window[i] = .5f - .5f * (float)Math.Cos(2 * Math.PI * i / (size - 1));
            var re = new float[size]; var im = new float[size];
            var previous = new float[size / 2]; var current = new float[size / 2];
            var flux = new float[count]; var lowFlux = new float[count];
            // Log-spaced bands from 30 Hz up; band 0..1 (below ~150 Hz) is the kick band.
            var edges = new List<int> { Math.Max(1, (int)(30.0 * size / rate)) };
            while (edges[edges.Count - 1] < size / 2 - 1) edges.Add(Math.Min(size / 2 - 1, Math.Max(edges[edges.Count - 1] + 1, (int)(edges[edges.Count - 1] * 2.2))));
            var lowEdge = Math.Max(2, (int)(150.0 * size / rate));
            var band = new float[edges.Count];
            for (var f = 0; f < count; f++)
            {
                var start = f * hop;
                for (var i = 0; i < size; i++) { re[i] = start + i < mono.Length ? mono[start + i] * window[i] : 0; im[i] = 0; }
                Fft(re, im);
                for (var k = 1; k < size / 2; k++) current[k] = (float)Math.Log(1 + 1000 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                float sum = 0, lowSum = 0;
                if (f > 0)
                    for (var b = 0; b + 1 < edges.Count; b++)
                    {
                        float rise = 0;
                        for (var k = edges[b]; k < edges[b + 1]; k++) { var d = current[k] - previous[k]; if (d > 0) rise += d; }
                        rise /= edges[b + 1] - edges[b];
                        sum += rise;
                        if (edges[b + 1] <= lowEdge + 1) lowSum += rise;
                    }
                flux[f] = sum; lowFlux[f] = lowSum;
                var swap = previous; previous = current; current = swap;
            }
            _ = band;
            return (Detrend(flux), Detrend(lowFlux), size * .5 / rate);
        }

        // Remove the local mean (0.5 s) and keep the positive part.
        private static float[] Detrend(float[] values)
        {
            var result = new float[values.Length];
            var half = EnvelopeRate / 4;
            var prefix = new double[values.Length + 1];
            for (var i = 0; i < values.Length; i++) prefix[i + 1] = prefix[i] + values[i];
            for (var i = 0; i < values.Length; i++)
            {
                int a = Math.Max(0, i - half), b = Math.Min(values.Length, i + half + 1);
                var mean = (prefix[b] - prefix[a]) / (b - a);
                result[i] = (float)Math.Max(0, values[i] - mean);
            }
            return result;
        }

        private static double Comb(float[] envelope, double bpm)
        {
            var period = 60.0 * EnvelopeRate / bpm;
            double score = 0;
            foreach (var multiple in new[] { 1, 2 })
            {
                var lag = period * multiple; double s = 0;
                for (var t = 0; t + lag < envelope.Length - 1; t++) s += envelope[t] * Sample(envelope, t + lag);
                score += s / (envelope.Length - lag) / multiple;
            }
            return score;
        }
        private static (double bpm, float confidence) Tempo(float[] envelope)
        {
            double bestBpm = 120, bestScore = double.MinValue, total = 0; var n = 0;
            for (var bpm = 60.0; bpm <= 200.0001; bpm += .05)
            {
                // Prior: log-normal around 120 BPM (one octave ~ 1 sigma).
                var octaves = Math.Log(bpm / 120, 2);
                var score = Comb(envelope, bpm) * Math.Exp(-.5 * octaves * octaves);
                total += score; n++;
                if (score > bestScore) { bestScore = score; bestBpm = bpm; }
            }
            var raw = Comb(envelope, bestBpm);
            if (bestBpm < 90 && bestBpm * 2 <= 200 && Comb(envelope, bestBpm * 2) >= .6 * raw) bestBpm = Refine(envelope, bestBpm * 2);
            else if (bestBpm > 180 && Comb(envelope, bestBpm / 2) >= .6 * raw) bestBpm = Refine(envelope, bestBpm / 2);
            return (bestBpm, (float)(bestScore / Math.Max(1e-12, total / n)));
        }
        // Best comb score within +-1 BPM (after an octave change).
        private static double Refine(float[] envelope, double around)
        {
            double best = around, bestScore = double.MinValue;
            for (var bpm = around - 1; bpm <= around + 1; bpm += .05)
            {
                var score = Comb(envelope, bpm);
                if (score > bestScore) { bestScore = score; best = bpm; }
            }
            return best;
        }

        private static double Phase(float[] envelope, double period)
        {
            double best = 0, bestScore = double.MinValue;
            for (double phase = 0; phase < period; phase += .5)
            {
                double score = 0;
                for (var t = phase; t < envelope.Length - 1; t += period) score += Sample(envelope, t);
                if (score > bestScore) { bestScore = score; best = phase; }
            }
            return best;
        }

        // Move each predicted beat to the onset peak within +-40 ms, then fit beat = phase + k * period.
        private static (double period, double phase) Refine(float[] envelope, double period, double phase)
        {
            var ks = new List<double>(); var ts = new List<double>();
            var reach = (int)(.04 * EnvelopeRate);
            var k = 0;
            for (var t = phase; t < envelope.Length - 1; t += period, k++)
            {
                var centre = (int)Math.Round(t); var peak = -1; float peakValue = 0;
                for (var i = Math.Max(1, centre - reach); i <= Math.Min(envelope.Length - 2, centre + reach); i++)
                    if (envelope[i] > peakValue && envelope[i] >= envelope[i - 1] && envelope[i] >= envelope[i + 1]) { peakValue = envelope[i]; peak = i; }
                if (peak < 0 || peakValue <= 0) continue;
                // Parabolic interpolation of the peak position.
                double a = envelope[peak - 1], b = envelope[peak], c = envelope[peak + 1];
                var offset = (a - 2 * b + c) == 0 ? 0 : .5 * (a - c) / (a - 2 * b + c);
                ks.Add(k); ts.Add(peak + Math.Max(-.5, Math.Min(.5, offset)));
            }
            if (ks.Count < 8) return (period, phase);
            double mk = ks.Average(), mt = ts.Average(), sxx = 0, sxy = 0;
            for (var i = 0; i < ks.Count; i++) { sxx += (ks[i] - mk) * (ks[i] - mk); sxy += (ks[i] - mk) * (ts[i] - mt); }
            var refined = sxy / sxx;
            if (Math.Abs(refined - period) > period * .03) return (period, phase); // keep the comb result on a bad fit
            var start = mt - refined * mk;
            while (start - refined >= 0) start -= refined;
            while (start < 0) start += refined;
            // Hop timing: envelope frame f covers samples from f * hop; the onset sits about half a window in.
            return (refined, start);
        }

        private static float[] BarEnergy(float[] mono, int rate, List<double> bars, double duration)
        {
            var rms = new float[bars.Count];
            for (var i = 0; i < bars.Count; i++)
            {
                var a = (int)(bars[i] * rate); var b = (int)(Math.Min(duration, i + 1 < bars.Count ? bars[i + 1] : duration) * rate);
                double sum = 0; var n = 0;
                for (var s = Math.Max(0, a); s < Math.Min(mono.Length, b); s++) { sum += mono[s] * mono[s]; n++; }
                rms[i] = n == 0 ? 0 : (float)Math.Sqrt(sum / n);
            }
            if (rms.Length == 0) return rms;
            var sorted = rms.OrderBy(v => v).ToArray();
            var low = sorted[(int)(sorted.Length * .1)]; var high = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .9))];
            return rms.Select(v => high - low < 1e-6f ? .5f : Math.Max(0, Math.Min(1, (v - low) / (high - low)))).ToArray();
        }

        private static float Sample(float[] values, double position)
        {
            if (position < 0 || position >= values.Length - 1) return 0;
            var i = (int)position; var f = (float)(position - i);
            return values[i] * (1 - f) + values[i + 1] * f;
        }

        // In-place radix-2 FFT.
        private static void Fft(float[] re, float[] im)
        {
            var n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (var length = 2; length <= n; length <<= 1)
            {
                var angle = -2 * Math.PI / length;
                float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
                for (var i = 0; i < n; i += length)
                {
                    float cr = 1, ci = 0;
                    for (var j = 0; j < length / 2; j++)
                    {
                        int a = i + j, b = i + j + length / 2;
                        float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                        var next = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = next;
                    }
                }
            }
        }
    }
}
