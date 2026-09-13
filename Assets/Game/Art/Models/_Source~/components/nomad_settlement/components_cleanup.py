"""Organisational cleanup of nomad_settlement/components.blend.

Renames objects, meshes, collections and materials, and deduplicates identical
material datablocks. Touches NO geometry: no vertex, transform, parent,
modifier or material *assignment* is changed. Run against a copy; the
original is preserved alongside.
"""

import bpy
import sys

# ---------------------------------------------------------------- materials
# Canonical name per colour family. Local to this file, hence the Nomad infix,
# so appending it next to the shared palette cannot collide.
MAT_NAMES = {
    (0.4792, 0.2058, 0.1046): "Mat_Nomad_Clay_Terracotta",
    (0.085, 0.085, 0.085):    "Mat_Nomad_Metal_Charcoal",
    (0.7358, 0.5336, 0.3048): "Mat_Nomad_Clay_Sand",
    (0.3054, 0.3054, 0.3054): "Mat_Nomad_Metal_Grey",
    (0.8, 0.7522, 0.2733):    "Mat_Nomad_Glass_Amber",
    (0.1224, 0.0592, 0.0357): "Mat_Nomad_Clay_Oxblood",
    (1.0, 0.8916, 0.6091):    "Mat_Nomad_Clay_Bone",
    (0.7359, 0.4515, 0.2561): "Mat_Nomad_Clay_Ochre",
}

