using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Application.Cqrs.Commands;
using RealEstateApp.Application.Cqrs.Queries;
using RealEstateApp.Domain;
using RealEstateApp.WebApi.Contracts;

namespace RealEstateApp.WebApi.Controllers;

[ApiController]
[Authorize(Roles = Roles.Admin + "," + Roles.Developer)]
public abstract class CatalogControllerBase(
    CatalogKind kind,
    IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>> list,
    IQueryHandler<GetCatalogQuery, CatalogDto> get,
    ICommandHandler<CreateCatalogCommand, CatalogDto> create,
    ICommandHandler<UpdateCatalogCommand, CatalogDto> update,
    ICommandHandler<DeleteCatalogCommand, Unit> delete) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<IReadOnlyList<CatalogDto>>> List(CancellationToken ct)
    {
        var items = await list.HandleAsync(new ListCatalogQuery(kind), ct);
        return items.Count == 0 ? NoContent() : Ok(items);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<CatalogDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogDto>> Get([FromRoute, Range(1, int.MaxValue)] int id, CancellationToken ct) =>
        Ok(await get.HandleAsync(new GetCatalogQuery(kind, id), ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost]
    [ProducesResponseType<CatalogDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CatalogDto>> Create([FromBody] CatalogRequest request, CancellationToken ct)
    {
        var item = await create.HandleAsync(new CreateCatalogCommand(kind, request.ToInput()), ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id}")]
    [ProducesResponseType<CatalogDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogDto>> Update([FromRoute, Range(1, int.MaxValue)] int id,
        [FromBody] CatalogRequest request, CancellationToken ct) =>
        Ok(await update.HandleAsync(new UpdateCatalogCommand(kind, id, request.ToInput()), ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute, Range(1, int.MaxValue)] int id, CancellationToken ct)
    {
        await delete.HandleAsync(new DeleteCatalogCommand(kind, id), ct);
        return NoContent();
    }
}
