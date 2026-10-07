using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The places on a worn garment — a belt, a backpack — where a carried item can hang.
    ///
    /// <para>
    /// The points live on the garment's prefab, so every Raxy wearing that belt hangs its tools in
    /// the same places and the places are tuned once, in Prefab Mode, against the belt's own
    /// geometry. Each point is a child transform: origin at the loop, ring or strap the item hangs
    /// from, +Y up toward the garment, +Z out from the wearer's body — the same frame
    /// <see cref="BeltMount.Hang"/> is authored in.
    /// </para>
    /// <para>
    /// A point is read in the garment's MESH space, which is the space of the rest-pose view shown
    /// when the prefab is opened. A worn garment is skinned, so its own transform says nothing about
    /// where it is on a body; the mesh's bind pose does. <see cref="TryPoseOnBone"/> carries a point
    /// through the bind pose of <see cref="Bone"/>, which is why one set of points fits every Raxy
    /// whatever size its model is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class GarmentMounts : MonoBehaviour
    {
        [Serializable]
        public struct Mount
        {
            public BeltSlot slot;

            [Tooltip("Child of this garment's prefab. Origin: where the garment holds the item. " +
                     "+Y up toward the garment, +Z out from the wearer's body.")]
            public Transform point;
        }

        [Tooltip("The bone the points ride on: Hips for a belt, Chest for a backpack. The garment " +
                 "must be skinned to it.")]
        [SerializeField] private HumanBodyBones bone = HumanBodyBones.Hips;

        [Tooltip("A slot appears at most once; a wearer of two garments offering the same slot " +
                 "uses the first.")]
        [SerializeField] private Mount[] mounts = Array.Empty<Mount>();

        public HumanBodyBones Bone => bone;

        public Mount[] Mounts => mounts;

        /// <summary>
        /// Where <paramref name="mount"/> lies in the local space of <paramref name="wearerBone"/>.
        /// False when the garment is not skinned to that bone, which nothing worn should be.
        /// </summary>
        public bool TryPoseOnBone(Transform wearerBone, Mount mount, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            var skin = GetComponent<SkinnedMeshRenderer>();
            int index = Array.IndexOf(skin.bones, wearerBone);
            if (index < 0 || skin.sharedMesh == null) return false;

            Matrix4x4 pointInMesh = transform.worldToLocalMatrix * mount.point.localToWorldMatrix;
            Matrix4x4 pointOnBone = skin.sharedMesh.bindposes[index] * pointInMesh;
            position = pointOnBone.GetPosition();
            rotation = pointOnBone.rotation;
            return true;
        }

        private void OnValidate()
        {
            for (int i = 0; i < mounts.Length; i++)
            {
                Transform point = mounts[i].point;
                if (point != null && point.IsChildOf(transform)) continue;

                Debug.LogWarning($"GarmentMounts on '{name}': mount {i} ({mounts[i].slot}) must be a " +
                                 "child of this garment or it will not survive instantiation.", this);
            }
        }
    }
}
