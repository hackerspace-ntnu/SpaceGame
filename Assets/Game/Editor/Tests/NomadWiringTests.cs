// Every nomad NomadPrefabBuilder writes carries the humanoid action wiring, read off disk.
//
// The builder overwrites each prefab wholesale, so the wiring has to come from the builder itself:
// the Wire Humanoid Prefabs pass reached the nomads that existed when it ran and nothing built
// afterwards. A nomad without it keeps the old Hurt/Death trigger names the humanoid controller has
// no parameter for, and takes hits with a clean console and no reaction.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class NomadWiringTests
    {
        private static IEnumerable<string> NomadPrefabs =>
            new[] { NomadPrefabBuilder.Nomad, NomadPrefabBuilder.SkySoldier }
                .Concat(NomadPrefabBuilder.SandNomads)
                .Concat(NomadPrefabBuilder.SkyNomads)
                .Concat(NomadPrefabBuilder.StriderNomads)
                .Select(recipe => recipe.PrefabPath);

        [TestCaseSource(nameof(NomadPrefabs))]
        public void ABuiltNomadCarriesTheHumanoidActionWiring(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Assert.Ignore($"{path} has not been built.");

            Assert.IsTrue(CharacterActionWiring.WearsHumanoidController(prefab),
                $"{path} is a humanoid nomad and must animate with Humanoid.controller");
            Assert.IsNotNull(prefab.GetComponent<CharacterActions>(),
                $"{path} has no CharacterActions: NomadPrefabBuilder must call CharacterActionWiring.Ensure");

            foreach (HealthReactionModule reaction in prefab.GetComponentsInChildren<HealthReactionModule>(true))
            {
                var so = new SerializedObject(reaction);
                Assert.IsEmpty(so.FindProperty("hurtAnimTrigger").stringValue,
                    $"{path}: the humanoid controller has no Hurt trigger");
                Assert.IsEmpty(so.FindProperty("dieAnimTrigger").stringValue,
                    $"{path}: the humanoid controller has no Death trigger");
            }
        }
    }
}
