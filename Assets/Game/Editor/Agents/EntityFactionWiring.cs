// Getting EntityFaction onto every agent that needs one, and keeping it there.
//
// Without an EntityFaction an agent is invisible to EntityTargetRegistry and to every targeting
// module in the game, and it fails SILENTLY -- no warning, no error, just a creature nothing ever
// notices and that never notices anything. Six shipped agents were in exactly that state: the
// BountyHunter that the Bounty Hunters caravan template fields, both ostriches, the DesertCrawler,
// the HumanoidRobot and the CrabWalker6. A Clanker patrol walked past all of them.
//
// Five of the six have no builder at all -- they are hand-authored prefabs -- so this is a wiring
// pass in the mould of AgentGroundConformWiring, SaveableWiring and RagdollWiring: idempotent,
// re-runnable, and callable from inside a builder that overwrites its prefab wholesale (as
// DesertCrawlerBuilder does) so a rebuild cannot drop what this added.
//
// Re-run from: Tools > SpaceGame > Agents > Wire Entity Factions
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;

namespace SpaceGame.EditorTools
{
    public static class EntityFactionWiring
    {
        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        public const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        /// <summary>
        /// Which faction each unowned agent prefab belongs on, by prefab file name.
        ///
        /// <para>
        /// A declared list rather than a guess from the prefab's folder or its name: which side a
        /// creature is on is a design decision, and inferring it is how an animal ends up shooting
        /// at people. Anything not named here is left alone — an agent whose faction nobody has
        /// decided yet should keep failing loudly in review rather than quietly joining a side.
        /// </para>
        /// <para>
        /// The DesertCrawler is a Mechanics machine in the design and sits on the Sand Tribe until
        /// that faction asset exists, so it is at least targetable in the meantime.
        /// </para>
        /// </summary>
        private static readonly (string Prefab, string Faction)[] Assignments =
        {
            ("BountyHunter",  "OutlawFaction"),
            ("Ostrich",       "FaunaFaction"),
            ("DesertCrawler", "SandTribeFaction"),
            ("HumanoidRobot", "ClankerFaction"),
            ("CrabWalker6",   "ClankerFaction"),
        };

        // NomadOstrich is deliberately absent: it is a prefab VARIANT of Ostrich and inherits the
        // component from it. Listing it as well is how it ended up with two EntityFactions — the
        // Fauna one inherited from its base and a Sand Tribe one of its own — which is the same
        // silent duplicate the agent skill warns about for AgentTargeting. A caravan's mount is an
        // animal like any other; Clankers ignore animals by design (§3.2), so a nomad's ostrich
        // does not get shot out from under him on the strength of who is riding it.

        /// <summary>
        /// Put an <see cref="EntityFaction"/> and its saver on <paramref name="root"/> if the
        /// prefab is one this pass owns and does not have them yet. Answers whether anything
        /// changed, so a caller can skip a pointless prefab save.
        ///
        /// <para>
        /// The faction reference is written only on the frame the component is ADDED, so re-running
        /// never overwrites a side somebody has since changed in the Inspector — the same rule
        /// <see cref="AgentGroundConformWiring.Ensure"/> follows for its slope value.
        /// </para>
        /// </summary>
        public static bool Ensure(GameObject root, string prefabName)
        {
            if (root == null) return false;

            (string Prefab, string Faction) assignment =
                Assignments.FirstOrDefault(a => a.Prefab == prefabName);
            if (assignment.Prefab == null) return false;

            bool changed = false;

            // GetComponents, not GetComponent: a prefab VARIANT inherits its base's components, so
            // a second one added here is invisible in the variant's own file and silently doubles
            // the entity in EntityTargetRegistry. Counting is the only way to see it.
            EntityFaction[] existing = root.GetComponents<EntityFaction>();
            if (existing.Length > 1)
            {
                Debug.LogError($"[EntityFactionWiring] {prefabName} has {existing.Length} " +
                               "EntityFaction components. One of them is almost certainly inherited " +
                               "from a base prefab — remove the duplicate and take the prefab out of " +
                               "the Assignments list if it is a variant.", root);
                return false;
            }

            if (existing.Length == 0)
            {
                string path = $"{FactionDir}/{assignment.Faction}.asset";
                var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(path);
                var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);

                if (faction == null || table == null)
                {
                    Debug.LogError($"[EntityFactionWiring] {prefabName} wants {path} and " +
                                   $"{RelationshipsPath}; one of them is missing. Nothing written — " +
                                   "an EntityFaction with a null faction is the same silent " +
                                   "invisibility this pass exists to fix.", root);
                    return false;
                }

                // Added and filled in here rather than through EntityFaction.Ensure, which is the
                // RUNTIME repair path and warns that a prefab is missing the component — which is
                // the very thing this pass is in the middle of fixing.
                var component = root.AddComponent<EntityFaction>();
                var so = new SerializedObject(component);
                so.FindProperty("faction").objectReferenceValue = faction;
                so.FindProperty("relationshipTable").objectReferenceValue = table;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            // Which faction an entity is on is reassigned at runtime (the arena re-teams every bot),
            // so it has to be saved rather than re-read from the prefab. [RequireComponent] would
            // add one, but only on a component added through the Inspector.
            if (root.GetComponent<EntityFactionSaveable>() == null)
            {
                root.AddComponent<EntityFactionSaveable>();
                changed = true;
            }

            return changed;
        }

        [MenuItem("Tools/SpaceGame/Agents/Wire Entity Factions")]
        public static void WireAll()
        {
            int changed = 0;

            foreach ((string prefabName, string _) in Assignments)
            {
                string path = AssetDatabase.FindAssets($"t:Prefab {prefabName}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == prefabName);

                if (path == null)
                {
                    Debug.LogError($"[EntityFactionWiring] No prefab named {prefabName}. " +
                                   "It was renamed or deleted; fix the list in this file.");
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Ensure(contents, prefabName)) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);

                    // A read-only AssetDatabase discards a prefab save and says nothing at all.
                    if (!saved)
                    {
                        Debug.LogError($"[EntityFactionWiring] Could not save {path}. " +
                                       "The AssetDatabase refused the write.");
                        continue;
                    }

                    changed++;
                    Debug.Log($"[EntityFactionWiring] Gave {prefabName} an EntityFaction.", contents);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[EntityFactionWiring] {changed} of {Assignments.Length} prefabs updated.");

            // The savers this just added need prefab ids stamped, exactly as a builder's run does.
            if (changed > 0 && !SaveableWiring.TryWirePrefabs())
                Debug.LogError("[EntityFactionWiring] Wire Saveable Prefabs refused to run (Play mode?). " +
                               "The new EntityFactionSaveables have no prefabId yet.");
        }
    }
}
