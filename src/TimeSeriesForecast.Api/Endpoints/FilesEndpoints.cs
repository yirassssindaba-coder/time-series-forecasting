using Microsoft.EntityFrameworkCore;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Security;
using TimeSeriesForecast.Core.Models;

namespace TimeSeriesForecast.Api.Endpoints;

public static class FilesEndpoints
{
    public static RouteGroupBuilder MapFiles(this RouteGroupBuilder group)
    {
        group.WithTags("files");

        group.MapPost("/files/upload", Upload).RequireAuthorization("files:write");
        group.MapGet("/files/{id:guid}", Download).RequireAuthorization("files:read");
        group.MapDelete("/files/{id:guid}", Delete).RequireAuthorization("files:write");
        group.MapGet("/files/{id:guid}/sign", Sign).RequireAuthorization("files:read");

        return group;
    }

    private static string StorageRoot(IConfiguration cfg)
    {
        // Default keeps files next to the app output, and works on Windows + Linux.
        var root = cfg["Storage:Root"] ?? "storage";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
    }

    private static async Task<IResult> Upload(HttpContext ctx, AppDbContext db, IConfiguration cfg, CancellationToken ct)
    {
        if (!ctx.Request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data required" });

        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null) return Results.BadRequest(new { error = "file required" });

        var root = StorageRoot(cfg);
        Directory.CreateDirectory(root);

        var id = Guid.NewGuid();
        var ext = Path.GetExtension(file.FileName);
        var storedName = id + ext;
        var storedPath = Path.Combine(root, storedName);

        await using (var fs = File.Create(storedPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        var meta = new FileObject
        {
            Id = id,
            OriginalName = file.FileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storedName
        };

        db.Files.Add(meta);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/files/{id}", new { meta.Id, meta.OriginalName, meta.ContentType, meta.SizeBytes });
    }

    private static async Task<IResult> Sign(AppDbContext db, IFileSigner signer, Guid id, HttpRequest req, CancellationToken ct)
    {
        var exists = await db.Files.AsNoTracking().AnyAsync(f => f.Id == id, ct);
        if (!exists) return Results.NotFound();

        int secs = int.TryParse(req.Query["expiresSeconds"], out var s) ? Math.Clamp(s, 30, 86400) : 300;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(secs);
        var sig = signer.Sign(id, expiresAt);
        return Results.Ok(new { id, expiresAt = expiresAt.ToUnixTimeSeconds(), token = sig });
    }

    private static async Task<IResult> Download(HttpContext ctx, AppDbContext db, IConfiguration cfg, IFileSigner signer, Guid id, CancellationToken ct)
    {
        var meta = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (meta is null) return Results.NotFound();

        // Signed URL validation is optional. If token is not provided, it behaves like a normal authenticated download.
        var token = ctx.Request.Query["token"].ToString();
        var expRaw = ctx.Request.Query["expiresAt"].ToString();
        if (!string.IsNullOrWhiteSpace(token) && long.TryParse(expRaw, out var expSeconds))
        {
            var exp = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            if (DateTimeOffset.UtcNow > exp) return Results.StatusCode(403);
            if (!signer.Verify(id, exp, token)) return Results.StatusCode(403);
        }

        var root = StorageRoot(cfg);
        var fullPath = Path.Combine(root, meta.StoragePath);
        if (!File.Exists(fullPath)) return Results.NotFound();

        return Results.File(File.ReadAllBytes(fullPath), meta.ContentType, fileDownloadName: meta.OriginalName);
    }

    private static async Task<IResult> Delete(AppDbContext db, IConfiguration cfg, Guid id, CancellationToken ct)
    {
        var meta = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (meta is null) return Results.NotFound();

        var root = StorageRoot(cfg);
        var fullPath = Path.Combine(root, meta.StoragePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        db.Files.Remove(meta);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
