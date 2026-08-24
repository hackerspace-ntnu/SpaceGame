"""`TrackedPart` — a `Part` that assigns materials by face identity.

## The bug this exists for

`_buildlib.Part._absorb` — the path every `torus`, `tube`, `prism` and `loft`
goes through — records `n_before = len(self.bm.faces)`, calls
`bm.from_mesh(scratch)`, and then claims `self.bm.faces[n_before:]` as the faces
it just made. That assumption is false: `from_mesh` does **not** leave the
existing faces in their old index slots, and it clobbers material indices on the
way through. Measured on Blender 5.1.1, a Part holding a 6-face cone and an
8-face cone, absorbing a 72-face torus:

    the torus's returned list overlaps the second cone by 6 faces
    final material counts {DARK: 8, STEEL: 6, BRASS: 72}

Six faces of the cone wore the torus's material and six faces of the torus were
left on index 0. The returned slice has the right *length* every time, which is
why the model builds, the counts look plausible, and the only symptom is a
handful of faces in a neighbouring part's colour.

## Why this is a separate module rather than a fix in `_buildlib`

Every component in the library imports `_buildlib`, and some were tuned by eye
against the wrong colours — correcting `Part` centrally would silently restyle
shipped models nobody asked to change. That remains a deliberate, separate job.

This module changes nothing for anyone: existing scripts keep importing `Part`
and keep their current output. New work opts in by importing `TrackedPart`.

`components/props/grapple_dart.py` carries a local copy that predates this file.
It is left alone on purpose — its `.blend` is the source of truth and must never
be regenerated, so deduplicating the class there would be edit-for-edit's sake.

## Usage

    from _tracked import TrackedPart

    p = TrackedPart(mats)
    hard = p.slab(...) + p.box(...)
    p.restamp()                       # before bevelling, not after
    p.bevel(hard, width=0.0016)
    p.finish("Mesh_Thing", coll)

`restamp()` replays every recorded `(faces, index)` pair in creation order. With
`_absorb` overridden it should be a no-op, and it prints how many faces it had
to correct precisely so a future Blender reordering surfaces as a number rather
than as a mysteriously gold nose cone.

Call it **before** `bevel()`: bevel's own new faces are deliberately not in the
log, so they keep material index 0 — which is why `MATS[0]` must always be a
structural metal.
"""

from _buildlib import Part


class TrackedPart(Part):
    """A `Part` that identifies absorbed geometry by identity, not by index."""

    def __init__(self, materials):
        super().__init__(materials)
        self._stamps = []

    def _tag(self, faces, mat):
        faces = list(faces)
        self._stamps.append((faces, mat))
        return super()._tag(faces, mat)

    def _absorb(self, bm2, mat):
        before = set(self.bm.faces)
        n_log = len(self._stamps)
        super()._absorb(bm2, mat)
        del self._stamps[n_log:]        # drop the bogus index-slice stamp
        new = [f for f in self.bm.faces if f not in before]
        return self._tag(new, mat)

    def restamp(self, label=""):
        """Replay every recorded material assignment. Returns faces corrected."""
        n = 0
        for faces, mat in self._stamps:
            for f in faces:
                if f.is_valid and f.material_index != mat:
                    f.material_index = mat
                    n += 1
        if n:
            print("  restamp%s corrected %d face(s)"
                  % (" " + label if label else "", n))
        return n
