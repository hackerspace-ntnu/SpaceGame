// Builds Assets/Prefabs/agents/vehicle/ShipRV.prefab from Assets/Models/Vehicles/RV/ship_model.fbx.
//
// The FBX arrives as ~20 flat, unnamed meshes ("Cube.007", "Cylinder.001", …) with no rig and no
// interior. This script turns that into a mountable vehicle: it renames the parts, yaws the model
// so the nose points down +Z (Unity's forward — the model is authored nose-along -X), hangs the two
// doors and the two engine pylons off hinge pivots, boxes in a walkable interior, and drops a
// steering wheel in the cockpit.
//
// Everything geometric is measured from the meshes at build time rather than hardcoded, so
// re-exporting the FBX with tweaked proportions and re-running this still lands in the right place.
// The one thing it cannot survive is the exporter renumbering the meshes — see PartNames below.
//
// Re-run from: Tools ▸ Vehicles ▸ Build ShipRV Prefab
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ShipRVBuilder
{
    private const string ModelPath = "Assets/Prefabs/agents/vehicle/ship_model 1.blend";
    private const string PrefabPath = "Assets/Prefabs/agents/vehicle/ShipRV.prefab";

    // Model is authored with the nose along -X; vehicles in this project drive along +Z.
    private static readonly Quaternion ModelYaw = Quaternion.Euler(0f, 90f, 0f);

    // Source mesh name → what it actually is. The artist names the moving parts and the two shell
    // variants directly, so those pass through untouched; only the structural meshes still carrying
    // Blender defaults get renamed, and the four openable wall panels get names that say which is
    // which. Identified by measuring bounds and rendering the model colour-coded — see
    // Assets/Scripts/Vehicles/README.md.
    //
    // The wall panels all arrive as "cockpit_door.NNN". The four openable ones are the four
    // largest, unambiguously so: 5.11 m long with 188 triangles each, against 1.91 m or less and
    // 44 triangles for every other panel in that group.
    //
    // Despite the naming, the cockpit bulkhead door is *not* one of the "cockpit_door" meshes —
    // it is "Cube.020", the only thin full-height slab standing in the bulkhead plane (2.31 x 2.80
    // x 0.23 at z ~ 3.4, where the cockpit shell meets the cabin). Nothing else in the model is
    // shaped or placed like it.
    private static readonly (string raw, string name)[] PartNames =
    {
        ("Cube",             "CockpitShell"),
        ("Cube.003",         "TailShell"),
        ("Cube.004",         "WingRootBlock"),
        ("Cube.009",         "DeckPlate"),
        ("Cube.013",         "CeilingPlate"),
        ("Cube.020",         "CockpitDoorPanel"),
        ("baggage_door",     "GarageDoorPanel"),
        ("cockpit_door.001", "WallPortLowerPanel"),
        ("cockpit_door.002", "WallPortUpperPanel"),
        ("cockpit_door.004", "WallStarboardUpperPanel"),
        ("cockpit_door.005", "WallStarboardLowerPanel"),
    };

    // Named meshes the build reads without renaming them first.
    private static readonly string[] RequiredRawParts =
    {
        "shell_closed", "shell_open",
        "left_wing", "left_motor", "left_wing_axel",
        "right_wing", "right_motor", "right_wing_axel",
    };

    // The four openable wall panels, as (renamed part, hinge edge, swing sign).
    // Each side of the cabin is one large opening split into a stacked pair. They open as a
    // clamshell: the upper panel hinges on its top edge and lifts up-and-out, the lower one hinges
    // on its bottom edge and drops down-and-out, both swinging clear of the hull to the outside.
    private readonly struct WallSpec
    {
        public readonly string Part;
        public readonly bool Port;      // outboard edge is -X (port) or +X (starboard)
        public readonly bool HingeTop;  // hinge along the top edge rather than the bottom
        public readonly float Angle;

        public WallSpec(string part, bool port, bool hingeTop, float angle)
        {
            Part = part; Port = port; HingeTop = hingeTop; Angle = angle;
        }
    }

    private static readonly WallSpec[] Walls =
    {
        new WallSpec("WallPortUpperPanel",      port: true,  hingeTop: true,  angle: -105f),
        new WallSpec("WallPortLowerPanel",      port: true,  hingeTop: false, angle: 105f),
        new WallSpec("WallStarboardUpperPanel", port: false, hingeTop: true,  angle: 105f),
        new WallSpec("WallStarboardLowerPanel", port: false, hingeTop: false, angle: -105f),
    };

    [MenuItem("Tools/Vehicles/Build ShipRV Prefab")]
    public static void Build()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
        {
            Debug.LogError($"[ShipRVBuilder] Model not found at {ModelPath}");
            return;
        }

        GameObject root = new GameObject("ShipRV");

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        // Restructuring a model instance (reparenting meshes under hinge pivots) is not possible
        // while it is still a prefab instance, so the link to the FBX is deliberately broken here.
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localRotation = ModelYaw;
        model.transform.localPosition = Vector3.zero;

        StripBlenderSceneObjects(model.transform);
        RenameParts(model.transform);

        if (!VerifyParts(model.transform))
        {
            Object.DestroyImmediate(root);
            return;
        }

        // Sit the origin under the middle of the hull at ground level: rotation happens about the
        // root, and a vehicle that pivots around a point buried in its own nose handles badly.
        Bounds whole = MeasureAll(model.transform);
        model.transform.localPosition = new Vector3(-whole.center.x, -whole.min.y, -whole.center.z);

        var parts = new PartLookup(model.transform);

        ArticulatedPart garageDoor = BuildGarageDoor(model.transform, parts);
        ArticulatedPart cockpitDoor = BuildCockpitDoor(model.transform, parts);
        ArticulatedPart wingPort = BuildWing(model.transform, parts, port: true);
        ArticulatedPart wingStarboard = BuildWing(model.transform, parts, port: false);

        ArticulatedPart[] walls = new ArticulatedPart[Walls.Length];
        for (int i = 0; i < Walls.Length; i++)
            walls[i] = BuildWall(model.transform, parts, Walls[i]);

        MakeDoubleSided(model.transform);

        BuildCollision(root.transform, parts);
        (Transform seat, Transform dismount, Transform cameraPivot) = BuildCockpit(root.transform, parts);
        BuildCargoBayFittings(root.transform, parts);

        MountModule mount = BuildRootComponents(root, seat, dismount, cameraPivot);
        WireDeployment(root, mount, garageDoor, cockpitDoor, wingPort, wingStarboard, walls);
        WireShellVariants(root, parts, walls);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ShipRVBuilder] Built {PrefabPath}");
    }

    // ─────────── Model prep ───────────
    // The FBX still carries Blender's default scene Camera and Light. A stray enabled Camera
    // inside a vehicle prefab hijacks rendering the moment the ship is spawned.
    private static void StripBlenderSceneObjects(Transform model)
    {
        foreach (Transform child in model.Cast<Transform>().ToArray())
        {
            if (child.GetComponent<Camera>() == null && child.GetComponent<Light>() == null)
                continue;
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void RenameParts(Transform model)
    {
        foreach ((string raw, string name) in PartNames)
        {
            Transform t = model.Find(raw);
            if (t != null)
                t.name = name;
            else if (model.Find(name) == null)
                Debug.LogWarning($"[ShipRVBuilder] Model has no mesh '{raw}' — the FBX export may have renumbered its meshes.");
        }
    }

    // Every bulkhead, floor and hinge below is measured off these meshes. A lookup that misses
    // returns an empty Bounds and the build carries on regardless, collapsing the collision into a
    // wall across the middle of the ship and hinging a doorless, invisible slab in the cabin —
    // which is far harder to diagnose than a refusal to build. So refuse to build.
    //
    // This is exactly how the cockpit door broke once already: the export dropped the mesh named
    // "cockpit_door" and every measurement derived from the doorway silently went to zero.
    private static bool VerifyParts(Transform model)
    {
        List<string> missing = new List<string>();

        foreach ((string raw, string name) in PartNames)
            if (model.Find(name) == null)
                missing.Add($"{name} — expected source mesh '{raw}'");

        foreach (string raw in RequiredRawParts)
            if (model.Find(raw) == null)
                missing.Add(raw);

        if (missing.Count == 0)
            return true;

        Debug.LogError($"[ShipRVBuilder] Aborting — the model is missing {missing.Count} part(s) the " +
                       "build measures from. The export has most likely renumbered its meshes; " +
                       $"update PartNames to match.\n  {string.Join("\n  ", missing)}");
        return false;
    }

    private static Bounds MeasureAll(Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>
    /// Named access to the model's meshes and their world-space bounds. The build runs with the
    /// ship root at the origin with no rotation, so world space and root-local space coincide and
    /// every measurement below can be used directly as a local coordinate.
    /// </summary>
    private class PartLookup
    {
        private readonly Transform model;
        private readonly Dictionary<string, Bounds> cache = new Dictionary<string, Bounds>();

        public PartLookup(Transform model) => this.model = model;

        public Transform T(string name)
        {
            Transform t = model.Find(name);
            if (t == null)
                Debug.LogError($"[ShipRVBuilder] Missing part '{name}'");
            return t;
        }

        public Renderer R(string name)
        {
            Transform t = model.Find(name);
            return t != null ? t.GetComponent<Renderer>() : null;
        }

        public Bounds B(string name)
        {
            if (cache.TryGetValue(name, out Bounds cached))
                return cached;

            Transform t = T(name);
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            Bounds b = r != null ? r.bounds : new Bounds();
            cache[name] = b;
            return b;
        }
    }

    // ─────────── Hinges ───────────
    private static Transform MakePivot(Transform parent, string name, Vector3 position, params Transform[] children)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(parent, false);
        // Identity rotation keeps the hinge axis a clean ship axis, which makes the angles in the
        // inspector readable ("90 about Z") instead of an arbitrary local direction.
        pivot.transform.SetPositionAndRotation(position, Quaternion.identity);

        foreach (Transform child in children)
            if (child != null)
                child.SetParent(pivot.transform, worldPositionStays: true);

        return pivot.transform;
    }

    private static ArticulatedPart AddPart(Transform pivot, ArticulatedPart.MotionType motion,
                                           Vector3 axis, float angle, float openDuration, float closeDuration)
    {
        ArticulatedPart part = pivot.gameObject.AddComponent<ArticulatedPart>();
        Serialize.Apply(part,
            ("motion", (int)motion),
            ("axis", axis),
            ("openAngle", angle),
            ("openDuration", openDuration),
            ("closeDuration", closeDuration));
        return part;
    }

    // Rear hatch: hinged along its bottom edge so it drops aft into a boarding ramp.
    private static ArticulatedPart BuildGarageDoor(Transform model, PartLookup parts)
    {
        Bounds b = parts.B("GarageDoorPanel");
        Transform pivot = MakePivot(model, "GarageDoor",
            new Vector3(b.center.x, b.min.y, b.center.z), parts.T("GarageDoorPanel"));

        // Slightly past horizontal so the free end slopes down to meet the ground.
        ArticulatedPart part = AddPart(pivot, ArticulatedPart.MotionType.Rotate, Vector3.right, -100f, 2.2f, 2.2f);
        AddPanelCollider(pivot, b);
        pivot.gameObject.AddComponent<ArticulatedPartInteraction>();
        return part;
    }

    // Bulkhead door between cockpit and cargo bay: hinged on its starboard edge, swings aft into
    // the bay so it never blocks the pilot's station.
    private static ArticulatedPart BuildCockpitDoor(Transform model, PartLookup parts)
    {
        Bounds b = parts.B("CockpitDoorPanel");
        Transform pivot = MakePivot(model, "CockpitDoor",
            new Vector3(b.max.x, b.center.y, b.center.z), parts.T("CockpitDoorPanel"));

        ArticulatedPart part = AddPart(pivot, ArticulatedPart.MotionType.Rotate, Vector3.up, -90f, 1.1f, 1.1f);
        AddPanelCollider(pivot, b);
        pivot.gameObject.AddComponent<ArticulatedPartInteraction>();
        return part;
    }

    // Wing + motor + axle swing as one rigid unit about the axle the artist modelled at the
    // outboard tip, dropping the nacelles from stowed-over-the-hull to deployed-beside-the-hull.
    // The model names these left/right; left is port (-X once the model is yawed into place).
    // Past 90° the wing does not just reach horizontal, it carries on down so the nacelles sit
    // below the axle line and the hull sits higher between them.
    private const float WingDeployAngle = 115f;

    private static ArticulatedPart BuildWing(Transform model, PartLookup parts, bool port)
    {
        string side = port ? "left" : "right";
        Bounds axle = parts.B(side + "_wing_axel");

        Transform pivot = MakePivot(model, "Wing" + (port ? "Port" : "Starboard"), axle.center,
            parts.T(side + "_wing"), parts.T(side + "_motor"), parts.T(side + "_wing_axel"));

        return AddPart(pivot, ArticulatedPart.MotionType.Rotate, Vector3.forward,
                       port ? WingDeployAngle : -WingDeployAngle, 2.5f, 2.5f);
    }

    // One of the four large cabin wall panels. Hinges along its horizontal outboard edge — top
    // edge for the upper panels, bottom edge for the lower ones — so a side opens as a clamshell
    // and both leaves swing clear of the hull rather than into the cabin.
    //
    // The panel carries its own collider, which is what makes the opening navigable: while closed
    // it is the cabin wall, and swinging it away takes the wall with it. That is why the static
    // collision below deliberately leaves the cabin sides open.
    //
    // The hinge line sits past the panel's own edge — above the top panel, below the bottom one —
    // rather than flush with it, so the two leaves tuck further into the hull as they swing and
    // open a taller gap for a boarding player than hinging exactly at the doorway edge would.
    private const float WallHingeMargin = 0.25f;

    private static ArticulatedPart BuildWall(Transform model, PartLookup parts, WallSpec spec)
    {
        Bounds b = parts.B(spec.Part);
        Transform panel = parts.T(spec.Part);
        if (panel == null)
            return null;

        // Hinge line runs fore-aft along Z, offset past the panel's outboard edge.
        Vector3 hinge = new Vector3(
            spec.Port ? b.min.x : b.max.x,
            spec.HingeTop ? b.max.y + WallHingeMargin : b.min.y - WallHingeMargin,
            b.center.z);

        Transform pivot = MakePivot(model, spec.Part.Replace("Panel", string.Empty), hinge, panel);
        ArticulatedPart part = AddPart(pivot, ArticulatedPart.MotionType.Rotate, Vector3.forward, spec.Angle, 2.0f, 2.0f);
        AddPanelCollider(pivot, b);
        pivot.gameObject.AddComponent<ArticulatedPartInteraction>();
        return part;
    }

    private static void WireShellVariants(GameObject root, PartLookup parts, ArticulatedPart[] walls)
    {
        Renderer closed = parts.R("shell_closed");
        Renderer open = parts.R("shell_open");
        if (closed == null || open == null)
        {
            Debug.LogWarning("[ShipRVBuilder] shell_closed / shell_open not both present — skipping shell switching.");
            return;
        }

        ShellVariantSwitcher switcher = root.AddComponent<ShellVariantSwitcher>();
        Serialize.Apply(switcher, ("closedShell", closed), ("openShell", open));
        Serialize.ApplyArray(switcher, "parts", walls);

        // Authored state is buttoned up: seamless shell on, cut-out shell off.
        closed.enabled = true;
        open.enabled = false;
    }

    private static void AddPanelCollider(Transform pivot, Bounds worldBounds)
    {
        BoxCollider box = pivot.gameObject.AddComponent<BoxCollider>();
        box.center = worldBounds.center - pivot.position;
        box.size = worldBounds.size;
    }

    // ─────────── Materials ───────────
    // The hull is modelled as a surface, not a solid, so with back-face culling on you can stand in
    // the cabin and see straight out through the walls, floor and ceiling. Rendering the ship
    // double-sided fixes the interior without changing the exterior at all: front faces still draw
    // exactly as they did, back faces simply stop being discarded.
    //
    // The source materials are sub-assets of the .blend, regenerated every time the artist saves
    // it, so a cull flag written onto them would silently revert on the next import. These
    // generated copies live beside the prefab instead and are refreshed from their source on every
    // build, so re-running this after a re-texture still picks up the new look.
    private const string GeneratedMaterialFolder = "Assets/Prefabs/agents/vehicle/Materials";
    private const string DoubleSidedSuffix = " (DoubleSided)";

    private static void MakeDoubleSided(Transform model)
    {
        var remap = new Dictionary<Material, Material>();

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;

                Material variant = DoubleSidedCopy(mats[i], remap);
                if (variant == mats[i])
                    continue;

                mats[i] = variant;
                changed = true;
            }

            if (changed)
                r.sharedMaterials = mats;
        }
    }

    private static Material DoubleSidedCopy(Material source, Dictionary<Material, Material> cache)
    {
        if (cache.TryGetValue(source, out Material cached))
            return cached;

        // Already one of ours — happens when the builder is re-run against a rebuilt prefab.
        if (source.name.EndsWith(DoubleSidedSuffix))
        {
            cache[source] = source;
            return source;
        }

        if (!AssetDatabase.IsValidFolder(GeneratedMaterialFolder))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(GeneratedMaterialFolder),
                                       System.IO.Path.GetFileName(GeneratedMaterialFolder));

        string path = $"{GeneratedMaterialFolder}/{source.name}{DoubleSidedSuffix}.mat";
        Material variant = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (variant == null)
        {
            variant = new Material(source) { name = source.name + DoubleSidedSuffix };
            AssetDatabase.CreateAsset(variant, path);
        }
        else
        {
            variant.shader = source.shader;
            variant.CopyPropertiesFromMaterial(source);
        }

        if (variant.HasProperty("_Cull"))
            variant.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        variant.doubleSidedGI = true;
        EditorUtility.SetDirty(variant);

        cache[source] = variant;
        return variant;
    }

    // ─────────── Cargo bay fittings ───────────
    private const string WorkstationPath = "Assets/Prefabs/Environment/RepairWorkstation.prefab";

    // The scrap-fed repair workstation, backed against the port cabin wall and facing starboard
    // across the bay. Kept as a nested prefab instance so edits to the workstation itself still
    // flow through. Measured off the wall panel rather than the deck: the panels sit a good half
    // metre inboard of the hull skin, and a machine lined up with the skin would stand inside the
    // wall.
    private static void BuildCargoBayFittings(Transform root, PartLookup parts)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkstationPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ShipRVBuilder] {WorkstationPath} not found — cargo bay left empty.");
            return;
        }

        Bounds portWall = parts.B("WallPortLowerPanel");
        float floorTop = parts.B("DeckPlate").max.y;

        GameObject station = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        station.name = "RepairWorkstation";

        // Yawed a quarter turn so the machine's length runs fore-aft along the wall and its face —
        // gauge, status lamp — looks in across the cabin at whoever is standing in front of it.
        BoxCollider box = station.GetComponent<BoxCollider>();
        float halfDepth = box != null ? box.size.z * 0.5f : 0.6f;

        station.transform.localPosition = new Vector3(portWall.max.x + halfDepth, floorTop, portWall.center.z);
        station.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
    }

    // ─────────── Collision ───────────
    // The model has no interior walls and no cockpit floor. These boxes are what actually makes the
    // ship solid from outside and walkable from inside.
    private static void BuildCollision(Transform root, PartLookup parts)
    {
        Bounds hull = parts.B("shell_closed");
        Bounds cockpit = parts.B("CockpitShell");
        Bounds deck = parts.B("DeckPlate");
        Bounds ceiling = parts.B("CeilingPlate");
        Bounds gdoor = parts.B("GarageDoorPanel");
        Bounds cdoor = parts.B("CockpitDoorPanel");

        float floorTop = deck.max.y;
        float ceilBottom = ceiling.min.y;
        const float wall = 0.2f;

        GameObject group = new GameObject("Collision");
        group.transform.SetParent(root, false);

        // The walkable volume runs all the way aft to the cargo doorway, not just to the end of
        // the hull shell — the tail section between the two is where the boarding ramp lands, and
        // a player walking up it would otherwise drop through the floor.
        float aft = gdoor.center.z;

        // Deck slabs run from the belly up to walking height, so the same box both carries the
        // player inside and rests the ship on the ground outside. Width comes from the deck plate,
        // not the outer shell: the wall panels sit about half a metre inboard of the skin, and a
        // floor as wide as the hull would let the player walk off the deck into that void.
        Box(group.transform, "CargoBayFloor", deck.min.x, deck.max.x, hull.min.y, floorTop, aft, cdoor.center.z);
        Box(group.transform, "CockpitFloor", cockpit.min.x + wall, cockpit.max.x - wall, hull.min.y, floorTop, cdoor.center.z, cockpit.max.z - 0.4f);

        Box(group.transform, "Ceiling", hull.min.x, hull.max.x, ceilBottom, ceilBottom + 0.3f, aft, cockpit.max.z);

        // No static walls down the cabin sides: that span is exactly what the four openable panels
        // cover, and each carries its own collider. Boxing the sides here as well would leave the
        // opening blocked by an invisible wall after the panels swing away.
        //
        // The panels only reach aft to roughly the middle of the bay, so the remaining stretch
        // between them and the tail still needs solid sides.
        Bounds wallPort = parts.B("WallPortLowerPanel");
        Box(group.transform, "HullSidePortAft", deck.min.x, deck.min.x + wall, floorTop, ceilBottom, aft, wallPort.min.z);
        Box(group.transform, "HullSideStarboardAft", deck.max.x - wall, deck.max.x, floorTop, ceilBottom, aft, wallPort.min.z);

        Box(group.transform, "CockpitSidePort", cockpit.min.x, cockpit.min.x + wall, floorTop, ceilBottom, cdoor.center.z, cockpit.max.z);
        Box(group.transform, "CockpitSideStarboard", cockpit.max.x - wall, cockpit.max.x, floorTop, ceilBottom, cdoor.center.z, cockpit.max.z);
        Box(group.transform, "NoseCap", cockpit.min.x, cockpit.max.x, floorTop, ceilBottom, cockpit.max.z - 0.4f, cockpit.max.z);

        // Bulkhead either side of the cockpit doorway.
        Box(group.transform, "BulkheadPort", hull.min.x, cdoor.min.x, floorTop, ceilBottom, cdoor.center.z - wall * 0.5f, cdoor.center.z + wall * 0.5f);
        Box(group.transform, "BulkheadStarboard", cdoor.max.x, hull.max.x, floorTop, ceilBottom, cdoor.center.z - wall * 0.5f, cdoor.center.z + wall * 0.5f);

        // Tail either side of the cargo doorway, in the plane of the door itself.
        Box(group.transform, "TailPort", hull.min.x, gdoor.min.x, floorTop, ceilBottom, aft - wall * 0.5f, aft + wall * 0.5f);
        Box(group.transform, "TailStarboard", gdoor.max.x, hull.max.x, floorTop, ceilBottom, aft - wall * 0.5f, aft + wall * 0.5f);
        Box(group.transform, "TailLintel", gdoor.min.x, gdoor.max.x, gdoor.max.y, ceilBottom, aft - wall * 0.5f, aft + wall * 0.5f);
    }

    private static void Box(Transform parent, string name,
                            float x0, float x1, float y0, float y1, float z0, float z1)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        box.size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
    }

    // ─────────── Cockpit ───────────
    private static (Transform seat, Transform dismount, Transform cameraPivot) BuildCockpit(Transform root, PartLookup parts)
    {
        Bounds cockpit = parts.B("CockpitShell");
        Bounds hull = parts.B("shell_closed");
        float floorTop = parts.B("DeckPlate").max.y;

        float centreX = cockpit.center.x;
        float noseZ = cockpit.max.z;

        GameObject group = new GameObject("Cockpit");
        group.transform.SetParent(root, false);

        Transform wheel = BuildSteeringWheel(group.transform, new Vector3(centreX, floorTop + 1.05f, noseZ - 1.35f));

        Transform seat = Empty(group.transform, "SeatPoint", new Vector3(centreX, floorTop + 0.02f, noseZ - 2.15f));
        // Standing beside the wheel rather than outside the hull — stepping out of the chair at
        // 3000 m would otherwise drop the pilot through the sky.
        Transform dismount = Empty(group.transform, "DismountPoint", new Vector3(centreX + 0.75f, floorTop + 0.02f, noseZ - 2.15f));
        Transform cameraPivot = Empty(root, "CameraPivot", new Vector3(0f, hull.center.y + 1.2f, hull.center.z));

        MountStation station = wheel.gameObject.AddComponent<MountStation>();
        Serialize.Apply(station, ("mount", (Object)null)); // resolved from parents at Awake
        return (seat, dismount, cameraPivot);
    }

    private static Transform Empty(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    // Placeholder geometry built from primitives — swap the children for a modelled yoke whenever
    // one exists; only the collider and MountStation on "SteeringWheel" itself matter.
    private static Transform BuildSteeringWheel(Transform parent, Vector3 localPosition)
    {
        GameObject wheel = new GameObject("SteeringWheel");
        wheel.transform.SetParent(parent, false);
        wheel.transform.localPosition = localPosition;
        wheel.transform.localRotation = Quaternion.Euler(-65f, 0f, 0f); // tilted back toward the pilot

        const float radius = 0.34f;
        const float thickness = 0.05f;
        const int segments = 12;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            Quaternion around = Quaternion.Euler(0f, 0f, angle);
            GameObject seg = Primitive(wheel.transform, "Rim" + i, PrimitiveType.Cylinder);
            seg.transform.localPosition = around * new Vector3(0f, radius, 0f);
            seg.transform.localRotation = around * Quaternion.Euler(0f, 0f, 90f);
            seg.transform.localScale = new Vector3(thickness, Mathf.PI * radius / segments, thickness);
        }

        for (int i = 0; i < 3; i++)
        {
            float angle = 90f + i * 120f;
            Quaternion around = Quaternion.Euler(0f, 0f, angle);
            GameObject spoke = Primitive(wheel.transform, "Spoke" + i, PrimitiveType.Cylinder);
            spoke.transform.localPosition = around * new Vector3(0f, radius * 0.5f, 0f);
            spoke.transform.localRotation = around * Quaternion.Euler(0f, 0f, 90f);
            spoke.transform.localScale = new Vector3(thickness * 0.7f, radius * 0.5f, thickness * 0.7f);
        }

        GameObject hub = Primitive(wheel.transform, "Hub", PrimitiveType.Cylinder);
        hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        hub.transform.localScale = new Vector3(0.14f, 0.04f, 0.14f);

        GameObject column = Primitive(wheel.transform, "Column", PrimitiveType.Cylinder);
        column.transform.localPosition = new Vector3(0f, -radius - 0.25f, 0f);
        column.transform.localScale = new Vector3(0.09f, 0.3f, 0.09f);

        // One collider on the parent is the interaction surface; the primitives' own colliders
        // would otherwise sit in front of it and swallow the raycast.
        BoxCollider box = wheel.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(radius * 2.2f, radius * 2.2f, 0.22f);

        return wheel.transform;
    }

    private static GameObject Primitive(Transform parent, string name, PrimitiveType type)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ─────────── Root components ───────────
    private static MountModule BuildRootComponents(GameObject root, Transform seat, Transform dismount, Transform cameraPivot)
    {
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 4000f;
        // Low linear damping: FlyingRigidbodyMotor owns the rider's velocity outright and has its
        // own deceleration, so drag here only fights the throttle when coasting.
        body.linearDamping = 0.3f;
        body.angularDamping = 4f;
        body.useGravity = false; // FlyingRigidbodyMotor owns altitude
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // The motor writes rotation through the transform; letting physics also spin an 11 m hull
        // that the player walks around inside only produces drift.
        body.constraints = RigidbodyConstraints.FreezeRotation;

        FlyingRigidbodyMotor motor = root.AddComponent<FlyingRigidbodyMotor>();
        Serialize.Apply(motor,
            ("body", body),
            ("maxSpeed", 45f),
            ("maxVerticalSpeed", 20f),
            ("acceleration", 30f),
            ("deceleration", 22f),
            ("faceRotateSpeed", 2.5f),
            ("riderTurnSpeed", 100f),
            ("altitudeHold", false));

        root.AddComponent<AgentController>();

        MountModule mount = root.AddComponent<MountModule>();
        Serialize.Apply(mount,
            ("seatPoint", seat),
            ("dismountPoint", dismount),
            ("thirdPersonPivot", cameraPivot),
            // Only the cockpit wheel boards this ship; without this every hull collider would
            // resolve up to this component and become a mount point.
            ("mountableByDirectInteraction", false),
            ("allowAISelfMovementWhenMounted", false),
            ("defaultPerspective", (int)MountModule.CameraPerspective.ThirdPerson),
            // Framed for an 11 m hull rather than the default man-sized mount.
            ("thirdPersonOffset", new Vector3(0f, 7f, -18f)),
            ("thirdPersonDistance", 18f),
            ("thirdPersonLookAhead", 14f),
            ("fallbackDismountDistance", 4f));

        SteerModule steer = root.AddComponent<SteerModule>();
        Serialize.Apply(steer,
            ("mountModule", mount),
            ("moveActionName", "Move"),
            ("verticalActionName", "Vertical"),
            ("jumpEnabled", false),
            ("leapEnabled", false),
            ("riderCanRun", false),
            ("turnSmoothTime", 0.25f));

        return mount;
    }

    private static void WireDeployment(GameObject root, MountModule mount, ArticulatedPart garageDoor,
                                       ArticulatedPart cockpitDoor, ArticulatedPart wingPort,
                                       ArticulatedPart wingStarboard, ArticulatedPart[] walls)
    {
        VehicleDeploymentController deployment = root.AddComponent<VehicleDeploymentController>();
        Serialize.Apply(deployment, ("mountModule", mount), ("retractOnDismount", true));
        Serialize.ApplyArray(deployment, "deployOnMount", wingPort, wingStarboard);

        // Everything that opens shuts when a pilot takes the controls — flying with the cabin
        // walls hanging open would be daft, and it doubles as the "seal up for takeoff" beat.
        var close = new List<Object> { garageDoor, cockpitDoor };
        foreach (ArticulatedPart w in walls)
            if (w) close.Add(w);
        Serialize.ApplyArray(deployment, "closeOnMount", close.ToArray());
    }

    // ─────────── SerializedObject helper ───────────
    // The runtime components keep their fields private with [SerializeField], which is the right
    // call for gameplay code but means the builder has to go through SerializedObject to set them.
    private static class Serialize
    {
        public static void Apply(Object target, params (string prop, object value)[] values)
        {
            SerializedObject so = new SerializedObject(target);
            foreach ((string prop, object value) in values)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p == null)
                {
                    Debug.LogError($"[ShipRVBuilder] {target.GetType().Name} has no serialized field '{prop}'");
                    continue;
                }

                switch (value)
                {
                    case null: p.objectReferenceValue = null; break;
                    case int i: p.intValue = i; break;
                    case float f: p.floatValue = f; break;
                    case bool b: p.boolValue = b; break;
                    case string s: p.stringValue = s; break;
                    case Vector3 v: p.vector3Value = v; break;
                    case Component c: p.objectReferenceValue = c; break;
                    case Object o: p.objectReferenceValue = o; break;
                    default: Debug.LogError($"[ShipRVBuilder] Unsupported value type for '{prop}'"); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void ApplyArray(Object target, string prop, params Object[] values)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogError($"[ShipRVBuilder] {target.GetType().Name} has no serialized field '{prop}'");
                return;
            }

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
