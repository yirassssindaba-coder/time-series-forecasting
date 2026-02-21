using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Core.Models;
using TimeSeriesForecast.Core.Security;

namespace TimeSeriesForecast.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        // Roles
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "admin", ct)
                        ?? db.Roles.Add(new Role { Name = "admin" }).Entity;
        var userRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "user", ct)
                       ?? db.Roles.Add(new Role { Name = "user" }).Entity;

        // Permissions (minimal set; extend freely)
        var perms = new[]
        {
            "items:read", "items:write", "items:delete", "admin:manage", "series:read", "series:write", "files:write", "files:read"
        };
        foreach (var p in perms)
        {
            if (!await db.Permissions.AnyAsync(x => x.Name == p, ct))
                db.Permissions.Add(new Permission { Name = p });
        }
        await db.SaveChangesAsync(ct);

        // Attach admin role permissions
        var allPermIds = await db.Permissions.Select(x => x.Id).ToListAsync(ct);
        foreach (var pid in allPermIds)
        {
            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == pid, ct))
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = pid });
        }

        // User role permissions (read + series)
        var userPermNames = new[] { "items:read", "series:read" };
        var userPermIds = await db.Permissions.Where(p => userPermNames.Contains(p.Name)).Select(p => p.Id).ToListAsync(ct);
        foreach (var pid in userPermIds)
        {
            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == userRole.Id && rp.PermissionId == pid, ct))
                db.RolePermissions.Add(new RolePermission { RoleId = userRole.Id, PermissionId = pid });
        }

        // Default admin user
        var adminEmail = "admin@example.com";
        if (!await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            var hasher = new PasswordHasher<AppUser>();
            var user = new AppUser
            {
                Email = adminEmail,
                DisplayName = "Admin",
                EmailVerified = true,
            };
            user.PasswordHash = hasher.HashPassword(user, "Admin123!");
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
        }

        // Demo category + tags
        if (!await db.Categories.AnyAsync(ct))
            db.Categories.Add(new Category { Name = "General" });

        if (!await db.Tags.AnyAsync(ct))
        {
            db.Tags.AddRange(
                new Tag { Name = "featured" },
                new Tag { Name = "promo" },
                new Tag { Name = "new" });
        }

        // Feature flags
        if (!await db.FeatureFlags.AnyAsync(ff => ff.Key == "items.export.pdf", ct))
            db.FeatureFlags.Add(new FeatureFlag { Key = "items.export.pdf", Enabled = true, Description = "Enable PDF export" });

        if (!await db.FeatureFlags.AnyAsync(ff => ff.Key == "series.forecast", ct))
            db.FeatureFlags.Add(new FeatureFlag { Key = "series.forecast", Enabled = true, Description = "Enable forecasting endpoints" });

        await db.SaveChangesAsync(ct);
    }
}
