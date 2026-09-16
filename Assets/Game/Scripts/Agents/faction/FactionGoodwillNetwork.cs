// Tells one player how the tribes feel about them, and nobody else.
//
// The goodwill ledger is server state — it decides who targets whom, so two machines keeping their
// own copy is two machines disagreeing about who is at war. But a player's own standing is
// something their client has to be able to draw (Phase 7's visor readout), so it has to cross.
//
// **Aimed at one client, which is why this is an Rpc rather than a NetMsg.** The plan asked for
// `NetMsg.FactionGoodwill`, but the NetMessaging channel has no unicast: NetTo is Server, All or
// Others, so the only way to send one player their own bands over it is to broadcast everybody's
// bands to everybody and filter on receipt. That is bandwidth spent to leak information the design
// explicitly says each client should not have (§5: "clients each receive their own bands, not
// everyone's"). The multiplayer skill's own decision tree routes "an answer for ONE player" to a
// NetworkBehaviour with an [Rpc], which is what this is — the same shape NetworkedTeleport uses to
// place one player's body.
//
// Lives on the player prefab, so "the affected player's client" is just this object's owner.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Agents
{
    public class FactionGoodwillNetwork : NetworkBehaviour
    {
        private FactionGoodwillLedger Ledger => FactionGoodwillLedger.Instance;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Replay what this player already stands at. A band change is an EVENT, and events
                // do not replay — a player joining a world where they had already made themselves
                // unwelcome would otherwise be told nothing and draw themselves as everybody's
                // friend until they next annoyed somebody.
                SendAllBands();

                if (Ledger != null) Ledger.BandChanged += OnBandChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && Ledger != null) Ledger.BandChanged -= OnBandChanged;

            // A client that has left must not carry its old bands into the next session it joins.
            if (IsOwner && !IsServer) Ledger?.ClearMirror();
        }

        /// <summary>
        /// Server side. Only this player's own rows are sent, and only to them.
        ///
        /// The ledger raises this for every player, so the profile filter is what makes the
        /// "each client receives their own bands" rule true rather than merely intended.
        /// </summary>
        private void OnBandChanged(FactionDefinition faction, string profileId,
                                   GoodwillBand previous, GoodwillBand next)
        {
            if (profileId == null || profileId != ProfileId) return;

            Send(faction, next);
        }

        private void SendAllBands()
        {
            if (Ledger == null || string.IsNullOrEmpty(ProfileId)) return;

            foreach (FactionDefinition tribe in Ledger.Tribes())
                Send(tribe, Ledger.BandFor(tribe, ProfileId));
        }

        private void Send(FactionDefinition faction, GoodwillBand band)
        {
            if (Ledger == null) return;

            int index = Ledger.IndexOf(faction);
            if (index < 0) return;

            // The host owns its own player object and has the rows already, so sending to itself
            // would put a mirror entry beside the truth — two answers to one question.
            if (IsOwner) return;

            BandRpc(index, (int)band, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// Owner side. Writes the read-only mirror the ledger answers from on a client.
        /// </summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void BandRpc(int tribeIndex, int band, RpcParams rpcParams)
        {
            FactionDefinition faction = Ledger?.TribeAt(tribeIndex);

            // An index this build does not have: an older client against a newer server, after a
            // tribe was added. Ignored rather than guessed at — the alternative is drawing the wrong
            // tribe's standing, which is worse than drawing none.
            if (faction == null) return;

            Ledger.SetMirroredBand(faction, (GoodwillBand)band);
        }

        /// <summary>
        /// The profile this body speaks for. Resolved live rather than cached: a player binds after
        /// its NetworkObject spawns, so a value read in OnNetworkSpawn would usually be empty.
        /// </summary>
        private string ProfileId
        {
            get
            {
                PlayerSaveService players = SaveManager.Instance?.Players;
                return players != null && players.TryGetProfileFor(gameObject, out string id)
                    ? id
                    : null;
            }
        }
    }
}
