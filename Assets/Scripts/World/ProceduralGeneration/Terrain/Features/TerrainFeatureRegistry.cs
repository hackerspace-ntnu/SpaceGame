using System;
using System.Collections.Generic;

/// <summary>
/// Maps a <see cref="TerrainFeatureType"/> enum value to a fresh instance of the concrete
/// <see cref="TerrainFeature"/> subclass that builds it. The <c>TerrainFeatureSpawner</c> uses this
/// to turn the inspector's feature-type dropdown into an actual feature object.
///
/// OPEN FOR EXTENSION: the foundation registers only <see cref="FlatPadFeature"/>. Each of the
/// nine feature-implementing agents adds exactly one line to <see cref="RegisterBuiltIns"/> —
///     Register(() => new SandDunesFeature());
/// — and nothing else in the spawner / editor / pipeline needs to change. Features are stateless
/// (all state lives in the <see cref="FeatureContext"/>), so a single shared instance per type is
/// fine and the factory simply news one up.
/// </summary>
public static class TerrainFeatureRegistry
{
    static readonly Dictionary<TerrainFeatureType, Func<TerrainFeature>> _factories = new();
    static bool _initialised;

    /// <summary>Registers a feature factory. Last registration for a type wins, so a feature agent
    /// can override the stub if their type clashes during development.</summary>
    public static void Register(Func<TerrainFeature> factory)
    {
        if (factory == null) return;
        var probe = factory();
        if (probe == null) return;
        _factories[probe.FeatureType] = factory;
    }

    /// <summary>Creates a fresh feature instance for the given type, or null if none is registered
    /// yet (e.g. one of the nine features not implemented). Callers should null-check and surface
    /// a clear "feature not implemented" message.</summary>
    public static TerrainFeature Create(TerrainFeatureType type)
    {
        EnsureInitialised();
        return _factories.TryGetValue(type, out var factory) ? factory() : null;
    }

    /// <summary>True if a concrete feature has been registered for this type.</summary>
    public static bool IsImplemented(TerrainFeatureType type)
    {
        EnsureInitialised();
        return _factories.ContainsKey(type);
    }

    /// <summary>All currently-registered feature types — handy for an editor "implemented?" list.</summary>
    public static IEnumerable<TerrainFeatureType> RegisteredTypes
    {
        get { EnsureInitialised(); return _factories.Keys; }
    }

    static void EnsureInitialised()
    {
        if (_initialised) return;
        _initialised = true;
        RegisterBuiltIns();
    }

    /// <summary>
    /// Central registration point. Feature agents append their one-liner here.
    /// </summary>
    static void RegisterBuiltIns()
    {
        // --- Reference stub shipped with the foundation -------------------------
        Register(() => new FlatPadFeature());

        // --- The nine features (plus Cliff) ------------------------------------
        Register(() => new SandDunesFeature());
        Register(() => new MesaFeature());
        Register(() => new ButteFeature());
        Register(() => new CliffFeature());
        Register(() => new CanyonFeature());
        Register(() => new CanyonPathFeature());
        Register(() => new RidgeFeature());
        Register(() => new NaturalBridgeFeature());
        Register(() => new StoneArchFeature());
        Register(() => new CaveEntranceFeature());

        // --- Large composite area features -------------------------------------
        Register(() => new ArchingCaveFeature());
        Register(() => new BadlandsMazeFeature());

        // --- Scatter area features ---------------------------------------------
        Register(() => new BouldersFeature());
    }
}
