using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(40)]
    public string Frequency { get; set; } = "daily"; // daily | weekly | monthly

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SeriesPoint> Points { get; set; } = new();
    public List<ForecastRun> ForecastRuns { get; set; } = new();
}

public sealed class SeriesPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
}

public sealed class ForecastRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = null!;

    [MaxLength(40)]
    public string Method { get; set; } = ForecastMethod.Ets;

    public int Horizon { get; set; } = 14;
    public int Holdout { get; set; } = 0;

    public double Mae { get; set; }
    public double Rmse { get; set; }

    // JSON (list of {timestamp,value})
    public string ForecastJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ForecastMethod
{
    public const string Ets = "ets";                // simple exponential smoothing
    public const string SeasonalNaive = "seasonal_naive";
}
