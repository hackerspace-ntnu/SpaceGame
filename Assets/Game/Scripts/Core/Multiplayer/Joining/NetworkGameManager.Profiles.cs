// Which save profile each client is playing, learned before it has a body — so the world can be
// streamed around where that player LEFT OFF rather than around the spawn point.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Core
{
    public partial class NetworkGameManager
    {
        [Tooltip("How long the server waits for a joining client to report which save profile it is " +
                 "playing, before streaming the world around the spawn point instead of around that " +
                 "client's saved position. Only costs this long for a client whose report is lost; " +
                 "the report is normally already in by the time the world is ready to preload.")]
        [SerializeField] private float profileReportTimeout = 5f;

        /// <summary>
        /// Which save profile each connected client is playing, as reported by that client the
        /// moment it has this object.
        ///
        /// <para>
        /// The server has no other way to know before a player object exists. Connection approval —
        /// the usual carrier for a payload like this — is deliberately off in this project so the
        /// lobby and Relay flows stay as they are, so the id arrives on this scene object's own
        /// channel instead: every client has it as soon as it has persistentScene, which is well
        /// before its body is spawned. <see cref="PlayerSaveSync"/> still does the binding and still
        /// validates the claim independently; this copy only decides which chunks to stream.
        /// </para>
        /// </summary>
        private readonly Dictionary<ulong, string> profileByClient = new();

        /// <summary>
        /// A client telling the server which save profile it is playing, before it has a body.
        ///
        /// Trusted only as far as it goes: the answer picks which chunks are streamed for this
        /// client and nothing else. Actually binding the profile — and therefore handing over a
        /// saved inventory — happens in <see cref="PlayerSaveSync"/>, which checks the claim against
        /// the live bindings on its own.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportProfileServerRpc(FixedString64Bytes profileId, RpcParams rpcParams = default)
        {
            string profile = profileId.ToString();
            if (!PlayerSaveSync.IsWellFormed(profile)) return;

            profileByClient[rpcParams.Receive.SenderClientId] = profile;
        }

        /// <summary>
        /// Waits until <paramref name="clientId"/> has told us its profile, or we give up.
        ///
        /// Bounded, and the bound is the point: a client that never reports — an old build, a lost
        /// packet, a peer that dropped between connecting and loading the scene — must not hold its
        /// own spawn open forever. Giving up simply means the world is streamed around the spawn
        /// point, which is where it went before any of this existed.
        /// </summary>
        private IEnumerator WaitForProfile(ulong clientId)
        {
            if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
                yield break;                        // the host reads its own id locally

            float deadline = Time.time + Mathf.Max(0f, profileReportTimeout);

            while (!profileByClient.ContainsKey(clientId))
            {
                // A client that left while we waited has nothing left to wait for.
                if (NetworkManager.Singleton == null ||
                    !NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId))
                    yield break;

                if (Time.time >= deadline)
                {
                    Debug.LogWarning($"[NGM] Client {clientId} never reported a save profile, so the " +
                                     "world is being streamed around the spawn point instead of around " +
                                     "wherever they left off. If they had a saved position they will be " +
                                     "moved there after spawning, across terrain that may still be loading.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// The saved position for a client, when one is known before it spawns.
        ///
        /// <para>
        /// This answers for remote clients too, and it has to. It decides which chunks are streamed,
        /// and a client restored to the far side of the map after the world was prepared around the
        /// spawn point is teleported onto terrain that was never loaded — the exact fall the host
        /// path at the call site exists to prevent. The host was only ever the easy case because its
        /// profile is a local read; a remote client's arrives over
        /// <see cref="ReportProfileServerRpc"/> instead, which is what makes the same answer
        /// available here rather than only after <see cref="PlayerSaveSync"/> has bound the body.
        /// </para>
        /// <para>
        /// Connection approval stays off. It is the mechanism this would normally use and turning it
        /// on would put a required handshake in front of every lobby and Relay join in the project;
        /// a report on an already-replicated scene object costs nothing and breaks nothing.
        /// </para>
        /// </summary>
        private bool TryGetSavedSpawn(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            PlayerSaveService players = SaveManager.Instance?.Players;
            if (players == null) return false;

            bool isLocal = NetworkManager.Singleton != null &&
                           clientId == NetworkManager.Singleton.LocalClientId;

            string profileId = isLocal
                ? PlayerProfile.LocalId
                : profileByClient.GetValueOrDefault(clientId);

            if (string.IsNullOrEmpty(profileId)) return false;

            // A profile someone else is already playing is not this client's to be restored into.
            // PlayerSaveSync refuses the binding in that case, so honouring it here would stream the
            // world around a position this client is never going to be put at.
            if (!isLocal && players.TryGetBoundPlayer(profileId, out GameObject live) && live != null)
            {
                Debug.LogWarning($"[NGM] Client {clientId} reported profile '{profileId}', which " +
                                 $"'{live.name}' is already playing. Spawning them at the spawn point.");
                return false;
            }

            return players.TryGetSpawnPosition(profileId, out position, out rotation);
        }
    }
}
