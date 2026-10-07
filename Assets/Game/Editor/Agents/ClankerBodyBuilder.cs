// Dresses the Clanker in three more bodies: the Same Gev Dudios sci-fi robots (Robert, Paperman,
// Engie) that used to stand in as PatrolRobot 1/2/3. Only the body is theirs. Everything the robot
// does comes from ClankerStack, the same stack the RPR Clanker gets, so a town of mixed bodies is
// one kind of enemy -- same patrol, same gun, same faction, same save and network wiring.
//
// Each prefab is rebuilt from nothing over its old path. That throws away the old PatrolRobot
// stack (herd, wander, the built-in ray gun, the baked-in gun and sword meshes in the hand) and
// keeps the asset's GUID, so DefaultNetworkPrefabs, SettlementConfig.asset and any save that
// refers to it by prefab id still resolve.
//
// Three things about these bodies worth knowing:
//
//   ANIMATOR. The FBXs import Humanoid, and their clips are the astronaut's
//   (Humanoid.controller), retargeted through the avatar -- so unlike the RPR body the
//   clip paths do not matter, only that the Animator sits on the FBX instance root with that
//   avatar. The hand bone comes from the avatar too, not a name search.
//
//   BOUNDS. A SkinnedMeshRenderer's bounds are its authored localBounds, which on these meshes
//   reach 0.07-0.12 m below the soles and 0.2 m over the head. SkinnedBounds bakes the bind-pose
//   mesh instead, so the capsule height and the soles-on-y=0 offset are the real body. The radius
//   is the shoulders' half-span, not the bounds: Engie's arms and tools make it 3.7 m wide.
//
//   PACE. Every body runs at the RPR Clanker's measured stride speed ("behave exactly like the
//   Clanker"), which is faster than the astronaut run these bodies were tuned to. The driver keeps
//   each body's own blend-tree tuning and plays the clips faster by runSpeed / AuthoredRunSpeed,
//   which is what keeps the feet from skating (AgentAnimatorDriver.animatorSpeedScale).
//
// Built by: Tools > SpaceGame > Agents > Build Clanker Prefab (ClankerBuilder), with the RPR body.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class ClankerBodyBuilder
    {
        /// <summary>One body: where it comes from, how it is scaled and dressed, and where it goes.</summary>
        public sealed class Recipe
        {
            public string Fbx;
            public string PrefabPath;
            /// <summary>The scale the old PatrolRobot prefab stood this body at. Paperman's is not uniform.</summary>
            public Vector3 Scale;
            public string[] Materials;
            /// <summary>What the humanoid avatar maps RightHand to. Checked, not searched for.</summary>
            public string HandBone;
        }

        private const string ModelDir = "Assets/ThirdParty/Same Gev Dudios/Sci-Fi Robots Bundle/Models";
        private const string MaterialDir = "Assets/ThirdParty/Same Gev Dudios/Sci-Fi Robots Bundle/Materials/SRP";
        private const string PrefabDir = "Assets/Game/Prefabs/Agents/Robots";

        public const string ControllerPath = HumanoidControllerBuilder.ControllerPath;

        /// <summary>
        /// The run speed (m/s) these bodies' astronaut clips were tuned to as PatrolRobots: their
        /// NavMeshAgent speed and measuredRunSpeed were both 3.5, with the playback rate at 1.
        /// </summary>
        public const float AuthoredRunSpeed = 3.5f;

        /// <summary>
        /// The PatrolRobots' AgentAnimatorDriver blend-tree scale and walk boost, kept: they pick
        /// the astronaut blend tree's walk and run entries for these bodies, and the RPR Clanker's
        /// 1 / 1 belong to its own tree built in true m/s.
        /// </summary>
        public const float AnimationSpeedMultiplier = 1.5f;
        public const float WalkAnimBoost = 1.1f;

        public static readonly Recipe[] All =
        {
            new Recipe
            {
                Fbx = ModelDir + "/Robert.fbx",
                PrefabPath = PrefabDir + "/PatrolRobot 1.prefab",
                Scale = new Vector3(1.6f, 1.6f, 1.6f),
                Materials = new[] { MaterialDir + "/Robert.mat" },
                HandBone = "Hand1.R",
            },
            new Recipe
            {
                Fbx = ModelDir + "/Paperman.fbx",
                PrefabPath = PrefabDir + "/PatrolRobot 2.prefab",
                Scale = new Vector3(2.3f, 1.8f, 2.3f),
                Materials = new[] { MaterialDir + "/Paperman.mat", MaterialDir + "/PapermanGlasses.mat" },
                HandBone = "Hand.R",
            },
            new Recipe
            {
                Fbx = ModelDir + "/Engie.fbx",
                PrefabPath = PrefabDir + "/PatrolRobot 3.prefab",
                Scale = new Vector3(1.55f, 1.55f, 1.55f),
                Materials = new[] { MaterialDir + "/Engie.mat" },
                HandBone = "Hand.R",
            },
        };

        /// <summary>
        /// Builds <paramref name="recipe"/>'s prefab from nothing, running at <paramref name="runSpeed"/>.
        /// Registration, save wiring and ragdoll wiring are the caller's (ClankerBuilder does all
        /// bodies in one pass).
        /// </summary>
        public static GameObject Build(Recipe recipe, float runSpeed)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.Fbx);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Material[] materials = recipe.Materials.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            if (source == null || controller == null || materials.Any(m => m == null))
            {
                Debug.LogError($"[ClankerBodyBuilder] {recipe.PrefabPath}: missing {recipe.Fbx}, {ControllerPath} or one of its materials.");
                return null;
            }

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(recipe.PrefabPath));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Body";
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = recipe.Scale;

            // Soles onto y = 0, measured off the baked mesh (see BOUNDS in the header).
            Bounds bounds = SkinnedBounds(model);
            model.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);
            bounds = SkinnedBounds(model);

            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            skin.sharedMaterials = materials;
            // A skinned body's bind-pose bounds are not where the body is once it animates.
            skin.updateWhenOffscreen = true;

            // On the FBX instance root, with the avatar the importer built, as every humanoid here.
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(recipe.Fbx).OfType<Avatar>().FirstOrDefault();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.avatar == null || !animator.avatar.isHuman)
            {
                Object.DestroyImmediate(root);
                Debug.LogError($"[ClankerBodyBuilder] {recipe.Fbx} has no humanoid avatar; the body would stand still.");
                return null;
            }

            float animatorSpeedScale = runSpeed / AuthoredRunSpeed;
            float radius = ShoulderHalfSpan(animator);
            ClankerStack.Apply(root, new ClankerBodyFit
            {
                animator = animator,
                bounds = bounds,
                radius = radius,
                handSocket = animator.GetBoneTransform(HumanBodyBones.RightHand),
                runSpeed = runSpeed,
                animationSpeedMultiplier = AnimationSpeedMultiplier,
                walkAnimBoost = WalkAnimBoost,
                animatorSpeedScale = animatorSpeedScale,
            });

            GameObject saved = ClankerStack.Save(root, recipe.PrefabPath);
            if (saved != null)
                Debug.Log($"[ClankerBodyBuilder] {recipe.PrefabPath}: {System.IO.Path.GetFileNameWithoutExtension(recipe.Fbx)} " +
                          $"{bounds.size.y:F2} m tall, radius {radius:F2} m; run {runSpeed:F2} m/s, walk {runSpeed * ClankerStack.WalkFraction:F2} m/s, " +
                          $"animatorSpeedScale {animatorSpeedScale:F3}.");
            return saved;
        }

        /// <summary>The built prefab, read off disk: the shared stack plus this body's rig.</summary>
        public static bool Verify(Recipe recipe, out string report)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
            if (prefab == null) { report = "prefab did not save"; return false; }

            var missing = new List<string>();
            ClankerStack.Verify(prefab, missing);

            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator != null && (animator.avatar == null || !animator.avatar.isHuman))
                missing.Add("the avatar is not humanoid -- the astronaut clips drive nothing");
            if (animator != null && AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) != ControllerPath)
                missing.Add($"the Animator is not on {ControllerPath}");

            var equipment = prefab.GetComponent<SpaceGame.Agents.EntityEquipmentController>();
            var socket = equipment != null
                ? new SerializedObject(equipment).FindProperty("handSocket").objectReferenceValue as Transform
                : null;
            if (socket == null || socket.name != recipe.HandBone)
                missing.Add($"the hand socket is not {recipe.HandBone}");

            if (prefab.GetComponentsInChildren<Renderer>(true).Any(r => !(r is SkinnedMeshRenderer)))
                missing.Add("a baked-in hand prop survived");

            report = missing.Count == 0
                ? "verified: humanoid on the astronaut controller, hand socket, network hash, save id."
                : "missing: " + string.Join("; ", missing);
            return missing.Count == 0;
        }

        /// <summary>
        /// Half the distance between the upper arms: the torso's width, which is the footprint.
        /// The bind-pose bounds are not -- Engie's arms and tools stand 3.7 m wide, and a capsule
        /// sized off them would be a 1.1 m-radius agent that fits through no gap a Clanker can.
        /// </summary>
        private static float ShoulderHalfSpan(Animator animator) =>
            Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position,
                             animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position) * 0.5f;

        /// <summary>
        /// The body's bind pose as the mesh really is, in the space of <paramref name="instance"/>'s
        /// parent (or the world, unparented): every SkinnedMeshRenderer baked and its vertices
        /// encapsulated. Renderer.bounds would be the authored localBounds instead.
        /// </summary>
        public static Bounds SkinnedBounds(GameObject instance)
        {
            Transform frame = instance.transform.parent;
            var baked = new Mesh();
            try
            {
                bool any = false;
                var bounds = new Bounds();
                foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.BakeMesh(baked, true);
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        Vector3 world = skin.transform.TransformPoint(vertex);
                        Vector3 point = frame != null ? frame.InverseTransformPoint(world) : world;
                        if (any) bounds.Encapsulate(point);
                        else { bounds = new Bounds(point, Vector3.zero); any = true; }
                    }
                }
                if (!any) throw new System.InvalidOperationException($"{instance.name} has no skinned mesh to measure.");
                return bounds;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }
    }
}
