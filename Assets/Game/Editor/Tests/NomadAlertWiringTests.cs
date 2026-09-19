// The five sand nomads, read off disk: can one of them tell the rest that a fight has started?
//
// This is a PREFAB test, not a behaviour test, because the way this has actually failed is that
// the builder's component list grew and nobody re-ran it. AlertBroadcaster and AlertReceiverModule
// were in NomadPrefabBuilder's list and on none of the five prefabs, so "alert your allies" was
// wired, tested at the module level by AlertChainTests, and still did nothing in the game: shoot
// one nomad in a caravan of four and the other three kept grazing.
//
// Reads the asset rather than the class for the usual reason (INVARIANTS.md): a serialized field
// keeps whatever was last saved into it, so a default changed in C# proves nothing about what ships.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public class NomadAlertWiringTests
    {
        private static GameObject[] Nomads()
        {
            var prefabs = new System.Collections.Generic.List<GameObject>();

            foreach (NomadPrefabBuilder.NomadRecipe recipe in NomadPrefabBuilder.SandNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                if (prefab == null)
                    Assert.Ignore($"{recipe.PrefabPath} has not been built " +
                                  "(Tools > SpaceGame > Agents > Build Sand Nomad NPCs).");
                prefabs.Add(prefab);
            }

            return prefabs.ToArray();
        }

        private static int Int(Object target, string field) =>
            new SerializedObject(target).FindProperty(field).intValue;

        private static float Float(Object target, string field) =>
            new SerializedObject(target).FindProperty(field).floatValue;

        [Test]
        public void EveryNomadCanRaiseTheAlarmAndAnswerIt()
        {
            foreach (GameObject nomad in Nomads())
            {
                Assert.IsNotNull(nomad.GetComponent<AlertBroadcaster>(),
                                 $"{nomad.name} cannot tell the caravan he is being shot");
                Assert.IsNotNull(nomad.GetComponent<AlertReceiverModule>(),
                                 $"{nomad.name} cannot be told");
                Assert.IsNotNull(nomad.GetComponent<ProvocationModule>(),
                                 $"{nomad.name} has nothing to turn an alert into a grudge");
            }
        }

        [Test]
        public void EveryNomadHearsAGunshotAndACryForHelp()
        {
            foreach (GameObject nomad in Nomads())
            {
                var ears = nomad.GetComponent<NoiseReceiverModule>();
                Assert.IsNotNull(ears,
                    $"{nomad.name} is deaf — a caravan on the march is longer than the 35 m " +
                    "alert radius, so the far half never learns the near half is in a fight");

                Assert.AreEqual((int)NoiseTypeMask.Gunshot, Int(ears, "investigateOn"),
                    $"{nomad.name} should go and LOOK at a gunshot — it may be nothing to do with him");
                Assert.AreEqual((int)NoiseTypeMask.Hurt, Int(ears, "aggroOn"),
                    $"{nomad.name} should take a tribesman's cry as a fight, and nothing else — " +
                    "aggroing on a gunshot turns every hunt into an ambush");
            }
        }

        /// <summary>
        /// The receiver has to outrank nothing in particular, but it must not tie with the
        /// fallback: a script-added module keeps priority 0 (Reset is not called for AddComponent),
        /// which is exactly where WanderModule sits, and the alert then loses the coin toss.
        /// </summary>
        [Test]
        public void TheReactiveModulesOutrankWandering()
        {
            foreach (GameObject nomad in Nomads())
            {
                Assert.AreEqual(ModulePriority.Reactive - 1,
                                Int(nomad.GetComponent<AlertReceiverModule>(), "priority"),
                                $"{nomad.name}'s alert response");
                Assert.AreEqual(ModulePriority.Reactive - 2,
                                Int(nomad.GetComponent<NoiseReceiverModule>(), "priority"),
                                $"{nomad.name}'s hearing");
                Assert.AreEqual(ModulePriority.Fallback,
                                Int(nomad.GetComponent<WanderModule>(), "priority"),
                                $"{nomad.name}'s wander");
            }
        }

        /// <summary>
        /// A sighting must NOT be announced. The Sand Tribe is Neutral toward the player, so there
        /// is nothing to announce today — but the flag is what stops a future hostile row turning
        /// a caravan into a posse that hunts on first glance.
        /// </summary>
        [Test]
        public void ANomadAnnouncesBeingHitButNotMerelySeeingSomeone()
        {
            foreach (GameObject nomad in Nomads())
            {
                var broadcaster = nomad.GetComponent<AlertBroadcaster>();
                Assert.IsFalse(new SerializedObject(broadcaster).FindProperty("announceSightings").boolValue,
                               $"{nomad.name} would raise the caravan on anyone who walks past");
                Assert.AreEqual(35f, Float(broadcaster, "alertRadius"), 1e-3f, nomad.name);
            }
        }

        /// <summary>
        /// Both reactions have to survive a reload, or a caravan converging on the player forgets
        /// it was ever told anything the moment the world loads. Neither saver is listed in the
        /// builder — SaveablePolicy adds one per module during Wire Saveable Prefabs — so this is
        /// also the guard that that pass actually ran.
        /// </summary>
        [Test]
        public void WhatTheCaravanWasToldSurvivesAReload()
        {
            foreach (GameObject nomad in Nomads())
            {
                Assert.IsNotNull(nomad.GetComponent<AlertResponseSaveable>(), nomad.name);
                Assert.IsNotNull(nomad.GetComponent<NoiseInvestigationSaveable>(), nomad.name);
                Assert.IsNotNull(nomad.GetComponent<ProvocationSaveable>(), nomad.name);
                Assert.IsFalse(string.IsNullOrEmpty(nomad.GetComponent<SaveableEntity>().PrefabId),
                               $"{nomad.name} has no prefabId and will not come back at all");
            }
        }
    }
}
