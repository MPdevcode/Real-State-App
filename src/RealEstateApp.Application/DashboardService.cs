using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class DashboardService(IUnitOfWork work, ICurrentUser user, IAccountService accounts) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        ServiceRules.RequireRole(user, Roles.Admin);
        var properties = await work.Repository<Property>().ListAsync(ct: ct);
        // Account services may share the scoped persistence context, so do not query them concurrently.
        var agents = await accounts.ListAsync(Roles.Agent, ct: ct);
        var clients = await accounts.ListAsync(Roles.Client, ct: ct);
        var developers = await accounts.ListAsync(Roles.Developer, ct: ct);
        return new DashboardDto(properties.Count(property => property.Status == PropertyStatus.Available),
            properties.Count(property => property.Status == PropertyStatus.Sold),
            agents.Count(account => account.IsActive), agents.Count(account => !account.IsActive),
            clients.Count(account => account.IsActive), clients.Count(account => !account.IsActive),
            developers.Count(account => account.IsActive), developers.Count(account => !account.IsActive));
    }
}
