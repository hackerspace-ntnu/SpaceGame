// A NavMesh link that something stands between: an airlock, a door, a pen gate.
//
// An ordinary link is crossed the moment an agent reaches it (a plain walk, a ladder, a jump). A gated link
// is crossed when its gate lets the traveller through: NavMeshAgentMotor stops the traveller at the link's
// start and asks the gate, every tick, what to do next -- wait, walk to this point, or it is across.
// The gate does the opening and closing; the motor only carries the body.
//
// Put the implementing component on the same GameObject as the NavMeshLink. Server-only: only the machine
// that simulates an agent ever ticks its motor, so a gate never runs on a client.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>What a gate tells one traveller to do this tick.</summary>
    public enum LinkGateStep : byte
    {
        /// <summary>Stand where you are (queueing, or in the middle of the crossing waiting on the gate).</summary>
        Wait,

        /// <summary>Walk to <see cref="LinkGateOrder.Target"/> at <see cref="LinkGateOrder.Speed"/>.</summary>
        Walk,

        /// <summary>Across: the motor puts the body on the mesh at the link's far end.</summary>
        Done,
    }

    public readonly struct LinkGateOrder
    {
        public readonly LinkGateStep Step;
        public readonly Vector3 Target;
        public readonly float Speed;

        private LinkGateOrder(LinkGateStep step, Vector3 target, float speed)
        {
            Step = step;
            Target = target;
            Speed = speed;
        }

        public static LinkGateOrder Wait => new(LinkGateStep.Wait, default, 0f);
        public static LinkGateOrder Done => new(LinkGateStep.Done, default, 0f);
        public static LinkGateOrder Walk(Vector3 target, float speed) => new(LinkGateStep.Walk, target, speed);
    }

    public interface INavLinkGate
    {
        /// <summary>Both ends of the link, for the planner to recognise a path step that crosses it.</summary>
        void Ends(out Vector3 start, out Vector3 end);

        /// <summary>Seconds a crossing takes beyond walking the distance, for the planner's walking times.</summary>
        float TransitSeconds { get; }

        /// <summary>
        /// What <paramref name="traveller"/>, standing at <paramref name="body"/> and bound for <paramref name="destination"/> (one
        /// of the link's ends), does this tick. Asked every tick until it answers <see cref="LinkGateStep.Done"/>.
        /// </summary>
        LinkGateOrder Direct(Component traveller, Vector3 body, Vector3 destination);

        /// <summary>The traveller will not be asked again: it was teleported, disabled or destroyed mid-crossing.</summary>
        void Release(Component traveller);
    }

    /// <summary>The gates that are live, so a walking-time estimate can charge for the ones a path crosses.</summary>
    public static class NavLinkGates
    {
        // A path corner sits on the link's end, to the mesh's precision.
        private const float EndTolerance = 1.5f;

        private static readonly List<INavLinkGate> Live = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        public static void Register(INavLinkGate gate)
        {
            if (!Live.Contains(gate)) Live.Add(gate);
        }

        public static void Unregister(INavLinkGate gate) => Live.Remove(gate);

        /// <summary>Seconds the crossings between two consecutive path corners cost beyond walking: 0 unless the step is a gated link.</summary>
        public static float TransitSecondsBetween(Vector3 from, Vector3 to)
        {
            float seconds = 0f;
            foreach (INavLinkGate gate in Live)
            {
                gate.Ends(out Vector3 start, out Vector3 end);
                bool forward = Near(from, start) && Near(to, end);
                bool back = Near(from, end) && Near(to, start);
                if (forward || back) seconds += gate.TransitSeconds;
            }
            return seconds;
        }

        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= EndTolerance * EndTolerance;
    }
}
