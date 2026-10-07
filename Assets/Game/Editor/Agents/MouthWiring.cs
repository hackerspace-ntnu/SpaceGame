using UnityEditor;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Gives a character whose rig has a <see cref="JawBoneName"/> bone a <see cref="TalkingMouth"/>
    /// on its root, pointed at the jaw, with the axis that opens it measured off the rig.
    ///
    /// <para>
    /// The axis is measured rather than typed in because the FBX importer's axis conversion decides
    /// what the jaw's local space is, and a hard-coded axis is a number that is right until someone
    /// re-exports. It is the character's right as the jaw sees it at rest: turning about that by a
    /// positive angle pitches the chin down. Measured on the prefab's saved pose, which is the rest
    /// pose.
    /// </para>
    ///
    /// <para>
    /// Drifters get this from <c>SculptCharacterBuilder.ApplyBehaviour</c>; a drifter whose rig has
    /// no jaw is left alone.
    /// </para>
    /// </summary>
    public static class MouthWiring
    {
        /// <summary>Unity's own Humanoid spelling, so the avatar maps it as the jaw too.</summary>
        public const string JawBoneName = "Jaw";

        /// <summary>
        /// Adds or updates the <see cref="TalkingMouth"/> on <paramref name="root"/>. Idempotent.
        /// Owns the jaw and its axis; leaves the timing and the openings as the component or a
        /// person set them.
        /// </summary>
        /// <returns>False if the rig has no jaw, in which case nothing is added.</returns>
        public static bool Ensure(GameObject root)
        {
            Transform jaw = FindJaw(root.transform);
            if (jaw == null) return false;

            var mouth = root.GetComponent<TalkingMouth>();
            if (mouth == null) mouth = root.AddComponent<TalkingMouth>();

            var so = new SerializedObject(mouth);
            SerializedFields.Set(so, "jaw", jaw);
            SerializedFields.SetVector3(so, "openAxis", jaw.InverseTransformDirection(root.transform.right).normalized);
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        public static Transform FindJaw(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == JawBoneName)
                    return t;
            return null;
        }

        /// <summary>True if <paramref name="root"/> carries a TalkingMouth pointed at a jaw.</summary>
        public static bool IsWired(GameObject root)
        {
            var mouth = root.GetComponent<TalkingMouth>();
            return mouth != null && new SerializedObject(mouth).FindProperty("jaw").objectReferenceValue != null;
        }
    }
}
