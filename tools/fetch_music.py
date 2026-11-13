"""Placeholder music, fetched rather than kept in git: free tracks (CC0, or CC-BY and credited in
NOTICE.txt, the Credits window and data/sounds/CREDITS.md) downloaded into data/sounds/music/free/
as .ogg, two per mood, until the game's own songs replace them. Run once per checkout or build
machine; without it the game plays with no music (and --check-art says so).

    python3 tools/fetch_music.py            # download what's missing
    python3 tools/fetch_music.py --force    # download everything again

Needs ffmpeg (with libvorbis). The files go to data/sounds/music/free/<mood>_<name>.ogg, which
sounds.json's playlists pick up by wildcard.
"""
import argparse
import os
import subprocess
import sys
import tempfile
import urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "data", "sounds", "music", "free")
KM = "https://incompetech.com/music/royalty-free/mp3-royaltyfree/"
OGA = "https://opengameart.org/sites/default/files/"

# (mood, file name, title, author, licence, download URL, page)
TRACKS = [
    ("title", "skye_cuillin", "Skye Cuillin", "Kevin MacLeod", "CC-BY 4.0", KM + "Skye%20Cuillin.mp3", "https://incompetech.com"),
    ("title", "exploration", "Medieval: Exploration", "RandomMind", "CC0", OGA + "Exploration_0.mp3", "https://opengameart.org/content/medieval-exploration"),
    ("day", "village_consort", "Village Consort", "Kevin MacLeod", "CC-BY 4.0", KM + "Village%20Consort.mp3", "https://incompetech.com"),
    ("day", "market_day", "Medieval: Market Day", "RandomMind", "CC0", OGA + "Market_Day.mp3", "https://opengameart.org/content/medieval-market-day"),
    ("night", "teller_of_the_tales", "Teller of the Tales", "Kevin MacLeod", "CC-BY 4.0", KM + "Teller%20of%20the%20Tales.mp3", "https://incompetech.com"),
    ("night", "midnight_tale", "Midnight Tale", "Kevin MacLeod", "CC-BY 4.0", KM + "Midnight%20Tale.mp3", "https://incompetech.com"),
    ("danger", "varyag_war_drum", "Varyag War Drum", "Tausdei Music", "CC-BY 3.0", OGA + "varyag_war_drum.ogg", "https://opengameart.org/content/varyag-war-drum"),
    ("danger", "five_armies", "Five Armies", "Kevin MacLeod", "CC-BY 4.0", KM + "Five%20Armies.mp3", "https://incompetech.com"),
    ("festival", "master_of_the_feast", "Master of the Feast", "Kevin MacLeod", "CC-BY 4.0", KM + "Master%20of%20the%20Feast.mp3", "https://incompetech.com"),
    ("festival", "minstrel_dance", "Medieval: Minstrel Dance", "RandomMind", "CC0", OGA + "Minstrel_Dance_0.mp3", "https://opengameart.org/content/medieval-minstrel-dance"),
    ("reveal", "lament_for_a_warriors_soul", "Fantasy: Lament for a Warrior's Soul", "RandomMind", "CC0", OGA + "Lament_for_a_Warriors_Soul_REUPLOAD.mp3", "https://opengameart.org/content/fantasy-lament-for-a-warriors-soul"),
    ("reveal", "heavy_heart", "Heavy Heart", "Kevin MacLeod", "CC-BY 4.0", KM + "Heavy%20Heart.mp3", "https://incompetech.com"),
]


def fetch(url, path):
    request = urllib.request.Request(url, headers={"User-Agent": "heroes-not-included-fetch-music"})
    with urllib.request.urlopen(request, timeout=60) as response, open(path, "wb") as f:
        while chunk := response.read(1 << 16):
            f.write(chunk)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--force", action="store_true", help="download again even if present")
    args = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    failed = 0
    for mood, name, title, author, lic, url, _ in TRACKS:
        out = os.path.join(OUT, f"{mood}_{name}.ogg")
        if os.path.exists(out) and not args.force:
            print(f"have  {os.path.relpath(out, ROOT)}")
            continue
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "track" + os.path.splitext(url)[1])
            try:
                fetch(url, src)
                subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", src, "-vn", "-ac", "2", "-ar", "44100",
                                "-c:a", "libvorbis", "-q:a", "4", out], check=True)
                print(f"got   {os.path.relpath(out, ROOT)}  ({title}, {author}, {lic})")
            except Exception as e:  # one bad download shouldn't stop the rest
                failed += 1
                print(f"error {title}: {e}", file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
