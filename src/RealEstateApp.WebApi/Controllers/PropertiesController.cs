using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Application.Cqrs.Queries;
using RealEstateApp.Domain;
using RealEstateApp.WebApi.Contracts;

namespace RealEstateApp.WebApi.Controllers;

[ApiController]
[Authorize(Roles = Roles.Admin + "," + Roles.Developer)]
[Route("api/properties")]
public sealed class PropertiesController(
    IQueryHandler<ListPropertiesQuery, IReadOnlyList<PropertyResponse>> list,
    IQueryHandler<GetPropertyQuery, PropertyResponse> get,
    IQueryHandler<GetPropertyByCodeQuery, PropertyResponse> getByCode) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PropertyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<IReadOnlyList<PropertyResponse>>> List([FromQuery] PropertySearchRequest request, CancellationToken ct)
    {
        var properties = await list.HandleAsync(new ListPropertiesQuery(request.ToFilter()), ct);
        return properties.Count == 0 ? NoContent() : Ok(properties);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<PropertyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyResponse>> Get([FromRoute, Range(1, int.MaxValue)] int id, CancellationToken ct) =>
        Ok(await get.HandleAsync(new GetPropertyQuery(id), ct));

    [HttpGet("by-code/{code}")]
    [ProducesResponseType<PropertyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyResponse>> GetByCode(
        [FromRoute, Required, StringLength(6, MinimumLength = 6), RegularExpression("^[0-9]{6}$")] string code,
        CancellationToken ct) => Ok(await getByCode.HandleAsync(new GetPropertyByCodeQuery(code), ct));
}
