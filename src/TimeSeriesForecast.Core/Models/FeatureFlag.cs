using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class FeatureFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    [MaxLength(400)]
    public string? Description { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
