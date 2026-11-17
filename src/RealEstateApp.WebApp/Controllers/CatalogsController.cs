using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[Authorize(Roles = Roles.Admin), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CatalogsController(ICatalogService catalogs, IMapper mapper, ILogger<CatalogsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CatalogKind kind, CancellationToken ct)
        => View(new CatalogPageViewModel { Kind = kind, Items = mapper.Map<List<CatalogViewModel>>(await Service(kind).ListAsync(ct)) });

    [HttpGet]
    public IActionResult Create(CatalogKind kind)
    {
        Service(kind);
        return View("CatalogForm", new CatalogPageViewModel { Kind = kind });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CatalogKind kind, [Bind(Prefix = "Input")] CatalogInputViewModel input, CancellationToken ct)
    {
        var service = Service(kind);
        if (ModelState.IsValid)
        {
            try
            {
                await service.CreateAsync(mapper.Map<CatalogInput>(input), ct);
                TempData["Success"] = kind switch
                {
                    CatalogKind.PropertyTypes => "El tipo de propiedad fue creado correctamente.",
                    CatalogKind.SaleTypes => "El tipo de venta fue creado correctamente.",
                    _ => "La mejora fue creada correctamente."
                };
                return RedirectToAction(nameof(Index), new { kind });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View("CatalogForm", new CatalogPageViewModel { Kind = kind, Input = input });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(CatalogKind kind, int id, CancellationToken ct)
        => View("CatalogForm", new CatalogPageViewModel { Kind = kind, Id = id, Input = mapper.Map<CatalogInputViewModel>(await Service(kind).GetAsync(id, ct)) });

    [HttpPost]
    public async Task<IActionResult> Edit(CatalogKind kind, int id, [Bind(Prefix = "Input")] CatalogInputViewModel input, CancellationToken ct)
    {
        var service = Service(kind);
        if (ModelState.IsValid)
        {
            try
            {
                await service.UpdateAsync(id, mapper.Map<CatalogInput>(input), ct);
                TempData["Success"] = kind switch
                {
                    CatalogKind.PropertyTypes => "El tipo de propiedad fue actualizado correctamente.",
                    CatalogKind.SaleTypes => "El tipo de venta fue actualizado correctamente.",
                    _ => "La mejora fue actualizada correctamente."
                };
                return RedirectToAction(nameof(Index), new { kind });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View("CatalogForm", new CatalogPageViewModel { Kind = kind, Id = id, Input = input });
    }

    [HttpGet]
    public async Task<IActionResult> Delete(CatalogKind kind, int id, CancellationToken ct)
    {
        var item = await Service(kind).GetAsync(id, ct);
        return View("Confirm", new ConfirmationViewModel
        {
            Title = $"Eliminar {item.Name}",
            Message = kind switch
            {
                CatalogKind.PropertyTypes => "¿Está seguro que desea eliminar este tipo de propiedad y todas las propiedades asociadas?",
                CatalogKind.SaleTypes => "¿Está seguro que desea eliminar este tipo de venta y las propiedades asociadas?",
                _ => "¿Está seguro que desea eliminar esta mejora?"
            },
            Consequences = kind == CatalogKind.Improvements
                ? $"Se removerá esta mejora de {item.PropertyCount} propiedades. Las propiedades y sus historiales NO se eliminarán."
                : $"Se eliminarán {item.PropertyCount} propiedades asociadas y sus imágenes, ofertas, conversaciones y favoritos. Esta acción es irreversible.",
            Controller = "Catalogs", Kind = kind, Id = id.ToString(), PostAction = nameof(Delete), Destructive = true
        });
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(CatalogKind kind, int id, CancellationToken ct)
    {
        var service = Service(kind);
        try
        {
            await service.DeleteAsync(id, ct);
            TempData["Success"] = kind switch
            {
                CatalogKind.PropertyTypes => "El tipo de propiedad fue eliminado correctamente.",
                CatalogKind.SaleTypes => "El tipo de venta fue eliminado correctamente.",
                _ => "La mejora fue eliminada correctamente."
            };
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Catalog deletion failed for {Kind}", kind);
            TempData["Error"] = kind switch
            {
                CatalogKind.PropertyTypes => "No fue posible eliminar el tipo de propiedad. Intente nuevamente más tarde.",
                CatalogKind.SaleTypes => "No fue posible eliminar el tipo de venta. Intente nuevamente más tarde.",
                _ => "No fue posible eliminar la mejora. Intente nuevamente más tarde."
            };
        }
        return RedirectToAction(nameof(Index), new { kind });
    }

    private IGenericService<CatalogDto, CatalogInput> Service(CatalogKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new NotFoundException("El catálogo solicitado no existe.");
        return catalogs.For(kind);
    }
}
