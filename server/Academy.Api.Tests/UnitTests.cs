using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Billing;
using Academy.Api.Infrastructure.Tenancy;
using Academy.Api.Infrastructure.Web;

namespace Academy.Api.Tests;

/// <summary>Pure rules that need no database.</summary>
public sealed class UnitTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    [Theory]
    [InlineData(TenantStatus.Active, SubscriptionStatus.Active, 5, true, false)]
    [InlineData(TenantStatus.Active, SubscriptionStatus.Trialing, 0, true, false)]
    [InlineData(TenantStatus.Active, SubscriptionStatus.PastDue, -7, true, true)]
    [InlineData(TenantStatus.Active, SubscriptionStatus.PastDue, -8, false, false)]
    [InlineData(TenantStatus.Active, SubscriptionStatus.Cancelled, 30, false, false)]
    [InlineData(TenantStatus.Suspended, SubscriptionStatus.Active, 30, false, false)]
    public void Access_follows_tenant_status_period_end_and_grace(
        TenantStatus tenant, SubscriptionStatus subscription, int periodEndOffsetDays, bool allowed, bool inGrace)
    {
        var result = TenantAccessRules.Evaluate(tenant, subscription, Today.AddDays(periodEndOffsetDays), Today, graceDays: 7);

        Assert.Equal(allowed, result.Allowed);
        Assert.Equal(inGrace, result.InGrace);
        Assert.Equal(allowed && !inGrace, result.Reason is null);
    }

    [Fact]
    public void Academy_without_a_subscription_has_no_access()
    {
        Assert.False(TenantAccessRules.Evaluate(TenantStatus.Active, null, null, Today, 7).Allowed);
    }

    [Theory]
    [InlineData(BillingInterval.Monthly, "2026-01-31", "2026-02-27")]
    [InlineData(BillingInterval.Monthly, "2026-11-14", "2026-12-13")]
    [InlineData(BillingInterval.Quarterly, "2026-11-14", "2027-02-13")]
    [InlineData(BillingInterval.Yearly, "2026-11-14", "2027-11-13")]
    public void Billing_period_ends_the_day_before_the_next_one_starts(BillingInterval interval, string start, string expectedEnd)
    {
        Assert.Equal(DateOnly.Parse(expectedEnd), BillingService.PeriodEndFor(DateOnly.Parse(start), interval));
    }

    [Fact]
    public void Gateway_signature_must_match_exactly()
    {
        const string secret = "test-secret";
        var signature = Signatures.HmacSha256Hex("order_1|pay_1", secret);

        Assert.True(Signatures.Matches("order_1|pay_1", secret, signature));
        Assert.True(Signatures.Matches("order_1|pay_1", secret, signature.ToUpperInvariant()));
        Assert.False(Signatures.Matches("order_1|pay_2", secret, signature));
        Assert.False(Signatures.Matches("order_1|pay_1", "other-secret", signature));
        Assert.False(Signatures.Matches("order_1|pay_1", secret, ""));
        Assert.False(Signatures.Matches("order_1|pay_1", secret, null));
    }

    [Fact]
    public void Role_permissions_narrow_from_super_admin_to_staff()
    {
        Assert.True(RolePermissions.Has(Roles.SuperAdmin, Permissions.BillingManage));
        Assert.False(RolePermissions.Has(Roles.Admin, Permissions.BillingManage));
        Assert.True(RolePermissions.Has(Roles.Admin, Permissions.UsersManage));
        Assert.True(RolePermissions.Has(Roles.Accountant, Permissions.LedgerCorrect));
        Assert.False(RolePermissions.Has(Roles.Accountant, Permissions.UsersManage));
        Assert.True(RolePermissions.Has(Roles.Staff, Permissions.PaymentsWrite));
        Assert.False(RolePermissions.Has(Roles.Staff, Permissions.LedgerCorrect));
        Assert.False(RolePermissions.Has(Roles.Staff, Permissions.DashboardFinance));
        Assert.Empty(RolePermissions.For(Roles.Owner));
        Assert.Empty(RolePermissions.For("Unknown"));
    }

    [Fact]
    public void Only_super_admins_and_owners_manage_super_admins()
    {
        Assert.Contains(Roles.SuperAdmin, Roles.ManageableBy(Roles.SuperAdmin));
        Assert.Contains(Roles.SuperAdmin, Roles.ManageableBy(Roles.Owner));
        Assert.DoesNotContain(Roles.SuperAdmin, Roles.ManageableBy(Roles.Admin));
        Assert.Empty(Roles.ManageableBy(Roles.Staff));
    }

    [Theory]
    [InlineData(100.50, true)]
    [InlineData(0.01, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(10.005, false)]
    [InlineData(100000001, false)]
    public void Amounts_must_be_positive_with_two_decimals(double amount, bool valid)
    {
        Assert.Equal(valid, Validate.Amount((decimal)amount));
    }

    [Fact]
    public void Search_text_cannot_inject_like_wildcards()
    {
        Assert.Equal(@"%50\%\_off%", QueryExtensions.Like(" 50%_off "));
    }

    [Fact]
    public void Voiding_records_who_when_and_why()
    {
        var userId = Guid.NewGuid();
        var expense = new Expense { Amount = 10 };

        expense.Void(userId, "Duplicate");

        Assert.Equal(LedgerStatus.Voided, expense.Status);
        Assert.Equal(userId, expense.VoidedByUserId);
        Assert.Equal("Duplicate", expense.VoidReason);
        Assert.NotNull(expense.VoidedAtUtc);
    }
}
