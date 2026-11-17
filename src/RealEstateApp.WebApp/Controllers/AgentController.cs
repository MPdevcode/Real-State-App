using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.Support;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[Authorize(Roles = Roles.Agent), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AgentController(IPropertyService properties, ICatalogService catalogs, IAccountService accounts,
    IConversationService conversations, IOfferService offers, IMapper mapper) : PropertyListingController(properties, catalogs, mapper)
{
    [HttpGet]
    public async Task<IActionResult> Index([Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
        => View("PropertyListing", await ListingAsync(filter, "agent", "Su portafolio, al día.",
            "Propiedades disponibles y vendidas. Conversaciones y oportunidades en un mismo lugar.", nameof(Index), ct: ct));

    [HttpGet]
    public async Task<IActionResult> Maintenance([Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
        => View("PropertyListing", await ListingAsync(filter, "maintenance", "Mantenimiento de propiedades", "Administre sus propiedades disponibles. Las ventas conservan su historial en el Home.", nameof(Maintenance), ct: ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? clientId, CancellationToken ct) => View(await DetailsAsync(id, clientId, ct));

    [HttpPost]
    public async Task<IActionResult> Reply(int id, string clientId, [Bind(Prefix = "Message")] MessageInputViewModel message, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await conversations.SendAsync(id, clientId, message.Body, ct);
                TempData["Success"] = "Su respuesta fue enviada correctamente.";
                return RedirectToAction(nameof(Details), new { id, clientId });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var model = await DetailsAsync(id, clientId, ct);
        model.Message = message;
        return View("Details", model);
    }

    [HttpPost]
    public async Task<IActionResult> Respond(int id, string clientId, OfferResponseViewModel input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await Properties.GetAsync(id, "agent", ct);
                var relevantOffers = await offers.ListAsync(id, clientId, ct);
                if (!relevantOffers.Any(x => x.Id == input.OfferId && x.ClientId == clientId))
                    throw new NotFoundException("La oferta no pertenece a esta propiedad y cliente.");
                await offers.RespondAsync(input.OfferId, input.Accept!.Value, ct);
                TempData["Success"] = input.Accept.Value ? "La oferta fue aceptada correctamente y la propiedad fue marcada como vendida." : "La oferta fue rechazada correctamente.";
            }
            catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        }
        else TempData["Error"] = "Seleccione una oferta y una respuesta válidas.";
        return RedirectToAction(nameof(Details), new { id, clientId });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new PropertyInputViewModel();
        await OptionsAsync(model, ct);
        return View("PropertyForm", model);
    }

    [HttpPost, RequestSizeLimit(22 * 1024 * 1024)]
    public async Task<IActionResult> Create(PropertyInputViewModel model, CancellationToken ct)
    {
        if (model.RetainedImageIds.Count != 0) ModelState.AddModelError("", "Una propiedad nueva no puede conservar imágenes existentes.");
        if (ModelState.IsValid)
        {
            try
            {
                using var uploads = new UploadBatch(model.Files);
                await Properties.CreateAsync(Mapper.Map<PropertyInput>(model), uploads.Files, ct);
                TempData["Success"] = "La propiedad fue creada correctamente.";
                return RedirectToAction(nameof(Maintenance));
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await OptionsAsync(model, ct);
        return View("PropertyForm", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var property = await EditablePropertyAsync(id, "modificar", ct);
        var model = Mapper.Map<PropertyInputViewModel>(property);
        await OptionsAsync(model, ct);
        return View("PropertyForm", model);
    }

    [HttpPost, RequestSizeLimit(22 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, PropertyInputViewModel model, CancellationToken ct)
    {
        var property = await EditablePropertyAsync(id, "modificar", ct);
        model.Id = property.Id;
        model.Code = property.Code;
        model.ExistingImages = Mapper.Map<List<ImageViewModel>>(property.Images);
        if (ModelState.IsValid)
        {
            try
            {
                using var uploads = new UploadBatch(model.Files);
                await Properties.UpdateAsync(id, Mapper.Map<PropertyInput>(model), uploads.Files, ct);
                TempData["Success"] = "La propiedad fue actualizada correctamente.";
                return RedirectToAction(nameof(Maintenance));
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await OptionsAsync(model, ct);
        return View("PropertyForm", model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var property = await EditablePropertyAsync(id, "eliminar", ct);
        return View("Confirm", new ConfirmationViewModel
        {
            Title = $"Eliminar propiedad {property.Code}", Message = "¿Está seguro que desea eliminar esta propiedad?",
            Consequences = "Se eliminarán sus imágenes, ofertas, conversaciones y favoritos asociados. Esta acción no se puede deshacer.",
            Controller = "Agent", Id = id.ToString(), PostAction = nameof(Delete), CancelAction = nameof(Maintenance), Destructive = true
        });
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken ct)
    {
        try
        {
            await Properties.DeleteAsync(id, ct);
            TempData["Success"] = "La propiedad fue eliminada correctamente.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Maintenance));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct)
        => View(Mapper.Map<ProfileInputViewModel>(await accounts.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct)));

    [HttpPost, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Profile(ProfileInputViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                using var uploads = new UploadBatch(model.Photo is null ? [] : [model.Photo]);
                await accounts.UpdateProfileAsync(Mapper.Map<ProfileInput>(model), uploads.Files.SingleOrDefault(), ct);
                TempData["Success"] = "Su perfil fue actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        model.CurrentPhoto = (await accounts.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct)).Photo;
        return View(model);
    }

    private async Task OptionsAsync(PropertyInputViewModel model, CancellationToken ct)
    {
        model.PropertyTypes = Mapper.Map<List<CatalogViewModel>>(await Catalogs.For(CatalogKind.PropertyTypes).ListAsync(ct));
        model.SaleTypes = Mapper.Map<List<CatalogViewModel>>(await Catalogs.For(CatalogKind.SaleTypes).ListAsync(ct));
        model.Improvements = Mapper.Map<List<CatalogViewModel>>(await Catalogs.For(CatalogKind.Improvements).ListAsync(ct));
    }

    private async Task<PropertyDto> EditablePropertyAsync(int id, string action, CancellationToken ct)
    {
        var property = await Properties.GetAsync(id, "agent", ct);
        if (property.Status != PropertyStatus.Available)
            throw new BusinessException($"No se puede {action} una propiedad que ya fue vendida.");
        return property;
    }

    private async Task<PropertyDetailsViewModel> DetailsAsync(int id, string? clientId, CancellationToken ct)
    {
        var property = await Properties.GetAsync(id, "agent", ct);
        var model = new PropertyDetailsViewModel
        {
            Property = Mapper.Map<PropertyViewModel>(property), SelectedClientId = clientId,
            Conversations = Mapper.Map<List<ConversationViewModel>>(await conversations.ListAsync(id, ct)),
            Offers = Mapper.Map<List<OfferViewModel>>((await offers.ListAsync(id, ct: ct)).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToList())
        };
        if (clientId is not null && !model.Conversations.Any(x => x.ClientId == clientId) && !model.Offers.Any(x => x.ClientId == clientId))
            throw new NotFoundException("El cliente no tiene conversaciones ni ofertas para esta propiedad.");
        return model;
    }
}
