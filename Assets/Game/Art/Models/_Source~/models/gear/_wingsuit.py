"""Shared wing-membrane geometry for the wingsuit family.

Extracted from `wingsuit.py` when `wingsuit_worn.py` needed the same cloth at a
different size and droop. The maths is not obvious and it is not cheap to
re-derive, so it lives in one place rather than being pasted twice with two sets
of constants drifting apart — same reason `_gauntlet.py` and `_ornithopter.py`
exist.

Everything here is parameterised by a `Wing`. Nothing is a module constant,
because the two callers disagree about every single number.

## The frame each membrane is built in — load-bearing

    +X   outboard along the arm, shoulder (0) to the outboard end
    −Y   the chord: the leading edge is at y = 0 and PINNED, the trailing edge
         is at y = −chord and free
    ±Z   the panel's own camber and thickness

The origin is the **shoulder end of the leading edge**, because that is the
point the wing is strapped by — the arm bone for the flight suit, the wearer's
shoulder for the worn one.

`−Y` being the chord is what `SpaceGame/ClothWind` needs: the shader pins a
garment by a gradient along one object-space axis, from a held plane to a free
one. `WingsuitBuilder` measures that axis off the mesh's own vertices on every
run rather than carrying it as a constant — see `wingsuit_BUILD.md` for what
happened the one time a model like this shipped measured-then-stale numbers.
"""

import math


class Wing:
    """One membrane's shape. Distances in metres, `camber` may be negative.

    A negative `camber` bows the panel toward −Z instead of +Z. That is not a
    stylistic knob: a wing placed on the wearer's left and one on their right
    get world bases of opposite handedness, so the axis that is "aft" for one
    side is "forward" for the other, and the sign here is how a caller keeps
    both panels bowing the same way in the world without mirroring a mesh.
    """

    def __init__(self, span, root_chord, tip_chord, camber,
                 chord_falloff=1.35, skin_root=0.014, skin_tip=0.008,
                 span_stations=9, chord_points=6, bow_peak=0.8):
        self.span = span
        self.root_chord = root_chord
        self.tip_chord = tip_chord
        self.camber = camber
        self.chord_falloff = chord_falloff
        self.skin_root = skin_root
        self.skin_tip = skin_tip
        self.span_stations = span_stations
        self.chord_points = chord_points
        self.bow_peak = bow_peak

    # -- spanwise profiles, all in the fraction s: shoulder 0, outboard end 1 --

    def chord_at(self, s):
        """Wing depth at spanwise fraction `s`.

        A power curve rather than a straight taper: a linear trailing edge reads
        as a triangle of cloth, and what a wingsuit actually has is a deep panel
        at the body that falls away quickly and then runs out thin along the
        forearm.
        """
        return ((self.root_chord - self.tip_chord) * (1.0 - s) ** self.chord_falloff
                + self.tip_chord)

    def camber_at(self, s):
        """How far the panel bows at spanwise fraction `s`.

        Zero at both ends — the shoulder end is pulled flat against the body and
        the outboard end is pinched onto the cuff — and deepest just inboard of
        half span, which is where the air actually pools.
        """
        return self.camber * math.sin(math.pi * s) ** 0.75

    def skin_at(self, s):
        return self.skin_root + (self.skin_tip - self.skin_root) * s

    def section(self, s):
        """One chordwise profile of the panel, as a closed loop in (y, z).

        Returned in `Part.loft`'s (u, v) convention for axis 'X', so u is
        chordwise (−Y, aft to the free trailing edge) and v is the panel's own
        thickness.

        The loop runs the upper surface leading-to-trailing and the lower surface
        back again, so both surfaces share their leading and trailing vertices
        and the panel closes without a seam down its edges.
        """
        chord = self.chord_at(s)
        camber = self.camber_at(s)
        half = self.skin_at(s) * 0.5

        upper, lower = [], []
        for i in range(self.chord_points):
            t = i / (self.chord_points - 1.0)
            y = -chord * t

            # The bow itself: zero at the pinned leading edge, zero again at the
            # free trailing edge, deepest around a third of the way back — which
            # is where a stretched panel's slack actually collects.
            bow = camber * math.sin(math.pi * t ** self.bow_peak)

            # Pinch the thickness out toward the trailing edge so the panel ends
            # in a hem rather than in a slab edge.
            thick = half * (1.0 - 0.65 * t)

            upper.append((y, bow + thick))
            lower.append((y, bow - thick))

        # Drop the shared first and last points from the return leg so the loop
        # has no doubled vertices for remove_doubles to weld into a non-manifold
        # pinch.
        return upper + list(reversed(lower[1:-1]))

    def sections(self, span_sign=1):
        """Every lofted station, ready for `Part.loft(..., axis='X')`."""
        out = []
        for i in range(self.span_stations):
            s = i / (self.span_stations - 1.0)
            out.append((span_sign * self.span * s, self.section(s)))
        return out
