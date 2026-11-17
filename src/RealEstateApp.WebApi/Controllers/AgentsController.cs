using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application.Cqrs;
using RealEstateApp.Application.Cqrs.Commands;
using RealEstateApp.Application.Cqrs.Queries;
using RealEstateApp.Domain;
using RealEstateApp.WebApi.Contracts;

namespace RealEstateApp.WebApi.Controllers;

[ApiController]
[Authorize(Roles = Roles.Admin + "," + Roles.Developer)]
[Route("api/agents")]
public sealed class AgentsController(
    IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>> list,
    IQueryHandler<GetAgentQuery, AgentResponse> get,
    IQueryHandler<ListAgentPropertiesQuery, IReadOnlyList<PropertyResponse>> properties,
    ICommandHandler<ChangeAgentStatusCommand, Unit> changeStatus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AgentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<IReadOnlyList<AgentResponse>>> List(CancellationToken ct)
    {
        var agents = await list.HandleAsync(new ListAgentsQuery(), ct);
        return agents.Count == 0 ? NoContent() : Ok(agents);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<AgentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentResponse>> Get(
        [FromRoute, Required, StringLength(36, MinimumLength = 36), RegularExpression(RouteValidation.AgentIdPattern)] string id,
        CancellationToken ct) =>
        Ok(await get.HandleAsync(new GetAgentQuery(id), ct));

    [HttpGet("{id}/properties")]
    [ProducesResponseType<IReadOnlyList<PropertyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PropertyResponse>>> Properties(
        [FromRoute, Required, StringLength(36, MinimumLength = 36), RegularExpression(RouteValidation.AgentIdPattern)] string id,
        [FromQuery] PropertySearchRequest request, CancellationToken ct)
    {
        var result = await properties.HandleAsync(new ListAgentPropertiesQuery(id, request.ToFilter()), ct);
        return result.Count == 0 ? NoContent() : Ok(result);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPatch("{id}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(
        [FromRoute, Required, StringLength(36, MinimumLength = 36), RegularExpression(RouteValidation.AgentIdPattern)] string id,
        [FromBody] ChangeAgentStatusRequest request, CancellationToken ct)
    {
        await changeStatus.HandleAsync(new ChangeAgentStatusCommand(id, request.IsActive!.Value), ct);
        return NoContent();
    }
}
