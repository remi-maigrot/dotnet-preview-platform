using System.Text.Json.Serialization;

namespace Sample.Api.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<AbsenceType>))]
public enum AbsenceType
{
    PaidLeave,
    CompensatoryTime,
    SickLeave,
    UnpaidLeave,
}
