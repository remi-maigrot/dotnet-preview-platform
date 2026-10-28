using System.Globalization;

namespace Devctl.Cli.Diagnostics;

internal enum Severity
{
    Info,
    Warning,
    Error,
}

internal sealed record Finding(Severity Severity, string Title, string Detail, string Hint);

/// <summary>
/// Turns a snapshot into findings, with rules for the failures previews actually hit. Deterministic and offline:
/// the optional assistant only rephrases this report, it does not replace it.
/// </summary>
internal static class Diagnosis
{
    public static IReadOnlyList<Finding> Analyse(PreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        if (snapshot.Phase == "Failed")
        {
            findings.Add(new Finding(Severity.Error, "The preview is in phase Failed", snapshot.Message ?? "No message from the operator.",
                "Fix the cause below, then run devctl preview create again: the operator retries on any change."));
        }

        foreach (var pod in snapshot.Pods)
        {
            foreach (var container in pod.Containers)
            {
                findings.AddRange(ContainerFindings(snapshot, pod, container));
            }
        }

        foreach (var job in snapshot.Jobs.Where(j => j.Failed > 0 && !j.Complete))
        {
            findings.Add(new Finding(Severity.Error, $"Job {job.Name} failed",
                $"{job.Failed} failed attempt(s){(job.FailureReason is null ? string.Empty : $", {job.FailureReason}: {job.FailureMessage}")}.",
                job.Name.Contains("deploy", StringComparison.Ordinal)
                    ? "helm could not install the chart: its log below usually names the resource or the hook that failed."
                    : "See the log of its pod below."));
        }

        foreach (var warning in snapshot.Events.Where(e => e.Type == "Warning"))
        {
            if (warning.Message.Contains("exceeded quota", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(Severity.Error, "The namespace quota is exhausted", $"{warning.Object}: {warning.Message}",
                    "Lower the resources requested in the preview, or raise Operator__QuotaCpu / Operator__QuotaMemory."));
            }
            else if (warning.Reason == "FailedScheduling")
            {
                findings.Add(new Finding(Severity.Error, $"{warning.Object} cannot be scheduled", warning.Message,
                    "The cluster is short of CPU or memory: delete idle previews (devctl preview list) or add a node."));
            }
            else if (warning.Reason == "Unhealthy" && warning.Message.Contains("Readiness", StringComparison.Ordinal) && warning.Count >= 3)
            {
                findings.Add(new Finding(Severity.Warning, $"{warning.Object} keeps failing its readiness probe", warning.Message,
                    "/health/ready checks the database: is postgresql running, and is the connection string secret right?"));
            }
        }

        if (findings.Count == 0)
        {
            findings.Add(snapshot.Phase == "Ready"
                ? new Finding(Severity.Info, "Nothing wrong found", "The preview is Ready and every container runs.", "If the API still misbehaves, its logs are below.")
                : new Finding(Severity.Info, $"The preview is {snapshot.Phase}, with no failure yet",
                    snapshot.Message ?? "The operator is still working on it.", "Wait a minute, then run diagnose again."));
        }

        return [.. findings.DistinctBy(f => f.Title).OrderByDescending(f => f.Severity)];
    }

    private static IEnumerable<Finding> ContainerFindings(PreviewSnapshot snapshot, PodInfo pod, ContainerInfo container)
    {
        var where = $"{pod.Name}/{container.Name}";
        switch (container.Waiting)
        {
            case "ImagePullBackOff" or "ErrImagePull" or "InvalidImageName":
                yield return new Finding(Severity.Error, $"The image of {where} cannot be pulled", container.WaitingMessage ?? container.Waiting,
                    "Check that this tag exists in the registry and that the cluster can reach it (devctl up pushes local builds).");
                yield break;
            case "CreateContainerConfigError":
                yield return new Finding(Severity.Error, $"{where} cannot start", container.WaitingMessage ?? container.Waiting,
                    "A Secret or ConfigMap it mounts is missing: the operator creates them, see its logs (devctl logs operator).");
                yield break;
        }

        var reason = container.LastTerminationReason ?? container.TerminatedReason;
        var exitCode = container.LastExitCode ?? container.TerminatedExitCode;
        if (reason == "OOMKilled")
        {
            yield return new Finding(Severity.Error, $"{where} runs out of memory", "The kernel killed it (OOMKilled).",
                "Raise the memory limit of the preview (spec.resources.memory) or look for a leak in its log.");
        }
        else if (container.Waiting == "CrashLoopBackOff" || container.Restarts > 0)
        {
            yield return new Finding(Severity.Error, $"{where} crashes",
                string.Create(CultureInfo.InvariantCulture, $"{container.Restarts} restart(s), last exit code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}{(reason is null ? string.Empty : $" ({reason})")}."),
                "Its log from before the restart is below; a configuration error is usually on the last lines.");
        }
    }
}
