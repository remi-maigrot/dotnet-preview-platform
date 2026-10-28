using System.CommandLine;
using Devctl.Cli.Diagnostics;
using Spectre.Console;

namespace Devctl.Cli.Commands;

/// <summary>
/// devctl preview diagnose: collects the state of a preview, explains it with offline rules, and, when an
/// OpenAI-compatible endpoint is configured, asks a model to explain the (redacted) report.
/// </summary>
internal static class DiagnoseCommand
{
    private const int TailLines = 60;
    private const int MaxLogs = 6;

    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var pr = PreviewCommand.PullRequestOption();
        var showPayload = new Option<bool>("--show-payload")
        {
            Description = $"Print the exact request that would be sent to {AssistantSettings.EndpointVariable}, redacted, and send nothing.",
        };
        var noAssistant = new Option<bool>("--no-assistant") { Description = "Only the report, even when an assistant endpoint is configured." };

        var command = new Command("diagnose", "Explain why a preview is not working: status, failing pods and jobs, warnings and logs.")
        {
            pr,
            showPayload,
            noAssistant,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var cluster = context.Cluster(result.GetValue(globals.Context)!);
            var name = Platform.PreviewName(result.GetValue(pr));

            AssistantSettings? assistant;
            try
            {
                assistant = result.GetValue(noAssistant) ? null : AssistantSettings.FromEnvironment(context.EnvironmentVariable);
            }
            catch (ArgumentException ex)
            {
                output.Error(ex.Message);
                return ExitCodes.Usage;
            }

            var snapshot = await output.StatusAsync($"Reading {name}…", () => cluster.GetSnapshotAsync(name, cancellationToken));
            if (snapshot is null)
            {
                output.Error($"No preview {name}.", "devctl preview list shows the existing ones.");
                return ExitCodes.NotFound;
            }

            var findings = Diagnosis.Analyse(snapshot);
            var logs = await CollectLogsAsync(cluster, snapshot, cancellationToken);
            var report = DiagnosisReport.Render(snapshot, findings, logs);

            if (result.GetValue(showPayload))
            {
                if (assistant is null)
                {
                    output.Error($"No assistant configured: set {AssistantSettings.EndpointVariable} and {AssistantSettings.ModelVariable}.");
                    return ExitCodes.Usage;
                }

                context.Console.Profile.Out.Writer.WriteLine(Assistant.Payload(assistant, report));
                return ExitCodes.Success;
            }

            string? explanation = null;
            if (assistant is not null)
            {
                try
                {
                    var payload = Assistant.Payload(assistant, report);
                    explanation = await output.StatusAsync($"Asking {assistant.Endpoint.Host}…",
                        () => new Assistant(context.Http, assistant).ExplainAsync(payload, cancellationToken));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    // The report stands on its own: an unreachable assistant is a warning, not a failure.
                    output.Warning($"The assistant could not be reached: {Markup.Escape(ex.Message)}");
                }
            }

            if (output.IsJson)
            {
                output.WriteJson(new { preview = name, phase = snapshot.Phase, findings, report, explanation });
            }
            else
            {
                // Raw Markdown: wrapping it at the terminal width would break the lines a user pastes into an issue.
                context.Console.Profile.Out.Writer.Write(report);
                if (explanation is not null)
                {
                    context.Console.Write(new Panel(new Text(explanation)).Header($"Assistant ({assistant!.Model})").Border(BoxBorder.Rounded));
                }
                else if (assistant is null)
                {
                    output.Info($"[grey]Set {AssistantSettings.EndpointVariable} and {AssistantSettings.ModelVariable} to have this report explained by a model (it is redacted first; --show-payload shows what is sent).[/]");
                }
            }

            return findings.Any(f => f.Severity == Severity.Error) ? ExitCodes.Failure : ExitCodes.Success;
        });
        return command;
    }

    /// <summary>
    /// Logs worth reading: containers that restarted (their previous run is where the crash is), containers not
    /// ready, and the pods of failed jobs. Bounded, so a broken namespace does not produce a novel.
    /// </summary>
    private static async Task<IReadOnlyList<LogExcerpt>> CollectLogsAsync(Kubernetes.IPlatformClient cluster, PreviewSnapshot snapshot, CancellationToken cancellationToken)
    {
        var failedJobs = snapshot.Jobs.Where(j => j.Failed > 0 && !j.Complete).Select(j => j.Name).ToHashSet(StringComparer.Ordinal);
        var wanted = snapshot.Pods
            .SelectMany(pod => pod.Containers.Select(container => (pod, container)))
            .Where(x => x.container.Restarts > 0
                || (!x.container.Ready && x.pod.Phase == "Running")
                || (x.pod.JobName is { } job && failedJobs.Contains(job)))
            .Take(MaxLogs);

        var logs = new List<LogExcerpt>();
        foreach (var (pod, container) in wanted)
        {
            var previous = container.Restarts > 0;
            var text = await cluster.GetLogAsync(snapshot.Namespace, pod.Name, container.Name, previous, TailLines, cancellationToken);
            if (text.Length > 0)
            {
                logs.Add(new LogExcerpt(pod.Name, container.Name, previous, text));
            }
        }

        return logs;
    }
}
