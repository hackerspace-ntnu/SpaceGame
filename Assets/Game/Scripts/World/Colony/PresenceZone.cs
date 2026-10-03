using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// A trigger volume that knows which player bodies are inside it. Players only (anything with a
    /// <see cref="SuitOxygen"/>), counted by body rather than by collider, because a player is several.
    ///
    /// <para>
    /// Every machine has every player's body, and each runs its own copy of this. The airlock asks it two
    /// things: whether anyone stands in a hatch's doorway (asked on the machine that decides, so a hatch is
    /// never shut on somebody), and whether the clicker stands in the chamber (asked on the clicker's own
    /// machine, where their body's position is the truth).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PresenceZone : MonoBehaviour
    {
        private readonly Dictionary<SuitOxygen, int> inside = new();
        private readonly List<SuitOxygen> gone = new();

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        /// <summary>Whether anyone is in.</summary>
        public bool Occupied
        {
            get
            {
                DropGone();
                return inside.Count > 0;
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
