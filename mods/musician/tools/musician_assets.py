#!/usr/bin/env python3
"""The musician mod's art and sounds, made from nothing (docs/MODDING.md): run from the repo root.

    python3 mods/musician/tools/musician_assets.py

Writes into the mod's own folders (textures/, sounds/), never the game's. The pipe is a tin
whistle in D: one root sample per octave, pitched up to the scale's other notes as they're played.
A breathy sine stack (the fundamental, a soft second and third) with a chiff at the start and a
little vibrato; placeholder sound, to be refined by ear.
"""
import math, os, struct, wave
import numpy as np
from PIL import Image, ImageDraw

MOD = os.path.join(os.path.dirname(__file__), "..")
RATE = 44100


def pipe_note(freq, seconds=1.4, seed=1):
    rng = np.random.default_rng(seed)
    t = np.arange(int(seconds * RATE)) / RATE
    vib = 1 + 0.004 * np.sin(2 * math.pi * 5.2 * t) * np.clip((t - 0.25) / 0.3, 0, 1)
    phase = 2 * math.pi * freq * np.cumsum(vib) / RATE
    tone = np.sin(phase) + 0.22 * np.sin(2 * phase) + 0.07 * np.sin(3 * phase)
    breath = rng.normal(0, 1, t.size)
    # A breath's hiss, filtered soft (a moving average), and a chiff where the note speaks.
    breath = np.convolve(breath, np.ones(18) / 18, mode="same")
    chiff = np.exp(-t / 0.025) * 0.9
    attack = np.clip(t / 0.035, 0, 1)
    release = np.clip((seconds - t) / 0.25, 0, 1)
    env = attack * release
    out = (tone * 0.55 + breath * (0.05 + chiff)) * env
    return out / np.max(np.abs(out)) * 0.8


def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = (np.clip(samples, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())


def pipe_icon(path):
    """A tin whistle on the slant: a pale tin body, a dark mouthpiece, six finger holes."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(6, 26), (26, 6)], fill=(60, 50, 44, 255), width=6)    # outline
    d.line([(6, 26), (26, 6)], fill=(196, 196, 186, 255), width=4)  # tin
    d.line([(7, 24), (24, 7)], fill=(232, 232, 220, 255), width=1)  # its shine
    d.line([(22, 10), (26, 6)], fill=(70, 52, 40, 255), width=4)    # the mouthpiece
    for k in range(6):
        x, y = 10 + k * 2, 22 - k * 2
        d.point((x, y), fill=(40, 34, 30, 255))
    img.save(path)


if __name__ == "__main__":
    # D5 and D6: the whistle's two octaves (the game pitches each up to the scale's notes).
    write_wav(os.path.join(MOD, "sounds", "pipe_d5.wav"), pipe_note(587.33, seed=1))
    write_wav(os.path.join(MOD, "sounds", "pipe_d6.wav"), pipe_note(1174.66, seed=2))
    pipe_icon(os.path.join(MOD, "textures", "items", "pipe.png"))
    print("musician assets written")
