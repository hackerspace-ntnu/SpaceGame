"""Manage the project's shared material palette.

The palette starts empty and grows only when a model genuinely needs a color
that does not yet exist. That discipline is the whole point: a palette that
accepts every request becomes three hundred near-identical greys, at which
point it is no longer a palette.

Run inside Blender:

    # once, at library creation
    blender --background --python palette.py -- init

    # every time a genuinely new material is needed
    blender --background --python palette.py -- add \
        --category Metal --name Steel_Worn --hex 7A7D80 \
        --roughness 0.55 --metallic 1.0 \
        --note "Hull plating, beams, used equipment"

    # inspect before adding — always do this first
    blender --background --python palette.py -- list
    blender --background --python palette.py -- check --hex 7A7D80 --metallic 1.0

`add` refuses when an existing material is perceptually close to the one
requested, and tells you which one to use instead. Override with --force only
when the difference is deliberate and meaningful at viewing distance.

Material metadata (hex, category, intended use) is stored as custom properties
on the material datablock, so PALETTE.md can always be regenerated from the
.blend rather than drifting away from it.
"""

import argparse
import os
import sys

import bpy


# Perceptual distance thresholds, CIE76 deltaE over Lab.
# Below REFUSE, two colors are the same color for practical purposes.
# Between REFUSE and WARN they are close enough to be worth a second thought.
DELTA_E_REFUSE = 5.0
DELTA_E_WARN = 12.0


# --------------------------------------------------------------------------
# Color math
# --------------------------------------------------------------------------

def hex_to_srgb(hex_str):
    hex_str = hex_str.strip().lstrip("#")
    if len(hex_str) != 6:
        raise ValueError(f"Expected 6-digit hex, got {hex_str!r}")
    return [int(hex_str[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def srgb_to_linear(channels):
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
            for c in channels]


def linear_to_srgb(channels):
    out = []
    for c in channels:
        c = max(0.0, min(1.0, c))
        out.append(c * 12.92 if c <= 0.0031308
                   else 1.055 * (c ** (1 / 2.4)) - 0.055)
    return out


def srgb_to_hex(channels):
    return "".join(f"{int(round(c * 255)):02X}" for c in channels)


def linear_to_lab(rgb):
    """Linear RGB -> CIELAB (D65). Used for perceptual distance only."""
    r, g, b = rgb
    x = r * 0.4124 + g * 0.3576 + b * 0.1805
    y = r * 0.2126 + g * 0.7152 + b * 0.0722
    z = r * 0.0193 + g * 0.1192 + b * 0.9505

    # D65 white point
    x, y, z = x / 0.95047, y / 1.00000, z / 1.08883

    def f(t):
        return t ** (1 / 3) if t > 0.008856 else (7.787 * t) + (16 / 116)

    fx, fy, fz = f(x), f(y), f(z)
    return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)]


def delta_e(lab_a, lab_b):
    return sum((a - b) ** 2 for a, b in zip(lab_a, lab_b)) ** 0.5


# --------------------------------------------------------------------------
# Palette access
# --------------------------------------------------------------------------

def read_entries():
    """Every palette material, with its stored metadata."""
    entries = []
    for mat in bpy.data.materials:
        if not mat.name.startswith("Mat_"):
            continue
        hex_str = mat.get("hex")
        if not hex_str:
            continue
        entries.append({
            "material": mat,
            "name": mat.name,
            "category": mat.get("category", "Uncategorised"),
            "hex": hex_str,
            "roughness": round(float(mat.get("roughness", 0.5)), 3),
            "metallic": round(float(mat.get("metallic", 0.0)), 3),
            "note": mat.get("note", ""),
            "lab": linear_to_lab(srgb_to_linear(hex_to_srgb(hex_str))),
        })
    return sorted(entries, key=lambda e: (e["category"], e["name"]))


def find_near_matches(hex_str, metallic, entries):
    """Existing materials perceptually close to the requested color.

    Metallic is compared too: the same hex as metal and as plastic are
    genuinely different materials, not duplicates.
    """
    target_lab = linear_to_lab(srgb_to_linear(hex_to_srgb(hex_str)))
    matches = []
    for entry in entries:
        if abs(entry["metallic"] - float(metallic)) > 0.4:
            continue
        distance = delta_e(target_lab, entry["lab"])
        if distance <= DELTA_E_WARN:
            matches.append((distance, entry))
    return sorted(matches, key=lambda m: m[0])


