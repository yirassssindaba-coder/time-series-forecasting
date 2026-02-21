using System.ComponentModel.DataAnnotations;

namespace TimeSeriesForecast.Core.Models;

public sealed class Item
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Range(0, 1_000_000)]
    public decimal Price { get; set; }

    [MaxLength(40)]
    public string Status { get; set; } = ItemStatus.Draft;

    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    public List<ItemTag> ItemTags { get; set; } = new();

    // Lifecycle
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    // Optimistic locking (ETag)
    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ItemStatus
{
    public const string Draft = "draft";
    public const string Review = "review";
    public const string Published = "published";
    public const string Unpublished = "unpublished";
    public const string Cancelled = "cancelled";
    public const string Closed = "closed";
}
