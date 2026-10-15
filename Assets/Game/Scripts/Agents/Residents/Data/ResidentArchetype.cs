// What kind of person a resident is: a role, where it works or what it leaves home for, and the two
// axes every reaction number is derived from. Six fields on purpose — everything a designer would
// otherwise tune per archetype is either derived from nerve and temper (Resident.ApplyDerivedTuning)
// or a row in the line table whose `speaker` is this asset's name.
using UnityEngine;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    [CreateAssetMenu(menuName = "SpaceGame/Residents/Archetype", fileName = "Archetype")]
    public sealed class ResidentArchetype : ScriptableObject
    {
        [Tooltip("Lower-case role word the {role} token speaks: smith, hunter, villager.")]
        public string roleName;

        [Tooltip("The job spot this archetype works at (a Work SpotUse). Set → Stationed.")]
        public SpotUse post;

        [Tooltip("What it leaves the settlement for. Set (and no post) → Outrider; neither → Roamer.")]
        public TripKind trips;

        [Tooltip("An errand it keeps up through its working day (watering, hauling). With a post it alternates with the work; " +
                 "with none it fills the day a roamer would spend strolling.")]
        public ChoreDefinition chore;

        [Tooltip("A standing assignment that replaces the working day. Patrol: walks the perimeter in a pair. Set → Stationed.")]
        public ResidentDuty duty;

        [Tooltip("Warns and then escalates against a player who goes armed or sprinting within sight, one band at a time. " +
                 "Never a fight on its own account: the meter still has to be filled.")]
        public bool challengesArmed;

        [Tooltip("Timid (0) to bold (1): stays in at a disturbance or walks out to look; asks or tells.")]
        [Range(0f, 1f)] public float nerve = 0.5f;

        [Tooltip("Warm (0) to prickly (1): hits it takes before fighting, how long it holds a grudge.")]
        [Range(0f, 1f)] public float temper = 0.5f;

        [Tooltip("Takes hold of a free cart standing by its post and keeps both hands on its handles while it works there. " +
                 "With no cart in reach it works empty-handed: nothing is mimed.")]
        public bool pushesCart;

        [Tooltip("What it carries in hand. Empty = nothing.")]
        public InventoryItem heldItem;

        [Tooltip("What it wears on its belt, in order. Items without a BeltMount are not drawn.")]
        public InventoryItem[] beltItems = System.Array.Empty<InventoryItem>();

        [Tooltip("What it can be on a band the settlement sends out. None = it only fills a slot any adult may fill.")]
        public ExpeditionRole expeditionRoles;

        /// <summary>Derived, never stored: a post makes it Stationed, trips an Outrider, else a Roamer.</summary>
        public Lifestyle Lifestyle =>
            post || duty != ResidentDuty.None ? Lifestyle.Stationed : trips != TripKind.None ? Lifestyle.Outrider : Lifestyle.Roamer;
    }
}
