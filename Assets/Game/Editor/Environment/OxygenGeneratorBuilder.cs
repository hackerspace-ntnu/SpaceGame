// Builds the oxygen plant prefab from the exported oxygen_generator.fbx.
//
// The model comes out of Blender via models/props/oxygen_generator_export.py: a wall-mounted
// machine with a round bottle collar, a rectangular cell slot, an amber lamp on its control head,
// a green readout on its base panel, and two 6 mm marker cubes whose ORIGINS are the docked poses.
// Everything above the meshes — the body collider, the two aim volumes, the OxygenGenerator, its
// saver and the light it casts — is generated here rather than hand-wired, for the reason the
// other builders exist: a prefab wired by hand is a prefab nobody can rebuild after the model
// changes.
//
// The prefab is a SHIP FIXTURE. It carries no NetworkObject of its own: nested under
// PlayerShip.prefab (PlayerShipBuilder.BuildOxygenGenerator) it inherits the ship's, which is what
// makes the generator's NetworkVariable and RPC replicate. Dropped into a chunk on its own it would
// need a NetworkObject added by whatever places it — the repair station is wrapped the same way.
//
// Run OxygenGearBuilder FIRST: the aim volumes are fitted to the items that go in the docks, so
// this builder measures the bottle and the cell off their own prefabs rather than restating their
// sizes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the prefab is
// rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Oxygen Generator Prefab
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class OxygenGeneratorBuilder
    {
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/OxygenGenerator.prefab";

        private const string Fbx = "Assets/Game/Art/Models/Props/oxygen_generator.fbx";

        // Part names as oxygen_generator.py exports them. The builder finds parts by these, so a
        // rename in the model script fails here, loudly, instead of shipping a machine whose lamp
        // never lights.
        private const string TankMarkerName = "Marker_OxyGen_TankDock";
        private const string CellMarkerName = "Marker_OxyGen_CellDock";
        private const string ControlHeadName = "Mesh_OxyGen_ControlHead";
        private const string BasePanelName = "Mesh_OxyGen_BasePanel";

        /// <summary>Seconds one bottle takes. The number the request asked for.</summary>
        private const float FillSeconds = 5f;

        /// <summary>
        /// Forgiveness added round each aim volume, in the model's own metres (so 1.7x that in the
        /// ship). Fixed rather than adaptive so the aim stays learnable.
        /// </summary>
        private const float DockAimPad = 0.05f;

        /// <summary>
        /// How far in front of the machine's own body box each aim volume must reach.
        ///
        /// <para>
        /// This is the whole reason the docks are reachable. The interaction ray takes the nearest
        /// hit, and the body box — solid, and with an interactable above it on the hull — answers
        /// for everything inside it. A trigger volume standing proud of that box is met first; one
        /// flush with it, or inside it, never is. The cell slot needs the push: a docked cell
        /// clears the machine's front face by 32 mm and nothing else.
        /// </para>
        /// </summary>
        private const float DockFrontClearance = 0.15f;

        /// <summary>Amber, matching the model's own <c>Mat_Emissive_Amber</c>.</summary>
        private static readonly Color LitColour = new Color(1f, 0.72f, 0.25f);

        /// <summary>Unpowered. Dark glass, not black — a black lamp reads as a hole.</summary>
        private static readonly Color DarkColour = new Color(0.10f, 0.08f, 0.06f);

        // The light the machine throws on the bulkhead beside it. A point light, no shadows: one
        // fixture in a lit cabin buys nothing from a shadow map and costs a lot.
        private const float LightRange = 2.5f;
        private const float LightIntensity = 1.6f;

        [MenuItem("Tools/SpaceGame/Build Oxygen System (items + generator)")]
        public static void BuildAll()
        {
            OxygenGearBuilder.Build();
            Build();
        }

        [MenuItem("Tools/SpaceGame/Build Oxygen Generator Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                Debug.LogError("[OxygenGenerator] No model at " + Fbx +
                               " — run oxygen_generator_export.py first.");
                return;
            }

            var drained = AssetDatabase.LoadAssetAtPath<InventoryItem>(OxygenGearBuilder.DrainedTankAsset);
            var charged = AssetDatabase.LoadAssetAtPath<InventoryItem>(OxygenGearBuilder.ChargedTankAsset);
            var cell = AssetDatabase.LoadAssetAtPath<InventoryItem>(OxygenGearBuilder.PowerCellAsset);

            if (drained == null || charged == null || cell == null)
            {
                Debug.LogError("[OxygenGenerator] The supply items are missing — run " +
                               "Tools/SpaceGame/Items/Build Oxygen Gear first. This builder " +
                               "measures the aim volumes off the items that go in the docks.");
                return;
            }

            var root = new GameObject("OxygenGenerator");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);

                Transform tankMarker = Find(visual, TankMarkerName);
                Transform cellMarker = Find(visual, CellMarkerName);
                Transform controlHead = Find(visual, ControlHeadName);
                Transform basePanel = Find(visual, BasePanelName);
                if (tankMarker == null || cellMarker == null ||
                    controlHead == null || basePanel == null) return;

                // The markers are build-time landmarks, not things to draw. Only their POSITION is
                // trusted: a Blender empty exports with whatever rotation and 100x scale it had in
                // the file, which is why nothing is ever parented to one.
                HideMarker(tankMarker);
                HideMarker(cellMarker);

                BoxCollider body = BuildBodyCollider(root, visual, tankMarker, cellMarker);

                // The bottle stands straight out of the wall, plugged in base first, gauge down.
                // MEASURED off the item — see PlugPose for why assuming its up is what broke this
                // once already.
                Quaternion tankPose = PlugPose(drained);

                // The cell lies on its back in the slot with its port toward the wall, which is the
                // pose it is modelled in — its charging port is on its own -Z and its charge ladder
                // on its +Z, so the machine's own front direction is the item's, unrotated.
                Quaternion cellPose = Quaternion.identity;

                Transform tankSeat = BuildDock(root, body, "TankDock",
                                               tankMarker.localPosition, tankPose, drained,
                                               OxygenGenerator.DockKind.Tank);
                Transform cellSeat = BuildDock(root, body, "CellDock",
                                               cellMarker.localPosition, cellPose, cell,
                                               OxygenGenerator.DockKind.Cell);
                if (tankSeat == null || cellSeat == null) return;

                Light light = BuildLight(root, controlHead);

                var generator = root.AddComponent<OxygenGenerator>();
                var so = new SerializedObject(generator);
                so.FindProperty("drainedTank").objectReferenceValue = drained;
                so.FindProperty("chargedTank").objectReferenceValue = charged;
                so.FindProperty("powerCell").objectReferenceValue = cell;
                so.FindProperty("tankSeat").objectReferenceValue = tankSeat;
                so.FindProperty("cellSeat").objectReferenceValue = cellSeat;
                so.FindProperty("fillSeconds").floatValue = FillSeconds;
                so.FindProperty("powerLight").objectReferenceValue = light;
                so.FindProperty("litColour").colorValue = LitColour;
                so.FindProperty("darkColour").colorValue = DarkColour;
                WireLamps(so, controlHead, basePanel);
                so.ApplyModifiedPropertiesWithoutUndo();

                // Both docks point back at the machine. Done after the component exists, because
                // the reference cannot be made before it does.
                foreach (OxygenGeneratorDock dock in root.GetComponentsInChildren<OxygenGeneratorDock>(true))
                {
                    var dockSo = new SerializedObject(dock);
                    dockSo.FindProperty("generator").objectReferenceValue = generator;
                    dockSo.ApplyModifiedPropertiesWithoutUndo();
                }

                // Baked in rather than left to SaveablePolicy, for the reason the repair station's
                // is: the runtime auto-attach only runs on an entity's ROOT, so a fixture nested
                // under the PlayerShip would never get its saver, and the cell somebody fitted
                // would be gone after every load. The policy's own add is null-guarded, so a
                // standalone placement is unaffected.
                root.AddComponent<OxygenGeneratorSaveable>();

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                // A read-only AssetDatabase (an MPPM clone) discards prefab saves without
                // erroring, so "saved" is only true once the file is on disk.
                if (!File.Exists(PrefabPath))
                {
                    Debug.LogError("[OxygenGenerator] " + PrefabPath + " did not reach disk — is " +
                                   "this a read-only editor clone?");
                    return;
                }

                // Wire it NOW, not at the next ship build. The wiring sweep gives every saveable
                // prefab a SaveableEntity and a TransformSaveable; PlayerShipBuilder strips both
                // off its nested instance (one entity per prefab, on the root) — but it can only
                // strip what exists when it nests. Left to the ship build's own sweep, which runs
                // AFTER the nesting, the two land on this prefab afterwards, the nested instance
                // inherits them, and the ship fails Verify with a second entity aboard. The import
                // first: the sweep finds prefabs through the search index, which does not yet hold
                // an asset saved in this same call.
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                if (!SaveableWiring.TryWirePrefabs())
                {
                    Debug.LogError("[OxygenGenerator] The save-wiring pass refused to run, so the " +
                                   "prefab has no entity for the ship builder to strip — rebuild " +
                                   "it stopped.");
                    return;
                }

                Verify();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ─────────────────────────── The machine ───────────────────────────

        private static Transform Find(GameObject visual, string name)
        {
            Transform found = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);

            if (found == null)
                Debug.LogError("[OxygenGenerator] No '" + name + "' in " + Fbx +
                               " — oxygen_generator.py names the parts this builder wires; " +
                               "re-export, or update both.");
            return found;
        }

        private static void HideMarker(Transform marker)
        {
            foreach (MeshRenderer renderer in marker.GetComponentsInChildren<MeshRenderer>(true))
                renderer.enabled = false;
        }

        /// <summary>
        /// One box over everything drawn, measured off the renderers rather than typed in, so a
        /// remodel that grows the machine grows the thing a player walks around. Measured with the
        /// root at the origin, so world bounds are local bounds.
        /// </summary>
        private static BoxCollider BuildBodyCollider(GameObject root, GameObject visual,
                                                     params Transform[] excluded)
        {
            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !excluded.Any(e => r.transform.IsChildOf(e)))
                .ToArray();

            Bounds bounds = renderers[0].bounds;
            foreach (MeshRenderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            return box;
        }

        /// <summary>
        /// One receptacle: a trigger volume fitted to the item that goes in it, and a seat holding
        /// the docked pose.
        ///
        /// <para>
        /// The volume is derived from the ITEM, so a remodelled bottle moves its own aim target,
        /// and it is then pushed clear of the machine's body box because the ray takes the nearest
        /// hit and the body box would otherwise answer for both docks (see
        /// <see cref="DockFrontClearance"/>).
        /// </para>
        /// <para>
        /// The seat is a child rather than the dock itself so the trigger box can be authored in
        /// the machine's own unrotated frame while the item still lands turned.
        /// </para>
        /// </summary>
        private static Transform BuildDock(GameObject root, BoxCollider body, string name,
                                           Vector3 markerPosition, Quaternion pose,
                                           InventoryItem item, OxygenGenerator.DockKind kind)
        {
            if (!TryMeasureItem(item, out Bounds itemBounds)) return null;

            var dock = new GameObject(name).transform;
            dock.SetParent(root.transform, false);
            dock.localPosition = markerPosition;

            var seat = new GameObject("Seat").transform;
            seat.SetParent(dock, false);
            seat.localRotation = pose;

            // Where the docked item sits, in the machine's frame.
            Bounds docked = Rotated(itemBounds, pose);
            docked.center += markerPosition;
            docked.Expand(DockAimPad * 2f);

            float wantedFront = body.center.z + body.size.z * 0.5f + DockFrontClearance;
            if (docked.max.z < wantedFront)
                docked.SetMinMax(docked.min,
                                 new Vector3(docked.max.x, docked.max.y, wantedFront));

            var trigger = dock.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = docked.center - markerPosition;
            trigger.size = docked.size;

            var component = dock.gameObject.AddComponent<OxygenGeneratorDock>();
            var so = new SerializedObject(component);
            so.FindProperty("dock").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[OxygenGenerator] " + name + " aim volume reaches z " +
                      docked.max.z.ToString("F3") + " against a body front of " +
                      wantedFront.ToString("F3") + " (clearance " +
                      DockFrontClearance.ToString("F2") + " m).");

            return seat;
        }

        /// <summary>
        /// The pose that plugs an item into the collar: standing straight out of the wall along
        /// the machine's +Z, BASE first, with its readable face turned down.
        ///
        /// <para>
        /// <b>All of it is measured, and every part of it has already been got wrong once.</b>
        /// This began as <c>FromToRotation(up, forward)</c>, because the bottle was modelled
        /// standing on its skirt and its length was its own up. Then the bottle was authored lying
        /// down — so that it lies flat on the pack's socket instead of standing 0.76 m out of the
        /// wearer's back — and that quietly turned a bottle plugged into the hatch into one lying
        /// against the machine. The size-only fix after it was still wrong: the bottle's long axis
        /// is its z, but it extends along MINUS z from its origin, so a rotation that only matched
        /// the axis plugged it straight into the wall.
        /// </para>
        /// <para>
        /// So: the direction the item extends from its own ORIGIN is the direction that goes into
        /// the collar, because an item is modelled with its origin at the end it stands on. Taken
        /// with its SIGN, from the offset between the origin and the middle of the bounds, with
        /// the longest axis as the fallback for anything pivoted at its centre.
        /// </para>
        /// <para>
        /// The roll comes from the gauge rather than from a constant, and it is built with
        /// <c>LookRotation</c> rather than a second <c>FromToRotation</c>: two vectors that are
        /// exactly opposite leave <c>FromToRotation</c> free to pick any perpendicular axis, so
        /// the roll would be whatever Unity felt like. This dock sits 2.7 m up, above a standing
        /// eye, so the face a player can read is the DOWNWARD one.
        /// </para>
        /// </summary>
        private static Quaternion PlugPose(InventoryItem item)
        {
            if (!TryMeasureItem(item, out Bounds bounds)) return Quaternion.identity;

            Vector3 along = ExtendDirection(bounds);
            Vector3 face = FaceDirection(item);

            // No gauge to aim: the plain rotation onto +Z, roll unspecified.
            if (face == Vector3.zero) return Quaternion.FromToRotation(along, Vector3.forward);

            // Inverse of a frame built FROM the item's axes, so it maps them onto the machine's:
            // along -> +Z (out of the wall) and the gauge -> -Y (down, where it can be read).
            return Quaternion.Inverse(Quaternion.LookRotation(along, -face));
        }

        /// <summary>
        /// Which way an item extends from its own origin — the end that goes in first is the
        /// opposite one. Signed, and the longest axis only as a fallback.
        /// </summary>
        private static Vector3 ExtendDirection(Bounds bounds)
        {
            Vector3 offset = bounds.center;
            Vector3 size = bounds.size;

            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float meaningful = longest * 0.1f;

            float ax = Mathf.Abs(offset.x), ay = Mathf.Abs(offset.y), az = Mathf.Abs(offset.z);

            if (ax >= ay && ax >= az && ax > meaningful) return new Vector3(Mathf.Sign(offset.x), 0f, 0f);
            if (ay >= az && ay > meaningful) return new Vector3(0f, Mathf.Sign(offset.y), 0f);
            if (az > meaningful) return new Vector3(0f, 0f, Mathf.Sign(offset.z));

            // Pivoted at its middle: the offset says nothing, so fall back to the longest axis.
            return size.x >= size.y && size.x >= size.z ? Vector3.right
                 : size.y >= size.z ? Vector3.up
                 : Vector3.forward;
        }

        /// <summary>
        /// Which way the item's readable face points, in the item's own frame: from the middle of
        /// the item to the middle of its gauge. Zero when it has none.
        /// </summary>
        private static Vector3 FaceDirection(InventoryItem item)
        {
            var supply = item.itemPrefab.GetComponent<DockableSupply>();
            if (supply == null || supply.Readout == null) return Vector3.zero;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.itemPrefab);
            try
            {
                Renderer gauge = instance.GetComponentsInChildren<Renderer>(true)
                    .FirstOrDefault(r => r.name == supply.Readout.name);

                if (gauge == null) return Vector3.zero;

                Bounds all = ItemBounds.Measure(instance, null);
                Vector3 local = instance.transform.InverseTransformPoint(gauge.bounds.center);
                Vector3 out_ = local - all.center;

                return out_.sqrMagnitude < 1e-8f ? Vector3.zero : out_.normalized;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The item's own local extents, measured once off its prefab. Instantiated rather than
        /// read off the asset because <c>ItemBounds</c> measures a hierarchy.
        /// </summary>
        private static bool TryMeasureItem(InventoryItem item, out Bounds bounds)
        {
            bounds = default;

            if (item == null || item.itemPrefab == null)
            {
                Debug.LogError("[OxygenGenerator] A dock's item has no prefab, so its aim volume " +
                               "cannot be fitted.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.itemPrefab);
            try
            {
                bounds = ItemBounds.Measure(instance, null);
                return bounds.size.sqrMagnitude > 1e-8f;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The axis-aligned box that contains <paramref name="bounds"/> turned by
        /// <paramref name="rotation"/> — the absolute-value rotation matrix, which is exact for the
        /// quarter turns the docks use and correct for anything else.
        /// </summary>
        private static Bounds Rotated(Bounds bounds, Quaternion rotation)
        {
            Matrix4x4 m = Matrix4x4.Rotate(rotation);
            Vector3 e = bounds.extents;

            var turned = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);

            return new Bounds(rotation * bounds.center, turned * 2f);
        }

        /// <summary>
        /// The two emissive submeshes that say whether the machine has power: the amber lamp on the
        /// control head and the green readout on the base panel.
        ///
        /// <para>
        /// Found by MATERIAL NAME, not by a typed index. These models are one mesh per PART and
        /// nine materials deep, so the emissive submesh's index is an accident of the export order
        /// — and a stale index paints the enamel instead of the lamp, which looks like a broken
        /// shader rather than like a wrong number.
        /// </para>
        /// </summary>
        private static void WireLamps(SerializedObject so, Transform controlHead, Transform basePanel)
        {
            var found = new List<KeyValuePair<Renderer, int>>();

            AddLamp(found, controlHead, "Mat_Emissive_Amber");
            AddLamp(found, basePanel, "Mat_Emissive_Green_CRT");

            SerializedProperty lamps = so.FindProperty("lamps");
            lamps.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
            {
                SerializedProperty entry = lamps.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("part").objectReferenceValue = found[i].Key;
                entry.FindPropertyRelative("materialIndex").intValue = found[i].Value;
            }
        }

        private static void AddLamp(List<KeyValuePair<Renderer, int>> into, Transform part,
                                    string materialName)
        {
            var renderer = part.GetComponent<Renderer>();
            if (renderer == null) return;

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].name.StartsWith(materialName)) continue;

                into.Add(new KeyValuePair<Renderer, int>(renderer, i));
                return;
            }

            Debug.LogWarning("[OxygenGenerator] " + part.name + " has no '" + materialName +
                             "' submesh, so that lamp is not wired.");
        }

        /// <summary>
        /// The light the machine casts, at the control head, standing out of its own front face so
        /// the wall behind it is lit rather than the inside of the mesh.
        /// </summary>
        private static Light BuildLight(GameObject root, Transform controlHead)
        {
            var renderer = controlHead.GetComponent<Renderer>();
            Vector3 at = renderer != null
                ? root.transform.InverseTransformPoint(renderer.bounds.center)
                : Vector3.up;

            var marker = new GameObject("PowerLight").transform;
            marker.SetParent(root.transform, false);
            marker.localPosition = at + Vector3.forward * DockFrontClearance;

            Light light = marker.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LitColour;
            light.range = LightRange;
            light.intensity = LightIntensity;
            light.shadows = LightShadows.None;

            // Authored OFF, and switched rather than dimmed: a URP light at zero intensity is
            // still a light the renderer sorts, and an unpowered machine must not glow.
            light.enabled = false;
            return light;
        }

        // ─────────────────────────── Proof ───────────────────────────

        /// <summary>
        /// Re-read what this run wrote, off disk, and assert it landed. Unity discards prefab saves
        /// outright when the AssetDatabase is read-only and says nothing, so a run that reports
        /// success having written nothing is a real outcome rather than a hypothetical one.
        /// </summary>
        private static bool Verify()
        {
            var problems = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (prefab == null)
            {
                Debug.LogError("[OxygenGenerator] NOT VERIFIED: no prefab at " + PrefabPath);
                return false;
            }

            var generator = prefab.GetComponent<OxygenGenerator>();
            if (generator == null) problems.Add("no OxygenGenerator on the root");
            else if (!Mathf.Approximately(generator.FillSeconds, FillSeconds))
                problems.Add("fillSeconds reads " + generator.FillSeconds.ToString("F2"));

            if (prefab.GetComponent<OxygenGeneratorSaveable>() == null)
                problems.Add("no OxygenGeneratorSaveable, so a fitted cell would not survive a load");

            var body = prefab.GetComponent<BoxCollider>();
            if (body == null || body.isTrigger) problems.Add("no solid body collider on the root");

            OxygenGeneratorDock[] docks = prefab.GetComponentsInChildren<OxygenGeneratorDock>(true);
            if (docks.Length != 2) problems.Add("expected 2 docks, found " + docks.Length);

            foreach (OxygenGeneratorDock dock in docks)
            {
                if (dock.Generator == null)
                    problems.Add(dock.name + " does not point at the generator");

                var trigger = dock.GetComponent<BoxCollider>();
                if (trigger == null || !trigger.isTrigger)
                {
                    problems.Add(dock.name + " has no TRIGGER collider, so the machine's own body " +
                                 "box answers the ray and the dock can never be aimed at");
                    continue;
                }

                if (body == null) continue;

                // The one geometric property the whole interaction depends on.
                float front = trigger.center.z + trigger.size.z * 0.5f + dock.transform.localPosition.z;
                float bodyFront = body.center.z + body.size.z * 0.5f;
                if (front < bodyFront)
                    problems.Add(dock.name + " reaches z " + front.ToString("F3") +
                                 " against a body front of " + bodyFront.ToString("F3") +
                                 " — it is inside the machine and unreachable");
            }

            int lights = prefab.GetComponentsInChildren<Light>(true).Length;
            if (lights != 1) problems.Add("expected exactly one Light, found " + lights);

            var entity = prefab.GetComponent<SaveableEntity>();
            if (entity == null)
                problems.Add("no SaveableEntity for the ship builder to strip — the wiring sweep " +
                             "did not run");

            if (problems.Count == 0)
            {
                Debug.Log("[OxygenGenerator] VERIFIED off disk: " + PrefabPath + ", two aim " +
                          "volumes clear of the body box, fillSeconds " + FillSeconds.ToString("F1") +
                          ", saver baked in. Next: Tools/Vehicles/Build PlayerShip Prefab.");
                return true;
            }

            Debug.LogError("[OxygenGenerator] NOT VERIFIED:\n  " + string.Join("\n  ", problems));
            return false;
        }
    }
}
