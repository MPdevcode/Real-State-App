using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.Infrastructure.Persistence;

namespace RealEstateApp.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<RealEstateDbContext>();
        var enabled = configuration.GetValue<bool>("Seed:Enabled");
        var definitions = new[]
        {
            (Key: "Admin", Role: Roles.Admin, Email: "admin@realestate.local", NationalId: "SEED-ADMIN"),
            (Key: "Developer", Role: Roles.Developer, Email: "developer@realestate.local", NationalId: "SEED-DEVELOPER"),
            (Key: "Agent", Role: Roles.Agent, Email: "agent@realestate.local", NationalId: (string?)null),
            (Key: "Client", Role: Roles.Client, Email: "client@realestate.local", NationalId: (string?)null)
        };

        await unitOfWork.ExecuteAsync(async () =>
        {
            if (db.Database.IsSqlServer() && db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Identity seeding requires a serializable IUnitOfWork transaction on RealEstateDbContext.");
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = N'RealEstate:Identity:Admins',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51000, 'Could not acquire the administrator account lock.', 1;
                """, ct);

            foreach (var role in Roles.All)
            {
                ct.ThrowIfCancellationRequested();
                if (!await roles.RoleExistsAsync(role)) EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
            }
            if (!enabled) return;

            foreach (var definition in definitions)
            {
                ct.ThrowIfCancellationRequested();
                var address = configuration[$"Seed:{definition.Key}Email"] ?? definition.Email;
                var userName = configuration[$"Seed:{definition.Key}UserName"] ?? address;
                var byName = await users.FindByNameAsync(userName);
                var byEmail = await users.FindByEmailAsync(address);
                var existing = byName ?? byEmail;
                if (existing is not null)
                {
                    if (byName?.Id != existing.Id || byEmail?.Id != existing.Id
                        || !await users.IsInRoleAsync(existing, definition.Role))
                        throw new InvalidOperationException($"Seed:{definition.Key} identity conflicts with an existing account. Configure a distinct seed username/email; existing accounts are never promoted or overwritten.");
                    continue;
                }

                var password = configuration[$"Seed:{definition.Key}Password"];
                if (string.IsNullOrWhiteSpace(password)) password = configuration["Seed:Password"];
                if (string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException($"Configure Seed:{definition.Key}Password (or an external development-only Seed:Password) before enabling seed users.");
                var nationalId = definition.NationalId;
                var normalizedId = nationalId?.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
                if (normalizedId is not null && await db.Users.AnyAsync(u => u.NormalizedNationalId == normalizedId, ct))
                    throw new InvalidOperationException($"The national ID reserved for the {definition.Key} seed account is already in use.");
                var user = new ApplicationUser
                {
                    UserName = userName, Email = address,
                    FirstName = configuration[$"Seed:{definition.Key}FirstName"] ?? definition.Key,
                    LastName = configuration[$"Seed:{definition.Key}LastName"] ?? "RealEstate",
                    PhoneNumber = configuration[$"Seed:{definition.Key}Phone"],
                    Photo = configuration[$"Seed:{definition.Key}Photo"],
                    NationalId = nationalId, NormalizedNationalId = normalizedId,
                    IsActive = true, EmailConfirmed = true
                };
                EnsureSuccess(await users.CreateAsync(user, password));
                EnsureSuccess(await users.AddToRoleAsync(user, definition.Role));
            }
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Identity seed failed: " + string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
