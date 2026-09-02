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
    /// <see cref="PackSaveCodec"/>, which takes a <see cref="PackLayout"/> and a set of
    /// surfaces directly so it can be tested without a controller, a socket and a pack prefab.
    /// </summary>
    [RequireComponent(typeof(BackpackController))]
    public class BackpackSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "backpack";

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

            PackSaveCodec.State state = PackSaveCodec.Capture(pack.Layout);

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

            PackSaveCodec.Restore(pack.Layout, pack.Surfaces, pack.Shapes, state, this);

            if (state == null) return;

            var restored = state.ToObject<PackSaveCodec.State>(SaveSerializer.Serializer);
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

    /// <summary>
    /// The save format for a <see cref="PackContainer"/>'s contents, and the rules for reading it
    /// back.
    ///
    /// Shared by the backpack and the ship's inventory wall: the two write the same placement
    /// records under different keys, and the arithmetic that turns a record back into a layout has
    /// to be one piece of code, or two containers can disagree about what fits. The key each of
    /// them saves under is its own — see BackpackSaveable.Key and WallInventorySaveable.Key.
    /// </summary>
    public static class PackSaveCodec
    {
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

        /// <summary>
        /// The version <see cref="Capture"/> writes. Absent from a payload means "older than
        /// versioning", which for this format is v1 or v2.
        ///
        /// <para>
        /// Every version past v2 exists for exactly one reason: <see cref="PackPlacementRecord.u"/>
        /// and <c>v</c> are METRES, not normalised coordinates and not cell indices, so a move of
        /// <see cref="PackScale.Factor"/> restates every uv already on disk. v3 is the 1.5 rig of
        /// 2026-09-01; <b>v4 is the 1.05 rig of 2026-09-02</b>. <see cref="Restore"/> already falls
        /// back to first-fit when a stored spot is illegal, so nothing would be LOST without the
        /// migration — but the gear would quietly rearrange itself on the first load after the
        /// change, which is worse than losing it in one respect: the player cannot tell it
        /// happened until they go looking for something.
        /// </para>
        /// <para>
        /// The migration is one multiply and it is exactly cell-preserving: a face and the uvs on
        /// it scaled by the same factor put every item back on the cell it was saved on, which
        /// <c>PackScaleTests</c> asserts over every origin on an 8x8 face. So a save from any
        /// frame reopens with the gear exactly where the player left it.
        /// </para>
        /// </summary>
        public const int Version = 4;

        /// <summary>
        /// The last version whose uvs are in the original <see cref="PackScale.LegacyCell"/> frame.
        /// </summary>
        private const int LastUnscaledVersion = 2;

        /// <summary>
        /// The last version whose uvs are in the 2026-09-01 <see cref="PackScale.EnlargedCell"/>
        /// frame.
        /// </summary>
        private const int LastEnlargedVersion = 3;

        /// <summary>
        /// What to multiply a payload's stored uvs by to bring them into today's frame: today's
        /// cell over the cell that payload was written at.
        ///
        /// <para>
        /// Written as a ratio of cells rather than as a factor, because that is what it means and
        /// because it stays right when <see cref="PackScale.Factor"/> moves again: add the frame's
        /// cell to <see cref="PackScale"/>, add its version here, and every older save keeps
        /// working. A version this does not recognise — a file from a NEWER build — is left alone,
        /// which is the only safe answer: guessing a scale for a frame that does not exist yet
        /// would move gear that is already in the right place.
        /// </para>
        /// </summary>
        internal static float UvScaleFor(int version) =>
            version <= LastUnscaledVersion ? PackGrid.Cell / PackScale.LegacyCell
            : version <= LastEnlargedVersion ? PackGrid.Cell / PackScale.EnlargedCell
            : 1f;

        public struct State
        {
            /// <summary>
            /// Which frame the numbers below are in. See <see cref="Version"/>. Serialised as a
            /// plain int and absent from every file written before 2026-09-01, where the default
            /// <c>0</c> correctly reads as "older than the enlargement".
            /// </summary>
            public int version;

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

            return new State { version = Version, placements = records };
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
                // Every version stamped its uvs in the cell frame of the rig it was written on, so
                // the scale is today's cell over that one. Multiplying here — rather than letting
                // the illegal placements drop through to first-fit — is what makes an old save
                // reopen with every item exactly where the player left it. A payload with no
                // version at all is pre-2026-09-01, which the default 0 reads correctly. The v1
                // branch below needs none of this: it never stored a position to be wrong about.
                float uvScale = UvScaleFor(state.Value<int?>("version").GetValueOrDefault());

                foreach (JToken token in v2)
                    RestoreOne(layout, surfaces, shapes, token, uvScale, context);

                return;
            }

            RestoreLegacy(layout, surfaces, shapes, straps, context);
            RestoreLegacy(layout, surfaces, shapes, main, context);
        }

        /// <param name="uvScale">
        /// What to multiply the record's stored uv by to bring it into today's frame. 1 for a
        /// payload already written at <see cref="Version"/>; see <see cref="UvScaleFor"/> for
        /// anything older.
        /// </param>
        private static void RestoreOne(PackLayout layout, IReadOnlyList<PackSurface> surfaces,
                                       PackShapeLibrary shapes, JToken token, float uvScale,
                                       Object context)
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
                                   new Vector2(record.u, record.v) * uvScale,
                                   PackShapes.SnapYaw(item, shapes, record.yaw)))
                return;

            if (!PackContainer.TryArrange(layout, surfaces, item, shapes))
            {
                Debug.LogWarning($"[Save] Stored item '{item.itemName}' no longer fits anywhere on " +
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

                if (!PackContainer.TryArrange(layout, surfaces, item, shapes))
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
                Debug.LogWarning($"[Save] Stored item '{id}' is not in the registry — it was left " +
                                 "out of whatever was holding it. Was the item asset deleted?",
                                 context);
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
