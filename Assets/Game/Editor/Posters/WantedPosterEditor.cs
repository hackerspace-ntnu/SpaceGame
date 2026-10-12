using System;
using SpaceGame.World.Posters;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>The poster's fields, then what was baked from them and a button to bake again.</summary>
    [CustomEditor(typeof(WantedPoster))]
    [CanEditMultipleObjects]
    public sealed class WantedPosterEditor : Editor
    {
        private const float PreviewWidth = 160f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1) return;

            var poster = (WantedPoster)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(StatusOf(poster), MessageType.None);

            if (poster.Poster != null)
            {
                Rect area = GUILayoutUtility.GetRect(PreviewWidth, PreviewWidth * poster.Poster.height / poster.Poster.width,
                    GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(area, poster.Poster);
            }

            using (new EditorGUI.DisabledScope(poster.Agent == null || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Bake again")) BakeNow(poster);
            }
        }

        private static string StatusOf(WantedPoster poster)
        {
            if (poster.Agent == null) return "Pick an agent and the poster bakes itself.";
            if (EditorApplication.isPlaying) return "Posters bake outside play mode.";
            return WantedPosterBaker.IsCurrent(poster) ? "Baked." : "Baking shortly…";
        }

        private static void BakeNow(WantedPoster poster)
        {
            try
            {
                Debug.Log("[Posters] " + WantedPosterBaker.Bake(poster), poster);
            }
            catch (Exception e)
            {
                Debug.LogException(e, poster);
            }
        }
    }
}
