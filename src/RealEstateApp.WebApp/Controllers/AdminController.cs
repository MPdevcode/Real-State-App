using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApp.Application;
using RealEstateApp.Domain;
using RealEstateApp.WebApp.ViewModels;

namespace RealEstateApp.WebApp.Controllers;

[Authorize(Roles = Roles.Admin), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IDashboardService dashboard, IAccountService accounts, IMapper mapper,
    ILogger<AdminController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(mapper.Map<DashboardViewModel>(await dashboard.GetAsync(ct)));

    [HttpGet]
    public async Task<IActionResult> Agents(CancellationToken ct)
        => View(new UserListingViewModel { Role = Roles.Agent, Users = mapper.Map<List<UserViewModel>>(await accounts.ListAsync(Roles.Agent, ct: ct)) });

    [HttpGet]
    public async Task<IActionResult> AgentStatus(string id, CancellationToken ct)
    {
        var agent = await AgentAsync(id, ct);
        return View("Confirm", new ConfirmationViewModel
        {
            Title = agent.IsActive ? "Inactivar agente" : "Activar agente",
            Message = agent.IsActive ? "¿Está seguro que desea inactivar este agente?" : "¿Está seguro que desea activar este agente?",
            Consequences = agent.IsActive ? "El agente no podrá iniciar sesión y sus propiedades dejarán de aparecer en los listados públicos." : "El agente podrá iniciar sesión y sus propiedades disponibles volverán a ser públicas.",
            Controller = "Admin", Id = id, Active = !agent.IsActive, PostAction = nameof(AgentStatus), CancelAction = nameof(Agents)
        });
    }

    [HttpPost]
    public async Task<IActionResult> AgentStatus(string id, StatusInputViewModel input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await AgentAsync(id, ct);
                await accounts.ChangeStatusAsync(id, input.Active!.Value, ct);
                TempData["Success"] = input.Active.Value ? "El agente fue activado correctamente." : "El agente fue inactivado correctamente.";
            }
            catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        }
        else TempData["Error"] = "Debe indicar un estado válido.";
        return RedirectToAction(nameof(Agents));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteAgent(string id, CancellationToken ct)
    {
        var agent = await AgentAsync(id, ct);
        return View("Confirm", new ConfirmationViewModel
        {
            Title = "Eliminar agente", Message = "¿Está seguro que desea eliminar este agente y todas sus propiedades?",
            Consequences = $"Se eliminarán {agent.PropertyCount} propiedades, incluidas las vendidas, y sus imágenes, mejoras asociadas, ofertas, conversaciones y favoritos. Esta acción es irreversible.",
            Controller = "Admin", Id = id, PostAction = nameof(DeleteAgent), CancelAction = nameof(Agents), Destructive = true
        });
    }

    [HttpPost, ActionName("DeleteAgent")]
    public async Task<IActionResult> DeleteAgentConfirmed(string id, CancellationToken ct)
    {
        try
        {
            await AgentAsync(id, ct);
            await accounts.DeleteAgentAsync(id, ct);
            TempData["Success"] = "El agente fue eliminado correctamente.";
        }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agent deletion failed");
            TempData["Error"] = "No fue posible eliminar el agente. Intente nuevamente más tarde.";
        }
        return RedirectToAction(nameof(Agents));
    }

    private async Task<UserDto> AgentAsync(string id, CancellationToken ct)
    {
        var agent = await accounts.GetAsync(id, ct);
        if (agent.Role != Roles.Agent) throw new NotFoundException("El agente seleccionado no existe.");
        return agent;
    }
}
