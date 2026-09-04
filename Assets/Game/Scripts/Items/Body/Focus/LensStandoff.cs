using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How far in front of its subject a focus lens may sit when something solid is in the way.
    ///
    /// <para>
    /// Pure, so the arithmetic is testable without a physics scene. This project's EditMode tests
    /// cannot raise <c>Awake</c>, so a decision left inside the camera would be a decision nobody
    /// can pin — the same split <see cref="BodySiteState"/> makes for what a site shows.
    /// </para>
    /// </summary>
    public static class LensStandoff
    {
        /// <param name="wanted">The authored distance.</param>
        /// <param name="hit">Distance along the shot to the nearest blocker's SURFACE, or
        /// <see cref="float.PositiveInfinity"/> for none. Infinity needs no special case: it
        /// survives the subtraction and loses the <see cref="Mathf.Min"/>, so "no blocker" comes
        /// out as exactly <paramref name="wanted"/>.</param>
        /// <param name="radius">Clearance kept from the blocker — the lens stops this far short of
        /// it. It is the wall probe's radius, so a lens placed here is a whole probe-sphere clear
        /// of everything the probe swept past.</param>
        /// <param name="floor">Nearest the lens is ever allowed; the crop gets tight rather than
        /// the lens going through the body. Applied outermost, so it holds even against a
        /// <paramref name="wanted"/> authored nearer than it.</param>
        public static float Resolve(float wanted, float hit, float radius, float floor) =>
            Mathf.Max(floor, Mathf.Min(wanted, hit - radius));
    }
}
