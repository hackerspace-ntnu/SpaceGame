// One gesture at a time on an NPC's masked Upper Body layer: a punch thrown while the legs keep
// running the Base Layer underneath it.
//
// The player's arms have PlayerAimRig for this. An NPC wearing the same controller has no rig, and
// two things on that layer then defeat a plain SetTrigger with a clean console. The layer sits at
// weight 0 unless HoldAnimator has snapped it up for a held item, so the clip plays and nothing
// appears. And its Any State -> Empty transition (HoldStyle 0, Gesturing false) is true on every
// frame, so even at weight 1 the gesture is evicted a frame after it starts. This holds both open
// for the gesture's length, then hands the layer back to whatever HoldAnimator left there.
//
// A plain class that AgentAnimatorDriver owns and ticks rather than a component of its own: it runs
// wherever the driver runs, which is every machine, so a watcher's copy of the NPC punches too.
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Agents
{
    public sealed class AgentUpperBodyGesture
    {
        private static readonly int HoldStyleHash = Animator.StringToHash(PlayerAimRig.HoldStyleParameter);
        private static readonly int HoldMirrorHash = Animator.StringToHash(PlayerAimRig.HoldMirrorParameter);
        private static readonly int GesturingHash = Animator.StringToHash(PlayerAimRig.GesturingParameter);

        private readonly Animator animator;
        private readonly int layer;
        private readonly float blendTime;

        private float holdTimer;
        private float weight;

        // The frame Play was called on. A gesture is started from Update and ticked in the same
        // frame's LateUpdate, whose deltaTime is the whole PREVIOUS frame: counting it would take
        // a frame's worth off a hold that has not run at all yet, and at a few frames a second
        // that ate most of a punch.
        private int playedFrame = -1;

        // True from Play until the layer is back where HoldAnimator wants it. Outside that window
        // this writes nothing at all, so it never fights a held item's snap.
        private bool owning;

        // HoldMirror as it was before the gesture took it. HoldAnimator never writes the mirror, so
        // a punch thrown with the left fist would otherwise leave an armed NPC holding its gun in
        // the mirrored pose for good.
        private bool restMirror;

        private AgentUpperBodyGesture(Animator animator, int layer, float blendTime)
        {
            this.animator = animator;
            this.layer = layer;
            this.blendTime = blendTime;
        }

        /// <summary>
        /// A gesture player for <paramref name="animator"/>, or null when its controller has no
        /// Upper Body layer, or lacks a parameter that layer's transitions read.
        /// </summary>
        public static AgentUpperBodyGesture For(Animator animator, float blendTime)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return null;

            int layer = animator.GetLayerIndex(PlayerAimRig.UpperBodyLayer);
            if (layer < 0) return null;

            AnimatorControllerParameter[] parameters = animator.parameters;
            if (!Has(parameters, HoldStyleHash) || !Has(parameters, HoldMirrorHash) ||
                !Has(parameters, GesturingHash))
                return null;

            return new AgentUpperBodyGesture(animator, layer, blendTime);
        }

        /// <summary>
        /// Fire <paramref name="trigger"/> on the Upper Body layer and hold the layer up for
        /// <paramref name="seconds"/>. <paramref name="mirrored"/> picks the left-handed twin of
        /// the state, which is how every hold and gesture on that layer is authored.
        /// </summary>
        public void Play(string trigger, float seconds, bool mirrored)
        {
            if (!owning)
            {
                weight = animator.GetLayerWeight(layer);
                restMirror = animator.GetBool(HoldMirrorHash);
                owning = true;
            }

            holdTimer = seconds;
            playedFrame = Time.frameCount;
            animator.SetBool(HoldMirrorHash, mirrored);
            animator.SetBool(GesturingHash, true);
            animator.SetTrigger(trigger);
        }

        public void Tick(float deltaTime)
        {
            if (!owning) return;

            if (holdTimer > 0f && Time.frameCount != playedFrame)
            {
                holdTimer -= deltaTime;
                if (holdTimer <= 0f) EndGesture();
            }

            float target = holdTimer > 0f ? 1f : RestWeight;
            weight = PoseBlend.Ease(weight, target, blendTime, deltaTime);
            animator.SetLayerWeight(layer, weight);

            if (holdTimer <= 0f && Mathf.Approximately(weight, target))
                owning = false;
        }

        /// <summary>
        /// Hand the layer straight back without blending. For a driver being switched off: a
        /// Gesturing bool left true would block every hold pose on the layer for good.
        /// </summary>
        public void Release()
        {
            if (!owning || !animator) return;

            owning = false;
            holdTimer = 0f;
            EndGesture();
            animator.SetLayerWeight(layer, RestWeight);
        }

        // Written together and on the same frame: the layer's Any State transitions read both, and
        // the hold pose it returns to must come back on the arm it was on.
        private void EndGesture()
        {
            animator.SetBool(GesturingHash, false);
            animator.SetBool(HoldMirrorHash, restMirror);
        }

        // HoldAnimator's rule for an NPC on this controller: the layer fully up for any held
        // style, down for none.
        private float RestWeight => animator.GetInteger(HoldStyleHash) != 0 ? 1f : 0f;

        private static bool Has(AnimatorControllerParameter[] parameters, int hash)
        {
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].nameHash == hash) return true;
            return false;
        }
    }
}
