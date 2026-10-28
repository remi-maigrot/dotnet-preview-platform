namespace Sample.Api.Security;

internal static class Policies
{
    public const string Manager = "manager";
}

internal static class Claims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string ManagerRole = "manager";
}
