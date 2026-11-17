using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs.Queries;

public sealed record ListCatalogQuery(CatalogKind Kind) : IQuery<IReadOnlyList<CatalogDto>>;
public sealed record GetCatalogQuery(CatalogKind Kind, int Id) : IQuery<CatalogDto>;

public sealed class ListCatalogQueryHandler(ICatalogService catalogs)
    : IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>>
{
    public async Task<IReadOnlyList<CatalogDto>> HandleAsync(ListCatalogQuery query, CancellationToken ct = default) =>
        await catalogs.For(query.Kind).ListAsync(ct);
}

public sealed class GetCatalogQueryHandler(ICatalogService catalogs)
    : IQueryHandler<GetCatalogQuery, CatalogDto>
{
    public Task<CatalogDto> HandleAsync(GetCatalogQuery query, CancellationToken ct = default) =>
        catalogs.For(query.Kind).GetAsync(query.Id, ct);
}