# ------------------------------------------------------------- object names
# old object name -> (new object name, new collection)
RENAMES = {
    # --- tower shell -------------------------------------------------------
    "Cylinder.002":                 ("Mesh_TowerBody_Plinth",          "Coll_Tower_Body"),
    "Cylinder":                     ("Mesh_TowerBody_SegLower",        "Coll_Tower_Body"),
    "cylinder_clock_inwards":       ("Mesh_TowerBody_SegMid",          "Coll_Tower_Body"),
    "Cylinder.006":                 ("Mesh_TowerBody_SegUpper",        "Coll_Tower_Body"),
    "roof_simple_inwards_cylinder": ("Mesh_TowerBody_RoofCap",         "Coll_Tower_Body"),
    "roof_pipe_thich":              ("Mesh_TowerBody_StackWide",       "Coll_Tower_Body"),
    "roof_pipe_thin":               ("Mesh_TowerBody_StackNarrow",     "Coll_Tower_Body"),
    # --- tower trim --------------------------------------------------------
    "detail_ring":                  ("Mesh_TowerTrim_BandThin",        "Coll_Tower_Trim"),
    "detail_ring_1/3":              ("Mesh_TowerTrim_BandArcThird",    "Coll_Tower_Trim"),
    "detail_ring_high":             ("Mesh_TowerTrim_BandTall",        "Coll_Tower_Trim"),
    "detail_ring__large":           ("Mesh_TowerTrim_BandWide",        "Coll_Tower_Trim"),
    "detail_column":                ("Mesh_TowerTrim_Pilaster",        "Coll_Tower_Trim"),
    "Cube.005":                     ("Mesh_TowerTrim_PilasterMounted", "Coll_Tower_Trim"),
    "detail_column_inwards":        ("Mesh_TowerTrim_PilasterTapered", "Coll_Tower_Trim"),
    "Cube.004":                     ("Mesh_TowerTrim_FootPad",         "Coll_Tower_Trim"),
    "door_foundation":              ("Mesh_TowerTrim_DoorStep",        "Coll_Tower_Trim"),
    # --- door --------------------------------------------------------------
    "door_frame":                   ("Mesh_Door_Frame",                "Coll_Door_Single"),
    "Cube.016":                     ("Mesh_Door_Leaf",                 "Coll_Door_Single"),
    # --- windows -----------------------------------------------------------
    "window":                       ("Mesh_WindowRectLarge_Frame",     "Coll_Window_RectLarge"),
    "Cube.014":                     ("Mesh_WindowRectLarge_Glass",     "Coll_Window_RectLarge"),
    "window.001":                   ("Mesh_WindowRectSmall_Frame",     "Coll_Window_RectSmallHigh"),
    "Cube.015":                     ("Mesh_WindowRectSmall_Glass",     "Coll_Window_RectSmallHigh"),
    "Cylinder.012":                 ("Mesh_WindowRoundDeep_Frame",     "Coll_Window_RoundDeep"),
    "Cylinder.013":                 ("Mesh_WindowRoundDeep_Glass",     "Coll_Window_RoundDeep"),
    "Cylinder.011":                 ("Mesh_WindowRoundFlat_Frame",     "Coll_Window_RoundFlat"),
    "Cylinder.014":                 ("Mesh_WindowRoundFlat_Glass",     "Coll_Window_RoundFlat"),
    # --- roof deck A (was roof_outpost) ------------------------------------
    "roof_terrace":                 ("Mesh_RoofDeck_Ringwall",         "Coll_RoofDeck_Vented"),
    "Cube.009":                     ("Mesh_RoofVent_Housing",          "Coll_RoofDeck_Vented"),
    "Cylinder.001":                 ("Mesh_RoofVent_FanWell",          "Coll_RoofDeck_Vented"),
    "Cylinder.003":                 ("Mesh_RoofVent_PortA",            "Coll_RoofDeck_Vented"),
    "Cylinder.004":                 ("Mesh_RoofVent_PortB",            "Coll_RoofDeck_Vented"),
    "Torus":                        ("Mesh_RoofVent_Elbow",            "Coll_RoofDeck_Vented"),
    "BézierCurve":                  ("Curve_RoofVent_CowlA",           "Coll_RoofDeck_Vented"),
    "BézierCurve.001":              ("Curve_RoofVent_CowlB",           "Coll_RoofDeck_Vented"),
    "Cube.006":                     ("Mesh_RoofDeck_BlockMed",         "Coll_RoofDeck_Vented"),
    "Cube.007":                     ("Mesh_RoofDeck_BlockSmall",       "Coll_RoofDeck_Vented"),
    "Cube.008":                     ("Mesh_RoofDeck_BlockLarge",       "Coll_RoofDeck_Vented"),
    # --- roof deck B (duplicate that was loose in "Collection") ------------
    "roof_terrace.001":             ("Mesh_RoofDeck_Ringwall_B",       "Coll_RoofDeck_Vented_B"),
    "Cube.010":                     ("Mesh_RoofVent_Housing_B",        "Coll_RoofDeck_Vented_B"),
    "Cylinder.009":                 ("Mesh_RoofVent_FanWell_B",        "Coll_RoofDeck_Vented_B"),
    "Cylinder.007":                 ("Mesh_RoofVent_PortA_B",          "Coll_RoofDeck_Vented_B"),
    "Cylinder.008":                 ("Mesh_RoofVent_PortB_B",          "Coll_RoofDeck_Vented_B"),
    "Torus.001":                    ("Mesh_RoofVent_Elbow_B",          "Coll_RoofDeck_Vented_B"),
    "BézierCurve.002":              ("Curve_RoofVent_CowlA_B",         "Coll_RoofDeck_Vented_B"),
    "BézierCurve.003":              ("Curve_RoofVent_CowlB_B",         "Coll_RoofDeck_Vented_B"),
    "Cube.013":                     ("Mesh_RoofDeck_BlockMed_B",       "Coll_RoofDeck_Vented_B"),
    "Cube.012":                     ("Mesh_RoofDeck_BlockSmall_B",     "Coll_RoofDeck_Vented_B"),
    "Cube.011":                     ("Mesh_RoofDeck_BlockLarge_B",     "Coll_RoofDeck_Vented_B"),
    # --- crowned roof cap (was block_extended_top) -------------------------
    "Cylinder.005":                 ("Mesh_RoofCap_Brim",              "Coll_RoofCap_Crowned"),
    "Cylinder.010":                 ("Mesh_RoofCap_Dome",              "Coll_RoofCap_Crowned"),
    "detail_ring__large.001":       ("Mesh_RoofCap_BandLower",         "Coll_RoofCap_Crowned"),
    "detail_ring__large.002":       ("Mesh_RoofCap_BandUpper",         "Coll_RoofCap_Crowned"),
    "detail_ring__large.003":       ("Mesh_RoofCap_BandBrim",          "Coll_RoofCap_Crowned"),
    "detail_column.001":            ("Mesh_RoofCap_ColumnRing",        "Coll_RoofCap_Crowned"),
    # --- plain drum --------------------------------------------------------
    "extended_block_small_cylinder": ("Mesh_Drum_Plain",               "Coll_Drum_Plain"),
    # --- panel box (was electrical_box) ------------------------------------
    "Cube":                         ("Mesh_PanelBox_Body",             "Coll_Greeble_PanelBox"),
    "Cube.001":                     ("Mesh_PanelBox_PlateLarge",       "Coll_Greeble_PanelBox"),
    "Cube.003":                     ("Mesh_PanelBox_PlateWide",        "Coll_Greeble_PanelBox"),
    "Cube.002":                     ("Mesh_PanelBox_PlateSmall",       "Coll_Greeble_PanelBox"),
    # --- plated meter box (was inside block_extended_top) ------------------
    "Cube.025":                     ("Mesh_MeterBox_Backplate",        "Coll_Greeble_MeterBoxPlated"),
    "Cube.032":                     ("Mesh_MeterBox_Face",             "Coll_Greeble_MeterBoxPlated"),
    "Cube.028":                     ("Mesh_MeterBox_VentUpper",        "Coll_Greeble_MeterBoxPlated"),
    "Cube.029":                     ("Mesh_MeterBox_VentLower",        "Coll_Greeble_MeterBoxPlated"),
    "Cube.027":                     ("Mesh_MeterBox_Lip",              "Coll_Greeble_MeterBoxPlated"),
    "Cube.026":                     ("Mesh_MeterBox_Stud",             "Coll_Greeble_MeterBoxPlated"),
    "Cube.031":                     ("Mesh_MeterBox_DialPlateUpper",   "Coll_Greeble_MeterBoxPlated"),
    "Cube.030":                     ("Mesh_MeterBox_DialPlateLower",   "Coll_Greeble_MeterBoxPlated"),
    "Cylinder.018":                 ("Mesh_MeterBox_DialSmall",        "Coll_Greeble_MeterBoxPlated"),
    "Cylinder.017":                 ("Mesh_MeterBox_DialLarge",        "Coll_Greeble_MeterBoxPlated"),
    # --- loose meter box (was electricacl_box_dark_1) ----------------------
    "Cube.021":                     ("Mesh_MeterBoxLoose_VentUpper",      "Coll_Greeble_MeterBoxLoose"),
    "Cube.020":                     ("Mesh_MeterBoxLoose_VentLower",      "Coll_Greeble_MeterBoxLoose"),
    "Cube.022":                     ("Mesh_MeterBoxLoose_Lip",            "Coll_Greeble_MeterBoxLoose"),
    "Cube.023":                     ("Mesh_MeterBoxLoose_Stud",           "Coll_Greeble_MeterBoxLoose"),
    "Cube.018":                     ("Mesh_MeterBoxLoose_DialPlateUpper", "Coll_Greeble_MeterBoxLoose"),
    "Cube.019":                     ("Mesh_MeterBoxLoose_DialPlateLower", "Coll_Greeble_MeterBoxLoose"),
    "Cylinder.015":                 ("Mesh_MeterBoxLoose_DialSmall",      "Coll_Greeble_MeterBoxLoose"),
    "Cylinder.016":                 ("Mesh_MeterBoxLoose_DialLarge",      "Coll_Greeble_MeterBoxLoose"),
    # --- pipe wraps --------------------------------------------------------
    "BézierCurve.004":              ("Curve_PipeWrap_Run_Upper",       "Coll_PipeWrap_Upper"),
    "Cube.024":                     ("Mesh_PipeWrap_ClampA_Upper",     "Coll_PipeWrap_Upper"),
    "Cube.017":                     ("Mesh_PipeWrap_CollarA_Upper",    "Coll_PipeWrap_Upper"),
    "Cube.033":                     ("Mesh_PipeWrap_ClampB_Upper",     "Coll_PipeWrap_Upper"),
    "Cube.034":                     ("Mesh_PipeWrap_CollarB_Upper",    "Coll_PipeWrap_Upper"),
    "BézierCurve.005":              ("Curve_PipeWrap_Run_Lower",       "Coll_PipeWrap_Lower"),
    "Cube.037":                     ("Mesh_PipeWrap_ClampA_Lower",     "Coll_PipeWrap_Lower"),
    "Cube.038":                     ("Mesh_PipeWrap_CollarA_Lower",    "Coll_PipeWrap_Lower"),
    "Cube.036":                     ("Mesh_PipeWrap_ClampB_Lower",     "Coll_PipeWrap_Lower"),
    "Cube.035":                     ("Mesh_PipeWrap_CollarB_Lower",    "Coll_PipeWrap_Lower"),
    # --- antennas ----------------------------------------------------------
    "antenna_high":                 ("Mesh_AntennaMast_Tall",          "Coll_Antenna_Masts"),
    "antenna_thin":                 ("Mesh_AntennaMast_Thin",          "Coll_Antenna_Masts"),
    "antenna_bulky":                ("Mesh_AntennaMast_Bulky",         "Coll_Antenna_Masts"),
    "antenna_off_from_building":    ("Mesh_AntennaMast_Offset",        "Coll_Antenna_Masts"),
    "Sphere":                       ("Mesh_AntennaDish_Reflector",     "Coll_Antenna_Dish"),
    "Cube.043":                     ("Mesh_AntennaDish_Mast",          "Coll_Antenna_Dish"),
}

