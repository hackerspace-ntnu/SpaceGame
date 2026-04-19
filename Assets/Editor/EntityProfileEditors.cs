// Custom editors for all EntityProfile_* components.
// Each one draws the default inspector fields plus a Generate button.
// Clicking Generate adds all required modules to the GameObject and sets their values.
// After generating you can remove the profile component — the modules are fully configured.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// ─────────────────────────────────────────────────────────────
// Shared helpers
// ─────────────────────────────────────────────────────────────

public static class EntityProfileEditorUtils
{
    public static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (!c) c = Undo.AddComponent<T>(go);
        return c;
    }

    public static void SetFloat(Object target, string field, float value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.floatValue = value; so.ApplyModifiedProperties(); }
    }

    public static void SetInt(Object target, string field, int value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.intValue = value; so.ApplyModifiedProperties(); }
    }

    public static void SetBool(Object target, string field, bool value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.boolValue = value; so.ApplyModifiedProperties(); }
    }

    public static void SetString(Object target, string field, string value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.stringValue = value; so.ApplyModifiedProperties(); }
    }

    public static void SetObject(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.objectReferenceValue = value; so.ApplyModifiedProperties(); }
    }

    public static void SetLayerMask(Object target, string field, LayerMask value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p != null) { p.intValue = value.value; so.ApplyModifiedProperties(); }
    }

    public static void SetModuleActive(BehaviourModuleBase module, bool value)
    {
        if (module == null)
            return;

        module.enabled = value;
        SetBool(module, "active", value);
        EditorUtility.SetDirty(module);
    }

    public static void DrawGenerateButton(string label, System.Action onGenerate)
    {
        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button($"⚙  Generate {label}", GUILayout.Height(32)))
            onGenerate();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.HelpBox("Generate adds all required modules and sets their values. Safe to run multiple times.", MessageType.None);
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_RobotHerdPatrol
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_RobotHerdPatrol))]
public class EntityProfile_RobotHerdPatrolEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Robot Herd Patrol", () =>
        {
            var p = (EntityProfile_RobotHerdPatrol)target;
            var go = p.gameObject;

            var navAgent = EntityProfileEditorUtils.GetOrAdd<NavMeshAgent>(go);
            var motor = EntityProfileEditorUtils.GetOrAdd<NavMeshAgentMotor>(go);
            var controller = EntityProfileEditorUtils.GetOrAdd<AgentController>(go);
            var basePatrol = EntityProfileEditorUtils.GetOrAdd<BasePatrolModule>(go);
            var herd = EntityProfileEditorUtils.GetOrAdd<HerdModule>(go);
            var wander = go.GetComponent<WanderModule>();
            var flock = go.GetComponent<FlockingModule>();
            var chase = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var perception = EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
            var search = EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
            var alertTx = EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
            var alertRx = EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
            var noiseRx = EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
            var noise = EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
            var audio = EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
            var faction = EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
            var inventory = EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
            var loot = EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var healthReactions = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            var melee = EntityProfileEditorUtils.GetOrAdd<EntityCombatModule>(go);
            var ranged = EntityProfileEditorUtils.GetOrAdd<RangedAttackModule>(go);
            var keepDistance = EntityProfileEditorUtils.GetOrAdd<KeepDistanceModule>(go);
            var strafe = EntityProfileEditorUtils.GetOrAdd<StrafeModule>(go);

            bool useMelee = p.attackStyle == RobotHerdAttackStyle.Melee || p.attackStyle == RobotHerdAttackStyle.Mixed;
            bool useRanged = p.attackStyle == RobotHerdAttackStyle.Ranged ||
                             p.attackStyle == RobotHerdAttackStyle.KitingRanged ||
                             p.attackStyle == RobotHerdAttackStyle.Mixed;
            bool useKiting = p.attackStyle == RobotHerdAttackStyle.KitingRanged ||
                             p.attackStyle == RobotHerdAttackStyle.Mixed;

            EntityProfileEditorUtils.SetObject(motor, "agent", navAgent);
            EntityProfileEditorUtils.SetObject(controller, "motorComponent", motor);

            EntityProfileEditorUtils.SetObject(basePatrol, "baseTransform", p.baseTransform);
            EntityProfileEditorUtils.SetFloat(basePatrol, "patrolRadius", p.patrolRadius);
            EntityProfileEditorUtils.SetFloat(basePatrol, "sampleDistance", p.patrolSampleDistance);
            EntityProfileEditorUtils.SetFloat(basePatrol, "minDestinationDistance", p.patrolMinDestinationDistance);
            EntityProfileEditorUtils.SetFloat(basePatrol, "minWaitTime", p.patrolMinWait);
            EntityProfileEditorUtils.SetFloat(basePatrol, "maxWaitTime", p.patrolMaxWait);
            EntityProfileEditorUtils.SetFloat(basePatrol, "speedMultiplier", p.herdSpeed);

            EntityProfileEditorUtils.SetString(herd, "herdId", p.herdId);

            EntityProfileEditorUtils.SetObject(chase, "target", null);
            EntityProfileEditorUtils.SetObject(ranged, "target", null);
            EntityProfileEditorUtils.SetObject(melee, "target", null);
            EntityProfileEditorUtils.SetObject(keepDistance, "target", null);
            EntityProfileEditorUtils.SetObject(strafe, "target", null);

            EntityProfileEditorUtils.SetString(chase, "targetTag", p.targetTag);
            EntityProfileEditorUtils.SetString(ranged, "targetTag", p.targetTag);
            EntityProfileEditorUtils.SetString(melee, "targetTag", p.targetTag);
            EntityProfileEditorUtils.SetString(keepDistance, "targetTag", p.targetTag);
            EntityProfileEditorUtils.SetString(strafe, "targetTag", p.targetTag);

            EntityProfileEditorUtils.SetFloat(chase, "detectRange", p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase, "loseTargetRange", p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase, "attackRange", useRanged ? p.maxFireRange : p.chaseStopRange);
            EntityProfileEditorUtils.SetFloat(chase, "chaseStopDistance", useRanged ? p.maxFireRange * 0.8f : p.chaseStopRange);
            EntityProfileEditorUtils.SetFloat(chase, "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(perception, "fieldOfViewAngle", p.fieldOfViewAngle);
            EntityProfileEditorUtils.SetFloat(perception, "memoryDuration", p.memoryDuration);
            EntityProfileEditorUtils.SetLayerMask(perception, "occlusionLayers", p.occlusionLayers);

            EntityProfileEditorUtils.SetFloat(melee, "attackRange", p.meleeRange);
            EntityProfileEditorUtils.SetFloat(melee, "attackCooldown", p.meleeCooldown);
            EntityProfileEditorUtils.SetInt(melee, "attackDamage", p.meleeDamage);

            EntityProfileEditorUtils.SetObject(ranged, "projectilePrefab", p.projectilePrefab);
            EntityProfileEditorUtils.SetObject(ranged, "muzzleTransform", p.muzzleTransform);
            EntityProfileEditorUtils.SetFloat(ranged, "minRange", p.minFireRange);
            EntityProfileEditorUtils.SetFloat(ranged, "maxRange", p.maxFireRange);
            EntityProfileEditorUtils.SetFloat(ranged, "projectileSpeed", p.projectileSpeed);
            EntityProfileEditorUtils.SetFloat(ranged, "fireCooldown", p.fireCooldown);
            EntityProfileEditorUtils.SetInt(ranged, "burstCount", p.burstCount);
            EntityProfileEditorUtils.SetFloat(ranged, "burstInterval", p.burstInterval);
            EntityProfileEditorUtils.SetFloat(ranged, "spreadAngle", p.spreadAngle);
            EntityProfileEditorUtils.SetBool(ranged, "leadTarget", p.leadTarget);

            EntityProfileEditorUtils.SetFloat(keepDistance, "detectRadius", p.keepDistanceDetect);
            EntityProfileEditorUtils.SetFloat(keepDistance, "preferredDistance", p.keepDistancePreferred);
            EntityProfileEditorUtils.SetFloat(keepDistance, "speedMultiplier", p.keepDistanceSpeed);
            EntityProfileEditorUtils.SetFloat(strafe, "engageRange", p.strafeEngageRange);
            EntityProfileEditorUtils.SetFloat(strafe, "strafeRadius", p.strafeRadius);

            EntityProfileEditorUtils.SetInt(basePatrol,   "priority", ModulePriority.Fallback);
            EntityProfileEditorUtils.SetInt(herd,         "priority", ModulePriority.Social);
            EntityProfileEditorUtils.SetInt(chase,        "priority", ModulePriority.Reactive);
            EntityProfileEditorUtils.SetInt(melee,        "priority", ModulePriority.Reactive);
            EntityProfileEditorUtils.SetInt(ranged,       "priority", ModulePriority.Reactive);
            // Strafe (21) orbits at range and yields below minStrafeDistance.
            // KeepDistance (20) backs off when too close — only activates when strafe yields.
            EntityProfileEditorUtils.SetInt(strafe,       "priority", ModulePriority.Reactive + 1);
            EntityProfileEditorUtils.SetInt(keepDistance, "priority", ModulePriority.Reactive);

            EntityProfileEditorUtils.SetFloat(alertTx, "alertRadius", p.alertRadius);
            EntityProfileEditorUtils.SetLayerMask(alertTx, "receiverLayers", p.alertReceiverLayers);
            EntityProfileEditorUtils.SetLayerMask(noise, "receiverLayers", p.alertReceiverLayers);

            EntityProfileEditorUtils.SetFloat(healthReactions, "despawnDelay", p.despawnDelay);

            EntityProfileEditorUtils.SetModuleActive(basePatrol, true);
            EntityProfileEditorUtils.SetModuleActive(herd, true);
            EntityProfileEditorUtils.SetModuleActive(wander, false);
            EntityProfileEditorUtils.SetModuleActive(flock, false);
            EntityProfileEditorUtils.SetModuleActive(chase, true);
            EntityProfileEditorUtils.SetModuleActive(search, true);
            EntityProfileEditorUtils.SetModuleActive(alertRx, true);
            EntityProfileEditorUtils.SetModuleActive(noiseRx, true);
            EntityProfileEditorUtils.SetModuleActive(melee, useMelee);
            EntityProfileEditorUtils.SetModuleActive(ranged, useRanged);
            EntityProfileEditorUtils.SetModuleActive(keepDistance, useKiting);
            EntityProfileEditorUtils.SetModuleActive(strafe, useRanged);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(navAgent);
            EditorUtility.SetDirty(motor);
            EditorUtility.SetDirty(basePatrol);
            EditorUtility.SetDirty(herd);
            EditorUtility.SetDirty(faction);
            EditorUtility.SetDirty(inventory);
            EditorUtility.SetDirty(loot);
            EditorUtility.SetDirty(audio);

            Debug.Log($"[EntityProfile] Robot Herd Patrol generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_HostileRobot
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_HostileRobot))]
public class EntityProfile_HostileRobotEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Hostile Robot", () =>
        {
            var p = (EntityProfile_HostileRobot)target;
            var go = p.gameObject;

            var chase    = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var strafe   = EntityProfileEditorUtils.GetOrAdd<StrafeModule>(go);
            var combat   = EntityProfileEditorUtils.GetOrAdd<EntityCombatModule>(go);
            var search   = EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
            var noiseRx  = EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
            var alertTx  = EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
            var alertRx  = EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
            var noise    = EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
            var audio    = EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
            var flock    = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
            var wander   = EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                           EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                           EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                           EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                           EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm      = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(chase,  "detectRange",         p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,  "loseTargetRange",     p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,  "attackRange",         p.attackRange);
            EntityProfileEditorUtils.SetFloat(chase,  "chaseSpeedMultiplier",p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(strafe, "engageRange",         p.strafeEngageRange);
            EntityProfileEditorUtils.SetFloat(strafe, "strafeRadius",        p.strafeRadius);

            EntityProfileEditorUtils.SetFloat(combat, "attackRange",         p.meleeDamageRange);
            EntityProfileEditorUtils.SetFloat(combat, "attackCooldown",      p.meleeCooldown);
            EntityProfileEditorUtils.SetInt  (combat, "attackDamage",        p.meleeDamage);

            EntityProfileEditorUtils.SetFloat(flock,  "separationRadius",    p.separationRadius);
            EntityProfileEditorUtils.SetFloat(flock,  "perceptionRadius",    p.perceptionRadius);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Hostile Robot generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_NPC
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_NPC))]
public class EntityProfile_NPCEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("NPC", () =>
        {
            var p  = (EntityProfile_NPC)target;
            var go = p.gameObject;

            var flee   = EntityProfileEditorUtils.GetOrAdd<FleeModule>(go);
            var wander = EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<InteractionFocusModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
            var hrm    = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(flee, "triggerRadius",       p.fleeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "safeRadius",          p.fleeSafeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "fleeSpeedMultiplier", p.fleeSpeed);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", 0f);

            Debug.Log($"[EntityProfile] NPC generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_DesertRat
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_DesertRat))]
public class EntityProfile_DesertRatEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Desert Rat", () =>
        {
            var p  = (EntityProfile_DesertRat)target;
            var go = p.gameObject;

            var flee = EntityProfileEditorUtils.GetOrAdd<FleeModule>(go);
                       EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                       EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                       EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                       EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm  = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(flee, "triggerRadius",       p.fleeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "safeRadius",          p.fleeSafeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "fleeSpeedMultiplier", p.fleeSpeed);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Desert Rat generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_BountyHunter
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_BountyHunter))]
public class EntityProfile_BountyHunterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Bounty Hunter", () =>
        {
            var p  = (EntityProfile_BountyHunter)target;
            var go = p.gameObject;

            var chase   = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var ranged  = EntityProfileEditorUtils.GetOrAdd<RangedAttackModule>(go);
            var strafe  = EntityProfileEditorUtils.GetOrAdd<StrafeModule>(go);
            var keepDis = EntityProfileEditorUtils.GetOrAdd<KeepDistanceModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
                          EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm     = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            if (p.enableFlocking)
            {
                var flock = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
                EntityProfileEditorUtils.SetFloat(flock, "separationRadius", p.separationRadius);
                EntityProfileEditorUtils.SetFloat(flock, "perceptionRadius", p.flockPerceptionRadius);
            }

            EntityProfileEditorUtils.SetFloat(chase,   "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,   "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,   "attackRange",          p.chaseStopRange);
            EntityProfileEditorUtils.SetFloat(chase,   "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(ranged,  "minRange",             p.minFireRange);
            EntityProfileEditorUtils.SetFloat(ranged,  "maxRange",             p.maxFireRange);
            EntityProfileEditorUtils.SetFloat(ranged,  "fireCooldown",         p.fireCooldown);
            EntityProfileEditorUtils.SetInt  (ranged,  "burstCount",           p.burstCount);
            EntityProfileEditorUtils.SetFloat(ranged,  "spreadAngle",          p.spreadAngle);
            EntityProfileEditorUtils.SetBool (ranged,  "leadTarget",           true);

            EntityProfileEditorUtils.SetFloat(strafe,  "engageRange",          p.strafeEngageRange);
            EntityProfileEditorUtils.SetFloat(strafe,  "strafeRadius",         p.strafeRadius);

            EntityProfileEditorUtils.SetFloat(keepDis, "detectRadius",         p.keepDistanceDetect);
            EntityProfileEditorUtils.SetFloat(keepDis, "preferredDistance",    p.keepDistancePreferred);

            // Priority layout: Strafe (21) orbits at range and yields below minStrafeDistance.
            // Chase (20) closes in or stop-and-faces when strafe yields. KeepDistance (19) backs off
            // when too close and both strafe and chase have yielded (i.e. inside minStrafeDistance).
            EntityProfileEditorUtils.SetInt(strafe,  "priority", ModulePriority.Reactive + 1);
            EntityProfileEditorUtils.SetInt(chase,   "priority", ModulePriority.Reactive);
            EntityProfileEditorUtils.SetInt(keepDis, "priority", ModulePriority.Reactive - 1);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Bounty Hunter generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_MountableAnt
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_MountableAnt))]
public class EntityProfile_MountableAntEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Mountable Ant", () =>
        {
            var p  = (EntityProfile_MountableAnt)target;
            var go = p.gameObject;

            var flee = EntityProfileEditorUtils.GetOrAdd<FleeModule>(go);
                       EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                       EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                       EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
            var hrm  = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(flee, "triggerRadius",       p.fleeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "safeRadius",          p.fleeSafeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "fleeSpeedMultiplier", p.fleeSpeed);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", 0f);

            Debug.Log($"[EntityProfile] Mountable Ant generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_RobotPhil
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_RobotPhil))]
public class EntityProfile_RobotPhilEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Robot Phil", () =>
        {
            var p  = (EntityProfile_RobotPhil)target;
            var go = p.gameObject;

            var chase  = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var combat = EntityProfileEditorUtils.GetOrAdd<EntityCombatModule>(go);
            var flock  = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm    = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(chase,  "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,  "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,  "attackRange",          p.attackRange);
            EntityProfileEditorUtils.SetFloat(chase,  "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(combat, "attackRange",          p.meleeDamageRange);
            EntityProfileEditorUtils.SetFloat(combat, "attackCooldown",       p.meleeCooldown);
            EntityProfileEditorUtils.SetInt  (combat, "attackDamage",         p.meleeDamage);

            EntityProfileEditorUtils.SetFloat(flock,  "separationRadius",     p.separationRadius);
            EntityProfileEditorUtils.SetFloat(flock,  "perceptionRadius",     p.perceptionRadius);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Robot Phil generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_RobotRoberto
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_RobotRoberto))]
public class EntityProfile_RobotRobertoEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Robot Roberto", () =>
        {
            var p  = (EntityProfile_RobotRoberto)target;
            var go = p.gameObject;

            var chase   = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var keepDis = EntityProfileEditorUtils.GetOrAdd<KeepDistanceModule>(go);
            var strafe  = EntityProfileEditorUtils.GetOrAdd<StrafeModule>(go);
            var flock   = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
                          EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                          EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm     = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(chase,   "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,   "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,   "attackRange",          p.attackRange);
            EntityProfileEditorUtils.SetFloat(chase,   "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(keepDis, "detectRadius",         p.keepDistanceDetect);
            EntityProfileEditorUtils.SetFloat(keepDis, "preferredDistance",    p.keepDistancePreferred);
            EntityProfileEditorUtils.SetFloat(keepDis, "speedMultiplier",      p.keepDistanceSpeed);

            EntityProfileEditorUtils.SetFloat(strafe,  "engageRange",          p.strafeEngageRange);
            EntityProfileEditorUtils.SetFloat(strafe,  "strafeRadius",         p.strafeRadius);

            EntityProfileEditorUtils.SetFloat(flock,   "separationRadius",     p.separationRadius);
            EntityProfileEditorUtils.SetFloat(flock,   "perceptionRadius",     p.perceptionRadius);

            // Priority layout: Strafe (21) orbits at range and yields below minStrafeDistance.
            // Chase (20) closes in or stop-and-faces when strafe yields. KeepDistance (19) backs off
            // when too close and both strafe and chase have yielded (i.e. inside minStrafeDistance).
            EntityProfileEditorUtils.SetInt(strafe,  "priority", ModulePriority.Reactive + 1);
            EntityProfileEditorUtils.SetInt(chase,   "priority", ModulePriority.Reactive);
            EntityProfileEditorUtils.SetInt(keepDis, "priority", ModulePriority.Reactive - 1);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Robot Roberto generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_RobotCath
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_RobotCath))]
public class EntityProfile_RobotCathEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Robot Cath", () =>
        {
            var p  = (EntityProfile_RobotCath)target;
            var go = p.gameObject;

            var chase  = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var ranged = EntityProfileEditorUtils.GetOrAdd<RangedAttackModule>(go);
            var strafe = EntityProfileEditorUtils.GetOrAdd<StrafeModule>(go);
            var flock  = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<CoverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm    = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(chase,  "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,  "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,  "attackRange",          p.attackRange);
            EntityProfileEditorUtils.SetFloat(chase,  "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(ranged, "minRange",             p.minFireRange);
            EntityProfileEditorUtils.SetFloat(ranged, "maxRange",             p.maxFireRange);
            EntityProfileEditorUtils.SetFloat(ranged, "fireCooldown",         p.fireCooldown);
            EntityProfileEditorUtils.SetInt  (ranged, "burstCount",           p.burstCount);
            EntityProfileEditorUtils.SetFloat(ranged, "spreadAngle",          p.spreadAngle);
            EntityProfileEditorUtils.SetBool (ranged, "leadTarget",           true);

            EntityProfileEditorUtils.SetFloat(strafe, "engageRange",          p.strafeEngageRange);
            EntityProfileEditorUtils.SetFloat(strafe, "strafeRadius",         p.strafeRadius);

            EntityProfileEditorUtils.SetFloat(flock,  "separationRadius",     p.separationRadius);
            EntityProfileEditorUtils.SetFloat(flock,  "perceptionRadius",     p.perceptionRadius);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Robot Cath generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_RobotErnst
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_RobotErnst))]
public class EntityProfile_RobotErnstEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Robot Ernst", () =>
        {
            var p  = (EntityProfile_RobotErnst)target;
            var go = p.gameObject;

            var chase  = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var combat = EntityProfileEditorUtils.GetOrAdd<EntityCombatModule>(go);
            var flock  = EntityProfileEditorUtils.GetOrAdd<FlockingModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
                         EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                         EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);
            var hrm    = EntityProfileEditorUtils.GetOrAdd<HealthReactionModule>(go);

            EntityProfileEditorUtils.SetFloat(chase,  "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase,  "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase,  "attackRange",          p.attackRange);
            EntityProfileEditorUtils.SetFloat(chase,  "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(combat, "attackRange",          p.meleeDamageRange);
            EntityProfileEditorUtils.SetFloat(combat, "attackCooldown",       p.meleeCooldown);
            EntityProfileEditorUtils.SetInt  (combat, "attackDamage",         p.meleeDamage);

            EntityProfileEditorUtils.SetFloat(flock,  "separationRadius",     p.separationRadius);
            EntityProfileEditorUtils.SetFloat(flock,  "perceptionRadius",     p.perceptionRadius);

            var wb = EntityProfileEditorUtils.GetOrAdd<WanderBehaviour>(go);
            EntityProfileEditorUtils.SetFloat(wb, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wb, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wb, "maxWaitTime",  p.wanderMaxWait);

            EntityProfileEditorUtils.SetFloat(hrm, "despawnDelay", p.despawnDelay);

            Debug.Log($"[EntityProfile] Robot Ernst generated on {go.name}", go);
        });
    }
}

#endif
