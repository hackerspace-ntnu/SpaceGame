using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class GroupMembershipTests
    {
        [Test]
        public void AMemberWithNoHealth_IsNotAFighter()
        {
            var machine = new GameObject("Walker");
            var group = new NpcGroup { Id = "city" };
            try
            {
                GroupMembership m = GroupMembership.Stamp(machine, group, 0, null);
                Assert.IsFalse(m.IsFighter, "an indestructible carrier can never be beaten");
                CollectionAssert.DoesNotContain(group.Fighters, machine);
            }
            finally { Object.DestroyImmediate(machine); }
        }

        [Test]
        public void AMemberWithHealth_IsStillAFighter()
        {
            var person = new GameObject("Nomad");
            person.AddComponent<HealthComponent>();
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();   // Enlist applies the tribe
            var group = new NpcGroup { Id = "city" };
            try
            {
                Assert.IsTrue(GroupMembership.Stamp(person, group, 0, tribe).IsFighter);
                CollectionAssert.Contains(group.Fighters, person);
            }
            finally { Object.DestroyImmediate(person); Object.DestroyImmediate(tribe); }
        }
    }
}
