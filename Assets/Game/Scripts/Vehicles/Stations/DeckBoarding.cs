// Look at the hull and press E to climb aboard.
//
// The dune foiler has no mount: you sail it by walking its deck and working the rigging, so there is
// no seat to sit in and nothing for a MountModule to own. What was missing was simply a way ON. The
// gangway is a 28-degree ramp and it does work, but it is one 1.8 m wide plank on the starboard side
// of an 18 m hull, and every other approach leaves you standing at the foot of a deck you cannot
// reach. This puts you on the deck from wherever you are looking at the craft.
//
// Deliberately NOT a MountModule. Mounting takes the vehicle over — camera, controls, the lot — and
// that is the one thing this craft must never do, because the controls ARE the deck. This only moves
// the player; they keep their own body, their own camera, and their own legs.
//
// It sits on the hull root so every hull collider resolves to it: Interactor walks up from whatever
// it hit until it finds an IInteractable, and the rigging stations are nearer their own handles than
// this is, so they keep priority. Looking at a rope station still works the rope.
//
// Boarding is deliberately NOT server-authoritative, unlike taking the helm. There is nothing here
// two players can race for: a deck is not a seat, everybody who wants to be on it can be, and
// nothing about the hull changes when somebody steps onto it. What the move DOES need is to happen
// on the machine that owns the body being moved — see Interact.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Locomotion;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    public class DeckBoarding : MonoBehaviour, IInteractable, IContextualInteractable,
                                IInteractionReadout
    {
        [Header("Where you land")]
        [Tooltip("Exact spot to put the player. When empty, the top of the deck collider is used.")]
        [SerializeField] private Transform boardPoint;

        [Tooltip("The walkable deck. Its top surface is where the player's feet end up.")]
        [SerializeField] private Collider deckSurface;

        [Tooltip("Gap left between the player's feet and the deck, so they settle onto it rather " +
                 "than starting the frame intersecting it.")]
        [SerializeField, Min(0f)] private float footClearance = 0.05f;

        [Header("Availability")]
        [Tooltip("Volume that counts as 'already aboard'. Boarding is not offered to someone " +
                 "standing in it. Normally the carry volume.")]
        [SerializeField] private Collider abroadVolume;

        [Tooltip("Ride height, as a fraction of the strut, above which there is no boarding. The " +
                 "deck is thirteen metres up at speed; stepping onto it from the sand would be a " +
                 "teleport, and stepping onto it from a moving craft would be a teleport into a " +
                 "wall. Found on the craft when empty.")]
        [SerializeField] private FoilLift foil;

        [SerializeField, Range(0f, 1f)] private float boardBelowRideHeight = 0.06f;

        /// <summary>What the craft is called on the HUD.</summary>
        public string Label => "Dune foiler";

        /// <summary>Prompt text. Drawn by VisorReticle's info box.</summary>
        public string Prompt => "RMB: climb aboard";

        /// <summary>Boarding has no position to show, so no bar is drawn.</summary>
        public float? Value01 => null;

        public string ValueText => string.Empty;

        private void Awake()
        {
            if (deckSurface == null) deckSurface = FindChildCollider("COL_Deck");
            if (abroadVolume == null) abroadVolume = FindChildCollider("COL_CarryVolume");
            if (foil == null) foil = GetComponentInParent<FoilLift>();

            if (deckSurface == null && boardPoint == null)
                Debug.LogWarning($"[{nameof(DeckBoarding)}] {name} has neither a board point nor a " +
                                 "deck collider; there is nowhere to put anyone.", this);
        }

        private Collider FindChildCollider(string childName)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c.name == childName) return c;
            return null;
        }

        /// <summary>
        /// Is there anywhere to put anyone, and is the deck reachable at all right now?
        ///
        /// The ride-height test is here rather than in <see cref="Interact"/> because a craft up
        /// on its foil has its deck thirteen metres in the air with the gangway stowed. Boarding
        /// it from the sand is not a shortcut, it is a teleport into a hull doing forty metres a
        /// second, and offering it in the prompt is a promise the craft cannot keep.
        /// </summary>
        public bool CanInteract()
        {
            if (boardPoint == null && deckSurface == null) return false;
            return foil == null || foil.RideHeight01 <= boardBelowRideHeight;
        }

        /// <summary>
        /// And not for someone already standing on it.
        ///
        /// This component sits on the hull root so every hull collider resolves to it, which is
        /// what makes the craft boardable from any angle — and is also why, once aboard, looking
        /// at the mast or down at the planks used to offer "climb aboard" and, if pressed, throw
        /// the player back amidships. The refusal has to be per-player: someone else standing on
        /// the sand alongside still needs the prompt.
        /// </summary>
        public bool CanInteract(Interactor interactor)
        {
            if (interactor == null) return true;
            return !IsAlreadyAboard(ResolvePlayer(interactor));
        }

        /// <summary>
        /// Put this player on the deck.
        ///
        /// <para>
        /// The move goes through <see cref="NetworkedTeleport"/> rather than being written onto the
        /// Rigidbody here, and that is the whole multiplayer half of this component. A player's
        /// NetworkTransform is OWNER-authoritative, so a machine that does not own this body can
        /// write its position all it likes and the owner's next state update overwrites it within a
        /// tick — silently. On a client that meant boarding teleported you onto a hull that, on the
        /// host, was somewhere else entirely; a moment later the host's idea of where you were won,
        /// and you were back in the sand. NetworkedTeleport encodes the rule once: whoever owns the
        /// body performs the move, everybody else finds out through the transform sync that is
        /// already running. It also degrades to a plain placement offline and for anything with no
        /// NetworkObject, so single-player and the EditMode tests take the same path.
        /// </para>
        /// <para>
        /// It brings <see cref="SpaceGame.Core.Persistence.SaveTeleport"/> with it, which is
        /// strictly more correct than the assignment it replaces: assigning transform.position does
        /// not move a body in this project (physics auto-sync is off and the player is
        /// interpolated), so the pose had to be written onto the Rigidbody as well — and the
        /// interpolation history had to be dropped across the write, which is the part the old code
        /// did not do and which reads as the player sliding back toward the sand for a frame.
        /// </para>
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;

            Transform player = ResolvePlayer(interactor);
            if (player == null) return;

            // Not our body to move. Interactor only ever fires for the local player, so this is a
            // guard against a caller rather than a case the game reaches — but IInteractable is a
            // public surface, and moving somebody else's body from here would be a teleport that
            // gets undone within a tick and is invisible while it happens.
            if (!Network.Owns(player)) return;

            // Everything below asks colliders where they are, and this hull is transform-driven:
            // the locomotion writes its position every Update, while collider bounds only catch up
            // at the next physics step. Unsynced, the deck answers from where it was rather than
            // where it is — half a metre astern at cruising speed, and a great deal further than
            // that on the frame after the craft has been moved by anything else. The player then
            // lands beside the hull instead of on it.
            //
            // Every word of that survives the craft becoming networked, and one more reason joins
            // it: on a machine that does not own the hull the pose is written by the replicated
            // NetworkTransform, which is a transform write like any other and leaves the colliders
            // exactly as far behind.
            Physics.SyncTransforms();

            if (IsAlreadyAboard(player)) return;

            if (!ResolveLanding(player, out Vector3 landing)) return;

            // Zeroing the fall is SaveTeleport's default: arriving with the speed you built up
            // walking into the hull launches you off the far rail.
            NetworkedTeleport.Move(player.gameObject, landing, player.rotation);

            Physics.SyncTransforms();
        }

        /// <summary>
        /// The body this interactor belongs to. Resolved through the Rigidbody rather than off
        /// <c>transform.root</c>, so this keeps working if the Interactor is ever moved onto a
        /// camera rig under the player.
        /// </summary>
        private static Transform ResolvePlayer(Interactor interactor)
        {
            Rigidbody body = interactor.GetComponentInParent<Rigidbody>();
            return body != null ? body.transform : interactor.transform.root;
        }

        /// <summary>True when this player is standing in the craft's carry volume already.</summary>
        private bool IsAlreadyAboard(Transform player)
        {
            if (abroadVolume == null || player == null) return false;
            Vector3 closest = abroadVolume.ClosestPoint(player.position);
            return (closest - player.position).sqrMagnitude < 1e-4f;
        }

        /// <summary>
        /// Where the player's ROOT has to go for their feet to land on the deck. Measured off their
        /// own collider rather than assumed — see <see cref="BodyFeet"/> — because the player's
        /// pivot is not at their feet and a hardcoded offset would bury them to the knees or drop
        /// them from a height.
        /// </summary>
        private bool ResolveLanding(Transform player, out Vector3 landing)
        {
            landing = Vector3.zero;

            if (boardPoint != null)
            {
                landing = boardPoint.position;
                return true;
            }

            if (deckSurface == null) return false;

            Bounds deck = deckSurface.bounds;
            float rootAboveFeet = new BodyFeet(player).RootAboveFeet;

            float radius = 0.4f;
            foreach (Collider c in player.GetComponentsInChildren<Collider>())
                if (c is CapsuleCollider capsule && !capsule.isTrigger)
                    radius = Mathf.Max(radius, capsule.bounds.extents.x);

            float footY = deck.max.y + footClearance;
            float rootY = footY + rootAboveFeet;

            // Amidships is the obvious place to arrive, and it is also where the rope stations are:
            // dropped exactly on the deck centre the player lands inside a station handle and gets
            // shoved off by the physics solver. So walk the centreline out from the middle and take
            // the first clear spot, which puts them beside the rigging rather than inside it.
            Vector3 along = deck.size.z >= deck.size.x ? Vector3.forward : Vector3.right;
            float reach = (Vector3.Dot(along, Vector3.forward) > 0.5f ? deck.extents.z : deck.extents.x) - radius;

            for (float step = 0f; step <= reach; step += 0.75f)
            {
                foreach (float sign in step == 0f ? OneWay : BothWays)
                {
                    Vector3 candidate = new Vector3(deck.center.x, rootY, deck.center.z) + along * (step * sign);
                    if (!IsBlocked(candidate, footY, radius))
                    {
                        landing = candidate;
                        return true;
                    }
                }
            }

            // Everywhere is cluttered — put them on the deck anyway rather than refusing to board.
            landing = new Vector3(deck.center.x, rootY, deck.center.z);
            return true;
        }

        private static readonly float[] OneWay = { 1f };
        private static readonly float[] BothWays = { -1f, 1f };

        /// <summary>Is there something solid where the player would stand?</summary>
        private bool IsBlocked(Vector3 rootPosition, float footY, float radius)
        {
            // The capsule starts above the deck surface, so the deck itself is never a hit.
            Vector3 bottom = new Vector3(rootPosition.x, footY + radius, rootPosition.z);
            Vector3 top = new Vector3(rootPosition.x, footY + 1.8f - radius, rootPosition.z);

            foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius, ~0,
                                                          QueryTriggerInteraction.Ignore))
            {
                if (c == deckSurface) continue;
                if (c.transform.IsChildOf(transform)) return true;   // a handle, a rail, the mast
            }
            return false;
        }

        /// <summary>Wire it up. Used by the prefab builder.</summary>
        public void Bind(Collider deck, Collider aboard, FoilLift lift, Transform point = null)
        {
            deckSurface = deck;
            abroadVolume = aboard;
            foil = lift;
            boardPoint = point;
        }

        private void OnDrawGizmosSelected()
        {
            if (deckSurface == null && boardPoint == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Vector3 p = boardPoint != null
                ? boardPoint.position
                : new Vector3(deckSurface.bounds.center.x, deckSurface.bounds.max.y,
                              deckSurface.bounds.center.z);
            Gizmos.DrawWireSphere(p, 0.5f);
            Gizmos.DrawLine(p, p + Vector3.up * 2f);
        }
    }
}
