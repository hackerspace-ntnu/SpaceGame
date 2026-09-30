using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class StriderTribeAssetTests
    {
        private static FactionDefinition Striders() => AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath);

        [Test]
        public void StriderFaction_ExistsWithNeutralDefaultAndAnId()
        {
            FactionDefinition striders = Striders();
            Assert.IsNotNull(striders, "run Tools/SpaceGame/Agents/Author Strider Faction");
            Assert.AreEqual("The Striders", striders.factionName);
            Assert.AreEqual(FactionRelationship.Neutral, striders.defaultStance,
                "a Hostile-default faction is never tracked by the goodwill ledger (spacegame-tribe §1)");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(RosterAuthoring.StriderFactionPath), striders.ID);
        }

        [Test]
        public void EveryStriderNomad_IsBakedOnTheStriders_AndCanWalkToAGoal()
        {
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                Assert.IsNotNull(prefab, $"{recipe.PrefabPath} — run Tools/SpaceGame/Agents/Build Strider Nomad NPCs");
                var baked = new SerializedObject(prefab.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
                Assert.AreEqual(Striders(), baked, $"{prefab.name}: faction is not replicated, it must be baked");
                Assert.IsNotNull(prefab.GetComponent<GoalTravelModule>(), $"{prefab.name}: crew walk back to the gangway by goal");
                Assert.IsNotNull(prefab.GetComponent<Unity.Netcode.NetworkObject>());
            }
        }

        [Test]
        public void StriderCloth_IsItsOwnMaterials_NotSandsOrSkys()
        {
            string[] prefixes = { "NomadCloth_", "SkyNomadCloth_" };
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials.Where(m => m != null))
                    Assert.IsFalse(prefixes.Any(p => material.name.StartsWith(p)),
                        $"{prefab.name} wears {material.name}: one tribe's build would recolour another's");
            }
        }
    }
}
