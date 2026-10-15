using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>Where a quest step should point: a character where it stands, or the place it lives while its chunk is not loaded.</summary>
    public static class QuestPlaces
    {
        /// <summary>
        /// The loaded actor <paramref name="actorId"/>'s position, else the site
        /// <paramref name="fallbackSiteId"/>'s. False when neither can be found.
        /// </summary>
        public static bool TryLocate(string actorId, string fallbackSiteId, out Vector3 position)
        {
            if (QuestActor.TryFind(actorId, out QuestActor actor))
            {
                position = actor.transform.position;
                return true;
            }

            return QuestSite.TryGet(fallbackSiteId, out position, out _);
        }

        /// <summary>Whether <paramref name="point"/> lies within the site's radius, flat distance.</summary>
        public static bool IsInside(Vector3 point, Vector3 site, float radius)
        {
            Vector3 delta = point - site;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }
    }
}
