#!/usr/bin/env python3
"""
Composes Happy World Online's original background music and synthesized sound effects.

Everything here is generated from code (no samples), so the music belongs to the project outright.
Re-run to regenerate: python3 apps/game-client/Tools/audio/compose.py   (needs numpy + ffmpeg)
Output: apps/game-client/Assets/_Project/Resources/Art/Audio/*.ogg

Tracks loop seamlessly: each is an exact number of bars, and the reverb/decay tail that rings past
the end is folded back onto the start.
"""
import os, subprocess, sys, tempfile
import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', '_Project', 'Resources', 'Art', 'Audio')
RNG = np.random.default_rng(7)

def midi(n): return 440.0 * 2 ** ((n - 69) / 12)

NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
def n(s):
    """'C5', 'F#4', 'Bb3' -> midi number"""
    name, acc, octv = s[0], s[1:-1], int(s[-1])
    v = NOTE[name] + (1 if acc == '#' else -1 if acc == 'b' else 0)
    return 12 * (octv + 1) + v

# ---------------- instruments (mono float arrays) ----------------

def env(length, a, d, s, r, sustain_len):
    t = np.arange(length) / SR
    e = np.zeros(length)
    a_n, d_n = max(1, int(a * SR)), max(1, int(d * SR))
    s_end = min(length, int(sustain_len * SR))
    e[:a_n] = np.linspace(0, 1, a_n)[: length]
    if a_n < length:
        dd = np.arange(min(d_n, length - a_n))
        e[a_n:a_n + len(dd)] = 1 - (1 - s) * dd / d_n
    if a_n + d_n < s_end: e[a_n + d_n:s_end] = s
    if s_end < length:
        rr = np.arange(length - s_end) / SR
        start = e[s_end - 1] if s_end > 0 else s
        e[s_end:] = start * np.exp(-rr / max(r, 1e-3) * 5)
    return e

def marimba(freq, dur):
    L = int((dur + 0.6) * SR); t = np.arange(L) / SR
    x = (np.sin(2 * np.pi * freq * t) * np.exp(-t * 6)
         + 0.35 * np.sin(2 * np.pi * freq * 4 * t) * np.exp(-t * 22)
         + 0.12 * np.sin(2 * np.pi * freq * 10 * t) * np.exp(-t * 45))
    return x * np.minimum(1, t * 400)

def pluck(freq, dur, bright=0.5):
    """Karplus-Strong string (ukulele/guitar-ish)."""
    L = int((dur + 0.8) * SR)
    period = max(2, int(SR / freq))
    buf = RNG.uniform(-1, 1, period)
    # soften the excitation for a warmer pluck
    for _ in range(2): buf = 0.5 * (buf + np.roll(buf, 1))
    out = np.zeros(L)
    decay = 0.996 - (1 - bright) * 0.004
    i = 0
    while i < L:
        k = min(period, L - i)
        out[i:i + k] = buf[:k]
        buf = decay * 0.5 * (buf + np.roll(buf, -1))
        i += period
    return out * 0.8 * np.minimum(1, np.arange(L) / (0.002 * SR))  # 2 ms attack: no clicks

def bell(freq, dur):
    L = int((dur + 1.5) * SR); t = np.arange(L) / SR
    x = (np.sin(2 * np.pi * freq * t) * np.exp(-t * 2.2)
         + 0.5 * np.sin(2 * np.pi * freq * 2.76 * t) * np.exp(-t * 4)
         + 0.25 * np.sin(2 * np.pi * freq * 5.4 * t) * np.exp(-t * 7))
    return x * np.minimum(1, t * 600) * 0.6

def bass(freq, dur):
    L = int((dur + 0.3) * SR); t = np.arange(L) / SR
    x = np.sin(2 * np.pi * freq * t) + 0.3 * np.sin(2 * np.pi * freq * 2 * t)
    return x * env(L, 0.005, 0.25, 0.6, 0.15, dur) * 0.9

def pad(freqs, dur):
    L = int((dur + 0.8) * SR); t = np.arange(L) / SR
    x = np.zeros(L)
    for f in freqs:
        for det in (-0.12, 0.0, 0.12):
            ff = f * 2 ** (det / 12)
            x += np.sin(2 * np.pi * ff * t + RNG.uniform(0, 6)) + 0.2 * np.sin(4 * np.pi * ff * t)
    x /= (3 * len(freqs))
    return x * env(L, 0.35, 0.5, 0.8, 0.6, dur)

