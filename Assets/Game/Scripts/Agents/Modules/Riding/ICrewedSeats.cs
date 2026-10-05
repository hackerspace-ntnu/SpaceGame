// Seats that fill themselves with NPCs as their vehicle comes into the world -- NpcPassenger's
// saddle, a double monowheel's MountedGunners -- and can be stood down for good.
//
// A vehicle a player takes from a group (NpcWorldSim.ReleaseToPlayer) is theirs from then on: an
// ordinary saved player vehicle. Left alone, its seats would crew it again the moment a load put it
// back in the world, since the prefab spawns its riders on start. Standing down is what stops that,
// and CrewSaveable carries it across a reload.
namespace SpaceGame.Agents
{
    public interface ICrewedSeats
    {
        /// <summary>Stood down: these seats never crew themselves again.</summary>
        bool IsStoodDown { get; }

        /// <summary>
        /// Stop crewing these seats. <paramref name="restoring"/> is false when a player has just taken
        /// the vehicle -- whoever is aboard gets down and fights on -- and true when a load is putting a
        /// taken vehicle back, where anyone its spawn already seated is a stranger to take away.
        /// Authority only; does nothing elsewhere.
        /// </summary>
        void StandDown(bool restoring);
    }
}
