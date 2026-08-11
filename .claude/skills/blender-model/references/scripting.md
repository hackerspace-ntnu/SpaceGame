# Scripting

## When to script

Script when creating a **new** `.blend` from nothing. Use MCP when editing a file that already exists.

The reason is not aesthetic. A script that runs against an existing file operates blind — it cannot see the hand edits the user made after generation, and a name collision or a stray `bpy.ops.object.delete()` silently destroys work that exists nowhere else. MCP lets you inspect the live scene before touching it.

## Headless invocation

```bash
blender --background --python build_crate.py -- --out Assets/Models/_Source~/components/props/crate_panel.blend
```

Everything after `--` is passed to the script rather than consumed by Blender. Parse it:

```python
import sys, argparse

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
parser = argparse.ArgumentParser()
parser.add_argument("--out", required=True)
args = parser.parse_args(argv)
```

Always start a new-file script from a clean scene, and only ever when writing to a path that does not yet exist:

```python
import os, bpy

if os.path.exists(args.out):
    raise SystemExit(
        f"Refusing to overwrite existing file: {args.out}\n"
        "The .blend is the source of truth. Edit it via MCP instead of regenerating."
    )

bpy.ops.wm.read_factory_settings(use_empty=True)
```

That guard is the most important line in any generation script. Keep it.

## Scene setup

```python
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
```

## Building geometry

Prefer `bmesh` or direct mesh data construction over `bpy.ops` where practical — operators depend on context, selection state, and the active object, which makes them fragile in background mode and hard to reason about.

```python
import bmesh

mesh = bpy.data.meshes.new("Mesh_CratePanel_Plain")
obj = bpy.data.objects.new("Mesh_CratePanel_Plain", mesh)

bm = bmesh.new()
bmesh.ops.create_cube(bm, size=1.0)
bmesh.ops.transform(bm, matrix=Matrix.Diagonal((0.6, 0.05, 0.6)).to_4x4(), verts=bm.verts)
bm.to_mesh(mesh)
bm.free()
```

When you do use operators, set context explicitly rather than relying on whatever happens to be selected:

```python
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
```

## Collections and naming

```python
coll = bpy.data.collections.new("Coll_CratePanel_Plain")
scene.collection.children.link(coll)
coll.objects.link(obj)
```

Name at creation time, never after. An object created unnamed and renamed later can leave `.001` suffixes on its mesh datablock even when the object name looks right.

Check both object and data names before finishing:

```python
for o in bpy.data.objects:
    assert not o.name[-4:-3] == '.', f"Auto-suffixed name: {o.name}"
    o.data.name = o.name  # keep datablock names in sync
```

## Origins

Set the origin by moving the mesh data relative to the object, not by moving the object:

```python
from mathutils import Matrix

offset = Matrix.Translation(-desired_origin_location)
mesh.transform(offset)
obj.location = desired_origin_location
```

Then apply transforms so the object reads clean.

## Applying transforms

```python
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
```

Leave location unapplied — the origin is meaningful.

## Materials

Link from the palette rather than creating new materials:

```python
palette_path = "Assets/Models/_Source~/palette.blend"
with bpy.data.libraries.load(palette_path, link=True) as (src, dst):
    dst.materials = [m for m in src.materials if m in ("Mat_Metal_Steel_Worn",)]

obj.data.materials.append(bpy.data.materials["Mat_Metal_Steel_Worn"])
```

If the material you need is not in the palette, stop and add it to the palette properly — do not define it locally. See `materials.md`.

## Armatures

```python
arm_data = bpy.data.armatures.new("Arm_DoorAssembly")
arm_obj = bpy.data.objects.new("Arm_DoorAssembly", arm_data)
coll.objects.link(arm_obj)

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')
bone = arm_data.edit_bones.new("Bone_LidPivot")
bone.head = (0, 0, 0)
bone.tail = (0, 0, 0.2)
bpy.ops.object.mode_set(mode='OBJECT')
```

For rigid mechanical parts, parent the object directly to a bone rather than using automatic weights — vertex-weight deformation on a rigid hinge produces smearing at the pivot.

## Saving

```python
os.makedirs(os.path.dirname(args.out), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.out))
```

## Defensive rules

These follow from the `.blend` being the source of truth:

- **Never** `read_factory_settings` in a script that opens an existing file.
- **Never** delete objects by name pattern or by iterating `bpy.data.objects` — you cannot know what the user added.
- **Never** clear a collection.
- Create with unique, specific names so an additive run cannot collide with existing content.
- If a script must touch an existing file, have it open the file, add only, and save — and get the user's agreement first, since MCP is the correct tool for that job.

## Verifying a build

After generating, inspect the result rather than assuming it worked:

```bash
blender --background Assets/Models/_Source~/components/props/crate_panel.blend \
  --python-expr "import bpy; [print(o.name, o.type, tuple(round(d,3) for d in o.dimensions)) for o in bpy.data.objects]"
```

Check names, dimensions against the intended real-world size, and material assignments.
