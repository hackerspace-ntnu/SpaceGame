// Where a booster may go, and how it sits when it gets there.
//
// Pure functions, deliberately: the SAME question is asked twice, on two machines that do not
// trust each other. The holder asks it before the request leaves, so a hopeless aim costs no round
// trip and — more importantly — no booster. The server asks it again when the request arrives,
// because the first answer came from a machine that decides nothing (GDC-L1-MP-0004). Two copies of
// that rule would be two rules the day one of them is edited, which is the shape of bug that only
// shows up as "the item vanishes and nothing happens".
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
        /// This is the one question the item refuses on, and it exists because the alternative is
        /// the design's own worst case: a booster clamped to a parked vehicle, a mounted rider or a
        /// legged rig that fires, burns for two seconds and moves nothing at all, with a clean
        /// console. Force written to a kinematic Rigidbody is discarded in silence, and a body
        /// whose transform is authored by a locomotion solver overwrites anything the solver was
        /// not asked for.
        /// </para>
        /// <para>
        /// So the two answers, in this order:
        /// </para>
        /// <list type="number">
        /// <item><b><see cref="ITowable"/> first.</b> A machine that carries its own flight or gait
        /// as state has to be ASKED — it owns what a push costs and what its airframe will take.
        /// This branch is also what catches a mounted rider: their body is kinematic and parented
        /// into the seat, so the thing above them in the hierarchy is what is actually moving.</item>
        /// <item><b>A live <see cref="NavMeshAgent"/> is a refusal, not a body.</b> An agent writes
        /// the transform every frame it is enabled, so a force put into the Rigidbody underneath it
        /// is gone before anyone sees it — the same failure as a kinematic body, wearing a Rigidbody
        /// that says otherwise. The fix is one interface, not a special case here: a motor that
        /// wants to be shoved implements <see cref="ITowable"/> and is caught by the branch above,
        /// the way <c>LeggedDriver</c> and <c>OrnithopterFlightMotor</c> already are.</item>
        /// <item><b>A dynamic Rigidbody otherwise.</b> A crate, a dropped item, a player on their
        /// own feet.</item>
        /// </list>
        /// <para>
        /// Anything else is refused, and the booster stays in the hotbar. A press that quietly does
        /// nothing and eats the item is worse than a press that does nothing and keeps it.
        /// </para>
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
