// Players interact with exactly four things in a nomad settlement: a building's door (InteractableTrigger forwarding
// to a SceneTransition), a pen's gate (DoorInteraction), a Seat and a cart (Pushable). Everything else — props, fixtures,
// furniture — is scenery, so no settlement prefab may carry any other IInteractable: one on a decoration is a solid collider
// that answers the crosshair and blocks the door behind it. A seat is low and a cart stands out in the open, never in front of a door.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class SettlementInteractableTests
    {
        private const string SettlementDir = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement";

        private static readonly System.Type[] Allowed = { typeof(DoorInteraction), typeof(InteractableTrigger), typeof(Seat), typeof(Pushable) };

        [Test]
        public void NoSettlementPrefab_OffersAnythingButDoorsGatesSeatsAndCarts()
        {
            var offenders = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SettlementDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour is not IInteractable || Allowed.Contains(behaviour.GetType())) continue;
                    offenders.Add($"{path}: {behaviour.GetType().Name} on {behaviour.name}");
                }
            }

            Assert.IsEmpty(offenders, "only DoorInteraction, InteractableTrigger, Seat and Pushable may be interactable in a settlement:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void PropsAndFixtures_AreNotInteractable()
        {
            Assert.IsFalse(typeof(IInteractable).IsAssignableFrom(typeof(SettlementProp)));
            Assert.IsFalse(typeof(IInteractable).IsAssignableFrom(typeof(SettlementFixture)));
        }
    }
}
