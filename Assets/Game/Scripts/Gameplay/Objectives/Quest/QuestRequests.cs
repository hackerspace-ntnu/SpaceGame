using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What a player's conversation asks the server for: "I finished talking to this character" and
    /// "I accept the chain this character offered". A conversation runs on the talker's own machine
    /// (<c>DialogInteraction</c> is local), so these are the only parts of it that reach the server.
    ///
    /// <para>
    /// <b>The server believes the distance, not the client.</b> A request is honoured only when the
    /// asking player's body is within the character's <see cref="QuestActor.TalkRange"/> on the server
    /// — so no client can finish "talk to the elder" from across the map, and a host or offline player
    /// goes through the same check with its own body.
    /// </para>
    /// </summary>
    public static class QuestRequests
    {
        /// <summary>The local player <paramref name="talker"/> finished a conversation with <paramref name="actor"/>.</summary>
        public static void Talked(QuestActor actor, Transform talker)
        {
            if (actor == null) return;

            if (Network.Decides) ServerTalked(actor.ActorId, talker);
            else if (QuestRequestNetwork.Active != null) QuestRequestNetwork.Active.SendTalked(actor.ActorId);
            else Debug.LogError("[Quest] No QuestRequestNetwork to carry a conversation to the server.");
        }

        /// <summary>The local player <paramref name="talker"/> accepted the chain <paramref name="chainId"/> from <paramref name="actor"/>.</summary>
        public static void Accepted(QuestActor actor, string chainId, Transform talker)
        {
            if (actor == null) return;

            if (Network.Decides) ServerAccepted(actor.ActorId, chainId, talker);
            else if (QuestRequestNetwork.Active != null) QuestRequestNetwork.Active.SendAccepted(actor.ActorId, chainId);
            else Debug.LogError("[Quest] No QuestRequestNetwork to carry an accepted quest to the server.");
        }

        /// <summary>SERVER: <paramref name="talker"/>'s player finished a conversation with the actor <paramref name="actorId"/>.</summary>
        internal static void ServerTalked(string actorId, Transform talker)
        {
            if (TryReach(actorId, talker, out _)) QuestMarks.Raise(QuestMark.Talked, actorId);
        }

        /// <summary>SERVER: <paramref name="talker"/>'s player accepted <paramref name="chainId"/> from the actor <paramref name="actorId"/>.</summary>
        internal static void ServerAccepted(string actorId, string chainId, Transform talker)
        {
            if (!TryReach(actorId, talker, out QuestActor actor)) return;

            if (!actor.TryGetComponent(out QuestDialog dialog) || !dialog.Offers(chainId))
            {
                Debug.LogWarning($"[Quest] '{actorId}' was asked for chain '{chainId}', which it does not offer.", actor);
                return;
            }

            if (ObjectiveDirector.Instance != null) ObjectiveDirector.Instance.StartQuest(chainId);
        }

        private static bool TryReach(string actorId, Transform talker, out QuestActor actor)
        {
            if (!QuestActor.TryFind(actorId, out actor))
            {
                Debug.LogWarning($"[Quest] A conversation named '{actorId}', which is not loaded on the server.");
                return false;
            }

            if (actor.InTalkRange(talker)) return true;

            Debug.LogWarning($"[Quest] A conversation with '{actorId}' came from too far away to be believed.", actor);
            return false;
        }
    }
}
