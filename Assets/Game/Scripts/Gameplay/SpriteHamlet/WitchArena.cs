using System;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// The clearing the witch is fought in: where each keeper takes up its throwing post, where she
    /// is put back when the fight resets, and how far from its centre she may prowl.
    ///
    /// <para>
    /// Authored on the glade and FOUND by the fight's pieces — the witch is spawned at runtime and
    /// the keepers live in the hamlet, so neither prefab can reference a scene object. Every machine
    /// can find it, but only the server reads it: the posts drive the keepers' brains, and the home
    /// is where the server warps her.
    /// </para>
    /// </summary>
    public class WitchArena : MonoBehaviour
    {
        [Serializable]
        private struct KeeperPost
        {
            [Tooltip("The keeper's QuestActor id.")]
            public string keeperId;

            [Tooltip("Where it stands to throw. On the NavMesh, at the edge of the clearing.")]
            public Transform at;
        }

        private static readonly LiveSet<WitchArena> Live = new();

        [Tooltip("How far from this object's centre the witch may prowl, metres.")]
        [SerializeField, Min(2f)] private float radius = 16f;

        [Tooltip("Where she is put back when the fight resets: at her door.")]
        [SerializeField] private Transform witchHome;

        [Tooltip("One post per keeper, around the edge of the clearing.")]
        [SerializeField] private KeeperPost[] posts = Array.Empty<KeeperPost>();

        public float Radius => radius;

        public Vector3 Centre => transform.position;

        /// <summary>Her door, or the centre when none is authored.</summary>
        public Transform WitchHome => witchHome != null ? witchHome : transform;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>The nearest arena within <paramref name="searchRadius"/> of <paramref name="position"/>.</summary>
        public static bool TryFindNear(Vector3 position, float searchRadius, out WitchArena arena) =>
            Live.TryFindNearest(position, searchRadius, out arena);

        /// <summary>Where <paramref name="keeperId"/> throws from, if the arena names it.</summary>
        public bool TryGetPost(string keeperId, out Vector3 post)
        {
            foreach (KeeperPost entry in posts)
            {
                if (entry.at == null || entry.keeperId != keeperId) continue;

                post = entry.at.position;
                return true;
            }

            post = default;
            return false;
        }

        /// <summary><paramref name="point"/>, pulled back inside the prowling radius.</summary>
        public Vector3 Clamp(Vector3 point)
        {
            Vector3 offset = point - Centre;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radius * radius) return point;

            Vector3 inside = Centre + offset.normalized * radius;
            inside.y = point.y;
            return inside;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.9f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
            foreach (KeeperPost entry in posts)
                if (entry.at != null) Gizmos.DrawWireCube(entry.at.position + Vector3.up * 0.4f, Vector3.one * 0.8f);
        }
    }
}
