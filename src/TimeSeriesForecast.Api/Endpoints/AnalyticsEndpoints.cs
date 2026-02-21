using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapAnalytics(this RouteGroupBuilder group)
    {
        group.WithTags("analytics");

        group.MapPost("/analytics/events", Track).RequireAuthorization("analytics:write");
        group.MapGet("/analytics/events", List).RequireAuthorization("analytics:read");

        return group;
    }

    public sealed record TrackEventDto(string Name, Dictionary<string, string>? Props);

    private static async Task<IResult> Track(HttpContext ctx, AppDbContext db, TrackEventDto dto, CancellationToken ct)
    {
        var ev = new AnalyticsEvent
        {
            ActorUserId = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value ?? "",
            Name = dto.Name.Trim(),
            PropertiesJson = dto.Props is null ? null : System.Text.Json.JsonSerializer.Serialize(dto.Props)
        };
        db.AnalyticsEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true, ev.Id });
    }

    private static async Task<IResult> List(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.AnalyticsEvents.AsNoTracking().OrderByDescending(x => x.At).Take(200).ToListAsync(ct);
        return Results.Ok(rows);
    }
}
