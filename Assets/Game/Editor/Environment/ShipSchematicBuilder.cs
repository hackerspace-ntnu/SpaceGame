// Bakes the miniature of the lander that the cockpit terminal draws on its SHIP page.
//
// It is cut from player_ship.fbx — the SAME model PlayerShipBuilder builds the ship itself from —
// so the drawing on the glass cannot describe a hull that no longer exists. A hand-drawn schematic
// is a second source of truth about the ship's shape, and the first thing to rot after a remodel.
//
// What comes out is deliberately not a ship: no colliders, no scripts, no articulation, no
// materials of its own, one flat child per mesh. Everything about how it LOOKS is decided at
// runtime by ShipSchematicStage, which paints each renderer through a property block — fitted,
// missing, or under the cursor — so this file has nothing to say about colour.
//
// Re-running is safe and is the intended workflow. Re-export the ship, run this, and the
// schematic is rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Ship Schematic Prefab
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class ShipSchematicBuilder
    {
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/ShipSchematic.prefab";

        private const string Fbx = "Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship.fbx";

        /// <summary>
        /// How long the miniature is made, in units. Any number works — the lens frames whatever
        /// it is handed — but a model about a unit across keeps the orbit's standoff and clip
        /// planes in a range where a float still has digits to spare.
        /// </summary>
        private const float TargetLength = 1f;

        /// <summary>
        /// Collision hulls travel with some exports and draw as a box round the whole ship. They
        /// are never part of the picture.
        /// </summary>
        private const string CollisionPrefix = "COL_";

        /// <summary>
        /// Where the line meshes live. A generated asset rather than sub-objects of the prefab: a
        /// mesh built in memory and referenced by a saved prefab comes back null the next time the
        /// project is opened, and this way one delete rebuilds the lot with no orphans left behind.
        /// </summary>
        private const string WirePath = "Assets/Game/Art/Models/Generated/ShipSchematicWire.asset";

        /// <summary>
        /// How far two faces must fold apart before the edge between them is inked. Low enough to
        /// catch a chamfer, high enough to leave the inside of a curved panel alone — on this hull
        /// it is the difference between a drawing and a hairball. Change it and rebuild; the number
        /// is a look, not a fact about the model.
        /// </summary>
        private const float CreaseDegrees = 28f;

        [MenuItem("Tools/SpaceGame/Build Ship Schematic Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                Debug.LogError($"[ShipSchematicBuilder] No model at {Fbx} — run player_ship_export.py first.");
                return;
            }

            GameObject source = Object.Instantiate(model);
            var root = new GameObject("ShipSchematic");
            try
            {
                var fit = new GameObject("Fit").transform;
                fit.SetParent(root.transform, false);

                var parts = new List<ShipSchematicModel.Part>();
                var hull = new List<Renderer>();
                var unknown = new List<string>();

                // One accumulator per module, and one for everything else. Welding the hull's many
                // panels together is the point: they meet along seams that are not folds, and a
                // per-panel outline would draw a cage instead of a ship.
                var partEdges = new List<FeatureEdges>();
                var hullEdges = new FeatureEdges();

                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    if (filter.name.StartsWith(CollisionPrefix)) continue;

                    Renderer copy = Copy(filter, fit);

                    if (!ShipPartNaming.IsPart(filter.name))
                    {
                        hull.Add(copy);
                        hullEdges.Add(filter, fit);
                        continue;
                    }

                    if (!ShipPartNaming.TryParseKind(filter.name, out ShipPartKind kind))
                    {
                        unknown.Add(filter.name);
                        continue;
                    }

                    var edges = new FeatureEdges();
                    edges.Add(filter, fit);
                    partEdges.Add(edges);

                    parts.Add(new ShipSchematicModel.Part
                    {
                        socketName = filter.name,
                        kind = kind,
                        partRenderer = copy,
                    });
                }

                if (unknown.Count > 0)
                {
                    Debug.LogError($"[ShipSchematicBuilder] {unknown.Count} mesh(es) carry the " +
                                   $"'{ShipPartNaming.Prefix}' prefix but name no known ShipPartKind: " +
                                   $"{string.Join(", ", unknown)}. Fix PART_KINDS in ship_parts.py, or " +
                                   "add the kind to ShipPartKind.cs.");
                    return;
                }

                if (parts.Count == 0)
                {
                    Debug.LogError($"[ShipSchematicBuilder] {Fbx} has no '{ShipPartNaming.Prefix}' meshes. " +
                                   "The schematic would draw a hull with nothing on it to click.");
                    return;
                }

                // Every socket the ship builds must be a socket the schematic draws, or a player
                // stares at a whole-looking ship the console calls incomplete.
                var missing = System.Enum.GetValues(typeof(ShipPartKind)).Cast<ShipPartKind>()
                    .Where(kind => parts.All(part => part.kind != kind))
                    .ToList();

                if (missing.Count > 0)
                {
                    Debug.LogError($"[ShipSchematicBuilder] No mesh for {string.Join(", ", missing)} — " +
                                   "the schematic and the ship would disagree about this hull.");
                    return;
                }

                // The wireframe. Built once, saved as assets, and only then referenced by the
                // prefab — a mesh made in memory and pointed at by a saved prefab is a null the
                // next time the project is opened.
                var partWireMesh = new Mesh[parts.Count];
                var wires = new List<Mesh>();

                for (int i = 0; i < parts.Count; i++)
                {
                    partWireMesh[i] = partEdges[i].ToMesh("Wire_" + parts[i].socketName, CreaseDegrees);
                    if (partWireMesh[i] != null) wires.Add(partWireMesh[i]);
                }

                Mesh hullWireMesh = hullEdges.ToMesh("Wire_Hull", CreaseDegrees);
                if (hullWireMesh != null) wires.Add(hullWireMesh);

                if (wires.Count == 0)
                {
                    Debug.LogError("[ShipSchematicBuilder] The model yielded no feature edges at " +
                                   $"{CreaseDegrees}° — the schematic would draw nothing.");
                    return;
                }

                if (!SaveWireMeshes(wires)) return;

                var partWire = new Renderer[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                    if (partWireMesh[i] != null) partWire[i] = Lines(partWireMesh[i], fit);

                var hullWire = new List<Renderer>();
                if (hullWireMesh != null) hullWire.Add(Lines(hullWireMesh, fit));

                Normalise(fit);
                SetLayer(root, ShipSchematicStage.LayerName);

                var index = root.AddComponent<ShipSchematicModel>();
                var so = new SerializedObject(index);
                SerializedProperty partsProperty = so.FindProperty("parts");
                partsProperty.arraySize = parts.Count;
                for (int i = 0; i < parts.Count; i++)
                {
                    SerializedProperty element = partsProperty.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("socketName").stringValue = parts[i].socketName;
                    element.FindPropertyRelative("kind").enumValueIndex = (int)parts[i].kind;
                    element.FindPropertyRelative("partRenderer").objectReferenceValue = parts[i].partRenderer;
                    element.FindPropertyRelative("wireRenderer").objectReferenceValue = partWire[i];
                }

                SerializedProperty hullProperty = so.FindProperty("hull");
                hullProperty.arraySize = hull.Count;
                for (int i = 0; i < hull.Count; i++)
                    hullProperty.GetArrayElementAtIndex(i).objectReferenceValue = hull[i];

                SerializedProperty hullWireProperty = so.FindProperty("hullWire");
                hullWireProperty.arraySize = hullWire.Count;
                for (int i = 0; i < hullWire.Count; i++)
                    hullWireProperty.GetArrayElementAtIndex(i).objectReferenceValue = hullWire[i];

                so.ApplyModifiedPropertiesWithoutUndo();

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                // A read-only AssetDatabase — an MPPM clone — discards prefab saves without erroring.
                if (!File.Exists(PrefabPath))
                {
                    Debug.LogError($"[ShipSchematicBuilder] {PrefabPath} did not reach disk — is this a " +
                                   "read-only editor clone?");
                    return;
                }

                Debug.Log($"[ShipSchematicBuilder] Built {PrefabPath}: {parts.Count} module(s), " +
                          $"{hull.Count} hull mesh(es).");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Rebuilds the miniature and hands it back. What the terminal's builder calls, so one menu
        /// item produces a whole terminal.
        ///
        /// <para>
        /// Unconditionally, where it once returned an existing prefab untouched. "Only build it if
        /// it is missing" meant that any change to what a miniature CONTAINS — the wireframe was
        /// the first — left the terminal builder happily wiring up last week's prefab, and the only
        /// symptom was a runtime error about a shader on a component whose prefab looked fine.
        /// </para>
        /// </summary>
        public static GameObject Rebuild()
        {
            Build();
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>
        /// Writes every line mesh into one generated asset. Deleted and remade rather than updated,
        /// so a rebuild after a module is renamed cannot leave the old mesh behind as a sub-object
        /// nothing references.
        /// </summary>
        private static bool SaveWireMeshes(List<Mesh> wires)
        {
            EnsureAssetFolder(Path.GetDirectoryName(WirePath).Replace('\\', '/'));
            AssetDatabase.DeleteAsset(WirePath);

            AssetDatabase.CreateAsset(wires[0], WirePath);
            for (int i = 1; i < wires.Count; i++) AssetDatabase.AddObjectToAsset(wires[i], WirePath);
            AssetDatabase.SaveAssets();

            if (File.Exists(WirePath)) return true;

            Debug.LogError($"[ShipSchematicBuilder] {WirePath} did not reach disk — is this a " +
                           "read-only editor clone?");
            return false;
        }

        /// <summary>
        /// Makes a folder the AssetDatabase knows about, one level at a time.
        ///
        /// <para>
        /// Not <c>Directory.CreateDirectory</c>: a folder made behind the AssetDatabase's back does
        /// not exist as far as <c>CreateAsset</c> is concerned until something refreshes, and the
        /// write into it fails on the run that created it and works on the next one — which is the
        /// worst way for a builder to behave.
        /// </para>
        /// </summary>
        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>
        /// A renderer for one line mesh, sitting at the origin of the frame its vertices were
        /// measured in — so it needs no transform of its own and cannot drift from the faces.
        /// </summary>
        private static Renderer Lines(Mesh mesh, Transform parent)
        {
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            return go.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// One flat child per mesh, holding the pose the mesh has relative to the model root. Flat
        /// rather than the model's own hierarchy because nothing here articulates: the schematic
        /// never opens a door, and a copy of the ship's rigging would be a second thing to keep in
        /// step with the ship's.
        /// </summary>
        private static Renderer Copy(MeshFilter filter, Transform parent)
        {
            var go = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);

            // The pose relative to the FBX root, which is what makes a flat copy sit where the
            // original did without inheriting any of the transforms above it.
            Transform modelRoot = filter.transform.root;
            go.transform.SetLocalPositionAndRotation(
                modelRoot.InverseTransformPoint(filter.transform.position),
                Quaternion.Inverse(modelRoot.rotation) * filter.transform.rotation);
            go.transform.localScale = filter.transform.lossyScale;

            go.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            return go.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Centres the copy on the origin and scales it to <see cref="TargetLength"/>, on the ONE
        /// transform above the meshes. On the root instead, the stage would have to keep that scale
        /// when it plants the miniature — and it deliberately forces the root to unit scale so that
        /// the miniature's space and the stage's are the same space.
        /// </summary>
        private static void Normalise(Transform fit)
        {
            Bounds bounds = VertexBounds(fit.gameObject);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 0.0001f ? TargetLength / longest : 1f;

            fit.localScale = Vector3.one * scale;
            fit.localPosition = -bounds.center * scale;
        }

        /// <summary>The box round what is drawn, from the vertices — see StandingTerminalBuilder for why not Renderer.bounds.</summary>
        private static Bounds VertexBounds(GameObject visual)
        {
            bool any = false;
            var bounds = new Bounds();

            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(v);
                    if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                    else bounds.Encapsulate(world);
                }
            }

            return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        private static void SetLayer(GameObject root, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogError($"[ShipSchematicBuilder] No layer named '{layerName}'. Add it in " +
                               "Project Settings ▸ Tags and Layers — the terminal's lens renders " +
                               "that layer and nothing else.");
                return;
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
