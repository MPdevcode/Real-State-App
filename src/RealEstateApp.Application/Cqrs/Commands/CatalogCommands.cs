using RealEstateApp.Domain;

namespace RealEstateApp.Application.Cqrs.Commands;

public sealed record CreateCatalogCommand(CatalogKind Kind, CatalogInput Input) : ICommand<CatalogDto>;
public sealed record UpdateCatalogCommand(CatalogKind Kind, int Id, CatalogInput Input) : ICommand<CatalogDto>;
public sealed record DeleteCatalogCommand(CatalogKind Kind, int Id) : ICommand<Unit>;

public sealed class CreateCatalogCommandHandler(ICatalogService catalogs)
    : ICommandHandler<CreateCatalogCommand, CatalogDto>
{
    public Task<CatalogDto> HandleAsync(CreateCatalogCommand command, CancellationToken ct = default) =>
        catalogs.For(command.Kind).CreateAsync(command.Input, ct);
}

public sealed class UpdateCatalogCommandHandler(ICatalogService catalogs)
    : ICommandHandler<UpdateCatalogCommand, CatalogDto>
{
    public Task<CatalogDto> HandleAsync(UpdateCatalogCommand command, CancellationToken ct = default) =>
        catalogs.For(command.Kind).UpdateAsync(command.Id, command.Input, ct);
}

public sealed class DeleteCatalogCommandHandler(ICatalogService catalogs)
    : ICommandHandler<DeleteCatalogCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteCatalogCommand command, CancellationToken ct = default)
    {
        await catalogs.For(command.Kind).DeleteAsync(command.Id, ct);
        return default;
    }
}
