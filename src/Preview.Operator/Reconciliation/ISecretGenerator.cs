using System.Security.Cryptography;

namespace Preview.Operator.Reconciliation;

internal interface ISecretGenerator
{
    /// <summary>A random URL-safe secret carrying <paramref name="bytes"/> bytes of entropy.</summary>
    string Generate(int bytes);
}

internal sealed class RandomSecretGenerator : ISecretGenerator
{
    public string Generate(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
