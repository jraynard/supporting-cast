"""Synthesized sound effects for data/sounds: the clean, tunable ones (the anvil's ring, coins, the
quench's hiss, the interface, babble syllables, ambience beds) made from scratch, so they're ours
outright and can be retuned here. Recorded CC0 sounds stand in where synthesis is thin (see
data/sounds/CREDITS.md); data/sounds/sounds.json decides which file each sound id plays.

Everything is seeded, so a run writes the same files. Needs numpy and ffmpeg (with libvorbis).

    python3 tools/sfx_synth.py              # write data/sounds/{sfx,amb,voice}/*.ogg
    python3 tools/sfx_synth.py --preview    # ...and .cache/sounds/preview.ogg, every sound in a row
    python3 tools/sfx_synth.py --only anvil # just the sounds whose name starts with this
"""
import argparse
import os
import subprocess
import sys
import tempfile
import wave

import numpy as np

RATE = 44100
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "data", "sounds")
CACHE = os.path.join(ROOT, ".cache", "sounds")


# ---------------------------------------------------------------- building blocks

def t_axis(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def noise(rng, seconds):
    return rng.standard_normal(int(seconds * RATE))


def band(x, lo=None, hi=None, slope=1.0):
    """Shape a signal's spectrum: a smooth band-pass from lo to hi Hz (either may be open)."""
    n = len(x)
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / RATE)
    gain = np.ones_like(f)
    if lo:
        gain *= 1 / (1 + (lo / np.maximum(f, 1e-3)) ** (4 * slope))
    if hi:
        gain *= 1 / (1 + (f / hi) ** (4 * slope))
    return np.fft.irfft(spec * gain, n)


def tilt(x, db_per_octave):
    """Pink-ish (negative) or bright (positive) tilt, relative to 1 kHz."""
    n = len(x)
    spec = np.fft.rfft(x)
    f = np.maximum(np.fft.rfftfreq(n, 1 / RATE), 20)
    return np.fft.irfft(spec * (f / 1000) ** (db_per_octave / 6.02), n)


