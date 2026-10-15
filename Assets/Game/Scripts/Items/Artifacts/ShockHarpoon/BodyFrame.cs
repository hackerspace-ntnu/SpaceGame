using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// What a skeleton drawn over a body needs to know that the rig itself does not say: where its
    /// head and chest are, which way they face, and how tall the body is.
    ///
    /// <para>
    /// Measured while the body still stands (<see cref="ShockedVisual"/> measures on attach, which
    /// the harpoon does before its dart lands): the root's forward is the way the body faces, and a
    /// head's own axes mean nothing — this project's rigs face +Z, −Y, or whatever their .blend did.
    /// The directions are stored in each bone's own space, so they stay right once the body is limp.
    /// </para>
    /// </summary>
    public sealed class BodyFrame
    {
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        public Vector3 HeadUp { get; private set; }
        public Vector3 HeadFront { get; private set; }
        public Vector3 ChestUp { get; private set; }
        public Vector3 ChestFront { get; private set; }

        /// <summary>The body's longest extent in metres: its height standing, its length lying down.</summary>
        public float Height { get; private set; }

        /// <summary>A body measured smaller than this is drawn at this size, in metres.</summary>
        private const float MinimumHeight = 0.3f;

        public static BodyFrame Measure(Transform body)
        {
            var frame = new BodyFrame { Height = MeasureHeight(body) };
            HashSet<Transform> bones = BonesOf(body);

            Animator animator = HumanoidOf(body);
            bool human = animator != null;

            frame.Head = human ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (frame.Head == null) frame.Head = Named(bones, "head");

            frame.Chest = human
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                : null;
            // Neck, then chest: two joints down from the head on any spine.
            if (frame.Chest == null && frame.Head != null && frame.Head.parent != null)
                frame.Chest = bones.Contains(frame.Head.parent.parent) ? frame.Head.parent.parent : frame.Head.parent;

            if (frame.Head != null)
            {
                (frame.HeadUp, frame.HeadFront) = Axes(frame.Head, body, bones);
            }
            if (frame.Chest != null)
            {
                (frame.ChestUp, frame.ChestFront) = Axes(frame.Chest, body, bones);
            }
            return frame;
        }

        /// <summary>The body's humanoid animator — a person, with named bones to ask for — or null for anything else.</summary>
        public static Animator HumanoidOf(Transform body)
        {
            Animator animator = body != null ? body.GetComponentInChildren<Animator>() : null;
            return animator != null && animator.isHuman ? animator : null;
        }

        /// <summary>
        /// Every joint the body's skinned meshes are bound to, less anything merely attached to it
        /// (a held tool, worn gear, a harpoon stuck in it: see <see cref="BodyAttachment"/>).
        /// </summary>
        public static HashSet<Transform> BonesOf(Transform body)
        {
            var bones = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer skin in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (BodyAttachment.Covers(skin.transform, body)) continue;
                // Each bone asked once: every mesh lists the whole skeleton, and the attachment walk is a
                // GetComponent per ancestor — 68 meshes by 65 bones on a Nomad was a tenth of a second.
                foreach (Transform bone in skin.bones)
                    if (bone != null && !bones.Contains(bone) && !BodyAttachment.Covers(bone, body)) bones.Add(bone);
            }
            return bones;
        }

        /// <summary>Up the bone (away from its parent) and the body's facing, both in the bone's own space.</summary>
        private static (Vector3 up, Vector3 front) Axes(Transform bone, Transform body, HashSet<Transform> bones)
        {
            Vector3 up = bone.parent != null && bones.Contains(bone.parent)
                ? bone.position - bone.parent.position
                : body.up;
            up = up.sqrMagnitude > 1e-8f ? up.normalized : body.up;

            Vector3 front = Vector3.ProjectOnPlane(body.forward, up);
            if (front.sqrMagnitude < 1e-6f) front = Vector3.ProjectOnPlane(-body.up, up);

            return (bone.InverseTransformDirection(up).normalized,
                    bone.InverseTransformDirection(front.normalized).normalized);
        }

        /// <summary>The shallowest joint whose name contains <paramref name="part"/>, skipping end markers.</summary>
        private static Transform Named(HashSet<Transform> bones, string part)
        {
            Transform best = null;
            int bestDepth = int.MaxValue;
            foreach (Transform bone in bones)
            {
                string n = bone.name.ToLowerInvariant();
                if (!n.Contains(part) || n.Contains("end") || n.Contains("top") || n.Contains("nub")) continue;

                int depth = 0;
                for (Transform t = bone; t != null; t = t.parent) depth++;
                if (depth >= bestDepth) continue;

                bestDepth = depth;
                best = bone;
            }
            return best;
        }

        private static float MeasureHeight(Transform body)
        {
            Bounds? bounds = null;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || BodyAttachment.Covers(r.transform, body)) continue;
                if (bounds is Bounds b) { b.Encapsulate(r.bounds); bounds = b; }
                else bounds = r.bounds;
            }

            if (!(bounds is Bounds all)) return MinimumHeight;
            return Mathf.Max(MinimumHeight, all.size.x, all.size.y, all.size.z);
        }
    }
}
