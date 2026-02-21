using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using System.Text;
using System.Diagnostics;
using System.Threading.RateLimiting;
using TimeSeriesForecast.Api.Data;
using TimeSeriesForecast.Api.Endpoints;
using TimeSeriesForecast.Api.Middleware;
using TimeSeriesForecast.Api.Security;
using TimeSeriesForecast.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwtOpts = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// DB
builder.Services.AddDbContext<AppDbContext>(o =>
{
    o.UseSqlite(builder.Configuration.GetConnectionString("Default"));
});

// Memory cache
builder.Services.AddMemoryCache();

// Feature flags
builder.Services.AddScoped<IFeatureFlags, FeatureFlagsService>();

// Security services
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<IFileSigner>(sp => new HmacFileSigner(jwtOpts));

// AuthN
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOpts.Issuer,
            ValidAudience = jwtOpts.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.Key))
        };
    });

// AuthZ (RBAC + Permissions)
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization(o =>
{
    // Permission policies
    string[] perms = new[]
    {
        "items:read","items:write","items:delete",
        "series:read","series:write",
        "files:read","files:write",
        "analytics:read","analytics:write",
        "admin:manage"
    };

    foreach (var p in perms)
        o.AddPolicy(p, policy => policy.Requirements.Add(new PermissionRequirement(p)));
});

// CORS (adjust in production)
builder.Services.AddCors(o =>
{
    o.AddPolicy("default", p => p
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});

// Rate limiting
var rpm = int.TryParse(builder.Configuration["RateLimit:PerMinute"], out var v) ? v : 120;
builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.Claims.FirstOrDefault(c => c.Type == "uid")?.Value ?? "auth"
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";

        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = rpm,
            TokensPerPeriod = rpm,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

// Health checks
builder.Services.AddHealthChecks();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Background worker (outbox)
builder.Services.AddHttpClient("outbox");
builder.Services.AddHostedService<OutboxWorker>();

// QuestPDF community license
QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

app.UseRateLimiter();
app.UseCors("default");

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Observability
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Ensure database + seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapItems();
api.MapCatalog();
api.MapSeries();
api.MapFiles();
api.MapAnalytics();
api.MapAdmin();

// Auto-open Swagger (default on; set TSF_OPEN_BROWSER=0 to disable)
var openBrowserEnv = Environment.GetEnvironmentVariable("TSF_OPEN_BROWSER");
var openBrowser = !(string.Equals(openBrowserEnv, "0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(openBrowserEnv, "false", StringComparison.OrdinalIgnoreCase));
if (openBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var server = app.Services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var addresses = server?.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;
            var baseUrl = addresses?.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                          ?? app.Urls.FirstOrDefault(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                          ?? "http://localhost:5000";
            var swaggerUrl = baseUrl.TrimEnd('/') + "/swagger";
            Process.Start(new ProcessStartInfo
            {
                FileName = swaggerUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    });
}

app.Run();

public partial class Program { }

