using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what is on the player's backpack, and where.
    ///
    /// Lives on the player rather than on the pack. The pack is not an independent world object —
    /// <c>BackpackController</c> instantiates one per player in Awake and parents it to the back
    /// socket, deploying it moves that same instance rather than spawning another — so its contents
    /// belong to the player's record. Giving the pack its own record would create a second,
    /// competing copy of the same gear.
    ///
    /// As with the hotbar, the component is only wiring: the format lives in
    /// <see cref="BackpackSaveCodec"/>, which takes a <see cref="PackLayout"/> and a set of
    /// surfaces directly so it can be tested without a controller, a socket and a pack prefab.
    /// </summary>
    [RequireComponent(typeof(BackpackController))]
    public class BackpackSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = BackpackSaveCodec.Key;

        private BackpackController controller;

        private BackpackController Controller =>
            controller != null ? controller : controller = GetComponent<BackpackController>();

        /// <summary>
        /// The pack itself, or null before the controller has built one.
        ///
        /// Read through the controller rather than found in children: deploying unparents the pack
        /// into the world, so a child search would lose it at exactly the moment a player is most
        /// likely to be looking at its contents.
        /// </summary>
        private BackpackObject Pack => Controller != null ? Controller.Pack : null;

        public string SaveKey => Key;

        public object CaptureState()
        {
            BackpackObject pack = Pack;
            if (pack == null) return null;

            BackpackSaveCodec.State state = BackpackSaveCodec.Capture(pack.Layout);

            // Where the pack IS, on top of what is on it. A pack left open on the sand came back on
            // its owner's shoulders with the right items on it — which reads as the save having
            // half worked, because it had.
            //
            // SavedState is never mid-arc: it is the state the pack is on its way to, for the same
            // reason a joiner is told that rather than a frame of an animation.
            if (Controller != null)
            {
                state.deployed = Controller.SavedState == BackpackController.State.Open;

                if (state.deployed)
                {
                    Pose pose = Controller.SavedPose;
                    state.packPosition = pose.position;
                    state.packRotation = pose.rotation;
                }
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            pendingDeployed = false;

            BackpackObject pack = Pack;
            if (pack == null) return;

            BackpackSaveCodec.Restore(pack.Layout, pack.Surfaces, pack.Shapes, state, this);

            if (state == null) return;

            var restored = state.ToObject<BackpackSaveCodec.State>(SaveSerializer.Serializer);
            if (!restored.deployed) return;

            pendingDeployed = true;
            pendingPose = new Pose(restored.packPosition, restored.packRotation);
        }

        private bool pendingDeployed;
        private Pose pendingPose;

        /// <summary>
        /// Set the pack back down, once there is ground under it.
        ///
        /// <para>
        /// Deferred for two reasons. The obvious one is that the chunk the pack was left in may
        /// still be streaming when the player's record is applied. The other is the trap recorded on
        /// <c>BackpackController.OnDisable</c>: a deploy that is interrupted lands the pack wherever
        /// its arc had got to, which during a load — when the player is disabled and re-enabled as
        /// scenes come and go around them — is a few centimetres behind their back. So the restore
        /// never starts an arc; it goes straight to the settled pose, and it does it late.
        /// </para>
        /// <para>
        /// Consumed on the first pass: unlike a rider reference, this names nobody. Re-applying it
        /// on a later pass would pick the pack up off wherever the player has since moved it and put
        /// it back where the file says.
        /// </para>
        /// </summary>
        public void OnLoadComplete()
        {
            if (!pendingDeployed || Controller == null) return;

            pendingDeployed = false;
            Controller.RestoreDeployState(BackpackController.State.Open, pendingPose);
        }
    }

    /// <summary>The backpack's save format and the rules for reading it back.</summary>
    public static class BackpackSaveCodec
    {
        public const string Key = "backpack";

        /// <summary>
        /// One item on the pack, as it goes to disk.
        ///
        /// <para>
        /// Plain floats rather than a <c>Vector2</c>, deliberately. Newtonsoft needs an explicit
        /// converter for every Unity vector type in this project — <c>Vector3</c> and
        /// <c>Quaternion</c> have one, and a <c>Vector2</c> without one serialises as a recursive
        /// mess of <c>normalized</c> and <c>magnitude</c> properties. Three floats sidestep the
        /// question entirely and read fine in a diff.
        /// </para>
        /// <para>
        /// The footprint is not stored. It is derived from the item's prefab, so it is recomputed
        /// on load — which is also what lets an item be resized in the editor without every save
        /// that mentions it going stale.
        /// </para>
        /// </summary>
        public struct PackPlacementRecord
        {
            public string itemId;

            /// <see cref="PackSurfaceId"/>. Persisted, so its values must never be renumbered.
            public byte surface;

            public float u;
            public float v;
            public float yaw;
        }

        public struct State
        {
            /// <summary>v2: every item, with the face and spot it was left on.</summary>
            public List<PackPlacementRecord> placements;

            /// <summary>
            /// v1: the two fixed-slot compartments, positional, empty slots as nulls.
            ///
            /// Written by no version of this codec any more and read by <see cref="Restore"/> only
            /// when <see cref="placements"/> is absent. They are kept on the struct rather than
            /// read straight off the JObject so that the migration is visible in the type, not
            /// buried in a string literal.
            /// </summary>
            public List<string> strapItemIds;
            public List<string> mainItemIds;

            /// <summary>
            /// True when the pack was standing open on the ground rather than on its owner's back.
            ///
            /// Shouldered is the default, so a save written before this field existed reads as
            /// false and restores a pack that is worn — which is what those saves meant.
            /// </summary>
            public bool deployed;

            /// <summary>Where it was set down. Only meaningful with <see cref="deployed"/>.</summary>
            public Vector3 packPosition;

            public Quaternion packRotation;
        }

        public static State Capture(PackLayout layout)
        {
            var records = new List<PackPlacementRecord>();

            if (layout != null)
            {
                foreach (PackPlacement placement in layout.Placements)
                {
                    if (string.IsNullOrEmpty(placement.ItemId)) continue;

                    records.Add(new PackPlacementRecord
                    {
                        itemId = placement.ItemId,
                        surface = (byte)placement.Surface,
                        u = placement.Uv.x,
                        v = placement.Uv.y,
                        yaw = placement.Yaw,
                    });
                }
            }

            return new State { placements = records };
        }

        /// <param name="shapes">
        /// The pack's grid-shape config, or null for derived shapes throughout. Threaded in rather
        /// than looked up, because the shape an item occupies has to be the same answer here as it
        /// is on the pack that wrote the file.
        /// </param>
        /// <param name="context">Optional, only for routing warnings to the right object in the console.</param>
        public static void Restore(PackLayout layout, IReadOnlyList<PackSurface> surfaces,
                                   PackShapeLibrary shapes, JObject state, Object context = null)
        {
            if (layout == null || state == null) return;

            var v2 = state["placements"] as JArray;
            var straps = state["strapItemIds"] as JArray;
            var main = state["mainItemIds"] as JArray;

            // A payload that says nothing about contents keeps whatever the pack has. That is the
            // difference between "it was stored empty" and "this save predates the pack storing
            // anything", and getting it wrong empties the pack of everyone loading an old world.
            if (v2 == null && straps == null && main == null) return;

            layout.Clear();

            if (v2 != null)
            {
                foreach (JToken token in v2) RestoreOne(layout, surfaces, shapes, token, context);
                return;
            }

            RestoreLegacy(layout, surfaces, shapes, straps, context);
            RestoreLegacy(layout, surfaces, shapes, main, context);
        }

        private static void RestoreOne(PackLayout layout, IReadOnlyList<PackSurface> surfaces,
                                       PackShapeLibrary shapes, JToken token, Object context)
        {
            if (token == null || token.Type != JTokenType.Object) return;

            var record = token.ToObject<PackPlacementRecord>(SaveSerializer.Serializer);

            InventoryItem item = Resolve(record.itemId, context);
            if (item == null) return;

            var surfaceId = (PackSurfaceId)record.surface;
            PackSurface surface = Find(surfaces, surfaceId);

            // Placed back exactly where it was left, when that is still legal. It may not be: the
            // item can have been resized in the editor, or the rig re-authored with a narrower
            // face, since the save was written. Falling back to first-fit keeps the gear; refusing
            // would delete it, and deleting a player's gear on a load is not a failure mode worth
            // having for the sake of a centimetre.
            //
            // A uv written before the grid existed lands on the nearest cell rather than being
            // refused — PackLayout snaps everything it is handed, which is what lets a free-placed
            // save become a grid-placed one with no second migration and no new file version. A
            // yaw of 24 or 45 degrees from the old wheel becomes the nearest quarter turn the same
            // way; the item moves by a few centimetres and stays on the face it was left on.
            if (surface != null
                && layout.TryPlace(item.ID, surfaceId, surface.Size, PackShapes.For(item, shapes),
                                   new Vector2(record.u, record.v),
                                   PackShapes.SnapYaw(item, shapes, record.yaw)))
                return;

            if (!BackpackObject.TryArrange(layout, surfaces, item, shapes))
            {
                Debug.LogWarning($"[Save] Backpack item '{item.itemName}' no longer fits anywhere on " +
                                 "the pack and was dropped from the restore. Did a surface shrink, " +
                                 "or the item grow?", context);
            }
        }

        /// <summary>
        /// A v1 compartment: a positional list of item ids, nulls for empty slots, and no idea
        /// where anything was — there was nowhere to be, only a slot number.
        ///
        /// <para>
        /// So every item is arranged by the same first-fit search a world pickup uses. This is the
        /// single most important line in the migration: without it, every save written before free
        /// placement loads a pack that is silently, completely empty.
        /// </para>
        /// </summary>
        private static void RestoreLegacy(PackLayout layout, IReadOnlyList<PackSurface> surfaces,
                                          PackShapeLibrary shapes, JArray ids, Object context)
        {
            if (ids == null) return;

            foreach (JToken token in ids)
            {
                if (token == null || token.Type != JTokenType.String) continue;

                InventoryItem item = Resolve(token.Value<string>(), context);
                if (item == null) continue;

                if (!BackpackObject.TryArrange(layout, surfaces, item, shapes))
                {
                    Debug.LogWarning($"[Save] Backpack item '{item.itemName}' from an older save had " +
                                     "nowhere to go on the new surfaces and was dropped. The old pack " +
                                     "held 22 slots; the new one is limited by area.", context);
                }
            }
        }

        private static InventoryItem Resolve(string id, Object context)
        {
            if (string.IsNullOrEmpty(id)) return null;

            InventoryItem item = Registry<InventoryItem>.Get(id);

            if (item == null)
            {
                Debug.LogWarning($"[Save] Backpack item '{id}' is not in the registry — it was left " +
                                 "off the pack. Was the item asset deleted?", context);
            }

            return item;
        }

        private static PackSurface Find(IReadOnlyList<PackSurface> surfaces, PackSurfaceId id)
        {
            if (surfaces == null) return null;

            for (int i = 0; i < surfaces.Count; i++)
                if (surfaces[i] != null && surfaces[i].Id == id) return surfaces[i];

            return null;
        }
    }
}
