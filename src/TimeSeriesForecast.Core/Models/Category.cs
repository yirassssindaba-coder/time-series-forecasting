using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Item> Items { get; set; } = new();
}
