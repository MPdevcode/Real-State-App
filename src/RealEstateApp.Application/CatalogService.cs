using AutoMapper;
using RealEstateApp.Domain;

namespace RealEstateApp.Application;

public sealed class CatalogService(IUnitOfWork work, ICurrentUser user, IMapper mapper) : ICatalogService
{
    public IGenericService<CatalogDto, CatalogInput> For(CatalogKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new BusinessException("Tipo de catálogo no válido.");
        return new Catalog(work, user, mapper, kind);
    }

    private sealed class Catalog(IUnitOfWork work, ICurrentUser user, IMapper mapper, CatalogKind kind)
        : IGenericService<CatalogDto, CatalogInput>
    {
        public async Task<List<CatalogDto>> ListAsync(CancellationToken ct = default)
        {
            var items = await work.Repository<CatalogItem>().ListAsync(item => item.Kind == kind, ct);
            var properties = await work.Repository<Property>().ListAsync(ct: ct);
            return items.OrderBy(item => item.Name).Select(item => WithCount(item, properties)).ToList();
        }

        public async Task<CatalogDto> GetAsync(int id, CancellationToken ct = default)
        {
            var item = await FindAsync(id, ct);
            return WithCount(item, await work.Repository<Property>().ListAsync(ct: ct));
        }

        public async Task<CatalogDto> CreateAsync(CatalogInput input, CancellationToken ct = default)
        {
            ServiceRules.RequireRole(user, Roles.Admin);
            var item = new CatalogItem { Kind = kind };
            await work.ExecuteAsync(async () =>
            {
                await ApplyAsync(item, input, false, ct);
                work.Repository<CatalogItem>().Add(item);
            }, ct);
            return mapper.Map<CatalogDto>(item);
        }

        public async Task<CatalogDto> UpdateAsync(int id, CatalogInput input, CancellationToken ct = default)
        {
            ServiceRules.RequireRole(user, Roles.Admin);
            await work.ExecuteAsync(async () => await ApplyAsync(await FindAsync(id, ct), input, true, ct), ct);
            return await GetAsync(id, ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            ServiceRules.RequireRole(user, Roles.Admin);
            await work.ExecuteAsync(async () =>
            {
                var item = await FindAsync(id, ct);
                if (kind is CatalogKind.PropertyTypes or CatalogKind.SaleTypes)
                {
                    var properties = await work.Repository<Property>().ListAsync(property =>
                        kind == CatalogKind.PropertyTypes ? property.PropertyTypeId == id : property.SaleTypeId == id, ct);
                    await work.DeletePropertiesAsync(properties.Select(property => property.Id).ToArray(), ct);
                }
                // Improvement joins are removed by the infrastructure cascade, not their properties.
                work.Repository<CatalogItem>().Remove(item);
            }, ct);
        }

        private async Task<CatalogItem> FindAsync(int id, CancellationToken ct)
        {
            var item = await work.Repository<CatalogItem>().FindAsync(id, ct);
            return item is not null && item.Kind == kind ? item : throw new NotFoundException(MissingMessage());
        }

        private string MissingMessage() => kind switch
        {
            CatalogKind.PropertyTypes => "El tipo de propiedad solicitado no existe.",
            CatalogKind.SaleTypes => "El tipo de venta solicitado no existe.",
            _ => "La mejora solicitada no existe."
        };

        private string DuplicateMessage(bool updating) => kind switch
        {
            CatalogKind.PropertyTypes => updating
                ? "Ya existe otro tipo de propiedad registrado con este nombre."
                : "Ya existe un tipo de propiedad registrado con este nombre.",
            CatalogKind.SaleTypes => updating
                ? "Ya existe otro tipo de venta registrado con este nombre."
                : "Ya existe un tipo de venta registrado con este nombre.",
            _ => updating
                ? "Ya existe otra mejora registrada con este nombre."
                : "Ya existe una mejora registrada con este nombre."
        };

        private async Task ApplyAsync(CatalogItem item, CatalogInput input, bool updating, CancellationToken ct)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.Name)) throw new BusinessException("Debe completar todos los campos requeridos.");
            var name = input.Name.Trim().Normalize();
            var normalized = name.ToUpperInvariant();
            var description = input.Description?.Trim();
            if (name.Length > 100 || normalized.Length > 100 || string.IsNullOrEmpty(description)
                || description.Length > 2000)
                throw new BusinessException("El nombre admite hasta 100 caracteres y la descripción es requerida (máximo 2000).");
            if ((await work.Repository<CatalogItem>().ListAsync(other =>
                    other.Kind == kind && other.NormalizedName == normalized && other.Id != item.Id, ct)).Count != 0)
                throw new BusinessException(DuplicateMessage(updating));
            item.Name = name;
            item.NormalizedName = normalized;
            item.Description = description;
        }

        private CatalogDto WithCount(CatalogItem item, List<Property> properties)
            => mapper.Map<CatalogDto>(item) with
            {
                PropertyCount = properties.Count(property => kind switch
                {
                    CatalogKind.PropertyTypes => property.PropertyTypeId == item.Id,
                    CatalogKind.SaleTypes => property.SaleTypeId == item.Id,
                    _ => property.Improvements.Any(improvement => improvement.ImprovementId == item.Id)
                })
            };
    }
}
