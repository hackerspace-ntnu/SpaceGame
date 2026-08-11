using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// THE PLAN — pure-data output of the BadlandsMaze "careful planning" pipeline.
/// ====================================================================================
///
/// The feature is built in stages, exactly as the project lead mandated for ArchingCave: plan the
/// TOP-ORDER structure first (the channel network that erodes the maze), then place the right
/// things in the right places (mesas, overhangs, boulders), then realise it as geometry. This file
/// holds the data each planning stage produces — it carries NO behaviour.
///
/// The mental model is INVERTED from ArchingCave. ArchingCave plans walkable space and FILLS the
/// gaps with rock. BadlandsMaze plans a solid rock massif and CARVES walkable channels out of it —
/// the chambers and channels here are SUBTRACTED from the rock, and the leftover rock between them
/// is the maze of mesas the player threads.
///
///   • <see cref="BadlandsMazeChamber"/> — a graph node: an open pool / junction carved into the
///                                          rock. Continuous radius — non-uniform by design.
///   • <see cref="BadlandsMazeChannel"/> — a walkable channel connecting two chambers. The "river"
///                                          bed; meanders via mid control points.
///   • <see cref="BadlandsMazeMesa"/>    — a placed rock mesa between the channels (Stage 2),
///                                          carrying its own height and overhang shaping.
///   • <see cref="BadlandsMazeBoulder"/> — a placed boulder / small rock on the channel floor.
///   • <see cref="BadlandsMazePlan"/>    — the whole bundle, handed to the SDF builder.
///
/// Stage 1 (<see cref="BadlandsMazePlanner"/>) fills Chambers + Channels.
/// Stage 2 (<see cref="BadlandsMazePlacer"/>) fills Mesas + Boulders.
/// Stage 3 (<see cref="BadlandsMazeSdf"/> / <see cref="BadlandsMazeChunker"/>) turns it into meshes.
/// </summary>
public sealed class BadlandsMazePlan
{
    /// <summary>Graph nodes — open chambers / pools carved into the rock massif.</summary>
    public readonly List<BadlandsMazeChamber> Chambers = new List<BadlandsMazeChamber>();

    /// <summary>Graph edges — walkable channels connecting chambers (guaranteed connected).</summary>
    public readonly List<BadlandsMazeChannel> Channels = new List<BadlandsMazeChannel>();

    /// <summary>Placed rock mesas between the channels — the maze walls (Stage 2).</summary>
    public readonly List<BadlandsMazeMesa> Mesas = new List<BadlandsMazeMesa>();

    /// <summary>Placed boulders / small rocks scattered on the channel floors (Stage 2).</summary>
    public readonly List<BadlandsMazeBoulder> Boulders = new List<BadlandsMazeBoulder>();

    /// <summary>Local-Y the walkable channel floor sits at — the surface the player walks. The
    /// surrounding rock massif rises from here; the channels are carved down to it.</summary>
    public float FloorY;

    /// <summary>Local-Y the top of the surrounding desert terrain sits at (the massif rim level
    /// before height variation). Channels are cut <c>channelDepth</c> below this.</summary>
    public float RimY;
}

/// <summary>
/// A planned chamber — one node of the channel graph: an open pool or junction carved into the
/// rock. Continuous radius, non-uniform across the maze by design. The player can stand in the
/// open here and look up at the surrounding mesa walls.
/// </summary>
public struct BadlandsMazeChamber
{
    /// <summary>Index of this chamber in <see cref="BadlandsMazePlan.Chambers"/>.</summary>
    public int Id;

    /// <summary>Chamber centre in feature-local XZ (Y is the floor height).</summary>
    public Vector2 Center;

    /// <summary>Continuous chamber radius in metres. Non-uniform across the maze by design.</summary>
    public float Radius;
}

/// <summary>
/// A walkable channel connecting two chambers — the carved "river bed" the player threads. It
/// meanders through a mid control point so it snakes rather than cutting dead straight, and its
/// width fluctuates along its length so it pinches and flares like a real eroded wash.
/// </summary>
public struct BadlandsMazeChannel
{
    /// <summary>Chamber id at each end of the channel.</summary>
    public int FromChamber, ToChamber;

    /// <summary>Feature-local XZ mid control point — the channel is a quadratic Bezier through
    /// this, giving it a smooth meander instead of a straight segment.</summary>
    public Vector2 Mid;

    /// <summary>Half-width of the channel at its midpoint, in metres. The SDF fluctuates the actual
    /// half-width along the length around this value.</summary>
    public float HalfWidth;
}

/// <summary>
/// A placed rock mesa — a lump of the surviving massif between the carved channels. It carries its
/// own footprint radius and top height (so the skyline is jagged, not a flat plateau). Its actual
/// overhanging silhouette is produced by the shared <see cref="RockBodyProfile"/> model — the EXACT
/// same rock-body shaping <see cref="MesaFeature"/> uses — so each mesa bulges and undercuts all
/// the way around as a consequence of its body shape, not a placed shelf.
/// </summary>
public struct BadlandsMazeMesa
{
    /// <summary>Mesa centre in feature-local XZ — the vertical axis of the rock body.</summary>
    public Vector2 Center;

    /// <summary>Nominal mesa radius in metres — the body's footprint reach at the base. The
    /// rock-body profile bulges OUT past this higher up; that overhang is intentional.</summary>
    public float Radius;

    /// <summary>Local-Y this mesa's flat-ish summit reaches. Varies per mesa for a jagged skyline.</summary>
    public float TopY;

    /// <summary>Per-mesa salt so each mesa's rock-body noise (bulges, lobes, lean) is unique.</summary>
    public int Salt;
}

/// <summary>A placed boulder / small rock resting on the channel floor — a lumpy ellipsoid blob
/// unioned into the rock field. Pure scenery the player walks around.</summary>
public struct BadlandsMazeBoulder
{
    /// <summary>Boulder centre in feature-local space (Y is roughly floor + radius).</summary>
    public Vector3 Center;

    /// <summary>Radius of the boulder in metres.</summary>
    public float Radius;

    /// <summary>Per-axis squash (XZ vs Y) so boulders are not all perfect spheres.</summary>
    public Vector3 Squash;

    /// <summary>Random orientation seed for the boulder's lumpiness noise.</summary>
    public int Salt;
}
