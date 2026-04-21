// Data-only profile for a friendly wandering NPC.
// Add to the NPC prefab, configure, then click Generate.
using UnityEngine;

public class EntityProfile_NPC : MonoBehaviour
{
    [TextArea(2, 4)]
    public string description = "A friendly NPC that wanders and flees from danger.";

    [Header("Health")]
    public int maxHealth = 80;

    [Header("Flee")]
    public float fleeRadius = 6f;
    public float fleeSafeRadius = 12f;
    public float fleeSpeed = 1.5f;

    [Header("Wander")]
    public float wanderRadius = 30f;
    public float wanderMinWait = 2f;
    public float wanderMaxWait = 8f;
}