COLL_ORDER = [
    "Coll_Tower_Body", "Coll_Tower_Trim", "Coll_Door_Single",
    "Coll_Window_RectLarge", "Coll_Window_RectSmallHigh",
    "Coll_Window_RoundDeep", "Coll_Window_RoundFlat",
    "Coll_RoofCap_Crowned", "Coll_RoofDeck_Vented", "Coll_RoofDeck_Vented_B",
    "Coll_Drum_Plain", "Coll_PipeWrap_Upper", "Coll_PipeWrap_Lower",
    "Coll_Greeble_PanelBox", "Coll_Greeble_MeterBoxPlated",
    "Coll_Greeble_MeterBoxLoose",
    "Coll_Antenna_Masts", "Coll_Antenna_Dish",
]

log = []


def sig(m):
    """Full comparable signature of a material, so only true twins merge."""
    parts = [m.blend_method if hasattr(m, "blend_method") else "",
             m.use_backface_culling, m.diffuse_color[:], m.metallic, m.roughness]
    if m.use_nodes:
        for n in sorted(m.node_tree.nodes, key=lambda x: (x.bl_idname, x.name)):
            parts.append(n.bl_idname)
            for i in n.inputs:
                v = getattr(i, "default_value", None)
                if v is None:
                    parts.append(None)
                elif hasattr(v, "__len__"):
                    parts.append(tuple(round(float(x), 6) for x in v))
                else:
                    parts.append(round(float(v), 6))
        parts.append(len(m.node_tree.links))
    return repr(parts)