def sweep_band(x, centers, q=2.0):
    """A band-pass whose centre moves (centers: Hz per sample, or a curve resampled), by overlap-add."""
    n = len(x)
    hop, size = 512, 2048
    win = np.hanning(size)
    out = np.zeros(n + size)
    centers = np.interp(np.arange(n), np.linspace(0, n - 1, len(centers)), centers)
    padded = np.concatenate([x, np.zeros(size)])
    f = np.fft.rfftfreq(size, 1 / RATE)
    for start in range(0, n, hop):
        c = centers[min(start + size // 2, n - 1)]
        bw = c / q
        g = np.exp(-0.5 * ((f - c) / bw) ** 2)
        seg = np.fft.irfft(np.fft.rfft(padded[start:start + size] * win) * g, size)
        out[start:start + size] += seg * win
    return out[:n] / 1.5


def env(seconds, attack=0.005, decay=0.3, curve=1.0):
    t = t_axis(seconds)
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = np.exp(-np.maximum(t - attack, 0) / max(decay, 1e-4)) ** curve
    return a * d


def modes(rng, seconds, partials, jitter=0.0, phase=True):
    """Struck-object modal synthesis: (freq Hz, amplitude, decay s) partials, each a damped sine."""
    t = t_axis(seconds)
    out = np.zeros_like(t)
    for f, a, d in partials:
        f *= 1 + jitter * rng.uniform(-1, 1)
        ph = rng.uniform(0, 2 * np.pi) if phase else 0
        out += a * np.sin(2 * np.pi * f * t + ph) * np.exp(-t / d)
    return out


def pluck(rng, freq, seconds, damping=0.996, bright=0.5):
    """Karplus-Strong plucked string."""
    n = int(seconds * RATE)
    period = max(2, int(RATE / freq))
    buf = rng.uniform(-1, 1, period)
    buf = band(np.concatenate([buf, buf]), hi=RATE * 0.5 * (0.2 + bright * 0.8))[:period]
    out = np.zeros(n)
    for i in range(n):
        j = i % period
        out[i] = buf[j]
        buf[j] = damping * 0.5 * (buf[j] + buf[(j + 1) % period])
    return out


def at(total, *parts):
    """Mix (offset seconds, signal) parts into one of length total seconds."""
    out = np.zeros(int(total * RATE))
    for off, sig in parts:
        s = int(off * RATE)
        n = min(len(sig), len(out) - s)
        if n > 0:
            out[s:s + n] += sig[:n]
    return out


def loopable(x, fade=0.5):
    """Crossfade the tail into the head so the sound loops without a seam."""
    k = int(fade * RATE)
    head, body, tail = x[:k], x[k:-k], x[-k:]
    ramp = np.linspace(0, 1, k)
    joined = head * ramp + tail * (1 - ramp)
    return np.concatenate([body, joined])


def norm(x, peak=0.9):
    m = np.max(np.abs(x))
    return x if m == 0 else x * (peak / m)


def fade_edges(x, ms=4):
    k = int(ms / 1000 * RATE)
    if len(x) > 2 * k:
        x = x.copy()
        x[:k] *= np.linspace(0, 1, k)
        x[-k:] *= np.linspace(1, 0, k)
    return x


# ---------------------------------------------------------------- the sounds

def anvil(rng, grade):
    """Hammer on a hot bar on the anvil: a ring (bright, long for a clean blow) over a thud."""
    base = rng.uniform(0.97, 1.03)
    ring = {"good": 1.1, "fair": 0.55, "poor": 0.2, "cold": 0.05}[grade]
    level = {"good": 1.0, "fair": 0.75, "poor": 0.45, "cold": 0.25}[grade]
    partials = [(f * base, a * level, d * ring) for f, a, d in [
        (1180, 0.9, 0.9), (2870, 0.7, 0.6), (4310, 0.45, 0.45), (6090, 0.3, 0.3), (8150, 0.18, 0.2), (1745, 0.25, 0.5)]]
    s = 1.6 if grade == "good" else 1.0
    tone = modes(rng, s, partials, jitter=0.01)
    hit = band(noise(rng, 0.06), lo=300, hi=6000) * env(0.06, 0.0005, 0.012)
    thud_weight = {"good": 0.5, "fair": 0.8, "poor": 1.1, "cold": 1.4}[grade]
    thud = (modes(rng, 0.25, [(140, 1, 0.05), (310, 0.6, 0.04)]) + band(noise(rng, 0.25), hi=900) * 0.3) * env(0.25, 0.001, 0.05) * thud_weight
    if grade == "cold":
        tone = band(tone, hi=2500)
    return norm(at(s, (0, tone), (0, hit * 1.6), (0, thud)), 0.95)


def coin(rng):
    def one():
        f0 = rng.uniform(2300, 2700)
        return modes(rng, 0.35, [(f0, 1, 0.12), (f0 * 1.63, 0.7, 0.09), (f0 * 2.37, 0.5, 0.07), (f0 * 3.11, 0.3, 0.05)])
    parts = [(0, one())] + [(0.04 + rng.uniform(0, 0.12) + i * 0.05, one() * rng.uniform(0.4, 0.8)) for i in range(int(rng.integers(1, 4)))]
    return norm(at(0.5, *parts), 0.7)


def bell(rng, fundamental=330.0, strikes=2, gap=1.1):
    """A town bell: the hum, prime, minor third, fifth and nominal of a cast bell."""
    def strike():
        p = [(0.5, 0.5, 2.8), (1.0, 0.9, 2.2), (1.2, 0.6, 1.6), (1.5, 0.35, 1.2), (2.0, 0.7, 1.0), (2.5, 0.25, 0.6), (3.0, 0.2, 0.45), (4.2, 0.1, 0.3)]
        tone = modes(rng, 3.2, [(fundamental * r, a, d) for r, a, d in p], jitter=0.002)
        clang = band(noise(rng, 0.05), lo=1500) * env(0.05, 0.0005, 0.01) * 0.6
        return at(3.2, (0, tone), (0, clang))
    return norm(at(3.2 + gap * (strikes - 1), *[(i * gap, strike()) for i in range(strikes)]), 0.8)


def chime(rng, freqs, seconds=1.2, decay=0.5, gap=0.09):
    parts = []
    for i, f in enumerate(freqs):
        parts.append((i * gap, modes(rng, seconds, [(f, 1, decay), (f * 2.01, 0.25, decay * 0.5), (f * 3.02, 0.1, decay * 0.3)])))
    return norm(at(seconds + gap * len(freqs), *parts), 0.6)


def tick(rng, f=1800, seconds=0.12):
    s = pluck(rng, f / 4, seconds, damping=0.97, bright=0.9) * env(seconds, 0.001, 0.03)
    s += modes(rng, seconds, [(f, 0.5, 0.02), (f * 1.5, 0.3, 0.015)])
    return norm(fade_edges(s), 0.5)


def fire_loop(rng, seconds=6.0, roar=1.0, crackle=1.0):
    n = noise(rng, seconds + 0.5)
    body = tilt(band(n, hi=900), -3)
    lfo = 0.7 + 0.3 * np.sin(2 * np.pi * 0.3 * t_axis(seconds + 0.5)) * np.sin(2 * np.pi * 0.17 * t_axis(seconds + 0.5) + 1)
    out = norm(body * lfo, 0.5) * roar
    pops = np.zeros_like(out)
    for _ in range(int(seconds * 14 * crackle)):
        i = int(rng.uniform(0, len(out) - 2000))
        k = int(rng.uniform(80, 900))
        pops[i:i + k] += rng.standard_normal(k) * np.exp(-np.arange(k) / (k / 5)) * rng.uniform(0.2, 1.0)
    out += band(pops, lo=1200, hi=9000) * 0.6 * crackle
    return norm(loopable(out), 0.8)


def whoosh(rng, seconds, lo, hi, peak=0.4, q=1.5):
    n = noise(rng, seconds)
    t = np.linspace(0, 1, len(n))
    centers = lo + (hi - lo) * np.sin(np.pi * t) ** 2
    s = sweep_band(n, centers, q) * np.sin(np.pi * np.clip(t / peak, 0, 1) / 2) * np.exp(-np.maximum(t - peak, 0) * 5)
    return norm(fade_edges(s), 0.8)


def hiss(rng, seconds, attack=0.02, decay=0.6, lo=2500, hi=12000):
    s = band(noise(rng, seconds), lo=lo, hi=hi) * env(seconds, attack, decay)
    bubbles = np.zeros_like(s)
    for _ in range(int(seconds * 30)):
        i = int(rng.uniform(0, len(s) - 600))
        k = int(rng.uniform(100, 500))
        bubbles[i:i + k] += np.sin(np.linspace(0, rng.uniform(20, 60), k)) * np.hanning(k) * rng.uniform(0.1, 0.4)
    return norm(s + bubbles * env(seconds, attack, decay * 1.5), 0.75)


def bubble_loop(rng, seconds=5.0):
    out = np.zeros(int((seconds + 0.5) * RATE))
    for _ in range(int(seconds * 18)):
        k = int(rng.uniform(0.03, 0.09) * RATE)
        f0 = rng.uniform(250, 700)
        tt = np.arange(k) / RATE
        f = f0 * (1 + 2.5 * tt / tt[-1])
        blip = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-tt / (k / RATE / 3))
        i = int(rng.uniform(0, len(out) - k))
        out[i:i + k] += blip * rng.uniform(0.2, 0.8)
    out += band(noise(rng, seconds + 0.5), hi=500) * 0.15
    return norm(loopable(out), 0.6)


def scrape_loop(rng, seconds, center, rate):
    n = noise(rng, seconds + 0.5)
    t = t_axis(seconds + 0.5)
    s = band(n, lo=center * 0.5, hi=center * 2) * (0.55 + 0.45 * np.sin(2 * np.pi * rate * t) ** 2)
    return norm(loopable(s), 0.6)


def grind_loop(rng, seconds=4.0):
    t = t_axis(seconds + 0.5)
    n = band(noise(rng, seconds + 0.5), lo=1800, hi=6000)
    whine = np.sin(2 * np.pi * 2900 * t + 3 * np.sin(2 * np.pi * 7 * t)) * 0.15
    s = (n + whine) * (0.8 + 0.2 * np.sin(2 * np.pi * 11 * t))
    return norm(loopable(s), 0.6)


def pour_loop(rng, seconds=4.0):
    n = noise(rng, seconds + 0.5)
    centers = 500 + 400 * np.abs(np.sin(np.cumsum(rng.uniform(0, 0.02, 200))))
    s = sweep_band(n, centers, 3) + band(n, lo=3000, hi=8000) * 0.15
    return norm(loopable(s), 0.6)


def clink(rng):
    f0 = rng.uniform(2600, 3600)
    s = modes(rng, 0.5, [(f0, 1, 0.2), (f0 * 2.32, 0.6, 0.12), (f0 * 4.1, 0.35, 0.07), (f0 * 0.61, 0.3, 0.1)])
    return norm(at(0.5, (0, s), (0, band(noise(rng, 0.02), lo=3000) * env(0.02, 0.0005, 0.004))), 0.6)


def knock(rng):
    f = rng.uniform(170, 230)
    s = modes(rng, 0.3, [(f, 1, 0.06), (f * 2.3, 0.6, 0.04), (f * 4.6, 0.3, 0.02)])
    s += band(noise(rng, 0.3), lo=400, hi=3000) * env(0.3, 0.0005, 0.01) * 0.8
    return norm(fade_edges(s), 0.8)


def rustle(rng, seconds=0.35):
    n = noise(rng, seconds)
    grains = (rng.uniform(0, 1, len(n)) > 0.985) * rng.uniform(0.3, 1, len(n))
    g = band(grains, lo=1500, hi=9000) * 4 + band(n, lo=800, hi=6000) * 0.5
    shape = np.sin(np.pi * np.linspace(0, 1, len(n))) ** 1.5
    return norm(fade_edges(g * shape), 0.55)


def scrub(rng):
    return rustle(rng, 0.28) * 0.9


def pinch(rng):
    parts = [(i * 0.03 + rng.uniform(0, 0.02), band(noise(rng, 0.015), lo=3000, hi=10000) * env(0.015, 0.0005, 0.003)) for i in range(int(rng.integers(4, 8)))]
    return norm(at(0.3, *parts), 0.4)


def click(rng):
    f = rng.uniform(3500, 5200)
    s = modes(rng, 0.08, [(f, 1, 0.012), (f * 1.7, 0.6, 0.008), (f * 0.45, 0.4, 0.015)])
    s += band(noise(rng, 0.08), lo=2000) * env(0.08, 0.0002, 0.002) * 0.8
    return norm(fade_edges(s), 0.5)


def ratchet(rng):
    return norm(at(0.35, *[(i * 0.055, click(rng) * (1 - i * 0.12)) for i in range(5)]), 0.5)


PENTATONIC = [523.25, 587.33, 659.25, 783.99, 880.0, 1046.5]


def rune(rng, i):
    return chime(rng, [PENTATONIC[i % len(PENTATONIC)]], seconds=0.9, decay=0.35) * 0.8


def weave(rng):
    notes = [PENTATONIC[i] for i in (0, 2, 4, 5)]
    c = chime(rng, notes, seconds=1.6, decay=0.6, gap=0.07)
    swell = band(noise(rng, len(c) / RATE), lo=3000, hi=10000) * np.sin(np.pi * np.linspace(0, 1, len(c))) * 0.08
    return norm(c + swell, 0.6)


def hum_loop(rng, seconds=6.0):
    t = t_axis(seconds + 0.5)
    lfo = 0.8 + 0.2 * np.sin(2 * np.pi * 0.25 * t)
    s = (np.sin(2 * np.pi * 220 * t) + np.sin(2 * np.pi * 220.7 * t) + 0.5 * np.sin(2 * np.pi * 330.4 * t) + 0.2 * np.sin(2 * np.pi * 440 * t)) * lfo
    return norm(loopable(s), 0.35)


def swing(rng):
    return whoosh(rng, 0.28, 500, 2200, peak=0.55, q=1.2) * 0.8


def miss(rng):
    return whoosh(rng, 0.22, 900, 3200, peak=0.5, q=1.5) * 0.7


def clash(rng):
    f0 = rng.uniform(900, 1300)
    ratios = [1, 1.73, 2.41, 3.37, 4.52, 5.93, 7.1]
    tone = modes(rng, 0.9, [(f0 * r, rng.uniform(0.3, 1) / (1 + i * 0.3), rng.uniform(0.12, 0.45)) for i, r in enumerate(ratios)], jitter=0.02)
    crack = band(noise(rng, 0.05), lo=1500, hi=9000) * env(0.05, 0.0002, 0.008) * 1.5
    # Steel rings bright, but not piercing: roll off the top.
    return norm(band(at(0.9, (0, tone), (0, crack)), hi=6500), 0.9)


def flesh(rng):
    t = t_axis(0.25)
    f = 120 * np.exp(-t * 12) + 55
    thump = np.sin(2 * np.pi * np.cumsum(f) / RATE) * env(0.25, 0.001, 0.06)
    slap = band(noise(rng, 0.25), lo=300, hi=3500) * env(0.25, 0.0005, 0.02)
    return norm(fade_edges(thump + slap * 0.7), 0.85)


def bow(rng):
    string = pluck(rng, rng.uniform(95, 120), 0.45, damping=0.985, bright=0.4) * env(0.45, 0.001, 0.12)
    return norm(at(0.5, (0, string), (0.01, whoosh(rng, 0.2, 1500, 4000, peak=0.2) * 0.5)), 0.8)


def roar(rng, seconds=2.4, base=70.0):
    """A big beast: a rough, growling voice through a throat of formants, rising then falling."""
    t = t_axis(seconds)
    u = t / seconds
    contour = base * (1 + 0.6 * np.sin(np.pi * np.clip(u * 1.3, 0, 1)) - 0.3 * u)
    rough = 1 + 0.12 * rng.standard_normal(len(t)).cumsum() / np.sqrt(len(t)) + 0.08 * np.sin(2 * np.pi * 23 * t)
    phase = np.cumsum(contour * rough) / RATE
    saw = 2 * (phase % 1) - 1
    growl = saw * (0.7 + 0.3 * np.sin(2 * np.pi * 31 * t))
    voice = sweep_band(growl, 380 + 300 * np.sin(np.pi * u), 1.2) + band(growl, lo=900, hi=2600) * 0.4
    breath = band(noise(rng, seconds), lo=200, hi=3000) * 0.35
    shape = np.clip(u / 0.1, 0, 1) * np.clip((1 - u) / 0.35, 0, 1)
    return norm(fade_edges((voice + breath) * shape), 0.95)


def breath(rng, seconds=1.8):
    n = noise(rng, seconds)
    roar_ = band(n, hi=1200) * 1.2 + band(n, lo=1200, hi=6000) * 0.4
    shape = np.clip(t_axis(seconds) / 0.08, 0, 1) * np.exp(-np.maximum(t_axis(seconds) - 0.3, 0) / 0.6)
    return norm(fade_edges(roar_ * shape + fire_loop(rng, seconds + 0.6, roar=0.3, crackle=2)[:len(n)] * shape * 0.5), 0.9)


def wings_loop(rng, seconds=2.5, rate=1.6):
    out = np.zeros(int((seconds + 0.5) * RATE))
    beat = 1 / rate
    k = 0
    while k * beat < seconds + 0.5:
        w = band(noise(rng, 0.45), hi=380) * np.sin(np.pi * np.linspace(0, 1, int(0.45 * RATE))) ** 2
        s = int(k * beat * RATE)
        n = min(len(w), len(out) - s)
        out[s:s + n] += w[:n] * rng.uniform(0.8, 1.0)
        k += 1
    return norm(loopable(out, 0.3), 0.8)


def wind_loop(rng, seconds=12.0):
    n = noise(rng, seconds + 1)
    t = t_axis(seconds + 1)
    gust = 0.55 + 0.45 * np.sin(2 * np.pi * 0.07 * t + 1) * np.sin(2 * np.pi * 0.13 * t)
    centers = 350 + 250 * (np.sin(2 * np.pi * 0.05 * t[::4410]) + 1)
    s = (tilt(band(n, hi=1400), -3) * 0.8 + sweep_band(n, centers, 4) * 0.5) * gust
    return norm(loopable(s, 1.0), 0.6)


def birds_loop(rng, seconds=14.0):
    out = np.zeros(int((seconds + 1) * RATE))
    for _ in range(int(seconds * 1.6)):
        f0 = rng.uniform(2200, 4200)
        notes = int(rng.integers(2, 6))
        start = rng.uniform(0, seconds)
        for j in range(notes):
            k = int(rng.uniform(0.05, 0.12) * RATE)
            tt = np.arange(k) / RATE
            f = f0 * (1 + rng.uniform(-0.3, 0.3) * tt / tt[-1]) * (1 + 0.04 * np.sin(2 * np.pi * rng.uniform(20, 50) * tt))
            chirp = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.hanning(k)
            i = int((start + j * rng.uniform(0.08, 0.16)) * RATE)
            if i + k < len(out):
                out[i:i + k] += chirp * rng.uniform(0.2, 0.6)
    out += band(noise(rng, seconds + 1), hi=600) * 0.03
    return norm(loopable(out, 1.0), 0.5)


def crickets_loop(rng, seconds=8.0):
    out = np.zeros(int((seconds + 1) * RATE))
    for c in range(5):
        f = rng.uniform(4200, 5200)
        period = rng.uniform(0.7, 1.2)
        offset = rng.uniform(0, period)
        amp = rng.uniform(0.3, 1.0)
        t0 = offset
        while t0 < seconds + 1:
            for p in range(int(rng.integers(3, 6))):
                k = int(0.012 * RATE)
                tt = np.arange(k) / RATE
                i = int((t0 + p * 0.022) * RATE)
                if i + k < len(out):
                    out[i:i + k] += np.sin(2 * np.pi * f * tt) * np.hanning(k) * amp
            t0 += period * rng.uniform(0.9, 1.1)
    out += band(noise(rng, seconds + 1), hi=300) * 0.02
    return norm(loopable(out, 0.8), 0.35)


def river_loop(rng, seconds=8.0):
    n = noise(rng, seconds + 1)
    s = tilt(band(n, lo=150, hi=4000), -2)
    bub = np.zeros_like(s)
    for _ in range(int(seconds * 60)):
        k = int(rng.uniform(0.01, 0.04) * RATE)
        tt = np.arange(k) / RATE
        f = rng.uniform(400, 1600) * (1 + 1.5 * tt / tt[-1])
        i = int(rng.uniform(0, len(bub) - k))
        bub[i:i + k] += np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.hanning(k) * rng.uniform(0.05, 0.25)
    return norm(loopable(norm(s, 0.5) + bub, 1.0), 0.6)


def crowd_loop(rng, seconds=10.0):
    """A room full of talk: many babbling voices (pulsed formant noise), a laugh, mugs."""
    out = np.zeros(int((seconds + 1) * RATE))
    t = t_axis(seconds + 1)
    for v in range(10):
        pitch = rng.uniform(90, 220)
        rate = rng.uniform(3, 6)
        talk = np.clip(np.sin(2 * np.pi * rate * t + rng.uniform(0, 6)) + rng.uniform(-0.2, 0.6), 0, 1)
        on = (np.sin(2 * np.pi * rng.uniform(0.05, 0.15) * t + rng.uniform(0, 6)) > rng.uniform(-0.3, 0.4)).astype(float)
        glott = np.sign(np.sin(2 * np.pi * pitch * t * (1 + 0.05 * np.sin(2 * np.pi * 0.5 * t)))) * 0.3 + band(noise(rng, seconds + 1), lo=200, hi=3000) * 0.4
        f1 = rng.uniform(450, 750)
        voiced = band(glott, lo=f1 * 0.6, hi=f1 * 2.2)
        out += voiced * talk * band(on, hi=20) * rng.uniform(0.3, 1.0)
    for _ in range(int(seconds * 0.6)):
        c = clink(rng) * rng.uniform(0.1, 0.25)
        i = int(rng.uniform(0, len(out) - len(c)))
        out[i:i + len(c)] += c
    return norm(loopable(band(out, hi=3500), 1.0), 0.6)


def din_loop(rng, seconds=8.0):
    """Fighting at a distance: clashes, thuds and shouting, softened by distance."""
    out = np.zeros(int((seconds + 1) * RATE))
    for _ in range(int(seconds * 5)):
        s = clash(rng) if rng.uniform() < 0.6 else flesh(rng)
        i = int(rng.uniform(0, len(out) - len(s)))
        out[i:i + len(s)] += s * rng.uniform(0.1, 0.5)
    for _ in range(int(seconds * 0.8)):
        s = roar(rng, rng.uniform(0.4, 0.9), base=rng.uniform(140, 260)) * 0.25
        i = int(rng.uniform(0, len(out) - len(s)))
        out[i:i + len(s)] += s
    out += band(noise(rng, seconds + 1), hi=500) * 0.1
    return norm(loopable(band(out, hi=4500), 1.0), 0.6)


VOWELS = {"a": (800, 1200), "e": (500, 1900), "i": (320, 2400), "o": (500, 850), "u": (330, 800)}


def syllable(rng, vowel, seconds=0.1, pitch=200.0):
    """A sung blip: a glottal pulse train through a vowel's two formants."""
    t = t_axis(seconds)
    f1, f2 = VOWELS[vowel]
    phase = np.cumsum(pitch * (1 + 0.04 * np.sin(2 * np.pi * 6 * t))) / RATE
    pulses = (phase % 1) ** 6 * 2 - 0.3
    voiced = band(pulses, lo=f1 * 0.7, hi=f1 * 1.3, slope=1.5) + 0.6 * band(pulses, lo=f2 * 0.8, hi=f2 * 1.25, slope=1.5)
    shape = np.clip(t / 0.012, 0, 1) * np.clip((seconds - t) / 0.04, 0, 1)
    return norm(voiced * shape, 0.7)


# name -> (folder, [signal makers]); variants become name_1, name_2...
def catalogue():
    v = lambda f, n: [f] * n
    return {
        "ui_open": ("sfx", [lambda r: tick(r, 1500)]),
        "ui_close": ("sfx", [lambda r: tick(r, 1100)]),
        "ui_coin": ("sfx", v(coin, 4)),
        "ui_bell": ("sfx", [lambda r: bell(r, 330.0, 3, 1.0)]),
        "ui_good": ("sfx", [lambda r: chime(r, [659.25, 987.77], 1.4, 0.5, 0.11)]),
        "anvil_good": ("sfx", v(lambda r: anvil(r, "good"), 3)),
        "anvil_fair": ("sfx", v(lambda r: anvil(r, "fair"), 3)),
        "anvil_poor": ("sfx", v(lambda r: anvil(r, "poor"), 3)),
        "anvil_cold": ("sfx", v(lambda r: anvil(r, "cold"), 2)),
        "forge_fire": ("amb", [lambda r: fire_loop(r, 6, roar=1.4, crackle=0.6)]),
        "forge_bellows": ("sfx", v(lambda r: whoosh(r, 0.9, 250, 900, peak=0.45, q=1.0), 2)),
        "forge_quench": ("sfx", v(lambda r: hiss(r, 2.2, 0.01, 0.7), 2)),
        "forge_steam": ("amb", [lambda r: norm(loopable(band(noise(r, 4.5), lo=3000, hi=12000)), 0.5)]),
        "work_grind": ("amb", [grind_loop]),
        "kitchen_bubble": ("amb", [bubble_loop]),
        "kitchen_stir": ("amb", [lambda r: scrape_loop(r, 4, 700, 0.8)]),
        "kitchen_pinch": ("sfx", v(pinch, 3)),
        "bar_pour": ("amb", [pour_loop]),
        "bar_clink": ("sfx", v(clink, 4)),
        "bar_knock": ("sfx", v(knock, 3)),
        "work_scrub": ("sfx", v(scrub, 3)),
        "work_cloth": ("sfx", v(rustle, 3)),
        "tinker_click": ("sfx", v(click, 4)),
        "tinker_ratchet": ("sfx", v(ratchet, 2)),
        "heal_rune": ("sfx", [lambda r, i=i: rune(r, i) for i in range(6)]),
        "heal_weave": ("sfx", [weave]),
        "heal_hum": ("amb", [hum_loop]),
        "combat_swing": ("sfx", v(swing, 3)),
        "combat_clash": ("sfx", v(clash, 5)),
        "combat_hit": ("sfx", v(flesh, 3)),
        "combat_bow": ("sfx", v(bow, 2)),
        "combat_miss": ("sfx", v(miss, 3)),
        "combat_din": ("amb", [din_loop]),
        "dragon_roar": ("sfx", [lambda r: roar(r, 2.4, 70), lambda r: roar(r, 1.8, 85), lambda r: roar(r, 3.0, 60)]),
        "dragon_breath": ("sfx", v(breath, 2)),
        "dragon_wings": ("amb", [wings_loop]),
        "amb_wind": ("amb", [wind_loop]),
        "amb_birds": ("amb", [birds_loop]),
        "amb_night": ("amb", [crickets_loop]),
        "amb_river": ("amb", [river_loop]),
        "amb_fire": ("amb", [lambda r: fire_loop(r, 5, roar=0.5, crackle=1.2)]),
        "amb_blaze": ("amb", [lambda r: fire_loop(r, 6, roar=2.0, crackle=1.6)]),
        "amb_crowd": ("amb", [crowd_loop]),
        "voice_blip": ("voice", [lambda r, k=k: syllable(r, k) for k in "aeiou"]),
    }


# ---------------------------------------------------------------- output

def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def to_ogg(x, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        wav = tmp.name
    try:
        write_wav(wav, x)
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", path], check=True)
    finally:
        os.remove(wav)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--preview", action="store_true", help="also write .cache/sounds/preview.ogg")
    ap.add_argument("--only", help="only names starting with this")
    args = ap.parse_args()
    made = []
    for name, (folder, makers) in catalogue().items():
        if args.only and not name.startswith(args.only):
            continue
        for i, make in enumerate(makers):
            rng = np.random.default_rng(abs(hash_name(name)) + i)
            x = fade_edges(make(rng)) if folder != "amb" else make(rng)
            suffix = f"_{i + 1}" if len(makers) > 1 else ""
            if folder == "voice":
                suffix = f"_{'aeiou'[i]}"
            path = os.path.join(OUT, folder, f"{name}{suffix}.ogg")
            to_ogg(x, path)
            made.append((name, x))
            print(f"{os.path.relpath(path, ROOT)}  {len(x) / RATE:.2f}s")
    if args.preview:
        os.makedirs(CACHE, exist_ok=True)
        gap = np.zeros(int(0.4 * RATE))
        sheet = np.concatenate([np.concatenate([x[:int(4 * RATE)], gap]) for _, x in made])
        path = os.path.join(CACHE, "preview.ogg")
        to_ogg(sheet, path)
        print(f"preview: {path} ({len(sheet) / RATE:.0f}s)")


def hash_name(name):
    """A stable seed from a name (Python's hash() changes per run)."""
    h = 2166136261
    for c in name.encode():
        h = ((h ^ c) * 16777619) & 0xFFFFFFFF
    return h


if __name__ == "__main__":
    sys.exit(main())
