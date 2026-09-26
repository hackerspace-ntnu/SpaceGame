using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A piece of clothing kept as a prefab of its own and skinned to whoever wears it.
    ///
    /// <para>
    /// A prefab cannot reference the bones of a character it is not part of, so a garment carries its
    /// bones by NAME and binds its renderer to the wearer's skeleton when it is put on: parented
    /// anywhere under a character whose rig has those bones -- every Raxy built from raxy.fbx. It
    /// binds in the editor as well, so a garment dropped onto a character in Prefab Mode is worn at
    /// once, and a binding already saved into a character prefab is only checked.
    /// </para>
    ///
    /// <para>
    /// Unworn, a skinned renderer has no bones and draws nothing, so a garment also carries a plain
    /// view of itself in its rest pose (<see cref="restPose"/>): the same mesh on a MeshRenderer. That
    /// is what shows when the garment prefab is opened, previewed or dropped in a scene on its own.
    /// Worn, the view is switched off in the editor and deleted in play, before anything -- the
    /// ragdoll measuring the body's parts at death -- can take it for part of the body.
    /// </para>
    ///
    /// <para>
    /// Clothes are part of the character prefab they are placed in, so every machine instantiates the
    /// same ones and nothing is sent or saved. Dressing and undressing DURING play is not what this
    /// is for: that would be state to replicate and to save.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class SkinnedGarment : MonoBehaviour
    {
        [Tooltip("The bones the garment's mesh is skinned to, by name, in the order of its bind poses. " +
                 "Written by the drifter builder from the FBX.")]
        [SerializeField] private string[] boneNames = System.Array.Empty<string>();

        [Tooltip("The bone the renderer's bounds follow, by name.")]
        [SerializeField] private string rootBoneName;

        [Tooltip("The garment in its rest pose, drawn unskinned, shown while nobody wears it. Written " +
                 "by the drifter builder.")]
        [SerializeField] private MeshRenderer restPose;

        private void OnEnable() => Bind();

        private void OnTransformParentChanged() => Bind();

        /// <summary>
        /// Binds the renderer to the skeleton of whatever this is parented under. True if it is bound;
        /// false on its own (nothing is wearing it: the rest pose shows) or, logged, if the wearer
        /// lacks a bone.
        /// </summary>
        public bool Bind()
        {
            var skin = GetComponent<SkinnedMeshRenderer>();
            Transform wearer = transform.parent;
            if (wearer == null)
            {
                ShowWorn(skin, false);
                return false;
            }

            if (IsBoundTo(skin, wearer))
            {
                ShowWorn(skin, true);
                return true;
            }

            var byName = new Dictionary<string, Transform>();
            foreach (var t in wearer.GetComponentsInChildren<Transform>(true))
                if (!t.IsChildOf(transform) && !byName.ContainsKey(t.name))
                    byName.Add(t.name, t);

            var bones = new Transform[boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (byName.TryGetValue(boneNames[i], out bones[i])) continue;

                Debug.LogError($"{name}: the character it is on has no bone '{boneNames[i]}', so the " +
                               "garment cannot be worn by it. Clothes fit the skeleton they were " +
                               "modelled on.", this);
                ShowWorn(skin, false);
                return false;
            }

            skin.bones = bones;
            skin.rootBone = !string.IsNullOrEmpty(rootBoneName) && byName.TryGetValue(rootBoneName, out var root)
                ? root
                : null;
            ShowWorn(skin, true);
            return true;
        }

        /// <summary>
        /// Worn: the skinned renderer draws and the rest-pose view goes -- deleted in play, switched
        /// off in the editor, where it belongs to the prefab. Unworn: the other way round. Writes only
        /// what differs, so an open prefab is not marked changed for nothing.
        /// </summary>
        private void ShowWorn(SkinnedMeshRenderer skin, bool worn)
        {
            if (skin.enabled != worn) skin.enabled = worn;
            if (restPose == null) return;

            if (worn && Application.isPlaying)
            {
                Destroy(restPose.gameObject);
                restPose = null;
                return;
            }

            if (restPose.gameObject.activeSelf == worn) restPose.gameObject.SetActive(!worn);

            // The view wears what the garment wears, so a colour changed on the prefab shows on it.
            if (!worn && !SameMaterials(restPose.sharedMaterials, skin.sharedMaterials))
                restPose.sharedMaterials = skin.sharedMaterials;
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private bool IsBoundTo(SkinnedMeshRenderer skin, Transform wearer)
        {
            var bones = skin.bones;
            if (bones.Length != boneNames.Length) return false;

            for (int i = 0; i < bones.Length; i++)
                if (bones[i] == null || bones[i].name != boneNames[i] || !bones[i].IsChildOf(wearer))
                    return false;
            return true;
        }
    }
}
