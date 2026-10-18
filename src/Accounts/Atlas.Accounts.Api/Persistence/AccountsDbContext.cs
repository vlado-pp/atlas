using Atlas.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Accounts.Api.Persistence;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public const string Schema = "accounts";

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.HasKey(a => a.Id);
            b.Property(a => a.ApplicationId).HasMaxLength(35).IsUnicode(false);
            b.HasIndex(a => a.ApplicationId).IsUnique();
            b.Property(a => a.Market).HasMaxLength(2).IsUnicode(false);
            b.Property(a => a.AccountNumber).HasMaxLength(34);
            b.Property(a => a.CardOrderReference).HasMaxLength(100);
        });
        modelBuilder.AddOutboxAndInbox(Schema);
    }
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlServer("Server=localhost;Database=design_time", sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountsDbContext.Schema))
            .Options);
}