def build_material(category, name, hex_str, roughness, metallic, note,
                   transmission=None, ior=None, emission=None):
    full_name = f"Mat_{category}_{name}"
    if full_name in bpy.data.materials:
        raise SystemExit(f"Material {full_name} already exists in the palette.")

    mat = bpy.data.materials.new(full_name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")

    rgb = srgb_to_linear(hex_to_srgb(hex_str))
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = float(roughness)
    bsdf.inputs["Metallic"].default_value = float(metallic)

    if transmission is not None:
        # Socket renamed between Blender 3.x and 4.x
        for key in ("Transmission Weight", "Transmission"):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = float(transmission)
                break
        mat.blend_method = 'BLEND'

    if ior is not None and "IOR" in bsdf.inputs:
        bsdf.inputs["IOR"].default_value = float(ior)

    if emission is not None:
        for key in ("Emission Color", "Emission"):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = (*rgb, 1.0)
                break
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = float(emission)

    # Metadata lives on the datablock so the docs can never drift from the file
    mat["category"] = category
    mat["hex"] = hex_str.strip().lstrip("#").upper()
    mat["roughness"] = float(roughness)
    mat["metallic"] = float(metallic)
    mat["note"] = note
    if transmission is not None:
        mat["transmission"] = float(transmission)
    if emission is not None:
        mat["emission_strength"] = float(emission)

    mat.use_fake_user = True  # survives saving without being assigned anywhere
    return mat


# --------------------------------------------------------------------------
# Documentation
# --------------------------------------------------------------------------

def write_doc(path, entries):
    lines = [
        "# Material Palette",
        "",
        "Generated from `palette.blend` by `scripts/palette.py`. Do not edit by hand —",
        "edit the palette and regenerate, or the two will disagree.",
        "",
        "Every model and component in this repository links its materials from here.",
        "Before adding anything, search this table for something that would serve.",
        "",
    ]

    if not entries:
        lines += [
            "The palette is currently **empty**. It fills up as models need colors.",
            "",
            "Add one with:",
            "",
            "```bash",
            "blender --background --python scripts/palette.py -- add \\",
            "    --category Metal --name Steel_Worn --hex 7A7D80 \\",
            "    --roughness 0.55 --metallic 1.0 \\",
            '    --note "Hull plating, beams, used equipment"',
            "```",
            "",
        ]
    else:
        categories = dict.fromkeys(e["category"] for e in entries)
        lines += [
            f"**{len(entries)} material(s)** across {len(categories)} categor(ies).",
            "",
        ]
        for category in categories:
            lines += [
                f"## {category}",
                "",
                "| Name | Hex | Roughness | Metallic | Intended for |",
                "|---|---|---|---|---|",
            ]
            for entry in entries:
                if entry["category"] != category:
                    continue
                lines.append(
                    f"| `{entry['name']}` | `#{entry['hex']}` | "
                    f"{entry['roughness']} | {entry['metallic']} | "
                    f"{entry['note'] or '—'} |"
                )
            lines.append("")

    directory = os.path.dirname(os.path.abspath(path))
    if directory:
        os.makedirs(directory, exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))


# --------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------

def load_palette(path, required=True):
    if os.path.exists(path):
        bpy.ops.wm.open_mainfile(filepath=path)
        return True
    if required:
        raise SystemExit(
            f"No palette at {path}. Create it first:\n"
            "  blender --background --python scripts/palette.py -- init"
        )
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return False


def cmd_init(args):
    path = os.path.abspath(args.palette)
    if os.path.exists(path):
        raise SystemExit(
            f"Palette already exists at {path}. Nothing to do — use `add`."
        )
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path)
    write_doc(args.doc, [])
    print(f"Created empty palette -> {path}")
    print(f"Created documentation -> {os.path.abspath(args.doc)}")


def cmd_list(args):
    load_palette(os.path.abspath(args.palette))
    entries = read_entries()
    if not entries:
        print("Palette is empty.")
        return
    print(f"{len(entries)} material(s):\n")
    for entry in entries:
        print(f"  {entry['name']:<40} #{entry['hex']}  "
              f"rough={entry['roughness']:<5} metal={entry['metallic']:<5} "
              f"{entry['note']}")


