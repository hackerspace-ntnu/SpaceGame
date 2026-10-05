// Netcode defaults a prefab builder has to put back after building its prefab in the open scene.
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class NetworkObjectDefaults
    {
        /// <summary>
        /// Turns scene migration sync back on in a saved prefab asset. Call it once the builder has
        /// written the prefab for the last time — after every save and every probe-and-apply pass.
        /// </summary>
        /// <remarks>
        /// <c>NetworkObject.OnValidate</c> treats any copy that lives in a build-listed open scene — the
        /// scratch root a builder creates, or a probe instance it edits and applies back — as in-scene
        /// placed and switches <see cref="NetworkObject.SceneMigrationSynchronization"/> off, and the save
        /// or apply bakes that into the asset. Setting it on the scratch root is therefore not enough:
        /// a later apply puts it back. The asset itself is in no scene, so the fix sticks there. A
        /// spawned agent needs it on, or clients never hear that it walked into another chunk scene.
        /// </remarks>
        public static void KeepSceneMigrationSync(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var netObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (netObject == null)
                throw new System.InvalidOperationException(
                    $"'{prefabPath}' is not a prefab with a root NetworkObject.");
            if (netObject.SceneMigrationSynchronization) return;

            netObject.SceneMigrationSynchronization = true;
            EditorUtility.SetDirty(netObject);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }
    }
}
