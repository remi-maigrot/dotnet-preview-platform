using System.Globalization;
using System.Text;

namespace Devctl.Cli.Diagnostics;

/// <summary>The report as Markdown: readable in a terminal, pasteable in an issue, and what the assistant receives.</summary>
internal static class DiagnosisReport
{
    private const int MaxEvents = 20;

    public static string Render(PreviewSnapshot snapshot, IReadOnlyList<Finding> findings, IReadOnlyList<LogExcerpt> logs, IReadOnlyList<ErrorTrace>? traces = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(logs);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# Preview {snapshot.Name}");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"- Phase: {snapshot.Phase}{(snapshot.Message is null ? string.Empty : $" ({snapshot.Message})")}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Namespace: {snapshot.Namespace}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- Image: {snapshot.Image}");

        report.AppendLine().AppendLine("## Findings").AppendLine();
        foreach (var finding in findings)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- **{finding.Severity}: {finding.Title}.** {finding.Detail} {finding.Hint}");
        }

        if (snapshot.Conditions.Count > 0)
        {
            report.AppendLine().AppendLine("## Conditions").AppendLine();
            foreach (var condition in snapshot.Conditions)
            {
                report.AppendLine($"- {condition.Type}={condition.Status} {condition.Reason} {condition.Message}".TrimEnd());
            }
        }

        report.AppendLine().AppendLine("## Pods").AppendLine();
        if (snapshot.Pods.Count == 0)
        {
            report.AppendLine("No pod in the namespace.");
        }

        foreach (var pod in snapshot.Pods)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- {pod.Name}: {pod.Phase}");
            foreach (var container in pod.Containers)
            {
                var state = container.Waiting ?? container.TerminatedReason ?? (container.Ready ? "ready" : "not ready");
                var last = container.LastTerminationReason is null ? string.Empty
                    : $", last terminated: {container.LastTerminationReason} (exit {container.LastExitCode})";
                report.AppendLine(CultureInfo.InvariantCulture, $"  - {container.Name}: {state}, {container.Restarts} restart(s){last}");
            }
        }

        if (snapshot.Jobs.Count > 0)
        {
            report.AppendLine().AppendLine("## Jobs").AppendLine();
            foreach (var job in snapshot.Jobs)
            {
                var state = job.Complete ? "complete" : job.Failed > 0 ? $"failed {job.Failed}x {job.FailureReason}".TrimEnd() : "running";
                report.AppendLine(CultureInfo.InvariantCulture, $"- {job.Name}: {state}");
            }
        }

        var warnings = snapshot.Events.Where(e => e.Type == "Warning").OrderByDescending(e => e.Time).Take(MaxEvents).ToList();
        if (warnings.Count > 0)
        {
            report.AppendLine().AppendLine("## Warning events (latest first)").AppendLine();
            foreach (var warning in warnings)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {warning.Time:u} {warning.Object} {warning.Reason} (x{warning.Count}): {warning.Message}");
            }
        }

        report.AppendLine().AppendLine("## Recent failed requests (Tempo, last hour)").AppendLine();
        if (traces is null)
        {
            report.AppendLine("Tempo did not answer: the observability stack is not installed, or not ready.");
        }
        else if (traces.Count == 0)
        {
            report.AppendLine("None.");
        }
        else
        {
            foreach (var trace in traces)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {trace.Start:u} {trace.Name} ({trace.DurationMs:0} ms), trace {trace.TraceId}");
            }

            report.AppendLine().AppendLine("Open one in Grafana: Explore, Tempo, paste the trace id; its logs are one click away.");
        }

        foreach (var log in logs)
        {
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Log of {log.Pod}/{log.Container}{(log.Previous ? " (before the last restart)" : string.Empty)}").AppendLine();
            report.AppendLine("```").AppendLine(log.Text.TrimEnd()).AppendLine("```");
        }

        return report.ToString();
    }
}
