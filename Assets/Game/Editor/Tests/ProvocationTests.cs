// The rules that keep the Golem and the Nomad peaceful, and the one thing that makes them fight.
//
// "Peaceful" here is not a disabled module or a behaviour flag — it is the absence of anyone to be
// hostile toward. Every combat module acts only when AgentTargeting holds a target, AgentTargeting
// only ever asks the registry for entities it is Hostile toward, and FactionRelationshipTable
// answers Neutral for any pair it has no row for. A faction with no rows therefore cannot acquire
// anyone, and the creature wanders instead.
//
// That is elegant and it is also fragile in one specific way: it depends on an ABSENCE. Nothing in
// the editor stops someone from adding a Fauna/Player row to GlobalRelationships, and the moment
// they do, every Fauna creature reverts to attacking on sight with no code change and no error.
// The first test here is a guard on that absence.
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class ProvocationTests
    {
        private const string FaunaPath = "Assets/Game/ScriptableObjects/Factions/Core/FaunaFaction.asset";
        private const string PlayerPath = "Assets/Game/ScriptableObjects/Factions/Core/PlayerFaction.asset";
        private const string WildlifePath = "Assets/Game/ScriptableObjects/Factions/Core/WildlifeFaction.asset";
        private const string TablePath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";
        private const string GolemPath = "Assets/Game/Prefabs/Agents/Creatures/Golem.prefab";
        private const string NomadPath = "Assets/Game/Prefabs/Agents/Characters/Nomad.prefab";

        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        private GameObject New(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Missing asset: {path}");
            return asset;
        }

        // ─────────────────────────────────────────────
        //  The absence that makes a creature peaceful
        // ─────────────────────────────────────────────

        [Test]
        public void Fauna_IsNeutralTowardThePlayer()
        {
            var table = Load<FactionRelationshipTable>(TablePath);
            var fauna = Load<FactionDefinition>(FaunaPath);
            var player = Load<FactionDefinition>(PlayerPath);

            Assert.AreEqual(FactionRelationship.Neutral, table.Get(fauna, player),
                "Fauna must have NO row in GlobalRelationships. A Fauna/Player row — of any " +
                "relationship other than Neutral — puts the Golem back to attacking on sight, " +
                "with no code change anywhere to explain it.");
        }

        [Test]
        public void Fauna_QueryFindsNoHostileCandidates()
        {
            var table = Load<FactionRelationshipTable>(TablePath);

            GameObject creature = New("Creature");
            var creatureFaction = creature.AddComponent<EntityFaction>();
            creatureFaction.SetFaction(Load<FactionDefinition>(FaunaPath), table);

            GameObject player = New("Player");
            var playerFaction = player.AddComponent<EntityFaction>();
            playerFaction.SetFaction(Load<FactionDefinition>(PlayerPath), table);

            // Re-register: SetFaction ran after OnEnable already put them in the registry.
            EntityTargetRegistry.Register(creatureFaction);
            EntityTargetRegistry.Register(playerFaction);

            var results = new System.Collections.Generic.List<EntityFaction>();
            EntityTargetRegistry.Query(creatureFaction, FactionRelationship.Hostile,
                                       Vector3.zero, 1000f, results);

            Assert.IsEmpty(results,
                "A Fauna creature must find nobody to be hostile toward — that, and nothing " +
                "else, is what stops Chase and CloseCombat from ever claiming a frame.");
        }

        // The Golem moved to Fauna rather than Wildlife being made peaceful, precisely so the two
        // creatures still on Wildlife keep hunting. If that row ever goes, they go passive silently.
        [Test]
        public void Wildlife_IsStillHostileTowardThePlayer()
        {
            var table = Load<FactionRelationshipTable>(TablePath);

            Assert.AreEqual(FactionRelationship.Hostile,
                            table.Get(Load<FactionDefinition>(WildlifePath),
                                      Load<FactionDefinition>(PlayerPath)),
                "DuneRat and Vrescal are still Wildlife and are still meant to attack on sight.");
        }

        // ─────────────────────────────────────────────
        //  Being provoked
        // ─────────────────────────────────────────────

        /// <summary>
        /// Runs Awake and OnEnable by hand.
        ///
        /// <para>
        /// Unity does not call either for components created in edit mode — the same problem
        /// SpiderWalkerGroundingTests solves with a public <c>Initialise</c> on its own component.
        /// It cannot be skipped here, because the entire mechanism under test lives in those two
        /// methods: AgentTargeting synthesises its settings object in Awake, and ProvocationModule
        /// subscribes to the damage event in OnEnable. Without this, nothing is listening, every
        /// creature quietly ignores every hit, and the tests "pass" by asserting on a component
        /// that was never switched on.
        /// </para>
        /// </summary>
        private static void Boot(GameObject go)
        {
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                Call(mb, "Awake");
                Call(mb, "OnEnable");
            }
        }

        private static void Call(MonoBehaviour mb, string method)
        {
            MethodInfo m = mb.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(mb, null);
        }

        private ProvocationModule BuildCreature(out HealthComponent health,
                                                out AgentTargeting targeting)
        {
            GameObject creature = New("Creature");
            health = creature.AddComponent<HealthComponent>();
            creature.AddComponent<EntityFaction>()
                    .SetFaction(Load<FactionDefinition>(FaunaPath),
                                Load<FactionRelationshipTable>(TablePath));
            targeting = creature.AddComponent<AgentTargeting>();
            var provocation = creature.AddComponent<ProvocationModule>();

            Boot(creature);
            return provocation;
        }

        private GameObject BuildAttacker()
        {
            GameObject attacker = New("Attacker");
            attacker.AddComponent<HealthComponent>();   // TargetResolution wants a live IDamageable
            attacker.AddComponent<EntityFaction>()
                    .SetFaction(Load<FactionDefinition>(PlayerPath),
                                Load<FactionRelationshipTable>(TablePath));
            Boot(attacker);
            return attacker;
        }

        [Test]
        public void UntouchedCreature_HoldsNoTarget()
        {
            ProvocationModule provocation = BuildCreature(out _, out AgentTargeting targeting);

            Assert.IsFalse(provocation.IsProvoked);
            Assert.IsFalse(targeting.HasTarget, "A peaceful creature starts with nobody to fight.");
        }

        [Test]
        public void BeingHurt_TurnsTheCreatureOnItsAttacker()
        {
            ProvocationModule provocation = BuildCreature(out HealthComponent health,
                                                          out AgentTargeting targeting);
            GameObject attacker = BuildAttacker();

            health.Damage(10, attacker.transform);

            Assert.IsTrue(provocation.IsProvoked);
            Assert.AreSame(attacker.transform, targeting.Target,
                "The attacker has to reach AgentTargeting — the combat modules read the target " +
                "from there and know nothing about provocation.");
        }

        // The bug this guards is the reason Projectile.cs had to change: damage used to be
        // attributed to the projectile's own transform, which carries no EntityFaction and is
        // destroyed on impact. Attribution has to climb to the entity.
        [Test]
        public void Attribution_ClimbsToTheEntity_NotTheColliderThatCarriedIt()
        {
            ProvocationModule provocation = BuildCreature(out HealthComponent health,
                                                          out AgentTargeting targeting);
            GameObject attacker = BuildAttacker();

            var limb = new GameObject("Hitbox");
            limb.transform.SetParent(attacker.transform);

            health.Damage(10, limb.transform);

            Assert.IsTrue(provocation.IsProvoked);
            Assert.AreSame(attacker.transform, targeting.Target,
                "A child collider is not something the creature can walk toward.");
        }

        [Test]
        public void DamageBelowTheThreshold_IsShruggedOff()
        {
            ProvocationModule provocation = BuildCreature(out HealthComponent health, out _);
            GameObject attacker = BuildAttacker();

            var so = new SerializedObject(provocation);
            so.FindProperty("damageThreshold").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();

            health.Damage(3, attacker.transform);
            Assert.IsFalse(provocation.IsProvoked, "Chip damage must not start a fight.");

            health.Damage(9, attacker.transform);
            Assert.IsTrue(provocation.IsProvoked, "A real hit must.");
        }

        [Test]
        public void SelfInflictedDamage_ProvokesNobody()
        {
            ProvocationModule provocation = BuildCreature(out HealthComponent health, out _);

            health.Damage(10, provocation.transform);

            Assert.IsFalse(provocation.IsProvoked,
                "Falling damage attributed to the creature itself must not make it hunt itself.");
        }

        [Test]
        public void Forgetting_ReleasesTheTarget()
        {
            ProvocationModule provocation = BuildCreature(out HealthComponent health,
                                                          out AgentTargeting targeting);
            GameObject attacker = BuildAttacker();

            health.Damage(10, attacker.transform);
            Assert.IsTrue(targeting.HasTarget);

            provocation.Forget();

            Assert.IsFalse(provocation.IsProvoked);
            Assert.IsFalse(targeting.HasTarget,
                "Calming down has to clear the target too, or the creature keeps fighting " +
                "somebody it has supposedly forgiven.");
        }

        // ─────────────────────────────────────────────
        //  Prefab wiring
        // ─────────────────────────────────────────────

        [Test]
        public void Golem_IsFaunaAndCanBeProvoked()
        {
            var golem = Load<GameObject>(GolemPath);

            var faction = golem.GetComponent<EntityFaction>();
            Assert.IsNotNull(faction, "The Golem needs an EntityFaction to be targetable at all.");
            Assert.AreSame(Load<FactionDefinition>(FaunaPath), faction.Faction,
                           "The Golem is Fauna now, not Wildlife.");

            Assert.IsNotNull(golem.GetComponent<ProvocationModule>(),
                "Without this the Golem is peaceful and stays peaceful — it would take hits " +
                "forever without ever fighting back.");
            Assert.IsNotNull(golem.GetComponent<CloseCombatModule>(),
                "It still needs something to fight WITH once provoked.");
            Assert.IsNotNull(golem.GetComponent<ChaseModule>());
        }

        [Test]
        public void Nomad_CarriesAStaffAndCanBeProvoked()
        {
            var nomad = Load<GameObject>(NomadPath);

            Assert.IsNotNull(nomad.GetComponent<ProvocationModule>());
            Assert.IsNotNull(nomad.GetComponent<CloseCombatModule>(),
                             "He fights with the staff at close range, not at a distance.");
            Assert.IsNull(nomad.GetComponent<AgentRangedCombatModule>(),
                          "The Nomad is deliberately melee-only.");

            Transform staff = nomad.GetComponentsInChildren<Transform>(true)
                                   .FirstOrDefault(t => t.name == "WalkingStaff");
            Assert.IsNotNull(staff, "The Nomad should be holding his walking staff.");

            Assert.IsTrue(staff.parent.name.EndsWith("RightHand"),
                          $"The staff hangs off '{staff.parent.name}', not a hand bone.");

            // An equipped visual must be inert. A NetworkObject here would nest inside the
            // Nomad's own, and a PickupableItem would make an NPC's weapon lootable off his body.
            Assert.IsNull(staff.GetComponentInChildren<Unity.Netcode.NetworkObject>(true),
                "An equipped visual must not be a network prefab.");
        }

        // Both gaits have to land on the blend tree's own sample positions (walk 4.0, run 7.2 in
        // the AstronautArmature "Move" tree). Between them the tree blends two clips at once and
        // the character shuffles; past the top one it clamps and he skates.
        //
        // This is a real trap rather than a hypothetical: the three fields involved look like they
        // do the same job, live on two different components, and only one of them is named after
        // speed at all.
        [Test]
        public void Nomad_WalkAndRunBothLandOnTheirBlendSamples()
        {
            var nomad = Load<GameObject>(NomadPath);

            var agent = nomad.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var motor = nomad.GetComponent<NavMeshAgentMotor>();
            var driver = nomad.GetComponent<AgentAnimatorDriver>();
            Assert.IsNotNull(agent);
            Assert.IsNotNull(motor);
            Assert.IsNotNull(driver);

            float walkMultiplier = new SerializedObject(motor)
                .FindProperty("walkSpeedMultiplier").floatValue;

            var driverSo = new SerializedObject(driver);
            float toBlend = driverSo.FindProperty("animationSpeedMultiplier").floatValue;
            float boost = driverSo.FindProperty("walkAnimBoost").floatValue;

            // AgentAnimatorDriver applies walkAnimBoost only when the intent is NOT running.
            float walkSpeedY = agent.speed * walkMultiplier * toBlend * boost;
            float runSpeedY = agent.speed * toBlend;

            Assert.AreEqual(4.0f, walkSpeedY, 0.15f,
                $"Walking feeds SpeedY {walkSpeedY:0.00}; the walk clip sits at 4.0.");
            Assert.AreEqual(7.2f, runSpeedY, 0.25f,
                $"Provoked he runs, feeding SpeedY {runSpeedY:0.00}; the run clip sits at 7.2. " +
                "A walkSpeedMultiplier of 1 leaves him with no run gear at all, and ChaseModule " +
                "asks to run the moment he is provoked.");

            Assert.Less(walkMultiplier, 1f,
                "He needs a gear below the agent's speed, or walking and chasing are the same pace.");
        }

        // The clip rate is GLOBAL — it scales the staff swing along with the walk. This is the
        // guard on the bug that produced it: matching a deliberately-slow walk to its stride put
        // every one-shot on the character into slow motion as a side effect.
        [Test]
        public void Nomad_DoesNotAnimateInSlowMotion()
        {
            var driver = Load<GameObject>(NomadPath).GetComponent<AgentAnimatorDriver>();
            Assert.IsNotNull(driver);

            float scale = new SerializedObject(driver)
                .FindProperty("animatorSpeedScale").floatValue;

            Assert.GreaterOrEqual(scale, 0.9f,
                $"animatorSpeedScale is {scale:0.00}, which slows EVERY clip including the attack. " +
                "It has to stay near 1, which means the walk speed has to stay near the clip's " +
                "authored stride — the two cannot be tuned independently.");
        }

        [Test]
        public void Nomad_TurnsToFaceYouAtConversationDistance()
        {
            var watch = Load<GameObject>(NomadPath).GetComponent<WatchModule>();
            Assert.IsNotNull(watch, "He should notice you walking up, before you interact.");

            var so = new SerializedObject(watch);

            Assert.AreEqual((int)FactionRelationship.Neutral,
                            so.FindProperty("requiredRelationship").enumValueIndex,
                            "NPCFaction is Neutral toward the player; any other setting and he " +
                            "faces nobody.");

            Assert.GreaterOrEqual(so.FindProperty("detectRadius").floatValue, 5f,
                "The player's Interactor casts 5 m. A shorter radius means he turns to face you " +
                "only after the interact prompt is already up.");

            int priority = so.FindProperty("priority").intValue;
            Assert.Greater(priority, 0,
                "Tied with WanderModule at Fallback he wanders past you about half the time, " +
                "decided by component order.");
            Assert.Less(priority, 20,
                "Above Chase he would stop to politely face someone he is fighting.");
        }
    }
}
