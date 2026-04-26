// Minimal agent profile — wires only the components every agent must have.
// Use this as a starting point before adding behaviour modules manually,
// or as the base layer before running a more specific profile on top.
using UnityEngine;

public class EntityProfile_BaseAgent : MonoBehaviour
{
    [TextArea(2, 4)]
    public string description = "Base agent.";

    [Header("Health")]
    public int maxHealth = 100;
    public float despawnDelay = 10f;
}
