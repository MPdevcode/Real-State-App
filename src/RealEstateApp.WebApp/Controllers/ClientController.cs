using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[Authorize(Roles = Roles.Client), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClientController(IPropertyService properties, ICatalogService catalogs, IFavoriteService favorites,
    IConversationService conversations, IOfferService offers, IMapper mapper) : PropertyListingController(properties, catalogs, mapper)
{
    [HttpGet]
    public async Task<IActionResult> Index([Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
        => View("PropertyListing", await ListingAsync(filter, "public", "Su próximo hogar empieza aquí.",
            "Guarde sus favoritas, converse con un agente y haga su propuesta.", nameof(Index), ct: ct));

    [HttpGet]
    public async Task<IActionResult> Favorites([Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
        => View("PropertyListing", await ListingAsync(filter, "favorites", "Mis propiedades", "Sus favoritas que siguen disponibles, en un solo lugar.", nameof(Favorites), ct: ct));

    [HttpPost]
    public async Task<IActionResult> SetFavorite(FavoriteInputViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await favorites.SetAsync(model.PropertyId, model.Favorite!.Value, ct);
                TempData["Success"] = model.Favorite.Value ? "La propiedad fue agregada a sus favoritas correctamente." : "La propiedad fue eliminada de sus favoritas correctamente.";
            }
            catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        }
        else TempData["Error"] = "Seleccione una propiedad y un estado de favorito válidos.";
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await DetailsAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> SendMessage(int id, [Bind(Prefix = "Message")] MessageInputViewModel message, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await conversations.SendAsync(id, null, message.Body, ct);
                TempData["Success"] = "Su mensaje fue enviado correctamente.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var model = await DetailsAsync(id, ct);
        model.Message = message;
        return View("Details", model);
    }

    [HttpPost]
    public async Task<IActionResult> SendOffer(int id, [Bind(Prefix = "Offer")] OfferInputViewModel offer, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await offers.CreateAsync(id, offer.Amount!.Value, ct);
                TempData["Success"] = "Su oferta fue enviada correctamente.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var model = await DetailsAsync(id, ct);
        model.Offer = offer;
        return View("Details", model);
    }

    private async Task<PropertyDetailsViewModel> DetailsAsync(int id, CancellationToken ct)
    {
        var property = await Properties.GetAsync(id, "client", ct);
        var ownId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return new()
        {
            Property = Mapper.Map<PropertyViewModel>(property),
            Conversations = Mapper.Map<List<ConversationViewModel>>((await conversations.ListAsync(id, ct)).Where(x => x.ClientId == ownId).ToList()),
            Offers = Mapper.Map<List<OfferViewModel>>((await offers.ListAsync(id, ct: ct)).Where(x => x.ClientId == ownId).OrderByDescending(x => x.CreatedAt).ToList())
        };
    }
}
