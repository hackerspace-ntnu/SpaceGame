using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Puts the four hand-authored gauntlets onto the shared gauntlet base, and keeps them there.
    ///
    /// <para>
    /// The Sucker Puncher and the Repulsor have builders that rebuild them from nothing. These four
    /// do not: their prefabs carry hand-authored content — a line renderer, a hook-head prefab
    /// reference, sound ids, colliders — that no script here knows how to re-author, and writing
    /// four more builders to own it would be four more places for it to rot. So this does the one
    /// thing that IS mechanical: it swaps the model child for the item's gauntlet FBX, re-points
    /// every reference that names a node inside that model, and applies the family's fit and sizes.
    /// Everything else on the prefab is left exactly as it was.
    /// </para>
    /// <para>
    /// Re-runnable. Run it after re-exporting any gauntlet model — the references it fixes are the
    /// ones a re-export silently breaks, because Unity assigns the file ids inside an FBX at import
    /// time and a node that changed name comes back as a null field with nothing in the console.
    /// </para>
    /// <para>
    /// <b>Verified out loud.</b> Unity discards prefab saves when the AssetDatabase is read-only and
    /// says nothing (see <c>ItemScaleLadder</c>), so every prefab is re-loaded off disk afterwards
    /// and its wiring asserted.
    /// </para>
    /// </summary>
    public static class GauntletReseat
    {
        private const string LogTag = "Gauntlets";
        private const string Gadgets = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/";
        private const string Models = "Assets/Game/Art/Models/Items/";

        /// <summary>A reference on a component that names a node inside the model.</summary>
        private readonly struct Wire
        {
            /// <summary>Component type name, unqualified — matched on the prefab's own components.</summary>
            public readonly string Component;
            /// <summary>Private serialized field to write.</summary>
            public readonly string Field;
            /// <summary>Node to find inside the model.</summary>
            public readonly string Node;
            /// <summary>What to hand the field: the node's transform, its game object, or its renderer.</summary>
            public readonly Kind As;

            public Wire(string component, string field, string node, Kind kind)
            {
                Component = component;
                Field = field;
                Node = node;
                As = kind;
            }
        }

        private enum Kind { Transform, GameObject, Renderer }

        /// <summary>
        /// A material the prefab puts on a model node, overriding the one the FBX carries.
        ///
        /// <para>
        /// The model library gives every face a palette material, which is what a mesh looks like
        /// as an object in the world. A screen is not that: it is a surface a shader draws on, and
        /// its material is a project asset the artifact's own code writes into. Re-instantiating
        /// the model resets it to the palette's flat green, and the only symptom is a scanner with
        /// a blank screen.
        /// </para>
        /// </summary>
        private readonly struct Paint
        {
            public readonly string Node;
            public readonly int Index;
            public readonly string Material;

            public Paint(string node, int index, string material)
            {
                Node = node;
                Index = index;
                Material = material;
            }
        }

        private readonly struct Gauntlet
        {
            public readonly string Prefab;
            public readonly string Model;
            /// <summary>Root children to keep. Everything else under the root is replaced.</summary>
            public readonly string[] Keep;
            public readonly Wire[] Wires;
            public readonly Paint[] Paints;

            public Gauntlet(string prefab, string model, string[] keep, Wire[] wires,
                            Paint[] paints = null)
            {
                Prefab = Gadgets + prefab;
                Model = Models + model;
                Keep = keep;
                Wires = wires;
                Paints = paints ?? Array.Empty<Paint>();
            }
        }

        private static readonly Gauntlet[] Roster =
        {
            new("GrapplingHook.prefab", "gauntlet_grapple.fbx",
                // The rope's LineRenderer lives on its own child and is authored, not derived.
                new[] { "line (1)" },
                new[]
                {
                    new Wire("GrapplingHookArtifact", "muzzle", "muzzle", Kind.Transform),
                    // Hidden while the head is in flight and shown again on return, so it has to be
                    // the harpoon OBJECT and not its transform.
                    new Wire("GrapplingHookArtifact", "seatedHook", "Mesh_GrappleHarpoon", Kind.GameObject),
                }),

            new("Leash.prefab", "gauntlet_leash.fbx",
                Array.Empty<string>(),
                new[] { new Wire("LeashArtifact", "muzzle", "muzzle", Kind.Transform) }),

            new("ItemScanner.prefab", "gauntlet_item_scanner.fbx",
                Array.Empty<string>(),
                new[]
                {
                    new Wire("ItemScannerScreen", "screenRenderer", "Mesh_Terminal_Scanner_Screen", Kind.Renderer),
                    new Wire("ItemScannerArtifact", "dial", "Mesh_Terminal_Scanner_Dial", Kind.Transform),
                    new Wire("ItemScannerArtifact", "antenna", "Mesh_Terminal_Scanner_Antenna", Kind.Transform),
                },
                new[]
                {
                    // The radar display. Slot 0 of the plate is the CRT face in the .blend; here it
                    // is the shader ItemScannerScreen writes its blips into.
                    new Paint("Mesh_Terminal_Scanner_Screen", 0,
                              "Assets/Game/Art/Materials/Items/ItemScannerScreen.mat"),
                }),

            new("RuinScanner.prefab", "gauntlet_ruin_scanner.fbx",
                Array.Empty<string>(),
                new[] { new Wire("RuinScannerArtifact", "muzzle", "Emitter", Kind.Transform) }),
        };

        [MenuItem("Tools/SpaceGame/Items/Reseat Gauntlets On The Base")]
        public static void Apply()
        {
            var log = new StringBuilder();
            int done = 0;

            foreach (Gauntlet g in Roster)
                if (ApplyOne(g, log)) done++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Verify(log);

            Debug.Log($"[{LogTag}] Reseated {done} of {Roster.Length} gauntlets.\n{log}");
        }

        private static bool ApplyOne(Gauntlet g, StringBuilder log)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(g.Model);
            if (model == null)
            {
                log.AppendLine($"  {g.Prefab}: no model at {g.Model} — run its export script first.");
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(g.Prefab);
            if (contents == null)
            {
                log.AppendLine($"  {g.Prefab}: missing from disk.");
                return false;
            }

            try
            {
                // Collect first, destroy after: destroying while iterating a Transform skips
                // siblings, which is how half a hierarchy survives a "delete everything" loop.
                var doomed = new List<GameObject>();
                foreach (Transform child in contents.transform)
                    if (!g.Keep.Contains(child.name))
                        doomed.Add(child.gameObject);
                foreach (GameObject child in doomed) UnityEngine.Object.DestroyImmediate(child);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(contents.transform, false);

                // The grip point is the wrist joint, which is the model's own origin — the frame
                // the whole family is authored in. Held in the hand (a gauntlet may sit in the
                // hotbar, where it cannot be used) that puts the cuff opening in the palm.
                var grip = new GameObject("GripPoint");
                grip.transform.SetParent(contents.transform, false);

                GauntletPrefab.MakeWorn(contents, grip.transform, instance.transform);

                foreach (Wire wire in g.Wires) Connect(contents, instance.transform, wire, g.Prefab, log);
                foreach (Paint paint in g.Paints) Repaint(instance.transform, paint, g.Prefab, log);
                MatchHookHead(contents, instance.transform, log);

                PrefabUtility.SaveAsPrefabAsset(contents, g.Prefab);
                log.AppendLine($"  {g.Prefab}: on {System.IO.Path.GetFileName(g.Model)}.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void Connect(GameObject root, Transform model, Wire wire, string prefab,
                                    StringBuilder log)
        {
            Component target = root.GetComponentsInChildren<Component>(true)
                                   .FirstOrDefault(c => c != null && c.GetType().Name == wire.Component);
            if (target == null)
            {
                log.AppendLine($"  {prefab}: no {wire.Component} to wire '{wire.Node}' into.");
                return;
            }

            Transform node = GauntletPrefab.FindDeep(model, wire.Node);
            if (node == null)
            {
                log.AppendLine($"  {prefab}: the model has no '{wire.Node}'; {wire.Component}.{wire.Field} left as it was.");
                return;
            }

            object value = wire.As switch
            {
                Kind.GameObject => node.gameObject,
                Kind.Renderer => (object)node.GetComponent<Renderer>(),
                _ => node,
            };

            if (value == null)
            {
                log.AppendLine($"  {prefab}: '{wire.Node}' has no Renderer for {wire.Component}.{wire.Field}.");
                return;
            }

            GauntletPrefab.SetPrivate(target, wire.Field, value);
        }

        /// <summary>
        /// Make the harpoon that FLIES the same size as the one sitting in the tube.
        ///
        /// <para>
        /// The grapple has two harpoons: <c>Mesh_GrappleHarpoon</c> inside the gauntlet's model,
        /// hidden the moment a shot leaves, and <c>GrappleHarpoon.prefab</c>, spawned in its place
        /// and flown to the anchor. They come from the same component file at different scales —
        /// the seated one is squeezed to whatever the launch tube and the fold envelope allow —
        /// and the artifact reconciles them with <c>hookHeadScale</c>.
        /// </para>
        /// <para>
        /// Derived here rather than typed on the prefab because it is a fact about the MODEL: the
        /// day someone re-exports the gauntlet with a different harpoon scale, a hard-coded number
        /// makes the hook change size in mid-air, and the only place that shows is a screenshot.
        /// </para>
        /// </summary>
        private static void MatchHookHead(GameObject root, Transform model, StringBuilder log)
        {
            var artifact = root.GetComponentInChildren<GrapplingHookArtifact>(true);
            if (artifact == null) return;

            var so = new SerializedObject(artifact);
            var flying = so.FindProperty("hookHeadPrefab")?.objectReferenceValue as GameObject;
            Transform seated = GauntletPrefab.FindDeep(model, "Mesh_GrappleHarpoon");
            if (flying == null || seated == null) return;

            float seatedLength = LongestAxis(seated);
            float flyingLength = LongestAxis(flying.transform);
            if (seatedLength < 1e-4f || flyingLength < 1e-4f)
            {
                log.AppendLine("  GrapplingHook: could not measure a harpoon; hookHeadScale left alone.");
                return;
            }

            GauntletPrefab.SetPrivate(artifact, "hookHeadScale", seatedLength / flyingLength);
            log.AppendLine($"  GrapplingHook: hookHeadScale {(seatedLength / flyingLength):F3} " +
                           $"(seated {seatedLength:F3} m against a {flyingLength:F3} m head).");
        }

        /// <summary>The longest world-space extent of everything a transform draws.</summary>
        private static float LongestAxis(Transform t)
        {
            var bounds = new Bounds();
            bool any = false;

            foreach (var filter in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;

                Bounds local = filter.sharedMesh.bounds;
                Vector3 scale = filter.transform.lossyScale;
                var scaled = new Bounds(
                    t.InverseTransformPoint(filter.transform.TransformPoint(local.center)),
                    new Vector3(Mathf.Abs(local.size.x * scale.x),
                                Mathf.Abs(local.size.y * scale.y),
                                Mathf.Abs(local.size.z * scale.z)));

                if (any) bounds.Encapsulate(scaled);
                else { bounds = scaled; any = true; }
            }

            return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 0f;
        }

        private static void Repaint(Transform model, Paint paint, string prefab, StringBuilder log)
        {
            Transform node = GauntletPrefab.FindDeep(model, paint.Node);
            var renderer = node != null ? node.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                log.AppendLine($"  {prefab}: no renderer '{paint.Node}' to paint.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(paint.Material);
            if (material == null)
            {
                log.AppendLine($"  {prefab}: no material at {paint.Material}.");
                return;
            }

            Material[] slots = renderer.sharedMaterials;
            if (paint.Index < 0 || paint.Index >= slots.Length)
            {
                log.AppendLine($"  {prefab}: '{paint.Node}' has {slots.Length} material slot(s), " +
                               $"so slot {paint.Index} does not exist. The model changed underneath this table.");
                return;
            }

            slots[paint.Index] = material;
            renderer.sharedMaterials = slots;
        }

        /// <summary>Read every prefab back off disk and check the wiring actually landed.</summary>
        private static void Verify(StringBuilder log)
        {
            foreach (Gauntlet g in Roster)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(g.Prefab);
                if (prefab == null) { log.AppendLine($"  VERIFY {g.Prefab}: gone."); continue; }

                var grip = prefab.GetComponent<ItemGrip>();
                var fit = prefab.GetComponent<GauntletFit>();
                if (grip == null || fit == null)
                {
                    log.AppendLine($"  VERIFY {g.Prefab}: not a worn gauntlet — the save was discarded.");
                    continue;
                }

                if (!Mathf.Approximately(fit.CuffScale, GauntletFit.DefaultCuffScale) ||
                    !Mathf.Approximately(fit.LengthScale, GauntletFit.DefaultLengthScale))
                    log.AppendLine($"  VERIFY {g.Prefab}: worn at {fit.CuffScale}x{fit.LengthScale}, " +
                                   "not the family's 1x1 — it will be the wrong size on the arm.");

                if (!Mathf.Approximately(grip.HoldSize, GauntletPrefab.HoldSize) ||
                    !Mathf.Approximately(grip.PackSize, GauntletPrefab.PackSize))
                    log.AppendLine($"  VERIFY {g.Prefab}: sizes are {grip.HoldSize}/{grip.PackSize}, " +
                                   $"not {GauntletPrefab.HoldSize}/{GauntletPrefab.PackSize}.");

                Transform model = prefab.transform.Find("Model");
                if (model == null) { log.AppendLine($"  VERIFY {g.Prefab}: no Model child."); continue; }

                foreach (Wire wire in g.Wires)
                    if (GauntletPrefab.FindDeep(model, wire.Node) == null)
                        log.AppendLine($"  VERIFY {g.Prefab}: '{wire.Node}' is not in the model.");

                foreach (Paint paint in g.Paints)
                {
                    Transform node = GauntletPrefab.FindDeep(model, paint.Node);
                    var renderer = node != null ? node.GetComponent<Renderer>() : null;
                    Material landed = renderer != null && paint.Index < renderer.sharedMaterials.Length
                        ? renderer.sharedMaterials[paint.Index] : null;

                    if (landed == null || AssetDatabase.GetAssetPath(landed) != paint.Material)
                        log.AppendLine($"  VERIFY {g.Prefab}: '{paint.Node}' slot {paint.Index} is " +
                                       $"{(landed != null ? landed.name : "empty")}, not {paint.Material}.");
                }
            }
        }
    }
}
