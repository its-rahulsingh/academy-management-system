using System.Net;
using System.Net.Http.Json;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class LedgerTests(AppFactory factory)
{
    [Fact]
    public async Task Payment_cannot_exceed_the_outstanding_fee()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);

        Assert.Equal(HttpStatusCode.Created, (await academy.PayAsync(studentId, 6000)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await academy.PayAsync(studentId, 4000.01m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await academy.PayAsync(studentId, 4000)).StatusCode);

        var student = await academy.StudentAsync(studentId);
        Assert.Equal("Paid", student["status"]!.GetValue<string>());
        Assert.Equal(0, student.Money("pending"));
    }

    [Fact]
    public async Task Simultaneous_payments_cannot_overpay_a_student()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);

        // Eight cashiers each try to collect 60% of the fee at the same moment: only one can fit.
        var cashiers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => academy.NewUserAsync("Staff")));
        var responses = await Task.WhenAll(cashiers.Select(c => academy.PayAsync(studentId, 6000, c)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(6000, (await academy.StudentAsync(studentId)).Money("paid"));
    }

    [Fact]
    public async Task Voiding_a_payment_restores_the_balance_and_keeps_the_record()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);
        var paymentId = (await (await academy.PayAsync(studentId, 3000)).ReadJsonAsync())["id"]!.GetValue<Guid>();

        Assert.Equal(HttpStatusCode.BadRequest, (await academy.Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await academy.Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "Entered twice" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await academy.Admin.PostAsJsonAsync($"/api/payments/{paymentId}/void", new { reason = "Again" })).StatusCode);

        Assert.Equal(10000, (await academy.StudentAsync(studentId)).Money("pending"));
        var listed = (await academy.Admin.GetJsonAsync("/api/payments"))["items"]!.AsArray().Single()!;
        Assert.Equal("Voided", listed["status"]!.GetValue<string>());
        Assert.Equal("Entered twice", listed["voidReason"]!.GetValue<string>());
        Assert.Equal(0, (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("totalRevenue"));
    }

    [Fact]
    public async Task Refunds_are_limited_to_what_was_paid_and_reduce_revenue()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);
        await academy.PayAsync(studentId, 5000);

        var tooMuch = await academy.Admin.PostAsJsonAsync("/api/payments/refunds", new { studentId, amount = 5001, method = "UPI" });
        var refund = await academy.Admin.PostAsJsonAsync("/api/payments/refunds", new { studentId, amount = 2000, method = "UPI" });

        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);
        var student = await academy.StudentAsync(studentId);
        Assert.Equal(3000, student.Money("paid"));
        Assert.Equal(7000, student.Money("pending"));
        Assert.Equal(3000, (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!.Money("totalRevenue"));
    }

    [Fact]
    public async Task Discount_lowers_what_is_owed_but_not_below_what_is_paid()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);
        await academy.PayAsync(studentId, 8000);

        var tooBig = await academy.Admin.PostAsJsonAsync($"/api/students/{studentId}/adjustments", new { kind = "Discount", amount = 2500, reason = "Scholarship" });
        var ok = await academy.Admin.PostAsJsonAsync($"/api/students/{studentId}/adjustments", new { kind = "Discount", amount = 2000, reason = "Scholarship" });

        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var student = await academy.StudentAsync(studentId);
        Assert.Equal(8000, student.Money("payable"));
        Assert.Equal("Paid", student["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fee_is_locked_once_a_payment_exists()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        var studentId = await academy.NewStudentAsync(courseId, fee: 10000);
        await academy.PayAsync(studentId, 1000);

        var changeFee = await academy.Admin.PutAsJsonAsync($"/api/students/{studentId}", new { fullName = "Asha Rao", courseId, feeAmount = 9000 });
        var rename = await academy.Admin.PutAsJsonAsync($"/api/students/{studentId}", new { fullName = "Asha R. Rao", courseId, feeAmount = 10000 });

        Assert.Equal(HttpStatusCode.BadRequest, changeFee.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Faculty_payouts_cannot_exceed_the_agreed_amount_and_can_be_voided()
    {
        var academy = await factory.NewAcademyAsync();
        var facultyId = (await (await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = "Ravi Kumar", agreedPayout = 20000 })).ReadJsonAsync())["id"]!.GetValue<Guid>();

        var first = await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 15000, method = "Bank Transfer" });
        var over = await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 5001, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        var payoutId = (await first.ReadJsonAsync())["id"]!.GetValue<Guid>();
        await academy.Admin.PostAsJsonAsync($"/api/payouts/{payoutId}/void", new { reason = "Wrong person" });
        var faculty = (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!;
        Assert.Equal(0, faculty.Money("paid"));
    }

    [Fact]
    public async Task Dashboard_and_reports_add_up()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync(), fee: 10000);
        await academy.PayAsync(studentId, 7000);
        await academy.Admin.PostAsJsonAsync("/api/expenses", new { title = "Rent", amount = 2000, category = "Rent", method = "Cash" });
        var voided = await (await academy.Admin.PostAsJsonAsync("/api/expenses", new { title = "Mistake", amount = 999, category = "Other", method = "Cash" })).ReadJsonAsync();
        await academy.Admin.PostAsJsonAsync($"/api/expenses/{voided["id"]}/void", new { reason = "Duplicate" });
        var facultyId = (await (await academy.Admin.PostAsJsonAsync("/api/faculty", new { fullName = "Ravi", agreedPayout = 5000 })).ReadJsonAsync())["id"]!.GetValue<Guid>();
        await academy.Admin.PostAsJsonAsync("/api/payouts", new { facultyId, amount = 1500, method = "UPI" });

        var finance = (await academy.Admin.GetJsonAsync("/api/dashboard"))["finance"]!;
        Assert.Equal(7000, finance.Money("totalRevenue"));
        Assert.Equal(3000, finance.Money("pendingFees"));
        Assert.Equal(2000, finance.Money("totalExpenses"));
        Assert.Equal(3500, finance.Money("facultyPayable"));
        Assert.Equal(3500, finance.Money("netCashBalance"));

        var report = await academy.Admin.GetJsonAsync("/api/reports/summary");
        Assert.Equal(7000, report.Money("netCollections"));
        Assert.Equal(3500, report.Money("netCash"));
        Assert.Equal(12, report["periods"]!.AsArray().Count);

        var csv = await academy.Admin.GetStringAsync("/api/reports/export/payments?format=csv");
        Assert.Contains("RCP-000001", csv);
        Assert.Contains("Asha Rao", csv);
    }

    [Fact]
    public async Task Receipt_shows_the_payment_and_the_running_balance()
    {
        var academy = await factory.NewAcademyAsync();
        var studentId = await academy.NewStudentAsync(await academy.NewCourseAsync("Science"), fee: 10000);
        var paymentId = (await (await academy.PayAsync(studentId, 4000)).ReadJsonAsync())["id"]!.GetValue<Guid>();

        var receipt = await academy.Admin.GetJsonAsync($"/api/payments/{paymentId}/receipt");

        Assert.Equal("RCP-000001", receipt["receipt"]!.GetValue<string>());
        Assert.Equal("Science", receipt["courseName"]!.GetValue<string>());
        Assert.Equal(6000, receipt.Money("balanceDue"));
        Assert.Equal("Super Admin", receipt["recordedBy"]!.GetValue<string>());
        Assert.StartsWith("Academy ", receipt["academyName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fee_reminders_are_queued_only_for_students_who_owe_and_have_an_email()
    {
        var academy = await factory.NewAcademyAsync();
        var courseId = await academy.NewCourseAsync();
        await academy.NewStudentAsync(courseId, name: "Owes With Email", email: "owes@family.test");
        await academy.NewStudentAsync(courseId, name: "Owes No Email");
        var paid = await academy.NewStudentAsync(courseId, name: "Fully Paid", email: "paid@family.test");
        await academy.PayAsync(paid, 10000);

        var result = await (await academy.Admin.PostAsJsonAsync("/api/students/reminders", new { })).ReadJsonAsync();

        Assert.Equal(1, result["queued"]!.GetValue<int>());
        var queued = await factory.WithDbAsync(db => Task.FromResult(
            db.EmailMessages.Where(m => m.TenantId == academy.Id && m.Category == "FeeReminder").Select(m => m.ToAddress).ToList()));
        Assert.Equal(["owes@family.test"], queued);
    }

    [Fact]
    public async Task Report_card_shows_test_history_with_class_average_and_rank()
    {
        var academy = await factory.NewAcademyAsync();
        var physicsId = await academy.NewSubjectAsync("Physics");
        var mathsId = await academy.NewSubjectAsync("Maths");
        var courseId = await academy.NewCourseWithSubjectsAsync("Science", physicsId, mathsId);
        var asha = await academy.NewStudentAsync(courseId, name: "Asha");
        var kabir = await academy.NewStudentAsync(courseId, name: "Kabir");
        var zoya = await academy.NewStudentAsync(courseId, name: "Zoya");

        async Task<Guid> NewTestAsync(string title, Guid subjectId, string date, int maxMarks) =>
            (await (await academy.Admin.PostAsJsonAsync("/api/tests", new { title, subjectId, courseId, maxMarks, testDate = date })).ReadJsonAsync())["id"]!.GetValue<Guid>();
        Task Mark(Guid test, Guid student, decimal marks) =>
            academy.Admin.PutAsJsonAsync($"/api/tests/{test}/results/{student}", new { marks, remarks = "" });

        var physics = await NewTestAsync("Physics 1", physicsId, "2026-07-01", 50);
        var maths = await NewTestAsync("Maths 1", mathsId, "2026-08-01", 100);
        await Mark(physics, asha, 40);
        await Mark(physics, kabir, 45);
        await Mark(physics, zoya, 20);
        await Mark(maths, asha, 90);
        await Mark(maths, kabir, 60);

        var card = await academy.Admin.GetJsonAsync($"/api/tests/report-cards/{asha}");
        var tests = card["tests"]!.AsArray();

        Assert.Equal("Asha", card["student"]!["fullName"]!.GetValue<string>());
        Assert.Equal(["Physics 1", "Maths 1"], tests.Select(t => t!["title"]!.GetValue<string>()));
        Assert.Equal(80, tests[0]!.Money("percent"));
        Assert.Equal(70, tests[0]!.Money("classAveragePercent"));
        Assert.Equal((2, 3), (tests[0]!["rank"]!.GetValue<int>(), tests[0]!["takers"]!.GetValue<int>()));
        Assert.Equal((1, 2), (tests[1]!["rank"]!.GetValue<int>(), tests[1]!["takers"]!.GetValue<int>()));
        Assert.Equal(85, card["summary"]!.Money("averagePercent"));
        Assert.Equal("Maths 1", card["summary"]!["best"]!["title"]!.GetValue<string>());
        Assert.Equal(2, card["subjects"]!.AsArray().Count);
    }

    [Fact]
    public async Task Report_card_is_private_to_the_academy_and_hidden_from_accountants()
    {
        var a = await factory.NewAcademyAsync();
        var b = await factory.NewAcademyAsync();
        var studentId = await a.NewStudentAsync(await a.NewCourseAsync());
        var accountant = await a.NewUserAsync("Accountant");

        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync($"/api/tests/report-cards/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync($"/api/tests/report-cards/{studentId}")).StatusCode);
        Assert.Equal(0, (await a.Admin.GetJsonAsync($"/api/tests/report-cards/{studentId}"))["summary"]!["testsTaken"]!.GetValue<int>());
    }
}
