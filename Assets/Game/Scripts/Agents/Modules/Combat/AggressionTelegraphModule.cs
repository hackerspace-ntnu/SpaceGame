// Shows what the aggression meter is doing, so the player can read a fight coming and call it off.
//
// The meter (ProvocationModule + AggressionMath) is deliberately invisible. This is the half the
// player actually experiences, and without it the meter is strictly worse than the coin flip it
// replaced: a hidden number that decides when a nomad attacks, with no way to learn the rule and
// no way to back out. GDC-L1-SYS-0006 is explicit about the trade — you may hide the rule, but you
// owe the player observable consequences to form a hypothesis against. So:
//
//   wary   the nomad stops what he is doing and looks at you, and says something.
//   drawn  he plants his feet, brings his weapon up, and gives you a last warning. He will not
//          talk to you while he is like this.
//   grudge the existing fight, unchanged.
//
// All three are reversible until the last. Look away inside wary or drawn and calmRate drains the
// meter and he goes back to work, which is what makes menace a threat the player is making rather
// than damage they have already taken.
//
// NOT on Clankers or Outlaws, and that is composition rather than a flag: their stance is Hostile,
// so AgentTargeting acquires the player on sight and the meter never gets a say. A robot cowboy
// that warns you first is not the fiction. Simply leave this component off those prefabs.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public class AggressionTelegraphModule : BehaviourModuleBase, IFacingModule
    {
        [Header("Barks")]
        [Tooltip("Said once on entering the wary band — the agent has noticed and does not like it.")]
        [SerializeField] private string[] warningLines =
        {
            "Careful where you point that.",
            "I see you.",
            "That is close enough.",
        };

        [Tooltip("Said once on entering the drawn band. The last thing said before a fight.")]
        [SerializeField] private string[] lastWarningLines =
        {
            "Last warning.",
            "Put it down. Now.",
            "Do not make me.",
        };

        [Header("Posture")]
        [Tooltip("Raise the weapon (animator IsAiming) while drawn.")]
        [SerializeField] private bool aimWhileDrawn = true;

        [Tooltip("Plant the feet and face the threat while drawn. Off leaves the agent free to " +
                 "keep walking its errand with its gun up, which reads as indifference.")]
        [SerializeField] private bool standGroundWhileDrawn = true;

        [SerializeField] private int facingPriority = ModulePriority.RangedAttack;

        private ProvocationModule provocation;
        private AgentAnimatorDriver animatorDriver;
        private ChatterModule chatter;
        private AgentAuthority authority;

        // What this MACHINE is presenting. On the authority it follows the meter; on a watcher it
        // follows the band messages. Kept separate from ProvocationModule.Band because a watching
        // client has no meter to read — its copy of the agent is not simulated at all.
        private AggressionBand shown = AggressionBand.Calm;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient + 2);

        private void Awake()
        {
            provocation = GetComponent<ProvocationModule>();
            animatorDriver = GetComponentInChildren<AgentAnimatorDriver>();
            chatter = GetComponent<ChatterModule>();
            authority = new AgentAuthority(this);
        }

        private void OnTransformParentChanged() => authority?.Invalidate();

        private void OnEnable()
        {
            // Read the band rather than assuming Calm: a restored save hands back an agent that was
            // already wary, and OnEnable runs after the saver has set the meter.
            shown = provocation != null ? provocation.Band : AggressionBand.Calm;
            Present(shown, bark: false);

            if (provocation != null)
                provocation.BandChanged += OnBandChanged;

            this.NetOn(NetMsg.AgentActed, OnAgentActed);
        }

        private void OnDisable()
        {
            if (provocation != null)
                provocation.BandChanged -= OnBandChanged;

            this.NetOff(NetMsg.AgentActed, OnAgentActed);

            // Leave the body as we found it. An agent streamed out mid-warning would otherwise come
            // back holding an aim pose with nothing driving it.
            if (animatorDriver != null && aimWhileDrawn)
                animatorDriver.SetIsAiming(false);
        }

        public int FacingPriority => facingPriority;

        public override string ModuleDescription =>
            "Shows the aggression meter: a look and a bark when wary, weapon up and planted when " +
            "drawn, the ordinary fight at the top.\n\n" +
            "• warningLines / lastWarningLines — said once per band entered\n" +
            "• Leave this OFF Clankers and Outlaws: they are Hostile by stance and never climb " +
            "the meter.";

        /// <summary>
        /// The authority's meter moved. Present it here and tell every other machine, because the
        /// meter itself is server state — a watcher has no way to derive this.
        /// </summary>
        private void OnBandChanged(AggressionBand previous, AggressionBand next)
        {
            shown = next;
            Present(next, bark: next > previous);

            // Only the transitions worth seeing. Calm is the resting state and a stream of
            // "he calmed down" messages for every creature in the world cooling off is noise.
            if (Network.Server)
                AgentActionRelay.Broadcast(this, AgentAction.Band, transform.position, transform.forward, (int)next);
        }

        /// <summary>A watching machine drawing the posture the authority actually adopted.</summary>
        private void OnAgentActed(in NetArg arg, ulong sender)
        {
            if (arg.A != AgentAction.Band) return;

            // The deciding machine already presented this while deciding it; NetRelay filters the
            // sender out of its own broadcast, so in practice only a watcher gets here.
            if (authority == null || authority.SimulatedHere) return;

            var next = (AggressionBand)arg.B;
            bool escalating = next > shown;
            shown = next;
            Present(next, bark: escalating);
        }

        /// <summary>
        /// Put the body into <paramref name="band"/>'s posture. Runs on EVERY machine, and touches
        /// nothing but the animator, the popup and a sound — a watcher that presented a swing is a
        /// doubled swing, and the same rule holds for a stance.
        /// </summary>
        private void Present(AggressionBand band, bool bark)
        {
            if (animatorDriver != null && aimWhileDrawn)
                animatorDriver.SetIsAiming(band == AggressionBand.Drawn);

            if (!bark || chatter == null)
                return;

            string[] lines = band switch
            {
                AggressionBand.Wary => warningLines,
                AggressionBand.Drawn => lastWarningLines,
                _ => null,
            };

            if (lines == null || lines.Length == 0)
                return;

            chatter.TrySayNow(lines[Random.Range(0, lines.Length)]);
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // The meter only exists on the deciding machine, so only it may claim a frame. A
            // watcher's copy presents the posture through Present and moves nothing.
            if (provocation == null || !standGroundWhileDrawn)
                return null;

            if (provocation.Band != AggressionBand.Drawn)
                return null;

            Transform threat = provocation.Provoker;
            if (threat == null)
                return null;

            // Planted and facing. Not Idle(): StopAndFace says where to look as well as that we are
            // not going anywhere, and it is what makes "he is watching you specifically" read.
            return MoveIntent.StopAndFace(threat.position);
        }

        /// <summary>
        /// Face the threat from the wary band onward, without claiming the frame for it. Wary is
        /// deliberately facing-only: the nomad looks up from what he is doing rather than dropping
        /// it, which is the difference between being noticed and being confronted.
        /// </summary>
        public bool TryGetFacing(in AgentContext context, out Vector3 facePosition)
        {
            facePosition = Vector3.zero;

            if (provocation == null)
                return false;

            AggressionBand band = provocation.Band;
            if (band != AggressionBand.Wary && band != AggressionBand.Drawn)
                return false;

            Transform threat = provocation.Provoker;
            if (threat == null)
                return false;

            facePosition = threat.position;
            return true;
        }
    }
}
