using System.Diagnostics;
using System.Text.Json;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var sw = Stopwatch.StartNew();
        string? error = null;
        int statusCode = 0;

        try
        {
            await _next(ctx);
            statusCode = ctx.Response.StatusCode;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            statusCode = 500;
            throw;
        }
        finally
        {
            sw.Stop();
            var uid = ctx.User?.Claims?.FirstOrDefault(c => c.Type == "uid")?.Value ?? "";

            // Avoid logging swagger & health noise
            var path = ctx.Request.Path.ToString();
            if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                db.ActivityLogs.Add(new ActivityLog
                {
                    ActorUserId = uid,
                    Method = ctx.Request.Method,
                    Path = path,
                    StatusCode = statusCode,
                    DurationMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds),
                    Error = error
                });

                try { await db.SaveChangesAsync(ctx.RequestAborted); } catch { /* ignore logging failures */ }
            }
        }
    }
}

public static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLoggingMiddleware>();
}
