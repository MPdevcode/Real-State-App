using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs.Queries;

public sealed record ListPropertiesQuery(PropertyFilter Filter) : IQuery<IReadOnlyList<PropertyResponse>>;
public sealed record GetPropertyQuery(int Id) : IQuery<PropertyResponse>;
public sealed record GetPropertyByCodeQuery(string Code) : IQuery<PropertyResponse>;

public sealed class ListPropertiesQueryHandler(IPropertyService properties)
    : IQueryHandler<ListPropertiesQuery, IReadOnlyList<PropertyResponse>>
{
    public async Task<IReadOnlyList<PropertyResponse>> HandleAsync(ListPropertiesQuery query, CancellationToken ct = default) =>
        (await properties.SearchAsync(query.Filter, scope: "api", ct: ct)).Select(PropertyResponse.From).ToList();
}

public sealed class GetPropertyQueryHandler(IPropertyService properties)
    : IQueryHandler<GetPropertyQuery, PropertyResponse>
{
    public async Task<PropertyResponse> HandleAsync(GetPropertyQuery query, CancellationToken ct = default)
    {
        if (query.Id <= 0) throw new BusinessException("El identificador debe ser positivo.");
        return PropertyResponse.From(await properties.GetAsync(query.Id, scope: "api", ct: ct));
    }
}

public sealed class GetPropertyByCodeQueryHandler(IPropertyService properties)
    : IQueryHandler<GetPropertyByCodeQuery, PropertyResponse>
{
    public async Task<PropertyResponse> HandleAsync(GetPropertyByCodeQuery query, CancellationToken ct = default)
    {
        if (query.Code.Length != 6 || query.Code.Any(c => c is < '0' or > '9'))
            throw new BusinessException("El código debe tener seis dígitos ASCII.");
        return PropertyResponse.From(await properties.GetByCodeAsync(query.Code, ct));
    }
}
