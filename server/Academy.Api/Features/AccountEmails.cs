using Academy.Api.Data;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Auth;
using Academy.Api.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// Sign-in details emailed to people when an account is made for them or an administrator sets a new password.
/// For now the starting password is included in the email; every such account must choose its own password at
/// first sign-in (<see cref="AppUser.MustChangePassword"/>), and nothing else works until it does.
/// Queued with the caller's changes, so nothing is sent unless the account is actually saved.
/// </summary>
public static class AccountEmails
{
    public static Task QueueWelcomeAsync(AcademyDbContext db, EmailQueue email, string signInUrl, AppUser user, string password, CancellationToken ct) =>
        QueueAsync(db, email, signInUrl, user, password, isNewAccount: true, ct);

    public static Task QueuePasswordSetAsync(AcademyDbContext db, EmailQueue email, string signInUrl, AppUser user, string password, CancellationToken ct) =>
        QueueAsync(db, email, signInUrl, user, password, isNewAccount: false, ct);

    private static async Task QueueAsync(
        AcademyDbContext db, EmailQueue email, string signInUrl, AppUser user, string password, bool isNewAccount, CancellationToken ct)
    {
        var academy = await db.Tenants.AsNoTracking().Where(t => t.Id == user.TenantId).Select(t => new { t.Name, t.Code }).FirstOrDefaultAsync(ct);
        if (academy is null) return;

        var (subject, body) = isNewAccount
            ? EmailTemplates.Welcome(user.DisplayName, academy.Name, academy.Code, user.Email, password, signInUrl, WhatItIsFor(user.Role))
            : EmailTemplates.PasswordSet(user.DisplayName, academy.Name, academy.Code, user.Email, password, signInUrl);
        email.Enqueue(user.TenantId, user.Email, subject, body, isNewAccount ? "Welcome" : "PasswordSet");
    }

    private static string WhatItIsFor(string role) => role switch
    {
        Roles.Faculty => "Use it to clock in and out of your classes and to see your sessions and pay.",
        Roles.Student => "Use it to see your fees, receipts, subjects and test results.",
        Roles.SuperAdmin => "You can manage everything in the academy, including its users and subscription.",
        _ => ""
    };
}
