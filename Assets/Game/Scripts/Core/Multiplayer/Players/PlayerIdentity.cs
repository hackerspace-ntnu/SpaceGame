using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Core
{
    /// <summary>
    /// Carries a player's chosen name across the session, so every peer can put a name to every
    /// other player.
    /// <para>
    /// Nothing in the project synced one before this: the lobby screen invents a
    /// <c>"Player" + Random</c> for the Unity Lobby service and drops it at the loading screen, and
    /// the match leaderboard falls back to <c>"Player {clientId + 1}"</c>. Both are per-screen
    /// labels, not an identity.
    /// </para>
    /// <para>
    /// It lives on the player prefab rather than on a session-wide manager because the player
    /// object is the one networked thing that already exists once per human, spawns and despawns on
    /// exactly the right events, and is replicated to every peer without anything extra being
    /// placed in a scene. The name is owner-write, so each client is the authority on its own name
    /// and no RPC round-trip is needed to change it.
    /// </para>
    /// <para>
    /// The list of everyone currently spawned is <see cref="All"/>; <see cref="PlayerRoster"/>
    /// turns it into the rows a player list shows.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerIdentity : NetworkBehaviour
    {
        // 64 rather than 32 bytes: the limit players are shown is 20 characters, and a name in a
        // non-Latin script can run to four bytes a character.
        private readonly NetworkVariable<FixedString64Bytes> displayName = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// The suit colour this player wears, as an index into <c>SuitPalette.Swatches</c>.
        /// <para>
        /// An int, not a string. The palette is a static table compiled into every build, so the
        /// index is all that has to cross the wire — and it sidesteps the FixedString conversion trap
        /// that the name above has to be careful about.
        /// </para>
        /// <para>
        /// Owner-write like the name, and for the same reason: each client is the authority on its
        /// own appearance, so changing it needs no RPC round trip. Starts at -1 rather than 0 so
        /// <see cref="SuitRecolor"/> can tell "nobody has published yet" from "somebody chose
        /// Ember" — without that, every player flashes orange for a frame on spawn.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<int> suitColor = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// Which team this player stands on in a versus match, or -1 outside one.
        /// <para>
        /// SERVER-write, unlike the name and the colour above. Those are owner-write because each
        /// client is the authority on how it presents itself; a team is the opposite kind of value.
        /// The server needs it BEFORE it places a body — the spawn flow reads it to pick which
        /// ship the player starts in — and an owner-written value would arrive after the spawn it
        /// was supposed to inform. Owner-write would also let a client put itself on whichever side
        /// it liked.
        /// </para>
        /// <para>
        /// Assigned by <see cref="VersusTeamRoster"/> during the spawn flow. -1 rather than 0 so a
        /// story-world player reads as "no team" instead of as a member of the first one.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<int> team = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Tooltip("The component that paints this player's astronaut. Found in children when unset.")]
        [SerializeField] private SuitRecolor recolor;

        private static readonly List<PlayerIdentity> instances = new();

        /// <summary>Raised when a player joins, leaves, or renames — anything that changes the list.</summary>
        public static event Action RosterChanged;

        /// <summary>Every player object currently spawned on this peer, host first.</summary>
        public static IReadOnlyList<PlayerIdentity> All => instances;

        /// <summary>
        /// The name to show. Falls back to the client id while the name is still in flight, so a
        /// player list never has a blank row for someone who has only just joined.
        /// </summary>
        public string DisplayName
        {
            get
            {
                string value = displayName.Value.ToString();
                return string.IsNullOrWhiteSpace(value) ? $"Player {OwnerClientId + 1}" : value;
            }
        }

        /// <summary>
        /// True once this player's own chosen name has actually replicated, as opposed to
        /// <see cref="DisplayName"/> still answering with the <c>Player N</c> stand-in.
        /// <para>
        /// The two are not distinguishable from <see cref="DisplayName"/> alone, and the difference
        /// matters to anything that announces a player by name: a connection callback fires long
        /// before the owner has written this variable, so a join message sent on the callback names
        /// somebody "Player 3" who is about to become somebody.
        /// </para>
        /// </summary>
        public bool HasPublishedName => displayName.Value.Length > 0;

        /// <summary>True for the peer hosting the session, which is worth marking in a player list.</summary>
        public bool IsSessionHost => OwnerClientId == NetworkManager.ServerClientId;

        /// <summary>Which team this player is on, or -1 when they are not in a versus match.</summary>
        public int Team => team.Value;

        /// <summary>
        /// Publishes this player's team to every peer. Server-only — see <see cref="team"/>.
        ///
        /// Warns rather than failing silently when called from anywhere else, because the write
        /// would be rejected by Netcode and the player would simply stay teamless, which looks
        /// exactly like nobody having called this at all.
        /// </summary>
        public void SetTeam(int value)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"[Net] '{name}' was told to join team {value} by a machine that is " +
                                 "not the server. Ignored — the write would not have replicated.", this);
                return;
            }

            team.Value = value;
        }

        public override void OnNetworkSpawn()
        {
            instances.Add(this);
            displayName.OnValueChanged += OnNameChanged;
            suitColor.OnValueChanged += OnSuitColorChanged;
            team.OnValueChanged += OnTeamChanged;

            if (IsOwner)
            {
                PublishLocalProfile();
                GameSettings.Changed += PublishLocalProfile;
            }

            // Applied from whatever has already replicated rather than waiting for a change event:
            // a client joining a running session receives the current value as part of the spawn
            // payload, so the change event for it has already been and gone.
            ApplySuitColor();

            RosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            instances.Remove(this);
            displayName.OnValueChanged -= OnNameChanged;
            suitColor.OnValueChanged -= OnSuitColorChanged;
            team.OnValueChanged -= OnTeamChanged;

            if (IsOwner)
                GameSettings.Changed -= PublishLocalProfile;

            RosterChanged?.Invoke();
        }

        // OnNetworkDespawn does not run for an object destroyed without a despawn (a hard
        // disconnect, or a scene unload during shutdown), and a stale entry in a static list
        // outlives the session it belonged to.
        //
        // override, not a private OnDestroy. Declared privately this HID NetworkBehaviour.OnDestroy,
        // which is what disposes this behaviour's NetworkVariables and drops it from its
        // NetworkObject's ChildNetworkBehaviours list — so the displayName variable's native buffer
        // leaked on every player despawn, which in a session people join and leave is every time.
        // NetworkedHealthComponent and PlayerInventoryNetwork carry the same note.
        public override void OnDestroy()
        {
            if (instances.Remove(this))
                RosterChanged?.Invoke();

            base.OnDestroy();
        }

        private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
            => RosterChanged?.Invoke();

        private void OnSuitColorChanged(int previous, int current) => ApplySuitColor();

        // A team is shown beside a name wherever players are listed, so a change to it changes the
        // list the same way a rename does.
        private void OnTeamChanged(int previous, int current) => RosterChanged?.Invoke();

        /// <summary>
        /// Pushes this peer's stored name and suit colour onto the wire.
        ///
        /// Both live in <see cref="GameSettings"/> and both are published from the same place,
        /// because they change through the same event — a settings page write, or the lobby's
        /// cycler. Each is compared before being written so an unrelated settings change (a volume
        /// slider) does not dirty either variable.
        /// </summary>
        private void PublishLocalProfile()
        {
            if (!IsOwner || !IsSpawned) return;

            var wanted = new FixedString64Bytes(Truncate(GameSettings.PlayerName));
            if (!wanted.Equals(displayName.Value)) displayName.Value = wanted;

            int wantedColor = SuitPalette.Clamp(GameSettings.SuitColorIndex);
            if (wantedColor != suitColor.Value) suitColor.Value = wantedColor;
        }

        /// <summary>
        /// Paints the body from the replicated value.
        ///
        /// Runs on every peer, including the owner: the owner is not a special case, because what
        /// everyone should see is what was actually published rather than what this machine happens
        /// to have stored. A value of -1 means nothing has been published yet, and is left alone so
        /// the model keeps its authored colours until the real choice lands.
        /// </summary>
        private void ApplySuitColor()
        {
            if (suitColor.Value < 0) return;

            if (recolor == null) recolor = GetComponentInChildren<SuitRecolor>(true);
            if (recolor == null) return;

            recolor.Apply(suitColor.Value);
        }

        /// <summary>
        /// Assigning a string longer than the FixedString's capacity throws, so the byte length —
        /// not the character count — is what has to be checked before it is written.
        /// </summary>
        private static string Truncate(string name)
        {
            const int maxBytes = 60; // FixedString64Bytes holds 61; one spare for safety.

            string trimmed = GameSettings.SanitiseName(name);
            while (System.Text.Encoding.UTF8.GetByteCount(trimmed) > maxBytes && trimmed.Length > 1)
                trimmed = trimmed[..^1];

            return trimmed;
        }
    }
}
