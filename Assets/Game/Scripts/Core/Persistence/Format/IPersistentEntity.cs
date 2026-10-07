namespace SpaceGame.Persistence
{
    /// <summary>
    /// Marks a component whose GameObject is part of the mutable world, and must therefore survive a
    /// save.
    ///
    /// <b>Why this exists.</b> <c>SaveablePolicy</c> used to decide by looking for components that
    /// imply movement — a <c>HealthComponent</c>, a <c>NavMeshAgent</c>, a non-kinematic
    /// <c>Rigidbody</c>. Every one of those missed the things this game is actually made of. A legged
    /// machine is a KINEMATIC Rigidbody driven by <c>LeggedLocomotion</c> — the body is a collider,
    /// not a motor — and the DuneFoil has no Rigidbody on its root at all. So the Ostrich a player
    /// rides was not restored badly; it was never captured, and neither was any mount or vehicle.
    ///
    /// <b>Why an interface rather than a longer list of type names.</b> The transient blacklist in
    /// <c>SaveablePolicy</c> matches <c>GetType().Name</c>, because that file cannot reference the
    /// item and weapon assemblies. Name matching cannot see base classes: <c>OstrichLocomotion</c>
    /// does not match <c>"LeggedLocomotion"</c>, so a name list needs every subclass spelled out and
    /// goes silently wrong the first time one is renamed — and renames have already cost this project
    /// data. An interface is inherited by every subclass and checked by the compiler.
    ///
    /// <b>Why it lives here.</b> <c>SpaceGame.Persistence</c> declares no references, so implementing
    /// this couples the implementor to nothing. The locomotion and vehicle assemblies can reference
    /// it without acquiring a dependency on the game.
    ///
    /// Implemented by <c>AgentController</c>, <c>MountModule</c>, <c>SceneTracked</c>,
    /// <c>LeggedLocomotion</c> and <c>DuneFoilLocomotion</c>. Implement it on any new locomotion base
    /// or vehicle root that has none of those.
    ///
    /// Deliberately empty. It answers one question — "is this object part of the world?" — and a
    /// member would invite callers to ask it something else.
    /// </summary>
    public interface IPersistentEntity
    {
    }
}
