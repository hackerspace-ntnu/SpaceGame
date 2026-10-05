// The four Strider humanoids are the user's own models (strider1.blend, rigged in
// strider_characters.blend): humanoid avatars on renamed prefabs, NPC height, one skinned mesh each.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class StriderCharacterAssetTests
    {
        private const float Height = 3f;
        private const float HeightTolerance = 0.1f;
        private const int MaxSkinnedRenderers = 6;

        private static readonly string[] Names = { "Strider_Horned", "Strider_Longcoat", "Strider_Warrior", "Strider_Beanie" };

        [Test]
        public void TheRecipes_AreTheFourUserModels_OnTheirRenamedPrefabs()
        {
            CollectionAssert.AreEqual(Names, NomadPrefabBuilder.StriderNomads.Select(r => r.Name).ToArray(),
                "index order is the old prefabs' (Umber, Tan, Maroon, StrawHat): the crab and monowheel riders take index 0");
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                StringAssert.StartsWith("Assets/Game/Art/Models/Characters/Striders/", recipe.FbxPath);
                Assert.AreEqual($"Assets/Game/Prefabs/Agents/Characters/Striders/{recipe.Name}.prefab", recipe.PrefabPath);
                Assert.IsNull(recipe.ClothPalette, $"{recipe.Name} keeps the user's palette colours");
            }
        }

        [Test]
        public void EveryModel_ImportsAsAValidHumanoid()
        {
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(recipe.FbxPath).OfType<Avatar>().FirstOrDefault();
                Assert.IsNotNull(avatar, $"{recipe.FbxPath} has no avatar");
                Assert.IsTrue(avatar.isValid && avatar.isHuman, $"{recipe.FbxPath}: a non-human avatar stands in bind pose");
            }
        }

        [Test]
        public void EveryPrefab_StandsThreeMetresTall_OnAtMostSixSkinnedRenderers()
        {
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                Assert.IsNotNull(prefab, recipe.PrefabPath);
                Assert.LessOrEqual(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, MaxSkinnedRenderers, prefab.name);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    float low = float.MaxValue, high = float.MinValue;
                    foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        low = Mathf.Min(low, r.bounds.min.y);
                        high = Mathf.Max(high, r.bounds.max.y);
                    }
                    Assert.AreEqual(Height, high - low, HeightTolerance, $"{prefab.name} is not NPC height");
                    Assert.AreEqual(0f, low, 0.05f, $"{prefab.name}'s soles are not on its root");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }
    }
}
