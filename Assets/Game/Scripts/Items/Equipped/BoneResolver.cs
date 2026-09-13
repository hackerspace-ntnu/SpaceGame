using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Finds a bone on any rig: the humanoid mapping when there is one, a name search otherwise.
    /// Shared by everything that hangs an object off the skeleton — the hand sockets, the worn
    /// gear, the pack — so they all agree on which transform "the right hand" is.
    /// </summary>
    public static class BoneResolver
    {
        /// <param name="animator">The rig's Animator, or null for a character without one.</param>
        /// <param name="root">Where the name search starts.</param>
        /// <param name="bone">The humanoid bone to ask the Animator for first.</param>
        /// <param name="nameHints">Case-insensitive substrings; the first child whose name contains any of them wins.</param>
        public static Transform Resolve(Animator animator, Transform root, HumanBodyBones bone, string[] nameHints)
        {
            // Humanoid rig: ask the Animator for the actual bone Transform.
            if (animator != null && animator.isHuman)
            {
                var mapped = animator.GetBoneTransform(bone);
                if (mapped != null) return mapped;
            }

            // Generic rig: substring-search the hierarchy by bone name.
            if (root == null || nameHints == null || nameHints.Length == 0) return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                for (int h = 0; h < nameHints.Length; h++)
                {
                    var hint = nameHints[h];
                    if (string.IsNullOrEmpty(hint)) continue;
                    if (n.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
                }
            }

            return null;
        }
    }
}
