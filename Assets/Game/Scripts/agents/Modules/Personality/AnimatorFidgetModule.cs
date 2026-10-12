// Now and then, while the creature stands idle, plays one of its fidgets: a Caraxoid chasing its
// own tail, scratching, a chick begging. Animation only — it never claims movement, so a fidget is
// whatever the animator does with the trigger while the ladder carries on (a fidget state should
// fall back to locomotion by exit time, and anything that moves the body interrupts it there).
//
// Fired on the owner like every module tick; the animator replicates the trigger. Nothing to save:
// the next fidget is a fresh random wait after a load.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class AnimatorFidgetModule : BehaviourModuleBase
    {
        [Tooltip("Animator triggers to pick from, one at random each time.")]
        [SerializeField] private string[] triggers = { "TailPlay", "Scratch" };
        [Tooltip("Random wait between fidgets, in seconds, counted only while standing idle.")]
        [SerializeField] private Vector2 intervalRange = new Vector2(12f, 30f);
        [Tooltip("Bool that, while true, suppresses fidgets — e.g. IsResting while lying in a nest. " +
                 "Empty for none.")]
        [SerializeField] private string suppressWhileBool = "IsResting";

        private AgentAnimatorDriver animatorDriver;
        private Animator animator;
        private float wait;

        public override bool ClaimsMovement => false;

        private void Reset() => SetPriorityDefault(ModulePriority.Personality);

        public override string ModuleDescription =>
            "Fires a random animator trigger from `triggers` every few seconds of standing idle " +
            "with no target. Never claims movement.";

        private void Awake()
        {
            animatorDriver = GetComponentInChildren<AgentAnimatorDriver>(true);
            animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable() => wait = Random.Range(intervalRange.x, intervalRange.y);

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            bool busy = context.IsMoving || (context.Targeting != null && context.Targeting.HasTarget) || Suppressed();
            if (busy || triggers.Length == 0 || animatorDriver == null)
                return null;

            wait -= deltaTime;
            if (wait > 0f)
                return null;

            wait = Random.Range(intervalRange.x, intervalRange.y);
            animatorDriver.TriggerByName(triggers[Random.Range(0, triggers.Length)]);
            return null;
        }

        private bool Suppressed()
        {
            if (string.IsNullOrEmpty(suppressWhileBool) || animator == null || animator.runtimeAnimatorController == null)
                return false;
            // GetBool on a parameter the controller lacks warns every call, so ask once.
            if (suppressHash == 0)
            {
                suppressHash = Animator.StringToHash(suppressWhileBool);
                hasSuppressBool = System.Array.Exists(animator.parameters,
                    p => p.nameHash == suppressHash && p.type == AnimatorControllerParameterType.Bool);
            }
            return hasSuppressBool && animator.GetBool(suppressHash);
        }

        private int suppressHash;
        private bool hasSuppressBool;

        protected override void OnValidate()
        {
            intervalRange.x = Mathf.Max(1f, intervalRange.x);
            intervalRange.y = Mathf.Max(intervalRange.x, intervalRange.y);
        }
    }
}
