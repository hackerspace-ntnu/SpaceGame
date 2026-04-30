// Abstract base for all behaviour modules. Provides priority + active toggle in the Inspector.
// Subclass this and implement Tick() to create a new drag-and-drop behaviour.
//
// Recommended priority scale (use these constants or multiples of them):
//   ModulePriority.Scripted    = 100  — cutscenes, forced interactions (InteractionFocusModule)
//   ModulePriority.Override    =  30  — urgent reactions that beat everything (e.g. danger flee)
//   ModulePriority.MeleeAttack =  23  — close-combat attack (melee preempts ranged when both can hit)
//   ModulePriority.RangedAttack=  22  — ranged attack (stands and fires when in band)
//   ModulePriority.Reactive    =  20  — chase, kite, cover
//   ModulePriority.Social      =  15  — flocking, group movement
//   ModulePriority.Ambient     =  10  — watch, approach, keep-distance
//   ModulePriority.Personality =   5  — idle look-around, emotes
//   ModulePriority.Fallback    =   0  — wander, patrol (always last)
using UnityEngine;

public static class ModulePriority
{
    public const int Scripted     = 100;
    public const int Override     =  30;
    public const int MeleeAttack  =  23;
    public const int RangedAttack =  22;
    public const int Reactive     =  20;
    public const int Social       =  15;
    public const int Ambient      =  10;
    public const int Personality  =   5;
    public const int Fallback     =   0;
}

public abstract class BehaviourModuleBase : MonoBehaviour, IBehaviourModule
{
    [Header("Module")]
    [Tooltip(
        "Higher priority modules are evaluated first. First non-null result wins.\n" +
        "Recommended scale:\n" +
        "  100 = Scripted (cutscenes)\n" +
        "   30 = Override (danger flee)\n" +
        "   20 = Reactive (chase, flee)\n" +
        "   15 = Social (flocking)\n" +
        "   10 = Ambient (watch, approach)\n" +
        "    5 = Personality (look-around)\n" +
        "    0 = Fallback (wander, patrol)"
    )]
    [SerializeField] private int priority = ModulePriority.Fallback;
    [SerializeField] private bool active = true;

    public int Priority => priority;
    public bool IsActive => active && enabled && gameObject.activeInHierarchy;
    // Override to false in modules that only fire side effects and never return a MoveIntent.
    public virtual bool ClaimsMovement => true;

    public abstract MoveIntent? Tick(in AgentContext context, float deltaTime);

    // Override in subclasses to show a description + feature hints in the Inspector.
    public virtual string ModuleDescription => string.Empty;

    // Call from Reset() in subclasses to set the recommended default when first added in the Inspector.
    protected void SetPriorityDefault(int defaultPriority)
    {
        priority = defaultPriority;
    }

    // Call from OnValidate() to enforce a floor on priority (fixes existing prefabs after recompile).
    protected void SetMinPriority(int min)
    {
        if (priority < min) priority = min;
    }

    protected virtual void OnValidate() { }
}
