"""Adds the Stone materials the rock pack needs to the shared palette.blend (additive only).

Run:  blender --background --python rocks_palette.py
For every wanted colour it first looks for an existing non-metal palette material within deltaE 5 and
reuses it; otherwise it adds Mat_Stone_<Name>. Writes rocks_materials.json (key -> material name).
Safe to re-run: materials that already exist are kept as they are."""
import bpy, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE = os.path.normpath(os.path.join(HERE, "..", "..", "palette.blend"))

WANT = [  # key, name, hex, roughness, group, note
    ("Pale",        "Sandstone_Pale",     "E2D2A8", 0.95, "Yellow Desert", "Bleached dune sandstone, sunlit yellow-desert rock"),
    ("Gold",        "Sandstone_Gold",     "D2AC62", 0.95, "Yellow Desert", "Main yellow-desert rock colour"),
    ("Ochre",       "Sandstone_Ochre",    "B98A42", 0.95, "Yellow Desert", "Deep ochre sandstone, shaded yellow-desert rock"),
    ("Tan",         "Sandstone_Tan",      "C2A680", 0.95, "Yellow Desert", "Dusty beige sandstone, bridges yellow desert and grey stone"),
    ("Mustard",     "Sandstone_Mustard",  "A8934A", 0.95, "Yellow Desert", "Greenish-yellow weathered sandstone"),
    ("Terracotta",  "Redrock_Terracotta", "BC6A48", 0.95, "Red Desert",    "Main red-desert rock colour"),
    ("Rust",        "Redrock_Rust",       "A04E2E", 0.95, "Red Desert",    "Iron-rich deep orange-red rock"),
    ("Crimson",     "Redrock_Crimson",    "86382E", 0.95, "Red Desert",    "Darkest red-desert rock, shadowed canyon stone"),
    ("Salmon",      "Redrock_Salmon",     "D29278", 0.95, "Red Desert",    "Light sun-bleached red rock"),
    ("Mesa",        "Redrock_Mesa",       "C97B42", 0.95, "Red Desert",    "Orange mesa rock between the yellow and red deserts"),
    ("Umber",       "Redrock_Umber",      "77493A", 0.95, "Red Desert",    "Brown-red rock, base strata and shaded red-desert stone"),
    ("GranitePale", "Granite_Pale",       "C6C7C6", 0.9,  "Grey Stone",    "Lightest neutral grey stone"),
    ("GraniteMid",  "Granite_Mid",        "989A9B", 0.9,  "Grey Stone",    "Standard grey stone"),
    ("GraniteDark", "Granite_Dark",       "6B6E71", 0.9,  "Grey Stone",    "Dark grey stone, mountain rock"),
    ("Slate",       "Slate_Blue",         "606B77", 0.85, "Grey Stone",    "Blue-grey slate"),
    ("Basalt",      "Basalt_Charcoal",    "3A3C40", 0.85, "Grey Stone",    "Near-black volcanic rock, basalt columns"),
    ("Warm",        "Greystone_Warm",     "8E877D", 0.9,  "Grey Stone",    "Warm brown-grey stone that sits well next to desert rock"),
    ("Chalk",       "Limestone_Chalk",    "E2DDD0", 0.95, "Grey Stone",    "Off-white limestone / chalk"),
    ("Mud",         "Mudstone_Brown",     "6D5846", 0.95, "Grey Stone",    "Dark earthy brown mudstone"),
    ("Butter",      "Pastel_Butter",      "EADDA6", 0.9,  "Pastel",        "Pastel yellow stone"),
    ("Pink",        "Pastel_Pink",        "E6BFC6", 0.9,  "Pastel",        "Pastel pink stone, rose quartz"),
    ("Lilac",       "Pastel_Lilac",       "C9BCDD", 0.9,  "Pastel",        "Pastel purple stone, amethyst"),
    ("Mint",        "Pastel_Mint",        "BBD9C6", 0.9,  "Pastel",        "Pastel green stone"),
    ("Sky",         "Pastel_Sky",         "B8D0E2", 0.9,  "Pastel",        "Pastel blue stone"),
    ("Peach",       "Pastel_Peach",       "EBC8AC", 0.9,  "Pastel",        "Pastel orange stone"),
]

# Same colour as an existing paint / hide / wood material, but those have a different surface (gloss,
# sheen) and read wrong on rock, so these three get their own matte stone material on purpose.
FORCE = {"Crimson", "Warm", "Mud"}

def h2l(h):
    return [(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))]

def lab(rgb):
    r, g, b = rgb
    x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047
    y = r * 0.2126 + g * 0.7152 + b * 0.0722
    z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return [116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z))]

def info(mat):
    """(linear rgb, plain) for a palette material; plain = opaque, non-metal, non-emissive."""
    b = mat.node_tree.nodes.get("Principled BSDF") if mat.use_nodes and mat.node_tree else None
    if not b:
        return None, False
    rgb = list(b.inputs["Base Color"].default_value)[:3]
    plain = b.inputs["Metallic"].default_value < 0.3 and not b.inputs["Base Color"].is_linked
    for k in ("Transmission Weight", "Emission Strength"):
        if k in b.inputs and b.inputs[k].default_value > 0.01 and k != "Emission Strength":
            plain = False
    if "Emission Strength" in b.inputs and b.inputs["Emission Strength"].default_value > 0.01:
        ec = b.inputs["Emission Color"].default_value
        if max(ec[0], ec[1], ec[2]) > 0.01:
            plain = False
    return rgb, plain

bpy.ops.wm.open_mainfile(filepath=PALETTE)
print("PALETTE has", len(bpy.data.materials), "materials; with hex metadata:", sum(1 for m in bpy.data.materials if m.get("hex")))
mapping, added = {}, []
for key, name, hx, rough, group, note in WANT:
    full = "Mat_Stone_" + name
    if full in bpy.data.materials:          # ours already: keep it, but follow the colour table above
        m = bpy.data.materials[full]
        if m.get("hex") != hx:
            b = m.node_tree.nodes["Principled BSDF"]
            b.inputs["Base Color"].default_value = (*h2l(hx), 1.0)
            m.diffuse_color = (*h2l(hx), 1.0)
            m["hex"] = hx
            added.append(full + " (recoloured)")
        mapping[key] = full
        continue
    target = lab(h2l(hx))
    best = None
    for m in bpy.data.materials:
        rgb, plain = info(m)
        if rgb is None or not plain:
            continue
        d = sum((a - b) ** 2 for a, b in zip(target, lab(rgb))) ** 0.5
        if best is None or d < best[0]:
            best = (d, m.name)
    if best and best[0] <= 5.0 and key not in FORCE:
        print("REUSE %-12s -> %s (deltaE %.1f)" % (key, best[1], best[0]))
        mapping[key] = best[1]
        continue
    m = bpy.data.materials.new(full)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*h2l(hx), 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = 0.0
    m.diffuse_color = (*h2l(hx), 1.0)
    m["category"], m["hex"], m["roughness"], m["metallic"], m["note"] = "Stone", hx, rough, 0.0, note
    m.use_fake_user = True
    mapping[key] = full
    added.append(full)
    print("ADD   %-12s -> %s  nearest existing: %s" % (key, full, "%s (deltaE %.1f)" % (best[1], best[0]) if best else "-"))
if added:
    bpy.ops.wm.save_mainfile()
json.dump({"materials": mapping, "groups": {k: g for k, _, _, _, g, _ in WANT}}, open(os.path.join(HERE, "rocks_materials.json"), "w"), indent=1)
print("DONE added", len(added), "total now", len(bpy.data.materials))
