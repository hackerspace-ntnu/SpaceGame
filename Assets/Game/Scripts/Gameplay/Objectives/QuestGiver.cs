using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Gives the crew a quest from something in the world: a resident's dialogue, a pickup, a door, a poster — wire
    /// any UnityEvent or call to <see cref="Give"/> — or, with a <see cref="radius"/>, the first time any member of
    /// the crew comes that close to this object. For a quest that should begin wherever its trigger fires, with
    /// nothing placed in a scene, set <see cref="ObjectiveChain.Trigger"/> on the quest instead.
    ///
    /// <para>
    /// Safe on any machine: a client's <see cref="Give"/> asks the server, which begins the quest for the whole
    /// crew; the radius is judged on the server alone, from replicated player positions. Holds nothing worth
    /// saving: the begun quest is saved by the director, and giving it again does nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestGiver : MonoBehaviour
    {
        [Tooltip("The quest to give. Must be in the ObjectiveDirector's quest catalog.")]
        [SerializeField] private ObjectiveChain quest;

        [Tooltip("Give the quest the first time any member of the crew is this close to this object, metres, over " +
                 "the ground. 0: only when Give is called.")]
        [SerializeField, Min(0f)] private float radius;

        [Tooltip("How often the server checks the radius, seconds.")]
        [SerializeField, Min(0.05f)] private float checkSeconds = 0.5f;

        private float nextCheck;

        /// <summary>ANY machine: gives the crew this quest, unless they already have it.</summary>
        public void Give()
        {
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (director != null) director.Give(quest);
        }

        private void Update()
        {
            if (radius <= 0f || !Network.Decides || Time.time < nextCheck) return;
            nextCheck = Time.time + checkSeconds;

            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (director == null || director.Find(quest) != null) return;

            if (director.Lookups.NearestCrewDistance(transform.position) > radius) return;

            director.Give(quest);

            // Refused — not in the catalog, or no story in this world: said once by the director, not every check.
            if (director.Find(quest) == null) enabled = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (radius <= 0f) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
