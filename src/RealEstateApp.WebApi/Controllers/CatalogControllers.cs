using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Application.Cqrs.Commands;
using RealEstateApp.Application.Cqrs.Queries;
using RealEstateApp.Domain;

namespace RealEstateApp.WebApi.Controllers;

[Route("api/property-types")]
public sealed class PropertyTypesController(
    IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>> list,
    IQueryHandler<GetCatalogQuery, CatalogDto> get,
    ICommandHandler<CreateCatalogCommand, CatalogDto> create,
    ICommandHandler<UpdateCatalogCommand, CatalogDto> update,
    ICommandHandler<DeleteCatalogCommand, Unit> delete)
    : CatalogControllerBase(CatalogKind.PropertyTypes, list, get, create, update, delete);

[Route("api/sale-types")]
public sealed class SaleTypesController(
    IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>> list,
    IQueryHandler<GetCatalogQuery, CatalogDto> get,
    ICommandHandler<CreateCatalogCommand, CatalogDto> create,
    ICommandHandler<UpdateCatalogCommand, CatalogDto> update,
    ICommandHandler<DeleteCatalogCommand, Unit> delete)
    : CatalogControllerBase(CatalogKind.SaleTypes, list, get, create, update, delete);

[Route("api/improvements")]
public sealed class ImprovementsController(
    IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>> list,
    IQueryHandler<GetCatalogQuery, CatalogDto> get,
    ICommandHandler<CreateCatalogCommand, CatalogDto> create,
    ICommandHandler<UpdateCatalogCommand, CatalogDto> update,
    ICommandHandler<DeleteCatalogCommand, Unit> delete)
    : CatalogControllerBase(CatalogKind.Improvements, list, get, create, update, delete);
