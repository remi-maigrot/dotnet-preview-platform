using Sample.Domain.Common;

namespace Sample.Domain.Employees;

/// <summary>A person who can request absences. Managers decide on the requests of their direct reports.</summary>
public sealed class Employee
{
    // Required by EF Core to materialise the entity.
    private Employee()
    {
        FullName = string.Empty;
        Email = string.Empty;
    }

    public Employee(Guid id, string fullName, string email, Guid? managerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (managerId == id)
        {
            throw new ArgumentException("An employee cannot be their own manager.", nameof(managerId));
        }

        Id = id;
        FullName = fullName;
        Email = email;
        ManagerId = managerId;
    }

    public Guid Id { get; private set; }

    [PersonalData]
    public string FullName { get; private set; }

    [PersonalData]
    public string Email { get; private set; }

    public Guid? ManagerId { get; private set; }

    /// <summary>Replaces identifying data, for pseudonymisation and retention purges.</summary>
    public void Pseudonymise(string fullName, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        FullName = fullName;
        Email = email;
    }
}
