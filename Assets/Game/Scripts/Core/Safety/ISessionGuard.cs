// One thing that can go wrong with a session, and how to notice it.
//
// A guard measures an OUTCOME. It does not know how the state it finds came about, and deliberately
// so — UnderTerrainGuard's header makes the argument in full: a failsafe that enumerated causes
// would only ever cover the ones already known about, and the whole value of these is catching the
// cause nobody has hit yet.
namespace SpaceGame.Core.Safety
{
    public interface ISessionGuard
    {
        /// <summary>Short, stable, used in the fault site and in the log. e.g. "StuckScope".</summary>
        string Name { get; }

        /// <summary>
        /// Look at the world and fix it if it is broken.
        /// <paramref name="interval"/> is the seconds since this guard was last checked — guards
        /// measure their own patience with it rather than reading Time directly, so a test can march
        /// one forward without a frame loop.
        /// </summary>
        void Check(float interval);
    }
}
