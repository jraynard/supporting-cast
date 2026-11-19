#!/usr/bin/env python3
"""The musician mod's art and sounds, made from nothing (docs/MODDING.md): run from the repo root.

    python3 mods/musician/tools/musician_assets.py

Writes into the mod's own folders (textures/, sounds/), never the game's. The pipe is a tin
whistle in D: one root sample per octave, pitched up to the scale's other notes as they're played.
A breathy sine stack (the fundamental, a soft second and third) with a chiff at the start and a
little vibrato; placeholder sound, to be refined by ear. A held note is a loop of the same tone (seamless,
no chiff) with a burst of breath played as it starts. The lute is plucked (Karplus-Strong), the fiddle a bowed
sawtooth through a body's resonances (held on a seamless loop, as the pipe), the hand drum three strokes.
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


def pipe_loop(freq, seconds=1.0, seed=1):
    """A held note's tone, to loop while the key's held: no chiff, no fade, and every part of it (the tone, its
    vibrato, the breath's hiss) a whole number of cycles over the loop, so it runs on with no seam."""
    rng = np.random.default_rng(seed)
    cycles = round(freq * seconds)
    n = round(cycles * RATE / freq)
    t = np.arange(n) / RATE
    vib_cycles = round(5.2 * n / RATE)
    vib = 0.004 * np.sin(2 * math.pi * vib_cycles * np.arange(n) / n)
    # The phase climbs a whole number of cycles over the loop: the vibrato's wobble sums to nothing over it.
    phase = 2 * math.pi * cycles * np.arange(n) / n + 2 * math.pi * freq * np.cumsum(vib) / RATE
    tone = np.sin(phase) + 0.22 * np.sin(2 * phase) + 0.07 * np.sin(3 * phase)
    # The hiss, softened by a moving average taken round the loop's end, so it wraps.
    noise = rng.normal(0, 1, n)
    breath = np.real(np.fft.ifft(np.fft.fft(noise) * np.fft.fft(np.r_[np.ones(18) / 18, np.zeros(n - 18)])))
    out = tone * 0.55 + breath * 0.05
    return out / np.max(np.abs(out)) * 0.8


def pipe_chiff(seconds=0.09, seed=3):
    """The breath's burst as a note speaks: played with the held tone's start."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(seconds * RATE)) / RATE
    breath = np.convolve(rng.normal(0, 1, t.size), np.ones(10) / 10, mode="same")
    out = breath * np.exp(-t / 0.02) * np.clip(t / 0.003, 0, 1)
    return out / np.max(np.abs(out)) * 0.6


def lute_note(freq, seconds=2.6, seed=6):
    """A plucked course (Karplus-Strong): a burst of noise round a delay line a period long, each pass averaged
    (the string's damping), so the brightness goes first and the note dies away of itself. Two strings a hair apart,
    as a lute's courses are doubled."""
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    out = np.zeros(n)
    for detune, gain in ((1.0, 1.0), (1.0025, 0.7)):
        period = RATE / (freq * detune)
        p = int(period)
        frac = period - p
        buf = rng.uniform(-1, 1, p)
        buf = np.convolve(buf, [0.5, 0.5], mode="same")  # a pluck with the flesh of the thumb, not a pick
        line = np.zeros(n)
        line[:p] = buf
        for i in range(p, n):
            # A fractional delay (linear) keeps it in tune; 0.996 the string's loss, a little more for the brightness.
            a = line[i - p]
            b = line[i - p - 1] if i - p - 1 >= 0 else 0.0
            line[i] = 0.996 * (0.5 * ((1 - frac) * a + frac * b) + 0.5 * ((1 - frac) * b + frac * (line[i - p - 2] if i - p - 2 >= 0 else 0.0)))
        out += gain * line
    # The body: a soft lowpass, and a quick fade at the very end.
    out = np.convolve(out, np.ones(3) / 3, mode="same")
    t = np.arange(n) / RATE
    out *= np.clip((seconds - t) / 0.3, 0, 1)
    return out / np.max(np.abs(out)) * 0.8


def fiddle_body(freq):
    """A fiddle body's response at a frequency (Hz): its air and wood resonances and the bridge's hill, fixed in Hz
    (so each key's sample is made at its own pitch, never pitched up from another, or the body would move with it)."""
    lf = np.log2(np.maximum(freq, 1.0))
    def peak(hz, gain, width):  # a resonance, its width in octaves
        return gain * np.exp(-((lf - math.log2(hz)) / width) ** 2)
    response = 0.22 + peak(280, 0.9, 0.18) + peak(460, 1.0, 0.2) + peak(720, 0.55, 0.25) + peak(2900, 1.0, 0.5)
    response -= peak(1500, 0.18, 0.3)  # the dip between the wood and the bridge
    return np.maximum(response, 0.05) * np.where(freq > 9000, 0, 1)


def fiddle_wave(freq, n, cycles, vib_cycles, depth=0.0105):
    """A bowed string (its Helmholtz motion: a sawtooth's harmonics) through the body, with vibrato: as the pitch
    swings, each harmonic slides over the body's resonances, and the tone shimmers as a real fiddle's does."""
    i = np.arange(n)
    vib = depth * np.sin(2 * math.pi * vib_cycles * i / n)
    # The phase climbs whole cycles over the loop; the vibrato's wobble sums to nothing over it.
    phase = 2 * math.pi * cycles * i / n + 2 * math.pi * freq * np.cumsum(vib) / RATE
    out = np.zeros(n)
    for k in range(1, int(9000 / freq) + 1):
        inst = k * freq * (1 + vib)
        out += fiddle_body(inst) * np.sin(k * phase) / k
    return out, phase


def fiddle_loop(freq, seconds=1.2, seed=8):
    """A held bow's tone, seamless: tone, vibrato and rosin each a whole number of turns over the loop."""
    rng = np.random.default_rng(seed)
    cycles = round(freq * seconds)
    n = round(cycles * RATE / freq)
    vib_cycles = round(5.6 * n / RATE)
    tone, phase = fiddle_wave(freq, n, cycles, vib_cycles)
    # Rosin: hiss that comes with each slip of the string (it follows the cycle), high and soft, wrapped round the loop.
    noise = rng.normal(0, 1, n)
    spectrum = np.fft.rfft(noise)
    f = np.fft.rfftfreq(n, 1 / RATE)
    spectrum *= np.clip((f - 1800) / 1500, 0, 1) * np.exp(-f / 9000)
    rosin = np.fft.irfft(spectrum, n) * (0.55 + 0.45 * np.cos(phase))
    rosin /= np.max(np.abs(rosin)) + 1e-9
    tone /= np.max(np.abs(tone))
    out = tone * 0.8 + rosin * 0.06
    return out / np.max(np.abs(out)) * 0.8


def fiddle_note(freq, seconds=1.4, seed=7):
    """A bowed note, one-shot: the loop's tone with the bow's swell in and a lift off."""
    loop = fiddle_loop(freq, seed=seed)
    n = int(seconds * RATE)
    out = np.resize(loop, n)
    t = np.arange(n) / RATE
    return out * np.clip(t / 0.09, 0, 1) * np.clip((seconds - t) / 0.2, 0, 1)


def fiddle_bow(seconds=0.1, seed=9):
    """The bow biting as a note starts: a soft, high scrape (the tone swells in under it, as the bow takes hold)."""
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    spectrum = np.fft.rfft(rng.normal(0, 1, n))
    f = np.fft.rfftfreq(n, 1 / RATE)
    spectrum *= np.clip((f - 1200) / 1200, 0, 1) * np.exp(-f / 7000)
    scrape = np.fft.irfft(spectrum, n)
    out = scrape * np.exp(-t / 0.03) * np.clip(t / 0.008, 0, 1)
    return out / np.max(np.abs(out)) * 0.4


def drum_stroke(kind, seed=10):
    """A frame drum's strokes: doum (the open middle: a low membrane, its pitch falling as it settles), tek (the edge:
    higher, short) and ka (the rim: a dry click)."""
    rng = np.random.default_rng(seed)
    seconds = {"doum": 0.7, "tek": 0.25, "ka": 0.12}[kind]
    t = np.arange(int(seconds * RATE)) / RATE
    if kind == "doum":
        f = 82 + 40 * np.exp(-t / 0.03)
        phase = 2 * math.pi * np.cumsum(f) / RATE
        body = np.sin(phase) + 0.3 * np.sin(1.59 * phase)
        # Its harmonics, dying a little sooner than the body: small speakers (a Steam Deck's, a laptop's) can't
        # sound 82 Hz at all, and from these the ear hears the low note that isn't there. A little drive rounds
        # them into one thump; on big speakers and headphones the body's still underneath.
        over = 0.55 * np.sin(2 * phase) + 0.4 * np.sin(3 * phase) + 0.22 * np.sin(4 * phase) + 0.1 * np.sin(5 * phase)
        tone = body * np.exp(-t / 0.3) + over * np.exp(-t / 0.16)
        tone = np.tanh(2.2 * tone) / np.tanh(2.2)
        out = tone + rng.normal(0, 1, t.size) * np.exp(-t / 0.006) * 0.4
    elif kind == "tek":
        f = 330 + 60 * np.exp(-t / 0.01)
        phase = 2 * math.pi * np.cumsum(f) / RATE
        body = np.sin(phase) + 0.5 * np.sin(2.3 * phase) + 0.3 * np.sin(3.6 * phase)
        # Driven and let ring a little longer, so a quick stroke holds its own beside a held note.
        tone = np.tanh(2.5 * body * np.exp(-t / 0.08)) / np.tanh(2.5)
        out = tone + rng.normal(0, 1, t.size) * np.exp(-t / 0.004) * 0.5
    else:
        click = np.convolve(rng.normal(0, 1, t.size), [1, -1], mode="same")
        # The rim's crack, and the shell's knock under it, so the click has some weight.
        knock = np.sin(2 * math.pi * 420 * t) * np.exp(-t / 0.03)
        out = np.tanh(2.0 * (click * np.exp(-t / 0.016) * 0.6 + knock + np.sin(2 * math.pi * 1250 * t) * np.exp(-t / 0.025) * 0.4))
    out *= np.clip(t / 0.001, 0, 1)
    return out / np.max(np.abs(out)) * 0.85


def string_snap(seconds=0.35, seed=14):
    """A string giving way: a sharp crack and a twang falling away."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(seconds * RATE)) / RATE
    crack = np.convolve(rng.normal(0, 1, t.size), [1, -1], mode="same") * np.exp(-t / 0.006)
    f = 900 * np.exp(-t / 0.08) + 180
    twang = np.sin(2 * math.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.09)
    out = crack * 0.8 + twang * 0.6
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


def lute_icon(path):
    """A lute: a round-backed body, a pale rose, a neck bent back at the pegbox."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(15, 16), (27, 4)], fill=(60, 40, 28, 255), width=4)     # the neck's outline
    d.line([(15, 16), (27, 4)], fill=(150, 104, 62, 255), width=2)   # the neck
    d.line([(26, 5), (29, 8)], fill=(90, 60, 38, 255), width=3)      # the pegbox, bent back
    d.ellipse([3, 12, 21, 30], fill=(60, 40, 28, 255))
    d.ellipse([4, 13, 20, 29], fill=(196, 140, 78, 255))            # the body
    d.ellipse([9, 18, 14, 23], fill=(70, 48, 30, 255))              # the rose
    d.line([(12, 21), (26, 6)], fill=(232, 220, 190, 255), width=1)  # the strings
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def fiddle_icon(path):
    """A fiddle on the slant, its bow across it."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(16, 15), (27, 4)], fill=(50, 34, 24, 255), width=3)     # the neck
    d.ellipse([4, 15, 15, 26], fill=(60, 30, 18, 255))
    d.ellipse([9, 10, 19, 20], fill=(60, 30, 18, 255))
    d.ellipse([5, 16, 14, 25], fill=(170, 80, 40, 255))             # the lower bout
    d.ellipse([10, 11, 18, 19], fill=(170, 80, 40, 255))            # the upper bout
    d.line([(9, 21), (14, 16)], fill=(40, 24, 16, 255), width=1)     # the bridge and fingerboard
    d.line([(4, 8), (26, 28)], fill=(70, 50, 36, 255), width=2)      # the bow
    d.line([(4, 9), (26, 29)], fill=(236, 230, 210, 255), width=1)   # its hair
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def drum_icon(path):
    """A frame drum, seen a little from above: a pale skin in a wooden hoop."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([3, 7, 29, 27], fill=(60, 40, 28, 255))
    d.ellipse([4, 8, 28, 26], fill=(150, 100, 58, 255))             # the hoop
    d.ellipse([6, 8, 26, 22], fill=(228, 210, 170, 255))            # the skin
    d.ellipse([12, 12, 20, 18], fill=(210, 188, 146, 255))          # worn where it's struck
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def hat_icon(path):
    """A musician's felt hat, brim up to take coins, a feather in its band."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([2, 16, 30, 28], fill=(40, 30, 26, 255))
    d.ellipse([3, 17, 29, 27], fill=(96, 70, 52, 255))              # the brim
    d.rounded_rectangle([9, 8, 23, 22], radius=5, fill=(40, 30, 26, 255))
    d.rounded_rectangle([10, 9, 22, 21], radius=4, fill=(118, 86, 62, 255))  # the crown
    d.rectangle([10, 17, 22, 19], fill=(150, 46, 40, 255))          # its band
    d.line([(20, 17), (27, 6)], fill=(232, 220, 190, 255), width=2)  # a feather
    d.ellipse([12, 22, 15, 25], fill=(214, 214, 220, 255))          # a coin in it
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def strings_icon(path):
    """A coil of gut string, pale and tied off."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for k, r in enumerate((11, 9, 7)):
        d.ellipse([16 - r, 17 - r * 0.75, 16 + r, 17 + r * 0.75], outline=(60, 44, 30, 255), width=3)
        d.ellipse([16 - r, 17 - r * 0.75, 16 + r, 17 + r * 0.75], outline=(222, 206, 160, 255), width=1)
    d.line([(24, 11), (29, 5)], fill=(222, 206, 160, 255), width=1)  # its loose end
    d.rectangle([14, 6, 18, 9], fill=(150, 46, 40, 255))            # the tie
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


if __name__ == "__main__":
    # D5 and D6: the whistle's two octaves (the game pitches each up to the scale's notes).
    write_wav(os.path.join(MOD, "sounds", "pipe_d5.wav"), pipe_note(587.33, seed=1))
    write_wav(os.path.join(MOD, "sounds", "pipe_d6.wav"), pipe_note(1174.66, seed=2))
    write_wav(os.path.join(MOD, "sounds", "pipe_d5_loop.wav"), pipe_loop(587.33, seed=4))
    write_wav(os.path.join(MOD, "sounds", "pipe_d6_loop.wav"), pipe_loop(1174.66, seed=5))
    write_wav(os.path.join(MOD, "sounds", "pipe_chiff.wav"), pipe_chiff())
    pipe_icon(os.path.join(MOD, "textures", "items", "pipe.png"))
    # The lute and the fiddle in D, D4 and D5; the drum's three strokes.
    snd = lambda name: os.path.join(MOD, "sounds", name)
    write_wav(snd("lute_d4.wav"), lute_note(293.66, seed=6))
    write_wav(snd("lute_d5.wav"), lute_note(587.33, seconds=2.0, seed=11))
    write_wav(snd("fiddle_d4.wav"), fiddle_note(293.66, seed=7))
    write_wav(snd("fiddle_d5.wav"), fiddle_note(587.33, seed=12))
    # The fiddle's held notes, one per key (two octaves of the D scale), each made at its own pitch.
    for octave in range(2):
        for degree, semis in enumerate([0, 2, 4, 5, 7, 9, 11, 12]):
            freq = 293.66 * 2 ** ((semis + 12 * octave) / 12)
            write_wav(snd(f"fiddle_k{octave * 8 + degree:02d}_loop.wav"), fiddle_loop(freq, seed=20 + octave * 8 + degree))
    write_wav(snd("fiddle_bow.wav"), fiddle_bow())
    write_wav(snd("string_snap.wav"), string_snap())
    for kind in ("doum", "tek", "ka"):
        write_wav(snd(f"drum_{kind}.wav"), drum_stroke(kind))
    lute_icon(os.path.join(MOD, "textures", "items", "lute.png"))
    fiddle_icon(os.path.join(MOD, "textures", "items", "fiddle.png"))
    drum_icon(os.path.join(MOD, "textures", "items", "hand_drum.png"))
    hat_icon(os.path.join(MOD, "textures", "items", "hat.png"))
    strings_icon(os.path.join(MOD, "textures", "items", "strings.png"))
    print("musician assets written")
