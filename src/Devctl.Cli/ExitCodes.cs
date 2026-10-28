namespace Devctl.Cli;

/// <summary>Process exit codes, stable so scripts and CI can branch on them.</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
    public const int PrerequisiteMissing = 3;
    public const int NotFound = 4;
}
