using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[AllowAnonymous]
public sealed class HomeController(IPropertyService properties, ICatalogService catalogs, IAccountService accounts, IMapper mapper)
    : PropertyListingController(properties, catalogs, mapper)
{
    [HttpGet]
    public async Task<IActionResult> Index([Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
        => View("PropertyListing", await ListingAsync(filter, "public", "Un lugar para su próxima etapa.",
            "Propiedades disponibles. Personas que le acompañan. Encuentre su espacio en República Dominicana.", nameof(Index), ct: ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        if (User.IsInRole(Roles.Client)) return RedirectToAction("Details", "Client", new { id });
        return View("PublicDetails", new PropertyDetailsViewModel { Property = Mapper.Map<PropertyViewModel>(await Properties.GetAsync(id, "public", ct)) });
    }

    [HttpGet]
    public async Task<IActionResult> Agents(AgentSearchViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var agents = await accounts.ListAsync(Roles.Agent, onlyActive: true, ct: ct);
            var name = model.Name?.Trim();
            model.Agents = Mapper.Map<List<UserViewModel>>(agents
                .Where(x => string.IsNullOrEmpty(name) || x.FullName.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToList());
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AgentProperties(string id, [Bind(Prefix = "Filter")] PropertyFilterViewModel filter, CancellationToken ct)
    {
        var agent = (await accounts.ListAsync(Roles.Agent, onlyActive: true, ct: ct)).FirstOrDefault(x => x.Id == id)
            ?? throw new NotFoundException("El agente solicitado no existe o no se encuentra disponible.");
        return View("PropertyListing", await ListingAsync(filter, "public", $"Propiedades de {agent.FullName}",
            "Conozca sus propiedades disponibles y consulte todos los detalles.", nameof(AgentProperties), id, ct));
    }

    [HttpGet]
    public IActionResult Start()
    {
        if (User.IsInRole(Roles.Client)) return RedirectToAction("Index", "Client");
        if (User.IsInRole(Roles.Agent)) return RedirectToAction("Index", "Agent");
        if (User.IsInRole(Roles.Admin)) return RedirectToAction("Index", "Admin");
        return RedirectToAction(nameof(Index));
    }

    [IgnoreAntiforgeryToken]
    public IActionResult StatusPage(int code = 500)
    {
        code = code is >= 400 and <= 599 ? code : 500;
        Response.StatusCode = code;
        return View("Error", new ErrorViewModel
        {
            StatusCode = code, RequestId = HttpContext.TraceIdentifier,
            Message = code switch
            {
                404 => "La página o propiedad solicitada no existe o no se encuentra disponible.",
                403 => "No tiene permisos para acceder a esta sección.",
                400 => "La solicitud no es válida o el formulario ha vencido. Regrese e intente nuevamente.",
                _ => "No fue posible procesar la solicitud. Intente nuevamente más tarde."
            }
        });
    }
}
