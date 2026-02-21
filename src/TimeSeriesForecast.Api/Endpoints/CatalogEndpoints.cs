using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Endpoints;

public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalog(this RouteGroupBuilder group)
    {
        group.WithTags("catalog");

        group.MapGet("/categories", async (AppDbContext db, CancellationToken ct) =>
        {
            var data = await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(data);
        }).RequireAuthorization("items:read");

        group.MapPost("/categories", async (AppDbContext db, Category dto, CancellationToken ct) =>
        {
            dto.Name = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Name required" });
            db.Categories.Add(new Category { Name = dto.Name });
            await db.SaveChangesAsync(ct);
            return Results.Created("/api/v1/categories", dto);
        }).RequireAuthorization("items:write");

        group.MapGet("/tags", async (AppDbContext db, CancellationToken ct) =>
        {
            var data = await db.Tags.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(data);
        }).RequireAuthorization("items:read");

        group.MapPost("/tags", async (AppDbContext db, Tag dto, CancellationToken ct) =>
        {
            dto.Name = dto.Name.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Name required" });
            if (await db.Tags.AnyAsync(t => t.Name == dto.Name, ct)) return Results.Conflict(new { error = "Tag exists" });
            var tag = new Tag { Name = dto.Name };
            db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/tags/{tag.Id}", tag);
        }).RequireAuthorization("items:write");

        // Nested resource example
        group.MapGet("/categories/{id:guid}/items", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var data = await db.Items.AsNoTracking().Where(i => i.CategoryId == id && !i.IsDeleted).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
            return Results.Ok(data);
        }).RequireAuthorization("items:read");

        return group;
    }
}
