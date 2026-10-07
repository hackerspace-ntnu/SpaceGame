using SpaceGame.Core;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// The player's emotes: the gestures that are not tied to an item, listed in the
    /// <see cref="EmoteCatalog"/>, started from a chat command (<c>/wave</c>, <c>/dance</c> …) or
    /// the emote wheel, and seen by everyone.
    ///
    /// <para>
    /// <b>Multiplayer.</b> The body's actions are written by its owner alone — NGO's
    /// NetworkAnimator carries the pose to every other machine, late joiners included — so both
    /// ways in end at <see cref="CharacterActions"/> on the owner:
    /// </para>
    /// <list type="bullet">
    /// <item>The <b>wheel</b> already runs on the owner, so <see cref="PlayAsOwner"/> plays at once,
    /// with no round trip: the press is acknowledged on the frame it lands.</item>
    /// <item>A <b>chat command</b> runs on the server, which has no say over a client's animator,
    /// so <see cref="Play"/> sends <see cref="NetMsg.Emote"/> (<c>A</c> = catalog index) on THIS
    /// PLAYER's relay to everyone, and each machine hands it to <see cref="CharacterActions"/>,
    /// which takes it on the owner only.</item>
    /// </list>
    /// <para>
    /// Beyond the catalog, <see cref="PlayAction"/> plays ANY action the controller was built with
    /// the same way (<c>/act</c>), so every animation in the library can be seen on a body in a
    /// live session; <see cref="NetMsg.Emote"/> then carries the action catalog index, flagged by
    /// <c>B</c> = <see cref="ActionIndex"/>.
    /// </para>
    /// <para>
    /// A looping emote (a dance, kneeling) ends the moment the body moves, on the owner, who is
    /// the only machine that started it — whichever way it was started.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> none. An emote is over in seconds and a body restored mid-wave would be
    /// a bug.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CharacterActions))]
    public class PlayerEmotes : MonoBehaviour
    {
        [Tooltip("Animator speed (SpeedX/SpeedY) above which a looping emote ends: the player walked off.")]
        [SerializeField, Min(0f)] private float endLoopAbove = 0.5f;

        /// <summary><see cref="NetMsg.Emote"/>'s <c>B</c> when <c>A</c> is an action catalog index, not an emote.</summary>
        private const int ActionIndex = 1;

        private CharacterActions actions;
        private Animator animator;
        private CharacterAction loop;

        private void Awake()
        {
            actions = GetComponent<CharacterActions>();
            animator = GetComponentInChildren<Animator>(true);
        }

        private void OnEnable() => this.NetOn(NetMsg.Emote, OnEmote);

        private void OnDisable() => this.NetOff(NetMsg.Emote, OnEmote);

        /// <summary>
        /// Server-side: play emote <paramref name="index"/> on this body, everywhere. Anything but
        /// a catalog index is ignored rather than trusted — the index came in off the chat.
        /// </summary>
        public void Play(int index)
        {
            if (!Network.Decides) return;
            if (EntryAt(index) == null) return;
            this.NetToAll(NetMsg.Emote, new NetArg(a: index));
        }

        /// <summary>
        /// Server-side: play <paramref name="action"/> on this body, everywhere, as if it were an
        /// emote. False for an action the controller was not built with.
        /// </summary>
        public bool PlayAction(CharacterAction action)
        {
            int index = CharacterActionCatalog.Default != null ? CharacterActionCatalog.Default.IndexOf(action) : -1;
            if (!Network.Decides || index < 0) return false;
            this.NetToAll(NetMsg.Emote, new NetArg(a: index, b: ActionIndex));
            return true;
        }

        /// <summary>
        /// Owner-side: play emote <paramref name="index"/> on this body now. Nothing goes on the
        /// wire — the NetworkAnimator replicates the owner's layer states. False when this machine
        /// does not write the body or the index names no emote.
        /// </summary>
        public bool PlayAsOwner(int index) => Perform(index);

        private void Update()
        {
            if (loop == null) return;
            if (!actions.IsPlaying(loop))
            {
                loop = null;
                return;
            }

            if (!BodyMoving()) return;
            actions.Stop(loop);
            loop = null;
        }

        private void OnEmote(in NetArg arg, ulong sender)
        {
            if (arg.B == ActionIndex) Perform(CharacterActionCatalog.Default != null ? CharacterActionCatalog.Default.At(arg.A) : null);
            else Perform(arg.A);
        }

        private bool Perform(int index)
        {
            EmoteCatalog.Entry emote = EntryAt(index);
            if (emote == null) return false;

            if (emote.action == null)
            {
                Debug.LogError($"[PlayerEmotes] Emote '{emote.word}' has no action in the emote catalog.", this);
                return false;
            }

            return Perform(emote.action);
        }

        private bool Perform(CharacterAction action)
        {
            if (action == null || !actions.Play(action)) return false;
            if (action.Loops) loop = action;
            return true;
        }

        private static EmoteCatalog.Entry EntryAt(int index)
        {
            EmoteCatalog catalog = EmoteCatalog.Default;
            return catalog != null ? catalog.At(index) : null;
        }

        private bool BodyMoving()
        {
            BodyPosture posture = BodyPostures.Read(animator, endLoopAbove);
            return posture == BodyPosture.Moving || posture == BodyPosture.Airborne;
        }
    }
}
