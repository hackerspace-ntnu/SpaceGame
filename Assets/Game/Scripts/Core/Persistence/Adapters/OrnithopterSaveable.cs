using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists whether the ornithopter was in the air, and how fast.
    ///
    /// <b>This one is a safety fix as much as a fidelity one.</b> The craft's pose and velocity are
    /// already restored by the record and <see cref="RigidbodySaveable"/>, so a save taken in flight
    /// reloads the craft at altitude — and without this it reloads there NOT flying, with the flight
    /// model switched off and gravity on. The player watches their aircraft drop out of the sky as the
    /// reward for saving mid-journey.
    ///
    /// <b>Deferred, because launching needs the world under it.</b> <c>Launch</c> writes the pose and
    /// starts the flight model; run during hydration it would fight the store still placing the object,
    /// and the terrain the flight model measures against may not have streamed in yet.
    ///
    /// Restored through the same <c>Launch</c> the game uses rather than by assigning a flight state,
    /// so there is one way a flight begins. The cost is that stamina and flap phase restart — flap phase
    /// is meaningless to a player, and stamina restarting full is generous rather than wrong.
    /// </summary>
    [RequireComponent(typeof(OrnithopterFlightMotor))]
    public class OrnithopterSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "ornithopter";

        private OrnithopterFlightMotor motor;

        private OrnithopterFlightMotor Motor =>
            motor != null ? motor : motor = GetComponent<OrnithopterFlightMotor>();

        public string SaveKey => Key;

        private bool pendingFlying;
        private float pendingAirspeed;

        private MountModule mount;

        /// <summary>
        /// Hands a restored craft back to the wing pack that conceptually owns it.
        ///
        /// <b>Why a saver is doing runtime rewiring.</b> The craft is spawned by
        /// <c>WingPackItem</c> and torn down by it on landing, but a load rebuilds the craft through
        /// the save system, which knows nothing about wing packs — so the restored flight has no owner
        /// and never ends. The pack cannot fix this from its own side: it is equipped while the
        /// player's inventory is restored, which is strictly BEFORE the deferred pass that re-seats
        /// the rider, so at the moment it is equipped there is nothing yet to adopt.
        ///
        /// Driven off <c>Mounted</c> rather than off this saver's own <see cref="OnLoadComplete"/>
        /// because two savers on one entity run in component order, which is not an ordering anyone
        /// may depend on — and the rider arrives via <see cref="MountSaveable"/>, the other one.
        /// Subscribing to the event that actually reports the rider is order-independent.
        ///
        /// Also fires on the normal launch, where <c>AdoptCraft</c> is a no-op because the pack
        /// already holds the craft. That is deliberate: one path, exercised every flight, rather than
        /// a load-only path exercised almost never.
        /// </summary>
        private void OnEnable()
        {
            mount = GetComponent<MountModule>();
            if (mount != null) mount.Mounted += HandleRiderSeated;
        }

        private void OnDisable()
        {
            if (mount != null) mount.Mounted -= HandleRiderSeated;
        }

        private void HandleRiderSeated(PlayerMovement rider)
        {
            if (rider == null) return;

            WingPackItem pack = rider.GetComponentInChildren<WingPackItem>(true);
            if (pack != null) pack.AdoptCraft(gameObject);
        }

        public struct State
        {
            public bool flying;
            public float airspeed;
        }

        public object CaptureState() => Motor == null
            ? null
            : new State { flying = Motor.IsFlying, airspeed = Motor.Airspeed };

        public void RestoreState(JObject state)
        {
            pendingFlying = false;
            if (state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            pendingFlying = restored.flying;
            pendingAirspeed = restored.airspeed;
        }

        public void OnLoadComplete()
        {
            if (!pendingFlying || Motor == null) return;

            // Consumed, so a second pass (a chunk hydrating later) cannot relaunch a craft that has
            // since landed.
            pendingFlying = false;

            if (Motor.IsFlying) return;

            Motor.Launch(transform.forward, pendingAirspeed);
        }
    }
}
