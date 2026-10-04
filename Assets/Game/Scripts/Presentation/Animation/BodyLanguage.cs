using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// What a humanoid body shows of what happens to it: the layer between gameplay and
    /// <see cref="CharacterActions"/> that turns "a line was said", "took a hit", "picked
    /// something up" into a gesture, without the caller naming a clip.
    ///
    /// <para>
    /// <b>Three ways in.</b> <see cref="React(CharacterMoment)"/> — gameplay reports a
    /// <see cref="CharacterMoment"/> and the <see cref="MomentReactions"/> table decides the cue,
    /// the chance and the cooldown. <see cref="Express"/> — a one-shot of a
    /// <see cref="CharacterCue"/> directly. <see cref="Hold"/> / <see cref="Release"/> — a loop
    /// for as long as a state lasts (talking), re-asserted every frame by its owner.
    /// </para>
    /// <para>
    /// <b>Picking.</b> Of the actions tagged with the cue, only those that fit the body's
    /// <see cref="BodyPosture"/> are candidates — a full-body bow standing still, an arm gesture
    /// on the move — never the one picked last time for that cue if there is another, and a cue
    /// with no candidate falls back to its <see cref="CharacterCue.Fallback"/>. A body with
    /// <see cref="fullBody"/> off (the player) never gets a full-body pick: its legs are its
    /// input's (GDC-L1-ANIM-0002). A cue marked <see cref="CharacterCue.NeedsFreeHands"/> also skips
    /// every action that moves an arm the body's held item occupies — nobody waves a hammer.
    /// </para>
    /// <para>
    /// <b>Multiplayer.</b> Raise a moment where it happens. When every machine sees it (chatter,
    /// an idle fidget off the shared clock) or only the owner does (the player's own interact),
    /// call <see cref="React(CharacterMoment)"/>: writing follows <see cref="AnimatorAuthority"/>,
    /// and each roll is seeded from the body and how many times it has had that moment, so the
    /// machines that saw the same moments pick the same thing. When only the server decides it
    /// (damage), call <see cref="ReactEverywhere"/> and the pick travels.
    /// </para>
    /// <para><b>Persistence:</b> none. Cooldowns and picks are seconds long.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyLanguage : MonoBehaviour
    {
        /// <summary>How many fallbacks a cue follows before giving up — a guard against a cycle.</summary>
        public const int MaxFallbacks = 4;

        [Tooltip("Optional. Found on this object or its parents when empty.")]
        [SerializeField] private CharacterActions actions;

        [Tooltip("Optional. This body's own reactions: a moment with a row here uses it instead of " +
                 "the default table's (Resources/Animation/MomentReactions).")]
        [SerializeField] private MomentReactions reactions;

        [Tooltip("Whether a cue may take over the whole body when it stands still. Off for the " +
                 "player, whose legs belong to its input.")]
        [SerializeField] private bool fullBody = true;

        [Tooltip("Planar animator speed above which the body counts as moving.")]
        [SerializeField, Min(0f)] private float movingAbove = 0.5f;

        [Tooltip("Seconds between attempts to start a held loop while its slot is busy.")]
        [SerializeField, Min(0f)] private float holdRetrySeconds = 0.25f;

        private readonly Dictionary<CharacterCue, CharacterAction> lastPicked = new Dictionary<CharacterCue, CharacterAction>();
        private readonly Dictionary<CharacterCue, CharacterAction> held = new Dictionary<CharacterCue, CharacterAction>();
        private readonly Dictionary<CharacterCue, float> holdRetryAt = new Dictionary<CharacterCue, float>();
        private readonly Dictionary<CharacterMoment, float> readyAt = new Dictionary<CharacterMoment, float>();
        private readonly Dictionary<int, int> rollCount = new Dictionary<int, int>();
        private Animator animator;
        private BodyArms occupied;
        private bool seated;

        private static readonly List<CharacterAction> Candidates = new List<CharacterAction>();

        /// <summary>The body under <paramref name="anyPart"/>, or null for one without body language.</summary>
        public static BodyLanguage Of(Component anyPart) =>
            anyPart != null ? anyPart.GetComponentInParent<BodyLanguage>() : null;

        /// <summary><see cref="React(CharacterMoment)"/> on the body <paramref name="anyPart"/> belongs to, if it has one.</summary>
        public static bool React(Component anyPart, CharacterMoment moment)
        {
            BodyLanguage body = Of(anyPart);
            return body != null && body.React(moment);
        }

        /// <summary><see cref="ReactEverywhere(CharacterMoment)"/> on the body <paramref name="anyPart"/> belongs to, if it has one.</summary>
        public static bool ReactEverywhere(Component anyPart, CharacterMoment moment)
        {
            BodyLanguage body = Of(anyPart);
            return body != null && body.ReactEverywhere(moment);
        }

        public CharacterActions Actions
        {
            get
            {
                if (actions == null) actions = GetComponentInParent<CharacterActions>();
                return actions;
            }
        }

        /// <summary>What the body's legs are doing, from its animator parameters.</summary>
        public BodyPosture Posture
        {
            get
            {
                if (animator == null) animator = GetComponentInChildren<Animator>(true);
                return BodyPostures.Read(animator, movingAbove);
            }
        }

        /// <summary>
        /// Says the body sits on a seat. A resident on a cushion sits in a Full loop of its own and its animator never raises
        /// <c>Seated</c>, so it reads Standing, and a greeting bow or a stretch picked for a standing body would stand it up out
        /// of the seat for a second (a resident on a stool does raise <c>Seated</c>, but is kept to the same rule). A seated
        /// body's legs belong to the seat, like the player's belong to its input: its one-shots are arm gestures only.
        /// </summary>
        public void SitOn(bool sitting) => seated = sitting;

        /// <summary>The arms the body's held item occupies (from its animator parameters), and the ones something else has hold of.</summary>
        public BodyArms BusyArms
        {
            get
            {
                if (animator == null) animator = GetComponentInChildren<Animator>(true);
                return BodyHands.Busy(animator) | occupied;
            }
        }

        /// <summary>
        /// Says which arms something other than a held item has hold of (a pusher's hands on a cart's handles), so no gesture plays
        /// on them. <see cref="BodyArms.None"/> gives them back.
        /// </summary>
        public void OccupyArms(BodyArms arms) => occupied = arms;

        /// <summary>
        /// True while nobody can see the body (a resident indoors or offstage): it starts no gesture, fidget or loop, and
        /// <see cref="IdleVariation"/> and <see cref="SpeechGestures"/> stand idle. Whoever hides the body sets it, and clears it
        /// when the body is seen again; a loop held when it was set is the owner's to release.
        /// </summary>
        public bool Dormant { get; set; }

        private bool Writes => Actions != null && Actions.WritesAnimator && !Dormant;

        /// <summary>
        /// Stops the one-shot gestures that are moving an arm the held item now occupies. A gesture is picked while the hand is
        /// empty; a body that then draws a tool or takes up a basket must not go on stretching through it.
        /// </summary>
        public void DropGesturesOverHands()
        {
            if (!Writes) return;

            BodyArms busy = BusyArms;
            if (busy == BodyArms.None) return;

            for (int i = 0; i < (int)CharacterAction.Slot.Additive; i++)
            {
                var slot = (CharacterAction.Slot)i;
                if (IsGestureOver(Actions.PlayingOn(slot), busy)) Actions.Stop(slot);
            }
        }

        /// <summary>
        /// Whether <paramref name="action"/> is a one-shot gesture (one of its cues is the body language of an empty hand) that
        /// moves an arm in <paramref name="busy"/>. A loop is its owner's to hold or release; an action done WITH the item is not one.
        /// </summary>
        public static bool IsGestureOver(CharacterAction action, BodyArms busy)
        {
            if (action == null || action.Loops || action.FitsHands(busy)) return false;

            foreach (CharacterCue cue in action.Cues)
                if (cue != null && cue.NeedsFreeHands) return true;
            return false;
        }

        /// <summary>
        /// Show <paramref name="moment"/> here, if this body's table has a reaction for it and the
        /// chance and cooldown allow. True when something started.
        /// </summary>
        public bool React(CharacterMoment moment) => React(moment, NextRoll((int)moment));

        /// <summary>
        /// <see cref="React(CharacterMoment)"/> with the roll fixed by <paramref name="salt"/> — for a
        /// caller whose machines agree on a number (a bucket of the shared clock) and so must agree
        /// on the pick too, however many moments each has seen.
        /// </summary>
        public bool React(CharacterMoment moment, int salt)
        {
            if (!Writes || !TryRow(moment, out MomentReactions.Row row)) return false;

            System.Random roll = Roll((int)moment, salt);
            if (roll.NextDouble() >= row.chance) return false;

            if (!PlayPicked(row.action != null ? row.action : Resolve(row.cue, false, roll), roll)) return false;
            readyAt[moment] = Time.time + row.cooldown;
            return true;
        }

        /// <summary>
        /// Deciding machine only: roll <paramref name="moment"/>'s reaction here and play it on every
        /// machine, for moments only the server knows happened. True when an action was sent — so
        /// a caller whose gameplay only counts if it is SEEN (a block, a dodge) can tell a body
        /// that showed it from one with nothing fitting its posture.
        /// </summary>
        public bool ReactEverywhere(CharacterMoment moment)
        {
            if (!Network.Decides || Actions == null || !TryRow(moment, out MomentReactions.Row row)) return false;

            System.Random roll = Roll((int)moment, NextRoll((int)moment));
            if (roll.NextDouble() >= row.chance) return false;

            CharacterAction action = row.action != null ? row.action : Resolve(row.cue, false, roll);
            if (action == null) return false;

            Actions.PlayEverywhere(action, Arm(roll));
            readyAt[moment] = Time.time + row.cooldown;
            return true;
        }

        /// <summary>A one-shot of <paramref name="cue"/> here. The action played, or null.</summary>
        public CharacterAction Express(CharacterCue cue)
        {
            if (!Writes || cue == null) return null;

            System.Random roll = CueRoll(cue);
            CharacterAction action = Resolve(cue, false, roll);
            return PlayPicked(action, roll) ? action : null;
        }

        /// <summary>
        /// The one-shot <paramref name="cue"/> would play on this body right now, without playing
        /// it — for a machine that decides and then sends the action itself (a chat command on the
        /// server). Null when nothing fits.
        /// </summary>
        public CharacterAction Pick(CharacterCue cue) =>
            cue != null && Actions != null ? Resolve(cue, false, CueRoll(cue)) : null;

        /// <summary>
        /// Keep a loop of <paramref name="cue"/> playing — call every frame while the state lasts,
        /// then <see cref="Release"/>. Waits while the slot it needs is busy with something else,
        /// and swaps to a fitting loop when the posture changes under it (the speaker sets off walking).
        /// </summary>
        public CharacterAction Hold(CharacterCue cue) => Hold(cue, null);

        /// <summary>
        /// <see cref="Hold(CharacterCue)"/> with the pick fixed by <paramref name="pickSalt"/>: the same
        /// body asked again with the same salt (a worker returning to its spot after looking up) plays
        /// the same loop, on every machine, instead of re-rolling the cue's pool each time.
        /// </summary>
        public CharacterAction Hold(CharacterCue cue, int pickSalt) => Hold(cue, (int?)pickSalt);

        private CharacterAction Hold(CharacterCue cue, int? pickSalt)
        {
            if (!Writes || cue == null) return null;

            BodyPosture posture = Posture;
            if (held.TryGetValue(cue, out CharacterAction current) && current != null)
            {
                bool playing = Actions.IsPlaying(current);
                if (playing && Allowed(cue, current, posture)) return current;
                if (playing) Actions.Stop(current);
                held.Remove(cue);
            }

            if (holdRetryAt.TryGetValue(cue, out float retry) && Time.time < retry) return null;
            holdRetryAt[cue] = Time.time + holdRetrySeconds;

            System.Random roll = pickSalt.HasValue ? Roll(Animator.StringToHash(cue.name), pickSalt.Value) : CueRoll(cue);
            CharacterAction action = Resolve(cue, true, roll, avoidLast: !pickSalt.HasValue);
            if (action == null || Actions.PlayingOn(action.BodySlot) != null) return null;
            if (!PlayPicked(action, roll)) return null;

            held[cue] = action;
            return action;
        }

        /// <summary>Stop the loop <see cref="Hold"/> started for <paramref name="cue"/>, if it still plays.</summary>
        public void Release(CharacterCue cue)
        {
            if (cue == null) return;
            holdRetryAt.Remove(cue);
            if (!held.TryGetValue(cue, out CharacterAction action)) return;

            held.Remove(cue);
            if (action != null && Actions != null) Actions.Stop(action);
        }

        /// <summary>
        /// Of <paramref name="tagged"/>, one action that fits: right posture, full body only when
        /// allowed, looping or not as asked, none of its arms in <paramref name="occupied"/>, and not
        /// <paramref name="avoid"/> while another fits. Null when none does.
        /// </summary>
        public static CharacterAction Choose(IReadOnlyList<CharacterAction> tagged, BodyPosture posture, bool fullBody,
                                             bool loops, CharacterAction avoid, System.Random roll,
                                             BodyArms occupied = BodyArms.None)
        {
            Candidates.Clear();
            foreach (CharacterAction action in tagged)
            {
                if (action == null || action.VariantCount == 0 || !action.Fits(posture)) continue;
                if (!fullBody && action.BodySlot == CharacterAction.Slot.Full) continue;
                if (loops != action.Loops || !action.FitsHands(occupied)) continue;
                Candidates.Add(action);
            }

            if (Candidates.Count > 1) Candidates.Remove(avoid);
            return Candidates.Count == 0 ? null : Candidates[roll.Next(Candidates.Count)];
        }

        private CharacterAction Resolve(CharacterCue cue, bool loops, System.Random roll, bool avoidLast = true)
        {
            CharacterActionCatalog catalog = CharacterActionCatalog.Default;
            if (catalog == null) return null;

            BodyPosture posture = Posture;
            BodyArms busy = BusyArms;
            bool wholeBody = fullBody && !(seated && !loops);
            for (int depth = 0; cue != null && depth <= MaxFallbacks; depth++, cue = cue.Fallback)
            {
                CharacterAction last = null;
                if (avoidLast) lastPicked.TryGetValue(cue, out last);
                CharacterAction action = Choose(catalog.ActionsFor(cue), posture, wholeBody, loops, last, roll,
                                                 cue.NeedsFreeHands ? busy : BodyArms.None);
                if (action == null) continue;

                lastPicked[cue] = action;
                return action;
            }
            return null;
        }

        private bool Allowed(CharacterCue cue, CharacterAction action, BodyPosture posture) =>
            action.Fits(posture) && (fullBody || action.BodySlot != CharacterAction.Slot.Full)
            && action.FitsHands(cue.NeedsFreeHands ? BusyArms : BodyArms.None);

        private bool PlayPicked(CharacterAction action, System.Random roll) =>
            action != null && Actions.Play(action, Arm(roll), action.PickVariant(roll));

        // Either arm: an action authored on one and marked mirrorable plays on both, doubling what a
        // crowd shows; one that is not ignores the arm.
        private static ItemGrip.Hand Arm(System.Random roll) => roll.Next(2) == 0 ? ItemGrip.Hand.Left : ItemGrip.Hand.Right;

        private bool TryRow(CharacterMoment moment, out MomentReactions.Row row)
        {
            row = (reactions != null ? reactions.Find(moment) : null)
                  ?? (MomentReactions.Default != null ? MomentReactions.Default.Find(moment) : null);
            if (row == null || row.Silent) return false;
            return !readyAt.TryGetValue(moment, out float ready) || Time.time >= ready;
        }

        private int NextRoll(int key)
        {
            rollCount.TryGetValue(key, out int n);
            rollCount[key] = n + 1;
            return n;
        }

        private System.Random CueRoll(CharacterCue cue)
        {
            int key = Animator.StringToHash(cue.name);
            return Roll(key, NextRoll(key));
        }

        private System.Random Roll(int key, int salt)
        {
            unchecked
            {
                uint h = (uint)Actions.Seed * 0x9E3779B1u ^ (uint)key * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 15;
                return new System.Random((int)h);
            }
        }
    }
}
