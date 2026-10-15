using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents.Residents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// The way a colonist crosses a colony airlock: a bidirectional off-mesh link from a stand outside the outer hatch to
    /// a stand inside the inner one, with this as its owner and its gate. Every building is its own NavMesh island, the shut hatch leaves are
    /// baked as walls, and this link is the only way between; <c>NavMeshAgentMotor</c> hands a colonist that reaches either end to
    /// <see cref="Direct"/>, and <see cref="AirlockTransit"/> (the decisions) works the real hatches for it: the chamber vents and
    /// pressurises, the mist and beacon run, and everyone, players included, sees a colonist cycle through.
    ///
    /// <para>
    /// <b>Server only.</b> A colonist's motor only ticks where the agent is simulated, so this only runs there; the hatches it
    /// moves reach every client through the chamber's own <c>AirlockState</c>. Nothing here is saved: a colonist saved
    /// mid-crossing loads inside the chamber's island and is put on the end of its plan's way by the routine's settle.
    /// </para>
    /// <para>
    /// <b>The fallback.</b> A colonist whose turn does not come (a player holds the airlock) and whom nobody can see hops across
    /// after <see cref="hopAfterSeconds"/>, the way <c>ResidentRoutine</c> hops a tower guard. While anyone sees it, it keeps waiting.
    /// </para>
    /// </summary>
    public sealed class AirlockPassage : MonoBehaviour, INavLinkGate, IAirlockPort
    {
        private const float ReachedWithin = 0.05f;

        [SerializeField] private AirlockChamber chamber;

        [Header("Link ends")]
        [Tooltip("On the ground outside, past the outer hatch's swing. Placed by Tools/SpaceGame/Colony/Place Airlock Passages.")]
        [SerializeField] private Transform outsideStand;
        [Tooltip("On the room's floor, past the inner hatch's leaves.")]
        [SerializeField] private Transform insideStand;

        [Header("Crossing")]
        [Tooltip("Seconds the first colonist at an end waits for company before the airlock cycles, so a group crosses on one cycle.")]
        [SerializeField, Min(0f)] private float groupWindowSeconds = 1.5f;
        [Tooltip("Seconds a colonist that has not set foot in the airlock waits before hopping across, once nobody can see it.")]
        [SerializeField, Min(1f)] private float hopAfterSeconds = 60f;
        [Tooltip("A player this close with line of sight counts as watching a colonist hop.")]
        [SerializeField, Min(1f)] private float observedWithin = 60f;
        [Tooltip("Metres per second a colonist walks through the open hatches.")]
        [SerializeField, Min(0.1f)] private float crossingSpeed = 1.8f;

        [Header("Link")]
        [Tooltip("Multiplies the cost of the link's length in route finding, so a path does not go through a building for no reason.")]
        [SerializeField, Min(1f)] private float linkCostModifier = 4f;
        [Tooltip("The link's width, metres: a colonist is carried by hand through the hatches, so this only has to admit a body.")]
        [SerializeField, Min(0.1f)] private float linkWidth = 1.5f;
        [Tooltip("How far from each stand the NavMesh may be, in metres, for the end to attach to it.")]
        [SerializeField, Min(0.1f)] private float linkSnapDistance = 3f;
        [Tooltip("Seconds between attempts to attach the link while there is no NavMesh at a stand yet: a streamed world's mesh may arrive after the building.")]
        [SerializeField, Min(0.1f)] private float linkRetryInterval = 1f;
        [Tooltip("Seconds to keep trying before giving up and saying so.")]
        [SerializeField, Min(0.1f)] private float linkRetryTimeout = 30f;

        [Header("Planning")]
        [Tooltip("Seconds a crossing takes beyond the walk itself (two hatches, a cycle), for the day planner's walking times.")]
        [SerializeField, Min(0f)] private float transitSeconds = 20f;

        private readonly Dictionary<int, Component> travellers = new();
        private readonly Dictionary<int, Vector3> destinations = new();
        private readonly List<int> gone = new();
        private AirlockTransit transit;
        private NavMeshLinkInstance navLink;

        public float TransitSeconds => transitSeconds;

        private void OnEnable()
        {
            transit = new AirlockTransit(groupWindowSeconds, hopAfterSeconds);
            NavLinkGates.Register(this);
            StartCoroutine(RegisterNavLink());
        }

        private void OnDisable()
        {
            NavLinkGates.Unregister(this);
            if (NavMesh.IsLinkValid(navLink)) NavMesh.RemoveLink(navLink);
            navLink = default;
            travellers.Clear();
            destinations.Clear();
        }

        // Retries rather than trusting enable order, as a ladder does: the world's mesh may arrive after the building, and a link
        // added to nothing attaches to nothing.
        private IEnumerator RegisterNavLink()
        {
            var wait = new WaitForSeconds(linkRetryInterval);
            for (float waited = 0f; !TryAddNavLink(); waited += linkRetryInterval)
            {
                if (waited >= linkRetryTimeout)
                {
                    Debug.LogWarning($"[Airlock] {name} found no NavMesh within {linkSnapDistance} m of its stands after " +
                                     $"{linkRetryTimeout:0.#}s; colonists cannot cross it.", this);
                    yield break;
                }
                yield return wait;
            }
        }

        /// <summary>Attaches the link now, if both stands are on the NavMesh: for a tool that audits a settlement in the editor, where nothing is playing.</summary>
        public bool TryAttachLink() => TryAddNavLink();

        /// <summary>The link as registered; invalid until it has attached.</summary>
        public NavMeshLinkInstance Link => navLink;

        private bool TryAddNavLink()
        {
            if (!NavMesh.SamplePosition(outsideStand.position, out NavMeshHit outside, linkSnapDistance, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(insideStand.position, out NavMeshHit inside, linkSnapDistance, NavMesh.AllAreas))
                return false;

            navLink = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = outside.position,
                endPosition = inside.position,
                bidirectional = true,
                width = linkWidth,
                costModifier = linkCostModifier,
            });
            NavMesh.SetLinkOwner(navLink, this);
            return NavMesh.IsLinkValid(navLink);
        }

        public void Ends(out Vector3 start, out Vector3 end)
        {
            start = outsideStand.position;
            end = insideStand.position;
        }

        public LinkGateOrder Direct(Component traveller, Vector3 body, Vector3 destination)
        {
            int id = traveller.GetInstanceID();
            AirlockDirection heading = (destination - insideStand.position).sqrMagnitude < (destination - outsideStand.position).sqrMagnitude
                ? AirlockDirection.Inward : AirlockDirection.Outward;
            transit.Arrive(id, heading, Time.time);
            travellers[id] = traveller;
            destinations[id] = destination;

            if (transit.ShouldHop(id, Time.time, this))
            {
                Hop(traveller, destination);
                return LinkGateOrder.Wait;
            }

            TransitOrder order = transit.OrderFor(id);
            if (order.Step == TransitOrder.Kind.Walk)
            {
                Vector3 target = Waypoint(heading, order.Waypoint);
                if ((body - target).sqrMagnitude > ReachedWithin * ReachedWithin) return LinkGateOrder.Walk(target, crossingSpeed);

                transit.Reached(id);
                order = transit.OrderFor(id);
                if (order.Step == TransitOrder.Kind.Walk) return LinkGateOrder.Walk(Waypoint(heading, order.Waypoint), crossingSpeed);
            }

            if (order.Step != TransitOrder.Kind.Done) return LinkGateOrder.Wait;

            Forget(id);
            return LinkGateOrder.Done;
        }

        public void Release(Component traveller) => Forget(traveller.GetInstanceID());

        private void Update()
        {
            if (!Network.Simulates(chamber)) return;

            gone.Clear();
            foreach (KeyValuePair<int, Component> pair in travellers)
                if (pair.Value == null || !pair.Value.gameObject.activeInHierarchy) gone.Add(pair.Key);
            foreach (int id in gone) Forget(id);

            transit.Tick(Time.time, this);
        }

        // Where a colonist walks, in order: the near doorway, the chamber's middle, the far doorway, and the end it lands on. All but
        // the last stand at the interior's floor height: the sill is level with the room, and the stand outside is on the ramp below it.
        private Vector3 Waypoint(AirlockDirection heading, int index)
        {
            bool inward = heading == AirlockDirection.Inward;
            AirlockSide near = inward ? AirlockSide.Outer : AirlockSide.Inner;
            AirlockSide far = inward ? AirlockSide.Inner : AirlockSide.Outer;
            float floor = insideStand.position.y;
            return index switch
            {
                AirlockTransit.NearDoorway => AtFloor(chamber.DoorwayCentre(near), floor),
                AirlockTransit.ChamberMiddle => AtFloor(chamber.ChamberCentre, floor),
                AirlockTransit.FarDoorway => AtFloor(chamber.DoorwayCentre(far), floor),
                _ => (inward ? insideStand : outsideStand).position,
            };
        }

        private static Vector3 AtFloor(Vector3 point, float floor) => new(point.x, floor, point.z);

        private void Hop(Component traveller, Vector3 destination)
        {
            Vector3 heading = Vector3.ProjectOnPlane(destination - traveller.transform.position, Vector3.up);
            Quaternion facing = heading.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(heading) : traveller.transform.rotation;
            NetworkedTeleport.Move(traveller.gameObject, destination, facing);
        }

        private void Forget(int id)
        {
            transit.Leave(id);
            travellers.Remove(id);
            destinations.Remove(id);
        }

        // ── IAirlockPort ─────────────────────────────────────────────────────

        AirlockState IAirlockPort.State => chamber.State;
        bool IAirlockPort.IsFullyOpen(AirlockSide side) => chamber.IsFullyOpen(side);
        bool IAirlockPort.IsShut(AirlockSide side) => chamber.IsShut(side);
        AirlockRefusal IAirlockPort.Operate(AirlockSide side, bool fromChamber) => chamber.ServerOperate(side, fromChamber);

        bool IAirlockPort.Unobserved(int traveller)
        {
            if (!travellers.TryGetValue(traveller, out Component body) || body == null) return false;

            Vector3 chest = Vector3.up * ObserverCheck.ChestHeight;
            return !ObserverCheck.AnyPlayerSees(body.transform.position + chest, observedWithin) &&
                   !ObserverCheck.AnyPlayerSees(destinations[traveller] + chest, observedWithin);
        }
    }
}
