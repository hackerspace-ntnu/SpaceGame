using System;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// The player's emotes: the gestures that are not tied to an item, played on a chat command
    /// (<c>/wave</c>, <c>/cheer</c>, <c>/shrug</c>, <c>/flex</c>) and seen by everyone.
    ///
    /// <para>
    /// <b>Multiplayer.</b> A gesture is an animator trigger, and triggers do not replicate. An item
    /// gesture rides the item's own <c>Present</c>, which already runs on every machine; an emote
    /// has no item, so it gets one message of its own: the server decides (the chat command runs
    /// there) and sends <see cref="NetMsg.Emote"/> to everyone on THIS PLAYER's relay, and every
    /// machine, the sender's included, raises the trigger through <see cref="PlayerAimRig.PlayGesture"/>
    /// — the one door onto the Upper Body layer, which otherwise sits at weight zero with empty
    /// hands and swallows the clip.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> none. An emote is over in two seconds and a body restored mid-wave
    /// would be a bug.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlayerAimRig))]
    public class PlayerEmotes : MonoBehaviour
    {
        /// <summary>One emote: the word the player types, the animator trigger, the clip's length.</summary>
        [Serializable]
        public struct Emote
        {
            public string Name;
            public string Trigger;
            [Tooltip("Seconds the Upper Body layer is held up for it: the clip's own length.")]
            public float Seconds;
            [Tooltip("What the chat says about it, with {0} for the player's name. Empty for silence.")]
            public string Line;
        }

        /// <summary>
        /// The table the chat commands are registered from and the message indexes into. Order is
        /// the wire format: <see cref="NetArg.A"/> is an index here, so append, never reorder.
        /// </summary>
        public static readonly Emote[] Table =
        {
            new Emote { Name = "wave",  Trigger = "Wave",  Seconds = 2.0f, Line = "{0} waves." },
            new Emote { Name = "cheer", Trigger = "Cheer", Seconds = 1.6f, Line = "{0} cheers!" },
            new Emote { Name = "shrug", Trigger = "Shrug", Seconds = 1.4f, Line = "{0} shrugs." },
            new Emote { Name = "flex",  Trigger = "Flex",  Seconds = 1.8f, Line = "{0} flexes." },
        };

        /// <summary>The table index for a typed name, or -1. Case-insensitive; the chat is.</summary>
        public static int IndexOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            for (int i = 0; i < Table.Length; i++)
                if (string.Equals(Table[i].Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private PlayerAimRig rig;

        private void Awake() => rig = GetComponent<PlayerAimRig>();

        private void OnEnable() => this.NetOn(NetMsg.Emote, OnEmote);

        private void OnDisable() => this.NetOff(NetMsg.Emote, OnEmote);

        /// <summary>
        /// Server-side: play emote <paramref name="index"/> on this body, everywhere. Anything but
        /// a table index is ignored rather than trusted — the index came in off the chat.
        /// </summary>
        public void Play(int index)
        {
            if (!Network.Decides) return;
            if (index < 0 || index >= Table.Length) return;
            this.NetToAll(NetMsg.Emote, new NetArg(a: index));
        }

        private void OnEmote(in NetArg arg, ulong sender)
        {
            if (arg.A < 0 || arg.A >= Table.Length || rig == null) return;
            Emote emote = Table[arg.A];
            rig.PlayGesture(emote.Trigger, emote.Seconds);
        }
    }
}
