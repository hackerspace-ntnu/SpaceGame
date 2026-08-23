using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>What kind of thing a contact is. The scanner draws each with its own glyph.</summary>
    public enum ScanClass
    {
        /// <summary>Loose salvage — a dropped or placed item somebody could pick up.</summary>
        Item = 0,

        /// <summary>Something holding items: a crate, a pack, a wreck worth opening.</summary>
        Container = 1,

        /// <summary>A place rather than an object — a ruin, a cache, a marked site.</summary>
        Site = 2,

        /// <summary>Worth knowing about but not worth picking up. Hazards, beacons, wrecks.</summary>
        Signal = 3,
    }

    /// <summary>
    /// Something the item scanner can find.
    ///
    /// Implement it on anything that should show up on the display, then register with
    /// <see cref="ScannerRegistry"/> for as long as it should be findable. Everything the scanner
    /// knows about arrives through this interface — there is no second path and no special case
    /// for the item types that happen to exist today.
    /// </summary>
    public interface IScanTarget
    {
        /// <summary>False to stay off the display without unregistering — a lid closed, a
        /// pickup already claimed, a beacon out of power.</summary>
        bool IsScannable { get; }

        /// <summary>Where the return comes from, in world space.</summary>
        Vector3 ScanPosition { get; }

        /// <summary>Which glyph the display draws.</summary>
        ScanClass ScanClass { get; }

        /// <summary>Human-readable name, for anything that wants to list contacts.</summary>
        string ScanLabel { get; }
    }

    /// <summary>One return, resolved against a particular scan.</summary>
    public readonly struct ScanContact
    {
        public readonly IScanTarget Target;
        public readonly Vector3 Position;
        public readonly float Distance;
        public readonly ScanClass Class;

        public ScanContact(IScanTarget target, Vector3 position, float distance, ScanClass cls)
        {
            Target = target;
            Position = position;
            Distance = distance;
            Class = cls;
        }

        public string Label => Target != null ? Target.ScanLabel : "UNKNOWN";
    }

    /// <summary>
    /// Every scannable thing in the loaded world, and the query that finds the near ones.
    ///
    /// <para>
    /// A registry rather than a <c>Physics.OverlapSphere</c>, and the reason is the radius. At the
    /// scanner's 100 m a sphere cast sweeps up terrain, buildings, every chunk of set dressing
    /// inside a 4 million cubic metre ball, and then throws nearly all of it away — and it still
    /// misses anything whose collider is smaller than its icon or absent altogether. Registration
    /// is O(things that want finding), needs no collider, and cannot be defeated by a layer mask
    /// somebody retunes for a different reason.
    /// </para>
    /// <para>
    /// The cost is that a scannable object must remember to register. That is why registration
    /// lives in <c>OnEnable</c>/<c>OnDisable</c> of the components that implement
    /// <see cref="IScanTarget"/>, which is also what makes it survive world streaming: a chunk
    /// unloading disables its contents, and its entries leave with them.
    /// </para>
    /// </summary>
    public static class ScannerRegistry
    {
        private static readonly List<IScanTarget> Targets = new();

        /// <summary>How many things are currently findable. Diagnostics only.</summary>
        public static int Count => Targets.Count;

        public static void Register(IScanTarget target)
        {
            if (target == null || Targets.Contains(target)) return;
            Targets.Add(target);
        }

        public static void Unregister(IScanTarget target)
        {
            if (target != null) Targets.Remove(target);
        }

        /// <summary>
        /// Fill <paramref name="into"/> with everything inside <paramref name="radius"/> of
        /// <paramref name="origin"/>, nearest first, capped at <paramref name="limit"/>.
        ///
        /// <para>
        /// Returns the total number found, which can exceed what was written — the display uses
        /// the difference to show that there is more out there than it has room to draw.
        /// </para>
        /// </summary>
        public static int Collect(Vector3 origin, float radius, List<ScanContact> into, int limit)
        {
            into.Clear();
            float sqrRadius = radius * radius;
            int found = 0;

            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                IScanTarget target = Targets[i];

                // A destroyed MonoBehaviour still occupies its slot: the interface reference is a
                // plain C# reference and does not go null with the object behind it. Unity's
                // lifetime check only works through the Object type, hence the cast.
                if (target is Object obj && obj == null)
                {
                    Targets.RemoveAt(i);
                    continue;
                }

                if (target == null)
                {
                    Targets.RemoveAt(i);
                    continue;
                }

                if (!target.IsScannable) continue;

                Vector3 position = target.ScanPosition;
                float sqr = (position - origin).sqrMagnitude;
                if (sqr > sqrRadius) continue;

                found++;
                into.Add(new ScanContact(target, position, Mathf.Sqrt(sqr), target.ScanClass));
            }

            into.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            if (into.Count > limit) into.RemoveRange(limit, into.Count - limit);
            return found;
        }

        /// <summary>
        /// Empty the registry between play sessions.
        ///
        /// Static state survives Play-mode exit when domain reload is disabled, which is the
        /// default on this project. Without this the second session starts holding every target
        /// from the first, all of them destroyed, and the scanner spends its first scan sweeping
        /// them out.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Targets.Clear();
    }
}
