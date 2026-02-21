using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Middleware;
using TimeSeriesForecast.Api.Security;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Endpoints;

public static class ItemsEndpoints
{
    public static RouteGroupBuilder MapItems(this RouteGroupBuilder group)
    {
        group.WithTags("items");

        group.MapPost("/items", CreateOne)
            .RequireAuthorization("items:write");

        group.MapPost("/items/bulk", CreateBulk)
            .RequireAuthorization("items:write");

        group.MapGet("/items", List)
            .RequireAuthorization("items:read");

        group.MapGet("/items/{id:guid}", GetById)
            .RequireAuthorization("items:read");

        group.MapMethods("/items/{id:guid}", new[] { "HEAD" }, Exists)
            .RequireAuthorization("items:read");

        group.MapGet("/items/by-ids", GetManyByIds)
            .RequireAuthorization("items:read");

        group.MapPut("/items/{id:guid}", UpdateFull)
            .RequireAuthorization("items:write");

        group.MapPatch("/items/{id:guid}", UpdatePartial)
            .RequireAuthorization("items:write");

        group.MapPatch("/items/bulk", BulkUpdate)
            .RequireAuthorization("items:write");

        group.MapDelete("/items/{id:guid}", DeleteOne)
            .RequireAuthorization("items:delete");

        group.MapDelete("/items/bulk", BulkDelete)
            .RequireAuthorization("items:delete");

        group.MapDelete("/items", DeleteByQuery)
            .RequireAuthorization("admin:manage");

        group.MapPost("/items/{id:guid}/restore", Restore)
            .RequireAuthorization("items:write");

        group.MapPost("/items/purge", Purge)
            .RequireAuthorization("admin:manage");

        group.MapPost("/items/{id:guid}/archive", Archive)
            .RequireAuthorization("items:write");

        group.MapPost("/items/{id:guid}/unarchive", Unarchive)
            .RequireAuthorization("items:write");

        group.MapGet("/items/count", Count)
            .RequireAuthorization("items:read");

        group.MapGet("/items/distinct", Distinct)
            .RequireAuthorization("items:read");

        group.MapGet("/items/aggregate", Aggregate)
            .RequireAuthorization("items:read");

        group.MapGet("/items/report", Report)
            .RequireAuthorization("items:read");

        group.MapGet("/items/export", Export)
            .RequireAuthorization("items:read");

        group.MapPost("/items/import", Import)
            .RequireAuthorization("items:write");

        group.MapPost("/items/upsert", Upsert)
            .RequireAuthorization("items:write");

        group.MapPost("/items/deduplicate", Deduplicate)
            .RequireAuthorization("admin:manage");

        // Relations: tags
        group.MapPost("/items/{id:guid}/tags/attach", AttachTags)
            .RequireAuthorization("items:write");
        group.MapPost("/items/{id:guid}/tags/detach", DetachTags)
            .RequireAuthorization("items:write");
        group.MapPost("/items/{id:guid}/tags/sync", SyncTags)
            .RequireAuthorization("items:write");

        // Workflow actions
        group.MapPost("/items/{id:guid}/actions/{action}", WorkflowAction)
            .RequireAuthorization("items:write");

        return group;
    }

    public sealed record ItemCreateDto(string Name, string? Description, decimal Price, Guid? CategoryId);
    public sealed record ItemUpdateDto(string Name, string? Description, decimal Price, Guid? CategoryId, string Status, bool IsActive, bool IsVerified);
    public sealed record ItemPatchDto(string? Name, string? Description, decimal? Price, Guid? CategoryId, string? Status, bool? IsActive, bool? IsVerified);
    public sealed record BulkDto<T>(List<T> Items);

    public sealed record TagIdsDto(List<Guid> TagIds);

