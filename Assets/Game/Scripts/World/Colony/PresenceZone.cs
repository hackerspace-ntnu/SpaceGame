using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// A trigger volume that knows who is inside it: which player bodies (anything with a <see cref="SuitOxygen"/>), and
    /// whether any agent body (a colonist) stands in it.
    ///
    /// <para>
    /// Every machine has every player's body, and each runs its own copy of this. The airlock asks it two
    /// things: whether anyone stands in a hatch's doorway (asked on the machine that decides, so a hatch is
    /// never shut on somebody), and whether the clicker stands in the chamber (asked on the clicker's own
    /// machine, where their body's position is the truth).
    /// </para>
    /// <para>
    /// Players are counted by body rather than by collider, because a player is several, from the trigger's own callbacks. An
    /// agent is found by asking the physics scene what overlaps the volume: a colonist is walked through a doorway by its motor,
    /// not by physics, so no callback is promised for it. The question is only asked when someone asks <see cref="Occupied"/>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PresenceZone : MonoBehaviour
    {
        private const int MaxOverlaps = 32;
        private static readonly Collider[] Overlaps = new Collider[MaxOverlaps];

        private readonly Dictionary<SuitOxygen, int> inside = new();
        private readonly List<SuitOxygen> gone = new();
        private Collider volume;

        private Collider Volume => volume != null ? volume : volume = GetComponent<Collider>();

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        /// <summary>The middle of the volume, in world space.</summary>
        public Vector3 Centre => Volume.bounds.center;

        /// <summary>Whether a player or an agent is in.</summary>
        public bool Occupied
        {
            get
            {
                DropGone();
                return inside.Count > 0 || AgentInside();
            }
        }

        /// <summary>Whether <paramref name="body"/> is in.</summary>
        public bool Contains(SuitOxygen body)
        {
            DropGone();
            return body != null && inside.ContainsKey(body);
        }

        /// <summary>
        /// Bodies that were destroyed or disabled since (a player who disconnected, a scene that unloaded) are
        /// dropped here, because no exit callback comes for them.
        /// </summary>
        private void DropGone()
        {
            gone.Clear();
            foreach (SuitOxygen body in inside.Keys)
                if (body == null || !body.isActiveAndEnabled) gone.Add(body);
            foreach (SuitOxygen body in gone) inside.Remove(body);
        }

        // Solid colliders only: an agent carries sensor triggers far wider than its body.
        private bool AgentInside()
        {
            int count = Overlap();
            for (int i = 0; i < count; i++)
            {
                AgentController agent = Overlaps[i].GetComponentInParent<AgentController>();
                if (agent != null && agent.isActiveAndEnabled) return true;
            }
            return false;
        }

        // The volume's own box where it is one (it may be turned with its building), else its world bounds.
        private int Overlap()
        {
            if (Volume is not BoxCollider box)
                return Physics.OverlapBoxNonAlloc(Volume.bounds.center, Volume.bounds.extents, Overlaps, Quaternion.identity,
                                                  Physics.AllLayers, QueryTriggerInteraction.Ignore);

            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), half, Overlaps, t.rotation, Physics.AllLayers,
                                              QueryTriggerInteraction.Ignore);
        }

        private void OnTriggerEnter(Collider other)
        {
            SuitOxygen body = other.GetComponentInParent<SuitOxygen>();
            if (body == null) return;
            inside.TryGetValue(body, out int count);
            inside[body] = count + 1;
        }

        private void OnTriggerExit(Collider other)
        {
            SuitOxygen body = other.GetComponentInParent<SuitOxygen>();
            if (body == null || !inside.TryGetValue(body, out int count)) return;
            if (count <= 1) inside.Remove(body);
            else inside[body] = count - 1;
        }

        private void OnDisable() => inside.Clear();
    }
}
