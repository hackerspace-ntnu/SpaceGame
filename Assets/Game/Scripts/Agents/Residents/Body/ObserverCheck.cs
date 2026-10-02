// "Would anybody see this happen?" — the one question a resident asks before it does something a
// player must never watch: step through a wall into its house, or skip across the settlement.
//
// Line of sight from each player's eye, through solid geometry only. Characters do not block:
// every NPC stands on Default, and a resident's own capsule sitting on the very point being asked
// about would otherwise answer "nobody can see me" for the one body somebody is looking straight at.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class ObserverCheck
    {
        // The player's camera rides 1.45 m above its root, the root ~1 m above the soles (PlayerCharacter.prefab).
        private const float EyeAboveRoot = 1.45f;
        private const int MaxOccluderHits = 16;

        private static readonly RaycastHit[] Hits = new RaycastHit[MaxOccluderHits];
        private static readonly List<Transform> Players = new List<Transform>();

        /// <summary>True when any player within <paramref name="radius"/> has line of sight to <paramref name="point"/>.</summary>
        public static bool AnyPlayerSees(Vector3 point, float radius)
        {
            float radiusSquared = radius * radius;

            SessionPlayers.Collect(Players);
            foreach (Transform player in Players)
                if (Sees(player, point, radiusSquared)) return true;

            return false;
        }

        private static bool Sees(Transform player, Vector3 point, float radiusSquared)
        {
            Vector3 eye = player.position + Vector3.up * EyeAboveRoot;
            Vector3 toPoint = point - eye;
            float distance = toPoint.magnitude;
            if (distance * distance > radiusSquared) return false;
            if (distance <= Mathf.Epsilon) return true;

            int count = Physics.RaycastNonAlloc(eye, toPoint / distance, Hits, distance,
                PerceptionModule.SolidGeometryLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
                if (Hits[i].collider.GetComponentInParent<AgentController>() == null) return false;

            return true;
        }
    }
}