def base_color(m):
    if m.use_nodes:
        b = m.node_tree.nodes.get("Principled BSDF")
        if b:
            return tuple(round(float(x), 4) for x in b.inputs["Base Color"].default_value[:3])
    return None


# ---- 1. merge identical materials ----------------------------------------
groups = {}
for m in bpy.data.materials:
    groups.setdefault(sig(m), []).append(m)

used_names = {}
for s, mats in groups.items():
    bc = base_color(mats[0])
    stem = MAT_NAMES.get(bc)
    if stem is None:
        stem = "Mat_Nomad_Unsorted"
        log.append("WARN unmapped colour %s on %s" % (bc, mats[0].name))
    n = used_names.get(stem, 0)
    used_names[stem] = n + 1
    name = stem if n == 0 else "%s_v%d" % (stem, n + 1)
    keep = mats[0]
    for dup in mats[1:]:
        dup.user_remap(keep)
    keep.name = name
    log.append("MAT %-28s <- %d datablock(s)" % (name, len(mats)))

for m in list(bpy.data.materials):
    if m.users == 0 or (m.users == 1 and m.use_fake_user):
        log.append("MAT purged orphan %s" % m.name)
        bpy.data.materials.remove(m)

# ---- 2. rename objects and their single-user data ------------------------
missing = [k for k in RENAMES if k not in bpy.data.objects]
if missing:
    print("ABORT: objects not found: %s" % missing)
    sys.exit(1)
unlisted = [o.name for o in bpy.data.objects if o.name not in RENAMES]
if unlisted:
    print("ABORT: unlisted objects: %s" % unlisted)
    sys.exit(1)

for old, (new, _coll) in RENAMES.items():
    o = bpy.data.objects[old]
    o.name = new
    if o.data is not None and o.data.users == 1:
        o.data.name = new.replace("Mesh_", "Data_").replace("Curve_", "Data_")
    elif o.data is not None:
        log.append("NOTE shared data %s kept on %s" % (o.data.name, new))

# ---- 3. rebuild the collection layout ------------------------------------
scene_coll = bpy.context.scene.collection
new_colls = {}
for name in COLL_ORDER:
    c = bpy.data.collections.new(name)
    scene_coll.children.link(c)
    new_colls[name] = c

for old, (new, coll) in RENAMES.items():
    o = bpy.data.objects[new]
    for c in list(o.users_collection):
        c.objects.unlink(o)
    new_colls[coll].objects.link(o)

for c in list(bpy.data.collections):
    if c.name not in new_colls:
        log.append("COLL removed empty/legacy %s (%d objects left in it)"
                   % (c.name, len(c.objects)))
        bpy.data.collections.remove(c)

for o in bpy.data.objects:
    if not o.users_collection:
        print("ABORT: %s ended up in no collection" % o.name)
        sys.exit(1)

bpy.ops.wm.save_mainfile()
print("\n".join(log))
print("CLEANUP_OK objects=%d collections=%d materials=%d"
      % (len(bpy.data.objects), len(bpy.data.collections), len(bpy.data.materials)))
