using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class ExpenseProofAndActivityTests(AppFactory factory)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Pdf = "%PDF-1.4\n% test bill\n"u8.ToArray();

    private static object Proof(byte[] bytes, string name, string type = "image/png") =>
        new { fileName = name, dataUrl = $"data:{type};base64,{Convert.ToBase64String(bytes)}" };

    private static async Task<Guid> ExpenseAsync(HttpClient client, decimal amount, params object[] proofs)
    {
        var response = await client.PostAsJsonAsync("/api/expenses",
            new { title = $"Bill {Guid.NewGuid():N}"[..12], amount, category = "Utilities", method = "UPI", proofs });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync())["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> ExpenseRowAsync(HttpClient client, Guid id) =>
        (await client.GetJsonAsync("/api/expenses?pageSize=100"))["items"]!.AsArray().Single(e => e!["id"]!.GetValue<Guid>() == id)!;

    [Fact]
    public async Task Bills_are_attached_viewed_and_kept_with_the_expense()
    {
        var academy = await factory.NewAcademyAsync();
        var id = await ExpenseAsync(academy.Admin, 1200, Proof(Png, "bill.png"));
        Assert.Equal(1, (await ExpenseRowAsync(academy.Admin, id))["proofCount"]!.GetValue<int>());

        // A PDF can be added later; anything that is not an image or PDF is refused whatever it claims to be.
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/expenses/{id}/proofs", Proof(Pdf, "invoice.pdf", "application/pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await academy.Admin.PostAsJsonAsync($"/api/expenses/{id}/proofs", Proof("<script>alert(1)</script>"u8.ToArray(), "x.png"))).StatusCode);

        var files = (await academy.Admin.GetJsonAsync($"/api/expenses/{id}/proofs")).AsArray();
        Assert.Equal(2, files.Count);
        Assert.Equal("application/pdf", files[1]!["contentType"]!.GetValue<string>());
        var image = await academy.Admin.GetAsync($"/api/expenses/{id}/proofs/{files[0]!["id"]}");
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        // The log names the file but never stores its bytes.
        var logged = await factory.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == academy.Id && a.EntityType == "ExpenseProof").Select(a => a.Changes!).ToListAsync());
        Assert.Equal(2, logged.Count);
        Assert.All(logged, c => Assert.DoesNotContain(Convert.ToBase64String(Png), c));
        Assert.Contains(logged, c => c.Contains("invoice.pdf"));

        // Another academy can neither list nor open them.
        var other = await factory.NewAcademyAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.GetAsync($"/api/expenses/{id}/proofs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Admin.GetAsync($"/api/expenses/{id}/proofs/{files[0]!["id"]}")).StatusCode);

        // Once the expense is voided its proof is frozen.
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/expenses/{id}/void", new { reason = "Entered twice" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await academy.Admin.DeleteAsync($"/api/expenses/{id}/proofs/{files[0]!["id"]}")).StatusCode);
        Assert.Equal(2, (await academy.Admin.GetJsonAsync($"/api/expenses/{id}/proofs")).AsArray().Count);

        var csv = await academy.Admin.GetStringAsync($"/api/reports/export/expenses?format=csv&from=2000-01-01&to=2100-01-01");
        Assert.Contains("Proof attached", csv);
        Assert.Contains("Yes (2)", csv);
    }

    [Fact]
    public async Task The_super_admin_can_require_a_bill_above_an_amount()
    {
        var academy = await factory.NewAcademyAsync();
        var admin = await academy.NewUserAsync("Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/expenses/settings", new { proofRequiredAbove = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/expenses/settings", new { proofRequiredAbove = 1000 })).StatusCode);
        Assert.Equal(1000, (await academy.Admin.GetJsonAsync("/api/expenses/summary")).Money("proofRequiredAbove"));

        var refused = await admin.PostAsJsonAsync("/api/expenses", new { title = "Projector repair", amount = 2500, method = "Cash" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("₹1,000", await refused.ErrorAsync());
        await ExpenseAsync(admin, 900); // at or below the limit: proof optional

        var id = await ExpenseAsync(admin, 2500, Proof(Png, "repair.jpg"));
        var proof = (await admin.GetJsonAsync($"/api/expenses/{id}/proofs")).AsArray().Single()!["id"];
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/expenses/{id}/proofs/{proof}")).StatusCode);
        await admin.PostAsJsonAsync($"/api/expenses/{id}/proofs", Proof(Pdf, "repair.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/expenses/{id}/proofs/{proof}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/expenses/settings", new { proofRequiredAbove = (decimal?)null })).StatusCode);
        await ExpenseAsync(admin, 50000);
    }

    [Fact]
    public async Task The_activity_dashboard_counts_charts_and_flags_what_needs_attention()
    {
        var academy = await factory.NewAcademyAsync();
        var course = await academy.NewCourseAsync("Foundation", 50000);
        var student = await academy.NewStudentAsync(course, 50000, "Rohan Mehta");

        var payment = await (await academy.PayAsync(student, 10000)).ReadJsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/payments/{payment["id"]}/void", new { reason = "Parent asked for cash back" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync($"/api/students/{student}",
            new { fullName = "Rohan Mehta", courseId = course, feeAmount = 42000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await academy.Admin.PostAsJsonAsync($"/api/students/{student}/adjustments",
            new { kind = "Discount", amount = 6000, reason = "Scholarship" })).StatusCode);
        var expense = await ExpenseAsync(academy.Admin, 800, Proof(Png, "tea.png"));
        await academy.Admin.PostAsJsonAsync($"/api/expenses/{expense}/void", new { reason = "Wrong amount" });

        var stranger = factory.NewBrowser();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await stranger.PostAsJsonAsync("/api/auth/login", new { academyCode = academy.Code, email = $"admin@{academy.Code}.test", password = "wrong-guess" })).StatusCode);

        var d = await academy.Admin.GetJsonAsync("/api/audit/dashboard");
        var cards = d["cards"]!;
        Assert.True(cards["actions"]!.GetValue<int>() >= 6);
        Assert.True(cards["moneyEdits"]!.GetValue<int>() >= 4);
        Assert.True(cards["removals"]!.GetValue<int>() >= 2);
        Assert.Equal(3, cards["failedSignIns"]!.GetValue<int>());
        Assert.True(cards["activeUsers"]!.GetValue<int>() >= 1); // the owner who opened the academy counts too
        Assert.Equal(7, d["heatmap"]!.AsArray().Count);
        Assert.All(d["heatmap"]!.AsArray(), row => Assert.Equal(24, row!.AsArray().Count));
        Assert.Equal(30, d["timeline"]!.AsArray().Count);
        Assert.Equal(cards["actions"]!.GetValue<int>(), d["timeline"]!.AsArray().Sum(p => p!["total"]!.GetValue<int>()));
        Assert.Contains(d["byUser"]!.AsArray(), u => u!["name"]!.GetValue<string>() == "Super Admin" && u["role"]!.GetValue<string>() == "SuperAdmin");

        var flags = d["flags"]!.AsArray().Select(f => f!).ToList();
        JsonNode Flag(string title) => flags.Single(f => f["title"]!.GetValue<string>() == title);
        Assert.Equal("red", Flag("Payment cancelled")["severity"]!.GetValue<string>());
        Assert.Contains("Parent asked for cash back", Flag("Payment cancelled")["detail"]!.GetValue<string>());
        Assert.Contains("₹50,000 → ₹42,000", Flag("Agreed fee lowered")["detail"]!.GetValue<string>());
        Assert.Equal("red", Flag("Large discount")["severity"]!.GetValue<string>()); // the default alert is ₹5,000
        Assert.Contains("same day", Flag("Expense cancelled")["detail"]!.GetValue<string>());
        Assert.Contains("3 wrong passwords", Flag("Repeated failed sign-ins")["detail"]!.GetValue<string>());
        Assert.Equal("red", flags[0]["severity"]!.GetValue<string>());

        // Admins can look; only the Super Admin marks flags reviewed or changes the alert amount.
        var admin = await academy.NewUserAsync("Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit/dashboard")).StatusCode);
        var key = Flag("Large discount")["key"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/audit/flags/review", new { key, reviewed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await academy.NewUserAsync("Accountant")).GetAsync("/api/audit/dashboard")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync("/api/audit/flags/review", new { key, reviewed = true })).StatusCode);
        var after = await academy.Admin.GetJsonAsync("/api/audit/dashboard");
        Assert.DoesNotContain(after["flags"]!.AsArray(), f => f!["key"]!.GetValue<string>() == key);
        Assert.Equal(1, after["flagCounts"]!["reviewed"]!.GetValue<int>());
        var withReviewed = await academy.Admin.GetJsonAsync("/api/audit/dashboard?showReviewed=true");
        Assert.True(withReviewed["flags"]!.AsArray().Single(f => f!["key"]!.GetValue<string>() == key)!["reviewed"]!.GetValue<bool>());

        // Raising the alert amount turns the same discount into an ordinary one (the Super Admin gave it).
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync("/api/audit/settings", new { largeAmountAlert = 10000 })).StatusCode);
        var raised = await academy.Admin.GetJsonAsync("/api/audit/dashboard?showReviewed=true");
        Assert.DoesNotContain(raised["flags"]!.AsArray(), f => f!["title"]!.GetValue<string>() == "Large discount");

        // Another academy sees none of this.
        var other = await factory.NewAcademyAsync();
        var empty = await other.Admin.GetJsonAsync("/api/audit/dashboard");
        Assert.Empty(empty["flags"]!.AsArray());
        Assert.Equal(0, empty["cards"]!["failedSignIns"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_records_history_includes_its_payments_discounts_and_bills()
    {
        var academy = await factory.NewAcademyAsync();
        var course = await academy.NewCourseAsync("Foundation", 30000);
        var student = await academy.NewStudentAsync(course, 30000, "Isha Nair");
        // The fee locks once a payment exists, so it is changed first.
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PutAsJsonAsync($"/api/students/{student}", new { fullName = "Isha Nair", courseId = course, feeAmount = 28000 })).StatusCode);
        await academy.PayAsync(student, 5000);
        await academy.Admin.PostAsJsonAsync($"/api/students/{student}/adjustments", new { kind = "Discount", amount = 1000, reason = "Early bird" });
        var unrelated = await academy.NewStudentAsync(course, 30000, "Someone Else");
        await academy.PayAsync(unrelated, 7000);

        var history = await academy.Admin.GetJsonAsync($"/api/audit/history?entityType=Student&entityId={student}");
        Assert.Equal("Isha Nair", history["title"]!.GetValue<string>());
        var entries = history["entries"]!.AsArray().Select(e => e!).ToList();
        Assert.True(entries.Any(e => e["entityType"]!.GetValue<string>() == "Student" && e["action"]!.GetValue<string>() == "Updated"), history.ToJsonString());
        Assert.Contains(entries, e => e["entityType"]!.GetValue<string>() == "StudentPayment");
        Assert.Contains(entries, e => e["entityType"]!.GetValue<string>() == "FeeAdjustment");
        Assert.DoesNotContain(entries, e => e["changes"]?.ToString().Contains("7000") == true);
        // Newest first.
        Assert.True(entries[0]["id"]!.GetValue<long>() > entries[^1]["id"]!.GetValue<long>());

        var expense = await ExpenseAsync(academy.Admin, 500, Proof(Png, "receipt.png"));
        var expenseHistory = (await academy.Admin.GetJsonAsync($"/api/audit/history?entityType=Expense&entityId={expense}"))["entries"]!.AsArray();
        Assert.Contains(expenseHistory, e => e!["entityType"]!.GetValue<string>() == "ExpenseProof");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await academy.NewUserAsync("Staff")).GetAsync($"/api/audit/history?entityType=Student&entityId={student}")).StatusCode);
    }
}
