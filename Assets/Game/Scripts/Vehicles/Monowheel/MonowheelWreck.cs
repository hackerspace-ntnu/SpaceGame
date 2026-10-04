// What a monowheel does when it is killed: it stops dead and can no longer be ridden.
//
// HealthReactionModule parks a corpse by switching its AgentController off; the wheel's corpse
// despawn is off (despawnDelay 0), because a wreck its group lost stays in the world for
// AbandonedVehicle's lifetime. That is not enough for a physics vehicle: the MonowheelMotor runs
// on its own FixedUpdate and would keep rolling towards its last destination until the body is
// taken away, and the MountModule would keep offering the saddle of a wreck. So on death this stops the motor, switches it off, and switches the mount off
// (which dismounts a player riding it). Everyone the wheel carried gets down: the NPC driver
// (NpcPassenger.Dismount) and a double's gunners (MountedGunners.ReleaseGunners) are stood beside
// the wreck and fight on foot as the group's fighters -- counted, and killed, like any other --
// instead of riding it until the wreck is taken away and vanishing with it.
//
// Every one of those consequences is the authority's. HealthComponent also raises OnDeath through
// RestoreHealth, which on a client is how the server's death arrives (NetworkedHealthComponent):
// that copy only stops its own motor. It does not switch its MountModule off -- the dismount that
// causes is the server's to replicate, and a client acting first would drop its player ahead of it
// -- and it lets nobody down (the seats are the server's; their reparenting replicates). A revive
// undoes exactly what this did to the wheel; riders already let down stay on foot.
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthComponent), typeof(MonowheelMotor))]
    public sealed class MonowheelWreck : MonoBehaviour
    {
        private HealthComponent health;
        private MonowheelMotor motor;
        private MountModule mount;
        private MountedGunners gunners;
        private NpcPassenger passenger;
        private bool motorWasEnabled;
        private bool mountWasEnabled;
        private bool wrecked;

        private void Awake() => Resolve();

        private void OnEnable()
        {
            Resolve();
            health.OnDeath += Wreck;
            health.OnRevive += Repair;
        }

        private void OnDisable()
        {
            health.OnDeath -= Wreck;
            health.OnRevive -= Repair;
        }

        private void Resolve()
        {
            if (health) return;
            health = GetComponent<HealthComponent>();
            motor = GetComponent<MonowheelMotor>();
            mount = GetComponent<MountModule>();
            gunners = GetComponent<MountedGunners>();
            passenger = GetComponent<NpcPassenger>();
        }

        /// <summary>Stop, take the motor and the saddle away, and let the driver and any gunners down. Idempotent.</summary>
        public void Wreck()
        {
            Resolve();
            if (wrecked) return;
            wrecked = true;

            motor.ForceStop();
            motorWasEnabled = motor.enabled;
            motor.enabled = false;

            // Switching the mount off dismounts its player; only the machine that decides may do that.
            mountWasEnabled = mount && mount.enabled && Network.Decides;
            if (mountWasEnabled) mount.enabled = false;

            // A client's replicated death: the riders are the server's to let down (see the header).
            if (health.IsRestoring) return;
            if (gunners) gunners.ReleaseGunners();
            if (passenger) passenger.Dismount();
        }

        /// <summary>Give back exactly what <see cref="Wreck"/> took.</summary>
        public void Repair()
        {
            if (!wrecked) return;
            wrecked = false;

            if (motorWasEnabled) motor.enabled = true;
            if (mountWasEnabled && mount) mount.enabled = true;
        }
    }
}
