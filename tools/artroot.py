"""Where a generated sprite goes: the game's data/textures, or the mod that owns it (docs/MODDING.md).

A mod owns a sprite once its file is in the mod's own textures/ folder (mods/<id>/textures/<name>.png):
from then on every generator writes it there, so moving a sprite into a mod keeps it there. A new sprite
goes to the game's, unless the generator names the mod (``mod=``).
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
CORE = os.path.normpath(os.path.join(HERE, "..", "data", "textures"))
MODS = os.path.normpath(os.path.join(HERE, "..", "mods"))


def owner(name):
    """The mod whose textures/ holds this sprite, or None for the game's own."""
    if os.path.isdir(MODS):
        for mod in sorted(os.listdir(MODS)):
            if os.path.exists(os.path.join(MODS, mod, "textures", name + ".png")):
                return mod
    return None


def sound_path(rel, mod=None):
    """A generated sound file (``"amb/work_saw.ogg"``, relative to a sounds folder): the named mod's, else the
    mod's whose sounds/ already holds it, else the game's data/sounds."""
    if mod:
        p = os.path.join(MODS, mod, "sounds", rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        return p
    if os.path.isdir(MODS):
        for mod in sorted(os.listdir(MODS)):
            p = os.path.join(MODS, mod, "sounds", rel)
            if os.path.exists(p):
                return p
    p = os.path.normpath(os.path.join(HERE, "..", "data", "sounds", rel))
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


def texture_path(name, mod=None):
    """The file a sprite named ``name`` (``"buildings/sawhorse"``, no extension) is written to."""
    mod = mod or owner(name)
    root = os.path.join(MODS, mod, "textures") if mod else CORE
    path = os.path.join(root, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    return path
