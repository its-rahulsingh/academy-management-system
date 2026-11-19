using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Academy.Api.Data;

/// <summary>
/// Single database, shared schema, one <c>tenant_id</c> column per academy-owned table.
/// Isolation is enforced here rather than in each endpoint:
/// reads go through a global query filter, writes through <see cref="ApplyTenantRules"/>.
/// </summary>
public sealed partial class AcademyDbContext(DbContextOptions<AcademyDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    /// <summary>Evaluated per query; null means "no academy" and therefore matches no tenant-owned row.</summary>
    public Guid? CurrentTenantId => currentUser.TenantId;

    // Platform
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<TenantLogo> TenantLogos => Set<TenantLogo>();

    // Identity
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    // Academy
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Faculty> Faculty => Set<Faculty>();
    public DbSet<StudentPayment> StudentPayments => Set<StudentPayment>();
    public DbSet<FeeAdjustment> FeeAdjustments => Set<FeeAdjustment>();
    public DbSet<FacultyPayout> FacultyPayouts => Set<FacultyPayout>();
    public DbSet<FacultySession> FacultySessions => Set<FacultySession>();
    public DbSet<AdmissionApplication> AdmissionApplications => Set<AdmissionApplication>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<AcademyTest> Tests => Set<AcademyTest>();
    public DbSet<TestResult> TestResults => Set<TestResult>();
    public DbSet<TenantCounter> TenantCounters => Set<TenantCounter>();

    // Operations
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<Batch> Batches => Set<Batch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>(InvoiceNumberSequence);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademyDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            GetType().GetMethod(nameof(ConfigureTenantOwned), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }

        // Users and audit rows have a nullable tenant: owners (null) see platform rows, academies see only their own.
        modelBuilder.Entity<AppUser>().HasQueryFilter(u => u.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => a.TenantId == CurrentTenantId);
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(t => t.User.TenantId == CurrentTenantId);

        ApplySnakeCaseNames(modelBuilder);
    }

    public const string InvoiceNumberSequence = "subscription_invoice_number_seq";

    private void ConfigureTenantOwned<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<T>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>PostgreSQL folds unquoted identifiers to lower case, so snake_case keeps hand-written SQL free of quoting.</summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties()) property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            foreach (var key in entity.GetKeys()) key.SetName(ToSnakeCase(key.GetName()!));
            foreach (var foreignKey in entity.GetForeignKeys()) foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            foreach (var index in entity.GetIndexes()) index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    internal static string ToSnakeCase(string name) => SnakeBoundary().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeBoundary();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        AddAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        AddAuditEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps new rows with the caller's academy and refuses any write that crosses academies.
    /// A bug in an endpoint therefore fails loudly instead of leaking or corrupting another academy's data.
    /// </summary>
    private void ApplyTenantRules()
    {
        var tenantId = currentUser.TenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId
                    ?? throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} was saved without a tenant.");
            }

            if (tenantId is not null && entry.Entity.TenantId != tenantId)
                throw new InvalidOperationException($"Cross-tenant write to {entry.Metadata.ClrType.Name} was blocked.");
        }

        foreach (var entry in ChangeTracker.Entries<TenantEntity>().Where(e => e.State == EntityState.Added))
            entry.Entity.CreatedByUserId ??= currentUser.UserId;
    }

    private static readonly HashSet<Type> NotAudited =
        [typeof(AuditLog), typeof(EmailMessage), typeof(PasswordResetToken), typeof(TenantCounter), typeof(Batch), typeof(TenantLogo)];

    /// <summary>Bookkeeping columns whose changes are noise in an audit trail.</summary>
    private static readonly HashSet<string> SkippedProperties =
    [
        nameof(AppUser.SecurityStamp), nameof(AppUser.FailedLoginCount), nameof(AppUser.LockoutEndUtc),
        nameof(AppUser.LastLoginAtUtc), nameof(Subscription.UpdatedAtUtc)
    ];

    private void AddAuditEntries()
    {
        var logs = new List<AuditLog>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (NotAudited.Contains(entry.Metadata.ClrType)) continue;

            var changes = DescribeChanges(entry);
            if (changes.Count == 0) continue;

            logs.Add(new AuditLog
            {
                TenantId = (entry.Entity as ITenantOwned)?.TenantId ?? (entry.Entity as AppUser)?.TenantId,
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                Action = ActionName(entry),
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue)),
                Changes = JsonSerializer.Serialize(changes),
                IpAddress = currentUser.IpAddress
            });
        }
        AuditLogs.AddRange(logs);
    }

    private static string ActionName(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => "Created",
        EntityState.Deleted => "Deleted",
        _ when entry.Entity is LedgerEntry { Status: LedgerStatus.Voided } && entry.Property(nameof(LedgerEntry.Status)).IsModified => "Voided",
        _ => "Updated"
    };

    private static Dictionary<string, object?> DescribeChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (SkippedProperties.Contains(name)) continue;
            var secret = name == nameof(AppUser.PasswordHash);

            if (entry.State == EntityState.Modified)
            {
                if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue)) continue;
                changes[name] = secret ? "[changed]" : new { old = Plain(property.OriginalValue), @new = Plain(property.CurrentValue) };
            }
            else if (!secret)
            {
                changes[name] = Plain(entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue);
            }
        }
        return changes;
    }

    private static object? Plain(object? value) => value switch
    {
        Enum e => e.ToString(),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        _ => value
    };
}
