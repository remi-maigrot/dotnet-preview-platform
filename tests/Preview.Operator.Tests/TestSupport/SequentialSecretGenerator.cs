using System.Globalization;
using Preview.Operator.Reconciliation;

namespace Preview.Operator.Tests.TestSupport;

/// <summary>Predictable secrets, so tests can tell whether a credential was generated again.</summary>
internal sealed class SequentialSecretGenerator : ISecretGenerator
{
    private int _next;

    public string Generate(int bytes) => $"secret-{(++_next).ToString(CultureInfo.InvariantCulture)}-{bytes.ToString(CultureInfo.InvariantCulture)}";
}
