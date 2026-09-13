// Where a booster may go, and how it sits when it gets there.
//
// A booster clamps to ANY surface. Whether the thing it lands on then moves is a separate
// question with a separate answer: a crate slides, a rock face does not, and the flame is the
// same either way. Those two questions used to be one, and the item refused to clamp to
// everything the player could actually see — the desert, the settlement walls, the parked hulls,
// every creature on a NavMeshAgent — which read as the item being broken. The rule is authored
// and the outcome is the player's (GDC-L1-SYS-0002): stick it where you like and find out.
//
// Pure functions, deliberately: the SAME question is asked twice, on two machines that do not
// trust each other. The holder asks it before the request leaves, so a hopeless aim costs no round
// trip. The server asks it again when the request arrives, because the first answer came from a
// machine that decides nothing (GDC-L1-MP-0004). Two copies of that rule would be two rules the
// day one of them is edited, which is the shape of bug that only shows up as "the item vanishes
// and nothing happens".
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using SpaceGame.Agents;

namespace SpaceGame.Items
{
    /// <summary>
    /// The criteria and the seating for a strap-on booster. See the note at the top of the file.
    /// </summary>
    public static class BoosterClamp
    {
        /// <summary>
        /// The transform a booster clamped onto <paramref name="hit"/> is strapped to.
        ///
        /// <para>
        /// The <see cref="NetworkObject"/> rather than the collider, for two reasons at once. It is
        /// the only thing a clamp can be NAMED by on the wire — every other machine has to be told
        /// what this booster is riding, and a network id is the one handle they all share. And it
        /// is the transform whose pose the whole session agrees on: a ray can just as easily land
        /// on a limb, a hatch or a wheel, and a booster following a leg that an IK solver re-poses
        /// every frame would be strapped to something that is not where the body is.
        /// </para>
        /// <para>
        /// Falls back to the Rigidbody for anything nobody has networked — an interior prop, a
        /// scene opened straight from the editor — which is the same fallback the authority facade
        /// makes for unnetworked objects.
        /// </para>
        /// <para>
        /// <b>Null is an answer, not a failure.</b> Terrain, a settlement wall and a chunk rock are
        /// neither networked nor rigid, and a booster stuck to one of them rides the WORLD: it is
        /// clamped at a fixed world pose and burns there. See <c>BoosterMount.Clamp</c>.
        /// </para>
        /// </summary>
        public static Transform BodyFor(GameObject hit)
        {
            if (hit == null) return null;

            NetworkObject networked = hit.GetComponentInParent<NetworkObject>();
            if (networked != null) return networked.transform;

            Rigidbody body = hit.GetComponentInParent<Rigidbody>();
            return body != null ? body.transform : null;
        }

        /// <summary>
        /// Would a booster strapped to <paramref name="body"/> actually move it?
        ///
        /// <para>
        /// <b>Not a permission — a prediction.</b> The clamp itself never refuses; this is what
        /// decides where the push is spent and what the crosshair is allowed to promise. Force
        /// written to a kinematic Rigidbody is discarded in silence, and a body whose transform is
        /// authored by a locomotion solver overwrites anything the solver was not asked for, so a
        /// booster on one of those burns and moves nothing. That is a visible outcome — the flame
        /// is right there — rather than the silent one the old refusal was guarding against.
        /// </para>
        /// <para>
        /// So the two answers, in this order:
        /// </para>
        /// <list type="number">
        /// <item><b><see cref="ITowable"/> first.</b> A machine that carries its own flight or gait
        /// as state has to be ASKED — it owns what a push costs and what its airframe will take.
        /// This branch is also what catches a mounted rider: their body is kinematic and parented
        /// into the seat, so the thing above them in the hierarchy is what is actually moving.</item>
        /// <item><b>A live <see cref="NavMeshAgent"/> is a no, not a body.</b> An agent writes the
        /// transform every frame it is enabled, so a force put into the Rigidbody underneath it is
        /// gone before anyone sees it — the same outcome as a kinematic body, wearing a Rigidbody
        /// that says otherwise. The fix is one interface, not a special case here: a motor that
        /// wants to be shoved implements <see cref="ITowable"/> and is caught by the branch above,
        /// the way <c>LeggedDriver</c> and <c>OrnithopterFlightMotor</c> already are.</item>
        /// <item><b>A dynamic Rigidbody otherwise.</b> A crate, a dropped item, a player on their
        /// own feet.</item>
        /// </list>
        /// </summary>
        public static bool CanPush(Transform body)
        {
            if (body == null) return false;
            if (body.GetComponentInParent<ITowable>() != null) return true;

            NavMeshAgent agent = body.GetComponentInParent<NavMeshAgent>();
            if (agent != null && agent.enabled) return false;

            return body.TryGetComponent(out Rigidbody rigidbody) && !rigidbody.isKinematic;
        }

        /// <summary>
        /// How the booster sits on a surface whose outward normal is
        /// <paramref name="surfaceNormal"/>.
        ///
        /// <para>
        /// The bell points OUT of the surface, which is what makes the thrust go into it — and that
        /// is the whole aim of the item: a booster on the side of a crate slides the crate, one on
        /// its underside flies it. Nothing here chooses a direction; the direction is where the
        /// player stuck it (GDC-L1-SYS-0002).
        /// </para>
        /// <para>
        /// <paramref name="localExhaustAxis"/> comes off the model's own markers rather than an
        /// assumed axis — see <see cref="BoosterShell.LocalExhaustAxis"/>. The roll about that axis
        /// is left wherever <c>FromToRotation</c> puts it, because the booster is a cylinder with a
        /// band around it: there is no roll to get wrong.
        /// </para>
        /// </summary>
        public static Quaternion Seat(Vector3 localExhaustAxis, Vector3 surfaceNormal)
        {
            if (localExhaustAxis.sqrMagnitude < 1e-6f || surfaceNormal.sqrMagnitude < 1e-6f)
                return Quaternion.identity;

            return Quaternion.FromToRotation(localExhaustAxis.normalized, surfaceNormal.normalized);
        }
    }
}
