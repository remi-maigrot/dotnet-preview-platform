using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Devctl.Cli.Infrastructure;

internal enum OutputFormat
{
    Table,
    Json,
}

/// <summary>Writes either human output (Spectre) or machine output (one JSON document on stdout), never both.</summary>
internal sealed class Output(IAnsiConsole console, OutputFormat format, bool verbose, TextWriter errors)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // The JSON goes to a terminal or a pipe, never into HTML: keep ">=" readable instead of "\u003E=".
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public IAnsiConsole Console => console;

    public bool IsJson => format == OutputFormat.Json;

    public bool Verbose => verbose;

    public void WriteJson<T>(T value) => console.Profile.Out.Writer.WriteLine(JsonSerializer.Serialize(value, Json));

    public void Info(string message)
    {
        if (!IsJson)
        {
            console.MarkupLine(message);
        }
    }

    public void Success(string message) => Info($"[green]✓[/] {message}");

    public void Warning(string message) => Info($"[yellow]![/] {message}");

    /// <summary>Errors are always shown; in JSON mode they go to stderr so stdout stays parseable.</summary>
    public void Error(string message, string? hint = null)
    {
        if (IsJson)
        {
            errors.WriteLine($"error: {message}");
            if (hint is not null)
            {
                errors.WriteLine($"hint: {hint}");
            }

            return;
        }

        console.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
        if (hint is not null)
        {
            console.MarkupLine($"  [grey]{Markup.Escape(hint)}[/]");
        }
    }

    public void Trace(string message)
    {
        if (verbose && !IsJson)
        {
            console.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
        }
    }

    /// <summary>Runs <paramref name="work"/> behind a spinner in interactive mode, plainly otherwise.</summary>
    public async Task<T> StatusAsync<T>(string message, Func<Task<T>> work)
    {
        if (IsJson || !console.Profile.Capabilities.Interactive)
        {
            return await work();
        }

        return await console.Status().Spinner(Spinner.Known.Dots).StartAsync(message, _ => work());
    }
}
