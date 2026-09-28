using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The avatar masks of the humanoid controller's layers, written from code so they can be
    /// rebuilt and reviewed rather than ticked by hand.
    /// </summary>
    internal static class HumanoidMasks
    {
        public const string Folder = HumanoidControllerBuilder.Folder + "Masks/";

        /// <summary>The three masks the layers use.</summary>
        public readonly struct Set
        {
            public readonly AvatarMask Upper;
            public readonly AvatarMask Left;
            public readonly AvatarMask Right;

            public Set(AvatarMask upper, AvatarMask left, AvatarMask right)
            {
                Upper = upper;
                Left = left;
                Right = right;
            }
        }

        /// <summary>Write all three masks and return them.</summary>
        public static Set Write()
        {
            HumanoidControllerBuilder.EnsureFolder(Folder);
            return new Set(UpperBody(), LeftArm(), RightArm());
        }

        /// <summary>The masks as they are on disk, without writing anything. Null where missing.</summary>
        public static Set Load() => new Set(
            AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "UpperBody.mask"),
            AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "LeftArm.mask"),
            AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder + "RightArm.mask"));

        /// <summary>
        /// Chest and both arms, nothing else.
        ///
        /// <para>
        /// The head is deliberately OFF: PlayerHeadLook turns it in LateUpdate, and a layer at
        /// weight 1 with the head on would snap a corpse's head level in its death pose. The legs
        /// and root are off because the whole point of the layer is that they keep walking.
        /// </para>
        /// <para>
        /// Hand IK goals ON. Nothing writes one today, but the flags decide whether the layer
        /// carries IK goal data at all, and switching them off would silently rule out ever
        /// adding one. The clips carry no IK curves, so leaving them on costs nothing.
        /// </para>
        /// </summary>
        private static AvatarMask UpperBody() => Save("UpperBody", mask =>
        {
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
        });

        /// <summary>
        /// One arm and its fingers, no chest: an arm layer only ever runs over a pose that already
        /// owns the torso, and a second opinion about the chest would fight it.
        /// </summary>
        private static AvatarMask LeftArm() => Save("LeftArm", mask =>
        {
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
        });

        /// <summary>The mirror of <see cref="LeftArm"/>.</summary>
        private static AvatarMask RightArm() => Save("RightArm", mask =>
        {
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
        });

        /// <summary>
        /// Build a mask with every part off, let <paramref name="enable"/> switch parts on, and
        /// write it over the asset of that name — in place, so its GUID and every layer that
        /// references it survive.
        ///
        /// <para>
        /// The name is set before the copy: <c>CopySerialized</c> copies the name too, and an
        /// in-memory mask has none, which blanked the asset's and made Unity warn that the main
        /// object name no longer matched the file.
        /// </para>
        /// </summary>
        private static AvatarMask Save(string name, System.Action<AvatarMask> enable)
        {
            var mask = new AvatarMask { name = name };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            enable(mask);

            string path = Folder + name + ".mask";
            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mask, path);
                return mask;
            }

            EditorUtility.CopySerialized(mask, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
