using Academy.Api.Data;
using Academy.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Features;

/// <summary>
/// The price list on the public website, straight from the plans the owner manages: a plan shows there when it is
/// active and marked "show on website". A yearly plan whose code is the monthly plan's code plus "-yearly"
/// (basic → basic-yearly) appears as that plan's yearly option rather than as a card of its own.
/// </summary>
public static class PublicPlanEndpoints
{
    public const string YearlySuffix = "-yearly";

    public static void MapPublicPlanEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/public/plans", List).RequireRateLimiting(BrandingEndpoints.PublicRateLimitPolicy);

    private static async Task<IResult> List(AcademyDbContext db, HttpContext http, CancellationToken ct)
    {
        var plans = await db.Plans.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsActive && p.ShowOnWebsite)
            .OrderBy(p => p.Price)
            .ToListAsync(ct);
        var byCode = plans.ToDictionary(p => p.Code);

        var cards = new List<object>();
        foreach (var plan in plans)
        {
            // A yearly option is shown on its monthly plan's card.
            if (plan.Code.EndsWith(YearlySuffix) && byCode.ContainsKey(plan.Code[..^YearlySuffix.Length])) continue;

            byCode.TryGetValue(plan.Code + YearlySuffix, out var yearly);
            if (plan.Interval == BillingInterval.Yearly) yearly = null;
            var monthly = plan.Price / plan.Interval.Months();
            var monthsFree = yearly is not null && monthly > 0
                ? (int)Math.Round((12 * monthly - yearly.Price) / monthly, MidpointRounding.AwayFromZero)
                : 0;

            cards.Add(new
            {
                plan.Code,
                plan.Name,
                plan.Description,
                plan.Price,
                plan.Currency,
                plan.Interval,
                yearlyPrice = yearly?.Price,
                monthsFree = Math.Max(0, monthsFree),
                plan.MaxStudents,
                plan.MaxFaculty,
                plan.MaxUsers,
                plan.MaxReceiptEmailsPerMonth,
                plan.CustomBranding,
                plan.Highlighted
            });
        }

        // Prices change rarely; a short cache spares the database on busy days without hiding an edit for long.
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(cards);
    }
}
