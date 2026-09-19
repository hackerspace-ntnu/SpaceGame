// The war book: one war per (tribe, player), one party per war, escalation that survives the gap
// between parties, and ids that never collide with a restored party.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class WarBookTests
    {
        private FactionDefinition sand, sky;
        private WarBook book;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = "sand";
            sky = ScriptableObject.CreateInstance<FactionDefinition>();
            sky.ID = "sky";
            book = new WarBook();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(sand);
            Object.DestroyImmediate(sky);
        }

        [Test]
        public void Open_IsIdempotent_PerTribeAndPlayer()
        {
            War a = book.Open(sand, "p1");
            Assert.AreSame(a, book.Open(sand, "p1"));
            Assert.AreNotSame(a, book.Open(sand, "p2"), "two players at war are two wars");
            Assert.AreNotSame(a, book.Open(sky, "p1"));
            Assert.AreEqual(3, book.Wars.Count);
        }

        [Test]
        public void ANewWar_IsReadyToRaiseAtOnce()
        {
            War war = book.Open(sand, "p1");
            Assert.IsTrue(book.ReadyToRaise(war));
            Assert.AreEqual(0, war.Tier);
        }

        [Test]
        public void AssignParty_NamesItAfterTheWar_AndSkipsTakenIds()
        {
            War war = book.Open(sand, "p1");
            string id = book.AssignParty(war, taken => taken == "warparty:sand:p1:1");

            Assert.AreEqual("warparty:sand:p1:2", id);
            Assert.AreEqual(id, war.PartyGroupId);
            Assert.IsFalse(book.ReadyToRaise(war), "one party per war");
            Assert.AreSame(war, book.FindByGroup(id));
        }

        [Test]
        public void Resolve_Defeated_RaisesTier_AndStartsTheCooldown()
        {
            War war = book.Open(sand, "p1");
            book.AssignParty(war, _ => false);

            book.Resolve(war, Reckoning.Defeated, maxTier: 2, cooldown: 180f);

            Assert.AreEqual(1, war.Tier);
            Assert.IsFalse(war.HasParty);
            Assert.IsFalse(book.ReadyToRaise(war));

            book.Tick(179f);
            Assert.IsFalse(book.ReadyToRaise(war));
            book.Tick(1f);
            Assert.IsTrue(book.ReadyToRaise(war));
        }

        [Test]
        public void Cooldown_DoesNotRun_WhileAPartyIsOut()
        {
            War war = book.Open(sand, "p1");
            book.ClearParty(war, 50f);
            book.AssignParty(war, _ => false);

            book.Tick(100f);
            Assert.AreEqual(50f, war.Cooldown);
        }

        [Test]
        public void Close_ForgetsTheTier()
        {
            War war = book.Open(sand, "p1");
            book.AssignParty(war, _ => false);
            book.Resolve(war, Reckoning.Defeated, 2, 0f);

            book.Close(war);

            Assert.IsNull(book.Find(sand, "p1"));
            Assert.AreEqual(0, book.TierFor(sand, "p1"));
            Assert.AreEqual(0, book.Open(sand, "p1").Tier, "a new war starts from scouts");
        }

        [Test]
        public void RestoredTier_SeedsTheNextWarOpened()
        {
            book.RestoreTier(sand, "p1", 2);
            Assert.AreEqual(2, book.TierFor(sand, "p1"));
            Assert.AreEqual(2, book.Open(sand, "p1").Tier);
        }

        [Test]
        public void Adopt_TakesTheRestoredParty_WithoutASecondWar()
        {
            War adopted = book.Adopt(sand, "p1", "warparty:sand:p1:4", 1);

            Assert.AreSame(adopted, book.Open(sand, "p1"));
            Assert.AreEqual(1, book.Wars.Count);
            Assert.AreEqual("warparty:sand:p1:4", adopted.PartyGroupId);
            Assert.AreEqual(1, adopted.Tier);
        }

        [Test]
        public void RestoreTier_ToZero_SyncsAnOpenPartylessWar()
        {
            War war = book.Open(sand, "p1");
            book.AssignParty(war, _ => false);
            book.Resolve(war, Reckoning.Defeated, 2, 0f);

            book.RestoreTier(sand, "p1", 0);

            Assert.AreEqual(0, war.Tier);
            Assert.AreEqual(0, book.TierFor(sand, "p1"));
        }
    }
}
