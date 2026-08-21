// The list that turns a storm profile into a byte and back.
//
// Same trick as SfxId/AudioCatalog: the wire carries an index, not an asset reference, because a
// ScriptableObject cannot be sent over the network. Anything a storm can be must appear in this
// list, and the order matters only for the duration of a session — nothing is persisted by index.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [CreateAssetMenu(menuName = "World/Sandstorm Catalog", fileName = "SandstormCatalog")]
    public class SandstormCatalog : ScriptableObject
    {
        [Tooltip("Every kind of storm this world can produce. A profile missing from this list " +
                 "cannot be spawned — the manager will say so rather than fail quietly.")]
        [SerializeField] private List<SandstormProfile> profiles = new List<SandstormProfile>();

        public int Count => profiles.Count;

        public SandstormProfile Get(byte index) =>
            index < profiles.Count ? profiles[index] : null;

        /// <summary>The profile's index, or -1 if it is not in the catalog.</summary>
        public int IndexOf(SandstormProfile profile)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] == profile)
                    return i;
            }

            return -1;
        }

        private void OnValidate()
        {
            // A byte index caps the catalog at 256, and a silently truncated list would spawn the
            // wrong storm rather than none — the worse of the two failures.
            if (profiles.Count > byte.MaxValue + 1)
                Debug.LogError($"[Sandstorm] {name} holds {profiles.Count} profiles; only the " +
                               "first 256 can be addressed over the network.", this);
        }
    }
}
