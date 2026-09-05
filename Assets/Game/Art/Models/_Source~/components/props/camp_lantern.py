"""A camp lantern: the first placeable.

    blender --background --python components/props/camp_lantern.py -- --out components/props/camp_lantern.blend

Set it down and it lights the ground around it; look at it and press Q and it
goes back in your pack. Small, because you carry it -- 0.24 m across the base and
0.38 m to the top of the handle.

Deliberately NOT built on `supply_crate.blend`, which would have been the obvious
reuse: that file cannot be opened by Blender 4.2, the only Blender installed
here, exactly like `palette.blend`. Re-running its generator would have
overwritten the author's work, so this is a new component instead.

Materials are created locally under the palette's own names for the same reason
-- see saddle.py, which carries the full note.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

import bpy
import _buildlib as B

# name -> (hex, roughness, metallic), values from PALETTE.md
MATERIALS = [
    ("Mat_Metal_Brass_Tarnished", "9C7B3F", 0.45, 1.0),
    ("Mat_Metal_Steel_Worn", "7A7D80", 0.55, 1.0),
    ("Mat_Glass_Amber_Warm", "FFC46B", 0.10, 0.0),
]
BRASS, STEEL, GLASS = range(3)


def _srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def materials():
    out = []
    for name, hexcode, rough, metal in MATERIALS:
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name)
            mat.use_nodes = True
            bsdf = mat.node_tree.nodes["Principled BSDF"]
            rgb = [int(hexcode[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
            lin = [_srgb_to_linear(c) for c in rgb]
            bsdf.inputs["Base Color"].default_value = (lin[0], lin[1], lin[2], 1.0)
            bsdf.inputs["Roughness"].default_value = rough
            bsdf.inputs["Metallic"].default_value = metal
        out.append(mat)
    return out


def build(coll, mats):
    """Origin at the BASE, because that is where it meets the ground.

    A placeable is spawned at a raycast hit on the floor, so an origin anywhere
    else buries it or floats it by half its height.
    """
    body = B.Part(mats)
    body.cyl((0.0, 0.0, 0.012), 0.115, 0.024, axis='Z', seg=16, mat=STEEL)   # foot
    body.cyl((0.0, 0.0, 0.036), 0.085, 0.028, axis='Z', seg=16, mat=BRASS)   # oil reservoir
    body.cyl((0.0, 0.0, 0.215), 0.080, 0.022, axis='Z', seg=16, mat=BRASS)   # top cap
    body.cyl((0.0, 0.0, 0.245), 0.030, 0.040, axis='Z', seg=12, mat=BRASS)   # chimney
    # Four uprights caging the glass, so it reads as a lantern in silhouette.
    for i in range(4):
        import math
        a = math.radians(45 + 90 * i)
        body.box((math.cos(a) * 0.072, math.sin(a) * 0.072, 0.128),
                 (0.014, 0.014, 0.164), mat=BRASS)
    body.bevel(width=0.004, segments=1)
    body.finish("Mesh_Lantern_Body", coll)

    glass = B.Part(mats)
    glass.cyl((0.0, 0.0, 0.128), 0.068, 0.158, axis='Z', seg=16, mat=GLASS)
    glass.finish("Mesh_Lantern_Glass", coll)

    handle = B.Part(mats)
    handle.torus((0.0, 0.0, 0.300), 0.070, 0.008, axis='Y', maj_seg=18, min_seg=6, mat=STEEL)
    handle.finish("Mesh_Lantern_Handle", coll)

    # Where the light lives, read by the builder rather than guessed at.
    e = bpy.data.objects.new("LIGHT_Flame", None)
    e.empty_display_size = 0.05
    e.location = (0.0, 0.0, 0.135)
    coll.objects.link(e)


def main():
    out = B.parse_out()
    B.start(out)
    mats = materials()
    build(B.collection("Coll_CampLantern"), mats)
    B.save(out)
    B.report()


main()
