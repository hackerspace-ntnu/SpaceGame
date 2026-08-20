using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Guards the two serialized references that decide whether anybody gets a body at all.
    ///
    /// This is not hypothetical. An asset cleanup deleted PlayerCharacterNetworked.prefab, and both
    /// SpawnManager.networkPlayerPrefab and SingleplayerInitializer.playerPrefab quietly became null
    /// — a missing reference is not a compile error, and nothing complains until Instantiate(null)
    /// throws at spawn time. Singleplayer and multiplayer were both dead on the branch, and
    /// NetworkPrefabRegistrationTests could not see it: an entry pointing at a deleted asset was
    /// caught there, but a FIELD pointing at one was nobody's business.
    ///
    /// A null here is worth a red test rather than a runtime exception, because the runtime symptom
    /// ("nothing happens when I press play") gives no clue which field is empty.
    /// </summary>
    public class PlayerSpawnWiringTests
    {
        private const string SpawnManagerPrefabPath = "Assets/Game/Prefabs/Systems/SpawnManager.prefab";
        private const string SingleplayerPrefabPath = "Assets/Game/Prefabs/Systems/SingeplayerInitializer.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"No prefab at {path}");
            return prefab;
        }

        /// <summary>
        /// Reads a private [SerializeField] the way a broken reference actually presents: as a null
        /// objectReferenceValue on a property that still exists.
        /// </summary>
        private static GameObject SerializedPrefabField(Component component, string field)
        {
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(field);

            Assert.IsNotNull(property, $"{component.GetType().Name} has no serialized field '{field}'. " +
                                       "If it was renamed, rename it here too.");

            return property.objectReferenceValue as GameObject;
        }

        [Test]
        public void SpawnManager_HasAPlayerPrefab()
        {
            var spawnManager = Load(SpawnManagerPrefabPath).GetComponent<SpawnManager>();
            Assert.IsNotNull(spawnManager, "The SpawnManager prefab has no SpawnManager component.");

            Assert.IsNotNull(SerializedPrefabField(spawnManager, "networkPlayerPrefab"),
                "SpawnManager.networkPlayerPrefab is empty, so no player can ever be spawned — " +
                "for a client, for a host, or in singleplayer.");
        }

        [Test]
        public void SingleplayerInitializer_HasAPlayerPrefab()
        {
            var initializer = Load(SingleplayerPrefabPath).GetComponent<SingleplayerInitializer>();
            Assert.IsNotNull(initializer, "The SingleplayerInitializer prefab has no initializer component.");

            Assert.IsNotNull(SerializedPrefabField(initializer, "playerPrefab"),
                "SingleplayerInitializer.playerPrefab is empty, so starting a solo session spawns " +
                "no player.");
        }

        /// <summary>
        /// Everything the player body needs in order to take part in a session.
        ///
        /// Each of these has been missing at some point, and each failed silently in the same shape:
        /// the host was fine and the clients were not, or the action ran locally on a machine with no
        /// authority to run it. Naming them in one place means adding a body-scoped networked feature
        /// gets a line here rather than a bug report.
        /// </summary>
        [Test]
        public void PlayerPrefab_CarriesTheComponentsASessionNeeds()
        {
            var player = Load("Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab");

            Assert.IsNotNull(player.GetComponent<NetworkObject>(),
                "The player has no NetworkObject, so it cannot be spawned for a client.");

            Assert.IsNotNull(player.GetComponent<NetRelay>(),
                "The player has no NetRelay, so every NetMessaging message addressed to it — the " +
                "respawn request among them — runs locally on each machine instead of crossing.");

            Assert.IsNotNull(player.GetComponent<NetworkedTeleport>(),
                "The player has no NetworkedTeleport, so a server-side teleport (respawn, interior " +
                "exit) is overwritten by the owner's own transform sync and silently does nothing.");

            Assert.IsNotNull(player.GetComponent<PlayerRespawn>(),
                "The player has no PlayerRespawn, so the death screen's respawn button does nothing.");

            Assert.IsNotNull(player.GetComponent<PlayerInteriorTransit>(),
                "The player has no PlayerInteriorTransit, so a client walking into a building cannot " +
                "tell the server about it.");

            Assert.IsNotNull(player.GetComponent<PlayerSaveSync>(),
                "The player has no PlayerSaveSync, so nothing binds it to a save profile.");

            Assert.IsNotNull(player.GetComponentInChildren<Interactor>(true),
                "The player has no Interactor anywhere in its hierarchy, so it cannot interact with " +
                "anything.");
        }

        /// <summary>
        /// A missing script leaves a null entry in the component list. On the player prefab that is
        /// worth failing over: this prefab is restored from version control often enough that it
        /// picks up references to components deleted since.
        /// </summary>
        [Test]
        public void PlayerPrefab_HasNoMissingScripts()
        {
            var player = Load("Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab");

            int missing = 0;
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
                foreach (Component component in t.GetComponents<Component>())
                    if (component == null) missing++;

            Assert.Zero(missing,
                $"The player prefab has {missing} missing script(s). Unity refuses to save a prefab " +
                "with these in some flows, and they log on every spawn.");
        }
    }
}
