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

    // Adds and wires the physical + base AI components every agent needs.
    public static void SetupBaseComponents(GameObject go, int maxHealth, float despawnDelay)
    {
        var rb         = GetOrAdd<Rigidbody>(go);
        var col        = GetOrAdd<CapsuleCollider>(go);
        var navAgent   = GetOrAdd<NavMeshAgent>(go);
        var motor      = GetOrAdd<NavMeshAgentMotor>(go);
        var controller = GetOrAdd<AgentController>(go);
        var animDriver = GetOrAdd<AgentAnimatorDriver>(go);
        var health     = GetOrAdd<HealthComponent>(go);
        var hrm        = GetOrAdd<HealthReactionModule>(go);

        // Kinematic — NavMeshAgent owns movement; Rigidbody is for collision layer queries only.
        rb.isKinematic = true;
        rb.useGravity  = false;
        EditorUtility.SetDirty(rb);

        SetObject(motor,      "agent",          navAgent);
        SetObject(controller, "motorComponent", motor);
        SetObject(controller, "animatorDriver", animDriver);
        SetInt   (health,     "maxHealth",      maxHealth);
        SetInt   (health,     "currentHealth",  maxHealth);
        SetFloat (hrm,        "despawnDelay",   despawnDelay);

        EditorUtility.SetDirty(navAgent);
        EditorUtility.SetDirty(motor);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(health);
        EditorUtility.SetDirty(hrm);
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_BaseAgent
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_BaseAgent))]
public class EntityProfile_BaseAgentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Base Agent", () =>
        {
            var p  = (EntityProfile_BaseAgent)target;
            var go = p.gameObject;

            EntityProfileEditorUtils.SetupBaseComponents(go, p.maxHealth, p.despawnDelay);
            EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
            EntityProfileEditorUtils.GetOrAdd<EntityAudioModule>(go);
            EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
            EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
            EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);

            Debug.Log($"[EntityProfile] Base Agent generated on {go.name}", go);
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

            EntityProfileEditorUtils.SetupBaseComponents(go, p.maxHealth, 0f);

            var flee         = EntityProfileEditorUtils.GetOrAdd<FleeModule>(go);
            var wander       = EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
            var watch        = EntityProfileEditorUtils.GetOrAdd<WatchModule>(go);
            var approach     = EntityProfileEditorUtils.GetOrAdd<ApproachModule>(go);
            var keepDistance = EntityProfileEditorUtils.GetOrAdd<KeepDistanceModule>(go);
                               EntityProfileEditorUtils.GetOrAdd<InteractionFocusModule>(go);
                               EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                               EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);

            EntityProfileEditorUtils.SetFloat(flee, "triggerRadius",       p.fleeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "safeRadius",          p.fleeSafeRadius);
            EntityProfileEditorUtils.SetFloat(flee, "fleeSpeedMultiplier", p.fleeSpeed);

            EntityProfileEditorUtils.SetFloat(wander, "wanderRadius", p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wander, "minWaitTime",  p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wander, "maxWaitTime",  p.wanderMaxWait);

            // Player-reaction modules are added but left inactive — toggle per NPC in the inspector.
            EntityProfileEditorUtils.SetModuleActive(flee,         true);
            EntityProfileEditorUtils.SetModuleActive(wander,       true);
            EntityProfileEditorUtils.SetModuleActive(watch,        false);
            EntityProfileEditorUtils.SetModuleActive(approach,     false);
            EntityProfileEditorUtils.SetModuleActive(keepDistance, false);

            Debug.Log($"[EntityProfile] NPC generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_GenericEnemy
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_GenericEnemy))]
public class EntityProfile_GenericEnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Generic Enemy", () =>
        {
            var p  = (EntityProfile_GenericEnemy)target;
            var go = p.gameObject;

            EntityProfileEditorUtils.SetupBaseComponents(go, p.maxHealth, p.despawnDelay);

            var basePatrol   = EntityProfileEditorUtils.GetOrAdd<BasePatrolModule>(go);
            var herd         = EntityProfileEditorUtils.GetOrAdd<HerdModule>(go);
            var wander       = go.GetComponent<WanderModule>();
            var flock        = go.GetComponent<FlockingModule>();
            var chase        = EntityProfileEditorUtils.GetOrAdd<ChaseModule>(go);
            var perception   = EntityProfileEditorUtils.GetOrAdd<PerceptionModule>(go);
            var search       = EntityProfileEditorUtils.GetOrAdd<SearchModule>(go);
            var alertTx      = EntityProfileEditorUtils.GetOrAdd<AlertBroadcaster>(go);
            var alertRx      = EntityProfileEditorUtils.GetOrAdd<AlertReceiverModule>(go);
            var noiseRx      = EntityProfileEditorUtils.GetOrAdd<NoiseReceiverModule>(go);
            var noise        = EntityProfileEditorUtils.GetOrAdd<NoiseEmitter>(go);
            var melee        = EntityProfileEditorUtils.GetOrAdd<CloseCombatModule>(go);
            var ranged       = EntityProfileEditorUtils.GetOrAdd<AgentRangedCombatModule>(go);
            var keepDistance = EntityProfileEditorUtils.GetOrAdd<KeepDistanceModule>(go);
                               EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);
                               EntityProfileEditorUtils.GetOrAdd<EntityInventoryComponent>(go);
                               EntityProfileEditorUtils.GetOrAdd<EntityLootTable>(go);

            bool useMelee  = p.attackStyle == RobotHerdAttackStyle.Melee  || p.attackStyle == RobotHerdAttackStyle.Mixed;
            bool useRanged = p.attackStyle == RobotHerdAttackStyle.Ranged ||
                             p.attackStyle == RobotHerdAttackStyle.KitingRanged ||
                             p.attackStyle == RobotHerdAttackStyle.Mixed;
            bool useKiting = p.attackStyle == RobotHerdAttackStyle.KitingRanged || p.attackStyle == RobotHerdAttackStyle.Mixed;

            EntityProfileEditorUtils.SetObject(basePatrol, "baseTransform",         p.baseTransform);
            EntityProfileEditorUtils.SetFloat (basePatrol, "patrolRadius",           p.patrolRadius);
            EntityProfileEditorUtils.SetFloat (basePatrol, "sampleDistance",         8f);
            EntityProfileEditorUtils.SetFloat (basePatrol, "minDestinationDistance", 8f);
            EntityProfileEditorUtils.SetFloat (basePatrol, "minWaitTime",            p.patrolMinWait);
            EntityProfileEditorUtils.SetFloat (basePatrol, "maxWaitTime",            p.patrolMaxWait);
            EntityProfileEditorUtils.SetFloat (basePatrol, "speedMultiplier",        p.herdSpeed);

            EntityProfileEditorUtils.SetString(herd, "herdId", p.herdId);

            EntityProfileEditorUtils.SetObject(chase,        "target", null);
            EntityProfileEditorUtils.SetObject(ranged,       "target", null);
            EntityProfileEditorUtils.SetObject(melee,        "target", null);
            EntityProfileEditorUtils.SetObject(keepDistance, "target", null);

            EntityProfileEditorUtils.SetFloat(chase, "detectRange",          p.detectRange);
            EntityProfileEditorUtils.SetFloat(chase, "loseTargetRange",      p.loseTargetRange);
            EntityProfileEditorUtils.SetFloat(chase, "chaseStopDistance",    p.chaseStopRange);
            EntityProfileEditorUtils.SetFloat(chase, "chaseSpeedMultiplier", p.chaseSpeed);

            EntityProfileEditorUtils.SetFloat(perception, "fieldOfViewAngle", p.fieldOfViewAngle);
            EntityProfileEditorUtils.SetFloat(perception, "memoryDuration",   p.memoryDuration);
            EntityProfileEditorUtils.SetLayerMask(perception, "occlusionLayers", p.occlusionLayers);

            EntityProfileEditorUtils.SetFloat(melee, "attackRange",    p.meleeRange);
            EntityProfileEditorUtils.SetFloat(melee, "attackCooldown", p.meleeCooldown);
            EntityProfileEditorUtils.SetInt  (melee, "attackDamage",   p.meleeDamage);

            EntityProfileEditorUtils.SetObject(ranged, "weapon",       p.weapon);
            EntityProfileEditorUtils.SetObject(ranged, "fireProfile",  p.fireProfile);
            EntityProfileEditorUtils.SetObject(ranged, "aimProfile",   p.aimProfile);
            EntityProfileEditorUtils.SetObject(ranged, "muzzleSocket", p.muzzleTransform);

            EntityProfileEditorUtils.SetFloat(keepDistance, "detectRadius",      p.keepDistanceDetect);
            EntityProfileEditorUtils.SetFloat(keepDistance, "preferredDistance", p.keepDistancePreferred);
            EntityProfileEditorUtils.SetFloat(keepDistance, "speedMultiplier",   p.keepDistanceSpeed);

            EntityProfileEditorUtils.SetInt(basePatrol,   "priority", ModulePriority.Fallback);
            EntityProfileEditorUtils.SetInt(herd,         "priority", ModulePriority.Social);
            EntityProfileEditorUtils.SetInt(chase,        "priority", ModulePriority.Reactive);
            EntityProfileEditorUtils.SetInt(melee,        "priority", ModulePriority.MeleeAttack);
            EntityProfileEditorUtils.SetInt(ranged,       "priority", ModulePriority.RangedAttack);
            EntityProfileEditorUtils.SetInt(keepDistance, "priority", ModulePriority.Reactive);

            EntityProfileEditorUtils.SetFloat(alertTx, "alertRadius",    p.alertRadius);
            EntityProfileEditorUtils.SetLayerMask(alertTx, "receiverLayers", p.alertReceiverLayers);
            EntityProfileEditorUtils.SetLayerMask(noise,   "receiverLayers", p.alertReceiverLayers);

            EntityProfileEditorUtils.SetModuleActive(basePatrol,   true);
            EntityProfileEditorUtils.SetModuleActive(herd,         true);
            EntityProfileEditorUtils.SetModuleActive(wander,       false);
            EntityProfileEditorUtils.SetModuleActive(flock,        false);
            EntityProfileEditorUtils.SetModuleActive(chase,        true);
            EntityProfileEditorUtils.SetModuleActive(search,       true);
            EntityProfileEditorUtils.SetModuleActive(alertRx,      true);
            EntityProfileEditorUtils.SetModuleActive(noiseRx,      true);
            EntityProfileEditorUtils.SetModuleActive(melee,        useMelee);
            EntityProfileEditorUtils.SetModuleActive(ranged,       useRanged);
            EntityProfileEditorUtils.SetModuleActive(keepDistance, useKiting);

            EditorUtility.SetDirty(basePatrol);
            EditorUtility.SetDirty(herd);

            Debug.Log($"[EntityProfile] Generic Enemy generated on {go.name}", go);
        });
    }
}

