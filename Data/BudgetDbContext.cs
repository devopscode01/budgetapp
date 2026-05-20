using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

public sealed class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<ParsedTransaction> ParsedTransactions => Set<ParsedTransaction>();
    public DbSet<ManualExpense> ManualExpenses => Set<ManualExpense>();
    public DbSet<EtlRun> EtlRuns => Set<EtlRun>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<BudgetUser> BudgetUsers => Set<BudgetUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParsedTransaction>(e =>
        {
            e.HasIndex(x => x.DedupeHash).IsUnique();
            e.HasIndex(x => x.PostedDate);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.SourceFileName).HasMaxLength(260);
            e.Property(x => x.DedupeHash).HasMaxLength(64);
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ManualExpense>(e =>
        {
            e.HasIndex(x => x.Month);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<EtlRun>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Log).HasMaxLength(8000);
        });

        modelBuilder.Entity<Debt>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.CreditorName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.Property(x => x.MinimumPayment).HasPrecision(18, 2);
            e.Property(x => x.InterestRate).HasPrecision(6, 3);
        });

        modelBuilder.Entity<BudgetUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
        });
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        // Debts table (pre-auth schema)
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Debts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Debts" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL DEFAULT '',
                "CreditorName" TEXT NOT NULL DEFAULT '',
                "Type" INTEGER NOT NULL DEFAULT 0,
                "Balance" TEXT NOT NULL DEFAULT '0',
                "MinimumPayment" TEXT NOT NULL DEFAULT '0',
                "InterestRate" TEXT NOT NULL DEFAULT '0',
                "DueDate" TEXT NULL,
                "Notes" TEXT NOT NULL DEFAULT '',
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "UpdatedUtc" TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // BudgetUsers table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BudgetUsers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BudgetUsers" PRIMARY KEY,
                "Email" TEXT NOT NULL DEFAULT '',
                "DisplayName" TEXT NOT NULL DEFAULT '',
                "IsApproved" INTEGER NOT NULL DEFAULT 0,
                "IsAdmin" INTEGER NOT NULL DEFAULT 0,
                "CreatedUtc" TEXT NOT NULL DEFAULT '',
                "ApprovedUtc" TEXT NULL
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // Additive column migrations — SQLite doesn't support IF NOT EXISTS for ADD COLUMN,
        // so we catch the "duplicate column" error and ignore it.
        foreach (var sql in new[]
        {
            "ALTER TABLE \"ParsedTransactions\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"ManualExpenses\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"Debts\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"EtlRuns\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
        })
        {
            try { await Database.ExecuteSqlRawAsync(sql, cancellationToken: ct).ConfigureAwait(false); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column")) { }
        }
    }
}
