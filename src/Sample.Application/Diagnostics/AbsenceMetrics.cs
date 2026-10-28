using System.Diagnostics.Metrics;
using Sample.Domain.Absences;

namespace Sample.Application.Diagnostics;

/// <summary>Business metrics. Exported as <c>absence_requests_submitted_total</c> and <c>absence_requests_decided_total</c>.</summary>
public sealed class AbsenceMetrics
{
    public const string MeterName = "Sample.Absences";

    private readonly Counter<long> _submitted;
    private readonly Counter<long> _decided;

    public AbsenceMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _submitted = meter.CreateCounter<long>(
            "absence_requests_submitted",
            unit: "{request}",
            description: "Absence requests accepted by the API.");
        _decided = meter.CreateCounter<long>(
            "absence_requests_decided",
            unit: "{request}",
            description: "Absence requests that reached a final decision.");
    }

    public void Submitted(AbsenceType type) =>
        _submitted.Add(1, new KeyValuePair<string, object?>("absence.type", type.ToString()));

    public void Decided(AbsenceStatus outcome, bool automatic) =>
        _decided.Add(
            1,
            new KeyValuePair<string, object?>("absence.outcome", outcome.ToString()),
            new KeyValuePair<string, object?>("absence.automatic", automatic));
}
