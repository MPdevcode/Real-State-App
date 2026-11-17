using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RealEstateApp.Application;
using RealEstateApp.Domain;

namespace RealEstateApp.Infrastructure.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string Id => Principal?.Identity?.IsAuthenticated == true
        ? Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Principal.FindFirstValue("sub")
            ?? throw new AuthenticationException("La sesión no identifica al usuario.")
        : throw new AuthenticationException("Debe iniciar sesión.");

    public bool IsInRole(string role) => Principal?.Identity?.IsAuthenticated == true
        && (Principal.IsInRole(role) || Principal.HasClaim("role", role));

    public void RequireRole(string role)
    {
        if (!IsInRole(role)) throw new ForbiddenException("No tiene permisos para esta operación.");
    }
}
