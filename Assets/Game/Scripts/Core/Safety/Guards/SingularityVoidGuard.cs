// Gets the local player out of the singularity's void when nothing is holding them there.
//
// The void is the one interior in the game with NO DOOR. Every other one has an entrance you can
// walk back through; this one is entered by being eaten and left when the thing that ate you lets
// go, which means the only way out is a well that is still alive and still counting. So every way
// that well can stop existing — its chunk unloading, the host quitting, a load from a save taken
// mid-hold, a bug — is a player left standing in a white room forever with nothing to interact
// with and no console message.
//
// It is a guard rather than a check inside the artifact for the reason every guard here exists: the
// artifact is the thing that might not be running. A guard measures the OUTCOME — "inside the void,
// with no singularity claiming you" — and repairs it, which catches the cause nobody has hit yet.
// Same bargain as UnderTerrainGuard, and the same doctrine InputRestoreGuard follows.
using SpaceGame.Characters;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Core.Safety
{
    public sealed class SingularityVoidGuard : ISessionGuard
    {
        /// <summary>
        /// How long the player may be in the void unclaimed before they are put back.
        ///
        /// <para>
        /// Short, unlike the other guards' timeouts, and it can be: the claim it asks about is a
        /// replicated status that is applied in the same step the body is moved, so there is no
        /// handover window to be generous about. It exists at all because the status arrives as a
        /// message and the move does not — a client can legitimately be in the room for a frame or
        /// two before it is told why.
        /// </para>
        /// </summary>
        public const float TimeoutSeconds = 1.5f;

        private float unclaimedSeconds;

        public string Name => "SingularityVoid";

        public void Check(float interval)
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null)
            {
                unclaimedSeconds = 0f;
                return;
            }

            GameObject body = player.transform.root.gameObject;

            if (!InVoid(body) || StatusReceiver.HasStatus(body, StatusKind.Swallowed))
            {
                unclaimedSeconds = 0f;
                return;
            }

            unclaimedSeconds += interval;
            if (unclaimedSeconds < TimeoutSeconds) return;

            Debug.LogError($"[SingularityVoidGuard] The local player has been in the singularity " +
                           $"void for {unclaimedSeconds:F1}s with no singularity holding them. " +
                           "Putting them back — the well that ate them stopped existing without " +
                           "letting go, and that is the real bug.", player);

            // Through the player's own transit, which is the sanctioned route out for a player on
            // any machine: it runs the exit directly on the server and sends an owner RPC from a
            // client. Writing the transform here would move a body the server owns the truth of.
            if (body.TryGetComponent(out PlayerInteriorTransit transit)) transit.RequestExit();
            else InteriorManager.Instance?.ExitInterior(body);

            unclaimedSeconds = 0f;
        }

        /// <summary>
        /// Is this body inside the void right now?
        ///
        /// Asked of the interior system rather than of <c>body.scene</c>, because scene equality is
        /// not the question anywhere in this project — world streaming migrates players between
        /// chunk sub-scenes as a matter of course.
        /// </summary>
        private static bool InVoid(GameObject body)
        {
            InteriorManager interiors = InteriorManager.Instance;
            if (interiors == null || !interiors.IsInsideInterior(body)) return false;

            return body.scene.name == SingularityVoid.SceneName;
        }
    }
}
