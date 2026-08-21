// Which machine runs a timed effect on a player's body, and what it puts back when it ends.
//
// The bug these pin is the third instance of one shape: the player's NetworkTransform is
// AuthorityMode: Owner, so anything the SERVER does to that Rigidbody is overwritten by the owner's
// next state update, silently, within a tick. NetworkedTeleport was the first, NetMsg.RopeTug the
// second, and an effect registered from UsableItem.Use() — the authority-only half — was the third.
// It floated the server's kinematic copy of a client and nothing else.
//
// So the split is: consuming the item on the authority (Use, counted against maxUses, hotbar slot
// removed), and the physics on the machine that owns the body, driven from Present. The tests below
// hold both halves of that in place, plus the two ways an effect that toggles a flag goes wrong once
// it can overlap with itself.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class EffectItemAuthorityTests
    {
        private const BindingFlags Hidden =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        private readonly List<GameObject> spawned = new();

        private GameObject player;
        private Rigidbody body;
        private EffectManager effects;

        [SetUp]
        public void SetUp()
        {
            player = NewObject("player", typeof(Rigidbody));
            body = player.GetComponent<Rigidbody>();
            body.useGravity = true;

            effects = player.AddComponent<EffectManager>();

            // Edit-mode tests get no Awake for an AddComponent, and EffectManager resolves the
            // Rigidbody it acts on there. Without this it holds a null and refuses every effect.
            Invoke(effects, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            spawned.Clear();
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            spawned.Add(go);
            return go;
        }

        private AntiGravityPotion Potion()
        {
            // Its own GameObject, the way an equipped item prefab actually lives — parented into
            // the holder's hand, not merged into their body.
            return NewObject("potion").AddComponent<AntiGravityPotion>();
        }

        private static void Invoke(Component component, string method) =>
            component.GetType().GetMethod(method, Hidden)?.Invoke(component, null);

        /// <summary>
        /// Step the effect clock until nothing is running.
        ///
        /// Counted in steps rather than seconds on purpose: the manager ticks on
        /// <see cref="Time.fixedDeltaTime"/>, which is a project setting, and a test that computed
        /// its own step count from it would quietly start passing for the wrong reason if anybody
        /// changed the physics rate. The bound is generous — a hundred seconds at the default rate.
        /// </summary>
        private void RunEffectsUntilIdle()
        {
            for (int i = 0; i < 5000 && ActiveEffectCount() > 0; i++) Invoke(effects, "FixedUpdate");

            Assert.AreEqual(0, ActiveEffectCount(),
                "No effect expired in five thousand physics steps. Either the timer is not being " +
                "decremented, or Time.fixedDeltaTime is zero.");
        }

        private int ActiveEffectCount()
        {
            var list = (List<Effect>)typeof(EffectManager)
                .GetField("activeEffects", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(effects);

            return list.Count;
        }

        // ─────────── Which half runs the effect ───────────

        [Test]
        public void TheAuthorityHalfDoesNotTouchTheBody()
        {
            AntiGravityPotion potion = Potion();

            potion.TryUse(player);

            Assert.IsTrue(body.useGravity,
                "Use() runs on the server alone. A client's body is owner-authoritative, so a " +
                "server-side write to it is overwritten within a tick — the potion floated the " +
                "server's kinematic copy of that player and nobody ever saw it.");
            Assert.AreEqual(0, ActiveEffectCount(),
                "Nothing may be registered from the authority half at all, or the server runs a " +
                "second copy of the timer against a body it does not own.");
        }

        [Test]
        public void ThePresentHalfAppliesItOnTheMachineThatOwnsTheBody()
        {
            AntiGravityPotion potion = Potion();

            potion.PlayUse(player);

            Assert.IsFalse(body.useGravity,
                "Present() runs on every machine and is filtered on Network.Owns, so the holder " +
                "applies it immediately and with no round trip inside the feel of it.");
            Assert.AreEqual(1, ActiveEffectCount());
        }

        [Test]
        public void ConsumingTheItemStaysWithTheServer()
        {
            Assert.AreEqual(UseAuthority.Server, Potion().Authority,
                "The charge count and the hotbar slot it is removed from are shared state, and " +
                "PlayerInventoryNetwork's hotbar is server-authoritative. Owner authority would " +
                "count the use once per machine.");
        }

        [Test]
        public void EveryEffectItemKeepsServerAuthority()
        {
            foreach (Type type in typeof(EffectItem).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(EffectItem).IsAssignableFrom(type)) continue;

                var item = (EffectItem)NewObject(type.Name).AddComponent(type);

                Assert.AreEqual(UseAuthority.Server, item.Authority,
                    $"{type.Name} overrides Authority back to Owner. The whole point of EffectItem " +
                    "is that the two halves live on different machines; declaring Owner puts the " +
                    "consumption back on the client that pressed.");
            }
        }

        [Test]
        public void TheAuthorityOnlyHalfIsSealedShut()
        {
            MethodInfo use = typeof(EffectItem).GetMethod("Use", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(use, "EffectItem must still implement UsableItem's abstract Use().");
            Assert.IsTrue(use.IsFinal,
                "Use() has to be sealed. It is the obvious-looking place to register an effect and " +
                "it is the wrong machine — every EffectItem written so far put it there. Sealing it " +
                "means the only hook on offer is ApplyEffect, which runs where the body is.");
        }

        [Test]
        public void TheTimerRunsOnThePhysicsClock()
        {
            Assert.IsNull(typeof(EffectManager).GetMethod("Update", Hidden),
                "An effect's tick is where it pushes the body, and forces integrate once per " +
                "physics step. Run from Update, a 120 fps player floated four times as hard as a " +
                "30 fps one — in a session those two are looking at each other.");
            Assert.IsNotNull(typeof(EffectManager).GetMethod("FixedUpdate", Hidden));
        }

        // ─────────── What it puts back ───────────

        [Test]
        public void TheEffectRestoresWhatItFoundRatherThanAssertingGravity()
        {
            // Gravity is already off: this player is riding something (MountModule turns it off and
            // restores what it captured) or is being lifted clear by the under-terrain guard.
            body.useGravity = false;

            Potion().PlayUse(player);
            RunEffectsUntilIdle();

            Assert.IsFalse(body.useGravity,
                "An effect that ends by asserting `true` hands gravity back to a rider mid-flight, " +
                "and — worse — one that ends AFTER a mount captured its own `false` leaves that " +
                "player with gravity off for good.");
            Assert.AreEqual(0, ActiveEffectCount(), "The effect should have expired.");
        }

        [Test]
        public void ASecondPotionReplacesTheFirstInsteadOfRunningBesideIt()
        {
            Potion().PlayUse(player);
            Potion().PlayUse(player);

            Assert.AreEqual(1, ActiveEffectCount(),
                "Two overlapping anti-gravity effects cannot both be right about gravity: the " +
                "first one's expiry switches it back on in the middle of the second one's float. " +
                "The per-item field that used to guard this could not, because the second potion " +
                "is a freshly instantiated prefab that has never heard of the first.");
            Assert.IsFalse(body.useGravity, "The replacement must leave the player still floating.");

            RunEffectsUntilIdle();

            Assert.IsTrue(body.useGravity, "…and put gravity back exactly once, at the end.");
        }

        [Test]
        public void DestroyingTheBodyUndoesWhateverWasStillRunning()
        {
            Potion().PlayUse(player);
            Assert.IsFalse(body.useGravity);

            UnityEngine.Object.DestroyImmediate(effects);

            Assert.IsTrue(body.useGravity,
                "A body torn down mid-effect never reaches the expiry that puts it back, and a " +
                "flag left switched off on a Rigidbody something else still holds is the same " +
                "shape as the kinematic flag that made loaded worlds unplayable.");
        }
    }
}
