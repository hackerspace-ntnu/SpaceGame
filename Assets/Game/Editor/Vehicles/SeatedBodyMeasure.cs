// Where a humanoid sitting in the Sit state touches a seat, measured off its own rig.
//
// A seat offset is "how far from the seat marker the rider's origin goes", and that depends on the
// body, not the seat: a Strider nomad's origin is at its feet, the player's is a metre above its
// soles (PlayerCharacter.md), and the Sit clip decides how far the bottom drops below the hips.
// The monowheels carried the robot horse's 0.95 m drop for all of them, which sank a seated player
// about 1.4 m into the chassis. So the builder asks the body instead: sit it down in a preview
// scene, bake its skin, and read off the two places a chair meets it.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class SeatedBodyMeasure
    {
        /// <summary>How long the Sit state is given to settle (s), in <see cref="SettleSteps"/> animator steps.</summary>
        private const float SettleStep = 0.05f;
        private const int SettleSteps = 60;
        /// <summary>The bottom is the lowest skin within this of the hips across (x) and along (z), m.</summary>
        private const float BottomHalfWidth = 0.2f, BottomHalfDepth = 0.05f;
        /// <summary>The back of the pelvis is the rearmost skin in this band round the hips' height, m.</summary>
        private const float BackBelowHips = 0.15f, BackAboveHips = 0.25f, BackHalfWidth = 0.35f;

        /// <summary>
        /// The seated body's back-bottom corner in its root's space: y is the underside of the body
        /// below the hips (what rests on a cushion), z the back of the pelvis (what rests against a
        /// backrest). A rider sat on a seat marker at a seat's back corner therefore goes at minus this.
        /// </summary>
        public static Vector3 SitCorner(GameObject bodyPrefab)
        {
            if (bodyPrefab == null) throw new ArgumentNullException(nameof(bodyPrefab));

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, scene);
                body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform root = body.transform;

                Animator animator = body.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman)
                    throw new InvalidOperationException($"{bodyPrefab.name} has no humanoid Animator to sit down.");
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetBool(HumanoidParams.Seated, true);
                for (int i = 0; i < SettleSteps; i++) animator.Update(SettleStep);
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Sit"))
                    throw new InvalidOperationException($"{bodyPrefab.name} did not reach the Sit state with {HumanoidParams.Seated} set.");

                Vector3 hips = root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
                SkinnedMeshRenderer skin = Skin(body);
                if (skin == null)
                    throw new InvalidOperationException($"{bodyPrefab.name} has no skinned mesh to measure.");

                float bottom = float.MaxValue, back = float.MaxValue;
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                foreach (Vector3 vertex in baked.vertices)
                {
                    Vector3 p = root.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                    if (Mathf.Abs(p.x - hips.x) < BottomHalfWidth && Mathf.Abs(p.z - hips.z) < BottomHalfDepth && p.y < hips.y)
                        bottom = Mathf.Min(bottom, p.y);
                    if (Mathf.Abs(p.x - hips.x) < BackHalfWidth && p.y > hips.y - BackBelowHips && p.y < hips.y + BackAboveHips)
                        back = Mathf.Min(back, p.z);
                }
                UnityEngine.Object.DestroyImmediate(baked);

                if (bottom == float.MaxValue || back == float.MaxValue)
                    throw new InvalidOperationException($"{bodyPrefab.name}'s skin '{skin.name}' has nothing round its hips to measure.");
                return new Vector3(hips.x, bottom, back);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// The body's own skin: the skinned mesh with the most vertices. Clothes that hang below the
        /// seat (scarves, shawls, a slung pole) are separate, smaller meshes on every rig here
        /// (the player's Suit, a nomad's *_Suit).
        /// </summary>
        private static SkinnedMeshRenderer Skin(GameObject body)
        {
            SkinnedMeshRenderer largest = null;
            foreach (SkinnedMeshRenderer skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.sharedMesh != null && (largest == null || skin.sharedMesh.vertexCount > largest.sharedMesh.vertexCount))
                    largest = skin;
            return largest;
        }
    }
}
