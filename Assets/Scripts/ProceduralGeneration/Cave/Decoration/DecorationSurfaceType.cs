/// <summary>
/// Which subset of mesh triangles a <see cref="DecorationRule"/> may spawn on. Filtering happens
/// in <see cref="DecorationScatterer"/> via the triangle's *inward-facing* surface normal.
///
///   Floor    — surface points up        (inward normal.y >  +floorCeilingThreshold)
///   Ceiling  — surface points down      (inward normal.y <  -floorCeilingThreshold)
///   Wall     — surface points sideways  (|inward normal.y| <= floorCeilingThreshold)
///   Any      — no filter
///
/// "Inward" means "pointing into the air volume". A floor's inward normal points up (toward the
/// cave's air); a ceiling's inward normal points down. This is what the cave mesh's flat-shaded
/// triangle normals already encode.
/// </summary>
public enum DecorationSurfaceType
{
    Any,
    Floor,
    Ceiling,
    Wall,
}
