// ============================================================
// ENTITY SYSTEM SETUP GUIDE
// ============================================================
// Open this file to understand how to wire up the entity system.
// All wiring is done by dragging one profile script onto a prefab.
//
// ── FIRST-TIME SETUP ──────────────────────────────────────
//
// 1. PLAYER
//    - Open Assets/Prefabs/Player/PlayerCharacterNetworked.prefab
//    - Add component: EntityFaction     (assign PlayerFaction asset + GlobalRelationships)
//    - Add component: NoiseEmitter      (set receiverLayers to include enemy layer)
//    (EntityFaction self-registers the player in EntityTargetRegistry — all AI targeting
//     is faction-based. No RegisterAsTarget / targetTag needed anywhere.)
//
// 2. FACTIONS (create these ScriptableObjects once)
//    - Right-click in Project → Factions → Faction Definition
//      Create: RobotsFaction, PlayerFaction, NPCFaction, WildlifeFaction
//    - Right-click → Factions → Faction Relationship Table
//      Create: GlobalRelationships
//        Add: RobotsFaction ↔ PlayerFaction  = Hostile
//        Add: RobotsFaction ↔ NPCFaction     = Hostile
//        Add: PlayerFaction ↔ NPCFaction     = Allied
//        Add: WildlifeFaction ↔ everything   = Neutral (default)
//
// ── ROBOT BAND ────────────────────────────────────────────
//
// 3. Phil (charger):
//    - Open Assets/Prefabs/entities/Robots/Phil.prefab
//    - Add component: EntityProfile_RobotPhil
//    - Set AgentController.nearbyAgentScanRadius = 12
//    - Set AgentController.nearbyAgentLayer = [entity layer]
//    - Set EntityFaction.faction = RobotsFaction
//    - Set EntityFaction.relationshipTable = GlobalRelationships
//    - AlertBroadcaster.receiverLayers = [entity layer]
//    - NoiseEmitter.receiverLayers = [entity layer]
//
// 4. Roberto (scout):  Same steps → EntityProfile_RobotRoberto
// 5. Cath (cover):     Same steps → EntityProfile_RobotCath
//    - Assign weapon/fireProfile/aimProfile SO refs on AgentRangedCombatModule
// 6. Ernst (heavy):    Same steps → EntityProfile_RobotErnst
//
// ── NPC ───────────────────────────────────────────────────
//
// 7. Open Assets/Prefabs/entities/NPC/NPC.prefab
//    - Add component: EntityProfile_NPC
//    - Set EntityFaction.faction = NPCFaction
//    - Set EntityFaction.relationshipTable = GlobalRelationships
//    (NpcBrain remains on the prefab — AgentController uses it as a legacy fallback
//     if no module claims the frame. EntityProfile_NPC adds FleeModule + WanderModule
//     which override NpcBrain at runtime when relevant.)
//
// ── GENERIC ENEMY ─────────────────────────────────────────
//
// 8. Open Assets/Prefabs/entities/hostiles/enemy.prefab
//    - Add component: EntityProfile_HostileRobot (or copy one of the robot profiles)
//    - Wire faction + layers same as robots
//
// ── DESERT RAT ────────────────────────────────────────────
//
// 9. Create a rat prefab (or use any small humanoid mesh)
//    - Add: AgentController, NavMeshAgentMotor, WanderBehaviour, Animator, AgentAnimatorDriver
//    - Add component: EntityProfile_DesertRat
//    - No faction needed (wildlife is neutral by default)
//
// ── MOUNTABLE ANT ─────────────────────────────────────────
//
// 10. Open Assets/Prefabs/entities/MountableAnt.prefab
//     - Add component: EntityProfile_MountableAnt
//     - Modular path: just MountModule + SteerModule (plus AgentController + NavMeshAgentMotor + AI modules
//       if you want AI behaviour between rider inputs).
//     - Toggle MountModule.allowAISelfMovementWhenMounted to let the mount keep running its AI
//       between rider inputs (or false to freeze it until the rider steers).
//     - MountModule implements IInteractable — no separate MountInteractor needed.
//
// ── BOUNTY HUNTER ─────────────────────────────────────────
//
// 11. Duplicate any humanoid robot prefab → rename BountyHunter
//     - Add component: EntityProfile_BountyHunter
//     - Assign weapon/fireProfile/aimProfile SO refs on AgentRangedCombatModule
//     - Assign AgentRangedCombatModule.muzzleSocket to the gun barrel bone
//     - Place 2-3 hunters near each other for a hunting party
//
// ── COVER POINTS ──────────────────────────────────────────
//
// 12. Place CoverPoint components on empty GameObjects in the level
//     behind rocks, crates, walls. CoverModule on Cath/BountyHunter
//     will automatically find and use them.
//
// ── NAVMESH ───────────────────────────────────────────────
//
// 13. Bake a NavMesh for the level (Window → AI → Navigation → Bake).
//     All entities require a NavMesh to navigate.
//
// ── LAYER SETUP ───────────────────────────────────────────
//
// Recommended layers:
//   Entity  (layer 8) — all AI entities
//   Player  (layer 9) — player character
//
// Set AlertBroadcaster.receiverLayers, NoiseEmitter.receiverLayers,
// and AgentController.nearbyAgentLayer to "Entity" (or whatever layer you use).
//
// ============================================================
// This file contains no runtime code — it is documentation only.
// ============================================================
public static class EntitySystemSetup { }
