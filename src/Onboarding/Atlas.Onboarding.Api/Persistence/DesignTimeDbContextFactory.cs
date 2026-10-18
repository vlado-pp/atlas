using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Onboarding.Api.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting the host or choosing a market.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    public OnboardingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlServer("Server=localhost;Database=design_time", sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", OnboardingDbContext.Schema))
            .Options);
}
