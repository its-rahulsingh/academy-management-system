using Academy.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Academy.Api.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the web host.
/// Adding a migration does not connect to this database.
/// </summary>
public sealed class AcademyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AcademyDbContext>
{
    public AcademyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AcademyDb")
            ?? "Host=localhost;Port=5432;Database=academyflow_design_only;Username=postgres;Password=unused";
        var options = new DbContextOptionsBuilder<AcademyDbContext>().UseNpgsql(connectionString).Options;
        return new AcademyDbContext(options, new SystemCurrentUser());
    }
}
