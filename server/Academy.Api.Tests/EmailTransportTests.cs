using System.Net;
using System.Text.Json.Nodes;
using Academy.Api.Domain;
using Academy.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Academy.Api.Tests;

/// <summary>What is actually sent to the email services, without calling them.</summary>
public sealed class EmailTransportTests
{
    private sealed class Capture(HttpStatusCode status, string reply) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }
    }

    private static readonly EmailMessage Message = new() { ToAddress = "parent@example.com", Subject = "Fee reminder", Body = "Please pay.", Category = "FeeReminder" };

    [Fact]
    public async Task Resend_gets_the_academy_name_its_reply_address_and_the_key()
    {
        var capture = new Capture(HttpStatusCode.OK, """{"id":"abc"}""");
        var options = Options.Create(new EmailOptions { ResendApiKey = "re_test_key", FromAddress = "noreply@academyflow.in" });
        var transport = new ResendEmailTransport(new HttpClient(capture), options);

        await transport.SendAsync(Message, new EmailSender("Riverside Tutors", "office@riverside.test"), default);

        Assert.Equal(ResendEmailTransport.Endpoint, capture.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer re_test_key", capture.Request.Headers.Authorization!.ToString());
        var body = JsonNode.Parse(capture.Body)!;
        Assert.Equal("\"Riverside Tutors\" <noreply@academyflow.in>", body["from"]!.GetValue<string>());
        Assert.Equal("parent@example.com", body["to"]![0]!.GetValue<string>());
        Assert.Equal("office@riverside.test", body["reply_to"]!.GetValue<string>());
        Assert.Equal("Please pay.", body["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_refusal_from_Resend_is_reported_with_its_reason()
    {
        var capture = new Capture(HttpStatusCode.Forbidden, """{"statusCode":403,"message":"The academyflow.in domain is not verified."}""");
        var transport = new ResendEmailTransport(new HttpClient(capture), Options.Create(new EmailOptions { ResendApiKey = "re_x", FromAddress = "noreply@academyflow.in" }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(Message, null, default));
        Assert.Contains("domain is not verified", error.Message);
    }

    [Fact]
    public void Resend_is_used_ahead_of_Brevo_and_SMTP()
    {
        var both = new EmailOptions { ResendApiKey = "re_x", BrevoApiKey = "xkeysib", Host = "smtp.example.com", FromAddress = "a@b.c" };
        Assert.True(both.IsConfigured);
        Assert.True(both.UsesResend);
        Assert.False(both.UsesBrevo);
        Assert.Equal("Resend API", both.ServiceName);
        Assert.False(new EmailOptions { ResendApiKey = "re_x" }.IsConfigured);
    }
}
