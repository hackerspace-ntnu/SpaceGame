// Who a house takes in (household first, then kin, then anyone), that nobody is a visitor until the house says so, and
// that the room's places answer from an index range no settlement place can reach.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class HouseVisitTests
    {
        private const int Draws = 200;

        [Test]
        public void NoCandidates_ChoosesNobody()
        {
            Assert.AreEqual(-1, GuestPick.Choose(new List<GuestCandidate>(), new System.Random(1)));
        }

        [Test]
        public void ALoneCandidate_IsChosen()
        {
            Assert.AreEqual(0, GuestPick.Choose(new List<GuestCandidate> { new GuestCandidate(false, false) }, new System.Random(1)));
        }

        [Test]
        public void ThoseWhoLiveThere_AlwaysComeBeforeKinAndStrangers()
        {
            var candidates = new List<GuestCandidate>
            {
                new GuestCandidate(false, false), new GuestCandidate(false, true), new GuestCandidate(true, false), new GuestCandidate(false, true),
            };
            for (int seed = 0; seed < Draws; seed++)
                Assert.AreEqual(2, GuestPick.Choose(candidates, new System.Random(seed)), $"seed {seed}");
        }

        [Test]
        public void KinAlwaysComeBeforeStrangers()
        {
            var candidates = new List<GuestCandidate> { new GuestCandidate(false, false), new GuestCandidate(false, true) };
            int kin = 0;
            for (int seed = 0; seed < Draws; seed++)
                if (GuestPick.Choose(candidates, new System.Random(seed)) == 1) kin++;

            Assert.AreEqual(Draws, kin, "a friend's weight is the full spread of the draw, so a stranger never overtakes one");
        }

        [Test]
        public void TwoEqualCandidates_BothGetChosenAcrossDraws()
        {
            var candidates = new List<GuestCandidate> { new GuestCandidate(false, false), new GuestCandidate(false, false) };
            var chosen = new HashSet<int>();
            for (int seed = 0; seed < Draws; seed++) chosen.Add(GuestPick.Choose(candidates, new System.Random(seed)));

            Assert.AreEqual(2, chosen.Count);
        }

        [Test]
        public void AResidentNobodyInvited_IsNotOnAVisit()
        {
            var body = new GameObject("NotAGuest", typeof(Resident));
            try
            {
                Assert.IsFalse(HouseVisits.TryGetSegment(body.GetComponent<Resident>(), out _));
                Assert.IsFalse(HouseVisits.TryGetSegment(null, out _));
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }

        [Test]
        public void WithNoRoomLoaded_NoRoomPlaceAnswers()
        {
            Assert.IsNull(HouseRoom.PlaceAt(HouseRoom.PlaceBase));
            Assert.IsNull(HouseRoom.SpotAt(HouseRoom.PlaceBase));
        }

        [Test]
        public void ARoomsFirstPlaceIsAboveAnyIndexASettlementGathers()
        {
            Assert.Greater(HouseRoom.PlaceBase, 100000, "doors, camps, spots, trip and patrol points of any settlement stay far below");
        }
    }
}
