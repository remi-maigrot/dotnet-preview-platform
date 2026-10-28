namespace Sample.Domain.Common;

/// <summary>Category of a business error. The API layer maps each kind to an HTTP status code.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

/// <summary>A business error with a stable, machine-readable code.</summary>
public sealed record DomainError(string Code, string Description, ErrorKind Kind)
{
    public static DomainError Validation(string code, string description) => new(code, description, ErrorKind.Validation);

    public static DomainError NotFound(string code, string description) => new(code, description, ErrorKind.NotFound);

    public static DomainError Conflict(string code, string description) => new(code, description, ErrorKind.Conflict);

    public static DomainError Forbidden(string code, string description) => new(code, description, ErrorKind.Forbidden);
}