def square_lead(freq, dur):
    L = int((dur + 0.15) * SR); t = np.arange(L) / SR
    x = np.zeros(L)
    for k in (1, 3, 5, 7, 9):  # band-limited square
        x += np.sin(2 * np.pi * freq * k * t) / k
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(1, t * 3)
    x = np.interp(np.cumsum(vib) - 1, np.arange(L), x)
    return x * env(L, 0.01, 0.08, 0.6, 0.08, dur) * 0.45

def kick():
    L = int(0.35 * SR); t = np.arange(L) / SR
    f = 50 + 90 * np.exp(-t * 30)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 9)

def snare():
    L = int(0.25 * SR); t = np.arange(L) / SR
    noise = RNG.uniform(-1, 1, L)
    noise = noise - np.concatenate([[0], noise[:-1]]) * 0.6
    return (noise * np.exp(-t * 18) * 0.6 + np.sin(2 * np.pi * 190 * t) * np.exp(-t * 25) * 0.4)

def shaker():
    L = int(0.09 * SR); t = np.arange(L) / SR
    noise = RNG.uniform(-1, 1, L)
    noise = noise - np.concatenate([[0], noise[:-1]])
    return noise * np.exp(-t * 60) * np.minimum(1, t * 300) * 0.25

# ---------------- mixing helpers ----------------

class Track:
    def __init__(self, bars, bpm, beats_per_bar=4):
        self.bpm = bpm
        self.beat = 60 / bpm
        self.length = int(round(bars * beats_per_bar * self.beat * SR))
        self.L = np.zeros(self.length + SR * 3)
        self.R = np.zeros(self.length + SR * 3)

    def add(self, sig, beat, gain=1.0, pan=0.0, swing=0.0):
        start = beat * self.beat
        if swing and (beat * 2) % 2 == 1: start += swing * self.beat * 0.5
        i = int(start * SR)
        j = min(len(self.L), i + len(sig))
        lg, rg = gain * np.sqrt((1 - pan) / 2) * 1.414, gain * np.sqrt((1 + pan) / 2) * 1.414
        self.L[i:j] += sig[: j - i] * lg
        self.R[i:j] += sig[: j - i] * rg

    def render(self, reverb=0.25):
        L, R = self.L.copy(), self.R.copy()
        # tiny stereo reverb: a few feedback delays
        for buf, delays in ((L, (1557, 1617, 1491, 1422)), (R, (1277, 1356, 1188, 1116))):
            wet = np.zeros_like(buf)
            for d in delays:
                d *= 2
                y = buf.copy()
                for k in range(1, 6):
                    sh = d * k
                    if sh >= len(buf): break
                    y[sh:] += buf[:-sh] * (0.55 ** k)
                wet += y - buf
            buf += wet * reverb / len(delays)
        # fold everything past the loop end back to the start, so the loop is seamless
        out = np.stack([L, R], axis=1)
        loop = out[: self.length].copy()
        tail = out[self.length:]
        loop[: len(tail)] += tail[: self.length]
        # gentle limiter
        peak = np.max(np.abs(loop))
        loop = np.tanh(loop / peak * 1.3) * 0.85
        return loop

