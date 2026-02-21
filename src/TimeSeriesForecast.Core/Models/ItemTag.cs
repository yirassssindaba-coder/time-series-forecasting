namespace TimeSeriesForecast.Core.Models;

public sealed class ItemTag
{
    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }
}
