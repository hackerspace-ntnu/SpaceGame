// A standing rider (the walking city's elder) rides its post with its legs parked, and takes them
// back from wherever the post set it down.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Creatures.Crab;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class StandingRiderTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        [Test]
        public void CarriedByASeatedCarrier_ParksTheLegs_SetDown_GivesThemBack()
        {
            var house = new GameObject("House"); junk.Add(house);
            house.AddComponent<VesselSeats>();
            var elder = new GameObject("Elder"); junk.Add(elder);
            var legs = elder.AddComponent<CrabLocomotion>();
            var rider = elder.AddComponent<StandingRider>();

            elder.transform.SetParent(house.transform, true);
            rider.SyncWithCarrier();
            Assert.IsFalse(legs.enabled, "legs left running on a moving deck write the body back every frame");

            elder.transform.SetParent(null, true);
            rider.SyncWithCarrier();
            Assert.IsTrue(legs.enabled, "set down at the gangway, the elder walks again");
        }

        [Test]
        public void AParentThatIsNoCarrier_LeavesTheLegsAlone()
        {
            var holder = new GameObject("Holder"); junk.Add(holder);
            var elder = new GameObject("Elder"); junk.Add(elder);
            var legs = elder.AddComponent<CrabLocomotion>();
            var rider = elder.AddComponent<StandingRider>();

            elder.transform.SetParent(holder.transform, true);
            rider.SyncWithCarrier();
            Assert.IsTrue(legs.enabled);
        }

        [Test]
        public void Is_ReadsTheComponent()
        {
            var elder = new GameObject("Elder"); junk.Add(elder);
            var crew = new GameObject("Crew"); junk.Add(crew);
            elder.AddComponent<StandingRider>();
            Assert.IsTrue(StandingRider.Is(elder));
            Assert.IsFalse(StandingRider.Is(crew));
            Assert.IsFalse(StandingRider.Is(null));
        }
    }
}
