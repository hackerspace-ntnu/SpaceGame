// The camera-distance band over which a vehicle's near dust fades out. FarDust fades in over the same
// band, so the two crossfade and are never both at full (VehicleDust.md). Each near emitter keeps its
// own band: the legged and tracked machines' 80-200 m, the monowheels' 60-150 m.
namespace SpaceGame.Vehicles
{
    public interface IDustLodBand
    {
        /// <summary>Full near dust up to this camera distance (m).</summary>
        float LodNear { get; }

        /// <summary>No near dust beyond this camera distance (m).</summary>
        float LodFar { get; }
    }
}
