// What a kind of town IS: its shape, what stands in it, who lives there, and what they want.
//
// A PLAIN serializable class, not a ScriptableObject, so the ordinary way to configure a town is to
// drop TownGenerator on an empty GameObject and fill this in on the component. Needing to author an
// asset before you can place a single hut is friction with no payoff for a one-off town, and most
// towns are one-off. TownRecipeAsset exists for the other case — the same settings wanted in several
// places — and is entirely optional.
//
// NOT to be confused with SettlementPrefabConfig, which despite the name configures a tile
// generator for ONE monumental ruin, not a town. See GLOSSARY: "town" is a generated settlement of
// prefab instances; "settlement" is still the ruin generator's word.
//
// Nothing here configures NPC BEHAVIOUR, and that is deliberate. A people group is a prefab and a
// count; what the NPC does is whatever its prefab already does. Tasks, modules and goals belong on
// the prefab, where the agent system can see them.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Quests;

namespace SpaceGame.World.Towns
{
    [Serializable]
    public class TownRecipe
    {
        // ── Layout ───────────────────────────────────────────────────────────────

        [Header("Layout")]
        [Tooltip("Ring the first band of buildings sits on, in metres.")]
        public float innerRadius = 22f;

        [Tooltip("Second ring.")]
        public float midRadius = 36f;

        [Tooltip("Outermost ring. Also the default outer edge for any group with no band of its own.")]
        public float outerRadius = 50f;

        [Tooltip("Floor on the gap between any two clearance-reserving things, centre to centre. " +
                 "Raise this and press Generate again to push the same buildings further apart.")]
        public float minStructureSpacing = 8f;

        [Tooltip("Footprint assumed for a prefab with no meshes to measure.")]
        public float defaultFootprint = 14f;

        [Tooltip("Extra gap added to a measured footprint before spacing is checked.")]
        public float buildingPadding = 2f;

        [Tooltip("How many times to re-roll a position that lands on top of something before giving up.")]
        public int maxPlacementAttempts = 40;

        [Tooltip("Space clearance-reserving groups evenly around their band instead of scattering " +
                 "them randomly inside it. Gives a planned town rather than a shanty.")]
        public bool organizedLayout = true;

        [Tooltip("Random in/out wobble per slot when organised, in metres. Keep small or the ring stops reading.")]
        public float slotRadialJitter = 0.5f;

        [Tooltip("Random around-the-ring wobble per slot when organised, in degrees.")]
        public float slotAngularJitter = 2f;

        // ── Ground ───────────────────────────────────────────────────────────────

        [Header("Ground")]
        [Tooltip("Lay a pad when the ground under a building's footprint drops this far below its base.")]
        public float foundationPadThreshold = 0.35f;

        [Tooltip("How far a pad sticks out past the building on every side.")]
        public float foundationPadOverhang = 0.5f;

        [Tooltip("Material for foundation pads. Unset uses the default cube material, which will look wrong.")]
        public Material foundationPadMaterial;

        // ── Contents ─────────────────────────────────────────────────────────────

        [Header("Centrepiece")]
        [Tooltip("One thing dead in the middle, on no ring. The landmark the town is read by from " +
                 "a distance — leave it empty and every town of this kind looks like every other " +
                 "from the ridge line.")]
        public GameObject centrepiecePrefab;

        [Header("Buildings")]
        public List<TownGroup> buildings = new();

        [Header("Props")]
        public List<TownGroup> props = new();

        [Header("Scatter — ship scrap and items")]
        public List<TownGroup> scatter = new();

        [Header("People")]
        [Tooltip("Prefab and count. Nothing else — behaviour is the prefab's business.")]
        public List<TownGroup> people = new();

        // ── Quests ───────────────────────────────────────────────────────────────

        [Header("Questlines")]
        [Tooltip("Each is handed to one randomly chosen placed NPC that can hold a conversation. " +
                 "More questlines than eligible NPCs is refused rather than silently dropped.")]
        public List<Questline> questlines = new();

        // ── Territory ────────────────────────────────────────────────────────────

        [Header("Territory")]
        [Tooltip("Put a SettlementAlarm on the town root: it notices hostiles inside its radius, " +
                 "sirens, and hands the nearest intruder to idle defenders.")]
        public bool wireAlarm = false;

        [Tooltip("Put a SettlementPopulation on the town root: it tops the town back up to its cap " +
                 "a wave at a time. Its inhabitant table is DERIVED from the People groups, so " +
                 "there is no second list to keep in step.")]
        public bool wirePopulation = false;

        [Tooltip("Whose town this is. Required by both of the above.")]
        public FactionDefinition ownerFaction;

        [Tooltip("The stance table both of the above resolve through. Required by both.")]
        public FactionRelationshipTable relationshipTable;

        [Tooltip("Metres past the outer radius at which someone counts as inside the town.")]
        public float alarmMargin = 40f;

        [Tooltip("The town stops spawning once this many of its own are alive inside it.")]
        public int populationCap = 12;

        [Tooltip("Seconds between reinforcement waves.")]
        public float populationInterval = 60f;

        // ── Derived ──────────────────────────────────────────────────────────────

        /// <summary>The groups in a section, or null. Sections are emitted in enum order.</summary>
        public List<TownGroup> GroupsFor(TownSection section) => section switch
        {
            TownSection.Building => buildings,
            TownSection.Prop     => props,
            TownSection.Scatter  => scatter,
            TownSection.Person   => people,
            _                    => null,
        };

        /// <summary>
        /// The band a group actually occupies. An unset band (both zero) means the whole town,
        /// which is the right default for scatter and for people.
        /// </summary>
        public Vector2 ResolveBand(TownGroup group)
        {
            if (group == null) return new Vector2(0f, outerRadius);

            if (Mathf.Approximately(group.band.x, 0f) && Mathf.Approximately(group.band.y, 0f))
                return new Vector2(0f, outerRadius);

            return new Vector2(Mathf.Min(group.band.x, group.band.y),
                               Mathf.Max(group.band.x, group.band.y));
        }

        /// <summary>
        /// Drag the obviously-impossible values back into range. A plain class gets no
        /// <c>OnValidate</c> of its own, so whoever owns the field calls this from theirs —
        /// <see cref="TownRecipeAsset"/> and <c>TownGenerator</c> both do.
        /// </summary>
        public void Normalize()
        {
            innerRadius = Mathf.Max(0.1f, innerRadius);
            midRadius = Mathf.Max(innerRadius + 0.1f, midRadius);
            outerRadius = Mathf.Max(midRadius + 0.1f, outerRadius);
            maxPlacementAttempts = Mathf.Max(1, maxPlacementAttempts);
            defaultFootprint = Mathf.Max(0.1f, defaultFootprint);
            populationInterval = Mathf.Max(1f, populationInterval);
        }
    }
}
