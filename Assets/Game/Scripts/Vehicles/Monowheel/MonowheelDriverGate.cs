// Keeps a riderless Strider monowheel from driving itself.
//
// Its column brain (FormationModule, GoalTravelModule) belongs to the Strider at the tiller. When
// that rider dies or is knocked off -- or a player who took the wheel steps off it -- nobody is
// driving, and a wheel that kept riding in formation on its own would be a ghost. So while there is
// neither an NPC rider (NpcPassenger) nor a mounted player (MountModule), this parks those modules,
// and it hands them back the moment an NPC rider is aboard again.
//
// It cooperates with MountModule's own suppression rather than fighting it: while a player is
// mounted it does nothing at all (MountModule has switched the modules off and will restore
// exactly what it took), and like MountModule -- through the same ModuleSuppression -- it only ever
// re-enables the modules IT switched off, so a module turned off for another reason stays off. It is derived from live state every frame
// and holds nothing worth saving: after a reload the rider respawns (NpcPassenger.spawnOnStart) or
// does not, and the gate follows. Server-side only; clients never run a brain.
using SpaceGame.Agents;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [DisallowMultipleComponent]
    public sealed class MonowheelDriverGate : MonoBehaviour
    {
        [Tooltip("Whose NPC rider counts as a driver.")]
        [SerializeField] private NpcPassenger passenger;
        [Tooltip("While a player is mounted the gate stands aside: MountModule owns the modules then.")]
        [SerializeField] private MountModule mount;
        [Tooltip("The modules that drive the wheel on the rider's behalf -- parked while nobody drives.")]
        [SerializeField] private MonoBehaviour[] drivenModules = System.Array.Empty<MonoBehaviour>();

        // Exactly the modules this gate switched off, so it gives back only those.
        private readonly ModuleSuppression parked = new ModuleSuppression();

        /// <summary>Whether the gate currently holds the driven modules off.</summary>
        public bool IsParked => parked.IsApplied;

        private void Update()
        {
            if (!Network.Decides) return;
            Tick(passenger != null && passenger.HasRider, mount != null && mount.IsMounted);
        }

        /// <summary>One decision from live state. Public so the rule can be tested without a rider.</summary>
        public void Tick(bool hasNpcRider, bool playerMounted)
        {
            if (playerMounted) return;

            if (hasNpcRider) parked.Restore();
            else parked.Suppress(drivenModules);
        }
    }
}
