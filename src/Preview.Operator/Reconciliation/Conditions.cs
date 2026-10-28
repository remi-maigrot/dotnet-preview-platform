using k8s.Models;

namespace Preview.Operator.Reconciliation;

internal static class Conditions
{
    public const string True = "True";
    public const string False = "False";
    public const string Unknown = "Unknown";

    /// <summary>Sets a condition, keeping its last transition time when the status does not change.</summary>
    public static void Set(
        IList<V1Condition> conditions,
        string type,
        string status,
        string reason,
        string message,
        long? generation,
        DateTimeOffset now)
    {
        var existing = conditions.FirstOrDefault(c => c.Type == type);
        var transition = existing is not null && existing.Status == status ? existing.LastTransitionTime : now.UtcDateTime;
        if (existing is not null)
        {
            conditions.Remove(existing);
        }

        conditions.Add(new V1Condition
        {
            Type = type,
            Status = status,
            Reason = reason,
            Message = message,
            ObservedGeneration = generation,
            LastTransitionTime = transition,
        });
    }

    public static bool IsTrue(IEnumerable<V1Condition> conditions, string type) =>
        conditions.Any(c => c.Type == type && c.Status == True);
}
