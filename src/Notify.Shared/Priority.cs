namespace Notify.Shared;

public enum Priority
{
    Low,
    Normal,
    High,
}

public static class PriorityExtensions
{
    public static int ToApnsPriority(this Priority priority) => priority switch
    {
        Priority.Low => 5,
        Priority.Normal => 5,
        Priority.High => 10,
        _ => 5,
    };
}
