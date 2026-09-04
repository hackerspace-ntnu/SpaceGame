using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Everything the visor needs to describe something the crosshair is on, for the things that
    /// are not <see cref="IInteractable"/>.
    /// </summary>
    public readonly struct CrosshairReadout
    {
        /// <summary>The words, resolved by whoever owns the thing being described.</summary>
        public readonly InteractionDisplay Display;

        /// <summary>
        /// What to frame — the renderers under this transform are what the bracket snaps around.
        /// Null marks <see cref="Point"/> instead, which is what a spot on a flat surface is.
        /// </summary>
        public readonly Transform Subject;

        /// <summary>Where the look ray meets it, in world space.</summary>
        public readonly Vector3 Point;

        /// <summary>
        /// What the bracket re-snaps on. Two readouts with the same key are the same target, so a
        /// crosshair sliding across one wall does not re-acquire on every cell.
        /// </summary>
        public readonly Object Key;

        public CrosshairReadout(InteractionDisplay display, Transform subject, Vector3 point, Object key)
        {
            Display = display;
            Subject = subject;
            Point = point;
            Key = key;
        }
    }

    /// <summary>
    /// A thing the crosshair points at that cannot be an <see cref="IInteractable"/>, but still has
    /// to be named on screen.
    ///
    /// <para>
    /// The ship's inventory wall is the case this exists for. Its verb changes per cell — stow what
    /// is in your hand here, take what is lying there — so it cannot answer one <c>Interact</c>, and
    /// <c>WallAimController</c> casts the <see cref="Interactor.LookRay"/> itself instead. That left
    /// the wall the one surface in the game the visor could say nothing about: the player stood in
    /// front of a rack of gear and got a bare crosshair.
    /// </para>
    /// <para>
    /// Implemented by components on the PLAYER, not on the thing being described — the aim is the
    /// player's and so is the answer. The visor asks every one of them on its own body, in component
    /// order, and takes the first that answers; <see cref="Interactor.HoveredInteractable"/> is
    /// asked first and always wins, so a readout can never talk over a real interactable.
    /// </para>
    /// </summary>
    public interface ICrosshairReadout
    {
        /// <summary>
        /// What this is looking at right now, or false for nothing. Called once a frame from the
        /// HUD, so it must only report state the implementer has already resolved — never cast.
        /// </summary>
        bool TryReadCrosshair(out CrosshairReadout readout);
    }
}