// ─────────────────────────────────────────────────────────────
// EntityProfile_Vehicle
// ─────────────────────────────────────────────────────────────

[CustomEditor(typeof(EntityProfile_Vehicle))]
public class EntityProfile_VehicleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EntityProfileEditorUtils.DrawGenerateButton("Mountable Vehicle", () =>
        {
            var p  = (EntityProfile_Vehicle)target;
            var go = p.gameObject;

            // Base agent stack: Rigidbody + Collider + NavMeshAgent + Motor + Controller + Anim + Health.
            EntityProfileEditorUtils.SetupBaseComponents(go, p.maxHealth, p.despawnDelay);
            EntityProfileEditorUtils.GetOrAdd<EntityFaction>(go);

            var col      = go.GetComponent<CapsuleCollider>();
            var navAgent = go.GetComponent<NavMeshAgent>();
            var wander   = EntityProfileEditorUtils.GetOrAdd<WanderModule>(go);
            var mount    = EntityProfileEditorUtils.GetOrAdd<MountModule>(go);
            var steer    = EntityProfileEditorUtils.GetOrAdd<SteerModule>(go);

            // Collider sized for the vehicle.
            if (col)
            {
                col.radius = p.colliderRadius;
                col.height = p.colliderHeight;
                col.center = p.colliderCenter;
                EditorUtility.SetDirty(col);
            }

            // NavMeshAgent tuning — vehicles are bigger and faster than NPCs.
            if (navAgent)
            {
                navAgent.speed            = p.agentSpeed;
                navAgent.angularSpeed     = p.agentAngularSpeed;
                navAgent.acceleration     = p.agentAcceleration;
                navAgent.radius           = p.agentRadius;
                navAgent.height           = p.agentHeight;
                navAgent.stoppingDistance = p.stoppingDistance;
                EditorUtility.SetDirty(navAgent);
            }

            // Wander = idle AI behaviour when not ridden.
            EntityProfileEditorUtils.SetFloat(wander, "wanderRadius",    p.wanderRadius);
            EntityProfileEditorUtils.SetFloat(wander, "minWaitTime",     p.wanderMinWait);
            EntityProfileEditorUtils.SetFloat(wander, "maxWaitTime",     p.wanderMaxWait);
            EntityProfileEditorUtils.SetFloat(wander, "speedMultiplier", p.wanderSpeedMultiplier);
            EntityProfileEditorUtils.SetInt  (wander, "priority",        ModulePriority.Fallback);
            EntityProfileEditorUtils.SetModuleActive(wander, p.wanderEnabled);

            // Mount lifecycle.
            EntityProfileEditorUtils.SetObject(mount, "seatPoint", p.seatPoint != null ? p.seatPoint : go.transform);
            EntityProfileEditorUtils.SetBool  (mount, "allowAISelfMovementWhenMounted", p.allowAISelfMovementWhenMounted);
            EntityProfileEditorUtils.SetModuleActive(mount, true);

            // Rider steering.
            EntityProfileEditorUtils.SetBool(steer, "jumpEnabled",  p.jumpEnabled);
            EntityProfileEditorUtils.SetBool(steer, "leapEnabled",  p.leapEnabled);
            EntityProfileEditorUtils.SetBool(steer, "riderCanRun",  p.riderCanRun);
            EntityProfileEditorUtils.SetModuleActive(steer, true);

            Debug.Log($"[EntityProfile] Mountable Vehicle generated on {go.name}", go);
        });
    }
}

#endif
