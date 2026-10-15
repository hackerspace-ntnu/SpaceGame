// The measured reach of the work actions, read off the assets: every action ActionReachMeasurer measures carries a plausible
// reach and, where its work lands at one moment, a Contact time for each variant. A clip replaced or a tool re-gripped
// leaves the stored number stale until Measure Action Reach runs again; this catches the action that was never measured at all.
using System.Linq;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ActionReachAssetTests
    {
        // A Raxy is about 1.5 times a human: nothing it works is farther than an arm and a tool in front, or higher than a raised pick.
        private const float MostForward = 3f, MostUp = 3.5f, MostSideways = 1.5f;

        [Test]
        public void EveryMeasuredJobExistsAndCarriesAPlausibleReach()
        {
            foreach (ActionReachMeasurer.Job job in ActionReachMeasurer.Jobs)
            {
                CharacterAction action = HumanoidControllerBuilder.CollectActions().FirstOrDefault(a => a.name == job.Action);
                Assert.IsNotNull(action, $"'{job.Action}' is in the reach table but is not an action");
                Assert.IsTrue(action.HasReach, $"'{job.Action}' has no measured reach: run Tools > SpaceGame > Animation > Measure Action Reach");

                Vector3 reach = action.Reach;
                Assert.That(reach.z, Is.InRange(-MostForward, MostForward), $"'{job.Action}' reach forward {reach.z:0.00} m");
                Assert.That(reach.y, Is.InRange(-0.5f, MostUp), $"'{job.Action}' reach up {reach.y:0.00} m");
                Assert.That(reach.x, Is.InRange(-MostSideways, MostSideways), $"'{job.Action}' reach to the right {reach.x:0.00} m");
            }
        }

        [Test]
        public void AStrikeActionKnowsWhenItsWorkLandsInEveryVariant()
        {
            foreach (ActionReachMeasurer.Job job in ActionReachMeasurer.Jobs.Where(j => j.Rule != ActionReachMeasurer.ContactRule.CycleMean))
            {
                CharacterAction action = HumanoidControllerBuilder.CollectActions().First(a => a.name == job.Action);
                for (int v = 0; v < action.VariantCount; v++)
                    Assert.IsTrue(action.TryGetMark(CharacterAction.Mark.Contact, v, out float at) && at > 0f && at < 1f,
                                  $"'{job.Action}' variant {v} has no Contact time: its reach was measured at a moment nothing records");
            }
        }
    }
}
