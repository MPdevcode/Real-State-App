using Microsoft.AspNetCore.Identity;
using RealEstateApp.Application;
using RealEstateApp.Domain;

namespace RealEstateApp.Infrastructure.Identity;

public sealed class WebSessionService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : IWebSessionService
{
    public async Task SignInAsync(LoginResult login)
    {
        var user = await users.FindByIdAsync(login.UserId);
        if (user is null || !user.IsActive || user.UserName != login.UserName)
            throw new AuthenticationException("La cuenta no está activa.");
        var roles = await users.GetRolesAsync(user);
        if (!roles.Contains(login.Role) || roles.Contains(Roles.Developer)
            || (login.Role != Roles.Client && login.Role != Roles.Agent && login.Role != Roles.Admin))
            throw new ForbiddenException("Esta cuenta no tiene acceso a la aplicación web.");
        await signIn.SignInAsync(user, isPersistent: false);
    }

    public Task SignOutAsync() => signIn.SignOutAsync();
}
