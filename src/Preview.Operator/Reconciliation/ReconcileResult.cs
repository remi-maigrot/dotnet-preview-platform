namespace Preview.Operator.Reconciliation;

/// <param name="RequeueAfter">When to look at the resource again even if nothing changes. Null: wait for the next event.</param>
internal readonly record struct ReconcileResult(TimeSpan? RequeueAfter)
{
    public static ReconcileResult Done => new(null);

    public static ReconcileResult After(TimeSpan delay) => new(delay);
}
