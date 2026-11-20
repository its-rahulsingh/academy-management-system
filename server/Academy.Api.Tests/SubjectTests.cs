using System.Net;
using System.Net.Http.Json;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class SubjectTests(AppFactory factory)
{
    [Fact]
    public async Task Subject_names_are_unique_per_academy_ignoring_case()
    {
        var academy = await factory.NewAcademyAsync();
        await academy.NewSubjectAsync("Physics", "phy");

        var duplicate = await academy.Admin.PostAsJsonAsync("/api/subjects", new { name = "  PHYSICS " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Another academy has its own master.
        var other = await factory.NewAcademyAsync();
        await other.NewSubjectAsync("Physics");
        var subject = (await academy.Admin.GetJsonAsync("/api/subjects")).AsArray().Single()!;
        Assert.Equal("PHY", subject["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Courses_list_their_subjects_and_tests_must_use_one_of_them()
    {
        var academy = await factory.NewAcademyAsync();
        var physics = await academy.NewSubjectAsync("Physics");
        var biology = await academy.NewSubjectAsync("Biology");
        var courseId = await academy.NewCourseWithSubjectsAsync("JEE", physics);

        var course = (await academy.Admin.GetJsonAsync("/api/courses")).AsArray().Single()!;
        Assert.Equal(["Physics"], course["subjects"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));

        var wrongSubject = await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", subjectId = biology, courseId, maxMarks = 50 });
        var noSubject = await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", courseId, maxMarks = 50 });
        var right = await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", subjectId = physics, courseId, maxMarks = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, wrongSubject.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noSubject.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);

        var test = (await academy.Admin.GetJsonAsync("/api/tests"))["items"]![0]!;
        Assert.Equal("Physics", test["subjectName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_subject_with_tests_in_a_course_cannot_be_unlinked_from_it()
    {
        var academy = await factory.NewAcademyAsync();
        var physics = await academy.NewSubjectAsync("Physics");
        var maths = await academy.NewSubjectAsync("Maths");
        var courseId = await academy.NewCourseWithSubjectsAsync("JEE", physics, maths);
        await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", subjectId = physics, courseId, maxMarks = 50 });

        var dropPhysics = await academy.Admin.PutAsJsonAsync($"/api/courses/{courseId}", new { name = "JEE", fee = 10000, subjectIds = new[] { maths } });
        Assert.Equal(HttpStatusCode.BadRequest, dropPhysics.StatusCode);

        var dropMaths = await academy.Admin.PutAsJsonAsync($"/api/courses/{courseId}", new { name = "JEE", fee = 10000, subjectIds = new[] { physics } });
        Assert.Equal(HttpStatusCode.OK, dropMaths.StatusCode);

        // Leaving subjectIds out keeps the links as they are.
        await academy.Admin.PutAsJsonAsync($"/api/courses/{courseId}", new { name = "JEE Main", fee = 12000 });
        Assert.Single((await academy.Admin.GetJsonAsync("/api/courses")).AsArray().Single()!["subjects"]!.AsArray());
    }

    [Fact]
    public async Task Renaming_a_subject_renames_it_everywhere_and_inactive_ones_cannot_be_newly_linked()
    {
        var academy = await factory.NewAcademyAsync();
        var physics = await academy.NewSubjectAsync("Physics");
        var courseId = await academy.NewCourseWithSubjectsAsync("JEE", physics);
        await academy.Admin.PostAsJsonAsync("/api/tests", new { title = "Unit 1", subjectId = physics, courseId, maxMarks = 50 });

        await academy.Admin.PutAsJsonAsync($"/api/subjects/{physics}", new { name = "Physics (Main)", active = false });
        Assert.Equal("Physics (Main)", (await academy.Admin.GetJsonAsync("/api/tests"))["items"]![0]!["subjectName"]!.GetValue<string>());

        var later = await academy.Admin.PostAsJsonAsync("/api/courses", new { name = "NEET", fee = 1, subjectIds = new[] { physics } });
        Assert.Equal(HttpStatusCode.BadRequest, later.StatusCode);
        // Where it was already linked, the inactive subject may stay.
        var keep = await academy.Admin.PutAsJsonAsync($"/api/courses/{courseId}", new { name = "JEE", fee = 1, subjectIds = new[] { physics } });
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
    }

    [Fact]
    public async Task Another_academys_subject_cannot_be_linked()
    {
        var academy = await factory.NewAcademyAsync();
        var other = await factory.NewAcademyAsync();
        var foreign = await other.NewSubjectAsync("Physics");

        var response = await academy.Admin.PostAsJsonAsync("/api/courses", new { name = "JEE", fee = 1, subjectIds = new[] { foreign } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Faculty_are_linked_to_subjects_and_teaching_time_can_record_one_of_them()
    {
        var academy = await factory.NewAcademyAsync();
        var physics = await academy.NewSubjectAsync("Physics");
        var chemistry = await academy.NewSubjectAsync("Chemistry");
        var created = await academy.Admin.PostAsJsonAsync("/api/faculty",
            new { fullName = "Neha", payType = "Hourly", hourlyRate = 600, subjectIds = new[] { physics } });
        var facultyId = (await created.ReadJsonAsync())["id"]!.GetValue<Guid>();

        var faculty = (await academy.Admin.GetJsonAsync("/api/faculty")).AsArray().Single()!;
        Assert.Equal(["Physics"], faculty["subjects"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));

        var notTheirs = await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60, subjectId = chemistry });
        var theirs = await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 60, subjectId = physics });
        var unspecified = await academy.Admin.PostAsJsonAsync("/api/faculty-sessions", new { facultyId, minutes = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, notTheirs.StatusCode);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unspecified.StatusCode);

        var log = (await academy.Admin.GetJsonAsync("/api/faculty-sessions"))["items"]!.AsArray();
        Assert.Contains(log, s => s!["subjectName"]?.GetValue<string>() == "Physics");

        // Unlinking later keeps the subject on sessions already logged.
        await academy.Admin.PutAsJsonAsync($"/api/faculty/{facultyId}",
            new { fullName = "Neha", payType = "Hourly", hourlyRate = 600, subjectIds = new[] { chemistry } });
        log = (await academy.Admin.GetJsonAsync("/api/faculty-sessions"))["items"]!.AsArray();
        Assert.Contains(log, s => s!["subjectName"]?.GetValue<string>() == "Physics");
    }
}
