using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>Objects an effect makes and throws away, in Play or in an edit-mode preview.</summary>
    internal static class RuntimeObjects
    {
        /// <summary>Destroy <paramref name="obj"/>: at the end of the frame in Play, at once in the editor.</summary>
        public static void Discard(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
