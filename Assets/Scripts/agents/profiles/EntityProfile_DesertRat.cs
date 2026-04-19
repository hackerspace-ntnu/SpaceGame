// Data-only profile for a small skittish desert rat.
// Flees everything, never attacks.
using UnityEngine;

public class EntityProfile_DesertRat : MonoBehaviour
{
    [Header("Flee")]
    public float fleeRadius = 7f;
    public float fleeSafeRadius = 14f;
    public float fleeSpeed = 1.8f;

    [Header("Wander")]
    public float wanderRadius = 60f;
    public float wanderMinWait = 0.3f;
    public float wanderMaxWait = 2f;

    [Header("Health")]
    public float despawnDelay = 15f;
}
