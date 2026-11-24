using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Academy.Api.Tests.Support;

namespace Academy.Api.Tests;

[Collection(AppCollection.Name)]
public sealed class PublicPlanTests(AppFactory factory)
{
    private static JsonNode Card(JsonArray cards, string code) => cards.Single(c => c!["code"]!.GetValue<string>() == code)!;

    [Fact]
    public async Task The_website_price_list_comes_from_the_owners_plans()
    {
        var visitor = factory.NewBrowser();
        var cards = (await visitor.GetJsonAsync("/api/public/plans")).AsArray();

        // Yearly plans are folded into their monthly plan's card, with the months saved.
        Assert.DoesNotContain(cards, c => c!["code"]!.GetValue<string>().EndsWith("-yearly"));
        var basic = Card(cards, "basic");
        Assert.Equal(199, basic.Money("price"));
        Assert.Equal(1999, basic.Money("yearlyPrice"));
        Assert.Equal(2, basic["monthsFree"]!.GetValue<int>());
        Assert.True(basic["highlighted"]!.GetValue<bool>());
        Assert.Equal(75, basic["maxStudents"]!.GetValue<int>());
        var free = Card(cards, "free");
        Assert.Equal(0, free.Money("price"));
        Assert.Null(free["yearlyPrice"]);
        Assert.False(free["customBranding"]!.GetValue<bool>());
        // Cheapest first.
        Assert.Equal("free", cards[0]!["code"]!.GetValue<string>());

        // A new plan shows up; hiding it from the website or deactivating it takes it off again.
        var owner = await factory.OwnerAsync();
        var code = $"t{Guid.NewGuid():N}"[..12];
        var created = await owner.PostAsJsonAsync("/api/platform/plans", new
        {
            code, name = "Institute", description = "For multi-branch institutes.", price = 1499, interval = "Monthly",
            maxStudents = 1000, maxUsers = 15, maxFaculty = 60, highlighted = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync())["id"]!.GetValue<Guid>();
        var withNew = (await visitor.GetJsonAsync("/api/public/plans")).AsArray();
        Assert.Equal(1499, Card(withNew, code).Money("price"));
        Assert.Null(Card(withNew, code)["maxReceiptEmailsPerMonth"]);

        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/platform/plans/{id}", new
        {
            code, name = "Institute", price = 1499, interval = "Monthly", maxStudents = 1000, showOnWebsite = false
        })).StatusCode);
        Assert.DoesNotContain((await visitor.GetJsonAsync("/api/public/plans")).AsArray(), c => c!["code"]!.GetValue<string>() == code);
        var listed = (await owner.GetJsonAsync("/api/platform/plans")).AsArray().Single(p => p!["code"]!.GetValue<string>() == code)!;
        Assert.False(listed["showOnWebsite"]!.GetValue<bool>());
    }
}
