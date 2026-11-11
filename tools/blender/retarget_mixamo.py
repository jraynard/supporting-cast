"""Put Mixamo animations (FBX, "Without Skin") onto a character's rig as named actions. Runs inside
Blender, on the .blend from setup_vrm.py:

    blender -b art/3d/wanderer.blend --python tools/blender/retarget_mixamo.py -- art/3d/wanderer.json

The config's "animations" maps action names to FBX files (relative to the config):
{"Idle": "anims/Idle.fbx", "Walk": "anims/Walk.fbx", ...}. Each file is imported, and every frame
of it is baked onto the rig's bones of the same name ("J_Bip_*" when the model was uploaded to
Mixamo from VRoid; a "bone_map" {rig bone: file bone} covers other names). Bones are matched in
world space: each takes the turn its counterpart made from its own rest pose, so the two skeletons
may differ in bone rolls and axes, and motion Mixamo put on the armature object itself (lying down)
comes along. The hips also follow the file's movement, scaled by the two rigs' hip heights.
Actions get a fake user, so the .blend keeps them; the file is saved in place.
"""
import json
import os
import sys

import bpy
from mathutils import Matrix

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def ordered(bones):
    """Parents before children."""
    out = []

    def visit(b):
        out.append(b)
        for c in b.children:
            visit(c)
    for b in bones:
        if b.parent is None:
            visit(b)
    return out


def bake(rig, src, name, cfg):
    scene = bpy.context.scene
    act = src.animation_data.action if src.animation_data else None
    if act is None:
        raise SystemExit(f"{name}: the file has no animation")
    first, last = (int(round(v)) for v in act.frame_range)

    bone_map = cfg.get("bone_map", {})
    hips_name = cfg.get("hips", "J_Bip_C_Hips")
    pattern = cfg.get("retarget_bones", "J_Bip_")
    pairs = {}                                         # rig bone -> file bone
    for b in rig.data.bones:
        other = bone_map.get(b.name, b.name)
        if (b.name in bone_map or b.name.startswith(pattern)) and other in src.data.bones:
            pairs[b.name] = other
    if hips_name not in pairs:
        raise SystemExit(f"{name}: no {hips_name!r} in the file (bones like {', '.join(list(src.data.bones.keys())[:5])}...)")
    ratio = rig.data.bones[hips_name].head_local.z / max(1e-6, src.data.bones[pairs[hips_name]].head_local.z)

    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = action
    if hasattr(rig.animation_data, "action_slot") and getattr(action, "slots", None) is not None and len(action.slots):
        rig.animation_data.action_slot = action.slots[0]

    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    chain = ordered(rig.data.bones)
    for frame in range(first, last + 1):
        scene.frame_set(frame)
        posed = {}                                     # bone -> armature-space pose matrix this frame
        for b in chain:
            rest = b.matrix_local
            parent_posed = posed.get(b.parent.name) if b.parent else None
            # Where the bone sits with no basis of its own: its rest offset from the posed parent.
            natural = parent_posed @ (b.parent.matrix_local.inverted() @ rest) if b.parent else rest.copy()
            if b.name in pairs:
                sb = src.data.bones[pairs[b.name]]
                world = src.matrix_world @ src.pose.bones[sb.name].matrix
                turn = world.to_quaternion() @ sb.matrix_local.to_quaternion().inverted()
                rot = turn @ rest.to_quaternion()
                loc = natural.translation
                if b.name == hips_name:
                    loc = rest.translation + (world.translation - sb.head_local) * ratio
                target = Matrix.LocRotScale(loc, rot, None)
            else:
                target = natural
            posed[b.name] = target
            pb = rig.pose.bones[b.name]
            basis = (b.parent.matrix_local.inverted() @ rest).inverted() @ parent_posed.inverted() @ target if b.parent \
                else rest.inverted() @ target
            if b.name in pairs:
                loc, rot, _ = basis.decompose()
                pb.rotation_quaternion = rot
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=b.name)
                if b.name == hips_name:
                    pb.location = loc
                    pb.keyframe_insert("location", frame=frame, group=b.name)
    print(f"{name}: frames {first}-{last}, {len(pairs)} bones")
    return first, last


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        raise SystemExit("usage: blender -b <file.blend> --python tools/blender/retarget_mixamo.py -- <config.json> [Action ...]")
    path = os.path.abspath(argv[0])
    with open(path) as f:
        cfg = json.load(f)
    only = set(argv[1:])
    rig = bpy.data.objects[cfg.get("rig", cfg["root"])]
    ranges = {}
    for name, rel in cfg.get("animations", {}).items():
        if only and name not in only:
            continue
        before = set(bpy.data.objects)
        actions_before = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=os.path.join(os.path.dirname(path), rel))
        new = [o for o in bpy.data.objects if o not in before]
        src = next((o for o in new if o.type == "ARMATURE"), None)
        if src is None:
            raise SystemExit(f"{rel}: no armature in the file")
        try:
            ranges[name] = bake(rig, src, name, cfg)
        finally:
            for o in new:
                bpy.data.objects.remove(o, do_unlink=True)
            for a in set(bpy.data.actions) - actions_before:
                if a.name != name:
                    bpy.data.actions.remove(a)
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.location, pb.rotation_quaternion = (0, 0, 0), (1, 0, 0, 0)
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_mainfile()
    for name, (a, b) in ranges.items():
        print(f"action {name}: frames {a}-{b}")


main()
