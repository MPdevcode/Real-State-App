using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

public abstract class PropertyListingController(IPropertyService properties, ICatalogService catalogs, IMapper mapper) : Controller
{
    protected IPropertyService Properties { get; } = properties;
    protected ICatalogService Catalogs { get; } = catalogs;
    protected IMapper Mapper { get; } = mapper;
    protected async Task<PropertyListingViewModel> ListingAsync(PropertyFilterViewModel filter, string scope,
        string title, string subtitle, string action, string? agentId = null, CancellationToken ct = default)
    {
        var model = new PropertyListingViewModel
        {
            Filter = filter, Title = title, Subtitle = subtitle, AgentId = agentId,
            ListingAction = action, ListingController = ControllerContext.ActionDescriptor.ControllerName,
            Maintenance = scope == "maintenance",
            PropertyTypes = Mapper.Map<List<CatalogViewModel>>(await Catalogs.For(CatalogKind.PropertyTypes).ListAsync(ct))
        };
        if (filter.PropertyTypeId.HasValue && !model.PropertyTypes.Any(x => x.Id == filter.PropertyTypeId))
            ModelState.AddModelError("Filter.PropertyTypeId", "El tipo de propiedad seleccionado no existe.");
        if (ModelState.IsValid)
        {
            try
            {
                model.Properties = Mapper.Map<List<PropertyViewModel>>(
                    (await Properties.SearchAsync(Mapper.Map<PropertyFilter>(filter), scope, agentId, ct))
                    .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToList());
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var filtered = filter.PropertyTypeId.HasValue || filter.MinPrice.HasValue || filter.MaxPrice.HasValue
            || filter.Bedrooms.HasValue || filter.Bathrooms.HasValue;
        if (!string.IsNullOrWhiteSpace(filter.Code))
            model.EmptyMessage = "No se encontró ninguna propiedad disponible con el código ingresado.";
        else if (filtered)
            model.EmptyMessage = "No se encontraron propiedades disponibles con los filtros seleccionados.";
        else if (scope == "favorites")
            model.EmptyMessage = "No tiene propiedades favoritas disponibles en este momento.";
        else if (scope == "agent")
            model.EmptyMessage = "No tiene propiedades registradas en este momento.";
        else if (scope == "maintenance")
            model.EmptyMessage = "No tiene propiedades disponibles registradas en este momento.";
        else if (agentId is not null)
            model.EmptyMessage = "Este agente no tiene propiedades disponibles en este momento.";
        else
            model.EmptyMessage = "No se encontraron propiedades disponibles en este momento.";
        return model;
    }
}
