// An animal a ranch keeps or rounds up, put on its prefab: the tame one in the yard, or one of the wild herd. A ranch
// finds its stock by kind and by distance rather than by a scene reference, because a creature follows the streaming
// grid between chunk scenes. Every machine lists the ones it has, so the herd's count on the visor and the waypoint to
// the nearest stray are worked out from replicated positions with nothing sent.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class RanchStock : MonoBehaviour
    {
        public enum Kind
        {
            /// <summary>Kept in the yard, to be won over, saddled and ridden.</summary>
            Tame,
            /// <summary>One of a herd turned loose on a range, to be roped and penned.</summary>
            Wild,
        }

        [SerializeField] private Kind kind;

        private static readonly List<RanchStock> Live = new();

        private HealthComponent health;
        private WanderModule wander;
        private bool homedInPen;

        /// <summary>Every ranch animal loaded on this machine.</summary>
        public static IReadOnlyList<RanchStock> All => Live;

        public Kind Of => kind;
        public bool IsAlive => health == null || health.Alive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            wander = GetComponent<WanderModule>();
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>
        /// SERVER: it is shut in a pen centred on <paramref name="penCentre"/> — it grazes there from now on, rather than
        /// pacing the fence on the side nearest the range it was caught on. Once per penning.
        /// </summary>
        public void KeepIn(Vector3 penCentre)
        {
            if (homedInPen || wander == null) return;
            wander.SetHome(penCentre);
            homedInPen = true;
        }

        /// <summary>SERVER: it is out of the pen again; the next penning homes it afresh.</summary>
        public void LetOut() => homedInPen = false;
    }
}
