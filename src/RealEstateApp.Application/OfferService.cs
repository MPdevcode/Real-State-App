using AutoMapper;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class OfferService(IUnitOfWork work, ICurrentUser user, IAccountService accounts, IMapper mapper) : IOfferService
{
    public async Task<List<OfferDto>> ListAsync(int propertyId, string? clientId = null, CancellationToken ct = default)
    {
        if (user.IsInRole(Roles.Client))
        {
            var currentId = ServiceRules.RequireRole(user, Roles.Client);
            if (clientId is not null && clientId != currentId) throw new ForbiddenException("No puede consultar ofertas de otro cliente.");
            clientId = currentId;
        }
        else
        {
            var agentId = ServiceRules.RequireRole(user, Roles.Agent);
            (await ServiceRules.PropertyAsync(work, propertyId, ct)).EnsureOwner(agentId);
        }
        var offers = await work.Repository<Offer>().ListAsync(offer => offer.PropertyId == propertyId &&
            (clientId == null || offer.ClientId == clientId), ct);
        var names = new Dictionary<string, string>();
        var result = new List<OfferDto>();
        foreach (var offer in offers.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id))
        {
            if (!names.TryGetValue(offer.ClientId, out var name))
            {
                name = (await accounts.GetAsync(offer.ClientId, ct)).FullName;
                names.Add(offer.ClientId, name);
            }
            result.Add(mapper.Map<OfferDto>(offer) with { ClientName = name });
        }
        return result;
    }

    public async Task CreateAsync(int propertyId, decimal amount, CancellationToken ct = default)
    {
        var clientId = ServiceRules.RequireRole(user, Roles.Client);
        if (amount <= 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
            throw new BusinessException("El monto de la oferta debe ser un valor numérico mayor que cero.");
        await work.ExecuteAsync(async () =>
        {
            await work.LockPropertyAsync(propertyId, ct);
            var property = await ServiceRules.PropertyAsync(work, propertyId, ct);
            await ServiceRules.RequirePublicAsync(property, accounts, ct, "Esta propiedad ya no se encuentra disponible para recibir ofertas.");
            var repository = work.Repository<Offer>();
            if ((await repository.ListAsync(offer => offer.PropertyId == propertyId && offer.ClientId == clientId &&
                    offer.Status == OfferStatus.Pending, ct)).Count != 0)
                throw new BusinessException("Ya tiene una oferta pendiente para esta propiedad.");
            repository.Add(new Offer { PropertyId = propertyId, ClientId = clientId, Amount = amount });
        }, ct);
    }

    public async Task RespondAsync(int offerId, bool accept, CancellationToken ct = default)
    {
        var agentId = ServiceRules.RequireRole(user, Roles.Agent);
        // Discover only the lock key outside the transaction; this avoids taking an offer read lock before the property lock.
        var initial = await work.Repository<Offer>().FindAsync(offerId, ct)
            ?? throw new NotFoundException("Oferta no encontrada.");
        var propertyId = initial.PropertyId;
        await work.ExecuteAsync(async () =>
        {
            await work.LockPropertyAsync(propertyId, ct);
            var property = await ServiceRules.PropertyAsync(work, propertyId, ct);
            property.EnsureOwner(agentId);
            property.EnsureAvailable(accept ? "No se puede aceptar una oferta para una propiedad que ya fue vendida." : null);
            var pending = await work.Repository<Offer>().ListAsync(offer => offer.PropertyId == propertyId &&
                offer.Status == OfferStatus.Pending, ct);
            var target = pending.SingleOrDefault(offer => offer.Id == offerId)
                ?? throw new BusinessException("Esta oferta ya fue respondida.");
            target.Respond(accept);
            if (accept)
            {
                foreach (var other in pending.Where(offer => offer.Id != offerId)) other.Respond(false);
                property.Status = PropertyStatus.Sold;
            }
        }, ct);
    }
}
