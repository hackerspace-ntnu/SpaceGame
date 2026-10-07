// Rosters spec §5.5: what the hunted player is told, and how loudly.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class WarNoticeTextTests
    {
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = "sand";
            sand.factionName = "Sand Tribe";
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(sand);

        [Test]
        public void Raised_IsAWarning_NamingTheTribe()
        {
            var (_, text, severity) = WarNoticeText.For(WarNotice.Raised, sand);
            Assert.AreEqual(MessageSeverity.Warning, severity);
            StringAssert.Contains("Sand Tribe", text);
        }

        [Test]
        public void Weakening_AndGaveUp_AreNotices()
        {
            Assert.AreEqual(MessageSeverity.Notice, WarNoticeText.For(WarNotice.Weakening, sand).Severity);
            Assert.AreEqual(MessageSeverity.Notice, WarNoticeText.For(WarNotice.GaveUp, sand).Severity);
        }

        [Test]
        public void OneSlotPerTribe_SoTheLatestReplacesTheLast()
        {
            Assert.AreEqual(WarNoticeText.For(WarNotice.Raised, sand).Id, WarNoticeText.For(WarNotice.GaveUp, sand).Id);
        }
    }
}
