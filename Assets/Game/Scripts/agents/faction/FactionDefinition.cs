// ScriptableObject representing a single faction (e.g. Robots, BountyHunters, Player, Wildlife).
// Create via Assets > Create > Factions > Faction Definition.
using UnityEngine;
using UnityEngine.Serialization;
using SpaceGame.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Agents
{
    [CreateAssetMenu(menuName = "Factions/Faction Definition")]
    public class FactionDefinition : ScriptableObject, IRegistryEntry
    {
        /// <summary>
        /// The asset's GUID, assigned by <see cref="OnValidate"/> — the same mechanism
        /// <c>InventoryItem</c> and <see cref="TargetingProfile"/> use, for the same reason.
        ///
        /// <para>
        /// Which faction an entity belongs to is not authoring: <see cref="EntityFaction.SetFaction"/>
        /// is a runtime reassignment, and <c>MatchManager</c> re-teams every bot and player it spawns.
        /// So a save has to be able to name a faction, and a save file cannot hold an object
        /// reference. The display name is not usable as the key — it is a designer-facing string that
        /// is expected to change — and a list index moves the moment a faction is added.
        /// </para>
        /// <para>
        /// <c>[field: SerializeField]</c> is load-bearing: <see cref="OnValidate"/> is editor-only, so
        /// without it the value is recomputed on every import, never written to the asset, and every
        /// build ships with a null id.
        /// </para>
        /// </summary>
        [field: SerializeField]
        public string ID { get; set; }

        [Tooltip("Display name for this faction.")]
        public string factionName = "Unnamed Faction";

        [Tooltip("Stance toward any faction the relationship table has no row for.\n\n" +
                 "Leave this Neutral for almost everything. It exists so \"this lot are hostile to " +
                 "everyone\" is one checkbox instead of one row per faction — and, more importantly, " +
                 "one row per FUTURE faction that would otherwise be forgotten. Clankers are the " +
                 "only faction that sets it.\n\n" +
                 "A row always wins over this (design §3.2 step 2 before step 3), which is how " +
                 "\"Clankers shoot people but ignore animals\" is expressed: a Hostile default plus " +
                 "explicit Neutral rows toward Fauna and Wildlife.\n\n" +
                 "The \"peaceful = zero rows\" rule in the agent skill stays true for every faction " +
                 "that leaves this at Neutral.")]
        public FactionRelationship defaultStance = FactionRelationship.Neutral;

        [Tooltip("Colour for this faction in the visor readout, debug gizmos and editor tools.")]
        [FormerlySerializedAs("debugColor")]
        public Color hudColor = Color.white;

        [Tooltip("This faction's people, if it is a tribe. Empty for Humans, Outlaws, Clankers and " +
                 "animals — only a tribe fields caravans and war parties from a roster.")]
        public FactionRoster roster;

        /// <summary>
        /// Self-registration, so the save system can look a faction up by the id it stored. Runs when
        /// the asset is loaded, which is whenever anything referencing it is.
        ///
        /// Guarded on a non-empty id because <c>CreateInstance</c> raises OnEnable too, and
        /// registering an idless runtime instance would log a registry error.
        /// </summary>
        private void OnEnable() => Register(this);

        /// <summary>
        /// Put <paramref name="faction"/> in the registry if it can be named. Exposed to the assembly
        /// so <see cref="FactionRelationshipTable"/> can pull in every faction it mentions — see there
        /// for why that matters.
        /// </summary>
        internal static void Register(FactionDefinition faction)
        {
            if (faction != null && !string.IsNullOrEmpty(faction.ID))
                Registry<FactionDefinition>.Register(faction);
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // An asset path only exists for something on disk; a CreateInstance faction keeps an
            // empty id, which reads back as "no faction was recorded".
            string path = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path)) return;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (ID == guid) return;

            ID = guid;
            EditorUtility.SetDirty(this);
#endif
        }
    }
}
