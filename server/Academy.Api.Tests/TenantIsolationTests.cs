using System.Net;
using System.Net.Http.Json;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

/// <summary>The property the whole platform depends on: one academy can never see or touch another's data.</summary>
[Collection(AppCollection.Name)]
public sealed class TenantIsolationTests(AppFactory factory)
{
    [Fact]
    public async Task Academy_cannot_read_another_academys_records()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var studentId = await a.NewStudentAsync(await a.NewCourseAsync());
        await a.PayAsync(studentId, 2500);

        Assert.Equal(1, (await a.Admin.GetJsonAsync("/api/students"))["total"]!.GetValue<int>());
        Assert.Equal(0, (await b.Admin.GetJsonAsync("/api/students"))["total"]!.GetValue<int>());
        Assert.Equal(0, (await b.Admin.GetJsonAsync("/api/payments"))["total"]!.GetValue<int>());
        Assert.Empty((await b.Admin.GetJsonAsync("/api/courses")).AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync($"/api/students/{studentId}")).StatusCode);
        Assert.Equal(0, (await b.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("totalRevenue"));
    }

    [Fact]
    public async Task Academy_cannot_write_to_another_academys_records()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var courseId = await a.NewCourseAsync();
        var studentId = await a.NewStudentAsync(courseId);
        var payment = await (await a.PayAsync(studentId, 1000)).ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await b.PayAsync(studentId, 500, b.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.Admin.PostAsJsonAsync($"/api/payments/{payment["id"]}/void", new { reason = "not mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await b.Admin.PutAsJsonAsync($"/api/courses/{courseId}", new { name = "Hijacked", fee = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await b.Admin.PostAsJsonAsync("/api/students", new { fullName = "Intruder", courseId, feeAmount = 1 })).StatusCode);

        var student = await a.StudentAsync(studentId);
        Assert.Equal(1000, student.Money("paid"));
    }

    [Fact]
    public async Task Names_and_emails_are_unique_per_academy_not_globally()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();

        await a.NewCourseAsync("Shared Name");
        await b.NewCourseAsync("Shared Name");
        Assert.Equal(HttpStatusCode.Conflict, (await a.Admin.PostAsJsonAsync("/api/courses", new { name = "shared name", fee = 1 })).StatusCode);

        var user = new { displayName = "Same Person", email = "same@person.test", role = "Staff", password = AppFactory.Password };
        Assert.Equal(HttpStatusCode.Created, (await a.Admin.PostAsJsonAsync("/api/users", user)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.Admin.PostAsJsonAsync("/api/users", user)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Admin.PostAsJsonAsync("/api/users", user)).StatusCode);
    }

    [Fact]
    public async Task Receipt_numbers_run_independently_in_each_academy()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var studentA = await a.NewStudentAsync(await a.NewCourseAsync());
        var studentB = await b.NewStudentAsync(await b.NewCourseAsync());

        Assert.Equal("RCP-000001", (await (await a.PayAsync(studentA, 100)).ReadJsonAsync())["receipt"]!.GetValue<string>());
        Assert.Equal("RCP-000002", (await (await a.PayAsync(studentA, 100)).ReadJsonAsync())["receipt"]!.GetValue<string>());
        Assert.Equal("RCP-000001", (await (await b.PayAsync(studentB, 100, b.Admin)).ReadJsonAsync())["receipt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Owner_has_no_academy_data_and_academies_cannot_reach_the_owner_console()
    {
        var a = await factory.NewAcademyAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await a.Owner.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Admin.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Admin.PostAsJsonAsync($"/api/platform/tenants/{a.Id}/activate", new { })).StatusCode);
    }

    [Fact]
    public async Task Audit_log_is_scoped_to_the_academy()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        await a.NewCourseAsync("Audited Course");

        var logsA = (await a.Admin.GetJsonAsync("/api/audit?entityType=Course"))["items"]!.AsArray();
        var logsB = (await b.Admin.GetJsonAsync("/api/audit?entityType=Course"))["items"]!.AsArray();
        Assert.Single(logsA);
        Assert.Equal("Created", logsA[0]!["action"]!.GetValue<string>());
        Assert.Contains("Audited Course", logsA[0]!["changes"]!.GetValue<string>());
        Assert.Empty(logsB);
    }
}
