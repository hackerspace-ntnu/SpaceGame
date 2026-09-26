using SpaceGame.Core;
using SpaceGame.Items;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Plays <see cref="CharacterAction"/>s on a humanoid body. The one door onto the action
    /// layers of the generated controller — one per <see cref="CharacterAction.Slot"/>; nothing
    /// else writes their weights or states.
    ///
    /// <para>
    /// <b>How it plays.</b> Straight to the action's generated state by name hash, with
    /// <c>CrossFadeInFixedTime</c> — no trigger, no Any State transition, no parameter to name.
    /// A one-shot's state hands back to <c>Empty</c> on its own exit time; a loop runs until
    /// <see cref="Stop(CharacterAction.Slot)"/>. A layer's weight is 1 while it is doing something
    /// and 0 once it is resting in Empty, derived from the state every frame rather than kept as
    /// a flag, so an Animator that resets under it (re-enabled after a ragdoll) can never strand
    /// a layer up.
    /// </para>
    /// <para>
    /// <b>Multiplayer.</b> Callers call <see cref="Play"/> on every machine the triggering event
    /// reaches and never ask who should: <see cref="AnimatorAuthority"/> decides here. The player's
    /// body plays only on its owner and NGO's NetworkAnimator carries the state to everyone else,
    /// late joiners included; an NPC's body plays on every machine.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> none. Everything here is seconds long or re-asserted by the gameplay
    /// state that caused it — a seated NPC's loop is played again by whatever seats it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterActions : MonoBehaviour
    {
        [Tooltip("Optional. Found on this object or its children when empty.")]
        [SerializeField] private Animator animator;

        private sealed class Track
        {
            public int Layer = -1;
            public int MirrorHash;
            public int SpeedHash;
            public CharacterAction Action;
            public int Variant;
        }

        private static readonly int EmptyHash = Animator.StringToHash(HumanoidLayers.EmptyState);

        // Built here and everything else found on first use, never in Awake: other modules call in
        // from their own OnEnable during Instantiate, and Unity raises Awake/OnEnable component by
        // component in list order — a caller listed above this component arrives before its Awake
        // (AggressionTelegraphModule did, on every NPC spawn, as a NullReferenceException).
        private readonly Track[] tracks = NewTracks();
        private bool referencesResolved;
        private RuntimeAnimatorController resolvedFor;
        private NetworkAnimator networkAnimator;
        private NetworkObject networkObject;
        private System.Random random;
        private int seed;
        private bool seedFromNetwork;

        /// <summary>
        /// A number fixed per body and equal on every machine once the body is on the network —
        /// what speed picks and idle timing are drawn from, so a crowd stays out of step without
        /// anything being sent.
        /// </summary>
        public int Seed
        {
            get
            {
                if (seedFromNetwork) return seed;
                ResolveReferences();
                if (networkObject != null && networkObject.IsSpawned)
                {
                    seed = unchecked((int)networkObject.NetworkObjectId * 486187739);
                    seedFromNetwork = true;
                }
                else if (seed == 0)
                {
                    seed = GetInstanceID();
                }
                return seed;
            }
        }

        /// <summary>Whether this machine writes this body's actions. See <see cref="AnimatorAuthority"/>.</summary>
        public bool WritesAnimator
        {
            get
            {
                ResolveReferences();
                return AnimatorAuthority.Writes(networkAnimator);
            }
        }

        private void OnEnable() => this.NetOn(NetMsg.CharacterActed, OnActed);

        private void OnDisable() => this.NetOff(NetMsg.CharacterActed, OnActed);

        private void ResolveReferences()
        {
            if (referencesResolved) return;
            referencesResolved = true;

            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            networkAnimator = AnimatorAuthority.Find(animator);
            networkObject = GetComponentInParent<NetworkObject>(true);
        }

        private static Track[] NewTracks()
        {
            var tracks = new Track[System.Enum.GetValues(typeof(CharacterAction.Slot)).Length];
            for (int i = 0; i < tracks.Length; i++)
            {
                var slot = (CharacterAction.Slot)i;
                tracks[i] = new Track
                {
                    MirrorHash = Animator.StringToHash(HumanoidParams.ActionMirror(slot)),
                    SpeedHash = Animator.StringToHash(HumanoidParams.ActionSpeed(slot))
                };
            }
            return tracks;
        }

        /// <summary>
        /// Play <paramref name="action"/>. False when it did not start here — no controller, not
        /// this machine's to write, or an action the controller was not built with (logged).
        /// </summary>
        /// <param name="arm">The arm it is for, when that matters: mirrors an action authored on
        /// the other one (see <see cref="CharacterAction.Mirrors"/>).</param>
        /// <param name="variant">A specific variant — the one a message carried, so every machine
        /// plays the same swing. Negative picks one.</param>
        public bool Play(CharacterAction action, ItemGrip.Hand? arm = null, int variant = -1)
        {
            if (action == null || !Ready() || !WritesAnimator) return false;

            Track track = tracks[(int)action.BodySlot];
            if (track.Layer < 0) return false;

            // A loop asked for again is the caller re-asserting a state, not a new play.
            if (action.Loops && track.Action == action) return true;

            if (variant < 0 || variant >= action.VariantCount) variant = action.PickVariant(Rng);
            if (variant < 0) return false;

            var stage = action.Mode == CharacterAction.Playback.EnterLoopExit
                ? HumanoidLayers.Stage.Enter
                : HumanoidLayers.Stage.Main;
            int state = Animator.StringToHash(HumanoidLayers.StateName(action, variant, stage));
            if (!animator.HasState(track.Layer, state))
            {
                Debug.LogError($"[CharacterActions] '{name}' has no state for action '{action.name}' " +
                               "— it was added after the controller was built. Run Tools/SpaceGame/" +
                               "Animation/Rebuild Humanoid Controller.", this);
                return false;
            }

            // A full-body action owns the arms as well; an arm gesture left running above it would
            // play over the top of it. An additive action is left alone: it adds to whatever pose
            // it lands on, so a recoil already kicking carries on over the full-body action too.
            if (action.BodySlot == CharacterAction.Slot.Full)
            {
                for (int i = 1; i < tracks.Length; i++)
                {
                    var slot = (CharacterAction.Slot)i;
                    if (!HumanoidLayers.IsAdditive(slot)) Stop(slot);
                }
            }

            animator.SetBool(track.MirrorHash, action.Mirrors(arm));
            animator.SetFloat(track.SpeedHash, action.SpeedFor(Seed));
            animator.SetLayerWeight(track.Layer, 1f);
            animator.CrossFadeInFixedTime(state, action.FadeIn, track.Layer, 0f);

            track.Action = action;
            track.Variant = variant;
            return true;
        }

        /// <summary>
        /// Deciding machine only: play <paramref name="action"/> on this body on every machine,
        /// for the actions no replicated event already carries — a flinch decided from damage, a
        /// dwell loop. The variant is picked here and travels, so every machine plays the same one.
        /// </summary>
        /// <param name="except">A client that has already played it locally — the requester of a
        /// relayed gesture. That client is skipped, and this machine then plays it from its own
        /// broadcast like everyone else, so it is never played twice here either.</param>
        public void PlayEverywhere(CharacterAction action, ItemGrip.Hand? arm = null, ulong except = NetTarget.Self)
        {
            if (action == null || !Network.Decides) return;

            int index = CharacterActionCatalog.Default != null ? CharacterActionCatalog.Default.IndexOf(action) : -1;
            if (index < 0)
            {
                Debug.LogError($"[CharacterActions] '{action.name}' is not in the action catalog, so it cannot " +
                               "be sent. Run Tools/SpaceGame/Animation/Rebuild Humanoid Controller.", this);
                return;
            }

            int variant = action.PickVariant(Rng);
            if (except == NetTarget.Self) Play(action, arm, variant);
            this.NetToOthers(NetMsg.CharacterActed, new NetArg(a: index, b: PackVariant(variant, arm)), except);
        }

        /// <summary>Deciding machine only: <see cref="Stop(CharacterAction.Slot)"/> on every machine.</summary>
        public void StopEverywhere(CharacterAction.Slot slot)
        {
            if (!Network.Decides) return;
            Stop(slot);
            this.NetToOthers(NetMsg.CharacterActed, new NetArg(a: -1 - (int)slot));
        }

        /// <summary>
        /// A variant of <paramref name="action"/> from this body's own seeded stream — for a caller
        /// that must know which one plays before it plays, to time a blow to its contact frame and
        /// to send the same pick to every machine.
        /// </summary>
        public int PickVariant(CharacterAction action) => action != null ? action.PickVariant(Rng) : -1;

        /// <summary>
        /// The speed this body plays <paramref name="action"/> at, all multipliers included —
        /// what a timer has to divide by to land on a mark.
        /// </summary>
        public float PlaybackSpeed(CharacterAction action)
        {
            ResolveReferences();
            float animatorSpeed = animator != null ? animator.speed : 1f;
            return action != null ? action.SpeedFor(Seed) * animatorSpeed : animatorSpeed;
        }

        /// <summary>A variant and an arm in one int — NetArg.B of <see cref="NetMsg.CharacterActed"/>.</summary>
        public static int PackVariant(int variant, ItemGrip.Hand? arm) =>
            (variant & 0xFF) | ((arm.HasValue ? (int)arm.Value + 1 : 0) << 8);

        /// <summary>The exact inverse of <see cref="PackVariant"/>.</summary>
        public static (int variant, ItemGrip.Hand? arm) UnpackVariant(int packed)
        {
            int arm = (packed >> 8) & 0xFF;
            return (packed & 0xFF, arm == 0 ? (ItemGrip.Hand?)null : (ItemGrip.Hand)(arm - 1));
        }

        private void OnActed(in NetArg arg, ulong sender)
        {
            if (arg.A < 0)
            {
                Stop((CharacterAction.Slot)(-1 - arg.A));
                return;
            }

            CharacterAction action = CharacterActionCatalog.Default != null ? CharacterActionCatalog.Default.At(arg.A) : null;
            (int variant, ItemGrip.Hand? arm) = UnpackVariant(arg.B);
            Play(action, arm, variant);
        }

        /// <summary>Hand <paramref name="slot"/> back, through the action's exit clip if it has one.</summary>
        public void Stop(CharacterAction.Slot slot)
        {
            Track track = tracks[(int)slot];
            CharacterAction action = track.Action;
            if (action == null || !Ready() || !WritesAnimator) return;

            track.Action = null;

            int exit = Animator.StringToHash(
                HumanoidLayers.StateName(action, track.Variant, HumanoidLayers.Stage.Exit));
            bool hasExit = action.Mode == CharacterAction.Playback.EnterLoopExit
                           && animator.HasState(track.Layer, exit);

            animator.CrossFadeInFixedTime(hasExit ? exit : EmptyHash, action.FadeOut, track.Layer, 0f);
        }

        /// <summary>Stop <paramref name="action"/> if it is what its slot is playing.</summary>
        public void Stop(CharacterAction action)
        {
            if (IsPlaying(action)) Stop(action.BodySlot);
        }

        /// <summary>
        /// Whether <paramref name="action"/> is playing — as far as this machine knows, which on a
        /// watcher of the player's body is never: it did not start it.
        /// </summary>
        public bool IsPlaying(CharacterAction action) =>
            action != null && tracks[(int)action.BodySlot].Action == action;

        /// <summary>What <paramref name="slot"/> is playing on this machine, or null. The same caveat as <see cref="IsPlaying"/>.</summary>
        public CharacterAction PlayingOn(CharacterAction.Slot slot) => tracks[(int)slot].Action;

        private void Update()
        {
            if (!Ready() || !WritesAnimator) return;

            foreach (Track track in tracks)
            {
                if (track.Layer < 0 || animator.GetLayerWeight(track.Layer) <= 0f) continue;
                if (animator.IsInTransition(track.Layer)) continue;
                if (animator.GetCurrentAnimatorStateInfo(track.Layer).shortNameHash != EmptyHash) continue;

                animator.SetLayerWeight(track.Layer, 0f);
                track.Action = null;
            }
        }

        private System.Random Rng => random ??= new System.Random(Seed);

        /// <summary>
        /// Resolve the action layers against the controller actually on the Animator, once per
        /// controller: an NPC can be handed an override controller after it spawns.
        /// </summary>
        private bool Ready()
        {
            ResolveReferences();
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            if (resolvedFor == animator.runtimeAnimatorController) return true;

            resolvedFor = animator.runtimeAnimatorController;
            for (int i = 0; i < tracks.Length; i++)
            {
                string layer = HumanoidLayers.ForSlot((CharacterAction.Slot)i);
                tracks[i].Layer = animator.GetLayerIndex(layer);
                tracks[i].Action = null;

                if (tracks[i].Layer < 0)
                {
                    Debug.LogError($"[CharacterActions] '{name}' has no '{layer}' layer — its controller " +
                                   "is not the generated humanoid one, so actions on it will not play.", this);
                }
            }
            return true;
        }
    }
}
