namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A condition a character is IN, rather than an event that happened to it — beaten down and still
    /// down. A step reads it from the live character as well as the mark, because a mark only counts
    /// in the step it lands in: Merob beaten down on the walk back must still count once the step
    /// that asks for it begins.
    /// </summary>
    public interface ISubduable
    {
        bool IsSubdued { get; }
    }

    /// <summary>A line a character offers the visor under the step that is about it — "Her ward: 1/3 broken".</summary>
    public interface IQuestStatus
    {
        string QuestStatus { get; }
    }
}
