using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Middleware;
using TimeSeriesForecast.Core.Common;
using TimeSeriesForecast.Core.Forecasting;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Endpoints;

public static class SeriesEndpoints
{
    public static RouteGroupBuilder MapSeries(this RouteGroupBuilder group)
    {
        group.WithTags("series");

        group.MapPost("/series", Create).RequireAuthorization("series:write");
        group.MapGet("/series", List).RequireAuthorization("series:read");
        group.MapGet("/series/{id:guid}", GetById).RequireAuthorization("series:read");
        group.MapDelete("/series/{id:guid}", Delete).RequireAuthorization("series:write");

        group.MapPost("/series/{id:guid}/points/import", ImportPoints).RequireAuthorization("series:write");
        group.MapGet("/series/{id:guid}/points", ListPoints).RequireAuthorization("series:read");

        group.MapPost("/series/{id:guid}/forecast", Forecast).RequireAuthorization("series:write");
        group.MapGet("/series/{id:guid}/forecasts", ListForecastRuns).RequireAuthorization("series:read");

        return group;
    }

    public sealed record CreateSeriesDto(string Name, string? Description, string? Frequency);

    private static async Task<IResult> Create(HttpContext ctx, AppDbContext db, CreateSeriesDto dto, CancellationToken ct)
    {
        var replay = await Idempotency.TryReplayAsync(ctx, db, "POST:/series", ct);
        if (replay is not null) return replay;

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "Name required" });

        var s = new Series { Name = name, Description = dto.Description?.Trim(), Frequency = (dto.Frequency ?? "daily").Trim().ToLowerInvariant() };
        db.Series.Add(s);
        await db.SaveChangesAsync(ct);

        db.OutboxMessages.Add(new OutboxMessage { Type = "series.created", PayloadJson = JsonSerializer.Serialize(new { s.Id, s.Name }) });
        await db.SaveChangesAsync(ct);

        var bodyObj = new { s.Id, s.Name, s.Description, s.Frequency, s.CreatedAt };
        var body = JsonSerializer.Serialize(bodyObj);

        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(key))
            await Idempotency.StoreAsync(db, "POST:/series", key!, 201, body, ct);

        return Results.Created($"/api/v1/series/{s.Id}", bodyObj);
    }

    private static async Task<IResult> List(AppDbContext db, HttpRequest req, CancellationToken ct)
    {
        var search = req.Query["search"].ToString().Trim();
        var q = db.Series.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.Name.Contains(search));
        var data = await q.OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
        return Results.Ok(data.Select(s => new { s.Id, s.Name, s.Description, s.Frequency, s.CreatedAt }));
    }

    private static async Task<IResult> GetById(AppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.Series.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? Results.NotFound() : Results.Ok(s);
    }

    private static async Task<IResult> Delete(AppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.Series.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();
        db.Series.Remove(s);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ImportPoints(HttpContext ctx, AppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.Series.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();

        if (!ctx.Request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data required" });
        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null) return Results.BadRequest(new { error = "file required" });

        using var stream = file.OpenReadStream();
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        List<ForecastEngine.ForecastPoint> points;
        if (ext == ".json")
        {
            points = await JsonSerializer.DeserializeAsync<List<ForecastEngine.ForecastPoint>>(stream, cancellationToken: ct) ?? new();
        }
        else
        {
            points = (await CsvTimeSeriesReader.ReadAsync(stream)).ToList();
        }

        // replace or append based on ?mode=replace
        var mode = ctx.Request.Query["mode"].ToString().ToLowerInvariant();
        using var tx = await db.Database.BeginTransactionAsync(ct);

        if (mode == "replace")
        {
            var old = db.SeriesPoints.Where(p => p.SeriesId == id);
            db.SeriesPoints.RemoveRange(old);
        }

        foreach (var p in points)
        {
            db.SeriesPoints.Add(new SeriesPoint { SeriesId = id, Timestamp = p.Timestamp, Value = p.Value });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { imported = points.Count, mode = string.IsNullOrWhiteSpace(mode) ? "append" : mode });
    }

    private static async Task<IResult> ListPoints(AppDbContext db, Guid id, HttpRequest req, CancellationToken ct)
    {
        int take = int.TryParse(req.Query["take"], out var t) ? Math.Clamp(t, 1, 5000) : 2000;
        var data = await db.SeriesPoints.AsNoTracking()
            .Where(p => p.SeriesId == id)
            .OrderBy(p => p.Timestamp)
            .Take(take)
            .ToListAsync(ct);
        return Results.Ok(data);
    }

    public sealed record ForecastDto(string Method, int Horizon, int Holdout, double? Alpha, int? SeasonLength);

    private static async Task<IResult> Forecast(AppDbContext db, IFeatureFlags flags, Guid id, ForecastDto dto, CancellationToken ct)
    {
        if (!await flags.IsEnabledAsync("series.forecast", ct))
            return Results.StatusCode(403);

        var history = await db.SeriesPoints.AsNoTracking()
            .Where(p => p.SeriesId == id)
            .OrderBy(p => p.Timestamp)
            .Select(p => new ForecastEngine.ForecastPoint(p.Timestamp, p.Value))
            .ToListAsync(ct);

        if (history.Count < 3) return Results.BadRequest(new { error = "Not enough data points" });

        int horizon = Math.Clamp(dto.Horizon, 1, 3650);
        int holdout = Math.Clamp(dto.Holdout, 0, Math.Min(3650, history.Count - 1));

        var train = holdout > 0 ? history.Take(history.Count - holdout).ToList() : history;
        var actual = holdout > 0 ? history.Skip(history.Count - holdout).Select(p => p.Value).ToList() : new List<double>();

        IReadOnlyList<ForecastEngine.ForecastPoint> forecast;
        var method = dto.Method.Trim().ToLowerInvariant();
        switch (method)
        {
            case "ets":
            case "exp":
                forecast = ForecastEngine.ExponentialSmoothing(train, horizon, dto.Alpha ?? 0.35);
                break;
            case "seasonalnaive":
            case "snaive":
            default:
                forecast = ForecastEngine.SeasonalNaive(train, horizon, dto.SeasonLength ?? 7);
                method = "seasonalnaive";
                break;
        }

        double mae = 0, rmse = 0;
        if (holdout > 0)
        {
            // compare last holdout steps (align by count)
            var predHoldout = forecast.Take(holdout).Select(p => p.Value).ToList();
            var (m, r) = ForecastEngine.Evaluate(actual.Take(predHoldout.Count).ToList(), predHoldout);
            mae = m; rmse = r;
        }

        var run = new ForecastRun
        {
            SeriesId = id,
            Method = method,
            Horizon = horizon,
            Holdout = holdout,
            Mae = mae,
            Rmse = rmse,
            ForecastJson = ForecastEngine.ToJson(forecast)
        };
        db.ForecastRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            run.Id,
            run.Method,
            run.Horizon,
            run.Holdout,
            run.Mae,
            run.Rmse,
            forecast
        });
    }

    private static async Task<IResult> ListForecastRuns(AppDbContext db, Guid id, CancellationToken ct)
    {
        var rows = await db.ForecastRuns.AsNoTracking()
            .Where(r => r.SeriesId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new { r.Id, r.Method, r.Horizon, r.Holdout, r.Mae, r.Rmse, r.CreatedAt }));
    }
}
