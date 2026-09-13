using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Resizer Remote artifact: its prefab, its <see cref="InventoryItem"/> asset, its
    /// charge bar, and its entry in the network prefab list.
    ///
    /// A script rather than hand-authored YAML because the prefab nests an imported FBX, and the
    /// file ids Unity assigns inside a model are decided at import time — a hand-written prefab
    /// referencing guessed ids loads with a missing model and no error.
    ///
    /// Re-runnable, and re-running REPLACES the prefab wholesale. Tuning belongs in the numbers
    /// below, not in the Inspector.
    /// </summary>
    public static class ResizerRemoteBuilder
    {
        private const string ModelPath  = "Assets/Game/Art/Models/Items/resizer_remote.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ResizerRemote.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/ResizerRemote.asset";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>
        /// The beam material is the laser staff's, not a new one. `SpaceGame/LightningBeam`
        /// multiplies by vertex colour, so one shared asset serves every beam in the game and each
        /// one tints its own <see cref="LineRenderer"/> — a second material asset here would be a
        /// copy that has to be kept in step with the first for no gain.
        /// </summary>
        private const string BeamMatPath = "Assets/Game/Art/Materials/Weapons/LightningBeam.mat";

        /// <summary>Built by <c>OxygenGearBuilder</c>'s first run and shared by every bar since.</summary>
        private const string GaugeFillMatPath  = "Assets/Game/Art/Materials/Items/SupplyGaugeFill.mat";
        private const string GaugeTrackMatPath = "Assets/Game/Art/Materials/Items/SupplyGaugeTrack.mat";

        /// <summary>The plate the charge bar is laid over, by name in the FBX.</summary>
        private const string GaugePlateName = "Mesh_ResizerRemote_Gauge";

        /// <summary>Longest-axis size once held, in metres. A two-handed-looking handset carried
        /// one-handed, at the top of the hand-tool band.</summary>
        private const float HoldSize = 0.36f;

        /// <summary>
        /// How the handset sits in the closed hand, in the HAND's own frame — not in bone axes.
        ///
        /// <para>
        /// <b>Tune these here, never in the Inspector.</b> This builder replaces the prefab
        /// wholesale on its next run, so an offset dialled in on the asset survives exactly until
        /// somebody rebuilds and then vanishes with no error. Zero is the honest starting point
        /// (<c>ItemGrip</c>'s own convention: the item's +Z points where the back of the hand
        /// faces and +Y out the thumb side), and it has not been checked against a real rig,
        /// because that needs the editor open and a character to hold it.
        /// </para>
        /// </summary>
        private static readonly Vector3 GripRotationOffset = Vector3.zero;
        private static readonly Vector3 GripPositionOffset = Vector3.zero;

        /// <summary>How proud of the plate the track sits, and how thick the two boxes are. Small
        /// enough to read as a printed strip, large enough that neither z-fights the plate.</summary>
        private const float BarProud = 0.0006f;
        private const float BarThickness = 0.0010f;

        /// <summary>How far the fill is inset from the track across the bar, so the track reads as
        /// a frame round it rather than as a second bar behind it.</summary>
        private const float BarInset = 0.0012f;

        /// <summary>The handset starts charged: it is a tool the player is handed working.</summary>
        private const float StartingCharge = 1f;

        [MenuItem("Tools/Build Resizer Remote Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[ResizerRemote] No model at {ModelPath}. Run " +
                               "models/gear/resizer_remote_export.py first.");
                return;
            }

            var beamMat = AssetDatabase.LoadAssetAtPath<Material>(BeamMatPath);
            var fillMat = AssetDatabase.LoadAssetAtPath<Material>(GaugeFillMatPath);
            var trackMat = AssetDatabase.LoadAssetAtPath<Material>(GaugeTrackMatPath);
            if (beamMat == null || fillMat == null || trackMat == null)
            {
                // Both are built by other artifacts' first runs and shared since. Saying which is
                // missing beats a null reference forty lines further down.
                Debug.LogError("[ResizerRemote] Missing a shared material. Run Tools/Build Laser " +
                               $"Staff Artifact if {BeamMatPath} is absent, and the oxygen gear " +
                               $"build if {GaugeFillMatPath} is.");
                return;
            }

            GameObject root = BuildHierarchy(model, beamMat, fillMat, trackMat);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[ResizerRemote] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ResizerRemote] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model, Material beamMat,
                                                 Material fillMat, Material trackMat)
        {
            var root = new GameObject("ResizerRemote");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The markers exported with the mesh exist only to carry coordinates across the FBX;
            // lifted onto the root they become the emitter and the grip, and they are on the root
            // rather than under the model so a re-import cannot null them.
            Transform emitter = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                           "Marker_Emitter", "Emitter",
                                                           "ResizerRemote");
            Transform grip = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                        "Marker_Grip", "GripPoint",
                                                        "ResizerRemote");
            GauntletPrefab.HideRemainingMarkers(modelInstance.transform);

            LineRenderer beam = BuildBeam(root.transform, beamMat);
            Transform whip = GauntletPrefab.FindDeep(modelInstance.transform,
                                                     "Mesh_ResizerRemote_Whip");
            Transform knob = GauntletPrefab.FindDeep(modelInstance.transform,
                                                     "Mesh_ResizerRemote_Dial");
            var lamp = FindRenderer(modelInstance.transform, "Mesh_ResizerRemote_Lamp");
            var gaugePlate = FindRenderer(modelInstance.transform, GaugePlateName);

            // ── Pickup / world presence ──
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider fitted to the item's own mesh, the world sizing and the netcode
            // that lets another machine watch it be shoved about. Called AFTER the model is
            // parented, because it measures the meshes. Never write this block by hand.
            ItemWorldPresence.Apply(root);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip ──
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip);
            SetPrivate(itemGrip, "holdSize", HoldSize);
            SetPrivate(itemGrip, "sizeReference", modelInstance.transform);
            SetPrivate(itemGrip, "rotationOffset", GripRotationOffset);
            SetPrivate(itemGrip, "positionOffset", GripPositionOffset);

            // ── The battery, and the bar that reads it ──
            SupplyReservoir battery = AddBattery(root, gaugePlate);
            AddFillBar(root.transform, gaugePlate, fillMat, trackMat);

            // ── The handset ──
            var rig = root.AddComponent<ResizerRemoteRig>();
            SetPrivate(rig, "whip", whip);
            SetPrivate(rig, "knob", knob);
            SetPrivate(rig, "lamp", lamp);
            SetPrivate(rig, "beam", beam);
            SetPrivate(rig, "emitter", emitter);

            var artifact = root.AddComponent<ResizerRemoteArtifact>();
            SetPrivate(artifact, "rig", rig);
            SetPrivate(artifact, "battery", battery);

            // Unlimited on purpose: the battery is this item's only cost, and a charge count beside
            // it would delete the handset out of the inventory the first time it ran flat.
            SetPrivate(artifact, "maxUses", -1);
            SetPrivateEnum(artifact, "useSoundId", "InteractLever");

            return root;
        }

        /// <summary>
        /// The carrier beam: two points in world space, drawn by the shared lightning shader and
        /// tinted per instance by <see cref="ResizerRemoteRig"/>.
        ///
        /// Starts disabled. A handset lying in the sand with a beam coming out of it is the first
        /// thing anybody would notice, and the rig only ever switches it ON.
        /// </summary>
        private static LineRenderer BuildBeam(Transform root, Material material)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(root, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.02f;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        /// <summary>
        /// The handset's cell. A <see cref="SupplyKind.Power"/> reservoir, because that is what a
        /// radio runs on and because it is the kind the ship's own receptacles already accept — a
        /// handset that can be topped up at the bench is one more reason to go back to the ship.
        ///
        /// <para>
        /// Drain and refill are the item's whole cost: about twenty-two seconds of continuous
        /// transmission on a full cell, and a little over half a minute of not transmitting to fill
        /// it again. Four and a half full-strength resizes per charge, which is enough for the
        /// gadget to be worth carrying and far too few to hold somebody with.
        /// </para>
        /// </summary>
        private static SupplyReservoir AddBattery(GameObject root, Renderer gaugePlate)
        {
            var battery = root.AddComponent<SupplyReservoir>();

            SetPrivateEnum(battery, "kind", "Power");
            SetPrivate(battery, "capacity", 100f);
            SetPrivate(battery, "startingCharge", StartingCharge);
            SetPrivate(battery, "readout", gaugePlate);
            SetPrivate(battery, "drainPerSecond", 0.045f);
            SetPrivate(battery, "refillPerSecond", 0.030f);
            SetPrivate(battery, "refillDelay", 1.5f);

            // Hysteresis, not decoration: without it a cell that has just run dry allows one
            // frame's transmission, empties, and strobes for as long as the button is down. A fifth
            // of a charge is also the smallest burst worth handing back.
            SetPrivate(battery, "restartFraction", 0.2f);

            return battery;
        }

        /// <summary>
        /// Lay a charge bar over the handset's gauge plate: a dark track, and a fill that grows
        /// from one end of it. Both are plain boxes on the PREFAB ROOT — see <c>SupplyGauge</c> for
        /// why the runtime finds them by name rather than by component.
        ///
        /// <para>
        /// Measured off the plate's own bounds and <b>not</b> mirrored the way
        /// <c>OxygenGearBuilder.MeasureGauge</c> mirrors the bottle's and the battery's. That step
        /// exists because those two are hand-edited finals whose lit strip covers only part of a
        /// symmetric housing; this plate was built for this job in
        /// <c>models/gear/resizer_remote.py</c>, is the full instrument, and is already centred on
        /// itself — so mirroring it would be a correction for a problem it does not have.
        /// </para>
        /// <para>
        /// The plate is an axis-aligned slab and the root is at the origin while this runs, so its
        /// world AABB is its exact shape. The shallowest axis is the one pointing out of the model;
        /// the longest of the other two is the one the fill travels along.
        /// </para>
        /// </summary>
        private static void AddFillBar(Transform root, Renderer plate, Material fill, Material track)
        {
            if (plate == null)
            {
                Debug.LogWarning($"[ResizerRemote] No {GaugePlateName} in the FBX; the handset " +
                                 "ships without a charge bar.");
                return;
            }

            Bounds bounds = plate.bounds;
            Vector3 size = bounds.size;

            int outAxis = Smallest(size);
            int longAxis = Longest(size, outAxis);
            int acrossAxis = 3 - outAxis - longAxis;

            Vector3 outward = Axis(outAxis);
            Vector3 along = Axis(longAxis);

            // The plate is on the top of the case, so "out" is whichever way is away from the
            // model's own centre — a sign, not a guess.
            if (Vector3.Dot(outward, bounds.center - root.position) < 0f) outward = -outward;

            var rotation = Quaternion.LookRotation(outward, along);
            float length = size[longAxis];
            float width = size[acrossAxis];

            // The track first and the fill a step further out, so neither z-fights the plate nor
            // the other.
            Box(root, SupplyGauge.TrackName, track, bounds.center + outward * BarProud, rotation,
                new Vector3(length, width, BarThickness));

            // The anchor sits at the LOW end of the bar and is scaled along its own +X; the box
            // hangs off it by half a length so the pair grows from that end. Baked at the authored
            // starting charge, which is what makes the prefab, its generated icon and every
            // stripped display copy read correctly with no script having run.
            var anchor = new GameObject(SupplyGauge.AnchorName).transform;
            anchor.SetParent(root, false);
            anchor.position = bounds.center + outward * (BarProud + BarThickness)
                                            - rotation * Vector3.right * (length * 0.5f);
            anchor.rotation = rotation;
            anchor.localScale = new Vector3(StartingCharge, 1f, 1f);

            // Twice the track's thickness, so the fill is BURIED in it rather than resting on it.
            // Two parallel faces meeting exactly on a plane is a flicker the model scripts
            // themselves warn about, and the fill's back face would otherwise land precisely on the
            // track's front.
            Box(anchor, SupplyGauge.FillName, fill, new Vector3(length * 0.5f, 0f, 0f),
                Quaternion.identity, new Vector3(length,
                                                 Mathf.Max(width - BarInset * 2f, BarInset),
                                                 BarThickness * 2f));
        }

        private static void Box(Transform parent, string name, Material material,
                                Vector3 position, Quaternion rotation, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());

            box.transform.SetParent(parent, false);

            // Local when the parent is the anchor (whose own transform already carries the place),
            // world when it is the root — which is at the origin here, so the two agree.
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;

            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static int Smallest(Vector3 size) =>
            size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;

        private static int Longest(Vector3 size, int except)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
                if (i != except && (best < 0 || size[i] > size[best])) best = i;
            return best;
        }

        private static Vector3 Axis(int index) =>
            index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

        private static Renderer FindRenderer(Transform model, string name)
        {
            Transform found = GauntletPrefab.FindDeep(model, name);
            if (found != null) return found.GetComponent<Renderer>();

            Debug.LogWarning($"[ResizerRemote] No {name} in the FBX; the handset ships without it.");
            return null;
        }

        // ── Assets ─────────────────────────────────────────────────────────────

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Resizer Remote";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one of
        /// the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError("[ResizerRemote] PickupableItem missing."); return; }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        /// Netcode regenerates and nothing consults. A missing entry here fails on CLIENTS ONLY —
        /// the host instantiates its own copy and never opens the list — so solo testing cannot
        /// find it.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[ResizerRemote] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ── Reflection helpers ──
        //
        // Item components serialize private fields, which is right for runtime code and simply
        // means an editor script goes in the way the Inspector does. PickupableItem is additionally
        // internal to Assembly-CSharp, so it cannot be named from this assembly at all.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"[ResizerRemote] No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance |
                                                      BindingFlags.NonPublic | BindingFlags.Public |
                                                      BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }

            // Loud rather than silent: a renamed field would otherwise leave the prefab shipping
            // with an unwired reference and nothing in the console until somebody fires it.
            Debug.LogError($"[ResizerRemote] No field '{name}' on {target.GetType().Name}.");
            return null;
        }

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateEnum(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (field != null) field.SetValue(target, Enum.Parse(field.FieldType, enumValue));
        }
    }
}
