using System.Globalization;
using System.Security.Cryptography;
using AutoMapper;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class PropertyService(IUnitOfWork work, ICurrentUser user, IAccountService accounts,
    IFileStorage files, IMapper mapper) : IPropertyService
{
    public async Task<List<PropertyDto>> SearchAsync(PropertyFilter filter, string scope = "public",
        string? agentId = null, CancellationToken ct = default)
    {
        AuthorizeScope(scope);
        if ((scope is "agent" or "maintenance") && agentId is not null && agentId != user.Id)
            throw new ForbiddenException("No puede consultar propiedades de otro agente en este contexto.");
        await ValidateFilterAsync(filter, ct);
        var code = string.IsNullOrWhiteSpace(filter.Code) ? null : filter.Code;
        var currentId = user.Id;
        var favorites = user.IsInRole(Roles.Client) && !string.IsNullOrWhiteSpace(user.Id)
            ? (await work.Repository<Favorite>().ListAsync(item => item.ClientId == user.Id, ct))
                .Select(item => item.PropertyId).ToHashSet()
            : [];
        var properties = await work.Repository<Property>().ListAsync(property =>
            (scope == "api" ||
                scope == "agent" && property.AgentId == currentId &&
                    (property.Status == PropertyStatus.Available || property.Status == PropertyStatus.Sold) ||
                scope == "maintenance" && property.AgentId == currentId && property.Status == PropertyStatus.Available ||
                (scope == "public" || scope == "favorites") && property.Status == PropertyStatus.Available) &&
            (agentId == null || property.AgentId == agentId) &&
            (code == null || property.Code == code) &&
            (!filter.PropertyTypeId.HasValue || property.PropertyTypeId == filter.PropertyTypeId) &&
            (!filter.MinPrice.HasValue || property.Price >= filter.MinPrice) &&
            (!filter.MaxPrice.HasValue || property.Price <= filter.MaxPrice) &&
            (!filter.Bedrooms.HasValue || property.Bedrooms == filter.Bedrooms) &&
            (!filter.Bathrooms.HasValue || property.Bathrooms == filter.Bathrooms) &&
            (scope != "favorites" || favorites.Contains(property.Id)), ct);
        var agents = new Dictionary<string, UserDto>();
        var result = new List<PropertyDto>();
        foreach (var property in properties.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id))
        {
            if (!agents.TryGetValue(property.AgentId, out var agent))
            {
                agent = await accounts.GetAsync(property.AgentId, ct);
                agents.Add(property.AgentId, agent);
            }
            if ((scope is "public" or "favorites") && (!agent.IsActive || agent.Role != Roles.Agent)) continue;
            result.Add(ToDto(property, agent, favorites.Contains(property.Id)));
        }
        return result;
    }

    public async Task<PropertyDto> GetAsync(int id, string scope = "public", CancellationToken ct = default)
    {
        if (scope is not ("public" or "client" or "agent" or "api")) throw new BusinessException("Contexto no válido.");
        if (scope == "client") ServiceRules.RequireRole(user, Roles.Client);
        else AuthorizeScope(scope);
        var missing = scope == "api"
            ? "La propiedad solicitada no existe."
            : "La propiedad solicitada no existe o no se encuentra disponible.";
        Property property;
        try { property = await ServiceRules.PropertyAsync(work, id, ct); }
        catch (NotFoundException) { throw new NotFoundException(missing); }
        var clientHistory = scope == "client" && property.Status == PropertyStatus.Sold &&
            ((await work.Repository<Offer>().ListAsync(offer => offer.PropertyId == id && offer.ClientId == user.Id, ct)).Count != 0 ||
             (await work.Repository<Conversation>().ListAsync(conversation => conversation.PropertyId == id && conversation.ClientId == user.Id, ct)).Count != 0);
        if (!clientHistory && !InScope(property, scope == "client" ? "public" : scope))
            throw new NotFoundException(missing);
        var agent = await accounts.GetAsync(property.AgentId, ct);
        if ((scope is "public" or "client") && !clientHistory && (!agent.IsActive || agent.Role != Roles.Agent))
            throw new NotFoundException(missing);
        var favorite = user.IsInRole(Roles.Client) && !string.IsNullOrWhiteSpace(user.Id) &&
            (await work.Repository<Favorite>().ListAsync(item => item.ClientId == user.Id && item.PropertyId == id, ct)).Count != 0;
        return ToDto(property, agent, favorite);
    }

    public async Task<PropertyDto> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        ServiceRules.RequireApi(user);
        ServiceRules.ValidateCode(code);
        var property = (await work.Repository<Property>().ListAsync(item => item.Code == code, ct)).SingleOrDefault()
            ?? throw new NotFoundException("No existe una propiedad registrada con el código enviado.");
        return ToDto(property, await accounts.GetAsync(property.AgentId, ct), false);
    }

    public async Task<int> CreateAsync(PropertyInput input, IReadOnlyList<FileUpload> images, CancellationToken ct = default)
    {
        var agentId = ServiceRules.RequireRole(user, Roles.Agent);
        ValidateInput(input, images);
        if (input.RetainedImageIds.Count != 0) throw new BusinessException("Una propiedad nueva no puede retener imágenes.");
        var property = new Property { AgentId = agentId };
        var savedKeys = new List<string>();
        try
        {
            await work.ExecuteAsync(async () =>
            {
                var improvements = await ValidateCatalogsAsync(input, ct);
                // Persistence also enforces uniqueness; bound candidate generation rather than looping forever.
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
                    if ((await work.Repository<Property>().ListAsync(item => item.Code == code, ct)).Count != 0) continue;
                    property.Code = code;
                    break;
                }
                if (property.Code.Length == 0) throw new BusinessException("No se pudo generar un código único. Intente de nuevo.");
                ApplyInput(property, input, improvements);
                await AddImagesAsync(property, images, savedKeys, ct);
                work.Repository<Property>().Add(property);
            }, ct);
        }
        catch (Exception error)
        {
            var cleanupErrors = await ServiceRules.DeleteFilesAsync(files, savedKeys);
            if (cleanupErrors.Count != 0)
                throw new AggregateException("La creación falló y algunas imágenes no pudieron compensarse.", new[] { error }.Concat(cleanupErrors));
            throw;
        }
        return property.Id;
    }

    public async Task UpdateAsync(int id, PropertyInput input, IReadOnlyList<FileUpload> images, CancellationToken ct = default)
    {
        var agentId = ServiceRules.RequireRole(user, Roles.Agent);
        ValidateInput(input, images);
        var savedKeys = new List<string>();
        try
        {
            await work.ExecuteAsync(async () =>
            {
                await work.LockPropertyAsync(id, ct);
                var property = await ServiceRules.PropertyAsync(work, id, ct);
                property.EnsureOwner(agentId, "No tiene permisos para modificar esta propiedad.");
                property.EnsureAvailable("No se puede modificar una propiedad que ya fue vendida.");
                if (input.RetainedImageIds.Any(imageId => property.Images.All(image => image.Id != imageId)))
                    throw new BusinessException("Solo puede conservar imágenes de esta propiedad.");
                var improvements = await ValidateCatalogsAsync(input, ct);
                ApplyInput(property, input, improvements);
                foreach (var image in property.Images.Where(image => !input.RetainedImageIds.Contains(image.Id)).ToList())
                {
                    property.Images.Remove(image);
                    work.Repository<PropertyImage>().Remove(image);
                }
                property.Images = property.Images.OrderBy(image => image.SortOrder).ThenBy(image => image.Id).ToList();
                for (var index = 0; index < property.Images.Count; index++) property.Images[index].SortOrder = index;
                await AddImagesAsync(property, images, savedKeys, ct);
            }, ct);
        }
        catch (Exception error)
        {
            var cleanupErrors = await ServiceRules.DeleteFilesAsync(files, savedKeys);
            if (cleanupErrors.Count != 0)
                throw new AggregateException("La actualización falló y algunas imágenes no pudieron compensarse.", new[] { error }.Concat(cleanupErrors));
            throw;
        }
        // Infrastructure queues deleted image files durably in the same transaction.
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var agentId = ServiceRules.RequireRole(user, Roles.Agent);
        await work.ExecuteAsync(async () =>
        {
            await work.LockPropertyAsync(id, ct);
            var property = await ServiceRules.PropertyAsync(work, id, ct);
            property.EnsureOwner(agentId, "No tiene permisos para eliminar esta propiedad.");
            property.EnsureAvailable("No se puede eliminar una propiedad que ya fue vendida.");
            await work.DeletePropertiesAsync([id], ct);
        }, ct);
    }

    private void AuthorizeScope(string scope)
    {
        switch (scope)
        {
            case "public": break;
            case "agent":
            case "maintenance": ServiceRules.RequireRole(user, Roles.Agent); break;
            case "favorites": ServiceRules.RequireRole(user, Roles.Client); break;
            case "api": ServiceRules.RequireApi(user); break;
            default: throw new BusinessException("Contexto no válido.");
        }
    }

    private bool InScope(Property property, string scope) => scope switch
    {
        "public" or "favorites" => property.Status == PropertyStatus.Available,
        "agent" => property.AgentId == user.Id && property.Status is PropertyStatus.Available or PropertyStatus.Sold,
        "maintenance" => property.AgentId == user.Id && property.Status == PropertyStatus.Available,
        "api" => true,
        _ => false
    };

    private async Task ValidateFilterAsync(PropertyFilter filter, CancellationToken ct)
    {
        if (filter is null) throw new BusinessException("Filtros no válidos.");
        if (!string.IsNullOrWhiteSpace(filter.Code)) ServiceRules.ValidateCode(filter.Code);
        if (filter.MinPrice < 0 || filter.MaxPrice < 0 || filter.MinPrice > filter.MaxPrice ||
            filter.Bedrooms < 0 || filter.Bathrooms < 0)
            throw new BusinessException("Los rangos de búsqueda no son válidos.");
        if (filter.PropertyTypeId.HasValue)
            await ServiceRules.CatalogAsync(work, filter.PropertyTypeId.Value, CatalogKind.PropertyTypes, ct);
    }

    private static void ValidateInput(PropertyInput input, IReadOnlyList<FileUpload> images)
    {
        const decimal maxValue = 9999999999999999.99m;
        if (input is null || images is null || input.PropertyTypeId <= 0 || input.SaleTypeId <= 0 ||
            input.Price <= 0 || input.Price > maxValue || decimal.Round(input.Price, 2) != input.Price ||
            input.Size <= 0 || input.Size > maxValue || decimal.Round(input.Size, 2) != input.Size ||
            input.Bedrooms < 0 || input.Bathrooms < 0 ||
            string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 4000)
            throw new BusinessException("Los datos de la propiedad no son válidos.");
        if (input.ImprovementIds is null || input.ImprovementIds.Count == 0 ||
            input.ImprovementIds.Any(id => id <= 0) || input.ImprovementIds.Distinct().Count() != input.ImprovementIds.Count)
            throw new BusinessException("Seleccione mejoras distintas y válidas.");
        if (input.RetainedImageIds is null || input.RetainedImageIds.Any(id => id <= 0) ||
            input.RetainedImageIds.Distinct().Count() != input.RetainedImageIds.Count)
            throw new BusinessException("Las imágenes conservadas no son válidas.");
        if (input.RetainedImageIds.Count + images.Count is < 1 or > 4 || images.Any(image => image is null))
            throw new BusinessException("La propiedad debe tener entre una y cuatro imágenes.");
    }

    private async Task<List<CatalogItem>> ValidateCatalogsAsync(PropertyInput input, CancellationToken ct)
    {
        await ServiceRules.CatalogAsync(work, input.PropertyTypeId, CatalogKind.PropertyTypes, ct);
        await ServiceRules.CatalogAsync(work, input.SaleTypeId, CatalogKind.SaleTypes, ct);
        var improvements = new List<CatalogItem>();
        foreach (var id in input.ImprovementIds)
            improvements.Add(await ServiceRules.CatalogAsync(work, id, CatalogKind.Improvements, ct));
        return improvements;
    }

    private static void ApplyInput(Property property, PropertyInput input, List<CatalogItem> improvements)
    {
        property.PropertyTypeId = input.PropertyTypeId;
        property.SaleTypeId = input.SaleTypeId;
        property.Price = input.Price;
        property.Size = input.Size;
        property.Bedrooms = input.Bedrooms;
        property.Bathrooms = input.Bathrooms;
        property.Description = input.Description.Trim();
        property.Improvements.RemoveAll(join => !input.ImprovementIds.Contains(join.ImprovementId));
        foreach (var improvement in improvements.Where(item => property.Improvements.All(join => join.ImprovementId != item.Id)))
            property.Improvements.Add(new PropertyImprovement
            {
                PropertyId = property.Id, ImprovementId = improvement.Id, Improvement = improvement
            });
    }

    private async Task AddImagesAsync(Property property, IReadOnlyList<FileUpload> images, List<string> savedKeys, CancellationToken ct)
    {
        foreach (var image in images)
        {
            var key = await files.SaveImageAsync(image, ct);
            savedKeys.Add(key);
            property.Images.Add(new PropertyImage { PropertyId = property.Id, FileKey = key, SortOrder = property.Images.Count });
        }
    }

    private PropertyDto ToDto(Property property, UserDto agent, bool favorite)
        => new(property.Id, property.Code, property.PropertyTypeId, property.PropertyType.Name,
            property.SaleTypeId, property.SaleType.Name, property.Price, property.Size, property.Bedrooms,
            property.Bathrooms, property.Description, property.AgentId, agent.FullName, agent.Email, agent.Phone,
            agent.Photo, property.Status, property.CreatedAt,
            property.Images.OrderBy(image => image.SortOrder).ThenBy(image => image.Id).Select(mapper.Map<ImageDto>).ToArray(),
            property.Improvements.Select(join => mapper.Map<CatalogDto>(join.Improvement)).ToArray(), favorite);
}
