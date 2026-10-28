using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sample.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to add migrations and build the migration bundle without starting the API.
/// No database is contacted at design time; the bundle receives its connection string at run time.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=design_time");
        return new AppDbContext(options.Options);
    }
}
