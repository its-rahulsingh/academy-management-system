using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Academy.Api.Data.Configurations;

internal static class Columns
{
    public const int Money = 14;
    public const int Name = 150;
    public const int Email = 240;
    public const int Phone = 32;
    public const int Notes = 1200;
    public const int EnumText = 20;
    public const int Method = 30;

    public static void HasCreatedAt<T>(this EntityTypeBuilder<T> builder) where T : Entity =>
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now()");

    public static PropertyBuilder<decimal> IsMoney(this PropertyBuilder<decimal> property) => property.HasPrecision(Money, 2);

    public static PropertyBuilder<TEnum> IsEnumText<TEnum>(this PropertyBuilder<TEnum> property) where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(EnumText);
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> e)
    {
        e.ToTable("tenants");
        e.HasCreatedAt();
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(40);
        e.Property(x => x.Name).HasMaxLength(Columns.Name);
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.ContactName).HasMaxLength(Columns.Name);
        e.Property(x => x.ContactEmail).HasMaxLength(Columns.Email);
        e.Property(x => x.ContactPhone).HasMaxLength(Columns.Phone);
        e.Property(x => x.SuspensionReason).HasMaxLength(500);
        e.Property(x => x.Tagline).HasMaxLength(120);
    }
}

internal sealed class TenantLogoConfiguration : IEntityTypeConfiguration<TenantLogo>
{
    public void Configure(EntityTypeBuilder<TenantLogo> e)
    {
        e.ToTable("tenant_logos");
        e.HasKey(x => x.TenantId);
        e.Property(x => x.ContentType).HasMaxLength(40);
        e.ToTable(t => t.HasCheckConstraint("ck_tenant_logos_size", $"octet_length(data) <= {TenantLogo.MaxBytes}"));
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> e)
    {
        e.ToTable("plans");
        e.HasCreatedAt();
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Code).HasMaxLength(40);
        e.Property(x => x.Name).HasMaxLength(Columns.Name);
        e.Property(x => x.Description).HasMaxLength(500);
        e.Property(x => x.Price).IsMoney();
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Interval).IsEnumText();
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> e)
    {
        e.ToTable("subscriptions");
        e.HasCreatedAt();
        e.HasIndex(x => x.TenantId).IsUnique();
        // The billing job scans by period end, across all tenants.
        e.HasIndex(x => new { x.Status, x.CurrentPeriodEnd });
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.Price).IsMoney();
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Interval).IsEnumText();
        e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> e)
    {
        e.ToTable("subscription_invoices");
        e.HasCreatedAt();
        e.HasIndex(x => x.InvoiceNumber).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        e.HasIndex(x => new { x.Status, x.DueOn });
        e.HasIndex(x => x.ProviderOrderId);
        // One live invoice per period makes invoice generation safe to retry and to run on several servers.
        e.HasIndex(x => new { x.SubscriptionId, x.PeriodStart }).IsUnique().HasFilter("status <> 'Void'");
        e.Property(x => x.InvoiceNumber).HasMaxLength(30);
        e.Property(x => x.PlanName).HasMaxLength(Columns.Name);
        e.Property(x => x.Amount).IsMoney();
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.PaymentMethod).HasMaxLength(Columns.Method);
        e.Property(x => x.ProviderOrderId).HasMaxLength(80);
        e.Property(x => x.ProviderPaymentId).HasMaxLength(80);
        e.Property(x => x.Notes).HasMaxLength(Columns.Notes);
        e.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.ToTable("app_users");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        // NULLs are distinct in a unique index, so platform owners need their own rule.
        e.HasIndex(x => x.Email).IsUnique().HasFilter("tenant_id IS NULL");
        e.Property(x => x.Email).HasMaxLength(Columns.Email);
        e.Property(x => x.DisplayName).HasMaxLength(140);
        e.Property(x => x.Role).HasMaxLength(20);
        e.Property(x => x.SecurityStamp).HasMaxLength(32);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> e)
    {
        e.ToTable("password_reset_tokens");
        e.HasCreatedAt();
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.Property(x => x.TokenHash).HasMaxLength(64);
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("audit_logs");
        e.Property(x => x.Id).UseIdentityAlwaysColumn();
        e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now()");
        e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        e.Property(x => x.UserName).HasMaxLength(140);
        e.Property(x => x.Action).HasMaxLength(40);
        e.Property(x => x.EntityType).HasMaxLength(80);
        e.Property(x => x.EntityId).HasMaxLength(80);
        e.Property(x => x.Changes).HasColumnType("jsonb");
        e.Property(x => x.IpAddress).HasMaxLength(45);
    }
}

internal sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> e)
    {
        e.ToTable("email_outbox");
        e.HasCreatedAt();
        e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        e.Property(x => x.ToAddress).HasMaxLength(Columns.Email);
        e.Property(x => x.Subject).HasMaxLength(200);
        e.Property(x => x.Category).HasMaxLength(40);
        e.Property(x => x.Status).IsEnumText();
        e.Property(x => x.LastError).HasMaxLength(1000);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> e)
    {
        e.ToTable("batches");
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("now()").IsRequired();
    }
}
