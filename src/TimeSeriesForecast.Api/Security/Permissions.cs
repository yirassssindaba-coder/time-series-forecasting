using Microsoft.AspNetCore.Authorization;

namespace TimeSeriesForecast.Api.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var has = context.User.Claims.Any(c => c.Type == "perm" && string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));
        if (has) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class PermissionPolicy
{
    public static void AddPermissionPolicy(this AuthorizationOptions options, string permission)
    {
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
    }
}
