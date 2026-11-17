using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using RealEstateApp.Infrastructure.Identity;

namespace RealEstateApp.Infrastructure.Security;

// Both authentication handlers must call this after cryptographic validation.
public sealed class UserAccessValidator(UserManager<ApplicationUser> users)
{
    public const string VersionClaim = "auth_version";

    public async Task<bool> ValidateAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(id) || (subject is not null && subject != id)) return false;
        var versions = principal.FindAll(VersionClaim).ToArray();
        if (versions.Length != 1 || !int.TryParse(versions[0].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var version)) return false;

        var user = await users.FindByIdAsync(id);
        if (user is null || !user.IsActive || user.AuthVersion != version) return false;
        var claimedRoles = principal.FindAll(ClaimTypes.Role).Concat(principal.FindAll("role"))
            .Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var currentRoles = await users.GetRolesAsync(user);
        return claimedRoles.Count > 0 && claimedRoles.SetEquals(currentRoles);
    }
}
