using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Middleware;

public static class Idempotency
{
    public static async Task<IResult?> TryReplayAsync(HttpContext ctx, AppDbContext db, string route, CancellationToken ct)
    {
        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return null;

        var rec = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Key == key && x.Route == route, ct);
        if (rec is null) return null;

        // Return stored response
        return Results.Text(rec.ResponseBody, "application/json", statusCode: rec.StatusCode);
    }

    public static async Task StoreAsync(AppDbContext db, string route, string key, int statusCode, string body, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            Route = route,
            StatusCode = statusCode,
            ResponseBody = body
        });
        await db.SaveChangesAsync(ct);
    }
}
