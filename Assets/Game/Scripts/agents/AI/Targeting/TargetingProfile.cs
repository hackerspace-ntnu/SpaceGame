// Tuning asset for AgentTargeting. One asset per agent archetype per context, so the same
// prefab can hunt cautiously in the open world and aggressively in the arena without forking it.
//
// Create via Assets > Create > Agents > Targeting Profile.
using UnityEngine;
using SpaceGame.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Agents
{
    [CreateAssetMenu(menuName = "Agents/Targeting Profile", fileName = "NewTargetingProfile")]
    public class TargetingProfile : ScriptableObject, IRegistryEntry
    {
        /// <summary>
        /// The asset's GUID, assigned by <see cref="OnValidate"/> — the same mechanism
        /// <c>InventoryItem</c> uses, for the same reason.
        ///
        /// <para>
        /// <see cref="AgentTargeting.ApplyProfile"/> is a runtime swap (MatchManager gives arena bots
        /// a more aggressive profile than the prefab ships with), so "which profile is live" is
        /// state, not authoring — and a save has to name it. A save file cannot hold an object
        /// reference, and a name or a list index would break the moment the asset was renamed or
        /// another profile was added, so it is keyed by GUID like every other ScriptableObject this
        /// project persists.
        /// </para>
        ///
        /// <para>
        /// <c>[field: SerializeField]</c> is load-bearing: <see cref="OnValidate"/> is editor-only,
        /// so without it the value is recomputed on every import and never written to the asset,
        /// which works in the editor and ships every profile with a null id.
        /// </para>
        ///
        /// <para>
        /// Empty for a profile built at runtime with <c>CreateInstance</c> — <c>MatchManager</c>'s
        /// fallback arena profile and <c>AgentTargeting</c>'s synthesised inline defaults. That is
        /// correct: an object that exists only for this session cannot be named in a file, and a save
        /// that mentions no profile simply leaves the prefab's own in place.
        /// </para>
        /// </summary>
        [field: SerializeField]
        public string ID { get; set; }

        /// <summary>
        /// Self-registration, so the save system can look a profile up by the id it stored.
        ///
        /// Runs when the asset is loaded, which is whenever anything referencing it is — a prefab
        /// being instantiated, or the scene holding MatchManager. Guarded on a non-empty id because
        /// <c>CreateInstance</c> also raises OnEnable, and registering an idless runtime instance
        /// would log a registry error for every agent in the world that has no profile assigned.
        /// </summary>
        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(ID))
                Registry<TargetingProfile>.Register(this);
        }

        [Header("Who")]
        [Tooltip("Relationship a candidate must have to this agent to be considered a target.")]
        public FactionRelationship relationship = FactionRelationship.Hostile;

        [Header("Ranges")]
        [Tooltip("Candidates beyond this are never scored. Set this to comfortably exceed the " +
                 "longest weapon range on the agent, or it will never acquire something it could shoot.")]
        public float acquisitionRange = 35f;

        [Tooltip("An acquired target is dropped once it passes this distance. Must exceed " +
                 "acquisitionRange or targets flicker at the boundary.")]
        public float loseRange = 45f;

        [Header("Cadence")]
        [Tooltip("Seconds between re-scoring candidates. This is not perception latency — the held " +
                 "target's distance and visibility refresh every frame regardless.")]
        public float reevaluateInterval = 0.5f;

        [Header("Memory")]
        [Tooltip("Seconds an unseen target stays acquired before the agent gives up on it. Only " +
                 "applies when requireLineOfSight is on and a PerceptionModule is present.")]
        public float memoryDuration = 6f;

        [Header("Perception")]
        [Tooltip("Require field-of-view + line-of-sight to ACQUIRE a new target. Once acquired, " +
                 "memoryDuration governs how long losing sight is tolerated. Turn this off for " +
                 "arena modes where everyone knows where everyone is.")]
        public bool requireLineOfSightToAcquire = true;

        [Tooltip("Acquire anything inside this radius regardless of field of view, so an agent " +
                 "reacts to something that walked up behind it.")]
        public float proximityAcquireRange = 4f;

        [Header("Scoring")]
        [Tooltip("How strongly the agent favours the target it already has, as a fraction of " +
                 "distance. 0.3 means the current target is treated as 30% closer than it is — a " +
                 "new candidate must be meaningfully better to steal focus. 0 = always take the " +
                 "nearest, which flip-flops between two equidistant enemies.")]
        [Range(0f, 0.9f)] public float currentTargetBias = 0.3f;

        [Tooltip("Same idea for whoever last damaged this agent — makes it turn on its attacker " +
                 "instead of continuing toward someone marginally closer.")]
        [Range(0f, 0.9f)] public float lastAttackerBias = 0.45f;

        [Tooltip("Penalty applied to candidates the agent cannot currently see, as a fraction of " +
                 "distance. 0.5 means an occluded candidate is treated as 50% further away.")]
        [Range(0f, 4f)] public float occludedPenalty = 0.75f;

        private void OnValidate()
        {
#if UNITY_EDITOR
            // An asset path only exists for something on disk; a CreateInstance profile keeps an
            // empty id, which reads back as "no profile was recorded".
            string path = AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (ID != guid)
                {
                    ID = guid;
                    EditorUtility.SetDirty(this);
                }
            }
#endif

            acquisitionRange = Mathf.Max(1f, acquisitionRange);
            loseRange = Mathf.Max(acquisitionRange, loseRange);
            reevaluateInterval = Mathf.Max(0.05f, reevaluateInterval);
            memoryDuration = Mathf.Max(0f, memoryDuration);
            proximityAcquireRange = Mathf.Clamp(proximityAcquireRange, 0f, acquisitionRange);
        }
    }
}
