using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists every hatch, ramp, canopy and hinged panel on a vehicle — anything an
    /// <see cref="ArticulatedPart"/> drives.
    ///
    /// <b>One saver for all of them, not one each.</b> A saver owns a key within its entity's bag, so
    /// a component-per-part design would need every part to invent a distinct key — and two parts that
    /// picked the same one would silently overwrite each other's state. Enumerating from the root
    /// instead means adding a part to a vehicle needs no wiring at all.
    ///
    /// <b>Keyed by hierarchy path, not by index.</b> An index into <c>GetComponentsInChildren</c> is
    /// whatever Unity's traversal happens to produce, so re-parenting one panel silently reassigns
    /// every part's saved state — a boarding ramp restoring a canopy's openness. A path is stable
    /// against anything but renaming the part.
    ///
    /// Restored instantly rather than animated. The parts were already in that pose when the player
    /// left; playing every hatch open on load would be a vehicle that greets you by unfolding itself.
    /// </summary>
    public class ArticulatedPartsSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "parts";

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Hierarchy path below this object → whether that part was open.</summary>
            public Dictionary<string, bool> open;
        }

        public object CaptureState()
        {
            ArticulatedPart[] parts = GetComponentsInChildren<ArticulatedPart>(true);
            if (parts.Length == 0) return null;

            var open = new Dictionary<string, bool>(parts.Length);

            foreach (ArticulatedPart part in parts)
            {
                if (part == null) continue;
                open[PathOf(part.transform)] = part.IsOpen;
            }

            return new State { open = open };
        }

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            Dictionary<string, bool> open = state.ToObject<State>(SaveSerializer.Serializer).open;
            if (open == null) return;

            foreach (ArticulatedPart part in GetComponentsInChildren<ArticulatedPart>(true))
            {
                if (part == null) continue;

                // A path with no entry is left exactly as the prefab authored it — a part added since
                // the save was written, which must not be forced shut by its absence from the record.
                if (!open.TryGetValue(PathOf(part.transform), out bool wasOpen)) continue;

                part.SetOpen(wasOpen, instant: true);
            }
        }

        /// <summary>
        /// The part's path relative to this object, so the key does not change when the vehicle itself
        /// is renamed or moved.
        /// </summary>
        private string PathOf(Transform part)
        {
            var steps = new List<string>();

            for (Transform t = part; t != null && t != transform; t = t.parent)
                steps.Add(t.name);

            steps.Reverse();
            return string.Join("/", steps);
        }
    }
}
