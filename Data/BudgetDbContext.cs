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
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<BillAlert> BillAlerts => Set<BillAlert>();
    public DbSet<BillPayment> BillPayments => Set<BillPayment>();
    public DbSet<LlmConfig> LlmConfigs => Set<LlmConfig>();
    public DbSet<UserCategory> UserCategories => Set<UserCategory>();
    public DbSet<ReportToken> ReportTokens => Set<ReportToken>();

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
            e.Property(x => x.AddedByName).HasMaxLength(256);
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
            e.Property(x => x.AddedByName).HasMaxLength(256);
        });

        modelBuilder.Entity<BudgetUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.HouseholdId).HasMaxLength(128);
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Value).HasPrecision(18, 2);
            e.Property(x => x.AddedByName).HasMaxLength(256);
        });

        modelBuilder.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(64);
            e.Property(x => x.HouseholdId).HasMaxLength(128);
            e.Property(x => x.InvitedByName).HasMaxLength(256);
            e.Property(x => x.UsedByDisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<BillAlert>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.AddedByName).HasMaxLength(256);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.LinkedDebt).WithMany().HasForeignKey(x => x.LinkedDebtId).IsRequired(false);
            e.HasMany(x => x.Payments).WithOne(x => x.Bill).HasForeignKey(x => x.BillAlertId);
        });

        modelBuilder.Entity<BillPayment>(e =>
        {
            e.HasIndex(x => new { x.BillAlertId, x.Month }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.AcknowledgedByName).HasMaxLength(256);
        });

        modelBuilder.Entity<LlmConfig>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Endpoint).HasMaxLength(500);
            e.Property(x => x.ApiKey).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<UserCategory>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Keywords).HasMaxLength(2000);
        });

        modelBuilder.Entity<ReportToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.SharedByName).HasMaxLength(256);
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

        // Assets table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Assets" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Assets" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL DEFAULT '',
                "Name" TEXT NOT NULL DEFAULT '',
                "Type" INTEGER NOT NULL DEFAULT 0,
                "Value" TEXT NOT NULL DEFAULT '0',
                "Notes" TEXT NOT NULL DEFAULT '',
                "UpdatedUtc" TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // Invitations table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Invitations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Invitations" PRIMARY KEY AUTOINCREMENT,
                "Token" TEXT NOT NULL DEFAULT '',
                "HouseholdId" TEXT NOT NULL DEFAULT '',
                "InvitedByName" TEXT NOT NULL DEFAULT '',
                "CreatedAt" TEXT NOT NULL DEFAULT '',
                "ExpiresAt" TEXT NOT NULL DEFAULT '',
                "IsUsed" INTEGER NOT NULL DEFAULT 0,
                "UsedAt" TEXT NULL,
                "UsedByDisplayName" TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // BillAlerts table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BillAlerts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BillAlerts" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL DEFAULT '',
                "Name" TEXT NOT NULL DEFAULT '',
                "Amount" TEXT NULL,
                "DayOfMonth" INTEGER NOT NULL DEFAULT 1,
                "LinkedDebtId" INTEGER NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "Notes" TEXT NOT NULL DEFAULT '',
                "AddedByName" TEXT NOT NULL DEFAULT '',
                "CreatedUtc" TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // BillPayments table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BillPayments" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BillPayments" PRIMARY KEY AUTOINCREMENT,
                "BillAlertId" INTEGER NOT NULL,
                "Month" TEXT NOT NULL DEFAULT '',
                "Amount" TEXT NOT NULL DEFAULT '0',
                "AcknowledgedUtc" TEXT NOT NULL DEFAULT '',
                "AcknowledgedByName" TEXT NOT NULL DEFAULT '',
                "DebtDeducted" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_BillPayments_BillAlerts" FOREIGN KEY ("BillAlertId") REFERENCES "BillAlerts" ("Id") ON DELETE CASCADE
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // LinkedTransactionId added after initial schema — idempotent ALTER
        try { await Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"BillPayments\" ADD COLUMN \"LinkedTransactionId\" INTEGER NULL",
            cancellationToken: ct).ConfigureAwait(false); } catch { /* already exists */ }

        // Unique index on BillPayments (one payment per bill per month)
        await Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillPayments_BillAlertId_Month"
            ON "BillPayments" ("BillAlertId", "Month");
            """, cancellationToken: ct).ConfigureAwait(false);

        // LlmConfigs table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "LlmConfigs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_LlmConfigs" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL DEFAULT '',
                "Provider" INTEGER NOT NULL DEFAULT 0,
                "Endpoint" TEXT NOT NULL DEFAULT 'http://localhost:11434',
                "ApiKey" TEXT NOT NULL DEFAULT '',
                "Model" TEXT NOT NULL DEFAULT 'llama3.2',
                "IsEnabled" INTEGER NOT NULL DEFAULT 0
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // UserCategories table (IDs start at 100 to avoid colliding with built-in enum values 0–15)
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "UserCategories" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UserCategories" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL DEFAULT '',
                "Name" TEXT NOT NULL DEFAULT '',
                "Color" TEXT NOT NULL DEFAULT '#6366F1',
                "Keywords" TEXT NOT NULL DEFAULT '',
                "SortOrder" INTEGER NOT NULL DEFAULT 0
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        // Ensure user category IDs are always ≥ 100 by inserting a dummy row if the table is empty
        await Database.ExecuteSqlRawAsync(
            """
            INSERT INTO sqlite_sequence (name, seq)
            SELECT 'UserCategories', 99
            WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = 'UserCategories')
              AND (SELECT COUNT(*) FROM "UserCategories") = 0;
            """, cancellationToken: ct).ConfigureAwait(false);

        // Additive column migrations — SQLite doesn't support IF NOT EXISTS for ADD COLUMN,
        // so we catch the "duplicate column" error and ignore it.
        // ReportTokens table
        await Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ReportTokens" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ReportTokens" PRIMARY KEY,
                "UserId" TEXT NOT NULL DEFAULT '',
                "Month" TEXT NOT NULL DEFAULT '',
                "CreatedUtc" TEXT NOT NULL DEFAULT '',
                "ExpiresUtc" TEXT NOT NULL DEFAULT '',
                "SharedByName" TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken: ct).ConfigureAwait(false);

        foreach (var sql in new[]
        {
            "ALTER TABLE \"ParsedTransactions\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"ManualExpenses\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"Debts\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"EtlRuns\" ADD COLUMN \"UserId\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"BudgetUsers\" ADD COLUMN \"TotpSecret\" TEXT NULL",
            "ALTER TABLE \"BudgetUsers\" ADD COLUMN \"TotpEnabled\" INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE \"BudgetUsers\" ADD COLUMN \"HouseholdId\" TEXT NULL",
            "ALTER TABLE \"ManualExpenses\" ADD COLUMN \"AddedByName\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"Debts\" ADD COLUMN \"AddedByName\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"Assets\" ADD COLUMN \"AddedByName\" TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE \"ParsedTransactions\" ADD COLUMN \"Alias\" TEXT NULL",
        })
        {
            try { await Database.ExecuteSqlRawAsync(sql, cancellationToken: ct).ConfigureAwait(false); }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column")) { }
        }
    }
}
