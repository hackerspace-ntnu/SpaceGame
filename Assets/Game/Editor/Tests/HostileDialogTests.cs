// You cannot chat with something that is trying to kill you.
//
// Nothing connected the two halves of a character: DialogInteraction answered "yes, you may talk to
// me" from its own line counter alone, and AgentTargeting decided who the same character was
// fighting without ever being consulted about it. So provoking a Nomad left it chasing the player
// and swinging at them with "Press E" still lit on the crosshair, and pressing E opened a
// conversation mid-fight.
//
// The rule under test is deliberately narrow: being in a fight with THIS interactor closes the
// conversation. A character busy fighting somebody else still talks to a bystander, which is the
// difference between a rule about who is attacking you and a rule about who is busy.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class HostileDialogTests
    {
        private const string FaunaPath = "Assets/Game/ScriptableObjects/Factions/Core/FaunaFaction.asset";
        private const string PlayerPath = "Assets/Game/ScriptableObjects/Factions/Core/PlayerFaction.asset";
        private const string TablePath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject New(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Missing asset: {path}");
            return asset;
        }

        /// <summary>
        /// Runs Awake and OnEnable by hand — Unity calls neither for a component created in edit
        /// mode, and everything under test lives in them: AgentTargeting synthesises its settings
        /// in Awake, ProvocationModule subscribes to damage in OnEnable. Same helper as
        /// ProvocationTests, for the same reason.
        /// </summary>
        private static void Boot(GameObject go)
        {
            foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
            {
                Call(behaviour, "Awake");
                Call(behaviour, "OnEnable");
            }
        }

        private static void Call(MonoBehaviour behaviour, string method)
        {
            System.Reflection.MethodInfo info = behaviour.GetType().GetMethod(
                method, System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
            info?.Invoke(behaviour, null);
        }

        /// <summary>A talkative character that fights back — the Nomad's component set.</summary>
        private DialogInteraction BuildTalker(out HealthComponent health)
        {
            GameObject npc = New("Nomad");
            health = npc.AddComponent<HealthComponent>();
            npc.AddComponent<EntityFaction>()
               .SetFaction(Load<FactionDefinition>(FaunaPath), Load<FactionRelationshipTable>(TablePath));
            npc.AddComponent<AgentTargeting>();
            npc.AddComponent<ProvocationModule>();
            var dialog = npc.AddComponent<DialogInteraction>();

            Boot(npc);
            return dialog;
        }

        private Interactor BuildPlayer(string name)
        {
            GameObject player = New(name);
            player.AddComponent<HealthComponent>();     // TargetResolution wants a live IDamageable
            player.AddComponent<EntityFaction>()
                  .SetFaction(Load<FactionDefinition>(PlayerPath), Load<FactionRelationshipTable>(TablePath));
            var interactor = player.AddComponent<Interactor>();
            Boot(player);
            return interactor;
        }

        private static bool MayTalkTo(DialogInteraction dialog, Interactor interactor) =>
            ((IContextualInteractable)dialog).CanInteract(interactor);

        [Test]
        public void APeacefulCharacter_TalksToAnybody()
        {
            DialogInteraction dialog = BuildTalker(out _);

            Assert.IsTrue(MayTalkTo(dialog, BuildPlayer("Player")),
                "Nothing has happened between these two. The prompt must still appear.");
        }

        [Test]
        public void ACharacterFightingYou_WillNotTalk()
        {
            DialogInteraction dialog = BuildTalker(out HealthComponent health);
            Interactor player = BuildPlayer("Player");

            health.Damage(10, player.transform);

            Assert.IsTrue(dialog.CanInteract(),
                "The dialogue itself is still available — the refusal has to come from the " +
                "contextual test, or it would silence this character for everyone forever.");
            Assert.IsFalse(MayTalkTo(dialog, player),
                "It is chasing this player and swinging at them. There is no conversation to open.");
        }

        [Test]
        public void ACharacterFightingSomebodyElse_StillTalksToYou()
        {
            DialogInteraction dialog = BuildTalker(out HealthComponent health);
            Interactor attacker = BuildPlayer("Attacker");
            Interactor bystander = BuildPlayer("Bystander");

            health.Damage(10, attacker.transform);

            Assert.IsFalse(MayTalkTo(dialog, attacker));
            Assert.IsTrue(MayTalkTo(dialog, bystander),
                "The rule is about who is attacking you, not about who is busy.");
        }

        [Test]
        public void AGrudgeDropped_ReopensTheConversation()
        {
            DialogInteraction dialog = BuildTalker(out HealthComponent health);
            Interactor player = BuildPlayer("Player");

            health.Damage(10, player.transform);
            Assert.IsFalse(MayTalkTo(dialog, player));

            dialog.GetComponent<ProvocationModule>().Forget();

            Assert.IsTrue(MayTalkTo(dialog, player),
                "Once it has calmed down it is talkable again — the refusal tracks the fight, " +
                "not a one-way flag set by the first hit.");
        }

        /// <summary>
        /// The interactor is a component somewhere inside the player's body, and targeting holds
        /// the entity root. Comparing the two raw transforms answers "no" every time.
        /// </summary>
        [Test]
        public void AnInteractorOnAChildObject_IsStillRecognisedAsTheAttacker()
        {
            DialogInteraction dialog = BuildTalker(out HealthComponent health);

            GameObject player = New("Player");
            player.AddComponent<HealthComponent>();
            player.AddComponent<EntityFaction>()
                  .SetFaction(Load<FactionDefinition>(PlayerPath), Load<FactionRelationshipTable>(TablePath));
            Boot(player);

            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(player.transform);
            var interactor = rig.AddComponent<Interactor>();

            health.Damage(10, player.transform);

            Assert.IsFalse(MayTalkTo(dialog, interactor),
                "The Interactor lives on the camera rig, not on the entity root that targeting " +
                "acquired. Compare by hierarchy or this refusal never fires in the real game.");
        }
    }
}
