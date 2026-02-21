using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Middleware;
using TimeSeriesForecast.Api.Security;
using TimeSeriesForecast.Core.Models;
using TimeSeriesForecast.Core.Security;

namespace TimeSeriesForecast.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder group)
    {
        group.WithTags("admin");

        group.MapGet("/admin/users", ListUsers).RequireAuthorization("admin:manage");
        group.MapPost("/admin/users", CreateUser).RequireAuthorization("admin:manage");
        group.MapPost("/admin/users/{id:guid}/lock", LockUser).RequireAuthorization("admin:manage");
        group.MapPost("/admin/users/{id:guid}/unlock", UnlockUser).RequireAuthorization("admin:manage");

        group.MapGet("/admin/roles", ListRoles).RequireAuthorization("admin:manage");
        group.MapPost("/admin/roles", CreateRole).RequireAuthorization("admin:manage");
        group.MapPost("/admin/roles/{id:guid}/permissions/sync", SyncRolePerms).RequireAuthorization("admin:manage");

        group.MapGet("/admin/feature-flags", ListFlags).RequireAuthorization("admin:manage");
        group.MapPost("/admin/feature-flags", UpsertFlag).RequireAuthorization("admin:manage");

        group.MapGet("/admin/logs", ListLogs).RequireAuthorization("admin:manage");

        group.MapGet("/admin/sessions", ListSessions).RequireAuthorization("admin:manage");
        group.MapPost("/admin/sessions/{id:guid}/revoke", RevokeSession).RequireAuthorization("admin:manage");

        group.MapGet("/admin/dlq", ListDlq).RequireAuthorization("admin:manage");

        return group;
    }

    public sealed record CreateUserDto(string Email, string Password, string? DisplayName, List<string>? Roles);
    public sealed record CreateRoleDto(string Name);
    public sealed record SyncPermsDto(List<string> Permissions);
    public sealed record UpsertFlagDto(string Key, bool Enabled, string? Description);

    private static async Task<IResult> ListUsers(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Users.AsNoTracking().OrderBy(x => x.Email).Take(200).ToListAsync(ct);
        return Results.Ok(rows.Select(u => new { u.Id, u.Email, u.DisplayName, u.EmailVerified, u.IsLocked, u.CreatedAt }));
    }

    private static async Task<IResult> CreateUser(AppDbContext db, CreateUserDto dto, CancellationToken ct)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { error = "Email required" });
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return Results.Conflict(new { error = "Email exists" });

        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser { Email = email, DisplayName = dto.DisplayName?.Trim() ?? email, EmailVerified = true };
        user.PasswordHash = hasher.HashPassword(user, dto.Password);
        db.Users.Add(user);

        if (dto.Roles is not null && dto.Roles.Count > 0)
        {
            var roleIds = await db.Roles.Where(r => dto.Roles.Select(x => x.Trim().ToLowerInvariant()).Contains(r.Name)).Select(r => r.Id).ToListAsync(ct);
            foreach (var rid in roleIds) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rid });
        }

        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/admin/users/{user.Id}", new { user.Id, user.Email });
    }

    private static async Task<IResult> LockUser(AppDbContext db, Guid id, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return Results.NotFound();
        u.IsLocked = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> UnlockUser(AppDbContext db, Guid id, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return Results.NotFound();
        u.IsLocked = false;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ListRoles(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Roles.AsNoTracking().OrderBy(x => x.Name).Take(100).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateRole(AppDbContext db, CreateRoleDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "Name required" });
        if (await db.Roles.AnyAsync(r => r.Name == name, ct)) return Results.Conflict(new { error = "Role exists" });
        var role = new Role { Name = name };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/admin/roles/{role.Id}", role);
    }

    private static async Task<IResult> SyncRolePerms(AppDbContext db, Guid id, SyncPermsDto dto, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Results.NotFound();

        var desired = dto.Permissions.Select(p => p.Trim().ToLowerInvariant()).ToHashSet();
        // ensure permissions rows exist
        var existingPerms = await db.Permissions.Where(p => desired.Contains(p.Name)).ToListAsync(ct);
        var existingNames = existingPerms.Select(p => p.Name).ToHashSet();
        foreach (var p in desired.Where(p => !existingNames.Contains(p)))
        {
            db.Permissions.Add(new Permission { Name = p });
        }
        await db.SaveChangesAsync(ct);

        var permIds = await db.Permissions.Where(p => desired.Contains(p.Name)).Select(p => p.Id).ToListAsync(ct);

        role.RolePermissions.RemoveAll(rp => !permIds.Contains(rp.PermissionId));
        var have = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var pid in permIds)
        {
            if (!have.Contains(pid)) role.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = pid });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true, count = role.RolePermissions.Count });
    }

    private static async Task<IResult> ListFlags(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.FeatureFlags.AsNoTracking().OrderBy(x => x.Key).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> UpsertFlag(AppDbContext db, IFeatureFlags flags, UpsertFlagDto dto, CancellationToken ct)
    {
        var key = dto.Key.Trim();
        if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { error = "Key required" });

        var row = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            row = new FeatureFlag { Key = key, Enabled = dto.Enabled, Description = dto.Description?.Trim() };
            db.FeatureFlags.Add(row);
        }
        else
        {
            row.Enabled = dto.Enabled;
            row.Description = dto.Description?.Trim();
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await flags.InvalidateAsync(key);
        return Results.Ok(row);
    }

    private static async Task<IResult> ListLogs(AppDbContext db, HttpRequest req, CancellationToken ct)
    {
        int take = int.TryParse(req.Query["take"], out var t) ? Math.Clamp(t, 1, 500) : 200;
        var rows = await db.ActivityLogs.AsNoTracking().OrderByDescending(x => x.At).Take(take).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ListSessions(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.RefreshSessions.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return Results.Ok(rows.Select(s => new { s.Id, s.UserId, s.CreatedAt, s.ExpiresAt, s.Revoked, s.RevokedAt, s.UserAgent, s.Ip }));
    }

    private static async Task<IResult> RevokeSession(AppDbContext db, Guid id, CancellationToken ct)
    {
        var s = await db.RefreshSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();
        s.Revoked = true;
        s.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ListDlq(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.DeadLetterMessages.AsNoTracking().OrderByDescending(x => x.DeadAt).Take(200).ToListAsync(ct);
        return Results.Ok(rows);
    }
}
