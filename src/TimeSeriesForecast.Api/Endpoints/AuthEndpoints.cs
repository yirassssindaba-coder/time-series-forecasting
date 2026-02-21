using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Security;
using TimeSeriesForecast.Core.Security;

namespace TimeSeriesForecast.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder group)
    {
        group.WithTags("auth");

        group.MapPost("/auth/register", Register);
        group.MapPost("/auth/login", Login);
        group.MapPost("/auth/refresh", Refresh);
        group.MapPost("/auth/logout", Logout).RequireAuthorization();

        group.MapPost("/auth/forgot-password", ForgotPassword);
        group.MapPost("/auth/reset-password", ResetPassword);
        group.MapPost("/auth/change-password", ChangePassword).RequireAuthorization();

        group.MapPost("/auth/verify-email", VerifyEmail);
        group.MapPost("/auth/2fa/enable", Enable2fa).RequireAuthorization();
        group.MapPost("/auth/2fa/disable", Disable2fa).RequireAuthorization();
        group.MapPost("/auth/2fa/verify", Verify2fa);

        group.MapGet("/auth/sessions", ListSessions).RequireAuthorization();
        group.MapPost("/auth/sessions/{id:guid}/revoke", RevokeSession).RequireAuthorization();

        return group;
    }

    public sealed record RegisterDto(string Email, string Password, string? DisplayName);
    public sealed record LoginDto(string Email, string Password, string? TwoFactorCode);
    public sealed record RefreshDto(string RefreshToken);
    public sealed record ChangePasswordDto(string OldPassword, string NewPassword);
    public sealed record ForgotPasswordDto(string Email);
    public sealed record ResetPasswordDto(string Email, string Token, string NewPassword);
    public sealed record VerifyEmailDto(string Email, string Token);

    private static async Task<IResult> Register(HttpContext ctx, AppDbContext db, JwtService jwt, RegisterDto dto, CancellationToken ct)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { error = "Email required" });
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return Results.Conflict(new { error = "Email exists" });

        var user = new AppUser { Email = email, DisplayName = dto.DisplayName?.Trim() ?? email, EmailVerified = false };
        var hasher = new PasswordHasher<AppUser>();
        user.PasswordHash = hasher.HashPassword(user, dto.Password);

        db.Users.Add(user);

        // default role = user
        var roleUser = await db.Roles.FirstOrDefaultAsync(r => r.Name == "user", ct);
        if (roleUser is not null)
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleUser.Id });

        await db.SaveChangesAsync(ct);

        // create session + tokens
        var (refreshToken, refreshHash) = jwt.CreateRefreshToken();
        var session = new RefreshSession
        {
            UserId = user.Id,
            RefreshTokenHash = refreshHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
            Ip = ctx.Connection.RemoteIpAddress?.ToString()
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var accessToken = await jwt.CreateAccessTokenAsync(user, ct);
        return Results.Ok(new { accessToken, refreshToken, user = new { user.Id, user.Email, user.DisplayName, user.EmailVerified } });
    }

    private static async Task<IResult> Login(HttpContext ctx, AppDbContext db, JwtService jwt, LoginDto dto, CancellationToken ct)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return Results.Unauthorized();
        if (user.IsLocked) return Results.StatusCode(403);

        var hasher = new PasswordHasher<AppUser>();
        var ok = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (ok == PasswordVerificationResult.Failed) return Results.Unauthorized();

        if (user.TwoFactorEnabled)
        {
            // Demo: accept any non-empty code; real implementation uses TOTP.
            if (string.IsNullOrWhiteSpace(dto.TwoFactorCode))
                return Results.Json(new { requires2fa = true }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var (refreshToken, refreshHash) = jwt.CreateRefreshToken();
        var session = new RefreshSession
        {
            UserId = user.Id,
            RefreshTokenHash = refreshHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
            Ip = ctx.Connection.RemoteIpAddress?.ToString()
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var accessToken = await jwt.CreateAccessTokenAsync(user, ct);
        return Results.Ok(new { accessToken, refreshToken, user = new { user.Id, user.Email, user.DisplayName, user.EmailVerified } });
    }

    private static async Task<IResult> Verify2fa(HttpContext ctx, AppDbContext db, JwtService jwt, LoginDto dto, CancellationToken ct)
    {
        // alias to Login for demo
        return await Login(ctx, db, jwt, dto, ct);
    }

    private static async Task<IResult> Refresh(AppDbContext db, JwtService jwt, RefreshDto dto, CancellationToken ct)
    {
        var hash = jwt.HashRefreshToken(dto.RefreshToken);
        var session = await db.RefreshSessions.Include(s => s.User).FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is null) return Results.Unauthorized();
        if (session.Revoked) return Results.Unauthorized();
        if (DateTimeOffset.UtcNow > session.ExpiresAt) return Results.Unauthorized();
        if (session.User is null || session.User.IsLocked) return Results.Unauthorized();

        // rotate
        session.Revoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        var (newRefresh, newHash) = jwt.CreateRefreshToken();
        var newSession = new RefreshSession
        {
            UserId = session.UserId,
            RefreshTokenHash = newHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            UserAgent = session.UserAgent,
            Ip = session.Ip
        };
        db.RefreshSessions.Add(newSession);
        await db.SaveChangesAsync(ct);

        var accessToken = await jwt.CreateAccessTokenAsync(session.User, ct);
        return Results.Ok(new { accessToken, refreshToken = newRefresh });
    }

    private static async Task<IResult> Logout(HttpContext ctx, AppDbContext db, JwtService jwt, RefreshDto dto, CancellationToken ct)
    {
        var hash = jwt.HashRefreshToken(dto.RefreshToken);
        var session = await db.RefreshSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is null) return Results.Ok(new { ok = true });

        // Only allow owner or admin to revoke
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is not null && Guid.TryParse(uid, out var userId) && session.UserId != userId)
        {
            var isAdmin = ctx.User.IsInRole("admin") || ctx.User.Claims.Any(c => c.Type == "perm" && c.Value == "admin:manage");
            if (!isAdmin) return Results.StatusCode(403);
        }

        session.Revoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ListSessions(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is null || !Guid.TryParse(uid, out var userId)) return Results.Unauthorized();

        var rows = await db.RefreshSessions.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct);
        return Results.Ok(rows.Select(s => new { s.Id, s.CreatedAt, s.ExpiresAt, s.Revoked, s.RevokedAt, s.UserAgent, s.Ip }));
    }

    private static async Task<IResult> RevokeSession(HttpContext ctx, AppDbContext db, Guid id, CancellationToken ct)
    {
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is null || !Guid.TryParse(uid, out var userId)) return Results.Unauthorized();

        var session = await db.RefreshSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return Results.NotFound();
        if (session.UserId != userId) return Results.StatusCode(403);

        session.Revoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static Task<IResult> ForgotPassword(ForgotPasswordDto dto)
    {
        // Demo only: in real life send email + store secure token.
        return Task.FromResult<IResult>(Results.Ok(new { ok = true, message = "If the email exists, a reset link will be sent." }));
    }

    private static Task<IResult> ResetPassword(ResetPasswordDto dto)
    {
        // Demo stub.
        return Task.FromResult<IResult>(Results.Ok(new { ok = true, message = "Password reset flow is stubbed in this template." }));
    }

    private static async Task<IResult> ChangePassword(HttpContext ctx, AppDbContext db, ChangePasswordDto dto, CancellationToken ct)
    {
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is null || !Guid.TryParse(uid, out var userId)) return Results.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        var hasher = new PasswordHasher<AppUser>();
        var ok = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.OldPassword);
        if (ok == PasswordVerificationResult.Failed) return Results.StatusCode(403);

        user.PasswordHash = hasher.HashPassword(user, dto.NewPassword);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static Task<IResult> VerifyEmail(VerifyEmailDto dto)
    {
        // Demo stub.
        return Task.FromResult<IResult>(Results.Ok(new { ok = true, message = "Email verification flow is stubbed in this template." }));
    }

    private static async Task<IResult> Enable2fa(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is null || !Guid.TryParse(uid, out var userId)) return Results.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        user.TwoFactorEnabled = true;
        user.TwoFactorSecret = "demo-secret";
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true, secret = user.TwoFactorSecret });
    }

    private static async Task<IResult> Disable2fa(HttpContext ctx, AppDbContext db, CancellationToken ct)
    {
        var uid = ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        if (uid is null || !Guid.TryParse(uid, out var userId)) return Results.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }
}
