using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DesertCrawlerWorkerTests
    {
        private static GameObject Crawler() => AssetDatabase.LoadAssetAtPath<GameObject>(DesertCrawlerBuilder.PrefabPath);

        [Test]
        public void BelongsToTheStriders()
        {
            var faction = new SerializedObject(Crawler().GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
        }

        [Test]
        public void FollowsAColumn_AndWorksAWideRingAtAStop()
        {
            var formation = Crawler().GetComponent<FormationModule>();
            Assert.IsNotNull(formation);
            var so = new SerializedObject(formation);
            Assert.AreEqual(ModulePriority.Social, so.FindProperty("priority").intValue);
            Assert.IsEmpty(so.FindProperty("formationId").stringValue, "inert until the city names the band");
            Assert.AreEqual(DesertCrawlerBuilder.WorkRadius, so.FindProperty("restRadius").floatValue, 0.01f);
            Assert.IsNotNull(Crawler().GetComponent<SpaceGame.Vehicles.CrawlerToolModule>(), "the tool rig is the rock collecting");
        }

        [Test]
        public void StillReplicatesAndSaves_AfterAWholesaleRebuild()
        {
            var net = Crawler().GetComponent<Unity.Netcode.NetworkObject>();
            Assert.IsNotNull(net); Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsFalse(string.IsNullOrEmpty(Crawler().GetComponent<SpaceGame.Core.Persistence.SaveableEntity>().PrefabId));
        }
    }
}
