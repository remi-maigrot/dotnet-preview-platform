using System.Reflection;

namespace Architecture.Tests;

internal static class Layers
{
    public static readonly Assembly Domain = typeof(Sample.Domain.Absences.AbsenceRequest).Assembly;
    public static readonly Assembly Application = typeof(Sample.Application.DependencyInjection).Assembly;
    public static readonly Assembly Infrastructure = typeof(Sample.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Contracts = typeof(Sample.Api.Contracts.ApiHeaders).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;
}
