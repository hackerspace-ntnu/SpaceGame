using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists both compartments of the player's backpack.
    ///
    /// Lives on the player rather than on the pack. The pack is not an independent world object —
    /// <c>BackpackController</c> instantiates one per player in Awake and parents it to the back
    /// socket, deploying it moves that same instance rather than spawning another — so its contents
    /// belong to the player's record. Giving the pack its own record would create a second,
    /// competing copy of the same twenty-two slots.
    ///
    /// As with the hotbar, the component is only wiring: the format lives in
    /// <see cref="BackpackSaveCodec"/>, which takes a <see cref="BackpackContainer"/> directly so it
    /// can be tested without a controller, a socket and a pack prefab.
    /// </summary>
    [RequireComponent(typeof(BackpackController))]
    public class BackpackSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = BackpackSaveCodec.Key;

        private BackpackController controller;

        private BackpackController Controller =>
            controller != null ? controller : controller = GetComponent<BackpackController>();

        /// <summary>
        /// The pack's storage, or null before the controller has built one.
        ///
        /// Read through the controller rather than found in children: deploying unparents the pack
        /// into the world, so a child search would lose it at exactly the moment a player is most
        /// likely to be looking at its contents.
        /// </summary>
        private BackpackContainer Container => Controller != null ? Controller.Pack?.Container : null;

        public string SaveKey => Key;

        public object CaptureState()
        {
            BackpackContainer container = Container;
            if (container == null) return null;

            BackpackSaveCodec.State state = BackpackSaveCodec.Capture(container);

            // Where the pack IS, on top of what is in it. A pack left open on the sand came back on
            // its owner's shoulders with the right items inside — which reads as the save having
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

            BackpackContainer container = Container;
            if (container == null) return;

            BackpackSaveCodec.Restore(container, state, this);

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

        public struct State
        {
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

        public static State Capture(BackpackContainer container) => new()
        {
            // Inventory.GetItemIDs is already positional and already nulls out empty slots, which is
            // exactly the shape Restore reads back.
            strapItemIds = container.Get(BackpackCompartment.Strap).GetItemIDs(),
            mainItemIds = container.Get(BackpackCompartment.Main).GetItemIDs(),
        };

        /// <param name="context">Optional, only for routing warnings to the right object in the console.</param>
        public static void Restore(BackpackContainer container, JObject state, Object context = null)
        {
            if (container == null || state == null) return;

            RestoreCompartment(container.Get(BackpackCompartment.Strap), state["strapItemIds"] as JArray, context);
            RestoreCompartment(container.Get(BackpackCompartment.Main), state["mainItemIds"] as JArray, context);
        }

        private static void RestoreCompartment(Inventory inventory, JArray ids, Object context)
        {
            // A compartment absent from the payload keeps whatever it has. Clearing it would empty
            // the pack of anyone loading a save written before that compartment existed.
            if (inventory == null || ids == null) return;

            int size = inventory.GetSize();

            for (int i = 0; i < size; i++)
            {
                string id = i < ids.Count && ids[i]?.Type == JTokenType.String ? ids[i].Value<string>() : null;

                InventoryItem item = string.IsNullOrEmpty(id) ? null : Registry<InventoryItem>.Get(id);

                if (item == null && !string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[Save] Backpack item '{id}' is not in the registry — its slot " +
                                     "was left empty. Was the item asset deleted?", context);
                }

                inventory.RestoreSlot(i, item);
            }
        }
    }
}
