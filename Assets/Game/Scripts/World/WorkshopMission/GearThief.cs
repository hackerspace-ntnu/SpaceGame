// One of the raiders who stole the workshop's Heart Gear: an outlaw with a bag. The carrier (IsCarrier) guards the
// gear at the camp, snatches it up when the camp is alarmed, and runs; knocked down or held, he loses his grip on it;
// killed, his loot table drops it. The band (GearThiefBand) decides all of that on the server; this is the body's side.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Items;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class GearThief : MonoBehaviour
    {
        [Tooltip("The one who guards the gear and runs with it.")]
        [SerializeField] private bool isCarrier;

        [Tooltip("The bag slot the gear is carried in. Not 0, which is the slot drawn into the hand.")]
        [SerializeField, Min(1)] private int carrySlot = 1;

        [Tooltip("How close to its post at the camp counts as there, in metres.")]
        [SerializeField, Min(0.2f)] private float postArriveRadius = 1f;

        private static readonly List<GearThief> Live = new();

        private EntityInventoryComponent bag;
        private FleeModule flee;
        private AgentRagdoll ragdoll;
        private AgentTargeting targeting;
        private HealthComponent health;
        private AgentGoal goal;

        public static IReadOnlyList<GearThief> All => Live;

        public bool IsCarrier => isCarrier;
        public bool IsAlive => health == null || health.Alive;

        /// <summary>Has somebody in its sights: the camp is alarmed.</summary>
        public bool IsAlarmed => targeting != null && targeting.HasTarget;

        /// <summary>Knocked down, netted or held: in no state to keep hold of anything.</summary>
        public bool IsDown => ragdoll != null && ragdoll.IsHeldOrDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        private void Awake()
        {
            bag = GetComponent<EntityInventoryComponent>();
            flee = GetComponent<FleeModule>();
            ragdoll = GetComponent<AgentRagdoll>();
            targeting = GetComponent<AgentTargeting>();
            health = GetComponent<HealthComponent>();
            goal = GetComponent<AgentGoal>();
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        // ── Server ───────────────────────────────────────────────────────────

        /// <summary>Server: keep to <paramref name="post"/> at the camp, facing the way it faces, until something calls it away.</summary>
        public void Guard(Pose post)
        {
            if (goal != null) goal.TrySetSampled(post.position, postArriveRadius, "gear thief post", hold: true, facePoint: post.position + post.forward * 5f);
        }

        public bool Holds(InventoryItem item)
        {
            if (bag == null || item == null) return false;
            for (int slot = 0; slot < bag.Size; slot++)
            {
                InventorySlot held = bag.GetSlot(slot);
                if (held != null && held.Item == item) return true;
            }
            return false;
        }

        /// <summary>Server: into the bag. False when it has none.</summary>
        public bool Take(InventoryItem item)
        {
            if (bag == null) return false;
            bag.RestoreSlot(carrySlot, item);
            return true;
        }

        /// <summary>Server: lets go of <paramref name="item"/>, which falls in front of it as a loose pickup.</summary>
        public void Drop(InventoryItem item)
        {
            for (int slot = 0; slot < bag.Size; slot++)
            {
                InventorySlot held = bag.GetSlot(slot);
                if (held == null || held.Item != item) continue;
                bag.TryRemoveItem(slot);
                GameServices.ItemDropService.DropItem(transform, item);
                return;
            }
        }

        /// <summary>Server: run, now, from whoever it has in its sights.</summary>
        public void Run()
        {
            if (flee != null) flee.Alarm();
        }
    }
}
