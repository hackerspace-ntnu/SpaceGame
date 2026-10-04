// What a respawn takes off a player before it stands them back up.
//
// None of the systems collected here notices a death, and none of them should have to: a rope is
// tied to a body rather than to a life, a net holds whatever is lying under it, and a fire burns
// what it was set on. Their own lifecycles are right for what each of them models. What was missing
// is the one moment where all of that stops being true at once — a player who comes back in their
// ship is a new start, and arriving still roped to whoever caught them, on a rope now stretched
// across the world, is a state nobody designed.
//
// So this is a seam rather than a system: every release below already exists and already announces
// itself the way that system announces things. This is the list of them and the moment they run.
using UnityEngine;
using SpaceGame.Gameplay.Status;
using SpaceGame.Items;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Cut a body loose from everything holding it, at the moment it respawns.
    ///
    /// <para>
    /// Called by every respawn path — <c>PlayerRespawn</c> for the world and <c>ShipRespawn</c>
    /// for a minigame — on the DECIDING machine, immediately before the move. Before rather than
    /// after, because a rope resolves against both its ends every physics step: one left standing
    /// across a teleport spends that step hauling somebody back towards the grave they just left.
    /// </para>
    /// <para>
    /// Death is deliberately not the moment. A corpse may be roped, netted and burning — dragging a
    /// body somewhere is a thing players do, and it stops meaning anything if the ropes fall off the
    /// instant it stops moving. What ends is the respawn, and only for the body that respawns.
    /// </para>
    /// </summary>
    public static class RespawnRelease
    {
        /// <summary>
        /// Let go of <paramref name="body"/>: every rope on it, the net it lies under, the cordage
        /// it is tied up in, and every condition running on it.
        ///
        /// <para>
        /// Safe for anything that respawns, a bot with none of these components included. Each step
        /// answers "nothing to do" on its own rather than being asked first whether it applies.
        /// </para>
        /// </summary>
        public static void Everything(GameObject body)
        {
            if (body == null) return;

            // Leash, lasso and grapple, through the seam the laser staff already cuts them with.
            // Each parts the way it already parts and tells the session itself.
            CuttableRopes.CutEveryRopeOn(body);

            // The net, if one has hold of them. Only this captive leaves — a net that came down on
            // two people must not fall apart because one of them respawned.
            SnareCatch net = SnareCatch.Holding(body);
            if (net != null) net.FreeEverywhere(body);

            // The hogtie, which is cordage rather than a rope anything can see. Untie is the one
            // way a tie ends and is safe twice, so a body that is not tied costs a null check.
            Hogtie tie = body.GetComponentInChildren<Hogtie>();
            if (tie != null) tie.Untie();

            // Burning, foam, and anything else arriving through the same receiver.
            StatusReceiver status = StatusReceiver.Of(body);
            if (status != null) status.ClearAll();
        }
    }
}
