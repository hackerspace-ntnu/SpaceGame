using System;
using Unity.Netcode;

namespace SpaceGame.Core
{
    /// <summary>
    /// Keeps a handler attached to whichever <see cref="NetworkSceneManager"/> is alive right now.
    ///
    /// <para>
    /// The scene-event counterpart of <see cref="DisconnectHook"/>, and it exists for a second
    /// reason on top of that one's. A NetworkSceneManager is not merely late — it does not exist at
    /// all until a session starts, and it is REPLACED by a new instance on every start after that.
    /// So a listener that has to hear about scene loads across a whole play session, rather than
    /// for the lifetime of one session, cannot subscribe once anywhere.
    /// </para>
    ///
    /// <para>
    /// <see cref="Poll"/> is a null test and a reference compare, cheap enough to call every frame.
    /// </para>
    /// </summary>
    public sealed class SceneEventHook
    {
        private readonly NetworkSceneManager.SceneEventDelegate handler;

        /// <summary>The scene manager currently carrying <see cref="handler"/>, or null.</summary>
        private NetworkSceneManager attached;

        public SceneEventHook(NetworkSceneManager.SceneEventDelegate handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>Whether the handler is on a live scene manager. For logging and tests.</summary>
        public bool IsAttached => attached != null;

        /// <summary>
        /// Attaches to the live scene manager, or moves to a new one. Safe to call every frame, and
        /// safe with no session at all — the menu, a unit test and a torn-down session all look
        /// like that, and none of them is an error.
        /// </summary>
        public void Poll()
        {
            NetworkManager manager = NetworkManager.Singleton;

            // A plain C# object, unlike the NetworkManager: there is no Unity fake-null to lean on
            // here, so the manager is asked first and its SceneManager read only while it is alive.
            NetworkSceneManager scenes = manager != null ? manager.SceneManager : null;

            if (scenes == attached) return;

            Detach();
            if (scenes == null) return;

            scenes.OnSceneEvent += handler;
            attached = scenes;
        }

        /// <summary>Removes the handler. Pair with OnDestroy; safe to call twice.</summary>
        public void Detach()
        {
            if (attached != null) attached.OnSceneEvent -= handler;
            attached = null;
        }
    }
}
