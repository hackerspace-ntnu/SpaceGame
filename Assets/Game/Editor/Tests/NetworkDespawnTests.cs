// NetworkDespawn: a despawn must not leave an NPC's held weapon floating where its hand was.
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class NetworkDespawnTests
    {
        private GameObject npc;

        [TearDown]
        public void TearDown()
        {
            if (npc != null) Object.DestroyImmediate(npc);
        }

        [Test]
        public void ALocalCopyInTheHand_IsFound_ButNotTheHolderItself()
        {
            npc = new GameObject("Strider");
            npc.AddComponent<NetworkObject>();
            var hand = new GameObject("RightHand").transform;
            hand.SetParent(npc.transform);
            var gun = new GameObject("NetGun(Clone)");
            gun.transform.SetParent(hand);
            NetworkObject heldCopy = gun.AddComponent<NetworkObject>();
            var plain = new GameObject("Strap");
            plain.transform.SetParent(hand);

            CollectionAssert.AreEqual(new[] { heldCopy }, NetworkDespawn.LocalCopies(npc),
                "the held copy carries the item prefab's NetworkObject, never spawned: Netcode would lift it to the root");
        }

        [Test]
        public void EveryDespawnInTheGame_GoesThroughNetworkDespawn()
        {
            // A direct NetworkObject.Despawn is how the floating weapons came back (2026-10-05).
            var direct = new Regex(@"\.Despawn\(\s*(destroy\s*:\s*)?(true|false)?\s*\)");
            string helper = Path.GetFullPath("Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkDespawn.cs");

            string[] offenders = Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                .Where(path => Path.GetFullPath(path) != helper)
                .Where(path => File.ReadLines(path).Any(line => !line.TrimStart().StartsWith("//") && direct.IsMatch(line)))
                .ToArray();

            CollectionAssert.IsEmpty(offenders, "call NetworkDespawn.Despawn instead of NetworkObject.Despawn");
        }
    }
}
