using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Lead a group of characters to a place — "lead the Oathkeepers to the hut". Met when somebody
    /// in the crew and every one of the escorted characters are inside the place at once.
    ///
    /// <para>
    /// The step only judges. Following is the characters' own behaviour, switched on for this step
    /// by a <see cref="QuestStateGate"/> on them, so they keep following after a reload with nothing
    /// saved here.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Escort Step")]
    public class EscortStep : ObjectiveStep
    {
        [Tooltip("The QuestSite id of the destination.")]
        [SerializeField] private string siteId;

        [Tooltip("The QuestActor ids of the characters being led.")]
        [SerializeField] private string[] escortIds = System.Array.Empty<string>();

        [Tooltip("How close to a crew member an escorted character must be to count as 'with you', metres.")]
        [SerializeField, Min(1f)] private float withYouRadius = 15f;

        [Tooltip("Status line. {0} is how many are with the crew, {1} how many there are.")]
        [SerializeField] private string statusFormat = "With you  <b>{0}/{1}</b>";

        public override bool IsMet(ObjectiveWorld world)
        {
            if (!QuestSite.TryGet(siteId, out Vector3 site, out float radius)) return false;
            if (world.NearestCrew(site, out float crewDistance) == null || crewDistance > radius) return false;

            foreach (string id in escortIds)
                if (!QuestActor.TryFind(id, out QuestActor actor) || !QuestPlaces.IsInside(actor.transform.position, site, radius))
                    return false;

            return true;
        }

        public override string Status(ObjectiveWorld world)
        {
            int with = 0;
            foreach (string id in escortIds)
            {
                if (!QuestActor.TryFind(id, out QuestActor actor)) continue;
                if (world.NearestCrew(actor.transform.position, out float distance) != null && distance <= withYouRadius)
                    with++;
            }

            return string.Format(statusFormat, with, escortIds.Length);
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) =>
            QuestSite.TryGet(siteId, out position, out _);
    }
}
