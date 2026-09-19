namespace SpaceGame.Agents
{
    // ============================================================
    // ENTITY SYSTEM SETUP — see the docs, not this file
    // ============================================================
    //
    // This was a hand-written wiring guide. Every concrete instruction in it had gone stale: it
    // named six EntityProfile_* variants that were deleted, prefabs under Assets/Game/Prefabs/
    // entities/ that do not exist, a WanderBehaviour that is called WanderModule, NpcBrain as if
    // it were current, and AlertBroadcaster.receiverLayers / NoiseEmitter.receiverLayers, which
    // were removed when alerting moved onto EntityTargetRegistry. It also told you to create
    // "RobotsFaction / PlayerFaction / NPCFaction" and wire "PlayerFaction <-> NPCFaction = Allied",
    // none of which is the name or the stance of anything in the project.
    //
    // A guide that is wrong in every particular is worse than no guide, because the next person
    // follows it. The instructions are deleted rather than repaired: they duplicated documentation
    // that is generated, validated and kept current, and a second copy is a second thing to drift.
    //
    //   How an agent is composed, and the full module catalogue
    //       docs/AI/systems/AgentSystem.md
    //   Step-by-step for a NEW creature, with a reference builder to copy
    //       .claude/skills/spacegame-agent/SKILL.md  (and its reference.md)
    //   Factions, stances and the relationship table
    //       docs/AI/systems/AgentSystem.md, "Factions"
    //
    // Nothing references this class. It is kept only so the file's GUID stays valid; put no
    // instructions back in it.
    // ============================================================
    public static class EntitySystemSetup { }
}
