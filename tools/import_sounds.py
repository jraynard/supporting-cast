"""Recorded sounds (CC0) brought into data/sounds: trimmed, levelled, looped and converted to .ogg,
with data/sounds/CREDITS.md written from the sources' ledger. The packs are downloaded by hand (or
by a helper) outside the repo, since they're far bigger than what's kept. SPEC below says which
file becomes which; data/sounds/sounds.json decides which sound ids play them.

    python3 tools/import_sounds.py --from DIR --ledger DIR/ledger.json

DIR holds the unpacked packs (paths in SPEC are relative to it). The ledger is a JSON list of
{file, title, author, license, page} entries, one per source file. Rules for what may come in are
in docs/AUDIO.md: CC0 only for effects. Needs numpy and ffmpeg.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

import numpy as np

RATE = 44100
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "data", "sounds")

W = "fantasy-weapons-and-apparel-sfx-library/weapons-apparel/sfx/"
K = "50-rpg-sound-effects/RPGsounds_Kenney/OGG/"
P = "150-combat-sound-effects/150gameeffectsounds/01_combat_melee/"
B = "battle-sound-effects/battle_sound_effects_0/battle_sound_effects/"

# (output, mode, sources, options). Modes: "shot" (trimmed, peak-levelled one-shot, mono), "loop"
# (levelled, seamlessly looped; "stereo" keeps two channels for beds not placed in the world).
SPEC = [
    ("sfx/combat_clash_rec", "shot", [W + f"sword-knife-clash-{n}.wav" for n in (13, 14, 15, 16, 20, 21, 23, 24)], {}),
    ("sfx/combat_swing_rec", "shot", ["rpg-sound-pack/rpg_sound_pack/RPG Sound Pack/battle/swing.wav",
                                      "rpg-sound-pack/rpg_sound_pack/RPG Sound Pack/battle/swing2.wav",
                                      "rpg-sound-pack/rpg_sound_pack/RPG Sound Pack/battle/swing3.wav",
                                      B + "swish_2.wav", B + "swish_3.wav", B + "swish_4.wav"], {"peak": 0.7}),
    ("sfx/combat_hit_rec", "shot", [P + f for f in (
        "impact_punch_heavy_000_48kHz_16bit_0.65s_8fb58ba7_s02.wav", "impact_punch_heavy_001_48kHz_16bit_0.54s_fb104b0a_s02.wav",
        "impact_punch_heavy_002_48kHz_16bit_0.46s_565485a1_s02.wav", "impact_punch_heavy_003_48kHz_16bit_0.47s_2546df80_s02.wav",
        "impact_punch_heavy_004_48kHz_16bit_0.54s_b437d453_s02.wav", "impact_punch_medium_000_48kHz_16bit_0.36s_bd322da3_s02.wav")], {}),
    ("sfx/combat_bow_rec", "shot", [B + "Bow.wav"], {}),
    # A sandworm's roar and two beast growls, pitched down: a dragon's voice.
    ("sfx/dragon_roar_rec", "shot", ["cc0-deep-monster-roar/monster_roar.wav"], {"max": 5.0}),
    ("sfx/dragon_roar_rec_beast", "shot", ["monster-or-beast-sounds/monsters_or_beasts/Monsters or Beasts/Beast Growl 2.wav",
                                           "monster-or-beast-sounds/monsters_or_beasts/Monsters or Beasts/Beast Growl 3.wav"], {"pitch": 0.7}),
    ("sfx/dragon_breath_rec", "shot", ["fireball-1/105016__julien-matthey__jm-fx-fireball-01.wav",
                                       "fire-staff-sound-effects/legit_audio_fire_staff_-_free_0/LEGIT Audio_FIRE STAFF - FREE/Wavs/LEGIT_FIR_Fire_Staff 4-Audio.wav"],
     {"pitch": 0.8, "max": 3.0}),
    ("amb/dragon_wings_rec", "loop", ["large-wings-flap/wings_flap_large/wings_flap_large.ogg"], {"pitch": 0.8, "fade": 0.15}),
    ("sfx/forge_quench_rec", "shot", [f"steam-release-sounds/steam_hisses/steam hisses - Marker #{n}.wav" for n in (1, 2, 3)], {"peak": 0.75}),
    ("amb/kitchen_bubble_rec", "loop", ["boiling-water-loops/cooking_with_cover_01.ogg"], {"fade": 0.3}),
    ("sfx/work_cloth_rec", "shot", [K + f for f in ("cloth1.ogg", "cloth2.ogg", "cloth3.ogg", "cloth4.ogg", "clothBelt.ogg")], {"peak": 0.7}),
    ("sfx/ui_coin_rec", "shot", [K + "handleCoins.ogg", K + "handleCoins2.ogg"] + [f"fantasy-accessory-sfx-library/accessory/sfx/coinflip-0{n}.wav" for n in (1, 2, 3)], {"peak": 0.7}),
    ("sfx/bar_clink_rec", "shot", [f"100-cc0-sfx/100-CC0-SFX_0/glass_0{n}.ogg" for n in (1, 2, 3)] + ["100-cc0-sfx/100-CC0-SFX_0/dishes_01.ogg"], {"peak": 0.65}),
    ("sfx/bar_knock_rec", "shot", [f"100-cc0-metal-and-wood-sfx/100-CC0-wood-metal-SFX/wood_hit_0{n}.ogg" for n in (1, 2, 3)], {}),
    ("sfx/tinker_click_rec", "shot", ["door-lock-sounds/DoorLockSounds/DoorLockSounds/DoorLock.ogg",
                                      "door-lock-sounds/DoorLockSounds/DoorLockSounds/UnlockDoor.ogg",
                                      K + "metalClick.ogg", K + "metalLatch.ogg"], {"peak": 0.6}),
    ("amb/amb_wind_rec", "loop", ["wind1/wind1.wav"], {"stereo": True, "max": 40.0, "rms": 0.08}),
    ("amb/amb_birds_rec", "loop", ["amb-morning-sounds-perfect-loop/amb_morning_0.ogg"], {"stereo": True, "perfect": True, "rms": 0.06}),
    ("amb/amb_night_rec", "loop", ["crickets-ambient-noise-loopable/crickets_1.mp3"], {"stereo": True, "perfect": True, "rms": 0.05}),
    ("amb/amb_river_rec", "loop", ["100-cc0-sfx-2/sfx_100_v2/sfx100v2_loop_water_01.ogg"], {"perfect": True, "rms": 0.1}),
    ("amb/amb_fire_rec", "loop", ["fireplace-sound-loop/fire.wav"], {"max": 20.0, "rms": 0.08}),
    # A shouting crowd, dulled as though from down the road: the din of a fight in town.
    ("amb/combat_din_rec", "loop", ["crowd-shoutingspeaking-ambience/crowd_shouting_0.ogg"], {"stereo": True, "lowpass": 2500, "rms": 0.08}),
]


def decode(path, stereo=False, pitch=1.0, lowpass=None):
    filters = []
    if pitch != 1.0:
        filters.append(f"asetrate={int(RATE * pitch)},aresample={RATE}")
    if lowpass:
        filters.append(f"lowpass=f={lowpass}")
    cmd = ["ffmpeg", "-loglevel", "error", "-i", path, "-ac", "2" if stereo else "1", "-ar", str(RATE)]
    if filters:
        cmd += ["-af", ",".join(filters)]
    raw = subprocess.run(cmd + ["-f", "f32le", "-"], capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype="<f4").astype(np.float64)
    return x.reshape(-1, 2) if stereo else x.reshape(-1, 1)


def trim(x, threshold=0.004):
    """Cut leading and trailing near-silence, leaving a hair of room."""
    loud = np.where(np.max(np.abs(x), axis=1) > threshold)[0]
    if len(loud) == 0:
        return x
    a = max(0, loud[0] - int(0.005 * RATE))
    b = min(len(x), loud[-1] + int(0.05 * RATE))
    return x[a:b]


def fades(x, start_ms=3, end_ms=30):
    x = x.copy()
    a, b = int(start_ms / 1000 * RATE), int(end_ms / 1000 * RATE)
    if len(x) > a + b:
        x[:a] *= np.linspace(0, 1, a)[:, None]
        x[-b:] *= np.linspace(1, 0, b)[:, None]
    return x


def loop(x, fade):
    k = int(fade * RATE)
    if len(x) < 3 * k:
        return x
    ramp = np.linspace(0, 1, k)[:, None]
    return np.concatenate([x[k:-k], x[:k] * ramp + x[-k:] * (1 - ramp)])


def encode(x, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix=".raw", delete=False) as tmp:
        tmp.write(np.clip(x, -1, 1).astype("<f4").tobytes())
        raw = tmp.name
    try:
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-f", "f32le", "-ar", str(RATE), "-ac", str(x.shape[1]), "-i", raw,
                        "-c:a", "libvorbis", "-q:a", "4", "-serial_offset", "1", "-fflags", "+bitexact", path], check=True)
    finally:
        os.remove(raw)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--from", dest="src", required=True, help="folder holding the unpacked packs")
    ap.add_argument("--ledger", required=True, help="JSON list of sources and their licences")
    args = ap.parse_args()
    src = os.path.abspath(args.src)
    with open(args.ledger) as f:
        ledger = {os.path.relpath(e["file"], src): e for e in json.load(f)}

    credits = {}
    for out, mode, sources, o in SPEC:
        for i, rel in enumerate(sources):
            info = ledger.get(rel)
            if info is None or info["license"] != "CC0":
                sys.exit(f"{rel}: no CC0 entry in the ledger; effects must be CC0 (docs/AUDIO.md)")
            stereo = o.get("stereo", False)
            x = decode(os.path.join(src, rel), stereo, o.get("pitch", 1.0), o.get("lowpass"))
            if "max" in o:
                x = x[:int(o["max"] * RATE)]
            if mode == "shot":
                x = fades(trim(x))
                x *= o.get("peak", 0.9) / max(np.max(np.abs(x)), 1e-6)
            else:
                if not o.get("perfect"):
                    x = loop(x, o.get("fade", 1.0))
                x *= o.get("rms", 0.1) / max(np.sqrt(np.mean(x ** 2)), 1e-6)
                x = np.clip(x, -0.98, 0.98)
            name = f"{out}_{i + 1}.ogg" if len(sources) > 1 else f"{out}.ogg"
            encode(x, os.path.join(OUT, name))
            key = (info["page"], info["title"], info["author"], info["license"])
            credits.setdefault(key, []).append(name)
            print(f"{name}  {len(x) / RATE:.2f}s  <- {rel}")

    lines = [
        "# Recorded sounds",
        "",
        "Every sound in data/sounds that wasn't synthesized by tools/sfx_synth.py, and where it came from.",
        "All are CC0 (public domain): no credit is required, but it's given here gladly. Written by",
        "tools/import_sounds.py; music is listed under its own heading by hand.",
        "",
        "## Effects",
        "",
    ]
    for (page, title, author, lic), files in sorted(credits.items(), key=lambda kv: kv[0][1].lower()):
        lines.append(f"- **{title}** by {author} ({lic}), {page}")
        lines.append(f"  - {', '.join(sorted(files))}")
    path = os.path.join(OUT, "CREDITS.md")
    music = ""
    if os.path.exists(path):
        with open(path) as f:
            text = f.read()
        if "\n## Music" in text:
            music = text[text.index("\n## Music"):]
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n" + music)
    print(f"credits: {os.path.relpath(path, ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
