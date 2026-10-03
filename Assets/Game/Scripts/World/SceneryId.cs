// The wire id of a piece of scenery: one int every machine derives alike, because a chunk prop has no NetworkObject to name it by.
//
// It is the hash of the id a SaveableEntity falls back on (scene + hierarchy path + sibling index), so two machines that loaded
// the same scene agree on it with nothing sent, and renaming or re-parenting the prop changes it. Never 0, which is "none" on the wire.
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.World
{
    public static class SceneryId
    {
        public static int Of(UnityEngine.GameObject scenery)
        {
            int hash = RosterDraw.StableHash(SaveableEntity.DeriveAuthoredId(scenery));
            return hash == 0 ? 1 : hash;
        }
    }
}
