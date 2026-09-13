using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// How a menu control goes quiet while something is in flight.
    ///
    /// <para>
    /// Always through a <see cref="CanvasGroup"/>, never through a Button's own Disabled state.
    /// The menu button's animator has an EMPTY Disabled clip, so disabling an entry writes no
    /// curves and freezes it in whatever colour and scale the previous state left — a row the
    /// pointer was over stays white and scaled up indefinitely, and with raycasts off it never
    /// gets the pointer-exit that would take it back down. <c>interactable = false</c> is the
    /// input lock and nothing visual whatsoever; the alpha is the only thing that reads.
    /// </para>
    ///
    /// <para>
    /// A child group cannot undo a parent's alpha — they multiply — so "dim everything, keep one
    /// lit" has to be per-control groups rather than one group over the container. That is why
    /// <paramref name="dim"/> is a separate decision from <paramref name="locked"/>.
    /// </para>
    /// </summary>
    public static class MenuLock
    {
        /// <summary>What a locked control fades to. Present, plainly not available.</summary>
        public const float DimmedAlpha = 0.35f;

        public static void Set(CanvasGroup group, bool locked, bool dim)
        {
            if (group == null) return;

            group.interactable = !locked;
            group.blocksRaycasts = !locked;
            group.alpha = dim ? DimmedAlpha : 1f;
        }

        /// <summary>Locks and dims together — the ordinary case.</summary>
        public static void Set(CanvasGroup group, bool locked) => Set(group, locked, locked);
    }
}
