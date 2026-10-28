namespace Preview.DataSeeder;

/// <summary>
/// Fixed accounts present in every seeded preview, so smoke and load tests can sign tokens for them.
/// They are fictitious and exist only in previews.
/// </summary>
public static class KnownAccounts
{
    public static readonly Guid Manager = new("6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0001");
    public static readonly Guid Employee = new("6f1c2c1e-7a3b-4c8e-9a51-0b6f0d1a0002");

    public const string ManagerName = "Preview Manager";
    public const string EmployeeName = "Preview Employee";
}
