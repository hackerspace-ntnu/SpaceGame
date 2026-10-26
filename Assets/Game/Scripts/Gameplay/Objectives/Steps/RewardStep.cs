using System;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The end of a favour: the character takes what was brought (if anything) and puts the reward
    /// on the ground in front of them — Merob's staff at his feet, a battery and an oxygen tank from
    /// the villager whose door was blocked. Finished the moment it has begun.
    ///
    /// <para>
    /// Rewards are ordinary pickups spawned through <see cref="ObjectiveWorld.Spawn"/>, so they
    /// replicate and are saved like anything dropped; <c>Begun</c> is saved, so a reload never pays
    /// twice. Waits ("not yet") while the character's chunk is not loaded on the server.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Reward Step")]
    public class RewardStep : ObjectiveStep
    {
        [Tooltip("The QuestActor id of who gives the reward.")]
        [SerializeField] private string actorId;

        [Tooltip("Optional. A delivered item this character takes away first.")]
        [SerializeField] private InventoryItem takes;

        [Tooltip("How far from the character the delivered item may lie, metres.")]
        [SerializeField, Min(0.5f)] private float takeRadius = 3f;

        [SerializeField] private InventoryItem[] rewards = Array.Empty<InventoryItem>();

        [Tooltip("How far in front of the character the rewards are put down, metres.")]
        [SerializeField, Min(0.3f)] private float placeAhead = 1.2f;

        [Tooltip("Spacing between rewards, sideways, metres.")]
        [SerializeField, Min(0.1f)] private float spacing = 0.5f;

        [Tooltip("Height above the character's feet they are dropped from, metres.")]
        [SerializeField, Min(0f)] private float dropHeight = 0.5f;

        public override bool TryBegin(ObjectiveWorld world)
        {
            if (!QuestActor.TryFind(actorId, out QuestActor giver)) return false;

            Transform at = giver.transform;
            if (takes != null && LooseItems.TryFind(takes, at.position, takeRadius, out PickupableItem delivered, out _))
                GameServices.World.Despawn(delivered.gameObject);

            for (int i = 0; i < rewards.Length; i++)
            {
                if (rewards[i] == null) continue;

                float side = (i - (rewards.Length - 1) * 0.5f) * spacing;
                Vector3 drop = at.position + at.forward * placeAhead + at.right * side + Vector3.up * dropHeight;
                world.Spawn(rewards[i], drop, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            }

            return true;
        }

        public override bool IsMet(ObjectiveWorld world) => true;
    }
}
