// The prefab's NavMeshAgent decides autoBraking. NavMeshAgentMotor.Awake used to force it off on
// every agent, overriding the 48 prefabs that author it on, with no recorded reason.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class NavMeshAgentMotorBrakingTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void AwakeKeepsTheAuthoredAutoBraking(bool authored)
        {
            var go = new GameObject("NavMeshAgentMotorBrakingTestAgent");
            try
            {
                // Far from anything any other test could have put on the shared NavMesh.
                go.transform.position = new Vector3(500_000f, 500_000f, 500_000f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.autoBraking = authored;
                var motor = go.AddComponent<NavMeshAgentMotor>();

                typeof(NavMeshAgentMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                         .Invoke(motor, null);

                Assert.AreEqual(authored, agent.autoBraking, "the motor must not override the prefab's setting");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
