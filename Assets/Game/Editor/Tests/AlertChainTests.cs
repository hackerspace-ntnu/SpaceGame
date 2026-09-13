// The alert chain, end to end, on real components: one agent learns of a target and its allies in
// range are handed it — as a grudge where they can hold one — while a neutral bystander, an ally
// out of range, and an ally of a receiver (who must not be reached by a second-hand alert) are
// left alone. Plus the settlement alarm's clock.
//
// EditMode, so Awake/OnEnable never run: every component resolves its neighbours lazily for
// exactly this reason, and the registry is fed by hand where OnEnable would have done it.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class AlertChainTests
    {
        private FactionDefinition clankers, humans, fauna;
        private FactionRelationshipTable table;
        private readonly System.Collections.Generic.List<GameObject> made = new();

        [SetUp]
        public void SetUp()
        {
            clankers = Faction("Clankers");
            humans = Faction("Humans");
            fauna = Faction("Fauna");
            table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            var rows = new UnityEditor.SerializedObject(table).FindProperty("relationships");
            rows.arraySize = 1;
            var row = rows.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("factionA").objectReferenceValue = clankers;
            row.FindPropertyRelative("factionB").objectReferenceValue = humans;
            row.FindPropertyRelative("relationship").enumValueIndex = (int)FactionRelationship.Hostile;
            rows.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made)
            {
                if (go == null) continue;
                if (go.TryGetComponent(out EntityFaction f)) EntityTargetRegistry.Unregister(f);
                Object.DestroyImmediate(go);
            }
            made.Clear();
            Object.DestroyImmediate(table);
            Object.DestroyImmediate(clankers);
            Object.DestroyImmediate(humans);
            Object.DestroyImmediate(fauna);
        }

        private static FactionDefinition Faction(string name)
        {
            var f = ScriptableObject.CreateInstance<FactionDefinition>();
            f.factionName = name;
            f.ID = System.Guid.NewGuid().ToString("N");
            return f;
        }

        private GameObject Entity(string name, FactionDefinition faction, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var ef = go.AddComponent<EntityFaction>();
            ef.SetFaction(faction, table);
            EntityTargetRegistry.Register(ef);
            made.Add(go);
            return go;
        }

        private GameObject Clanker(string name, Vector3 at, bool withGrudge = true)
        {
            var go = Entity(name, clankers, at);
            go.AddComponent<AgentTargeting>();
            SetRadius(go.AddComponent<AlertBroadcaster>(), 30f);
            go.AddComponent<AlertReceiverModule>();
            if (withGrudge)
            {
                go.AddComponent<HealthComponent>();
                go.AddComponent<ProvocationModule>();
            }
            return go;
        }

        private static void SetRadius(AlertBroadcaster b, float radius)
        {
            var so = new UnityEditor.SerializedObject(b);
            so.FindProperty("alertRadius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void AnAllyInRangeIsHandedTheTargetAsAGrudge()
        {
            var player = Entity("Player", humans, new Vector3(50f, 0f, 0f));
            var spotter = Clanker("Spotter", Vector3.zero);
            var ally = Clanker("Ally", new Vector3(20f, 0f, 0f));

            spotter.GetComponent<AlertBroadcaster>().Broadcast(player.transform, player.transform.position);

            Assert.AreEqual(player.transform, ally.GetComponent<AgentTargeting>().Target);
            Assert.IsTrue(ally.GetComponent<ProvocationModule>().IsProvoked, "the alert must stick as a grudge");
            Assert.AreEqual(player.transform, ally.GetComponent<ProvocationModule>().Aggressor);
        }

        [Test]
        public void AnAllyOutOfRangeAndANeutralBystanderHearNothing()
        {
            var player = Entity("Player", humans, new Vector3(50f, 0f, 0f));
            var spotter = Clanker("Spotter", Vector3.zero);
            var farAlly = Clanker("FarAlly", new Vector3(0f, 0f, 45f));
            var golem = Entity("Golem", fauna, new Vector3(5f, 0f, 0f));
            golem.AddComponent<AgentTargeting>();
            golem.AddComponent<AlertReceiverModule>();

            spotter.GetComponent<AlertBroadcaster>().Broadcast(player.transform, player.transform.position);

            Assert.IsNull(farAlly.GetComponent<AgentTargeting>().Target, "45 m is past a 30 m alert");
            Assert.IsNull(golem.GetComponent<AgentTargeting>().Target, "Fauna is not allied with Clankers");
        }

        [Test]
        public void AnAlertDoesNotCascade()
        {
            // Spotter reaches Relay (20 m). Relay reaches Far (another 20 m). Far is 40 m from the
            // Spotter, outside its radius: if Far hears anything, the relay re-broadcast.
            var player = Entity("Player", humans, new Vector3(0f, 0f, -50f));
            var spotter = Clanker("Spotter", Vector3.zero);
            var relay = Clanker("Relay", new Vector3(20f, 0f, 0f));
            var far = Clanker("Far", new Vector3(40f, 0f, 0f));

            spotter.GetComponent<AlertBroadcaster>().Broadcast(player.transform, player.transform.position);

            Assert.AreEqual(player.transform, relay.GetComponent<AgentTargeting>().Target);
            Assert.IsNull(far.GetComponent<AgentTargeting>().Target, "a received alert was re-broadcast");
        }

        [Test]
        public void ASightingIsAnnouncedButAForcedTargetIsNot()
        {
            var player = Entity("Player", humans, new Vector3(0f, 0f, -50f));
            var spotter = Clanker("Spotter", Vector3.zero);
            var ally = Clanker("Ally", new Vector3(20f, 0f, 0f));
            var spotterBroadcaster = spotter.GetComponent<AlertBroadcaster>();

            // OnEnable does not run in EditMode, and SendMessage("OnEnable") trips Unity's own
            // ShouldRunBehaviour assertion; call the subscription directly instead.
            typeof(AlertBroadcaster)
                .GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(spotterBroadcaster, null);
            var targeting = spotter.GetComponent<AgentTargeting>();

            targeting.ForceTarget(player.transform);
            Assert.IsNull(ally.GetComponent<AgentTargeting>().Target, "a forced target must not be announced");

            targeting.ClearTarget();
            ally.GetComponent<AgentTargeting>().ClearTarget();

            // A provocation from a hit IS announced.
            spotter.GetComponent<ProvocationModule>().Provoke(player.transform);
            Assert.AreEqual(player.transform, ally.GetComponent<AgentTargeting>().Target);
        }

        [Test]
        public void AlarmRaisesOnFirstIntruderHoldsThenLowers()
        {
            var state = new SettlementAlarmLogic.State();

            var s0 = SettlementAlarmLogic.Step(ref state, false, 0f, 20f, 8f);
            Assert.IsFalse(s0.Raised); Assert.IsFalse(s0.PlaySiren);

            var s1 = SettlementAlarmLogic.Step(ref state, true, 1f, 20f, 8f);
            Assert.IsTrue(s1.Raised); Assert.IsTrue(s1.PlaySiren); Assert.IsTrue(state.Raised);

            var s2 = SettlementAlarmLogic.Step(ref state, true, 5f, 20f, 8f);
            Assert.IsFalse(s2.Raised); Assert.IsFalse(s2.PlaySiren, "not yet time for the repeat");

            var s3 = SettlementAlarmLogic.Step(ref state, true, 9.5f, 20f, 8f);
            Assert.IsTrue(s3.PlaySiren, "siren repeats every 8 s while raised");

            // Intruder leaves at t=10; steps back in at t=15: still raised, no second raise.
            SettlementAlarmLogic.Step(ref state, false, 10f, 20f, 8f);
            var s5 = SettlementAlarmLogic.Step(ref state, true, 15f, 20f, 8f);
            Assert.IsFalse(s5.Raised); Assert.IsTrue(state.Raised);

            // Leaves for good at t=16; lowered once 20 s have passed since the last sighting.
            SettlementAlarmLogic.Step(ref state, false, 16f, 20f, 8f);
            var s7 = SettlementAlarmLogic.Step(ref state, false, 30f, 20f, 8f);
            Assert.IsFalse(s7.Lowered); Assert.IsTrue(state.Raised);
            var s8 = SettlementAlarmLogic.Step(ref state, false, 35.5f, 20f, 8f);
            Assert.IsTrue(s8.Lowered); Assert.IsFalse(state.Raised);
        }

        [Test]
        public void RegistryAnswersForAFactionThatIsNotAnEntity()
        {
            Entity("Player", humans, new Vector3(10f, 0f, 0f));
            Entity("FarPlayer", humans, new Vector3(500f, 0f, 0f));
            Entity("Guard", clankers, new Vector3(-10f, 0f, 0f));
            Entity("Golem", fauna, Vector3.zero);

            var hostile = new System.Collections.Generic.List<EntityFaction>();
            EntityTargetRegistry.Query(clankers, table, FactionRelationship.Hostile, Vector3.zero, 100f, hostile);
            Assert.AreEqual(1, hostile.Count);
            Assert.AreEqual("Player", hostile[0].name);

            var allied = new System.Collections.Generic.List<EntityFaction>();
            EntityTargetRegistry.Query(clankers, table, FactionRelationship.Allied, Vector3.zero, 100f, allied);
            Assert.AreEqual(1, allied.Count);
            Assert.AreEqual("Guard", allied[0].name);
        }
    }
}
