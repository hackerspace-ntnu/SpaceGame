// Every person and animal can be put to sleep by simulation distance; no machine ever can.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DistanceDormancyPrefabTests
    {
        [TestCase("Assets/Game/Prefabs/Agents/Characters/Nomad.prefab", true)]
        [TestCase("Assets/Game/Prefabs/agents/creatures/CrabWalker6.prefab", true)]
        [TestCase("Assets/Game/Prefabs/Agents/Robots/Clanker.prefab", true)]
        [TestCase("Assets/Game/Prefabs/Agents/Caravan/NomadAppa.prefab", true)]
        [TestCase("Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab", false)]
        [TestCase("Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkyCityFleet.prefab", false)]
        [TestCase("Assets/Game/Prefabs/Spikes/Raxy_Gunner.prefab", false)]
        public void SubjectPathsAreThePeopleAndAnimalFolders(string path, bool subject)
        {
            Assert.AreEqual(subject, DistanceDormancyWiring.IsSubjectPath(path));
        }

        [Test]
        public void EveryAgentPrefabIsClassifiedByItsFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLowerInvariant().Contains("/prefabs/spikes/")) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool subject = DistanceDormancyWiring.IsSubjectPath(path);

                foreach (AgentController agent in prefab.GetComponentsInChildren<AgentController>(true))
                {
                    bool marked = agent.GetComponent<DistanceDormant>() != null;
                    Assert.AreEqual(subject, marked,
                        subject ? $"{path}: a person or animal without DistanceDormant (run Tools/SpaceGame/Agents/Wire Distance Dormancy)"
                                : $"{path}: a machine or structure with DistanceDormant would freeze in the distance");
                }
            }
        }
    }
}