def cmd_check(args):
    load_palette(os.path.abspath(args.palette))
    matches = find_near_matches(args.hex, args.metallic, read_entries())
    if not matches:
        print(f"No existing material is close to #{args.hex.upper()}. "
              "Adding it is justified.")
        return
    print(f"Existing materials close to #{args.hex.upper()}:\n")
    for distance, entry in matches:
        verdict = "same color" if distance <= DELTA_E_REFUSE else "close"
        print(f"  {entry['name']:<40} #{entry['hex']}  "
              f"deltaE={distance:5.1f}  ({verdict})")
        if entry["note"]:
            print(f"    {entry['note']}")


def cmd_add(args):
    path = os.path.abspath(args.palette)
    load_palette(path)

    entries = read_entries()
    matches = find_near_matches(args.hex, args.metallic, entries)
    blocking = [m for m in matches if m[0] <= DELTA_E_REFUSE]

    if blocking and not args.force:
        lines = [
            f"Refusing to add #{args.hex.upper()} — the palette already has "
            "this color:",
            "",
        ]
        for distance, entry in blocking:
            lines.append(f"  {entry['name']}  #{entry['hex']}  deltaE={distance:.1f}")
            if entry["note"]:
                lines.append(f"    intended for: {entry['note']}")
        lines += [
            "",
            "Use the existing material. If the difference is deliberate and reads",
            "at viewing distance, re-run with --force and say why in --note.",
        ]
        raise SystemExit("\n".join(lines))

    for distance, entry in matches:
        if distance > DELTA_E_REFUSE:
            print(f"Note: {entry['name']} (#{entry['hex']}) is nearby "
                  f"(deltaE={distance:.1f}). Confirm you need both.")

    mat = build_material(
        args.category, args.name, args.hex, args.roughness, args.metallic,
        args.note, args.transmission, args.ior, args.emission,
    )
    bpy.ops.wm.save_as_mainfile(filepath=path)
    write_doc(args.doc, read_entries())
    print(f"Added {mat.name} (#{args.hex.upper()}) -> {path}")
    print(f"Updated documentation -> {os.path.abspath(args.doc)}")


def cmd_doc(args):
    load_palette(os.path.abspath(args.palette))
    entries = read_entries()
    write_doc(args.doc, entries)
    print(f"Wrote {len(entries)} material(s) -> {os.path.abspath(args.doc)}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    parser = argparse.ArgumentParser(prog="palette.py")
    parser.add_argument("--palette", default="Assets/Models/_Source~/palette.blend")
    parser.add_argument("--doc", default="Assets/Models/_Source~/PALETTE.md")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("init", help="Create an empty palette")
    sub.add_parser("list", help="List every material in the palette")
    sub.add_parser("doc", help="Regenerate PALETTE.md from palette.blend")

    p_check = sub.add_parser("check", help="Find existing materials near a color")
    p_check.add_argument("--hex", required=True)
    p_check.add_argument("--metallic", type=float, default=0.0)

    p_add = sub.add_parser("add", help="Add a material to the palette")
    p_add.add_argument("--category", required=True,
                       help="Metal, Wood, Stone, Fabric, Emissive, ...")
    p_add.add_argument("--name", required=True,
                       help="Descriptor and qualifier, e.g. Steel_Worn")
    p_add.add_argument("--hex", required=True)
    p_add.add_argument("--roughness", type=float, default=0.5)
    p_add.add_argument("--metallic", type=float, default=0.0)
    p_add.add_argument("--note", required=True,
                       help="What this material is intended for")
    p_add.add_argument("--transmission", type=float, default=None)
    p_add.add_argument("--ior", type=float, default=None)
    p_add.add_argument("--emission", type=float, default=None)
    p_add.add_argument("--force", action="store_true",
                       help="Add despite a near-duplicate already existing")

    args = parser.parse_args(argv)
    {
        "init": cmd_init, "list": cmd_list, "check": cmd_check,
        "add": cmd_add, "doc": cmd_doc,
    }[args.command](args)


if __name__ == "__main__":
    main()
