// Data-only profile for the mountable ant creature.
// Wanders and flees when not mounted. MountSuppressorModule handles in-game mount state.
using UnityEngine;

public class EntityProfile_MountableAnt : MonoBehaviour
{
    [Header("Flee")]
    public float fleeRadius = 10f;
    public float fleeSafeRadius = 18f;
    public float fleeSpeed = 1.6f;

    [Header("Wander")]
    public float wanderRadius = 80f;
    public float wanderMinWait = 1f;
    public float wanderMaxWait = 6f;
}