def write_ogg(name, stereo, quality=4):
    os.makedirs(OUT, exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as f:
        tmp = f.name
    import wave
    pcm = (np.clip(stereo, -1, 1) * 32767).astype('<i2')
    ch = 1 if pcm.ndim == 1 else pcm.shape[1]
    with wave.open(tmp, 'wb') as w:
        w.setnchannels(ch); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    path = os.path.join(OUT, name + '.ogg')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', tmp, '-c:a', 'libvorbis', '-q:a', str(quality), path], check=True)
    os.unlink(tmp)
    print('wrote', os.path.relpath(path), f'{len(pcm)/SR:.1f}s', os.path.getsize(path) // 1024, 'KB')

# ---------------- melody writing ----------------

SCALE_C = [0, 2, 4, 5, 7, 9, 11]

def chord_tones(root_midi, quality):
    third = 3 if quality == 'm' else 4
    return [root_midi, root_midi + third, root_midi + 7]

def nearest(target, allowed):
    return min(allowed, key=lambda a: (abs(a - target), a))

def melody_bar(chord, prev, rhythm, rng, lo, hi, key_pcs):
    """One bar: chord tones on strong beats, scale steps between; returns [(beat, midi, dur)]."""
    tones = [m for m in range(lo, hi + 1) if m % 12 in [c % 12 for c in chord]]
    scale = [m for m in range(lo, hi + 1) if m % 12 in key_pcs]
    out, cur = [], prev
    for k, (b, d) in enumerate(rhythm):
        strong = b == int(b) and int(b) % 2 == 0
        if strong:
            cur = nearest(cur + rng.choice([-3, -1, 0, 2, 4]), tones)
        else:
            idx = scale.index(nearest(cur, scale))
            step = rng.choice([-1, 1, 1, 2, -2])
            cur = scale[int(np.clip(idx + step, 0, len(scale) - 1))]
        out.append((b, cur, d))
    return out, cur

def write_melody(progression, rhythms, seed, lo, hi, key_pcs, form='AABA'):
    """Writes phrase A and B once and repeats them, so the tune is hummable rather than random."""
    rng = np.random.default_rng(seed)
    bars_per_phrase = len(progression) // len(form)
    phrases, notes = {}, []
    prev = (lo + hi) // 2
    for p_i, label in enumerate(form):
        chords = progression[p_i * bars_per_phrase:(p_i + 1) * bars_per_phrase]
        if label in phrases and label != 'B':
            src = phrases[label]
            for bar_i, bar in enumerate(src):
                last = bar_i == len(src) - 1 and p_i == len(form) - 1
                for (b, m, d) in bar:
                    if last and b >= 2:  # final cadence lands on the tonic
                        m = nearest(m, [x for x in range(lo, hi + 1) if x % 12 == key_pcs[0]]); d = max(d, 4 - b)
                        notes.append(((p_i * bars_per_phrase + bar_i) * 4 + b, m, d)); break
                    notes.append(((p_i * bars_per_phrase + bar_i) * 4 + b, m, d))
            continue
        bars = []
        for bar_i, ch in enumerate(chords):
            r = rhythms[(bar_i + (2 if label == 'B' else 0)) % len(rhythms)]
            bar, prev = melody_bar(ch, prev, r, rng, lo, hi, key_pcs)
            bars.append(bar)
            for (b, m, d) in bar: notes.append(((p_i * bars_per_phrase + bar_i) * 4 + b, m, d))
        phrases[label] = bars
    return notes

# ---------------- the tracks ----------------

def town_day():
    """เมืองสดใส: ukulele + marimba, 108 bpm, 16 bars."""
    bpm, bars = 108, 16
    T = Track(bars, bpm)
    I, vi, IV, V, ii, iii = (n('C3'), ''), (n('A2'), 'm'), (n('F2'), ''), (n('G2'), ''), (n('D3'), 'm'), (n('E3'), 'm')
    prog = [I, vi, IV, V, I, vi, ii, V, IV, V, iii, vi, IV, V, I, I]
    chords = [chord_tones(r, q) for r, q in prog]
    rhythms = [
        [(0, 1), (1, 0.5), (1.5, 0.5), (2, 1.5), (3.5, 0.5)],
        [(0, 0.5), (0.5, 0.5), (1, 1), (2, 0.5), (2.5, 0.5), (3, 1)],
        [(0, 1.5), (1.5, 0.5), (2, 1), (3, 1)],
        [(0, 0.5), (0.5, 1), (1.5, 0.5), (2, 2)],
    ]
    mel = write_melody(chords, rhythms, seed=11, lo=n('E5'), hi=n('E6'), key_pcs=SCALE_C)
    for b, m, d in mel: T.add(marimba(midi(m), d * T.beat), b, 0.5, pan=0.15)
    for bar, ch in enumerate(chords):
        base = bar * 4
        T.add(bass(midi(ch[0]), T.beat * 0.9), base, 0.55)
        T.add(bass(midi(ch[0] + 7), T.beat * 0.9), base + 2, 0.45)
        T.add(bass(midi(ch[0] + 12 if bar % 2 else ch[0]), T.beat * 0.4), base + 3.5, 0.3)
        # ukulele strum: chord in a higher voicing on the "island strum" pattern
        voicing = [nearest(x + 24, range(n('G3'), n('A4') + 1)) for x in ch] + [ch[0] + 24]
        for b in (0, 1, 1.5, 2.5, 3, 3.5):
            for k, v in enumerate(sorted(set(voicing))):
                T.add(pluck(midi(v), 0.4, 0.7), base + b + k * 0.012, 0.16 if b in (0, 2.5) else 0.11, pan=-0.3)
        for b in np.arange(0, 4, 0.5): T.add(shaker(), base + b, 0.5 if b % 1 else 0.3, pan=0.4)
    return T.render(0.22)

def town_night():
    """ราตรีสงบ: music box over a soft pad, 76 bpm, 8 bars."""
    bpm, bars = 76, 8
    T = Track(bars, bpm)
    prog = [(n('A2'), 'm'), (n('F2'), ''), (n('C3'), ''), (n('G2'), ''), (n('A2'), 'm'), (n('F2'), ''), (n('D3'), 'm'), (n('E3'), '')]
    chords = [chord_tones(r, q) for r, q in prog]
    rhythms = [[(0, 1), (1, 1), (2, 2)], [(0, 1.5), (1.5, 0.5), (2, 1), (3, 1)], [(0, 2), (2, 1), (3, 1)]]
    mel = write_melody(chords, rhythms, seed=5, lo=n('A5'), hi=n('A6'), key_pcs=[9, 11, 0, 2, 4, 5, 7], form='AB')
    for b, m, d in mel: T.add(bell(midi(m), d * T.beat), b, 0.32, pan=0.2)
    for bar, ch in enumerate(chords):
        T.add(pad([midi(x + 12) for x in ch], 4 * T.beat), bar * 4, 0.35)
        for k, b in enumerate((0, 1, 2, 3)):  # slow broken chord
            T.add(pluck(midi(ch[k % 3] + 12), 0.8, 0.3), bar * 4 + b, 0.18, pan=-0.35)
        T.add(bass(midi(ch[0]), 3.5 * T.beat), bar * 4, 0.35)
    return T.render(0.4)

def minigame():
    """มินิเกม: bouncy chiptune-ish lead, 140 bpm, 8 bars."""
    bpm, bars = 140, 8
    T = Track(bars, bpm)
    prog = [(n('F2'), ''), (n('G2'), ''), (n('E2'), 'm'), (n('A2'), 'm'), (n('F2'), ''), (n('G2'), ''), (n('C3'), ''), (n('C3'), '')]
    chords = [chord_tones(r, q) for r, q in prog]
    rhythms = [[(0, 0.5), (0.5, 0.5), (1, 0.5), (1.5, 0.5), (2, 1), (3, 0.5), (3.5, 0.5)],
               [(0, 1), (1, 0.5), (1.5, 0.5), (2, 0.5), (2.5, 0.5), (3, 1)]]
    mel = write_melody(chords, rhythms, seed=23, lo=n('G4'), hi=n('G5'), key_pcs=SCALE_C, form='AA')
    for b, m, d in mel: T.add(square_lead(midi(m), d * T.beat * 0.9), b, 0.42)
    for bar, ch in enumerate(chords):
        base = bar * 4
        for b in np.arange(0, 4, 0.5):
            T.add(bass(midi(ch[0] + (12 if b % 1 else 0)), T.beat * 0.4), base + b, 0.42)
        for b in (0, 1, 2, 3): T.add(kick(), base + b, 0.55)
        for b in (1, 3): T.add(snare(), base + b, 0.35, pan=0.1)
        for b in np.arange(0, 4, 0.5): T.add(shaker(), base + b + 0.25, 0.45, pan=-0.3)
        for k, x in enumerate(ch):  # arpeggio sparkle
            T.add(marimba(midi(x + 36), 0.2), base + 0.5 + k * 1.0, 0.13, pan=0.4)
    return T.render(0.15)

# ---------------- ambience & sound effects ----------------

def rain(seconds=8.0):
    L = int(seconds * SR)
    white = RNG.uniform(-1, 1, (L, 2))
    # pink-ish rumble: cumulative smoothing
    out = np.zeros_like(white)
    a = 0.0
    for c in range(2):
        y = np.zeros(L); s = 0.0
        x = white[:, c]
        alpha = 0.06
        y = np.convolve(x, np.ones(8) / 8, mode='same')
        out[:, c] = y * 0.5 + x * 0.06
    # droplets
    for _ in range(int(seconds * 25)):
        i = RNG.integers(0, L - 2000); f = RNG.uniform(2500, 5000)
        t = np.arange(1500) / SR
        d = np.sin(2 * np.pi * f * t * (1 - t * 20)) * np.exp(-t * 120) * RNG.uniform(0.05, 0.15)
        out[i:i + 1500, RNG.integers(0, 2)] += d
    # seamless: crossfade the end into the start
    fade = int(0.5 * SR)
    w = np.linspace(0, 1, fade)[:, None]
    out[:fade] = out[:fade] * w + out[-fade:] * (1 - w)
    out = out[:-fade]
    return out / np.max(np.abs(out)) * 0.6

def jingle(notes, step, inst=marimba, tail=0.8):
    L = int((len(notes) * step + tail) * SR)
    x = np.zeros(L)
    for k, m in enumerate(notes):
        s = inst(midi(m), step * 1.5)
        i = int(k * step * SR); j = min(L, i + len(s))
        x[i:j] += s[: j - i]
    return x / np.max(np.abs(x)) * 0.8

def sizzle(seconds=3.0):
    L = int(seconds * SR)
    x = RNG.uniform(-1, 1, L)
    x = x - np.concatenate([[0], x[:-1]])  # high-pass: crackly
    crackle = (RNG.uniform(0, 1, L) > 0.9993) * RNG.uniform(0.5, 1.2, L)
    crackle = np.convolve(crackle, np.exp(-np.arange(200) / 30), mode='same')
    y = x * 0.12 * (0.8 + 0.2 * np.sin(np.arange(L) / SR * 2 * np.pi * 0.7)) + crackle * RNG.uniform(-1, 1, L) * 0.5
    fade = int(0.3 * SR); w = np.linspace(0, 1, fade)
    y[:fade] = y[:fade] * w + y[-fade:] * (1 - w)
    return y[:-fade] / np.max(np.abs(y)) * 0.5

def pour(seconds=2.0):
    L = int(seconds * SR); t = np.arange(L) / SR
    x = RNG.uniform(-1, 1, L)
    y = np.convolve(x, np.ones(30) / 30, mode='same')
    bubbles = np.zeros(L)
    for _ in range(int(seconds * 18)):
        i = RNG.integers(0, L - 3000); f = RNG.uniform(500, 1100)
        tt = np.arange(2500) / SR
        bubbles[i:i + 2500] += np.sin(2 * np.pi * f * tt * (1 + tt * 30)) * np.exp(-tt * 60) * 0.3
    y = y * 3 + bubbles
    fade = int(0.2 * SR); w = np.linspace(0, 1, fade)
    y[:fade] = y[:fade] * w + y[-fade:] * (1 - w)
    return y[:-fade] / np.max(np.abs(y)) * 0.5

def main():
    write_ogg('music_town_day', town_day(), 4)
    write_ogg('music_town_night', town_night(), 4)
    write_ogg('music_minigame', minigame(), 4)
    write_ogg('amb_rain', rain(), 3)
    write_ogg('sfx_fanfare', jingle([n('C5'), n('E5'), n('G5'), n('C6'), n('E6'), n('G6')], 0.09, marimba, 1.2), 5)
    write_ogg('sfx_star', jingle([n('E6'), n('B6')], 0.07, bell, 0.6), 5)
    write_ogg('sfx_lose', jingle([n('G4'), n('E4'), n('C4')], 0.14, marimba, 0.6), 5)
    write_ogg('sfx_countdown', jingle([n('A5')], 0.1, bell, 0.3), 5)
    write_ogg('sfx_go', jingle([n('A6')], 0.1, bell, 0.5), 5)
    write_ogg('loop_sizzle', sizzle(), 3)
    write_ogg('loop_pour', pour(), 3)

if __name__ == '__main__':
    main()
