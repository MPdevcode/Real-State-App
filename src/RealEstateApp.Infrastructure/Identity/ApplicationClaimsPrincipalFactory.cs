using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RealEstateApp.Infrastructure.Security;

namespace RealEstateApp.Infrastructure.Identity;

public sealed class ApplicationClaimsPrincipalFactory(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        foreach (var claim in identity.FindAll(UserAccessValidator.VersionClaim).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(UserAccessValidator.VersionClaim,
            user.AuthVersion.ToString(CultureInfo.InvariantCulture)));
        return identity;
    }
}
