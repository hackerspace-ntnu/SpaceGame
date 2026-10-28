using System;
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    /// <summary>One creature body under review: the prefab the game spawns and every clip its model carries.</summary>
    public sealed class ReviewCreature
    {
        public string Name;
        public GameObject Prefab;
        /// <summary>The model file the prefab's avatar comes from; the review instantiates this, never the agent.</summary>
        public GameObject Model;
        public List<ReviewClip> Clips;
        /// <summary>The speeds the game moves this body at, from its NavMeshAgent and motor (0 without one).</summary>
        public float WalkSpeed, RunSpeed;
    }

    /// <summary>One clip of a <see cref="ReviewCreature"/>, with what the game uses it for.</summary>
    public sealed class ReviewClip
    {
        public AnimationClip Clip;
        /// <summary>The controller states this clip plays as ("Walk", "Hurt"…); empty when the game never plays it.</summary>
        public List<string> Roles = new List<string>();
        /// <summary>How fast the game moves the body while this clip plays, in m/s; 0 for a standing clip.</summary>
        public float GameSpeed;

        public bool Measured;
        /// <summary>No bone turns between the first frame and the middle.</summary>
        public bool HeldPose;
        /// <summary>The largest one-frame jump, in degrees of the bone that turns most, wrap-around included for a loop.</summary>
        public float PopDegrees;
        /// <summary><see cref="PopDegrees"/> as a multiple of the clip's median frame.</summary>
        public float PopRatio;
        /// <summary>The frame the largest jump lands on; 0 for a loop's wrap from last frame to first.</summary>
        public int PopFrame;

        public string Name => Clip.name;
    }

    /// <summary>
    /// Every animated creature body in the project, found from its prefab: the avatar (or the
    /// controller's clips) names the model, the model holds the clips, the controller says which of them the game plays. Prefabs
    /// that share a model (Esko, Esko_Ranch, Esko_Wild) are one body and listed once.
    /// </summary>
    public static class CreatureAnimationCatalog
    {
        private const string PrefabRoot = "Assets/Game/Prefabs/Agents/creatures";
        private const string WalkRole = "Walk";
        private const string RunRole = "Run";

        public static List<ReviewCreature> Collect()
        {
            var byModel = new Dictionary<string, ReviewCreature>();
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (string path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = prefab.GetComponentInChildren<Animator>(true);
                string modelPath = animator != null ? ModelPath(animator) : null;
                if (modelPath == null || byModel.ContainsKey(modelPath)) continue;

                var creature = new ReviewCreature
                {
                    Name = prefab.name,
                    Prefab = prefab,
                    Model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),
                    Clips = Clips(modelPath, animator.runtimeAnimatorController),
                };
                ReadGameSpeeds(prefab, creature);
                foreach (ReviewClip clip in creature.Clips)
                    clip.GameSpeed = clip.Roles.Contains(RunRole) ? creature.RunSpeed
                        : clip.Roles.Contains(WalkRole) ? creature.WalkSpeed
                        : 0f;
                if (creature.Clips.Count > 0) byModel[modelPath] = creature;
            }
            return byModel.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        }

        // The model file the animator's skeleton comes from: its avatar's, or, for a Generic rig with
        // no avatar (Esko, the Caraxoids), the file most of its controller's clips are imported from.
        private static string ModelPath(Animator animator)
        {
            string path = animator.avatar != null
                ? AssetDatabase.GetAssetPath(animator.avatar)
                : animator.runtimeAnimatorController == null ? null
                : animator.runtimeAnimatorController.animationClips
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(p => AssetImporter.GetAtPath(p) is ModelImporter)
                    .GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            return path != null && AssetImporter.GetAtPath(path) is ModelImporter ? path : null;
        }

        // The model's own takes plus whatever the controller plays. An override slot left empty
        // still plays the template's clip, which belongs to another skeleton — listed so the
        // review shows it moving nothing.
        private static List<ReviewClip> Clips(string modelPath, RuntimeAnimatorController controller)
        {
            var clips = new Dictionary<AnimationClip, ReviewClip>();
            ReviewClip For(AnimationClip c) =>
                clips.TryGetValue(c, out ReviewClip r) ? r : clips[c] = new ReviewClip { Clip = c };

            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>())
                if (!clip.name.StartsWith("__preview__", StringComparison.Ordinal)) For(clip);

            if (controller is AnimatorOverrideController overrides)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrides.GetOverrides(pairs);
                foreach (var pair in pairs)
                    For(pair.Value != null ? pair.Value : pair.Key).Roles.Add(Role(pair.Key.name));
            }
            else if (controller != null)
            {
                foreach (var clip in controller.animationClips.Distinct())
                    For(clip).Roles.Add(Role(clip.name));
            }
            return clips.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        }

        // "Esko_Walk" -> "Walk": controller clips are named <Creature>_<State>.
        private static string Role(string clipName) => clipName.Substring(clipName.LastIndexOf('_') + 1);

        private static void ReadGameSpeeds(GameObject prefab, ReviewCreature creature)
        {
            var agent = prefab.GetComponentInChildren<NavMeshAgent>(true);
            if (agent == null) return;
            creature.RunSpeed = agent.speed;
            var motor = prefab.GetComponentInChildren<NavMeshAgentMotor>(true);
            float walkMultiplier = motor != null
                ? new SerializedObject(motor).FindProperty("walkSpeedMultiplier").floatValue
                : 1f;
            creature.WalkSpeed = agent.speed * walkMultiplier;
        }

        /// <summary>
        /// Fills <see cref="ReviewClip.HeldPose"/> and the pop fields by sampling every frame of the
        /// clip onto <paramref name="body"/>, an instance of the creature's model. A pop is a frame
        /// that jumps far more than the clip's median frame: a broken key, or a loop whose seam does
        /// not meet (often one frame before the end, when the last frame was snapped to the first).
        /// Leaves the body in an arbitrary pose.
        /// </summary>
        public static void Measure(ReviewClip clip, GameObject body)
        {
            if (clip.Measured) return;
            clip.Measured = true;
            AnimationClip c = clip.Clip;
            var bones = body.GetComponentsInChildren<Transform>();
            int frames = Mathf.Max(1, Mathf.RoundToInt(c.length * c.frameRate));
            var poses = Enumerable.Range(0, frames + 1)
                .Select(f => ClipMotion.Pose(c, body, bones, Mathf.Min(f / c.frameRate, c.length)))
                .ToList();
            clip.HeldPose = c.length < 0.1f || ClipMotion.MaxTurn(poses[0], poses[frames / 2]) < ClipMotion.StillDegrees;
            if (clip.HeldPose) return;

            // steps[i] lands on frame i + 1; a loop's last step wraps onto frame 0.
            var steps = Enumerable.Range(1, frames).Select(f => ClipMotion.MaxTurn(poses[f - 1], poses[f])).ToList();
            if (c.isLooping) steps.Add(ClipMotion.MaxTurn(poses[frames], poses[0]));
            float median = steps.OrderBy(step => step).ElementAt(steps.Count / 2);
            int worst = steps.IndexOf(steps.Max());
            clip.PopDegrees = steps[worst];
            clip.PopRatio = median > 0f ? steps[worst] / median : 0f;
            clip.PopFrame = worst == frames ? 0 : worst + 1;
        }
    }
}
