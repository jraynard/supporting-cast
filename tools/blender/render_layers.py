"""Render a rigged 3D character's look layers for the game's sprite sheets. Runs inside Blender:

    blender -b art/3d/wanderer.blend --python tools/blender/render_layers.py -- art/3d/wanderer.json

Then turn the renders into sheets with tools/sprites_from_renders.py (plain Python + Pillow).
docs/ART.md "3D to sprites" explains the scene conventions; the config (JSON) says:

  frame, pivot          the sheet's frame size and the feet's pixel ([40, 60], [20, 58])
  pixels_per_meter      how big the character comes out (a 1.75 m person at 31 px/m is ~54 px tall)
  supersample           renders are this many times the frame size (8); the next step shrinks them
  elevation             the camera's angle above the horizon, in degrees (the game looks down a little)
  root, rig             the object turned to face each view (the armature, usually) and the one whose
                        action is played (the same armature, usually)
  views                 view name -> degrees the root is turned about Z (0: facing the camera)
  layers                bottom to top: {"collection": Blender collection, "sheet": game sheet, "role": ...}
  bone_scale            {bone: factor}: the head bigger than the model's (a 3-heads-tall chibi from a
                        7-heads VRoid figure), on every frame (don't key scale on those bones)
  rest_pose             {bone: [x, y, z] degrees about the world axes}: a pose for tags whose action
                        doesn't exist yet (arms down from a VRM's T-pose)
  light                 the direction the sprite light shines, in camera terms [right, up, toward the
                        camera] ([0.6, -0.55, -0.45]: from the upper left, in front, like the game's art);
                        the scene's own lights are off while rendering
  layer "shade": "toon" renders that layer's objects with a white toon material instead of theirs
                        (keeping only their textures' alpha cut-outs): for tinted parts whose painted
                        texture fights the game's light, like VRoid hair's dark roots. "toon" in the
                        config ([[lit from, brightness], ...], 0-1) sets its steps
  samples               EEVEE samples per pixel (8; Blender's 64 changes nothing after the shrink)
  lying_center          for "camera": "top" tags (sleep, downed): the pixel the middle of the body lands
                        on ([20, 32]); the rig is turned and moved so it lies head up, whatever angle
                        and place the animation leaves it in ("head", "feet": the bones measured)
  tags                  {"name": "walk_{view}", "action": Blender action, "frames": [Blender frames],
                         "ms": per frame, "loop": true, "event": -1, "views": true, "camera": "top"}

Each layer renders with the layers drawn before it as holdouts and the later ones left out: it's
cut where an earlier layer is in front of it (the hair behind the body), and whole where later
layers will paint over it (the body under a shirt, which may come off), on a transparent background. Writes <renders>/NNNN_LL.png per frame and layer, and renders.json.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def config_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        raise SystemExit("usage: blender -b <file.blend> --python tools/blender/render_layers.py -- <config.json>")
    return os.path.abspath(argv[0])


def find_layer_collection(lc, name):
    if lc.collection.name == name:
        return lc
    for child in lc.children:
        found = find_layer_collection(child, name)
        if found is not None:
            return found
    return None


def set_action(obj, action):
    """Play an action on an object (Blender 4.4+ also wants the action's slot)."""
    if obj.animation_data is None:
        obj.animation_data_create()
    obj.animation_data.action = action
    if action is not None and hasattr(obj.animation_data, "action_slot") and getattr(action, "slots", None):
        obj.animation_data.action_slot = action.slots[0]


def pose_bones(rig, cfg, posed):
    """The config's bone scales, and the stand-in pose when there's no action. Pose bones are in
    armature space; turning about a bone's head in that space is turning about the world's axes as
    long as the rig itself isn't turned (it is, per view: the root turns after this, as a whole)."""
    if rig.pose is None:
        return
    if not posed:
        for pb in rig.pose.bones:
            pb.location = (0.0, 0.0, 0.0)
            pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
            pb.rotation_euler = (0.0, 0.0, 0.0)
    for bone, factor in cfg.get("bone_scale", {}).items():
        pb = rig.pose.bones.get(bone)
        if pb is None:
            raise SystemExit(f"bone_scale: no bone named {bone!r}")
        pb.scale = (factor, factor, factor) if isinstance(factor, (int, float)) else tuple(factor)
    if not posed:
        bpy.context.view_layer.update()
        for bone, degrees in cfg.get("rest_pose", {}).items():
            pb = rig.pose.bones.get(bone)
            if pb is None:
                raise SystemExit(f"rest_pose: no bone named {bone!r}")
            head = pb.head.copy()
            turn = Euler([math.radians(d) for d in degrees], "XYZ").to_matrix().to_4x4()
            pb.matrix = Matrix.Translation(head) @ turn @ Matrix.Translation(-head) @ pb.matrix
            bpy.context.view_layer.update()


TOON = [[0.0, 0.3], [0.3, 0.5], [0.6, 0.75], [0.85, 1.0]]   # [lit from, brightness as shown]


def toon_material(original, steps):
    """White, lit only by the sprite light, in constant steps; the original's first image texture
    still cuts it out where it's transparent (hair strand tips, lashes)."""
    mat = bpy.data.materials.new(f"SpriteToon {original.name if original else ''}")
    mat.use_nodes = True
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    diffuse = nodes.new("ShaderNodeBsdfDiffuse")
    diffuse.inputs["Color"].default_value = (1, 1, 1, 1)
    to_rgb = nodes.new("ShaderNodeShaderToRGB")
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    elements = ramp.color_ramp.elements
    while len(elements) < len(steps):
        elements.new(1.0)
    for el, (at, value) in zip(elements, steps):
        v = value ** 2.2                          # shown brightness to the linear value Blender wants
        el.position, el.color = at, (v, v, v, 1)
    emit = nodes.new("ShaderNodeEmission")
    links.new(diffuse.outputs["BSDF"], to_rgb.inputs["Shader"])
    links.new(to_rgb.outputs["Color"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], emit.inputs["Color"])
    texture = next((n for n in original.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None) \
        if original and original.use_nodes and original.node_tree else None
    if texture is None:
        links.new(emit.outputs["Emission"], output.inputs["Surface"])
        return mat
    cut = nodes.new("ShaderNodeTexImage")
    cut.image = texture.image
    mix = nodes.new("ShaderNodeMixShader")
    links.new(cut.outputs["Alpha"], mix.inputs["Fac"])
    links.new(nodes.new("ShaderNodeBsdfTransparent").outputs["BSDF"], mix.inputs[1])
    links.new(emit.outputs["Emission"], mix.inputs[2])
    links.new(mix.outputs["Shader"], output.inputs["Surface"])
    return mat


def objects_in(collection):
    yield from collection.objects
    for child in collection.children:
        yield from objects_in(child)


def lay_out(root, rig, cfg, big):
    """Turn and move the root so the body lies head up the frame, centred on lying_center."""
    layer = bpy.context.view_layer
    head_bone, feet_bone = cfg.get("head", "J_Bip_C_Head"), cfg.get("feet", "J_Bip_L_Foot")

    def ends():
        layer.update()
        m = rig.matrix_world
        return m @ rig.pose.bones[head_bone].head, m @ rig.pose.bones[feet_bone].head
    head, feet = ends()
    along = head - feet
    root.rotation_euler.z += math.pi / 2 - math.atan2(along.y, along.x)
    head, feet = ends()
    w, h = cfg["frame"]
    px, py = cfg["pivot"]
    cx, cy = cfg.get("lying_center", [w / 2, 32])
    ppm = float(cfg["pixels_per_meter"])
    middle = (head + feet) / 2
    root.location.x += (cx - px) / ppm - middle.x
    root.location.y += (py - cy) / ppm - middle.y


def main():
    path = config_path()
    with open(path) as f:
        cfg = json.load(f)
    name = os.path.splitext(os.path.basename(path))[0]
    out = os.path.join(REPO, cfg.get("renders", os.path.join(".cache", "renders", name)))
    os.makedirs(out, exist_ok=True)

    w, h = cfg["frame"]
    px, py = cfg["pivot"]
    ss = int(cfg.get("supersample", 8))
    big = max(w, h)
    scene = bpy.context.scene
    view_layer = bpy.context.view_layer

    r = scene.render
    r.resolution_x, r.resolution_y, r.resolution_percentage = w * ss, h * ss, 100
    r.film_transparent = True
    r.filter_size = float(cfg.get("filter_size", 0.5))       # a little anti-aliasing: the shrink step averages anyway
    r.image_settings.file_format = "PNG"
    r.image_settings.color_mode = "RGBA"
    r.image_settings.color_depth = "8"
    if hasattr(scene, "eevee"):                               # the shrink step averages 8x8 blocks: few samples do
        scene.eevee.taa_render_samples = int(cfg.get("samples", 8))
    scene.view_settings.view_transform = "Standard"          # colours as painted, not Filmic/AgX's tone curve
    scene.view_settings.look = "None"

    # An orthographic camera aimed at the root's origin (the feet), shifted so the origin lands on
    # the pivot pixel. Blender's shift is in units of the frame's larger side.
    cam_data = bpy.data.cameras.new("SpriteCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = big / float(cfg["pixels_per_meter"])
    cam_data.shift_x = (w / 2 - px) / big
    cam_data.shift_y = (py - h / 2) / big
    cam_data.clip_start, cam_data.clip_end = 0.01, 200.0
    cam = bpy.data.objects.new("SpriteCam", cam_data)
    scene.collection.objects.link(cam)
    old_camera = scene.camera
    scene.camera = cam

    def aim(elevation):
        e = math.radians(elevation)
        dist = 30.0
        cam.location = (0.0, -dist * math.cos(e), dist * math.sin(e))
        cam.rotation_euler = (math.radians(90.0 - elevation), 0.0, 0.0)

    root = bpy.data.objects[cfg["root"]]
    rig = bpy.data.objects[cfg.get("rig", cfg["root"])]
    old_mode = root.rotation_mode
    root.rotation_mode = "XYZ"                    # VRM rigs come in as quaternions; Blender converts
    old_rotation = root.rotation_euler.copy()
    old_location = root.location.copy()
    old_action = rig.animation_data.action if rig.animation_data else None

    layers = cfg["layers"]
    collections = []
    for layer in layers:
        if not layer.get("collection"):
            collections.append(None)          # a sheet with no geometry of its own (the base: outlines)
            continue
        lc = find_layer_collection(view_layer.layer_collection, layer["collection"])
        if lc is None:
            raise SystemExit(f"no collection named {layer['collection']!r} in this .blend")
        collections.append(lc)
    old_flags = [(lc.exclude, lc.holdout) for lc in collections if lc]

    # The sprite light: fixed to the camera (the figure turns, the light doesn't, as in the game).
    sun = bpy.data.objects.new("SpriteSun", bpy.data.lights.new("SpriteSun", "SUN"))
    sun.data.energy = math.pi                     # a white diffuse surface facing it comes out at 1
    sun.data.use_shadow = False                   # the toon steps shade; cast shadows would blot them
    scene.collection.objects.link(sun)
    sun.parent = cam
    sun.rotation_mode = "QUATERNION"
    sun.rotation_quaternion = Vector((0, 0, -1)).rotation_difference(Vector(cfg.get("light", [0.6, -0.55, -0.45])).normalized())
    lights_off = [o for o in scene.objects if o.type == "LIGHT" and o is not sun and not o.hide_render]
    for o in lights_off:
        o.hide_render = True

    swapped = []                                  # (slot, its material) for toon layers, put back at the end
    steps = cfg.get("toon", TOON)
    for layer, lc in zip(layers, collections):
        if lc is None or layer.get("shade") != "toon":
            continue
        made = {}
        for obj in set(objects_in(lc.collection)):
            for slot in obj.material_slots:
                key = slot.material.name if slot.material else ""
                if key not in made:
                    made[key] = toon_material(slot.material, steps)
                swapped.append((slot, slot.material))
                slot.material = made[key]

    frames, tags = [], {}
    views = cfg["views"]
    try:
        for tag in cfg["tags"]:
            action = bpy.data.actions.get(tag["action"]) if tag.get("action") else None
            if tag.get("action") and action is None:
                # A first test may have no animations yet: render the pose the rig is in. (Blender
                # drops actions nothing uses when saving: give each one a fake user, the shield icon.)
                print(f"warning: no action named {tag['action']!r} (have: {', '.join(a.name for a in bpy.data.actions) or 'none'}); "
                      f"{tag['name']} uses the current pose")
            set_action(rig, action)
            tag_views = views if tag.get("views", True) else {"down": views.get("down", 0)}
            for view, angle in tag_views.items():
                tag_name = tag["name"].format(view=view)
                first = len(frames)
                root.rotation_euler = old_rotation.copy()
                root.rotation_euler.z = old_rotation.z + math.radians(angle)
                aim(90.0 if tag.get("camera") == "top" else float(tag.get("elevation", cfg.get("elevation", 25))))
                root.location = old_location.copy()
                for k, frame in enumerate(tag["frames"]):
                    scene.frame_set(int(frame))
                    pose_bones(rig, cfg, action is not None)
                    if k == 0 and tag.get("camera") == "top":
                        lay_out(root, rig, cfg, big)       # once per tag, so its frames don't jitter
                    index = len(frames)
                    for i, lc in enumerate(collections):
                        if lc is None:
                            continue
                        # Cut only by the layers drawn before it (where they're in front: the body
                        # before the hair behind it); later layers paint over it anyway, so it stays
                        # whole under them (the body under a shirt, for when the shirt comes off).
                        for j, other in enumerate(collections):
                            if other is not None and other is not lc:
                                other.exclude = j > i
                                other.holdout = j < i
                        lc.exclude, lc.holdout = False, False
                        r.filepath = os.path.join(out, f"{index:04d}_{i:02d}.png")
                        bpy.ops.render.render(write_still=True)
                    frames.append({"tag": tag_name, "ms": int(tag.get("ms", 150))})
                tags[tag_name] = {"from": first, "to": len(frames) - 1, "loop": bool(tag.get("loop", True)), "event": int(tag.get("event", -1))}
                print(f"rendered {tag_name}: {len(tag['frames'])} frames")
    finally:
        for lc, (exclude, holdout) in zip([lc for lc in collections if lc], old_flags):
            lc.exclude, lc.holdout = exclude, holdout
        root.rotation_euler = old_rotation
        root.location = old_location
        root.rotation_mode = old_mode
        set_action(rig, old_action)
        for slot, mat in swapped:
            slot.material = mat
        for o in lights_off:
            o.hide_render = False
        bpy.data.objects.remove(sun)
        scene.camera = old_camera
        bpy.data.objects.remove(cam)

    with open(os.path.join(out, "renders.json"), "w") as f:
        json.dump({"config": os.path.relpath(path, REPO), "frame": [w, h], "pivot": [px, py], "supersample": ss,
                   "layers": [layer["sheet"] for layer in layers], "frames": frames, "tags": tags}, f, indent=1)
    print(f"wrote {len(frames)} frames x {sum(1 for c in collections if c)} layers to {out}")


main()
