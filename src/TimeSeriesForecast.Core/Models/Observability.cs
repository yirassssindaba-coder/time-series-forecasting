using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class ActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(80)]
    public string ActorUserId { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Method { get; set; } = string.Empty;

    [MaxLength(400)]
    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public int DurationMs { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

public sealed class AnalyticsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(80)]
    public string ActorUserId { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public string? PropertiesJson { get; set; }
}
