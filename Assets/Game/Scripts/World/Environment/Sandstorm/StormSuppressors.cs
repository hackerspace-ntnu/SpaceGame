// Things that hold storms off while they stand -- today the storm ward.
//
// A suppressed storm is not ended. Its record lives on, it keeps drifting along its path and ageing
// through its life, and it is saved like any other; it simply resolves at no intensity while any
// suppressor's circle is reached by its sand (StormShape.ReachesCircle). Take the suppressor away and
// the storm fades back in, wherever it has got to by then, if it is still meant to be there.
//
// Every machine asks its own registry, and every machine has the same suppressors: they are spawned
// NetworkObjects that register themselves when they appear. So suppression replicates for free, the
// same way storm positions do -- nothing about it is on the wire.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    /// <summary>Holds storms off within <see cref="SuppressionRadius"/> of where it stands.</summary>
    public interface IStormSuppressor
    {
        Vector3 SuppressionCentre { get; }

        /// <summary>Metres. A storm whose sand comes within this is held off.</summary>
        float SuppressionRadius { get; }
    }

    public static class StormSuppressors
    {
        private static readonly List<IStormSuppressor> Active = new List<IStormSuppressor>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active.Clear();

        public static void Register(IStormSuppressor suppressor)
        {
            if (!Active.Contains(suppressor)) Active.Add(suppressor);
        }

        public static void Unregister(IStormSuppressor suppressor) => Active.Remove(suppressor);

        /// <summary>Whether any registered suppressor holds off a storm with this footprint.</summary>
        public static bool Suppresses(in StormFootprint footprint)
        {
            foreach (IStormSuppressor suppressor in Active)
            {
                Vector3 centre = suppressor.SuppressionCentre;
                if (StormShape.ReachesCircle(footprint, new Vector2(centre.x, centre.z), suppressor.SuppressionRadius))
                    return true;
            }
            return false;
        }
    }
}
