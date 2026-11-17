using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs.Queries;

public sealed record ListAgentsQuery : IQuery<IReadOnlyList<AgentResponse>>;
public sealed record GetAgentQuery(string Id) : IQuery<AgentResponse>;
public sealed record ListAgentPropertiesQuery(string Id, PropertyFilter Filter) : IQuery<IReadOnlyList<PropertyResponse>>;

public sealed class ListAgentsQueryHandler(IAccountService accounts)
    : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>
{
    public async Task<IReadOnlyList<AgentResponse>> HandleAsync(ListAgentsQuery query, CancellationToken ct = default) =>
        (await accounts.ListAsync(Roles.Agent, onlyActive: false, ct: ct)).Select(AgentResponse.From).ToList();
}

public sealed class GetAgentQueryHandler(IAccountService accounts)
    : IQueryHandler<GetAgentQuery, AgentResponse>
{
    public async Task<AgentResponse> HandleAsync(GetAgentQuery query, CancellationToken ct = default)
    {
        var user = await accounts.GetAsync(AgentIdentity.Parse(query.Id), ct);
        if (user.Role != Roles.Agent) throw new NotFoundException("El agente solicitado no existe.");
        return AgentResponse.From(user);
    }
}

public sealed class ListAgentPropertiesQueryHandler(IAccountService accounts, IPropertyService properties)
    : IQueryHandler<ListAgentPropertiesQuery, IReadOnlyList<PropertyResponse>>
{
    public async Task<IReadOnlyList<PropertyResponse>> HandleAsync(ListAgentPropertiesQuery query, CancellationToken ct = default)
    {
        var id = AgentIdentity.Parse(query.Id);
        var user = await accounts.GetAsync(id, ct);
        if (user.Role != Roles.Agent) throw new NotFoundException("El agente solicitado no existe.");

        return (await properties.SearchAsync(query.Filter, scope: "api", agentId: id, ct: ct))
            .Select(PropertyResponse.From).ToList();
    }
}
