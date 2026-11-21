"""D-02 research: how well does a formant (LPC) vowel classifier pick Japanese vowels, and how
much does accompaniment hurt? Synthetic vowels only (no recorded vocals are in the test set).
Standard library only. Usage: python -I scripts/research/lipsync_vowel_experiment.py
"""
import cmath
import math
import random

RATE = 16000
# Approximate Japanese vowel formants (F1, F2, F3) for a female voice; male voices x 0.85.
VOWELS = {'a': (850, 1250, 2800), 'i': (330, 2700, 3300), 'u': (370, 1500, 2600), 'e': (520, 2200, 2900), 'o': (500, 900, 2700)}


def resonator(signal, freq, bandwidth):
    r = math.exp(-math.pi * bandwidth / RATE)
    a1, a2 = 2 * r * math.cos(2 * math.pi * freq / RATE), -r * r
    gain = 1 - r
    out, y1, y2 = [], 0.0, 0.0
    for x in signal:
        y = gain * x + a1 * y1 + a2 * y2
        out.append(y)
        y2, y1 = y1, y
    return out


def vowel(name, f0, seconds, scale, rng):
    n = int(RATE * seconds)
    period = RATE / f0
    source = []
    phase = 0.0
    for i in range(n):
        jitter = 1 + 0.01 * (rng.random() - 0.5)
        phase += jitter / period
        source.append(1.0 if phase >= 1 else 0.0)
        if phase >= 1:
            phase -= 1
    signal = source
    for f in VOWELS[name]:
        signal = resonator(signal, f * scale, 60 + f * 0.05)
    peak = max(abs(x) for x in signal) or 1
    return [x / peak for x in signal]


def accompaniment(n, rng):
    # Chord tones (A minor), a soft pad and drum-like noise bursts every 0.5 s.
    out = []
    for i in range(n):
        t = i / RATE
        tone = sum(math.sin(2 * math.pi * f * t) for f in (220.0, 261.63, 329.63, 440.0)) / 4
        burst = math.exp(-((t % 0.5) / 0.05)) * (rng.random() * 2 - 1)
        out.append(0.6 * tone + 0.6 * burst + 0.1 * (rng.random() * 2 - 1))
    return out


def mix(voice, music, snr_db):
    if snr_db is None:
        return voice
    pv = sum(x * x for x in voice) / len(voice)
    pm = sum(x * x for x in music) / len(music)
    k = math.sqrt(pv / (pm * 10 ** (snr_db / 10)))
    return [v + k * m for v, m in zip(voice, music)]


def lpc(frame, order=12):
    n = len(frame)
    w = [frame[i] * (0.54 - 0.46 * math.cos(2 * math.pi * i / (n - 1))) for i in range(n)]
    w = [w[0]] + [w[i] - 0.95 * w[i - 1] for i in range(1, n)]  # pre-emphasis
    r = [sum(w[i] * w[i + k] for i in range(n - k)) for k in range(order + 1)]
    if r[0] <= 0:
        return None
    a, e = [1.0] + [0.0] * order, r[0]
    for i in range(1, order + 1):
        acc = r[i] + sum(a[j] * r[i - j] for j in range(1, i))
        k = -acc / e
        a = [a[j] + k * a[i - j] if 0 < j < i else a[j] for j in range(order + 1)]
        a[i] = k
        e *= 1 - k * k
        if e <= 0:
            return None
    return a


def formants(a):
    # Peaks of the LPC envelope 1/|A(e^jw)| between 200 and 3500 Hz.
    freqs = list(range(200, 3500, 10))
    env = []
    for f in freqs:
        z = cmath.exp(-2j * math.pi * f / RATE)
        env.append(-math.log(abs(sum(c * z ** k for k, c in enumerate(a))) + 1e-12))
    peaks = [freqs[i] for i in range(1, len(env) - 1) if env[i] > env[i - 1] and env[i] >= env[i + 1]]
    return peaks


def classify(peaks, scale):
    if len(peaks) < 2:
        return None
    f1, f2 = peaks[0], peaks[1]
    best, best_d = None, 1e9
    for name, (t1, t2, _) in VOWELS.items():
        d = math.log(f1 / (t1 * scale)) ** 2 + math.log(f2 / (t2 * scale)) ** 2
        if d < best_d:
            best, best_d = name, d
    return best


def main():
    rng = random.Random(7)
    frame = int(0.03 * RATE)
    print('voice  SNR     a     i     u     e     o   mean')
    for voice, f0, scale in (('female', 220, 1.0), ('male', 130, 0.85)):
        for snr in (None, 20, 10, 5, 0):
            scores = []
            for name in VOWELS:
                v = vowel(name, f0, 0.6, scale, rng)
                m = accompaniment(len(v), rng)
                x = mix(v, m, snr)
                correct = total = 0
                for start in range(frame, len(x) - frame, frame):
                    a = lpc(x[start:start + frame])
                    if a is None:
                        continue
                    total += 1
                    correct += classify(formants(a), scale) == name
                scores.append(correct / max(1, total))
            label = 'clean' if snr is None else '%d dB' % snr
            print('%-6s %-6s ' % (voice, label) + ' '.join('%5.0f' % (s * 100) for s in scores) + '  %5.0f' % (sum(scores) / len(scores) * 100))


if __name__ == '__main__':
    main()
