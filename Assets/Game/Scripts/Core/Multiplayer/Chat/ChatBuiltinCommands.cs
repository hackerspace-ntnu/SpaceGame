using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The commands that ship with the game, and the player lookup they share.
    /// <para>
    /// Registered from a runtime hook rather than from <see cref="ChatCommands"/>'s own static
    /// constructor, so the table stays ignorant of what a player or a teleport is and anything else
    /// can add commands the same way this does.
    /// </para>
    /// </summary>
    public static class ChatBuiltinCommands
    {
        /// <summary>How far behind the target you land, in metres. Far enough not to be inside them.</summary>
        private const float ArrivalDistance = 1.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            // Register replaces by name, so running this again after a domain reload leaves one
            // entry per command rather than a duplicate.
            ChatCommands.Register("tp", "/tp <player>", "Teleport to another player.",
                Teleport, "teleport");

            ChatCommands.Register("help", "/help", "List the commands.", Help, "?", "commands");
        }

        // ------------------------------------------------------------------- commands

        /// <summary>
        /// Puts the sender just behind the named player, facing the same way they are.
        /// <para>
        /// The move goes through <see cref="NetworkedTeleport"/> and not through the transform,
        /// because the player's NetworkTransform is owner-authoritative: a server that writes a
        /// remote player's position has it overwritten by that player's next state update, within a
        /// tick and without an error. Every teleporting system in this project has hit that
        /// independently.
        /// </para>
        /// </summary>
        private static string Teleport(ulong sender, string[] args)
        {
            if (args.Length == 0) return "Usage: /tp <player>";

            // Joined rather than args[0], so a name with a space in it still resolves.
            string wanted = string.Join(" ", args);

            if (!TryResolvePlayer(wanted, out PlayerIdentity target, out string problem))
                return problem;

            if (target.OwnerClientId == sender)
                return "You are already exactly where you are.";

            GameObject body = FindBody(sender);
            if (body == null) return "You have no body to teleport right now.";

            Transform anchor = target.transform;

            // Flattened: a target standing on a slope, or one whose root is tilted by a mount,
            // would otherwise drop the arriving player through the ground or into the air.
            Vector3 facing = anchor.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            facing.Normalize();

            Vector3 destination = anchor.position - facing * ArrivalDistance;

            NetworkedTeleport.Move(body, destination, Quaternion.LookRotation(facing, Vector3.up));

            return $"Teleported to {target.DisplayName}.";
        }

        private static string Help(ulong sender, string[] args)
        {
            var builder = new StringBuilder("Commands: ");

            for (int i = 0; i < ChatCommands.All.Count; i++)
            {
                if (i > 0) builder.Append("  ");
                builder.Append(ChatCommands.All[i].Usage);
            }

            return builder.ToString();
        }

        // -------------------------------------------------------------------- lookup

        /// <summary>
        /// Finds the player <paramref name="wanted"/> names, or explains why it could not.
        /// <para>
        /// Exact match first, then a unique prefix, so <c>/tp fer</c> works but <c>/tp p</c> in a
        /// session of Pia and Per is refused rather than teleporting you to whichever of them
        /// happened to spawn first. Both passes are case-insensitive; nobody types their own
        /// capitalisation correctly under fire.
        /// </para>
        /// </summary>
        public static bool TryResolvePlayer(string wanted, out PlayerIdentity found, out string problem)
        {
            found = null;
            problem = string.Empty;

            if (string.IsNullOrWhiteSpace(wanted))
            {
                problem = "Name somebody.";
                return false;
            }

            wanted = wanted.Trim();

            var roster = PlayerIdentity.All;

            for (int i = 0; i < roster.Count; i++)
            {
                PlayerIdentity identity = roster[i];
                if (identity == null || !identity.IsSpawned) continue;

                if (string.Equals(identity.DisplayName, wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    found = identity;
                    return true;
                }
            }

            PlayerIdentity partial = null;
            bool ambiguous = false;

            for (int i = 0; i < roster.Count; i++)
            {
                PlayerIdentity identity = roster[i];
                if (identity == null || !identity.IsSpawned) continue;

                if (!identity.DisplayName.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (partial != null) ambiguous = true;
                partial = identity;
            }

            if (ambiguous)
            {
                problem = $"'{wanted}' matches more than one player. Type more of the name.";
                return false;
            }

            if (partial == null)
            {
                problem = $"No player called '{wanted}' is in this session.";
                return false;
            }

            found = partial;
            return true;
        }

        /// <summary>
        /// The player object belonging to <paramref name="clientId"/>.
        /// <para>
        /// Asks the spawn manager first, which is the authoritative answer on the server, and falls
        /// back to the replicated <see cref="PlayerIdentity"/> roster — the one list every peer has
        /// a complete copy of.
        /// </para>
        /// </summary>
        public static GameObject FindBody(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (manager != null && manager.IsListening && manager.SpawnManager != null)
            {
                NetworkObject player = manager.SpawnManager.GetPlayerNetworkObject(clientId);
                if (player != null) return player.gameObject;
            }

            var roster = PlayerIdentity.All;
            for (int i = 0; i < roster.Count; i++)
            {
                PlayerIdentity identity = roster[i];
                if (identity != null && identity.IsSpawned && identity.OwnerClientId == clientId)
                    return identity.gameObject;
            }

            return null;
        }
    }
}
