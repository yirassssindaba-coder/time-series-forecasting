using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Workers;

public sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;

    public OutboxWorker(IServiceScopeFactory scopeFactory, IConfiguration cfg, IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _cfg = cfg;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch
            {
                // swallow worker errors
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        var url = _cfg["Webhooks:OutboundUrl"];
        if (string.IsNullOrWhiteSpace(url)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var http = _httpFactory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var msg = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (msg is null) return;

        try
        {
            var payload = new { id = msg.Id, type = msg.Type, payload = msg.PayloadJson, createdAt = msg.CreatedAt };
            var res = await http.PostAsJsonAsync(url, payload, ct);
            res.EnsureSuccessStatusCode();

            msg.ProcessedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            msg.Attempts += 1;
            if (msg.Attempts >= 5)
            {
                db.DeadLetterMessages.Add(new DeadLetterMessage
                {
                    Type = msg.Type,
                    PayloadJson = msg.PayloadJson,
                    Error = ex.GetType().Name + ": " + ex.Message,
                    DeadAt = now
                });
                db.OutboxMessages.Remove(msg);
            }
            else
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, msg.Attempts));
                msg.NextAttemptAt = now.Add(backoff);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
