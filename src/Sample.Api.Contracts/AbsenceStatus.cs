using System.Text.Json.Serialization;

namespace Sample.Api.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AbsenceStatus>))]
public enum AbsenceStatus
{
    Submitted,
    Approved,
    Rejected,
    Cancelled,
}
