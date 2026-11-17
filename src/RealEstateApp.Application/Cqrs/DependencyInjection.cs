using Microsoft.Extensions.DependencyInjection;
using RealEstateApp.Application.Cqrs.Commands;
using RealEstateApp.Application.Cqrs.Queries;

namespace RealEstateApp.Application.Cqrs;

public static class CqrsDependencyInjection
{
    public static IServiceCollection AddCqrs(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<LoginCommand, TokenResult>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<CreateAccountCommand, AccountResponse>, CreateAccountCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeAgentStatusCommand, Unit>, ChangeAgentStatusCommandHandler>();
        services.AddScoped<ICommandHandler<CreateCatalogCommand, CatalogDto>, CreateCatalogCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateCatalogCommand, CatalogDto>, UpdateCatalogCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteCatalogCommand, Unit>, DeleteCatalogCommandHandler>();
        services.AddScoped<IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>, ListAgentsQueryHandler>();
        services.AddScoped<IQueryHandler<GetAgentQuery, AgentResponse>, GetAgentQueryHandler>();
        services.AddScoped<IQueryHandler<ListAgentPropertiesQuery, IReadOnlyList<PropertyResponse>>, ListAgentPropertiesQueryHandler>();
        services.AddScoped<IQueryHandler<ListPropertiesQuery, IReadOnlyList<PropertyResponse>>, ListPropertiesQueryHandler>();
        services.AddScoped<IQueryHandler<GetPropertyQuery, PropertyResponse>, GetPropertyQueryHandler>();
        services.AddScoped<IQueryHandler<GetPropertyByCodeQuery, PropertyResponse>, GetPropertyByCodeQueryHandler>();
        services.AddScoped<IQueryHandler<ListCatalogQuery, IReadOnlyList<CatalogDto>>, ListCatalogQueryHandler>();
        services.AddScoped<IQueryHandler<GetCatalogQuery, CatalogDto>, GetCatalogQueryHandler>();
        return services;
    }
}