    private static IQueryable<Item> ApplyQuery(IQueryable<Item> q, HttpRequest req)
    {
        // filter & search
        var search = req.Query["search"].ToString().Trim();
        var status = req.Query["status"].ToString().Trim();
        var tag = req.Query["tag"].ToString().Trim();

        var includeDeleted = req.Query["includeDeleted"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        var includeArchived = req.Query["includeArchived"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!includeDeleted) q = q.Where(x => !x.IsDeleted);
        if (!includeArchived) q = q.Where(x => !x.IsArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(x => x.Name.Contains(search) || (x.Description != null && x.Description.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(x => x.Status == status);
        }

        if (Guid.TryParse(req.Query["categoryId"], out var categoryId))
        {
            q = q.Where(x => x.CategoryId == categoryId);
        }

        if (bool.TryParse(req.Query["isActive"], out var isActive))
        {
            q = q.Where(x => x.IsActive == isActive);
        }

        if (decimal.TryParse(req.Query["minPrice"], out var minPrice))
        {
            q = q.Where(x => x.Price >= minPrice);
        }

        if (decimal.TryParse(req.Query["maxPrice"], out var maxPrice))
        {
            q = q.Where(x => x.Price <= maxPrice);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            q = q.Where(x => x.ItemTags.Any(it => it.Tag != null && it.Tag.Name == tag));
        }

        // sorting (single/multi)
        var sort = req.Query["sort"].ToString();
        if (!string.IsNullOrWhiteSpace(sort))
        {
            IOrderedQueryable<Item>? ordered = null;
            foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var desc = part.StartsWith('-');
                var key = desc ? part[1..] : part;

                Func<IQueryable<Item>, IOrderedQueryable<Item>> apply = key.ToLowerInvariant() switch
                {
                    "name" => src => desc ? src.OrderByDescending(x => x.Name) : src.OrderBy(x => x.Name),
                    "price" => src => desc ? src.OrderByDescending(x => x.Price) : src.OrderBy(x => x.Price),
                    "createdat" => src => desc ? src.OrderByDescending(x => x.CreatedAt) : src.OrderBy(x => x.CreatedAt),
                    "updatedat" => src => desc ? src.OrderByDescending(x => x.UpdatedAt) : src.OrderBy(x => x.UpdatedAt),
                    _ => src => desc ? src.OrderByDescending(x => x.CreatedAt) : src.OrderBy(x => x.CreatedAt)
                };

                if (ordered is null)
                {
                    ordered = apply(q);
                }
                else
                {
                    ordered = key.ToLowerInvariant() switch
                    {
                        "name" => desc ? ordered.ThenByDescending(x => x.Name) : ordered.ThenBy(x => x.Name),
                        "price" => desc ? ordered.ThenByDescending(x => x.Price) : ordered.ThenBy(x => x.Price),
                        "createdat" => desc ? ordered.ThenByDescending(x => x.CreatedAt) : ordered.ThenBy(x => x.CreatedAt),
                        "updatedat" => desc ? ordered.ThenByDescending(x => x.UpdatedAt) : ordered.ThenBy(x => x.UpdatedAt),
                        _ => desc ? ordered.ThenByDescending(x => x.CreatedAt) : ordered.ThenBy(x => x.CreatedAt)
                    };
                }
            }

            if (ordered is not null) q = ordered;
        }
        else
        {
            q = q.OrderByDescending(x => x.CreatedAt);
        }

        // cursor pagination (preferred if provided)
        // Implementation uses CreatedAt to keep translation simple for SQLite.
        if (req.Query.TryGetValue("cursor", out var cursorValue) && !string.IsNullOrWhiteSpace(cursorValue))
        {
            if (TryDecodeCursor(cursorValue!, out var cursorAt))
            {
                // In cursor mode, force stable ordering by CreatedAt desc.
                q = q.OrderByDescending(x => x.CreatedAt);
                q = q.Where(x => x.CreatedAt < cursorAt);
            }

            int size = int.TryParse(req.Query["size"], out var s) ? Math.Clamp(s, 1, 200) : 20;
            q = q.Take(size);
            return q;
        }

        // page/size
        if (int.TryParse(req.Query["page"], out var page) && int.TryParse(req.Query["size"], out var pageSize))
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            q = q.Skip((page - 1) * pageSize).Take(pageSize);
            return q;
        }

        // limit/offset
        if (int.TryParse(req.Query["offset"], out var offset) || int.TryParse(req.Query["limit"], out var limit))
        {
            offset = Math.Max(0, offset);
            limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 200) : 20;
            q = q.Skip(offset).Take(limit);
            return q;
        }

        // default
        q = q.Take(20);
        return q;
    }

