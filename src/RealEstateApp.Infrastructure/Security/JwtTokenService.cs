using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure.Identity;

namespace RealEstateApp.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly UserManager<ApplicationUser> users;
    private readonly SigningCredentials credentials;
    private readonly string issuer;
    private readonly string audience;
    private readonly int lifetimeMinutes;

    public JwtTokenService(UserManager<ApplicationUser> users, IConfiguration configuration)
    {
        this.users = users;
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure Jwt:Key with a secret of at least 32 characters (256 bits).");
        issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Configure Jwt:Issuer.");
        audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Configure Jwt:Audience.");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must not be empty.");
        lifetimeMinutes = configuration.GetValue("Jwt:LifetimeMinutes", 15);
        if (lifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Jwt:LifetimeMinutes must be between 1 and 1440.");
        credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
    }

    public async Task<TokenResult> CreateAsync(LoginResult login, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(login.UserId);
        if (user is null || !user.IsActive || user.UserName != login.UserName)
            throw new AuthenticationException("La cuenta no está activa.");
        var roles = await users.GetRolesAsync(user);
        if (!roles.Contains(login.Role) || (login.Role != Roles.Admin && login.Role != Roles.Developer))
            throw new ForbiddenException("Esta cuenta no tiene acceso a la API.");
        var now = DateTimeOffset.UtcNow;
        var expiration = now.AddMinutes(lifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Name, user.UserName!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(UserAccessValidator.VersionClaim, user.AuthVersion.ToString(CultureInfo.InvariantCulture))
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(issuer, audience, claims, now.UtcDateTime, expiration.UtcDateTime, credentials);
        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), user.UserName!, roles.ToArray(), expiration);
    }
}
