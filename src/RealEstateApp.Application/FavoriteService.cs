using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class FavoriteService(IUnitOfWork work, ICurrentUser user, IAccountService accounts) : IFavoriteService
{
    public async Task SetAsync(int propertyId, bool favorite, CancellationToken ct = default)
    {
        var clientId = ServiceRules.RequireRole(user, Roles.Client);
        await work.ExecuteAsync(async () =>
        {
            if (favorite)
            {
                await work.LockPropertyAsync(propertyId, ct);
                await ServiceRules.RequirePublicAsync(await ServiceRules.PropertyAsync(work, propertyId, ct), accounts, ct);
            }
            var repository = work.Repository<Favorite>();
            var existing = await repository.ListAsync(item => item.ClientId == clientId && item.PropertyId == propertyId, ct);
            if (favorite && existing.Count == 0) repository.Add(new Favorite { ClientId = clientId, PropertyId = propertyId });
            if (!favorite)
                foreach (var item in existing) repository.Remove(item);
        }, ct);
    }
}