    private static string EncodeCursor(DateTimeOffset createdAt)
    {
        var raw = createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset createdAt)
    {
        createdAt = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            {
                createdAt = dt.ToUniversalTime();
                return true;
            }
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
            {
                createdAt = DateTimeOffset.FromUnixTimeSeconds(unix);
                return true;
            }
        }
        catch
        {
            // ignored
        }
        return false;
    }

    private static object Shape(Item item, string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
        {
            return new
            {
                item.Id,
                item.Name,
                item.Description,
                item.Price,
                item.Status,
                item.IsActive,
                item.IsVerified,
                item.CategoryId,
                category = item.Category is null ? null : new { item.Category.Id, item.Category.Name },
                tags = item.ItemTags.Where(it => it.Tag != null).Select(it => new { it.Tag!.Id, it.Tag!.Name }),
                item.IsDeleted,
                item.IsArchived,
                item.Version,
                item.CreatedAt,
                item.UpdatedAt
            };
        }

        var fields = select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Select(f => f.ToLowerInvariant())
                           .ToHashSet();

        var dict = new Dictionary<string, object?>();
        void Add(string key, object? value) { if (fields.Contains(key.ToLowerInvariant())) dict[key] = value; }

        Add("id", item.Id);
        Add("name", item.Name);
        Add("description", item.Description);
        Add("price", item.Price);
        Add("status", item.Status);
        Add("isActive", item.IsActive);
        Add("isVerified", item.IsVerified);
        Add("categoryId", item.CategoryId);
        Add("version", item.Version);
        Add("createdAt", item.CreatedAt);
        Add("updatedAt", item.UpdatedAt);
        Add("isDeleted", item.IsDeleted);
        Add("isArchived", item.IsArchived);

        if (fields.Contains("category"))
            dict["category"] = item.Category is null ? null : new { item.Category.Id, item.Category.Name };
        if (fields.Contains("tags"))
            dict["tags"] = item.ItemTags.Where(it => it.Tag != null).Select(it => new { it.Tag!.Id, it.Tag!.Name });

        return dict;
    }

    private static string MakeEtag(Item item) => $"W/\"{item.Version}\"";

    private static bool CheckIfMatch(HttpRequest req, Item item)
    {
        var ifMatch = req.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ifMatch)) return true; // allow if not supplied

        return string.Equals(ifMatch.Trim(), MakeEtag(item), StringComparison.Ordinal);
    }

    private static async Task<IResult> CreateOne(HttpContext ctx, AppDbContext db, ItemCreateDto dto, CancellationToken ct)
    {
        var replay = await Idempotency.TryReplayAsync(ctx, db, "POST:/items", ct);
        if (replay is not null) return replay;

        dto = dto with { Name = dto.Name.Trim(), Description = dto.Description?.Trim() };
        if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Name is required" });

        var item = new Item
        {
            Name = dto.Name,
            Description = dto.Description,
            Price = dto.Price,
            CategoryId = dto.CategoryId,
            Status = ItemStatus.Draft
        };

        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        // outbox event
        db.OutboxMessages.Add(new OutboxMessage { Type = "item.created", PayloadJson = JsonSerializer.Serialize(new { item.Id, item.Name }) });
        await db.SaveChangesAsync(ct);

        var bodyObj = Shape(item, select: null);
        var body = JsonSerializer.Serialize(bodyObj);

        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(key))
            await Idempotency.StoreAsync(db, "POST:/items", key!, 201, body, ct);

        return Results.Created($"/api/v1/items/{item.Id}", bodyObj);
    }

    private static async Task<IResult> CreateBulk(HttpContext ctx, AppDbContext db, BulkDto<ItemCreateDto> bulk, CancellationToken ct)
    {
        var replay = await Idempotency.TryReplayAsync(ctx, db, "POST:/items/bulk", ct);
        if (replay is not null) return replay;

        if (bulk.Items.Count == 0) return Results.BadRequest(new { error = "Empty items" });

        using var tx = await db.Database.BeginTransactionAsync(ct);

        var created = new List<Item>();
        foreach (var dto in bulk.Items)
        {
            var name = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var item = new Item
            {
                Name = name,
                Description = dto.Description?.Trim(),
                Price = dto.Price,
                CategoryId = dto.CategoryId,
                Status = ItemStatus.Draft
            };
            db.Items.Add(item);
            created.Add(item);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var bodyObj = created.Select(i => Shape(i, null));
        var body = JsonSerializer.Serialize(bodyObj);

        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(key))
            await Idempotency.StoreAsync(db, "POST:/items/bulk", key!, 201, body, ct);

        return Results.Created("/api/v1/items", bodyObj);
    }

    private static async Task<IResult> List(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var include = req.Query["include"].ToString();
        var select = req.Query["select"].ToString();

        var q = db.Items.AsQueryable();

        // include/expand relations
        if (include.Contains("category", StringComparison.OrdinalIgnoreCase))
            q = q.Include(i => i.Category);
        if (include.Contains("tags", StringComparison.OrdinalIgnoreCase))
            q = q.Include(i => i.ItemTags).ThenInclude(it => it.Tag);

        q = ApplyQuery(q, req);

        var list = await q.AsNoTracking().ToListAsync(ct);
        var shaped = list.Select(i => Shape(i, select)).ToList();

        // Cursor token for the next page (base64 of CreatedAt in round-trip format)
        var nextCursor = list.Count == 0 ? null : EncodeCursor(list[^1].CreatedAt);
        return Results.Ok(new { data = shaped, nextCursor });
    }

    private static async Task<IResult> GetById(HttpContext ctx, AppDbContext db, Guid id, CancellationToken ct)
    {
        var include = ctx.Request.Query["include"].ToString();
        var select = ctx.Request.Query["select"].ToString();

        var q = db.Items.AsQueryable();
        if (include.Contains("category", StringComparison.OrdinalIgnoreCase))
            q = q.Include(i => i.Category);
        if (include.Contains("tags", StringComparison.OrdinalIgnoreCase))
            q = q.Include(i => i.ItemTags).ThenInclude(it => it.Tag);

        var item = await q.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        ctx.Response.Headers.ETag = MakeEtag(item);
        return Results.Ok(Shape(item, select));
    }

    private static async Task<IResult> Exists(AppDbContext db, Guid id, CancellationToken ct)
    {
        var ok = await db.Items.AnyAsync(i => i.Id == id, ct);
        return ok ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> GetManyByIds(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var idsRaw = req.Query["ids"].ToString();
        var ids = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        if (ids.Count == 0) return Results.BadRequest(new { error = "ids required" });

        var list = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        return Results.Ok(list.Select(i => Shape(i, null)));
    }

    private static async Task<IResult> UpdateFull(HttpContext ctx, AppDbContext db, Guid id, ItemUpdateDto dto, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.ItemTags).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (!CheckIfMatch(ctx.Request, item)) return Results.StatusCode(412);

        var before = JsonSerializer.Serialize(item);

        item.Name = dto.Name.Trim();
        item.Description = dto.Description?.Trim();
        item.Price = dto.Price;
        item.CategoryId = dto.CategoryId;
        item.Status = dto.Status.Trim().ToLowerInvariant();
        item.IsActive = dto.IsActive;
        item.IsVerified = dto.IsVerified;
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        db.ActivityLogs.Add(new ActivityLog { ActorUserId = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value ?? "", Method = "AUDIT", Path = $"items/{id}", StatusCode = 200, DurationMs = 0, BeforeJson = before, AfterJson = JsonSerializer.Serialize(item) });
        await db.SaveChangesAsync(ct);

        ctx.Response.Headers.ETag = MakeEtag(item);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> UpdatePartial(HttpContext ctx, AppDbContext db, Guid id, ItemPatchDto dto, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (!CheckIfMatch(ctx.Request, item)) return Results.StatusCode(412);

        var before = JsonSerializer.Serialize(item);

        if (dto.Name is not null) item.Name = dto.Name.Trim();
        if (dto.Description is not null) item.Description = dto.Description.Trim();
        if (dto.Price.HasValue) item.Price = dto.Price.Value;
        if (dto.CategoryId.HasValue) item.CategoryId = dto.CategoryId.Value;
        if (dto.Status is not null) item.Status = dto.Status.Trim().ToLowerInvariant();
        if (dto.IsActive.HasValue) item.IsActive = dto.IsActive.Value;
        if (dto.IsVerified.HasValue) item.IsVerified = dto.IsVerified.Value;

        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        db.ActivityLogs.Add(new ActivityLog { ActorUserId = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value ?? "", Method = "AUDIT", Path = $"items/{id}", StatusCode = 200, DurationMs = 0, BeforeJson = before, AfterJson = JsonSerializer.Serialize(item) });
        await db.SaveChangesAsync(ct);

        ctx.Response.Headers.ETag = MakeEtag(item);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> BulkUpdate(AppDbContext db, BulkDto<(Guid id, ItemPatchDto patch)> bulk, CancellationToken ct)
    {
        if (bulk.Items.Count == 0) return Results.BadRequest(new { error = "Empty" });

        using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var (id, patch) in bulk.Items)
        {
            var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) continue;

            if (patch.Name is not null) item.Name = patch.Name.Trim();
            if (patch.Description is not null) item.Description = patch.Description.Trim();
            if (patch.Price.HasValue) item.Price = patch.Price.Value;
            if (patch.CategoryId.HasValue) item.CategoryId = patch.CategoryId.Value;
            if (patch.Status is not null) item.Status = patch.Status.Trim().ToLowerInvariant();
            if (patch.IsActive.HasValue) item.IsActive = patch.IsActive.Value;
            if (patch.IsVerified.HasValue) item.IsVerified = patch.IsVerified.Value;

            item.Version += 1;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> DeleteOne(HttpRequest req, AppDbContext db, Guid id, CancellationToken ct)
    {
        var force = req.Query["force"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        if (force)
        {
            db.Items.Remove(item);
        }
        else
        {
            item.IsDeleted = true;
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.Version += 1;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BulkDelete(AppDbContext db, HttpRequest req, CancellationToken ct)
    {
        var idsRaw = req.Query["ids"].ToString();
        var ids = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        if (ids.Count == 0) return Results.BadRequest(new { error = "ids required" });

        var force = req.Query["force"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        using var tx = await db.Database.BeginTransactionAsync(ct);

        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (force)
        {
            db.Items.RemoveRange(items);
        }
        else
        {
            foreach (var it in items)
            {
                it.IsDeleted = true;
                it.DeletedAt = DateTimeOffset.UtcNow;
                it.Version += 1;
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { deleted = items.Count, force });
    }

    private static async Task<IResult> DeleteByQuery(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var q = ApplyQuery(db.Items.AsQueryable(), req);
        var items = await q.ToListAsync(ct);
        foreach (var it in items)
        {
            it.IsDeleted = true;
            it.DeletedAt = DateTimeOffset.UtcNow;
            it.Version += 1;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deleted = items.Count });
    }

    private static async Task<IResult> Restore(AppDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        item.IsDeleted = false;
        item.DeletedAt = null;
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> Purge(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        int days = int.TryParse(req.Query["days"], out var d) ? Math.Clamp(d, 1, 3650) : 30;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var toRemove = await db.Items.Where(i => i.IsDeleted && i.DeletedAt != null && i.DeletedAt < cutoff).ToListAsync(ct);
        db.Items.RemoveRange(toRemove);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { purged = toRemove.Count, cutoff });
    }

    private static async Task<IResult> Archive(AppDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();
        item.IsArchived = true;
        item.ArchivedAt = DateTimeOffset.UtcNow;
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> Unarchive(AppDbContext db, Guid id, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();
        item.IsArchived = false;
        item.ArchivedAt = null;
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> Count(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var q = ApplyQuery(db.Items.AsQueryable(), req);
        var count = await q.CountAsync(ct);
        return Results.Ok(new { count });
    }

    private static async Task<IResult> Distinct(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var field = req.Query["field"].ToString().ToLowerInvariant();
        var q = ApplyQuery(db.Items.AsQueryable(), req);

        object data = field switch
        {
            "status" => await q.Select(i => i.Status).Distinct().ToListAsync(ct),
            "categoryid" => await q.Select(i => i.CategoryId).Distinct().ToListAsync(ct),
            _ => await q.Select(i => i.Status).Distinct().ToListAsync(ct)
        };

        return Results.Ok(new { field, data });
    }

    private static async Task<IResult> Aggregate(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var field = req.Query["field"].ToString().ToLowerInvariant();
        var op = req.Query["op"].ToString().ToLowerInvariant();
        var groupBy = req.Query["groupBy"].ToString().ToLowerInvariant();

        var q = ApplyQuery(db.Items.AsQueryable(), req);

        if (field != "price") return Results.BadRequest(new { error = "Only field=price supported in template" });

        if (groupBy == "categoryid")
        {
            object grouped;

            if (op == "sum")
            {
                grouped = await q.GroupBy(i => i.CategoryId)
                    .Select(g => new { categoryId = g.Key, value = g.Sum(x => x.Price) })
                    .ToListAsync(ct);
            }
            else if (op == "avg")
            {
                grouped = await q.GroupBy(i => i.CategoryId)
                    .Select(g => new { categoryId = g.Key, value = g.Average(x => x.Price) })
                    .ToListAsync(ct);
            }
            else if (op == "min")
            {
                grouped = await q.GroupBy(i => i.CategoryId)
                    .Select(g => new { categoryId = g.Key, value = g.Min(x => x.Price) })
                    .ToListAsync(ct);
            }
            else if (op == "max")
            {
                grouped = await q.GroupBy(i => i.CategoryId)
                    .Select(g => new { categoryId = g.Key, value = g.Max(x => x.Price) })
                    .ToListAsync(ct);
            }
            else
            {
                grouped = await q.GroupBy(i => i.CategoryId)
                    .Select(g => new { categoryId = g.Key, value = g.Sum(x => x.Price) })
                    .ToListAsync(ct);
            }

            return Results.Ok(new { field, op, groupBy, data = grouped });
        }

        var single = op switch
        {
            "sum" => (decimal)await q.SumAsync(x => x.Price, ct),
            "avg" => (decimal)await q.AverageAsync(x => x.Price, ct),
            "min" => (decimal)await q.MinAsync(x => x.Price, ct),
            "max" => (decimal)await q.MaxAsync(x => x.Price, ct),
            _ => (decimal)await q.SumAsync(x => x.Price, ct)
        };

        return Results.Ok(new { field, op, value = single });
    }

    private static async Task<IResult> Report(HttpRequest req, AppDbContext db, CancellationToken ct)
    {
        var by = req.Query["by"].ToString().ToLowerInvariant();
        var q = ApplyQuery(db.Items.AsQueryable(), req);

        if (by == "status")
        {
            var rows = await q.GroupBy(i => i.Status)
                .Select(g => new { status = g.Key, count = g.Count(), sumPrice = g.Sum(x => x.Price) })
                .ToListAsync(ct);
            return Results.Ok(new { by, rows });
        }

        // default category
        var rows2 = await q.GroupBy(i => i.CategoryId)
            .Select(g => new { categoryId = g.Key, count = g.Count(), sumPrice = g.Sum(x => x.Price) })
            .ToListAsync(ct);
        return Results.Ok(new { by = "categoryId", rows = rows2 });
    }

    private static async Task<IResult> Export(HttpContext ctx, AppDbContext db, IFeatureFlags flags, CancellationToken ct)
    {
        var format = ctx.Request.Query["format"].ToString().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(format)) format = "csv";

        var q = ApplyQuery(db.Items.Include(i => i.Category).Include(i => i.ItemTags).ThenInclude(it => it.Tag).AsQueryable(), ctx.Request);
        var items = await q.AsNoTracking().ToListAsync(ct);

        return format switch
        {
            "csv" => ExportCsv(items),
            "xlsx" => ExportXlsx(items),
            "pdf" => await ExportPdfAsync(items, flags, ct),
            _ => ExportCsv(items)
        };
    }

    private static IResult ExportCsv(List<Item> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,name,price,status,category,tags,createdAt,updatedAt");
        foreach (var i in items)
        {
            var cat = i.Category?.Name ?? "";
            var tags = string.Join('|', i.ItemTags.Where(t => t.Tag != null).Select(t => t.Tag!.Name));
            sb.AppendLine($"{i.Id},{Escape(i.Name)},{i.Price},{i.Status},{Escape(cat)},{Escape(tags)},{i.CreatedAt:O},{i.UpdatedAt:O}");
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Results.File(bytes, "text/csv", "items_export.csv");

        static string Escape(string s) => '"' + s.Replace("\"", "\"\"") + '"';
    }

    private static IResult ExportXlsx(List<Item> items)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Items");
        ws.Cell(1, 1).Value = "Id";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Price";
        ws.Cell(1, 4).Value = "Status";
        ws.Cell(1, 5).Value = "Category";
        ws.Cell(1, 6).Value = "Tags";
        ws.Cell(1, 7).Value = "CreatedAt";
        ws.Cell(1, 8).Value = "UpdatedAt";

        int r = 2;
        foreach (var i in items)
        {
            ws.Cell(r, 1).Value = i.Id.ToString();
            ws.Cell(r, 2).Value = i.Name;
            ws.Cell(r, 3).Value = (double)i.Price;
            ws.Cell(r, 4).Value = i.Status;
            ws.Cell(r, 5).Value = i.Category?.Name ?? "";
            ws.Cell(r, 6).Value = string.Join(", ", i.ItemTags.Where(t => t.Tag != null).Select(t => t.Tag!.Name));
            ws.Cell(r, 7).Value = i.CreatedAt.ToString("O");
            ws.Cell(r, 8).Value = i.UpdatedAt.ToString("O");
            r++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items_export.xlsx");
    }

    private static async Task<IResult> ExportPdfAsync(List<Item> items, IFeatureFlags flags, CancellationToken ct)
    {
        if (!await flags.IsEnabledAsync("items.export.pdf", ct))
            return Results.StatusCode(403);

        QuestPDF.Settings.License = LicenseType.Community;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Text("Items Export").SemiBold().FontSize(16);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(70);
                        c.RelativeColumn();
                        c.ConstantColumn(60);
                        c.ConstantColumn(70);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Text("Id").SemiBold();
                        h.Cell().Text("Name").SemiBold();
                        h.Cell().Text("Price").SemiBold();
                        h.Cell().Text("Status").SemiBold();
                    });

                    foreach (var i in items.Take(300))
                    {
                        table.Cell().Text(i.Id.ToString()[..8]);
                        table.Cell().Text(i.Name);
                        table.Cell().Text(i.Price.ToString("0.##"));
                        table.Cell().Text(i.Status);
                    }
                });

                page.Footer().AlignRight().Text(x =>
                {
                    x.Span("Generated ");
                    x.Span(DateTimeOffset.UtcNow.ToString("O")).FontSize(9);
                });
            });
        });

        var bytes = doc.GeneratePdf();
        return Results.File(bytes, "application/pdf", "items_export.pdf");
    }

    private static async Task<IResult> Import(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        if (!ctx.Request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data required" });
        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null) return Results.BadRequest(new { error = "file required" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();

        List<ItemCreateDto> imported = new();
        if (ext == ".json")
        {
            imported = await JsonSerializer.DeserializeAsync<List<ItemCreateDto>>(stream, cancellationToken: ct) ?? new();
        }
        else
        {
            // csv: name,description,price,categoryId
            using var reader = new StreamReader(stream);
            string? line = await reader.ReadLineAsync(); // header
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                var parts = line.Split(',', 4);
                if (parts.Length < 3) continue;
                var name = parts[0];
                var desc = parts.Length >= 2 ? parts[1] : null;
                var priceStr = parts.Length >= 3 ? parts[2] : "0";
                var catStr = parts.Length >= 4 ? parts[3] : null;

                if (!decimal.TryParse(priceStr, out var price)) price = 0;
                Guid? cat = Guid.TryParse(catStr, out var g) ? g : null;

                imported.Add(new ItemCreateDto(name, desc, price, cat));
            }
        }

        var created = new List<Item>();
        using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var dto in imported)
        {
            var name = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var item = new Item { Name = name, Description = dto.Description?.Trim(), Price = dto.Price, CategoryId = dto.CategoryId, Status = ItemStatus.Draft };
            db.Items.Add(item);
            created.Add(item);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { imported = imported.Count, created = created.Count, ids = created.Select(i => i.Id) });
    }

    private static async Task<IResult> Upsert(AppDbContext db, ItemUpdateDto dto, CancellationToken ct)
    {
        // upsert by (Name)
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "Name required" });

        var item = await db.Items.FirstOrDefaultAsync(i => i.Name == name && !i.IsDeleted, ct);
        if (item is null)
        {
            item = new Item
            {
                Name = name,
                Description = dto.Description?.Trim(),
                Price = dto.Price,
                CategoryId = dto.CategoryId,
                Status = dto.Status.Trim().ToLowerInvariant(),
                IsActive = dto.IsActive,
                IsVerified = dto.IsVerified
            };
            db.Items.Add(item);
        }
        else
        {
            item.Description = dto.Description?.Trim();
            item.Price = dto.Price;
            item.CategoryId = dto.CategoryId;
            item.Status = dto.Status.Trim().ToLowerInvariant();
            item.IsActive = dto.IsActive;
            item.IsVerified = dto.IsVerified;
            item.Version += 1;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(Shape(item, null));
    }

    private static async Task<IResult> Deduplicate(AppDbContext db, CancellationToken ct)
    {
        // Deduplicate by Name: keep earliest CreatedAt, merge price (max), soft-delete others
        // EF Core GroupBy translation can be provider-specific. Pull into memory for a predictable demo.
        var items = await db.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var groups = items.GroupBy(i => i.Name).Where(g => g.Count() > 1).ToList();

        int merged = 0;
        foreach (var g in groups)
        {
            var keep = g.OrderBy(x => x.CreatedAt).First();
            foreach (var dup in g.Where(x => x.Id != keep.Id))
            {
                keep.Price = Math.Max(keep.Price, dup.Price);
                dup.IsDeleted = true;
                dup.DeletedAt = DateTimeOffset.UtcNow;
                dup.Version += 1;
                merged++;
            }
            keep.Version += 1;
            keep.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { merged });
    }

    private static async Task<IResult> AttachTags(AppDbContext db, Guid id, TagIdsDto dto, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.ItemTags).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        var existing = item.ItemTags.Select(it => it.TagId).ToHashSet();
        foreach (var tid in dto.TagIds.Distinct())
        {
            if (existing.Contains(tid)) continue;
            if (!await db.Tags.AnyAsync(t => t.Id == tid, ct)) continue;
            item.ItemTags.Add(new ItemTag { ItemId = id, TagId = tid });
        }
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> DetachTags(AppDbContext db, Guid id, TagIdsDto dto, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.ItemTags).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        item.ItemTags.RemoveAll(it => dto.TagIds.Contains(it.TagId));
        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SyncTags(AppDbContext db, Guid id, TagIdsDto dto, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.ItemTags).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        var desired = dto.TagIds.Distinct().ToHashSet();
        item.ItemTags.RemoveAll(it => !desired.Contains(it.TagId));

        var existing = item.ItemTags.Select(it => it.TagId).ToHashSet();
        foreach (var tid in desired)
        {
            if (existing.Contains(tid)) continue;
            if (!await db.Tags.AnyAsync(t => t.Id == tid, ct)) continue;
            item.ItemTags.Add(new ItemTag { ItemId = id, TagId = tid });
        }

        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true, count = item.ItemTags.Count });
    }

    private static async Task<IResult> WorkflowAction(AppDbContext db, Guid id, string action, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return Results.NotFound();

        action = action.Trim().ToLowerInvariant();

        switch (action)
        {
            case "submit": item.Status = ItemStatus.Review; break;
            case "approve": item.Status = ItemStatus.Published; break;
            case "reject": item.Status = ItemStatus.Draft; break;
            case "publish": item.Status = ItemStatus.Published; break;
            case "unpublish": item.Status = ItemStatus.Unpublished; break;
            case "activate": item.IsActive = true; break;
            case "deactivate": item.IsActive = false; break;
            case "verify": item.IsVerified = true; break;
            case "unverify": item.IsVerified = false; break;
            case "cancel": item.Status = ItemStatus.Cancelled; break;
            case "close": item.Status = ItemStatus.Closed; break;
            case "reopen": item.Status = ItemStatus.Published; break;
            default: return Results.BadRequest(new { error = "Unknown action" });
        }

        item.Version += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(Shape(item, null));
    }
}
