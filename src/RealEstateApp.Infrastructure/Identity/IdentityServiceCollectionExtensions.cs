using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Application;
using RealEstateApp.Infrastructure.Persistence;
using RealEstateApp.Infrastructure.Security;

namespace RealEstateApp.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<RealEstateDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "RealEstate.Identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Events.OnValidatePrincipal = async context =>
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<UserAccessValidator>();
                var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                if (context.Principal is null || !await validator.ValidateAsync(context.Principal)
                    || await signIn.ValidateSecurityStampAsync(context.Principal) is null)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
        });
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IWebSessionService, WebSessionService>();
        services.AddScoped<UserAccessValidator>();
        return services;
    }
}
