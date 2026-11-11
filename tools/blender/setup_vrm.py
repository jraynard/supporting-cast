"""Set up a .blend for tools/blender/render_layers.py from a VRoid (VRM) model. Runs inside Blender,
with the VRM add-on installed (Blender: Edit > Preferences > Get Extensions > "VRM"):

    blender -b --python tools/blender/setup_vrm.py -- model.vrm art/3d/wanderer.json

Imports the model, splits its meshes by material and files each piece into the collection of the
config's layer it belongs to, picked by the config's "materials" list: [substring, collection]
pairs, first match wins, matched against VRoid's material names ("N00_000_00_Body_00_SKIN",
"N00_002_01_Tops_01_CLOTH", ...). A piece nothing matches goes in an "Unsorted" collection that
is left out of the renders, and is listed: drag it into a layer's collection in Blender (or add a
pair to the config) before rendering. The armature is renamed to the config's "root" and the file
saved as art/3d/<name>.blend (or the path after the config). Facing: VRM models import facing -Y,
toward the sprite camera, which is the convention render_layers.py expects.
"""
import json
import os
import sys

import bpy

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
UNSORTED = "Unsorted"


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 2:
        raise SystemExit("usage: blender -b --python tools/blender/setup_vrm.py -- <model.vrm> <config.json> [out.blend]")
    return [os.path.abspath(a) for a in argv[:3]]


def collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col


def split_by_material(obj):
    """One object per material (VRoid puts the whole face, or all the clothes, in one mesh)."""
    if len([s for s in obj.material_slots if s.material]) < 2:
        return [obj]
    before = set(bpy.data.objects)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    return [obj] + [o for o in bpy.data.objects if o not in before]


def main():
    paths = args()
    model, config = paths[0], paths[1]
    with open(config) as f:
        cfg = json.load(f)
    name = os.path.splitext(os.path.basename(config))[0]
    out = paths[2] if len(paths) > 2 else os.path.join(REPO, "art", "3d", name + ".blend")

    bpy.ops.wm.read_homefile(use_empty=True)       # not read_factory_settings: that turns add-ons off
    if "vrm" not in dir(bpy.ops.import_scene):
        raise SystemExit("the VRM add-on isn't installed: Blender > Edit > Preferences > Get Extensions, search VRM")
    bpy.ops.import_scene.vrm(filepath=model)

    rigs = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not rigs:
        raise SystemExit("the model has no armature")
    rigs[0].name = cfg["root"]

    rules = [(s.lower(), c) for s, c in cfg.get("materials", [])]
    layer_names = {layer["collection"] for layer in cfg["layers"] if layer.get("collection")}
    sorted_into, unsorted = {}, []
    for obj in [o for o in bpy.data.objects if o.type == "MESH"]:
        for piece in split_by_material(obj):
            mat = next((s.material.name for s in piece.material_slots if s.material), "")
            target = next((c for s, c in rules if s in mat.lower()), None)
            if target is not None and target not in layer_names:
                print(f"warning: {mat!r} matched {target!r}, which isn't one of the config's layers")
                target = None
            piece.name = mat or piece.name
            for c in list(piece.users_collection):
                c.objects.unlink(piece)
            collection(target or UNSORTED).objects.link(piece)
            (sorted_into.setdefault(target, []) if target else unsorted).append(mat or piece.name)

    for layer in cfg["layers"]:                     # every layer gets its collection, even an empty one
        if layer.get("collection"):
            collection(layer["collection"])
    if UNSORTED in bpy.data.collections:
        lc = bpy.context.view_layer.layer_collection.children.get(UNSORTED)
        if lc is not None:
            lc.exclude = True

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    if not any(o.type == "LIGHT" for o in bpy.data.objects):
        sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
        sun.rotation_euler = (0.7, 0.0, -0.6)      # from the upper left, like the game's art
        scene.collection.objects.link(sun)

    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=out)
    for target in sorted(sorted_into):
        print(f"{target}: {', '.join(sorted_into[target])}")
    if unsorted:
        print(f"{UNSORTED} (left out of the renders; move into a layer's collection or add a 'materials' rule): {', '.join(unsorted)}")
    print(f"saved {out}")


main()
