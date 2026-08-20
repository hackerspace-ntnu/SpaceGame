using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.Core
{
    /// <summary>
    /// Marks a spawn / exit point inside an interior scene.
    /// Self-registers so InteriorManager can look it up by (scene, id) without scanning the hierarchy.
    /// </summary>
    public class InteriorAnchor : MonoBehaviour
    {
        [SerializeField] private string anchorId = "entrance";

        public string AnchorId => anchorId;

        private static readonly Dictionary<(string scene, string id), InteriorAnchor> s_anchors = new();

        /// <summary>
        /// Assign the anchor id at runtime (used by procedurally-spawned anchors, e.g. the cave
        /// entrance). Re-registers under the new key so <see cref="Find"/> resolves it. Safe to call
        /// after the component is already enabled.
        /// </summary>
        public void SetAnchorId(string id)
        {
            if (string.IsNullOrEmpty(id) || id == anchorId) { anchorId = id; return; }

            // Drop the stale registration keyed under the old id, if it was us.
            var oldKey = (gameObject.scene.name, anchorId);
            if (s_anchors.TryGetValue(oldKey, out var existing) && existing == this)
                s_anchors.Remove(oldKey);

            anchorId = id;

            if (isActiveAndEnabled)
                s_anchors[(gameObject.scene.name, anchorId)] = this;
        }

        private void OnEnable()
        {
            var key = (gameObject.scene.name, anchorId);
            s_anchors[key] = this;
        }

        private void OnDisable()
        {
            var key = (gameObject.scene.name, anchorId);
            if (s_anchors.TryGetValue(key, out var existing) && existing == this)
                s_anchors.Remove(key);
        }

        public static InteriorAnchor Find(Scene scene, string anchorId)
        {
            if (!scene.IsValid()) return null;
            s_anchors.TryGetValue((scene.name, anchorId), out var anchor);
            if (anchor != null) return anchor;

            // Fallback: scan the scene root in case OnEnable order missed registration.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<InteriorAnchor>(true))
                {
                    if (candidate.anchorId == anchorId)
                        return candidate;
                }
            }
            return null;
        }

        /// <summary>Find an anchor anywhere in any loaded scene. First match wins.</summary>
        public static InteriorAnchor FindAnywhere(string anchorId)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var a = Find(scene, anchorId);
                if (a != null) return a;
            }
            return null;
        }

        /// <summary>
        /// Teleport a player GameObject to this anchor. Uses Rigidbody.position when present
        /// (zeroing velocities) and falls back to Transform. Same logic as InteriorManager.
        /// </summary>
        public void TeleportPlayer(GameObject player)
        {
            if (player == null) return;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            if (player.TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = false;
                player.transform.SetPositionAndRotation(pos, rot);
                cc.enabled = true;
                return;
            }
            if (player.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.position = pos;
                rb.rotation = rot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            player.transform.SetPositionAndRotation(pos, rot);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
        }
    }
}
