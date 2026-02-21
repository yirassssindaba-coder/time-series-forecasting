using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(40)]
    public string Name { get; set; } = string.Empty;

    public List<ItemTag> ItemTags { get; set; } = new();
}
