using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WorkoutLogger.Context
{
    /// <summary>
    /// Used by the EF Core CLI (migrations add, database update) to construct a
    /// DbContext without booting the application.
    ///
    /// Without this, the tools build the host to find the context, which means
    /// Program.cs runs - including the Jwt:Key validation that deliberately
    /// throws on a bad configuration. Adding a migration should not require a
    /// valid signing key, so the two concerns are separated here: this factory
    /// needs a connection string and nothing else.
    /// </summary>
    public class WorkoutDbContextFactory : IDesignTimeDbContextFactory<WorkoutDbContext>
    {
        public WorkoutDbContext CreateDbContext(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                // Same source the running app uses, so the password does not have
                // to be duplicated anywhere for tooling to work.
                .AddUserSecrets<WorkoutDbContextFactory>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is not configured. Set it with " +
                    "'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"'. " +
                    "See SECURITY.md.");
            }

            var options = new DbContextOptionsBuilder<WorkoutDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new WorkoutDbContext(options);
        }
    }
}
