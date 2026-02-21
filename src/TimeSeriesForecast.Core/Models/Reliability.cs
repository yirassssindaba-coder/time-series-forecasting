using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Route { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string ResponseBody { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class DeadLetterMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    [MaxLength(2000)]
    public string? Error { get; set; }

    public DateTimeOffset DeadAt { get; set; } = DateTimeOffset.UtcNow;
}
