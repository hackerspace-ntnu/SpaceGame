// TEMPORARY. Makes the conjurer say why it is not moving.
//
// Every link in the chain that gets this creature walking has been verified correct on the prefab
// and in the assets -- faction, relationship table, acquisition range, module priority, motor
// reference, authority -- and it still stands there. That means the fault is in a runtime value
// nothing serialises, and guessing at those one per play session is slow.
//
// So this walks the chain in order and prints where it stops. The order matters: each line is a
// precondition for the next, and the FIRST one that reads wrong is the fault. Everything below it
// is a consequence.
//
// Delete this component and its line in LightningConjurerBuilder once the creature walks.
using System.Text;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Locomotion;

namespace SpaceGame.Creatures
{
    public class ConjurerDebugReadout : MonoBehaviour
    {
        [Tooltip("Seconds between readouts. The console is noisy enough without this every frame.")]
        [SerializeField] private float interval = 1f;
        [Tooltip("Turn off to silence it without removing the component.")]
        [SerializeField] private bool logToConsole = true;

        private AgentTargeting targeting;
        private EntityFaction faction;
        private AgentController controller;
        private LeggedLocomotion locomotion;
        private LeggedDriver driver;
        private float timer;

        private void Awake()
        {
            targeting = GetComponent<AgentTargeting>();
            faction = GetComponent<EntityFaction>();
            controller = GetComponent<AgentController>();
            locomotion = GetComponent<LeggedLocomotion>();
            driver = GetComponent<ConjurerDriver>();
        }

        private void Update()
        {
            if (!logToConsole) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Mathf.Max(0.1f, interval);

            var sb = new StringBuilder("[ConjurerDebug]\n");

            // 1. Can it be a participant at all? No faction means it is invisible to targeting and
            //    can never acquire, silently -- the single most common cause of an inert creature.
            sb.Append("  faction        : ")
              .Append(faction == null ? "MISSING" : faction.Faction == null ? "no FactionDefinition"
                                                                           : faction.Faction.name)
              .Append('\n');

            // 2. Is anything registered to be found? The registry is populated by EntityFaction.
            //    OnEnable, so a player that never enabled one is a registry with nothing hostile in
            //    it and a creature correctly deciding to do nothing.
            int registered = EntityTargetRegistry.All != null ? EntityTargetRegistry.All.Count : 0;
            sb.Append("  registry       : ").Append(registered).Append(" entity(ies)\n");

            // 3. Does the registry consider anything hostile to US, ignoring range entirely? This
            //    separates "nothing is hostile" (a faction/table problem) from "nothing is close
            //    enough" (a range problem), which look identical from the outside.
            if (faction != null)
            {
                Transform nearest = EntityTargetRegistry.ResolveNearest(
                    faction, FactionRelationship.Hostile, transform.position);
                sb.Append("  nearest hostile: ")
                  .Append(nearest == null
                      ? "NONE at any range"
                      : $"{nearest.name} at {Vector3.Distance(transform.position, nearest.position):0.0} m")
                  .Append('\n');
            }

            // 4. What did targeting actually decide, and inside what radius was it looking? A
            //    SightRange far below the authored 10 would mean something is scaling it down.
            if (targeting != null)
            {
                sb.Append("  targeting      : ")
                  .Append(targeting.HasTarget ? $"TARGET {targeting.Target.name} at " +
                                                $"{targeting.DistanceToTarget:0.0} m"
                                              : "no target")
                  .Append($"  (sight {targeting.SightRange:0.0} m, lose {targeting.LoseRange:0.0} m)\n");
            }

            // 5. Is the brain even running? AgentController skips its whole module stack on a
            //    machine that does not simulate the entity.
            sb.Append("  simulates here : ")
              .Append(controller == null ? "no AgentController" : controller.SimulatesHere.ToString())
              .Append('\n');

            // 6. Are the legs bound and willing to carry a command? A MaxSpeed of zero clamps every
            //    twist to zero, which stops the gait clock -- the machine stands with its feet
            //    planted no matter what the brain asks for.
            if (locomotion != null)
            {
                sb.Append("  locomotion     : ready=").Append(locomotion.IsReady)
                  .Append(" legs=").Append(locomotion.LegCount)
                  .Append($" ride={locomotion.RideHeight:0.00} max={locomotion.MaxSpeed:0.00} m/s")
                  .Append(" falling=").Append(locomotion.IsFalling)
                  .Append('\n');

                LeggedLocomotion.Diagnostics d = locomotion.LastFrame;
                sb.Append($"  gait           : speed={d.AchievedSpeed:0.00} stance={d.StanceLegs} " +
                          $"swing={d.SwingingLegs} unreachable={d.UnreachableLegs} " +
                          $"reach={d.WorstReachFraction:0.00} phase={d.Phase:0.00}\n");
            }

            // 7. Where is the driver being told to go? Non-null means the brain produced a
            //    destination and the fault is below here; null means it never did.
            if (driver != null)
                sb.Append("  driver         : velocity=").Append(driver.Velocity.magnitude.ToString("0.00"))
                  .Append(" following path=").Append(driver.IsFollowingPath).Append('\n');

            Debug.Log(sb.ToString(), this);
        }
    }
}
