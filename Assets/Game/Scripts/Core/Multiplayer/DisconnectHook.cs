using System;
using Unity.Netcode;

namespace SpaceGame.Core
{
    /// <summary>
    /// Keeps a handler attached to whichever <see cref="NetworkManager"/> is alive right now.
    ///
    /// <para>
    /// Subscribing once in Awake is the obvious thing to write and it loses a race that nothing
    /// reports. The only NetworkManager in the project lives in the Bootstrap scene, and
    /// <see cref="NetworkBootstrap"/> backfills one AfterSceneLoad whenever a scene is entered
    /// directly in the editor — so anything created before that backfill sees a null Singleton,
    /// skips its subscription and never gets another chance. <see cref="LobbySession"/> was written
    /// that way: it creates itself on first use, and if that happened to be before the manager
    /// existed, the entire disconnect path went missing with no error anywhere. Nobody would find
    /// that by reading the code, because the code looks like it subscribed.
    /// </para>
    ///
    /// <para>
    /// So the subscription is re-checked rather than assumed. <see cref="Poll"/> is a null test and
    /// a reference compare — cheap enough to call every frame — and it also survives the manager
    /// being REPLACED, which happens on every Play session in the editor and would otherwise leave
    /// a handler attached to a destroyed object.
    /// </para>
    /// </summary>
    public sealed class DisconnectHook
    {
        private readonly Action<ulong> handler;

        /// <summary>The manager currently carrying <see cref="handler"/>, or null.</summary>
        private NetworkManager attached;

        public DisconnectHook(Action<ulong> handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>Whether the handler is on a live manager. For logging and tests.</summary>
        public bool IsAttached => attached != null;

        /// <summary>
        /// Attaches to the live manager, or moves to a new one. Safe to call every frame, and safe
        /// with no NetworkManager at all — a scene opened straight from the editor, a unit test and
        /// a torn-down session all look like that, and none of them is an error.
        /// </summary>
        public void Poll()
        {
            NetworkManager manager = NetworkManager.Singleton;

            // Unity's == is what makes this correct across a Play-session boundary: a DESTROYED
            // manager compares equal to null, so a stale reference is dropped on the next new
            // manager rather than unsubscribed from a corpse.
            if (manager == attached) return;

            Detach();
            if (manager == null) return;

            manager.OnClientDisconnectCallback += handler;
            attached = manager;
        }

        /// <summary>Removes the handler. Pair with OnDestroy; safe to call twice, and safe to call
        /// after the manager it was attached to has been destroyed.</summary>
        public void Detach()
        {
            if (attached != null) attached.OnClientDisconnectCallback -= handler;
            attached = null;
        }
    }
}
