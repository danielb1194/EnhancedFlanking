using System.Runtime.CompilerServices;
using Il2CppMenace.Tactical.Skills;

/// <summary>
/// Tracks the flanking state for skills during combat math evaluation, allowing
/// the Enhanced Flanking plugin to determine whether a skill is currently
/// benefiting from a flanking bonus. Maintains a mapping of active flanks
/// per skill and provides a thread-static reference to the skill currently
/// being evaluated.
/// </summary>
public static class FlankStateTracker
{
    public static readonly ConditionalWeakTable<Skill, FlankContext> ActiveFlanks = new();

    // The bridge: keeps track of which skill is currently evaluating combat math
    [System.ThreadStatic]
    public static Skill CurrentlyEvaluatingSkill;

    public class FlankContext
    {
        public bool IsFlanking;
    }
}
