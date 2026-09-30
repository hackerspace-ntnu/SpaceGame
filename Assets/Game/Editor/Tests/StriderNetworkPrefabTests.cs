using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// Builder-made agents that migrate between chunk scenes must keep scene migration sync, which
    /// NetworkObject.OnValidate switches off on a root built inside an open scene.
    public class StriderNetworkPrefabTests
    {
        private static IEnumerable<string> MigratingPrefabPaths() =>
            NomadPrefabBuilder.StriderNomads.Select(r => r.PrefabPath)
                .Append(StriderCrabOutriderBuilder.PrefabPath)
                .Append(DesertCrawlerBuilder.PrefabPath);

        [TestCaseSource(nameof(MigratingPrefabPaths))]
        public void KeepsSceneMigrationSync(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"missing {path}");
            var netObject = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(netObject, $"{path} has no root NetworkObject");
            Assert.IsTrue(netObject.SceneMigrationSynchronization,
                $"{path}: clients stop seeing it move once it crosses into another chunk scene");
        }
    }
}
