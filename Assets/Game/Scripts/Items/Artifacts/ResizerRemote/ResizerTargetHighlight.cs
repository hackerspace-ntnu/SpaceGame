// Lighting up the body the resizer remote is pointed at.
//
// ENTIRELY LOCAL, ENTIRELY COSMETIC, AND ONLY ON THE HOLDER'S MACHINE. Nothing here is sent and
// nothing here is saved, for ShipPartHighlighter's reason: the rim answers *your* question — "is
// this handset going to reach that person" — and it is not a property of the person. Two players
// aiming two remotes at the same body would otherwise fight over one outline, and a rim driven off
// a message would need the target on the wire for a readout that is already true locally.
//
// It is also the only honest machine to draw it on. Aim is only real on the owner: a peer's copy of
// a player has an AimProvider with no live camera behind it, so nobody else can work out what this
// handset is pointed at without being told.
//
// WHAT IT IS NOT. It is not the target's warning that they are being aimed at. That is the
// handset's own business and travels for free on the hold stream — the whip extends, the lamp
// lights, the beam shows, on every machine, in Present/PresentHold. A victim reads the DEVICE, not
// their own silhouette.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// Traces the body under the crosshair in the colour of whichever way the remote is set.
    ///
    /// <para>
    /// The colour repeats what the handset's own dial and lamp already say, which is the point of
    /// it: the player is looking at the target, not at the thing in their hand, and a setting they
    /// have to look down to read is one they will get wrong (<c>GDC-L1-UX-0003</c>). It is a
    /// repeat rather than the only channel — the dial's pointer angle carries the same fact as a
    /// position, for a player who cannot separate the two colours.
    /// </para>
    /// <para>
    /// <b>The shell is rebuilt only when the target changes, never per frame.</b>
    /// <see cref="OutlineShell.Build"/> destroys and re-creates one throwaway renderer per renderer
    /// it traces, which is nothing on a socket with two meshes and a great deal of garbage every
    /// frame on a player character with twenty. <see cref="Refresh"/> is safe to call every frame
    /// and does nothing on the frames where the answer has not moved.
    /// </para>
    /// </summary>
    public sealed class ResizerTargetHighlight
    {
        /// <summary>Exactly the shell parts this highlighter made, so clearing is exact.</summary>
        private readonly List<GameObject> shell = new();

        private readonly Color growColour;
        private readonly Color shrinkColour;

        /// <summary>
        /// How thick to draw the rim, as a share of how far away the body is. A share rather than a
        /// width, so a rim reads at the same thickness on a target across a canyon as on one at
        /// arm's length — the shader inflates in world space, so a fixed width is a bold outline at
        /// three metres and a sub-pixel hairline at twenty-five.
        /// </summary>
        private readonly float widthPerMetre;
        private readonly float minWidth;
        private readonly float maxWidth;

        /// <summary>
        /// One material, retinted rather than two swapped. Created on the first rim rather than in
        /// a constructor: this object is made when the item is equipped, and an item equipped and
        /// never fired should not leave a material behind.
        /// </summary>
        private Material rim;

        /// <summary>What the shell currently traces, and at what setting and width.</summary>
        private StatusReceiver lit;
        private bool litGrowing;
        private float litWidth;

        /// <summary>The body being pointed at, or null. Read by the artifact's presentation.</summary>
        public StatusReceiver Aimed => lit;

        public ResizerTargetHighlight(Color growColour, Color shrinkColour,
                                      float widthPerMetre, float minWidth, float maxWidth)
        {
            this.growColour = growColour;
            this.shrinkColour = shrinkColour;
            this.widthPerMetre = widthPerMetre;
            this.minWidth = minWidth;
            this.maxWidth = maxWidth;
        }

        /// <summary>
        /// Light <paramref name="body"/> at <paramref name="distance"/> metres, in the colour for
        /// <paramref name="growing"/>. A null body clears.
        ///
        /// <para>
        /// The width is quantised before it is compared, so a target standing still does not
        /// rebuild its own shell every frame on the strength of a millimetre of camera bob.
        /// </para>
        /// </summary>
        public void Refresh(StatusReceiver body, float distance, bool growing)
        {
            if (body == null) { Clear(); return; }

            float width = Mathf.Clamp(distance * widthPerMetre, minWidth, maxWidth);
            width = Mathf.Round(width * 500f) / 500f;      // 2 mm steps

            if (body == lit && growing == litGrowing && Mathf.Approximately(width, litWidth)) return;

            rim ??= TintMaterials.Rim("ResizerTargetRim", growColour, width);
            TintMaterials.SetOutline(rim, growing ? growColour : shrinkColour);

            OutlineShell.BuildAtWidth(body.gameObject, rim, width, shell);

            lit = body;
            litGrowing = growing;
            litWidth = width;
        }

        /// <summary>
        /// Put the body this highlighter lit back the way it found it.
        ///
        /// Safe to call twice and safe on a body that has since been destroyed — which is the
        /// ordinary case, a highlighted creature being a creature somebody may well have shot.
        /// </summary>
        public void Clear()
        {
            OutlineShell.Clear(shell);
            lit = null;
        }

        /// <summary>
        /// Clear the rim and destroy the material behind it. Called when the item leaves the hand:
        /// the material is a runtime instance with <c>HideFlags.HideAndDontSave</c>, so nothing
        /// else will ever collect it.
        /// </summary>
        public void Dispose()
        {
            Clear();

            if (rim != null) Object.Destroy(rim);
            rim = null;
        }
    }
}
