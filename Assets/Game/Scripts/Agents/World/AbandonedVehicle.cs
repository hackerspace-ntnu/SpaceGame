// A group's vehicle that its group has lost -- its NPC driver knocked off or killed, or the vehicle
// itself destroyed -- left lying where it stopped for a while instead of vanishing with the group.
//
// Without this a defeated war-party monowheel stayed one of the group's Live members, so it went
// wherever the group went: a wreck blinked out with the corpse despawn (8 s), and a driverless wheel
// was taken away the moment the party folded or disbanded. Players who had just won the fight saw
// their prize disappear.
//
// So the moment a member is defeated this hands it to NpcWorldSim.ReleaseDefeated, which takes it
// out of the group the way a player taking it does (ReleaseToPlayer): out of Live and the column,
// its seats stood down so nobody re-crews it, and its record handed to the world store. From then
// on it counts down `lifetime`; once that is up it is taken away the first moment no player is
// within `unseenDistance`, so nobody watches it blink out. A player who mounts it before then makes
// it theirs for good: it never counts down again (MountModule switches a wreck's saddle off, so only
// a driverless wheel can be claimed).
//
// The countdown only runs while the vehicle is in the world: its remaining lifetime is saved
// (AbandonedVehicleSaveable), so a save/quit/load, or its chunk unloading, pauses it rather than
// resetting it. Server-side only; clients only ever see the network object stay and then despawn.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public sealed class AbandonedVehicle : MonoBehaviour
    {
        public enum Stage
        {
            /// <summary>Still its group's, crewed or not yet crewed.</summary>
            Serving,
            /// <summary>Its group lost it; counting down to being taken away.</summary>
            Abandoned,
            /// <summary>Nobody's to take away: a player claimed it, or it never belonged to a group.</summary>
            Kept,
        }

        [Tooltip("Seconds a vehicle its group lost stays in the world before it is taken away. A player " +
                 "mounting it first makes it theirs for good.")]
        [SerializeField] private float lifetime = 300f;

        [Tooltip("Once its lifetime is up it goes the first moment no player is within this distance " +
                 "(metres, flat), so nobody watches it blink out.")]
        [SerializeField] private float unseenDistance = 150f;

        public Stage Current { get; private set; } = Stage.Serving;

        /// <summary>Seconds left before it may be taken away. Meaningful only while <see cref="Stage.Abandoned"/>.</summary>
        public float Remaining { get; private set; }

        public float Lifetime => lifetime;

        private GroupMembership membership;
        private HealthComponent health;
        private NpcPassenger passenger;
        private MountModule mount;
        private bool hadDriver;
        private readonly List<Vector3> players = new();

        private void Awake()
        {
            TryGetComponent(out health);
            TryGetComponent(out passenger);
            TryGetComponent(out mount);
        }

        private void Update()
        {
            if (!Network.Decides) return;
            Tick(Time.deltaTime);
        }

        /// <summary>One server step. Public so the rule can be tested without the player loop.</summary>
        public void Tick(float delta)
        {
            if (Current == Stage.Serving) WatchForDefeat();
            else if (Current == Stage.Abandoned) CountDown(delta);
        }

        /// <summary>Count down from <paramref name="remaining"/> seconds. Also how a load puts back a saved countdown.</summary>
        public void Abandon(float remaining)
        {
            Current = Stage.Abandoned;
            Remaining = Mathf.Max(0f, remaining);
        }

        /// <summary>A vehicle its group has lost: its NPC driver gone after having had one, or itself destroyed.</summary>
        public static bool IsDefeated(bool hadDriver, bool hasDriver, bool alive) =>
            !alive || (hadDriver && !hasDriver);

        /// <summary>Its lifetime is up and nobody is close enough to see it go.</summary>
        public static bool ShouldTakeAway(float remaining, bool playerNear) =>
            remaining <= 0f && !playerNear;

        private void WatchForDefeat()
        {
            bool hasDriver = passenger != null && passenger.HasRider;
            if (hasDriver) hadDriver = true;

            // Stamped before the network spawn, so absent here means it never was a group's -- or a
            // player took it (NpcWorldSim.ReleaseToPlayer cleared the membership): theirs to keep.
            if (membership == null) TryGetComponent(out membership);
            if (membership == null || membership.Group == null)
            {
                Current = Stage.Kept;
                return;
            }

            if (!IsDefeated(hadDriver, hasDriver, health == null || health.Alive)) return;

            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return;

            sim.ReleaseDefeated(gameObject);
            Abandon(lifetime);
        }

        private void CountDown(float delta)
        {
            if (mount != null && mount.IsMounted)
            {
                Current = Stage.Kept;
                return;
            }

            Remaining = Mathf.Max(0f, Remaining - delta);
            if (ShouldTakeAway(Remaining, PlayerWithin(unseenDistance))) NpcSpawn.Remove(gameObject);
        }

        private bool PlayerWithin(float distance)
        {
            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return false;

            sim.CollectPlayerPositions(players);
            Vector3 here = transform.position;

            foreach (Vector3 player in players)
            {
                Vector3 delta = player - here;
                delta.y = 0f;
                if (delta.sqrMagnitude <= distance * distance) return true;
            }

            return false;
        }
    }
}
