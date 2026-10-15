using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Draws a humanoid clip as a contact sheet of stick figures: one cell per sample time, front
    /// view on the left and side view (the body faces right) on the right, the time in seconds in
    /// the corner. The way to LOOK at a clip — to find where a long take can be cut, whether a
    /// loop closes, what a pose actually is — without a character, a scene or play mode.
    ///
    /// <para>
    /// Poses come from <see cref="AnimationMode"/> sampling the clip on a throwaway copy of its
    /// own model, so what is drawn is exactly what the Humanoid retargeting produces. (A
    /// PlayableGraph over the same model left some rigs frozen in their first pose.) Red limbs are the body's right,
    /// blue its left; the figure is centred on the hips, the floor is the bottom edge.
    /// </para>
    /// </summary>
    public static class ClipPoseSheet
    {
        private const int Columns = 6;
        private const int CellWidth = 190;
        private const int CellHeight = 130;
        private const int FloorMargin = 8;
        private const int FrontCentre = 47;
        private const int SideCentre = 142;
        private const float PixelsPerMetre = 55f;

        private static readonly Color Background = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color Grid = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color Label = new Color(0.9f, 0.9f, 0.5f);
        private static readonly Color Left = new Color(0.35f, 0.6f, 1f);
        private static readonly Color Right = new Color(1f, 0.4f, 0.35f);

        // Each chain is drawn as connected segments; the colour is the side of the body.
        private static readonly (HumanBodyBones[] bones, Color colour)[] Chains =
        {
            (new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head }, Color.white),
            (new[] { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, Left),
            (new[] { HumanBodyBones.Chest, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }, Right),
            (new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes }, Left),
            (new[] { HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes }, Right),
        };

        // 3x5 digits, row-major, top row first; '#' is ink.
        private static readonly string[] Digits =
        {
            "###" + "#.#" + "#.#" + "#.#" + "###",
            ".#." + "##." + ".#." + ".#." + "###",
            "###" + "..#" + "###" + "#.." + "###",
            "###" + "..#" + "###" + "..#" + "###",
            "#.#" + "#.#" + "###" + "..#" + "..#",
            "###" + "#.." + "###" + "..#" + "###",
            "###" + "#.." + "###" + "#.#" + "###",
            "###" + "..#" + "..#" + "..#" + "..#",
            "###" + "#.#" + "###" + "#.#" + "###",
            "###" + "#.#" + "###" + "..#" + "###",
        };

        [MenuItem("Tools/SpaceGame/Animation/Pose Sheet From Selected Clip")]
        private static void FromSelection()
        {
            if (!(Selection.activeObject is AnimationClip clip))
            {
                Debug.LogWarning("[ClipPoseSheet] Select a humanoid AnimationClip (expand its model in the Project window).");
                return;
            }

            string path = Path.Combine("Library", "PoseSheets", clip.name + ".png");
            Render(clip, 0.5f, 0f, clip.length, path);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>
        /// Writes the sheet for <paramref name="clip"/> between <paramref name="from"/> and
        /// <paramref name="to"/> seconds, one cell per <paramref name="step"/>. Returns the cell count.
        /// </summary>
        public static int Render(AnimationClip clip, float step, float from, float to, string pngPath)
        {
            string modelPath = AssetDatabase.GetAssetPath(clip);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            // A take that copies another model's avatar has none of its own.
            Avatar avatar = (AssetImporter.GetAtPath(modelPath) as ModelImporter)?.sourceAvatar;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (avatar == null && asset is Avatar a) avatar = a;
            if (model == null || avatar == null || !avatar.isHuman)
                throw new InvalidOperationException($"[ClipPoseSheet] '{clip.name}' has no humanoid avatar in {modelPath}.");

            GameObject body = UnityEngine.Object.Instantiate(model);
            body.hideFlags = HideFlags.HideAndDontSave;
            Animator animator = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Someone else may already be in animation mode (the Animation window); leave it as found.
            bool ownsAnimationMode = !AnimationMode.InAnimationMode();
            if (ownsAnimationMode) AnimationMode.StartAnimationMode();

            int cells = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
            int rows = (cells + Columns - 1) / Columns;
            var sheet = new Texture2D(Columns * CellWidth, rows * CellHeight, TextureFormat.RGBA32, false);
            try
            {
                Fill(sheet, Background);
                for (int cell = 0; cell < cells; cell++)
                {
                    float time = Mathf.Min(from + cell * step, clip.length - 0.001f);
                    AnimationMode.SampleAnimationClip(body, clip, time);

                    int x = cell % Columns * CellWidth;
                    int y = (rows - 1 - cell / Columns) * CellHeight;
                    DrawCell(sheet, animator, x, y);
                    DrawNumber(sheet, time, x + 3, y + CellHeight - 8);
                }

                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
            }
            finally
            {
                if (ownsAnimationMode) AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(body);
                UnityEngine.Object.DestroyImmediate(sheet);
            }

            return cells;
        }

        private static void DrawCell(Texture2D sheet, Animator animator, int x, int y)
        {
            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            foreach ((HumanBodyBones[] bones, Color colour) in Chains)
            {
                for (int i = 0; i + 1 < bones.Length; i++)
                {
                    Transform a = animator.GetBoneTransform(bones[i]);
                    Transform b = animator.GetBoneTransform(bones[i + 1]);
                    if (a == null || b == null) continue;

                    DrawLine(sheet, Project(a.position.x - hips.x, a.position.y, x + FrontCentre, y),
                        Project(b.position.x - hips.x, b.position.y, x + FrontCentre, y), colour);
                    DrawLine(sheet, Project(a.position.z - hips.z, a.position.y, x + SideCentre, y),
                        Project(b.position.z - hips.z, b.position.y, x + SideCentre, y), colour);
                }
            }

            DrawLine(sheet, new Vector2Int(x, y), new Vector2Int(x + CellWidth - 1, y), Grid);
            DrawLine(sheet, new Vector2Int(x, y), new Vector2Int(x, y + CellHeight - 1), Grid);
        }

        private static Vector2Int Project(float across, float height, int centreX, int floorY) =>
            new Vector2Int(centreX + Mathf.RoundToInt(across * PixelsPerMetre), floorY + FloorMargin + Mathf.RoundToInt(height * PixelsPerMetre));

        private static void Fill(Texture2D texture, Color colour)
        {
            var pixels = new Color[texture.width * texture.height];
            Array.Fill(pixels, colour);
            texture.SetPixels(pixels);
        }

        private static void DrawLine(Texture2D texture, Vector2Int from, Vector2Int to, Color colour)
        {
            int dx = Mathf.Abs(to.x - from.x), dy = Mathf.Abs(to.y - from.y);
            int stepX = from.x < to.x ? 1 : -1, stepY = from.y < to.y ? 1 : -1;
            int error = dx - dy;
            int x = from.x, y = from.y;
            while (true)
            {
                Plot(texture, x, y, colour);
                Plot(texture, x + 1, y, colour);
                if (x == to.x && y == to.y) return;

                int doubled = 2 * error;
                if (doubled > -dy) { error -= dy; x += stepX; }
                if (doubled < dx) { error += dx; y += stepY; }
            }
        }

        private static void Plot(Texture2D texture, int x, int y, Color colour)
        {
            if (x >= 0 && y >= 0 && x < texture.width && y < texture.height) texture.SetPixel(x, y, colour);
        }

        /// <summary>Seconds with one decimal, e.g. "12.5", in the 3x5 font; (x, y) is the top-left of the first glyph.</summary>
        private static void DrawNumber(Texture2D texture, float seconds, int x, int y)
        {
            foreach (char c in seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
            {
                if (c == '.')
                {
                    Plot(texture, x, y - 4, Label);
                    x += 2;
                    continue;
                }

                string glyph = Digits[c - '0'];
                for (int i = 0; i < glyph.Length; i++)
                    if (glyph[i] == '#') Plot(texture, x + i % 3, y - i / 3, Label);
                x += 4;
            }
        }
    }
}
