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

            /// <summary>
            /// What this one is drawn at on the pack mat, in the metres <c>ItemGrip.packSize</c> is
            /// authored in. <see cref="GauntletPrefab.PackSize"/> — zero, meaning the device's true
            /// size — for all but the ruin scanner.
            ///
            /// <para>
            /// It lives in the table because this script REWRITES the field: a size typed onto the
            /// prefab by hand is silently replaced with the family default on the next reseat, and
            /// the only symptom is an item that grew back. Any divergence here must also be listed
            /// in <c>PackSizeTests</c>, which is what makes it a decision rather than a stray number.
            /// </para>
            /// </summary>
            public readonly float PackSize;

            /// <summary>
            /// How far round the forearm this one is worn from where its model puts it, in the
            /// degrees <c>GauntletFit.rollDegrees</c> is authored in. Zero for a model built in
            /// the family's frame, which is all of them but the item scanner.
            ///
            /// <para>
            /// In the table for the same reason the pack size is: this script REWRITES the field,
            /// so a roll typed onto the prefab by hand comes back as zero on the next reseat and
            /// the only symptom is a device that went back to the flank it was taken off.
            /// </para>
            /// </summary>
            public readonly float Roll;

            public Gauntlet(string prefab, string model, string[] keep, Wire[] wires,
                            Paint[] paints = null, float packSize = GauntletPrefab.PackSize,
                            float roll = 0f)
            {
                Prefab = Gadgets + prefab;
                Model = Models + model;
                Keep = keep;
                Wires = wires;
                Paints = paints ?? Array.Empty<Paint>();
                PackSize = packSize;
                Roll = roll;
            }
        }

        /// <summary>
        /// What the ruin scanner is drawn at on the pack mat, in the metres <c>packSize</c> is
        /// authored in — 0.58x the 0.389 m the artist built it at, and the only gauntlet that
        /// diverges.
        ///
        /// <para>
        /// At true size the device measured 4 x 5 cells, twenty of the rig's 255, for a gadget
        /// that is worn on a forearm. That is a hierarchy failure rather than a fit one
        /// (<c>GDC-L1-UX-0003</c>: rank by salience, and every element competes for attention) —
        /// nothing refused to hold it, it simply read as the most important thing on the mat. At
        /// 0.225 it is <b>2 x 3 = 6 cells</b>.
        /// </para>
        /// <para>
        /// <b>0.225 rather than the 0.2334 that is exactly 0.6x</b>, because the binding axis is
        /// not the one the number names. The device is 0.301 across against 0.389 along, so the
        /// width crosses its second cell at 0.2326 — 0.2334 costs a whole third column for 0.3%
        /// of overflow and draws the scanner loose inside 3 x 3. 0.225 sits 3% clear of that line
        /// and costs the same six cells any value between 0.19 and 0.2326 would. The precedent is
        /// <c>PackSizeTests.CellWhy</c>: a rule of thumb bends, a cell boundary does not.
        /// </para>
        /// </summary>
        private const float RuinScannerPackSize = 0.225f;

        /// <summary>
        /// How far round the forearm the item scanner's console is worn from where its model puts
        /// it: half a turn.
        ///
        /// <para>
        /// The .blend is hand-authored, and its 2026-09-03 edit stood the whole console on ONE
        /// flank — the model's +X, which <c>ForearmSeat</c>'s mirror makes the same flank of both
        /// arms. The lead wants the other one (user, 2026-09-06: <i>"mounted on the wrong side of
        /// the arm. Rotate it 180 degrees and move its attachment point so it sits on the opposite
        /// side of the wrist"</i>), and half a turn about the arm axis is both halves of that at
        /// once: the console swings across to the far flank at the same distance from the bone,
        /// carrying its own mount with it.
        /// </para>
        /// <para>
        /// <b>A roll rather than a mirror, and the display survives it</b> — the axis runs along
        /// the arm, so the screen plate's own up (which points at the hand) does not move, and a
        /// rotation cannot change the handedness <c>ItemScannerScreen</c> measures for
        /// <c>_FlipX</c>. The reader still sees the plate face-on, the same way up.
        /// </para>
        /// <para>
        /// Here rather than in the .blend because that file must not be regenerated — its
        /// generator no longer reproduces the hand edits — and turning a gauntlet about the arm is
        /// exactly what <c>GauntletFit.rollDegrees</c> exists for.
        /// </para>
        /// </summary>
        private const float ItemScannerRollDegrees = 180f;

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
                },
                // The one gauntlet worn turned off its model's own frame — see the constant.
                roll: ItemScannerRollDegrees),

            // The one gauntlet that is not drawn at its true size — see ScannerWhy in PackSizeTests
            // for the 0.225 and what it costs.
            new("RuinScanner.prefab", "gauntlet_ruin_scanner.fbx",
                Array.Empty<string>(),
                new[] { new Wire("RuinScannerArtifact", "muzzle", "Emitter", Kind.Transform) },
                packSize: RuinScannerPackSize),
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

                GauntletPrefab.MakeWorn(contents, grip.transform, instance.transform, g.PackSize, g.Roll);

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
                    !Mathf.Approximately(grip.PackSize, g.PackSize))
                    log.AppendLine($"  VERIFY {g.Prefab}: sizes are {grip.HoldSize}/{grip.PackSize}, " +
                                   $"not {GauntletPrefab.HoldSize}/{g.PackSize}.");

                if (!Mathf.Approximately(fit.RollDegrees, g.Roll))
                    log.AppendLine($"  VERIFY {g.Prefab}: worn rolled {fit.RollDegrees} deg about " +
                                   $"the arm, not {g.Roll} — it is on the wrong flank.");

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
